using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Contracts.Theory;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Theory;

public interface ITheoryAttendanceService
{
    /// <summary>The theory topics to choose (current catalogue, simple check-off).</summary>
    Task<List<TheoryTopicDto>> GetTopicsAsync(CancellationToken ct = default);

    /// <summary>
    /// Records a theory double lesson for several present students at once - a
    /// shortcut so the office doesn't open each student. For every present
    /// student a real theory lesson covering the chosen topic is recorded, which
    /// ticks the topic AND lists the lesson in the student's hours.
    /// </summary>
    Task<TheoryTickResultDto> TickAsync(TickTheoryRequest request, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Theory attendance ("Theorie-Anwesenheit", KONZEPT Stufe 2). A theory double
/// lesson has many students; instead of opening each one, the office picks date
/// + start time + topic + the present students and records the lesson for all of
/// them in one go. Each becomes a real recorded lesson (the lesson is the source
/// of truth for the progress) - no separate attendance list is stored.
/// </summary>
public class TheoryAttendanceService(
    FahrschuleDbContext db,
    IStudentProgressService progress,
    ILessonService lessons) : ITheoryAttendanceService
{
    /// <summary>A theory lesson is a Doppelstunde = 90 minutes (2 × 45,
    /// FahrSchAusbO). Used as the duration of the recorded attendance lesson.</summary>
    private const int TheoryDoubleLessonMinutes = 90;

    public async Task<List<TheoryTopicDto>> GetTopicsAsync(CancellationToken ct = default)
        => await CurrentTheoryTopics()
            .OrderBy(x => x.Section).ThenBy(x => x.SortOrder).ThenBy(x => x.Title)
            .Select(x => new TheoryTopicDto { Id = x.Id, ItemKey = x.ItemKey, Section = x.Section, Title = x.Title })
            .ToListAsync(ct);

    public async Task<TheoryTickResultDto> TickAsync(TickTheoryRequest request, Actor actor, CancellationToken ct = default)
    {
        var topic = await CurrentTheoryTopics().FirstOrDefaultAsync(x => x.Id == request.CurriculumItemId, ct)
            ?? throw new AppValidationException("Bitte ein gültiges Theorie-Thema wählen.");

        var studentIds = request.StudentIds.Distinct().ToList();
        if (studentIds.Count == 0)
        {
            throw new AppValidationException("Bitte mindestens einen Schüler wählen.");
        }

        var result = new TheoryTickResultDto();
        foreach (var studentId in studentIds)
        {
            if (!await db.Students.AnyAsync(s => s.Id == studentId, ct))
            {
                continue; // unknown/hidden student - just skip it
            }

            // Make sure the student's theory checklist exists, then find the topic.
            await progress.GetForStudentAsync(studentId, ct);
            var item = await db.StudentProgressItems
                .Include(p => p.Classes)
                .FirstOrDefaultAsync(p => p.StudentId == studentId && p.CurriculumItemKey == topic.ItemKey, ct);

            if (item is null || StudentProgressRules.IsCountable(item.RequiredCount))
            {
                result.NotApplicable++;
                continue;
            }
            if (item.IsCompleted)
            {
                result.AlreadyDone++; // already covered (lesson or manual) - don't double-record
                continue;
            }

            // Record a real theory lesson covering this topic. CreateAsync ticks
            // the simple point via the coverage and audits the lesson.
            var scope = item.Classes.Count == 1 ? item.Classes[0].LicenseClassId : (Guid?)null;
            await lessons.CreateAsync(studentId, new CreateLessonRequest
            {
                Type = nameof(LessonType.Theory),
                LicenseClassId = scope,
                DateOn = request.DateOn,
                StartTime = request.StartTime,
                DurationMinutes = TheoryDoubleLessonMinutes,
                CoveredItemIds = [item.Id],
            }, actor, ct);
            result.Ticked++;
        }

        return result;
    }

    /// <summary>Current (latest, active) simple theory topics - the ones that can be ticked.</summary>
    private IQueryable<CurriculumItem> CurrentTheoryTopics()
        => db.CurriculumItems.Where(x =>
            x.SupersededAtUtc == null && x.IsActive
            && x.RequiredCount == null && x.Section.StartsWith("Theorie"));
}
