using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;

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

            var allLecturersSlots = new List<List<TimeSpanRange>>();

            foreach (var id in lecturerIds)
            {
                var slots = await GetAvailableSlotsAsync(
                    id,
                    targetDate,
                    durationMinutes,
                    academicSessionId);

                allLecturersSlots.Add(slots);
            }

            var mutualSlots = allLecturersSlots.First();

            foreach (var slots in allLecturersSlots.Skip(1))
            {
                mutualSlots = IntersectSlots(
                    mutualSlots,
                    slots,
                    durationMinutes);
            }

            return mutualSlots;
        }

        private List<TimeSpanRange> IntersectSlots(List<TimeSpanRange> list1, List<TimeSpanRange> list2, int minDuration)
        {
            var result = new List<TimeSpanRange>();
            foreach (var s1 in list1)
            {
                foreach (var s2 in list2)
                {
                    var start = s1.StartTime > s2.StartTime ? s1.StartTime : s2.StartTime;
                    var end = s1.EndTime < s2.EndTime ? s1.EndTime : s2.EndTime;

                    if ((end - start).TotalMinutes >= minDuration)
                    {
                        result.Add(new TimeSpanRange { StartTime = start, EndTime = end });
                    }
                }
            }
            return result;
        }

        public async Task<List<TimeSpanRange>> GetAvailableSlotsAsync(int lecturerId, DateTime targetDate, int durationMinutes, int academicSessionId)
        {
            // 1. Resolve Lecturer
            var lecturer = await _context.ApplicationUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == lecturerId && !u.IsResign);
            if (lecturer == null) return new List<TimeSpanRange>();

            // Academic Session is supplied by the calling workflow.
            if (academicSessionId <= 0)
                return new List<TimeSpanRange>();

            // SAFETY: Do not assume a lecturer is available
            // when no teaching timetable has been configured
            // for the selected academic session.
            bool hasTimetable = await _context.SupervisorTimetableEntries
                .AsNoTracking()
                .AnyAsync(t =>
                    t.SupervisorId == lecturerId &&
                    t.AcademicSessionId == academicSessionId);

            if (!hasTimetable)
            {
                // No timetable configured for this session.
                // Return no bookable slots instead of assuming availability.
                return new List<TimeSpanRange>();
            }

            // 2. Resolve Working Hours from FYPSettings
            // Require valid LecturerWorkingHoursStart and LecturerWorkingHoursEnd values in FYPSetting.
            // Automatic 09:00 - 17:00 fallback is removed per strict academic policy.
            // If missing or invalid, return zero bookable slots.
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
                // Missing, unconfigured or invalid working hours window: zero bookable slots
                return new List<TimeSpanRange>();
            }

            string dayOfWeek = targetDate.ToString("dddd");

            // Base Free Time based on configured working hours
            var freeSlots = new List<TimeSpanRange> 
            { 
                new TimeSpanRange { StartTime = workStart, EndTime = workEnd } 
            };

            // 3. Weekly Availability blocks (WeeklyDefault can restrict or define allowed windows)
            var weeklyAvailability = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId && a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == dayOfWeek)
                .ToListAsync();

            if (weeklyAvailability.Any())
            {
                var availableRanges = new List<TimeSpanRange>();
                foreach (var w in weeklyAvailability.Where(w => w.IsAvailable))
                {
                    // Clamp to work hours
                    var s = w.StartTime < workStart ? workStart : w.StartTime;
                    var e = w.EndTime > workEnd ? workEnd : w.EndTime;
                    if (e > s)
                    {
                        availableRanges.Add(new TimeSpanRange { StartTime = s, EndTime = e });
                    }
                }
                if (availableRanges.Any())
                {
                    freeSlots = availableRanges;
                }
            }

            // 4. Exclude Teaching Commitments (Strict: newStart < existingEnd && newEnd > existingStart)
            var teaching = await _context.SupervisorTimetableEntries
                .AsNoTracking()
                .Where(t => t.SupervisorId == lecturerId && t.AcademicSessionId == academicSessionId && t.DayOfWeek == dayOfWeek)
                .ToListAsync();

            foreach (var t in teaching)
            {
                if (t.EndTime > t.StartTime)
                {
                    freeSlots = RemoveOverlap(freeSlots, t.StartTime, t.EndTime);
                }
            }

            // 5. Exclude Student Meetings (Approved or Pending)
            // Priority 1: Valid stored StartDateTime and EndDateTime.
            // Priority 2: Verified explicit booking duration when available.
            // Priority 3: Conservative handling when historical duration cannot be established.
            // (Do not treat current MeetingDurationMinutes as proof of historical duration.)
            var meetings = await _context.MeetingBookings
                .AsNoTracking()
                .Where(m => m.SupervisorId == lecturerId && m.MeetingDate == targetDate.Date && 
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
                // Do not treat current MeetingDurationMinutes as proof of historical duration.
                // Apply conservative conflict protection (60 minutes) so 45-min defense slots never overlap.
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

            // 6. Exclude Existing IAF Defenses (Scheduled or DefenseRequired with dates)
            var defenses = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project).ThenInclude(p => p.AssignedExaminers)
                .Where(m => m.DefenseDate == targetDate.Date && 
                            (m.Status == "DefenseScheduled" || m.Status == "DefenseRequired"))
                .ToListAsync();

            foreach (var d in defenses)
            {
                bool isParticipant = (d.Project?.SupervisorId == lecturerId) || 
                                     (d.Project?.AssignedExaminers != null && d.Project.AssignedExaminers.Any(ae => ae.ExaminerId == lecturerId));

                if (isParticipant)
                {
                    if (DateTime.TryParse(d.DefenseTime, out DateTime ddt))
                    {
                        TimeSpan dt = ddt.TimeOfDay;
                        freeSlots = RemoveOverlap(freeSlots, dt, dt.Add(TimeSpan.FromMinutes(durationMinutes)));
                    }
                }
            }

            // 7. Exclude Final Viva Presentations if VivaDate is set for this lecturer
            // Do not assume every Viva takes 60 minutes. Use authoritative duration and participant info.
            // If unavailable, handle conservatively across working hours.
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
                        // Viva duration is unconfigured / unavailable. Do not invent 60 minutes.
                        // Conservatively block to avoid unsafe double-booking.
                        freeSlots = RemoveOverlap(freeSlots, workStart, workEnd);
                        break;
                    }
                }
            }

            // 8. Date Overrides (Must NEVER override confirmed Teaching, Meeting, Defense or Viva conflicts)
            // An unavailable override blocks the specified time.
            var dateOverrides = await _context.SupervisorAvailabilities
                .AsNoTracking()
                .Where(a => a.SupervisorId == lecturerId && a.ScheduleType == "DateOverride" && a.SpecificDate == targetDate.Date)
                .ToListAsync();
            
            if (dateOverrides.Any())
            {
                var blockedOverrides = dateOverrides.Where(o => !o.IsAvailable).ToList();
                foreach (var block in blockedOverrides)
                {
                    // Block the override window (or entire working window if 00:00 to 00:00 or full day)
                    TimeSpan bStart = block.StartTime == TimeSpan.Zero && block.EndTime == TimeSpan.Zero ? workStart : block.StartTime;
                    TimeSpan bEnd = block.StartTime == TimeSpan.Zero && block.EndTime == TimeSpan.Zero ? workEnd : block.EndTime;
                    freeSlots = RemoveOverlap(freeSlots, bStart, bEnd);
                }
            }

            // 9. Slicing into Discrete Defense Time Slots
            // Do not present one large free interval (e.g. 09:00 - 17:00) as one huge defense appointment.
            // Generate discrete slots with the exact configured durationMinutes.
            var discreteSlots = new List<TimeSpanRange>();
            foreach (var freeRange in freeSlots)
            {
                var currentSlotStart = freeRange.StartTime;
                while (currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes)) <= freeRange.EndTime)
                {
                    discreteSlots.Add(new TimeSpanRange
                    {
                        StartTime = currentSlotStart,
                        EndTime = currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes))
                    });

                    // Advance by durationMinutes
                    currentSlotStart = currentSlotStart.Add(TimeSpan.FromMinutes(durationMinutes));
                }
            }

            return discreteSlots;
        }

        private List<TimeSpanRange> RemoveOverlap(List<TimeSpanRange> slots, TimeSpan blockStart, TimeSpan blockEnd)
        {
            var result = new List<TimeSpanRange>();
            foreach (var slot in slots)
            {
                // Strict overlap check: newStart < existingEnd && newEnd > existingStart
                // No overlap if: blockStart >= slot.EndTime || blockEnd <= slot.StartTime
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
    }
}
