using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Fundoo.User.Api.Contracts;
using Fundoo.User.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using Asp.Versioning;

namespace Fundoo.User.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/google")]
public sealed class GoogleOAuthController(
    IConfiguration configuration,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    IExternalLoginService externalLogin) : ControllerBase
{
    private const string StateCookieName = "Fundoo.GoogleOAuth.State";
    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(5);
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("Fundoo.User.Api.GoogleOAuth.State.v1");

    private string CallbackUri =>
        configuration["Google:CallbackUri"] ??
        "https://localhost:7000/api/v1/auth/google/callback";

    [HttpGet("start")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Start()
    {
        var clientId = configuration["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google sign-in is not configured.");

        var state = RandomToken(32);
        var nonce = RandomToken(32);
        var verifier = RandomToken(32);
        var protectedState = _protector.Protect(System.Text.Json.JsonSerializer.Serialize(
            new OAuthState(state, nonce, verifier, DateTimeOffset.UtcNow.ToUnixTimeSeconds())));

        Response.Cookies.Append(StateCookieName, protectedState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = FlowLifetime,
            Path = "/api/v1/auth/google/callback"
        });

        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = CallbackUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account"
        };

        return Redirect(QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth", query));
    }

    [HttpGet("callback")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        if (!string.IsNullOrWhiteSpace(error))
        {
            ClearStateCookie();
            return BadRequest(new { message = "Google sign-in was cancelled or denied." });
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            !Request.Cookies.TryGetValue(StateCookieName, out var protectedState))
            return BadRequest(new { message = "Google sign-in state is missing or expired. Start again." });

        OAuthState? flow;
        try
        {
            flow = System.Text.Json.JsonSerializer.Deserialize<OAuthState>(
                _protector.Unprotect(protectedState));
        }
        catch (CryptographicException)
        {
            ClearStateCookie();
            return BadRequest(new { message = "Google sign-in state is invalid. Start again." });
        }
        finally
        {
            ClearStateCookie();
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (flow is null ||
            now - flow.IssuedAtUnixSeconds > (long)FlowLifetime.TotalSeconds ||
            flow.IssuedAtUnixSeconds > now + 60 ||
            !FixedTimeEquals(flow.State, state))
            return BadRequest(new { message = "Google sign-in state did not match. Start again." });

        var clientId = configuration["Google:ClientId"];
        var clientSecret = configuration["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google sign-in credentials are not configured on the server.");

        using var tokenResponse = await httpClientFactory.CreateClient().PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = CallbackUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = flow.CodeVerifier
            }),
            cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
            return Unauthorized(new { message = "Google authorization code could not be exchanged. Start again." });

        var googleTokens = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(googleTokens?.IdToken) ||
            !HasMatchingNonce(googleTokens.IdToken, flow.Nonce))
            return Unauthorized(new { message = "Google identity token is invalid. Start again." });

        var result = await externalLogin.LoginWithGoogleAsync(
            googleTokens.IdToken, cancellationToken);
        if (!result.Succeeded)
            return Unauthorized(new { message = result.Error ?? "Google sign-in failed." });

        return Ok(new TokenResponse(result.AccessToken!));
    }

    private void ClearStateCookie() =>
        Response.Cookies.Delete(StateCookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth/google/callback"
        });

    private static string RandomToken(int byteCount) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static bool HasMatchingNonce(string idToken, string expectedNonce)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            var nonce = jwt.Claims.FirstOrDefault(claim => claim.Type == "nonce")?.Value;
            return nonce is not null && FixedTimeEquals(expectedNonce, nonce);
        }
        catch (Exception exception) when (
            exception is ArgumentException or SecurityTokenException)
        {
            return false;
        }
    }

    private sealed record OAuthState(
        string State,
        string Nonce,
        string CodeVerifier,
        long IssuedAtUnixSeconds);

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("id_token")]
        public string? IdToken { get; init; }
    }
}
