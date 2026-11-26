using Asp.Versioning;
using Fundoo.Notes.Api.Contracts;
using Fundoo.Notes.Application.Abstractions;
using Fundoo.Notes.Application.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notes.Api.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/notes")]
public class NotesController(INoteService notes) : ControllerBase
{
    // userId ONLY from the validated token, never from request body/query/route
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpPost]
    public async Task<IActionResult> Create(CreateNoteRequest r, CancellationToken ct)
    {
        var note = await notes.CreateAsync(UserId, r.Title, r.Content, r.Color, ct);
        return CreatedAtAction(nameof(Get), new { id = note.Id }, note);
    }

    // v1: purana format, seedha list
    [HttpGet]
    [MapToApiVersion("1.0")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await notes.ListAsync(UserId, NoteView.Active, ct));

    // v2: paging ke saath object
    [HttpGet]
    [MapToApiVersion("2.0")]
    public async Task<IActionResult> ListV2(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? title = null,
        [FromQuery] string? content = null,
        [FromQuery] Guid? labelId = null,
        CancellationToken ct = default)
    {
        if (page < 1)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid page.", detail: "The page value must be 1 or greater.");
        if (pageSize < 1 || pageSize > 100)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid page size.", detail: "pageSize must be between 1 and 100.");

        search = NormalizeFilter(search);
        title = NormalizeFilter(title);
        content = NormalizeFilter(content);

        var offset = ((long)page - 1) * pageSize;
        var (items, total) = await notes.ListPageAsync(
            UserId, NoteView.Active, offset, pageSize, search, title, content, labelId, ct);

        var totalPages = total == 0 ? 0 : ((total - 1) / pageSize) + 1;
        var pageIsAvailable = page <= totalPages;
        var message = items.Count > 0
            ? null
            : total == 0
                ? "No notes matched the selected keyword or label."
                : $"Page {page} is outside the available range. Choose a page from 1 to {totalPages}.";

        return Ok(new PagedResponse<NoteDto>(
            items,
            page,
            pageSize,
            total,
            totalPages,
            HasPreviousPage: page > 1 && pageIsAvailable,
            HasNextPage: page < totalPages,
            Message: message));
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [HttpGet("archived")]
    public async Task<IActionResult> Archived(CancellationToken ct) =>
        Ok(await notes.ListAsync(UserId, NoteView.Archived, ct));

    [HttpGet("trash")]
    public async Task<IActionResult> Trash(CancellationToken ct) =>
        Ok(await notes.ListAsync(UserId, NoteView.Trashed, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var note = await notes.GetAsync(UserId, id, ct);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateNoteRequest r, CancellationToken ct)
    {
        var note = await notes.UpdateAsync(UserId, id, r.Title, r.Content, r.Color, r.IsPinned, ct);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        ToResult(await notes.SetArchivedAsync(UserId, id, true, ct));

    [HttpPut("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct) =>
        ToResult(await notes.SetArchivedAsync(UserId, id, false, ct));

    [HttpPut("{id:guid}/trash")]
    public async Task<IActionResult> MoveToTrash(Guid id, CancellationToken ct) =>
        ToResult(await notes.SetTrashedAsync(UserId, id, true, ct));

    [HttpPut("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct) =>
        ToResult(await notes.SetTrashedAsync(UserId, id, false, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        return await notes.DeleteAsync(UserId, id, ct) switch
        {
            DeleteResult.Deleted => NoContent(),
            DeleteResult.NotTrashed => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Only trashed notes can be permanently deleted."),
            _ => NotFound()
        };
    }

    private IActionResult ToResult(NoteDto? note) => note is null ? NotFound() : Ok(note);
}
