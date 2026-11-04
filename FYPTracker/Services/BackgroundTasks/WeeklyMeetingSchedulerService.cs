using FYPTracker.Models;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Meetings;

namespace FYPTracker.Services.BackgroundTasks
{
    public class WeeklyMeetingSchedulerService
    {
        private readonly FYPTrackerContext _db;
        private readonly IAcademicSessionResolver _sessionResolver;

        public WeeklyMeetingSchedulerService(
            FYPTrackerContext db,
            IAcademicSessionResolver? sessionResolver = null)
        {
            _db = db;
            _sessionResolver = sessionResolver ?? new AcademicSessionResolver(db);
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

            var bookingWindow = await _sessionResolver.GetStageBookingWindowAsync(schedule.StudentId, schedule.Stage);
            if (!bookingWindow.IsValid || !bookingWindow.StartDate.HasValue || !bookingWindow.EndDate.HasValue)
            {
                return new(
                    false,
                    null,
                    bookingWindow.ErrorMessage ?? "Booking window is not properly configured.");
            }

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

            if (nextDate < bookingWindow.StartDate.Value.Date ||
                nextDate > bookingWindow.EndDate.Value.Date)
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