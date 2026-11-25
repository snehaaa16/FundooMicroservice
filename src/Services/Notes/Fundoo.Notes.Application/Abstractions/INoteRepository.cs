using Fundoo.Notes.Domain.Entities;

namespace Fundoo.Notes.Application.Abstractions;

public interface INoteRepository
{
    Task<Note?> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<List<Note>> ListAsync(Guid userId, NoteView view, CancellationToken ct = default);

    // Production repositories should apply LIMIT/OFFSET in the database. This default
    // implementation keeps simple in-memory repositories compatible.
    async Task<(List<Note> Items, long Total)> ListPageAsync(
        Guid userId, NoteView view, long offset, int pageSize, CancellationToken ct = default)
    {
        var all = await ListAsync(userId, view, ct);
        return (all.Skip((int)Math.Min(offset, all.Count)).Take(pageSize).ToList(), all.Count);
    }

    Task AddAsync(Note note, CancellationToken ct = default);
    Task UpdateAsync(Note note, CancellationToken ct = default);
    Task DeleteAsync(Note note, CancellationToken ct = default);
}
