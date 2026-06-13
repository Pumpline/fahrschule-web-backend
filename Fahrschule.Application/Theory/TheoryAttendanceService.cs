using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
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
    /// Ticks one theory topic for several students at once - a shortcut for the
    /// manual progress entry. Nothing about the "attendance" is stored; only the
    /// topic is marked done in each student's theory progress.
    /// </summary>
    Task<TheoryTickResultDto> TickAsync(TickTheoryRequest request, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Theory attendance shortcut ("Theorie-Anwesenheit", KONZEPT Stufe 2). A theory
/// double lesson has many students; instead of opening each student and ticking
/// the topic, the office picks date + topic + the students who were present and
/// ticks them in one go. Deliberately NOT a stored list (owner's decision,
/// 14.06.) - the lasting record is the training progress, not an attendance log.
/// </summary>
public class TheoryAttendanceService(
    FahrschuleDbContext db,
    IStudentProgressService progress,
    IAuditWriter auditWriter) : ITheoryAttendanceService
{
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
            var item = await db.StudentProgressItems.FirstOrDefaultAsync(
                p => p.StudentId == studentId && p.CurriculumItemKey == topic.ItemKey, ct);

            if (item is null || StudentProgressRules.IsCountable(item.RequiredCount))
            {
                result.NotApplicable++;
                continue;
            }
            if (item.IsCompleted)
            {
                result.AlreadyDone++;
                continue;
            }

            item.IsCompleted = true;
            item.CompletedOn = request.DateOn;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            result.Ticked++;

            // Audit (DSGVO): the student's theory progress changed.
            await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Theorie abgehakt",
                "Ausbildungsfortschritt", $"{studentId}/{topic.Title}",
                newValuesJson: JsonSerializer.Serialize(new { Datum = request.DateOn.ToString("dd.MM.yyyy") }),
                cancellationToken: ct);
        }

        return result;
    }

    /// <summary>Current (latest, active) simple theory topics - the ones that can be ticked.</summary>
    private IQueryable<CurriculumItem> CurrentTheoryTopics()
        => db.CurriculumItems.Where(x =>
            x.SupersededAtUtc == null && x.IsActive
            && x.RequiredCount == null && x.Section.StartsWith("Theorie"));
}
