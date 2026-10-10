using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;

namespace FYPTracker.Services.Meetings
{
    public class WeeklyMeetingSchedulingService : IWeeklyMeetingSchedulingService
    {
        private readonly FYPTrackerContext _context;
        private readonly IAcademicSessionResolver _sessionResolver;

        public WeeklyMeetingSchedulingService(FYPTrackerContext context, IAcademicSessionResolver sessionResolver)
        {
            _context = context;
            _sessionResolver = sessionResolver;
        }

        public async Task<(int ScheduledCount, int ConflictCount, string Message)> GenerateWeeklyScheduleAsync(int studentId, int supervisorId, int meetingId)
        {
            var initialMeeting = await _context.MeetingBookings.FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == studentId && m.SupervisorId == supervisorId);
            
            if (initialMeeting == null || initialMeeting.Status != "Approved")
            {
                return (0, 0, "Meeting is not valid or not approved.");
            }

            // If this meeting is already a child, do not spawn a new series
            if (initialMeeting.ParentMeetingBookingId != null)
            {
                return (0, 0, string.Empty);
            }

            var session = await _sessionResolver.GetStudentSessionAsync(studentId);
            if (session == null)
            {
                return (0, 0, "No active academic session found for student.");
            }

            int stage = initialMeeting.Stage ?? 1;
            int requiredMeetings = stage == 1 ? (session.RequiredMeetingsStage1 ?? 14) : (session.RequiredMeetingsStage2 ?? 14);

            if (requiredMeetings <= 1)
            {
                return (0, 0, "No additional weekly meetings required.");
            }

            // Enforce strict uniqueness using the Parent ID
            bool alreadyGenerated = await _context.MeetingBookings
                .AnyAsync(m => m.ParentMeetingBookingId == initialMeeting.Id);

            if (alreadyGenerated)
            {
                return (0, 0, "A recurring schedule was already generated for this meeting.");
            }

            var cutoffDate = await _sessionResolver.GetMeetingBookingCutoffAsync(studentId, stage);
            if (!cutoffDate.HasValue)
            {
                return (0, 0, "Could not determine booking cutoff date. Schedule generation aborted.");
            }

            var newMeetings = new List<MeetingBooking>();
            int scheduledCount = 0;
            int conflictCount = 0;

            // Mark the initial meeting as week 1 explicitly
            initialMeeting.WeekNumber = 1;

            var availabilities = await _context.SupervisorAvailabilities
                .Where(a => a.SupervisorId == supervisorId)
                .ToListAsync();
                
            var existingTimetableEntries = await _context.SupervisorTimetableEntries
                .Where(t => t.SupervisorId == supervisorId && t.AcademicSessionId == session.Id)
                .ToListAsync();
            bool hasTimetable = existingTimetableEntries.Any();

            for (int i = 1; i < requiredMeetings; i++)
            {
                var nextDate = initialMeeting.MeetingDate.AddDays(7 * i);

                if (nextDate.Date > cutoffDate.Value.Date)
                {
                    break;
                }

                var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == nextDate.Date);
                bool isUnavailable = !hasTimetable || (overrideAvail != null && !overrideAvail.IsAvailable);

                TimeSpan windowStart = overrideAvail != null ? overrideAvail.StartTime : TimeSpan.FromHours(9);
                TimeSpan windowEnd = overrideAvail != null ? overrideAvail.EndTime : TimeSpan.FromHours(17);

                bool timeValid = TimeSpan.TryParse(initialMeeting.MeetingTime, out var reqTime) && reqTime >= windowStart && reqTime < windowEnd;

                bool isBusy = false;
                if (timeValid && !isUnavailable)
                {
                    var reqEnd = reqTime.Add(TimeSpan.FromMinutes(30));
                    var dayStr = nextDate.DayOfWeek.ToString();
                    
                    isBusy = existingTimetableEntries.Any(b => 
                        b.DayOfWeek == dayStr &&
                        ((reqTime >= b.StartTime && reqTime < b.EndTime) || 
                         (reqEnd > b.StartTime && reqEnd <= b.EndTime) ||
                         (reqTime <= b.StartTime && reqEnd >= b.EndTime))
                    );
                }

                bool timeConflict = await _context.MeetingBookings.AnyAsync(m => 
                    m.SupervisorId == supervisorId && 
                    m.MeetingDate.Date == nextDate.Date && 
                    m.MeetingTime == initialMeeting.MeetingTime && 
                    m.Status != "Rejected" && m.Status != "Cancelled");

                bool hasConflict = isUnavailable || !timeValid || isBusy || timeConflict;
                
                string finalStatus = hasConflict ? "Postponed" : "Approved";
                string supervisorNote = hasConflict ? "Needs Reschedule (Timetable or Availability Conflict)" : null;

                if (hasConflict) conflictCount++;
                else scheduledCount++;

                newMeetings.Add(new MeetingBooking
                {
                    StudentId = initialMeeting.StudentId,
                    SupervisorId = initialMeeting.SupervisorId,
                    FYPProjectId = initialMeeting.FYPProjectId,
                    Stage = stage,
                    MeetingDate = nextDate,
                    MeetingDay = nextDate.DayOfWeek.ToString(),
                    MeetingTime = initialMeeting.MeetingTime,
                    WeekNumber = i + 1,
                    ParentMeetingBookingId = initialMeeting.Id,
                    Reason = "Auto-generated weekly recurring meeting.",
                    SupervisorNote = supervisorNote,
                    Status = finalStatus,
                    Location = initialMeeting.Location,
                    AcademicSessionId = session.Id,
                    CreatedDate = DateTime.Now,
                    RespondedDate = DateTime.Now
                });
            }

            if (newMeetings.Any())
            {
                _context.MeetingBookings.AddRange(newMeetings);
            }

            await _context.SaveChangesAsync();

            string message = $"Auto-generated {newMeetings.Count} recurring meetings.";
            if (conflictCount > 0) message += $" ({conflictCount} require rescheduling due to conflicts).";
            if (newMeetings.Count < (requiredMeetings - 1)) message += $" Stopped early at cutoff date.";

            return (scheduledCount, conflictCount, message);
        }
    }
}
