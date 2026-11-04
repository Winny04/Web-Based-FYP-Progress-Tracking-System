using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FYPTracker.Models;
using FYPTracker.Services.Defense;

namespace FYPTracker.Services
{
    public interface ILecturerAvailabilityService
    {
        Task<List<TimeSpanRange>> GetAvailableSlotsAsync(
            int lecturerId,
            DateTime targetDate,
            int durationMinutes,
            int academicSessionId);

        Task<List<TimeSpanRange>> GetMutualAvailableSlotsAsync(
            List<int> lecturerIds,
            DateTime targetDate,
            int durationMinutes,
            int academicSessionId);

        Task<bool> IsSlotAvailableAsync(
            int lecturerId,
            DateTime targetDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int academicSessionId,
            int? excludeBookingId = null,
            int? excludeMilestoneId = null);
    }

    public class TimeSpanRange
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }

    public class LecturerAvailabilityService : ILecturerAvailabilityService
    {
        private readonly FYPTrackerContext _context;

        public LecturerAvailabilityService(FYPTrackerContext context)
        {
            _context = context;
        }

        public async Task<List<TimeSpanRange>> GetMutualAvailableSlotsAsync(
            List<int> lecturerIds,
            DateTime targetDate,
            int durationMinutes,
            int academicSessionId)
        {
            if (lecturerIds == null || !lecturerIds.Any())
                return new List<TimeSpanRange>();

            var distinctIds = lecturerIds.Distinct().ToList();

            // 1. Get continuous free availability intervals for the first lecturer
            var mutualIntervals = await GetLecturerContinuousFreeIntervalsAsync(
                distinctIds.First(),
                targetDate,
                academicSessionId);

            if (!mutualIntervals.Any())
                return new List<TimeSpanRange>();

            // 2. Intersect continuous free intervals with subsequent lecturers
            foreach (var id in distinctIds.Skip(1))
            {
                var nextIntervals = await GetLecturerContinuousFreeIntervalsAsync(
                    id,
                    targetDate,
                    academicSessionId);

                mutualIntervals = IntersectContinuousIntervals(mutualIntervals, nextIntervals);
                if (!mutualIntervals.Any())
                    return new List<TimeSpanRange>();
            }

            // 3. Slice the resulting mutual continuous intervals into discrete slots
            return SliceIntoDiscreteSlots(mutualIntervals, durationMinutes);
        }

        private List<TimeSpanRange> IntersectContinuousIntervals(List<TimeSpanRange> list1, List<TimeSpanRange> list2)
        {
            var result = new List<TimeSpanRange>();
            foreach (var s1 in list1)
            {
                foreach (var s2 in list2)
                {
                    var start = s1.StartTime > s2.StartTime ? s1.StartTime : s2.StartTime;
                    var end = s1.EndTime < s2.EndTime ? s1.EndTime : s2.EndTime;

                    if (end > start)
                    {
                        result.Add(new TimeSpanRange { StartTime = start, EndTime = end });
                    }
                }
            }
            return MergeContiguousOrOverlappingIntervals(result);
        }

        public async Task<List<TimeSpanRange>> GetAvailableSlotsAsync(int lecturerId, DateTime targetDate, int durationMinutes, int academicSessionId)
        {
            var continuousIntervals = await GetLecturerContinuousFreeIntervalsAsync(
                lecturerId,
                targetDate,
                academicSessionId);

            return SliceIntoDiscreteSlots(continuousIntervals, durationMinutes);
        }

        public async Task<bool> IsSlotAvailableAsync(
            int lecturerId,
            DateTime targetDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int academicSessionId,
            int? excludeBookingId = null,
            int? excludeMilestoneId = null)
        {
            // 1. Validate inputs
            if (lecturerId <= 0 || academicSessionId <= 0)
                return false;

            // 2. Reject invalid or zero-length time intervals
            if (startTime >= endTime)
                return false;

            // 3. Reject intervals crossing the calendar-day boundary
            if (startTime < TimeSpan.Zero || endTime > TimeSpan.FromHours(24))
                return false;

            // 4. Retrieve continuous free intervals via B1 engine (with exact booking / milestone self-exclusion support)
            var continuousIntervals = await GetLecturerContinuousFreeIntervalsAsync(
                lecturerId,
                targetDate,
                academicSessionId,
                excludeBookingId: excludeBookingId,
                excludeMilestoneId: excludeMilestoneId);

            // 5. Check if requested interval is fully contained within continuous free intervals
            return IsIntervalFullyContained(continuousIntervals, startTime, endTime);
        }

        public static bool IsIntervalFullyContained(
            List<TimeSpanRange>? continuousIntervals,
            TimeSpan startTime,
            TimeSpan endTime)
        {
            if (continuousIntervals == null || !continuousIntervals.Any())
                return false;

            if (startTime >= endTime || startTime < TimeSpan.Zero || endTime > TimeSpan.FromHours(24))
                return false;

            return continuousIntervals.Any(range => range.StartTime <= startTime && range.EndTime >= endTime);
        }

        private async Task<List<TimeSpanRange>> GetLecturerContinuousFreeIntervalsAsync(
            int lecturerId,
            DateTime targetDate,
            int academicSessionId,
            int? excludeBookingId = null,
            int? excludeMilestoneId = null)
        {
            // 1. Resolve Lecturer
            var lecturer = await _context.ApplicationUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == lecturerId && !u.IsResign);
            if (lecturer == null) return new List<TimeSpanRange>();

            // Academic Session is supplied by the calling workflow.
            if (academicSessionId <= 0)
                return new List<TimeSpanRange>();

            // 2. Resolve Working Hours from FYPSettings
            // Require valid LecturerWorkingHoursStart and LecturerWorkingHoursEnd values in FYPSetting.
            // Automatic 09:00 - 17:00 fallback is removed per strict academic policy.
            // If missing or invalid, return zero bookable slots (fail-closed).
            var startSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");
            var endSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");

            if (!TryParseWorkingHours(startSetting?.Value, endSetting?.Value, out TimeSpan workStart, out TimeSpan workEnd))
            {
                return new List<TimeSpanRange>();
            }

            // Invariant DayOfWeek name (e.g. "Monday", "Tuesday")
            string dayOfWeek = targetDate.DayOfWeek.ToString();

            // 3. Hybrid Fail-Closed Availability Ingestion
            // Free slots start at ZERO. We do NOT assume working hours are free time.
            var rawConfirmedPositive = new List<TimeSpanRange>();

            // A. Confirmed Recurring Weekly Availability (WeeklyDefault, IsAvailable == true)
            var weeklyPositive = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId &&
                            a.ScheduleType == "WeeklyDefault" &&
                            a.DayOfWeek == dayOfWeek &&
                            a.IsAvailable)
                .ToListAsync();

            foreach (var w in weeklyPositive)
            {
                if (w.EndTime > w.StartTime)
                {
                    // Clamp to configured working hours
                    var s = w.StartTime < workStart ? workStart : w.StartTime;
                    var e = w.EndTime > workEnd ? workEnd : w.EndTime;
                    if (e > s)
                    {
                        rawConfirmedPositive.Add(new TimeSpanRange { StartTime = s, EndTime = e });
                    }
                }
            }

            // B. Confirmed Positive DateOverride (one-off available window on specific date)
            var datePositive = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId &&
                            a.ScheduleType == "DateOverride" &&
                            a.SpecificDate == targetDate.Date &&
                            a.IsAvailable)
                .ToListAsync();

            foreach (var d in datePositive)
            {
                if (d.EndTime > d.StartTime)
                {
                    // Clamp to configured working hours
                    var s = d.StartTime < workStart ? workStart : d.StartTime;
                    var e = d.EndTime > workEnd ? workEnd : d.EndTime;
                    if (e > s)
                    {
                        rawConfirmedPositive.Add(new TimeSpanRange { StartTime = s, EndTime = e });
                    }
                }
            }

            // FAIL-CLOSED GATE:
            // If there is no confirmed weekly availability and no confirmed positive date override,
            // the lecturer has zero available time on this day.
            if (!rawConfirmedPositive.Any())
            {
                return new List<TimeSpanRange>();
            }

            // Merge overlapping, duplicate, or adjacent positive intervals
            var freeSlots = MergeContiguousOrOverlappingIntervals(rawConfirmedPositive);

            // 4. Subtract Negative Availability Overrides (Strict Precedence)
            // A. Negative WeeklyDefault (recurring unavailable blocks, e.g. 13:00 - 14:00)
            var weeklyNegative = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId &&
                            a.ScheduleType == "WeeklyDefault" &&
                            a.DayOfWeek == dayOfWeek &&
                            !a.IsAvailable)
                .ToListAsync();

            foreach (var wn in weeklyNegative)
            {
                if (wn.EndTime > wn.StartTime)
                {
                    freeSlots = RemoveOverlap(freeSlots, wn.StartTime, wn.EndTime);
                }
            }

            // B. Negative DateOverride (date-specific unavailable, busy, or full-day leave)
            var dateNegative = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId &&
                            a.ScheduleType == "DateOverride" &&
                            a.SpecificDate == targetDate.Date &&
                            !a.IsAvailable)
                .ToListAsync();

            foreach (var dn in dateNegative)
            {
                // Full-day block convention: 00:00 to 00:00 or StartTime >= EndTime blocks entire working window
                TimeSpan bStart = (dn.StartTime == TimeSpan.Zero && dn.EndTime == TimeSpan.Zero) || (dn.StartTime >= dn.EndTime)
                    ? workStart
                    : dn.StartTime;
                TimeSpan bEnd = (dn.StartTime == TimeSpan.Zero && dn.EndTime == TimeSpan.Zero) || (dn.StartTime >= dn.EndTime)
                    ? workEnd
                    : dn.EndTime;

                freeSlots = RemoveOverlap(freeSlots, bStart, bEnd);
            }

            if (!freeSlots.Any())
            {
                return new List<TimeSpanRange>();
            }

            // 5. Exclude Teaching Commitments (Strict: newStart < existingEnd && newEnd > existingStart)
            var teaching = await _context.SupervisorTimetableEntries
                .AsNoTracking()
                .Where(t => t.SupervisorId == lecturerId &&
                            t.AcademicSessionId == academicSessionId &&
                            t.DayOfWeek == dayOfWeek)
                .ToListAsync();

            foreach (var t in teaching)
            {
                if (t.EndTime > t.StartTime)
                {
                    freeSlots = RemoveOverlap(freeSlots, t.StartTime, t.EndTime);
                }
            }

            if (!freeSlots.Any())
            {
                return new List<TimeSpanRange>();
            }

            // 6. Exclude Student Meetings (Approved or Pending)
            var meetings = await _context.MeetingBookings
                .AsNoTracking()
                .Where(m => m.SupervisorId == lecturerId &&
                            m.MeetingDate == targetDate.Date &&
                            (m.Status == "Approved" || m.Status == "Pending"))
                .ToListAsync();

            foreach (var m in meetings)
            {
                TimeSpan mStart;
                TimeSpan mEnd;

                // Priority 1: Stored StartDateTime and EndDateTime
                if (m.StartDateTime.HasValue && m.EndDateTime.HasValue && m.EndDateTime.Value > m.StartDateTime.Value)
                {
                    mStart = m.StartDateTime.Value.TimeOfDay;
                    mEnd = m.EndDateTime.Value.TimeOfDay;
                    freeSlots = RemoveOverlap(freeSlots, mStart, mEnd);
                }
                // Priority 2: Explicit duration established on record
                else if (m.StartDateTime.HasValue && m.EndDateTime.HasValue)
                {
                    mStart = m.StartDateTime.Value.TimeOfDay;
                    mEnd = m.EndDateTime.Value.TimeOfDay;
                    freeSlots = RemoveOverlap(freeSlots, mStart, mEnd);
                }
                // Priority 3: Historical meeting with legacy MeetingTime string but unverified duration
                // Conservative conflict protection (60 minutes) so slots never overlap.
                else if (!string.IsNullOrWhiteSpace(m.MeetingTime) &&
                         (TimeSpan.TryParse(m.MeetingTime, out TimeSpan parsedTime) || 
                          (DateTime.TryParse(m.MeetingTime, out DateTime parsedMmt) && (parsedTime = parsedMmt.TimeOfDay) == parsedTime)))
                {
                    mStart = parsedTime;
                    int conservativeMinutes = 60;
                    mEnd = mStart.Add(TimeSpan.FromMinutes(conservativeMinutes));
                    if (mEnd > workEnd) mEnd = workEnd;
                    freeSlots = RemoveOverlap(freeSlots, mStart, mEnd);
                }
                else
                {
                    // Real start/end time cannot be determined at all. Handle conflict conservatively
                    // across the configured working hours so no unsafe slots are offered.
                    freeSlots = RemoveOverlap(freeSlots, workStart, workEnd);
                    break;
                }
            }

            if (!freeSlots.Any())
            {
                return new List<TimeSpanRange>();
            }

            // 7. Exclude Existing IAF Defenses (Scheduled or DefenseRequired with dates)
            bool isIafTablePresent = false;
            if (_context.Database.IsRelational())
            {
                try
                {
                    var conn = _context.Database.GetDbConnection();
                    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    
                    var currentTx = _context.Database.CurrentTransaction;
                    if (currentTx != null)
                    {
                        cmd.Transaction = currentTx.GetDbTransaction();
                    }

                    cmd.CommandText = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IAFDefenseBooking';";
                    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    isIafTablePresent = count > 0;
                }
                catch
                {
                    // Fail closed: do not silently assume zero conflicts if database query fails.
                    return new List<TimeSpanRange>();
                }
            }
            else
            {
                isIafTablePresent = true;
            }

            if (isIafTablePresent)
            {
                var iafBookings = await _context.IAFDefenseBookings
                    .AsNoTracking()
                    .Where(b => b.DefenseDate == targetDate.Date &&
                                (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled") &&
                                (b.ExaminerId == lecturerId || (b.IsSupervisorRequired && b.OriginalSupervisorId == lecturerId)))
                    .ToListAsync();

                foreach (var b in iafBookings)
                {
                    // Exclude ONLY the verified active booking being rescheduled
                    if (excludeBookingId.HasValue && excludeBookingId.Value > 0)
                    {
                        if (b.Id == excludeBookingId.Value)
                        {
                            continue;
                        }
                    }
                    else if (excludeMilestoneId.HasValue && excludeMilestoneId.Value > 0)
                    {
                        if (b.MilestoneId == excludeMilestoneId.Value)
                        {
                            continue;
                        }
                    }

                    freeSlots = RemoveOverlap(freeSlots, b.StartTime, b.EndTime);
                }
            }

            // Fallback for legacy defense sessions recorded on FYPMilestone
            var legacyDefenses = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project!).ThenInclude(p => p.AssignedExaminers)
                .Where(m => m.DefenseDate == targetDate.Date && 
                            (m.Status == "DefenseScheduled" || m.Status == "DefenseRequired"))
                .ToListAsync();

            IAFDefenseBooking? excludedBooking = null;
            if (excludeBookingId.HasValue && excludeBookingId.Value > 0)
            {
                try
                {
                    excludedBooking = await _context.IAFDefenseBookings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == excludeBookingId.Value);
                }
                catch
                {
                    // Fail closed if database query encounters errors
                }
            }

            foreach (var d in legacyDefenses)
            {
                // A legacy record is suppressed ONLY if safely established that it represents
                // the exact same verified active booking and attempt being rescheduled.
                bool shouldSuppress = false;
                if (excludedBooking != null)
                {
                    shouldSuppress = ShouldSuppressLegacyDefense(
                        d.Id,
                        d.DefenseDate,
                        d.DefenseTime,
                        d.Remarks,
                        excludedBooking.MilestoneId,
                        excludedBooking.AttemptNumber,
                        excludedBooking.DefenseDate,
                        excludedBooking.StartTime);
                }

                if (shouldSuppress)
                {
                    continue;
                }

                bool isParticipant = (d.Project?.SupervisorId == lecturerId) || 
                                     (d.Project?.AssignedExaminers != null && d.Project.AssignedExaminers.Any(ae => ae.ExaminerId == lecturerId));

                if (isParticipant)
                {
                    if (DateTime.TryParse(d.DefenseTime, out DateTime ddt))
                    {
                        TimeSpan dt = ddt.TimeOfDay;
                        freeSlots = RemoveOverlap(freeSlots, dt, dt.Add(TimeSpan.FromMinutes(45)));
                    }
                    else
                    {
                        // Defense time unparseable or uncertain: fail closed conservatively across working hours
                        freeSlots = RemoveOverlap(freeSlots, workStart, workEnd);
                        break;
                    }
                }
            }

            if (!freeSlots.Any())
            {
                return new List<TimeSpanRange>();
            }

            // 8. Exclude Final Viva Presentations if VivaDate is set for this lecturer
            var vivaDurationSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "VivaDurationMinutes");
            int? authoritativeVivaDuration = null;
            if (int.TryParse(vivaDurationSetting?.Value, out int parsedVivaDur) && parsedVivaDur > 0)
            {
                authoritativeVivaDuration = parsedVivaDur;
            }

            var vivas = await _context.FYPProjects
                .AsNoTracking()
                .Include(p => p.AssignedExaminers)
                .Where(p => p.VivaDate.HasValue && p.VivaDate.Value.Date == targetDate.Date &&
                            (p.SupervisorId == lecturerId || (p.AssignedExaminers != null && p.AssignedExaminers.Any(ae => ae.ExaminerId == lecturerId))))
                .ToListAsync();

            foreach (var v in vivas)
            {
                if (v.VivaDate.HasValue)
                {
                    TimeSpan vStart = v.VivaDate.Value.TimeOfDay;
                    if (authoritativeVivaDuration.HasValue)
                    {
                        TimeSpan vEnd = vStart.Add(TimeSpan.FromMinutes(authoritativeVivaDuration.Value));
                        freeSlots = RemoveOverlap(freeSlots, vStart, vEnd);
                    }
                    else
                    {
                        // Viva duration is unconfigured / unavailable. Conservatively block working hours.
                        freeSlots = RemoveOverlap(freeSlots, workStart, workEnd);
                        break;
                    }
                }
            }

            return freeSlots;
        }

        private static List<TimeSpanRange> MergeContiguousOrOverlappingIntervals(List<TimeSpanRange> intervals)
        {
            if (intervals == null || !intervals.Any())
                return new List<TimeSpanRange>();

            var sorted = intervals.OrderBy(i => i.StartTime).ThenBy(i => i.EndTime).ToList();
            var merged = new List<TimeSpanRange>();

            var current = new TimeSpanRange { StartTime = sorted[0].StartTime, EndTime = sorted[0].EndTime };

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];

                if (next.StartTime <= current.EndTime)
                {
                    // Overlapping or adjacent interval: extend current EndTime if greater
                    if (next.EndTime > current.EndTime)
                    {
                        current.EndTime = next.EndTime;
                    }
                }
                else
                {
                    merged.Add(current);
                    current = new TimeSpanRange { StartTime = next.StartTime, EndTime = next.EndTime };
                }
            }

            merged.Add(current);
            return merged;
        }

        private static List<TimeSpanRange> SliceIntoDiscreteSlots(List<TimeSpanRange> freeIntervals, int durationMinutes)
        {
            var discreteSlots = new List<TimeSpanRange>();
            if (freeIntervals == null || !freeIntervals.Any() || durationMinutes <= 0)
                return discreteSlots;

            foreach (var freeRange in freeIntervals)
            {
                var currentSlotStart = freeRange.StartTime;
                while (currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes)) <= freeRange.EndTime)
                {
                    discreteSlots.Add(new TimeSpanRange
                    {
                        StartTime = currentSlotStart,
                        EndTime = currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes))
                    });

                    currentSlotStart = currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes));
                }
            }

            return discreteSlots;
        }

        public static bool TryParseWorkingHours(string? startValue, string? endValue, out TimeSpan workStart, out TimeSpan workEnd)
        {
            workStart = TimeSpan.Zero;
            workEnd = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(startValue) || !TimeSpan.TryParse(startValue, out workStart) ||
                string.IsNullOrWhiteSpace(endValue) || !TimeSpan.TryParse(endValue, out workEnd) ||
                workStart >= workEnd)
            {
                return false;
            }
            return true;
        }

        public static bool ShouldSuppressLegacyDefense(
            int legacyMilestoneId,
            DateTime? legacyDefenseDate,
            string? legacyDefenseTime,
            string? legacyRemarks,
            int? excludedBookingMilestoneId,
            int? excludedBookingAttemptNumber,
            DateTime? excludedBookingDate,
            TimeSpan? excludedBookingStartTime)
        {
            if (!excludedBookingMilestoneId.HasValue ||
                !excludedBookingAttemptNumber.HasValue ||
                !excludedBookingDate.HasValue ||
                !excludedBookingStartTime.HasValue)
            {
                return false;
            }

            if (legacyMilestoneId != excludedBookingMilestoneId.Value)
            {
                return false;
            }

            if (legacyDefenseDate != excludedBookingDate.Value)
            {
                return false;
            }

            var attemptResult = IAFDefenseBookingService.ResolveAuthoritativeAttemptNumber(legacyRemarks);
            if (!attemptResult.Success || attemptResult.AttemptNumber != excludedBookingAttemptNumber.Value)
            {
                return false;
            }

            if (!DateTime.TryParse(legacyDefenseTime, out DateTime ddt) ||
                ddt.TimeOfDay != excludedBookingStartTime.Value)
            {
                return false;
            }

            return true;
        }

        public static List<TimeSpanRange> ApplyDefenseConflicts(
            List<TimeSpanRange> freeSlots,
            IEnumerable<(int BookingId, int MilestoneId, int AttemptNumber, TimeSpan StartTime, TimeSpan EndTime)> bookings,
            int? excludeBookingId)
        {
            var result = freeSlots;
            foreach (var b in bookings)
            {
                if (excludeBookingId.HasValue && excludeBookingId.Value > 0 && b.BookingId == excludeBookingId.Value)
                {
                    continue;
                }
                result = RemoveOverlapStatic(result, b.StartTime, b.EndTime);
            }
            return result;
        }

        public static List<TimeSpanRange> ApplyDefenseConflicts(
            List<TimeSpanRange> freeSlots,
            IEnumerable<(int MilestoneId, TimeSpan StartTime, TimeSpan EndTime)> bookings,
            int? excludeMilestoneId)
        {
            var result = freeSlots;
            foreach (var b in bookings)
            {
                if (excludeMilestoneId.HasValue && excludeMilestoneId.Value > 0 && b.MilestoneId == excludeMilestoneId.Value)
                {
                    continue;
                }
                result = RemoveOverlapStatic(result, b.StartTime, b.EndTime);
            }
            return result;
        }

        public static List<TimeSpanRange> RemoveOverlapStatic(List<TimeSpanRange> slots, TimeSpan blockStart, TimeSpan blockEnd)
        {
            var result = new List<TimeSpanRange>();
            if (slots == null || !slots.Any())
                return result;

            foreach (var slot in slots)
            {
                if (blockEnd <= slot.StartTime || blockStart >= slot.EndTime)
                {
                    result.Add(slot);
                }
                else
                {
                    if (blockStart > slot.StartTime)
                    {
                        result.Add(new TimeSpanRange { StartTime = slot.StartTime, EndTime = blockStart });
                    }
                    if (blockEnd < slot.EndTime)
                    {
                        result.Add(new TimeSpanRange { StartTime = blockEnd, EndTime = slot.EndTime });
                    }
                }
            }
            return result;
        }

        private List<TimeSpanRange> RemoveOverlap(List<TimeSpanRange> slots, TimeSpan blockStart, TimeSpan blockEnd)
        {
            return RemoveOverlapStatic(slots, blockStart, blockEnd);
        }
    }
}
