using FYPTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Services.BackgroundTasks
{
    public class WeeklyMeetingSchedulerService
    {
        private readonly FYPTrackerContext _db;

        public WeeklyMeetingSchedulerService(
            FYPTrackerContext db)
        {
            _db = db;
        }

        public async Task<WeeklyMeetingCheckResult> CheckNextAsync(
            int scheduleId,
            CancellationToken cancellationToken = default)
        {
            var schedule = await _db.WeeklyMeetingSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.Id == scheduleId,
                    cancellationToken);

            if (schedule == null)
                return new(false, null, "Schedule not found.");

            if (!schedule.IsActive)
                return new(false, null, "Schedule is inactive.");

            if (schedule.Stage != 1 && schedule.Stage != 2)
                return new(false, null, "Invalid stage.");

            var session = await _db.AcademicSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.Id == schedule.AcademicSessionId,
                    cancellationToken);

            if (session?.StartDate == null ||
                session.EndDate == null)
            {
                return new(
                    false,
                    null,
                    "Academic Session is missing.");
            }

            string milestoneCode = schedule.Stage == 1
                ? "STAGE1_FINAL"
                : "STAGE2_FINAL";

            DateTime? dueDate =
                await _db.DefaultMilestoneTemplates
                    .AsNoTracking()
                    .Where(m =>
                        m.AcademicSessionId == schedule.AcademicSessionId &&
                        m.Stage == schedule.Stage &&
                        m.MilestoneCode == milestoneCode &&
                        m.IsActive)
                    .Select(m => m.DueDate)
                    .FirstOrDefaultAsync(cancellationToken);

            if (!dueDate.HasValue)
            {
                return new(
                    false,
                    null,
                    "Final milestone deadline is not configured.");
            }

            var cutoff = dueDate.Value.Date.AddDays(-7);

            if (cutoff > session.EndDate.Value.Date)
                cutoff = session.EndDate.Value.Date;

            var nextDate = schedule.NextMeetingDate.Date;

            // Never automatically create meetings in the past.
            while (nextDate < DateTime.Today)
            {
                nextDate = nextDate.AddDays(7);
            }

            // Preserve locked weekday.
            if (nextDate.DayOfWeek != schedule.LockedDayOfWeek)
            {
                return new(
                    false,
                    nextDate,
                    "Next meeting date does not match locked weekday.");
            }

            if (nextDate < session.StartDate.Value.Date ||
                nextDate > cutoff)
            {
                return new(
                    false,
                    nextDate,
                    "Next meeting is outside the allowed period.");
            }

            return new(
                true,
                nextDate,
                "Next weekly meeting date is valid.");
        }
    }

    public record WeeklyMeetingCheckResult(
        bool IsValid,
        DateTime? NextDate,
        string Message);
}