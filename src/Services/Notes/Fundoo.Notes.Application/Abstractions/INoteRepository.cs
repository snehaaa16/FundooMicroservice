using Fundoo.Notes.Domain.Entities;

namespace Fundoo.Notes.Application.Abstractions;

public interface INoteRepository
{
    Task<Note?> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<List<Note>> ListAsync(Guid userId, NoteView view, CancellationToken ct = default);

    // Production repositories should apply LIMIT/OFFSET in the database. This default
    // implementation keeps simple in-memory repositories compatible.
    async Task<(List<Note> Items, long Total)> ListPageAsync(
        Guid userId, NoteView view, long offset, int pageSize,
        string? search, string? title, string? content, Guid? labelId,
        CancellationToken ct = default)
    {
        IEnumerable<Note> filtered = await ListAsync(userId, view, ct);
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(n => n.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                                           || n.Content.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(title))
            filtered = filtered.Where(n => n.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(content))
            filtered = filtered.Where(n => n.Content.Contains(content, StringComparison.OrdinalIgnoreCase));
        if (labelId.HasValue)
            filtered = filtered.Where(n => n.NoteLabels.Any(nl => nl.LabelId == labelId.Value));

        var matching = filtered.ToList();
        var safeOffset = (int)Math.Clamp(offset, 0L, matching.Count);
        return (matching.Skip(safeOffset).Take(pageSize).ToList(), matching.Count);
    }

    Task AddAsync(Note note, CancellationToken ct = default);
    Task UpdateAsync(Note note, CancellationToken ct = default);
    Task DeleteAsync(Note note, CancellationToken ct = default);
}
