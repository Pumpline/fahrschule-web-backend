using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

/// <summary>
/// Shared rules that couple the training progress to recorded lessons (the new
/// model): a SIMPLE point is "done" when at least one non-deleted lesson covers
/// it OR it was completed manually (the exception path - Anrechnung/Übernahme or
/// theory attendance). Centralised so the lesson service, the manual tick and
/// the theory attendance all compute completion the same way.
///
/// Countable points are NOT handled here - their "done" is derived live from the
/// counted sessions (StudentProgressEntry), which the soft-delete query filter
/// already drops when a backing lesson is deleted.
/// </summary>
internal static class ProgressCoupling
{
    /// <summary>Is this point covered by at least one non-deleted lesson?</summary>
    public static Task<bool> IsCoveredByLessonAsync(FahrschuleDbContext db, Guid itemId, CancellationToken ct)
        => db.Set<LessonItem>().AnyAsync(
            li => li.StudentProgressItemId == itemId && !li.Lesson!.IsDeleted, ct);

    /// <summary>
    /// Recomputes the stored <see cref="StudentProgressItem.IsCompleted"/> /
    /// CompletedOn of the given SIMPLE points from their current lesson coverage
    /// plus the manual flag. Call after any change to a covering lesson. The
    /// completion date is the earliest covering lesson's date (a manual date is
    /// kept when there is no covering lesson). Non-simple ids are ignored.
    /// Does NOT save - the caller decides when to persist.
    /// </summary>
    public static async Task RecomputeSimpleAsync(
        FahrschuleDbContext db, IReadOnlyCollection<Guid> itemIds, DateTime now, CancellationToken ct)
    {
        if (itemIds.Count == 0) return;

        var items = await db.StudentProgressItems
            .Where(p => itemIds.Contains(p.Id) && p.RequiredCount == null) // simple = no counter
            .ToListAsync(ct);
        if (items.Count == 0) return;

        var ids = items.Select(p => p.Id).ToList();
        // Earliest covering (non-deleted) lesson date per item.
        var coverage = await db.Set<LessonItem>()
            .Where(li => ids.Contains(li.StudentProgressItemId) && !li.Lesson!.IsDeleted)
            .Select(li => new { li.StudentProgressItemId, li.Lesson!.DateOn })
            .ToListAsync(ct);
        var firstCovered = coverage
            .GroupBy(c => c.StudentProgressItemId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.DateOn));

        foreach (var p in items)
        {
            var coveredOn = firstCovered.TryGetValue(p.Id, out var d) ? (DateOnly?)d : null;
            var done = p.ManuallyCompleted || coveredOn is not null;
            var completedOn = !done ? null : coveredOn ?? p.CompletedOn;

            if (p.IsCompleted == done && p.CompletedOn == completedOn) continue;
            p.IsCompleted = done;
            p.CompletedOn = completedOn;
            p.UpdatedAtUtc = now;
        }
    }
}
