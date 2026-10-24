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

        public async Task<int?> GetMeetingDurationMinutesAsync()
        {
            var setting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "MeetingDurationMinutes");

            if (setting != null && int.TryParse(setting.Value, out int dur) && dur >= 15 && dur <= 180)
            {
                return dur;
            }

            return null; // Strict: Never silently use a hardcoded duration when missing or invalid
        }

        public async Task<(bool HasConflict, string ErrorMessage)> ValidateMeetingConflictAsync(
            int supervisorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int academicSessionId,
            int? ignoreMeetingId = null)
        {
            if (startTime >= endTime)
            {
                return (true, "Meeting start time must be earlier than end time.");
            }

            // 1. Validate Working Hours from FYPSetting (Strict: No 09:00 - 17:00 fallback)
            var startSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");
            var endSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");

            if (startSetting == null || string.IsNullOrWhiteSpace(startSetting.Value) ||
                !TimeSpan.TryParse(startSetting.Value, out TimeSpan workStart) ||
                endSetting == null || string.IsNullOrWhiteSpace(endSetting.Value) ||
                !TimeSpan.TryParse(endSetting.Value, out TimeSpan workEnd) ||
                workStart >= workEnd)
            {
                return (true, "Lecturer working hours are not configured or invalid in Admin Settings.");
            }

            // 2. Validate against Supervisor Date Overrides
            var overrideAvail = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.SupervisorId == supervisorId && a.ScheduleType == "DateOverride" && a.SpecificDate == meetingDate.Date);

            if (overrideAvail != null && !overrideAvail.IsAvailable)
            {
                return (true, "Supervisor is marked as unavailable on this date per Date Override.");
            }

            TimeSpan allowedStart = (overrideAvail != null && overrideAvail.StartTime > workStart) ? overrideAvail.StartTime : workStart;
            TimeSpan allowedEnd = (overrideAvail != null && overrideAvail.EndTime < workEnd) ? overrideAvail.EndTime : workEnd;

            if (startTime < allowedStart || endTime > allowedEnd)
            {
                return (true, $"Selected time ({startTime:hh\\:mm} - {endTime:hh\\:mm}) is outside the supervisor's working hours ({allowedStart:hh\\:mm} - {allowedEnd:hh\\:mm}).");
            }

            string dayStr = meetingDate.ToString("dddd");

            // 3. Validate against Lecturer Teaching Timetable
            var teachingEntries = await _context.SupervisorTimetableEntries
                .AsNoTracking()
                .Where(t => t.SupervisorId == supervisorId && t.AcademicSessionId == academicSessionId && t.DayOfWeek == dayStr)
                .ToListAsync();

            var teachingConflict = teachingEntries.FirstOrDefault(t => startTime < t.EndTime && endTime > t.StartTime);
            if (teachingConflict != null)
            {
                return (true, $"Selected time conflicts with a teaching commitment ({(string.IsNullOrEmpty(teachingConflict.Details) ? "Class" : teachingConflict.Details)} {teachingConflict.StartTime:hh\\:mm} - {teachingConflict.EndTime:hh\\:mm}).");
            }

            // 4. Validate against Existing Confirmed / Pending Meetings
            var existingMeetings = await _context.MeetingBookings
                .AsNoTracking()
                .Where(m => m.SupervisorId == supervisorId &&
                            m.MeetingDate == meetingDate.Date &&
                            m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired")
                .ToListAsync();

            foreach (var m in existingMeetings)
            {
                if (ignoreMeetingId.HasValue && m.Id == ignoreMeetingId.Value) continue;

                // Priority 1: Stored StartDateTime and EndDateTime (New confirmed meetings)
                if (m.StartDateTime.HasValue && m.EndDateTime.HasValue && m.EndDateTime.Value > m.StartDateTime.Value)
                {
                    TimeSpan mStart = m.StartDateTime.Value.TimeOfDay;
                    TimeSpan mEnd = m.EndDateTime.Value.TimeOfDay;
                    // Interval overlap: newStart < existingEnd && newEnd > existingStart
                    // Adjacent bookings (startTime == mEnd or endTime == mStart) are explicitly allowed
                    if (startTime < mEnd && endTime > mStart)
                    {
                        return (true, $"Selected time conflicts with an existing meeting ({mStart:hh\\:mm} - {mEnd:hh\\:mm}).");
                    }
                }
                // Priority 2: Legacy historical meeting without verified end time
                else if (TimeSpan.TryParse(m.MeetingTime, out TimeSpan legacyStart) ||
                         (DateTime.TryParse(m.MeetingTime, out DateTime parsedMmt) && (legacyStart = parsedMmt.TimeOfDay) == legacyStart))
                {
                    // Conservative protection: Do not assume 30 minutes!
                    // Protect a conservative interval around the legacy meeting to avoid unsafe double-booking
                    TimeSpan conservativeEnd = legacyStart.Add(TimeSpan.FromMinutes(60));
                    if (conservativeEnd > workEnd) conservativeEnd = workEnd;

                    if (startTime < conservativeEnd && endTime > legacyStart)
                    {
                        return (true, $"Selected time conflicts with a historical meeting (Booking #{m.Id} at {m.MeetingTime}) whose duration is unverified. To prevent unsafe double-booking, this slot cannot be booked.");
                    }
                }
                else
                {
                    return (true, $"Supervisor has an unverified legacy meeting booking on this date. Booking blocked conservatively.");
                }
            }

            // 5. Validate against IAF Defense Presentations
            var defenseConflicts = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project)
                .Where(m => m.DefenseDate == meetingDate.Date &&
                            (m.Status == "DefenseScheduled" || m.Status == "DefenseRequired") &&
                            m.Project != null && m.Project.SupervisorId == supervisorId)
                .ToListAsync();

            if (defenseConflicts.Any())
            {
                var iafDurationSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "IAFDefenseDurationMinutes");
                int iafDuration = (iafDurationSetting != null && int.TryParse(iafDurationSetting.Value, out int idur) && idur > 0) ? idur : 45;

                foreach (var d in defenseConflicts)
                {
                    if (DateTime.TryParse(d.DefenseTime, out DateTime dTime))
                    {
                        TimeSpan dStart = dTime.TimeOfDay;
                        TimeSpan dEnd = dStart.Add(TimeSpan.FromMinutes(iafDuration));
                        if (startTime < dEnd && endTime > dStart)
                        {
                            return (true, $"Selected time conflicts with an IAF Defense presentation ({dStart:hh\\:mm} - {dEnd:hh\\:mm}).");
                        }
                    }
                }
            }

            // 6. Validate against Final Viva Presentations
            var vivaConflicts = await _context.FYPProjects
                .AsNoTracking()
                .Include(p => p.AssignedExaminers)
                .Where(p => p.VivaDate.HasValue && p.VivaDate.Value.Date == meetingDate.Date &&
                            (p.SupervisorId == supervisorId || (p.AssignedExaminers != null && p.AssignedExaminers.Any(ae => ae.ExaminerId == supervisorId))))
                .ToListAsync();

            if (vivaConflicts.Any())
            {
                var vivaDurationSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "VivaDurationMinutes");
                if (vivaDurationSetting != null && int.TryParse(vivaDurationSetting.Value, out int vdur) && vdur > 0)
                {
                    foreach (var v in vivaConflicts)
                    {
                        TimeSpan vStart = v.VivaDate!.Value.TimeOfDay;
                        TimeSpan vEnd = vStart.Add(TimeSpan.FromMinutes(vdur));
                        if (startTime < vEnd && endTime > vStart)
                        {
                            return (true, $"Selected time conflicts with a Final Viva presentation ({vStart:hh\\:mm} - {vEnd:hh\\:mm}).");
                        }
                    }
                }
                else
                {
                    return (true, "Supervisor is scheduled for a Final Viva presentation on this date, but Viva Duration is not configured in Admin Settings.");
                }
            }

            return (false, string.Empty);
        }

        public async Task<string?> ValidateMeetingConflictAsync(
            int supervisorId,
            DateTime startDateTime,
            DateTime endDateTime,
            int? excludeMeetingId = null,
            int? sessionId = null)
        {
            int effectiveSessionId = sessionId ?? 0;
            if (effectiveSessionId == 0)
            {
                var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true && s.IsActive);
                effectiveSessionId = activeSession?.Id ?? 0;
            }

            var result = await ValidateMeetingConflictAsync(
                supervisorId,
                startDateTime.Date,
                startDateTime.TimeOfDay,
                endDateTime.TimeOfDay,
                effectiveSessionId,
                excludeMeetingId);

            return result.HasConflict ? result.ErrorMessage : null;
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

            int stage = initialMeeting.Stage ?? 1;
            var session = await _sessionResolver.GetStageSessionAsync(studentId, stage) ?? await _sessionResolver.GetStudentSessionAsync(studentId);
            if (session == null)
            {
                return (0, 0, "No active academic session found for student.");
            }

            int? requiredMeetingsOpt = stage == 1 ? session.RequiredMeetingsStage1 : session.RequiredMeetingsStage2;
            if (!requiredMeetingsOpt.HasValue && stage == 2)
            {
                var originalSession = await _sessionResolver.GetStudentSessionAsync(studentId);
                requiredMeetingsOpt = originalSession?.RequiredMeetingsStage2;
            }
            
            if (!requiredMeetingsOpt.HasValue)
            {
                return (0, 0, $"Required Meetings setting is not configured for Stage {stage}. Please contact Admin.");
            }

            int requiredMeetings = requiredMeetingsOpt.Value;

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

            // Retrieve Admin-Configured Meeting Duration (Strict: Never use hardcoded fallback)
            int? meetingDurationMinutes = await GetMeetingDurationMinutesAsync();
            if (!meetingDurationMinutes.HasValue)
            {
                return (0, 0, "Supervisor meeting duration is not configured in Admin Settings. Schedule generation aborted.");
            }

            int duration = meetingDurationMinutes.Value;

            // Ensure initial meeting has StartDateTime and EndDateTime persisted
            if (!initialMeeting.StartDateTime.HasValue && TimeSpan.TryParse(initialMeeting.MeetingTime, out var initTime))
            {
                initialMeeting.StartDateTime = initialMeeting.MeetingDate.Date.Add(initTime);
                initialMeeting.EndDateTime = initialMeeting.StartDateTime.Value.Add(TimeSpan.FromMinutes(duration));
            }

            var newMeetings = new List<MeetingBooking>();
            int scheduledCount = 0;
            int conflictCount = 0;

            // Mark the initial meeting as week 1 explicitly
            initialMeeting.WeekNumber = 1;

            if (!TimeSpan.TryParse(initialMeeting.MeetingTime, out var reqTime))
            {
                return (0, 0, "Initial meeting has an invalid meeting time string. Cannot generate recurring meetings.");
            }

            var reqEnd = reqTime.Add(TimeSpan.FromMinutes(duration));

            for (int i = 1; i < requiredMeetings; i++)
            {
                var nextDate = initialMeeting.MeetingDate.AddDays(7 * i);

                if (nextDate.Date > cutoffDate.Value.Date)
                {
                    break;
                }

                // Server-side conflict check using interval overlap
                var (hasConflict, conflictReason) = await ValidateMeetingConflictAsync(
                    supervisorId, nextDate, reqTime, reqEnd, session.Id);

                string finalStatus = hasConflict ? "Postponed" : "Approved";
                string? supervisorNote = hasConflict ? $"Needs Reschedule: {conflictReason}" : null;

                if (hasConflict) conflictCount++;
                else scheduledCount++;

                DateTime startDateTime = nextDate.Date.Add(reqTime);
                DateTime endDateTime = nextDate.Date.Add(reqEnd);

                newMeetings.Add(new MeetingBooking
                {
                    StudentId = initialMeeting.StudentId,
                    SupervisorId = initialMeeting.SupervisorId,
                    FYPProjectId = initialMeeting.FYPProjectId,
                    Stage = stage,
                    MeetingDate = nextDate.Date,
                    MeetingDay = nextDate.DayOfWeek.ToString(),
                    MeetingTime = reqTime.ToString(@"hh\:mm"),
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime,
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

            // Maintain the active WeeklyMeetingSchedule entity
            var existingSchedule = await _context.WeeklyMeetingSchedules
                .FirstOrDefaultAsync(s => s.StudentId == studentId && s.AcademicSessionId == session.Id && s.Stage == stage);

            if (existingSchedule == null)
            {
                var newSchedule = new WeeklyMeetingSchedule
                {
                    StudentId = studentId,
                    SupervisorId = supervisorId,
                    FYPProjectId = initialMeeting.FYPProjectId,
                    AcademicSessionId = session.Id,
                    Stage = stage,
                    FirstMeetingBookingId = initialMeeting.Id,
                    FirstMeetingDate = initialMeeting.MeetingDate.Date,
                    LockedDayOfWeek = initialMeeting.MeetingDate.DayOfWeek,
                    LockedTime = reqTime,
                    NextMeetingDate = initialMeeting.MeetingDate.Date.AddDays(7),
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };
                _context.WeeklyMeetingSchedules.Add(newSchedule);
            }
            else
            {
                existingSchedule.SupervisorId = supervisorId;
                existingSchedule.FirstMeetingBookingId = initialMeeting.Id;
                existingSchedule.FirstMeetingDate = initialMeeting.MeetingDate.Date;
                existingSchedule.LockedDayOfWeek = initialMeeting.MeetingDate.DayOfWeek;
                existingSchedule.LockedTime = reqTime;
                existingSchedule.NextMeetingDate = initialMeeting.MeetingDate.Date.AddDays(7);
                existingSchedule.IsActive = true;
            }

            await _context.SaveChangesAsync();

            string message = $"Auto-generated {newMeetings.Count} recurring meetings.";
            if (conflictCount > 0) message += $" ({conflictCount} require rescheduling due to conflicts).";
            if (newMeetings.Count < (requiredMeetings - 1)) message += $" Stopped early at cutoff date.";

            return (scheduledCount, conflictCount, message);
        }

        public async Task<List<OverrideMeetingConflictDto>> DetectOverrideMeetingConflictsAsync(
            int supervisorId,
            DateTime overrideDate,
            TimeSpan overrideStart,
            TimeSpan overrideEnd,
            bool isAvailable,
            int? overrideId = null)
        {
            var conflicts = new List<OverrideMeetingConflictDto>();

            // Only unavailable (Busy) date overrides create conflicts with existing booked meetings
            if (isAvailable)
            {
                return conflicts;
            }

            // Normalise start and end
            TimeSpan busyStart = overrideStart;
            TimeSpan busyEnd = overrideEnd;
            if (busyStart >= busyEnd || (busyStart == TimeSpan.Zero && busyEnd == TimeSpan.Zero))
            {
                busyStart = TimeSpan.Zero;
                busyEnd = TimeSpan.FromHours(24);
            }

            // Query existing Pending and Approved meetings for this supervisor on this date
            var meetings = await _context.MeetingBookings
                .AsNoTracking()
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == supervisorId &&
                            m.MeetingDate == overrideDate.Date &&
                            (m.Status == "Pending" || m.Status == "Approved"))
                .OrderBy(m => m.MeetingTime)
                .ThenBy(m => m.Id)
                .ToListAsync();

            foreach (var m in meetings)
            {
                TimeSpan mStart;
                TimeSpan mEnd;
                bool isLegacy = false;

                if (m.StartDateTime.HasValue && m.EndDateTime.HasValue && m.EndDateTime.Value > m.StartDateTime.Value)
                {
                    mStart = m.StartDateTime.Value.TimeOfDay;
                    mEnd = m.EndDateTime.Value.TimeOfDay;
                }
                else if (TimeSpan.TryParse(m.MeetingTime, out TimeSpan parsedStart) ||
                         (DateTime.TryParse(m.MeetingTime, out DateTime parsedDt) && (parsedStart = parsedDt.TimeOfDay) == parsedStart))
                {
                    mStart = parsedStart;
                    // Legacy meeting without verified end time: treat carefully
                    isLegacy = true;
                    mEnd = mStart.Add(TimeSpan.FromMinutes(60));
                    if (mEnd > TimeSpan.FromHours(24)) mEnd = TimeSpan.FromHours(24);
                }
                else
                {
                    // Fallback if MeetingTime cannot be parsed: treat as full-day overlap
                    mStart = TimeSpan.Zero;
                    mEnd = TimeSpan.FromHours(24);
                    isLegacy = true;
                }

                // Standard interval overlap logic: meetingStart < unavailableEnd && meetingEnd > unavailableStart
                if (mStart < busyEnd && mEnd > busyStart)
                {
                    string conflictReason = busyStart == TimeSpan.Zero && busyEnd >= TimeSpan.FromHours(23)
                        ? $"Supervisor set a full-day Busy override on {overrideDate:dd MMM yyyy}."
                        : $"Supervisor set a Busy override ({busyStart:hh\\:mm} - {busyEnd:hh\\:mm}) on {overrideDate:dd MMM yyyy}.";

                    conflicts.Add(new OverrideMeetingConflictDto
                    {
                        MeetingId = m.Id,
                        StudentId = m.StudentId,
                        StudentName = m.Student?.FullName ?? $"Student #{m.StudentId}",
                        SupervisorId = m.SupervisorId,
                        MeetingDate = m.MeetingDate,
                        MeetingTime = m.MeetingTime ?? $"{mStart:hh\\:mm}",
                        StartTime = mStart,
                        EndTime = mEnd,
                        Status = m.Status,
                        OverrideId = overrideId,
                        ConflictReason = conflictReason,
                        IsLegacyDurationUnverified = isLegacy
                    });
                }
            }

            return conflicts;
        }

        public async Task<List<OverrideMeetingConflictDto>> GetSupervisorActiveOverrideMeetingConflictsAsync(int supervisorId)
        {
            var allConflicts = new List<OverrideMeetingConflictDto>();

            // Find all active/future DateOverrides that are marked as unavailable (IsAvailable == false)
            var overrides = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == supervisorId &&
                            a.ScheduleType == "DateOverride" &&
                            a.SpecificDate.HasValue &&
                            a.SpecificDate.Value >= DateTime.Today &&
                            !a.IsAvailable)
                .OrderBy(a => a.SpecificDate)
                .ToListAsync();

            foreach (var o in overrides)
            {
                var conflicts = await DetectOverrideMeetingConflictsAsync(
                    supervisorId,
                    o.SpecificDate!.Value,
                    o.StartTime,
                    o.EndTime,
                    o.IsAvailable,
                    o.Id);

                allConflicts.AddRange(conflicts);
            }

            // Remove duplicates if multiple overrides overlap the same meeting
            return allConflicts
                .GroupBy(c => c.MeetingId)
                .Select(g => g.First())
                .OrderBy(c => c.MeetingDate)
                .ThenBy(c => c.StartTime)
                .ToList();
        }

        public async Task<List<OverrideMeetingConflictDto>> GetStudentActiveOverrideMeetingConflictsAsync(int studentId)
        {
            var studentConflicts = new List<OverrideMeetingConflictDto>();

            // Find all active or future Pending or Approved meetings for this student
            var studentMeetings = await _context.MeetingBookings
                .AsNoTracking()
                .Include(m => m.Student)
                .Where(m => m.StudentId == studentId &&
                            m.MeetingDate >= DateTime.Today &&
                            (m.Status == "Pending" || m.Status == "Approved"))
                .ToListAsync();

            if (!studentMeetings.Any())
            {
                return studentConflicts;
            }

            var supervisorIds = studentMeetings.Select(m => m.SupervisorId).Distinct().ToList();

            // Find all active/future DateOverrides that are marked as unavailable (IsAvailable == false) for these supervisors
            var overrides = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => supervisorIds.Contains(a.SupervisorId) &&
                            a.ScheduleType == "DateOverride" &&
                            a.SpecificDate.HasValue &&
                            a.SpecificDate.Value >= DateTime.Today &&
                            !a.IsAvailable)
                .ToListAsync();

            foreach (var m in studentMeetings)
            {
                var matchingOverride = overrides.FirstOrDefault(o => o.SupervisorId == m.SupervisorId && o.SpecificDate!.Value.Date == m.MeetingDate.Date);
                if (matchingOverride != null)
                {
                    TimeSpan busyStart = matchingOverride.StartTime;
                    TimeSpan busyEnd = matchingOverride.EndTime;
                    if (busyStart >= busyEnd || (busyStart == TimeSpan.Zero && busyEnd == TimeSpan.Zero))
                    {
                        busyStart = TimeSpan.Zero;
                        busyEnd = TimeSpan.FromHours(24);
                    }

                    TimeSpan mStart;
                    TimeSpan mEnd;
                    bool isLegacy = false;

                    if (m.StartDateTime.HasValue && m.EndDateTime.HasValue && m.EndDateTime.Value > m.StartDateTime.Value)
                    {
                        mStart = m.StartDateTime.Value.TimeOfDay;
                        mEnd = m.EndDateTime.Value.TimeOfDay;
                    }
                    else if (TimeSpan.TryParse(m.MeetingTime, out TimeSpan parsedStart) ||
                             (DateTime.TryParse(m.MeetingTime, out DateTime parsedDt) && (parsedStart = parsedDt.TimeOfDay) == parsedStart))
                    {
                        mStart = parsedStart;
                        isLegacy = true;
                        mEnd = mStart.Add(TimeSpan.FromMinutes(60));
                        if (mEnd > TimeSpan.FromHours(24)) mEnd = TimeSpan.FromHours(24);
                    }
                    else
                    {
                        mStart = TimeSpan.Zero;
                        mEnd = TimeSpan.FromHours(24);
                        isLegacy = true;
                    }

                    // Standard interval overlap: meetingStart < unavailableEnd && meetingEnd > unavailableStart
                    if (mStart < busyEnd && mEnd > busyStart)
                    {
                        string conflictReason = $"Your meeting on {m.MeetingDate:dd MMM yyyy} at {m.MeetingTime} overlaps your supervisor's unavailable period. Your booking has not been cancelled. Please wait for an updated schedule or contact your supervisor.";

                        studentConflicts.Add(new OverrideMeetingConflictDto
                        {
                            MeetingId = m.Id,
                            StudentId = m.StudentId,
                            StudentName = m.Student?.FullName ?? string.Empty,
                            SupervisorId = m.SupervisorId,
                            MeetingDate = m.MeetingDate,
                            MeetingTime = m.MeetingTime ?? $"{mStart:hh\\:mm}",
                            StartTime = mStart,
                            EndTime = mEnd,
                            Status = m.Status,
                            OverrideId = matchingOverride.Id,
                            ConflictReason = conflictReason,
                            IsLegacyDurationUnverified = isLegacy
                        });
                    }
                }
            }

            return studentConflicts.OrderBy(c => c.MeetingDate).ThenBy(c => c.StartTime).ToList();
        }
    }
}
