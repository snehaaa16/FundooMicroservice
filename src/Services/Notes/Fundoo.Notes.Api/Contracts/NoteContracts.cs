namespace Fundoo.Notes.Api.Contracts;

public class CreateNoteRequest
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Color { get; set; }
}

public class UpdateNoteRequest
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Color { get; set; }
    public bool IsPinned { get; set; }
}

// v2 paginated list metadata also explains empty and out-of-range pages.
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long Total,
    long TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    string? Message);
