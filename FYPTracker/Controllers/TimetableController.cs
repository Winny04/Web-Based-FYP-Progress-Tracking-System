using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FYPTracker.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using FYPTracker.Filters;

namespace FYPTracker.Controllers
{
    [Authorize]
    [AuthorizeUserType("Supervisor", "Examiner")]
    public class TimetableController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService _weeklyMeetingSchedulingService;

        public TimetableController(
            FYPTrackerContext context,
            FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService weeklyMeetingSchedulingService)
        {
            _context = context;
            _weeklyMeetingSchedulingService = weeklyMeetingSchedulingService;
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated) return null;
            return await _context.ApplicationUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity.Name || u.Email == User.Identity.Name);
        }

        private async Task<string?> GetAndSetSessionFilterAsync(int userId, string? sessionFilter)
        {
            var activeSessionConfig = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true && s.IsActive);
            string? activeSessionName = sessionFilter ?? activeSessionConfig?.Name;

            if (string.IsNullOrEmpty(sessionFilter) && !string.IsNullOrEmpty(activeSessionName))
            {
                var lastSessionName = HttpContext.Session.GetString($"SelectedSession_{userId}");
                if (!string.IsNullOrEmpty(lastSessionName))
                {
                    activeSessionName = lastSessionName;
                }
            }

            if (!string.IsNullOrEmpty(activeSessionName))
            {
                HttpContext.Session.SetString($"SelectedSession_{userId}", activeSessionName);
            }

            return activeSessionName;
        }

        public async Task<IActionResult> ManageAvailability(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var activeSessionName = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var availabilities = await _context.SupervisorAvailabilities
                .Where(a => a.SupervisorId == user.Id)
                .ToListAsync();

            ViewBag.AllSessions = await _context.AcademicSessions
                .OrderByDescending(s => s.StartDate)
                .Select(s => s.Name)
                .ToListAsync();
            ViewBag.SelectedSessionName = activeSessionName;
            ViewBag.CurrentSession = activeSessionName;

            var selectedSessionId = await _context.AcademicSessions
                .AsNoTracking()
                .Where(s => s.Name == activeSessionName)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();

            ViewBag.HasTimetable = selectedSessionId.HasValue &&
                await _context.SupervisorTimetableEntries
                    .AsNoTracking()
                    .AnyAsync(t =>
                        t.SupervisorId == user.Id &&
                        t.AcademicSessionId == selectedSessionId.Value);

            // Detect and attach all unresolved Busy Override conflicts with existing Pending / Approved meetings
            var activeConflicts = await _weeklyMeetingSchedulingService.GetSupervisorActiveOverrideMeetingConflictsAsync(user.Id);
            ViewBag.ActiveOverrideConflicts = activeConflicts;

            return View("~/Views/Supervisor/ManageAvailability.cshtml", availabilities);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAvailability(SupervisorAvailability model, List<string> SelectedDays, string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            model.SupervisorId = user.Id;
            
            if (model.Id == 0)
            {
                if (model.ScheduleType == "WeeklyDefault" && SelectedDays != null && SelectedDays.Any())
                {
                    // Validation: Prevent overlapping times on the same day
                    foreach (var day in SelectedDays)
                    {
                        bool exists = await _context.SupervisorAvailabilities
                            .AnyAsync(a => a.SupervisorId == user.Id && a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == day &&
                                      ((model.StartTime >= a.StartTime && model.StartTime < a.EndTime) || 
                                       (model.EndTime > a.StartTime && model.EndTime <= a.EndTime) ||
                                       (model.StartTime <= a.StartTime && model.EndTime >= a.EndTime)));
                            
                        if (exists)
                        {
                            TempData["Error"] = $"You already have an overlapping schedule set for {day}. Please adjust the times.";
                            return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                        }
                    }

                    foreach (var day in SelectedDays)
                    {
                        var newModel = new SupervisorAvailability
                        {
                            SupervisorId = user.Id,
                            ScheduleType = model.ScheduleType,
                            DayOfWeek = day,
                            SpecificDate = model.SpecificDate,
                            StartTime = model.StartTime,
                            EndTime = model.EndTime,
                            IsAvailable = model.IsAvailable
                        };
                        _context.SupervisorAvailabilities.Add(newModel);
                    }
                }
                else
                {
                    if (model.ScheduleType == "DateOverride" && model.SpecificDate.HasValue)
                    {
                        bool exists = await _context.SupervisorAvailabilities
                            .AnyAsync(a => a.SupervisorId == user.Id && a.ScheduleType == "DateOverride" && a.SpecificDate.HasValue && a.SpecificDate.Value.Date == model.SpecificDate.Value.Date &&
                                      ((model.StartTime >= a.StartTime && model.StartTime < a.EndTime) || 
                                       (model.EndTime > a.StartTime && model.EndTime <= a.EndTime) ||
                                       (model.StartTime <= a.StartTime && model.EndTime >= a.EndTime)));
                            
                        if (exists)
                        {
                            TempData["Error"] = $"You already have an overlapping override set for {model.SpecificDate.Value:dd MMM yyyy}. Please adjust the times.";
                            return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                        }
                    }
                    _context.SupervisorAvailabilities.Add(model);
                }
            }
            else
            {
                var existing = await _context.SupervisorAvailabilities.FindAsync(model.Id);
                if (existing != null && existing.SupervisorId == user.Id)
                {
                    existing.ScheduleType = model.ScheduleType;
                    existing.DayOfWeek = model.DayOfWeek;
                    existing.SpecificDate = model.SpecificDate;
                    existing.StartTime = model.StartTime;
                    existing.EndTime = model.EndTime;
                    existing.IsAvailable = model.IsAvailable;
                    _context.SupervisorAvailabilities.Update(existing);
                }
            }

            await _context.SaveChangesAsync();

            // Check if this newly saved or updated override creates conflicts with existing Pending / Approved meetings
            if (model.ScheduleType == "DateOverride" && model.SpecificDate.HasValue && !model.IsAvailable)
            {
                var newConflicts = await _weeklyMeetingSchedulingService.DetectOverrideMeetingConflictsAsync(
                    user.Id,
                    model.SpecificDate.Value,
                    model.StartTime,
                    model.EndTime,
                    model.IsAvailable,
                    model.Id);

                if (newConflicts.Any())
                {
                    TempData["Success"] = "Availability saved successfully!";
                    TempData["Warning"] = $"Warning: This busy override conflicts with {newConflicts.Count} existing meeting(s) (IDs: {string.Join(", ", newConflicts.Select(c => "#" + c.MeetingId))}). Please review them in Manage Meetings.";
                    return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                }
            }

            TempData["Success"] = "Availability saved successfully!";
            return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAbsence(
            DateTime startDate,
            DateTime? endDate,
            bool isFullDay,
            TimeSpan? startTime,
            TimeSpan? endTime,
            string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var today = DateTime.Today;
            if (startDate.Date < today)
            {
                TempData["Error"] = "Cannot record absence for past dates.";
                return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
            }

            var finalEndDate = endDate.HasValue ? endDate.Value.Date : startDate.Date;
            if (finalEndDate < startDate.Date)
            {
                TempData["Error"] = "End date cannot be earlier than start date.";
                return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
            }

            TimeSpan sTime;
            TimeSpan eTime;

            if (isFullDay)
            {
                sTime = TimeSpan.Zero;
                eTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59));
            }
            else
            {
                if (!startTime.HasValue || !endTime.HasValue)
                {
                    TempData["Error"] = "Please provide both start and end time for partial-day absences.";
                    return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                }

                if (startTime.Value >= endTime.Value)
                {
                    TempData["Error"] = "Start time must be earlier than end time.";
                    return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                }

                sTime = startTime.Value;
                eTime = endTime.Value;
            }

            // Generate daily range
            var days = new List<DateTime>();
            for (var dt = startDate.Date; dt <= finalEndDate; dt = dt.AddDays(1))
            {
                days.Add(dt);
            }

            // Check for duplicate / overlapping DateOverride records on the same dates
            foreach (var date in days)
            {
                bool exists = await _context.SupervisorAvailabilities
                    .AnyAsync(a => a.SupervisorId == user.Id &&
                                   a.ScheduleType == "DateOverride" &&
                                   a.SpecificDate.HasValue &&
                                   a.SpecificDate.Value.Date == date &&
                                   ((sTime >= a.StartTime && sTime < a.EndTime) ||
                                    (eTime > a.StartTime && eTime <= a.EndTime) ||
                                    (sTime <= a.StartTime && eTime >= a.EndTime)));

                if (exists)
                {
                    TempData["Error"] = $"You already have an overlapping schedule or absence record for {date:dd MMM yyyy}.";
                    return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
                }
            }

            var newEntries = new List<SupervisorAvailability>();
            foreach (var date in days)
            {
                newEntries.Add(new SupervisorAvailability
                {
                    SupervisorId = user.Id,
                    ScheduleType = "DateOverride",
                    DayOfWeek = null,
                    SpecificDate = date,
                    StartTime = sTime,
                    EndTime = eTime,
                    IsAvailable = false
                });
            }

            _context.SupervisorAvailabilities.AddRange(newEntries);
            await _context.SaveChangesAsync();

            // Detect conflicts with any existing meetings across the recorded absence range
            var allConflicts = new List<FYPTracker.Models.OverrideMeetingConflictDto>();
            foreach (var entry in newEntries)
            {
                var conflicts = await _weeklyMeetingSchedulingService.DetectOverrideMeetingConflictsAsync(
                    user.Id,
                    entry.SpecificDate!.Value,
                    entry.StartTime,
                    entry.EndTime,
                    entry.IsAvailable,
                    entry.Id);

                allConflicts.AddRange(conflicts);
            }

            var distinctConflicts = allConflicts
                .GroupBy(c => c.MeetingId)
                .Select(g => g.First())
                .ToList();

            if (distinctConflicts.Any())
            {
                TempData["Success"] = "Absence recorded successfully!";
                TempData["Warning"] = $"Warning: This absence period conflicts with {distinctConflicts.Count} existing meeting(s) (IDs: {string.Join(", ", distinctConflicts.Select(c => "#" + c.MeetingId))}). Please review them in Manage Meetings.";
                return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
            }

            TempData["Success"] = "Absence period recorded successfully!";
            return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAvailability(int id, string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var availability = await _context.SupervisorAvailabilities.FindAsync(id);
            if (availability != null && availability.SupervisorId == user.Id)
            {
                _context.SupervisorAvailabilities.Remove(availability);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Availability removed.";
            }

            return RedirectToAction(nameof(ManageAvailability), new { sessionFilter });
        }

        [HttpGet]
        public async Task<IActionResult> GetBookedSlots(int supervisorId, string date)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.Id != supervisorId) return Json(new List<string>());

            if (!DateTime.TryParse(date, out var d)) return Json(new List<string>());

            var bookedTimes = await _context.MeetingBookings
                .Where(m => m.SupervisorId == supervisorId && m.MeetingDate.Date == d.Date && m.Status != "Rejected" && m.Status != "Cancelled")
                .Select(m => m.MeetingTime)
                .ToListAsync();

            return Json(bookedTimes);
        }

        [HttpGet]
        public async Task<IActionResult> ManageTimetable(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var activeSessionName = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);
            ViewBag.CurrentUser = user;

            var entries = new List<SupervisorTimetableEntry>();
            if (string.IsNullOrEmpty(activeSessionName))
            {
                TempData["Error"] = "No active academic session found. You cannot upload a timetable without an active session.";
                ViewBag.ActiveSessionName = "None";
            }
            else
            {
                var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == activeSessionName);
                if (activeSession != null)
                {
                    entries = await _context.SupervisorTimetableEntries
                        .Where(t => t.SupervisorId == user.Id && t.AcademicSessionId == activeSession.Id)
                        .ToListAsync();
                    ViewBag.ActiveSessionName = activeSession.Name;
                    ViewBag.CurrentSession = activeSession.Name;
                    ViewBag.SelectedSessionName = activeSession.Name;
                }
            }

            ViewBag.AllSessions = await _context.AcademicSessions
                .OrderByDescending(s => s.StartDate)
                .Select(s => s.Name)
                .ToListAsync();

            return View("~/Views/Supervisor/ManageTimetable.cshtml", entries);
        }

        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("DayOfWeek,StartTime,EndTime,Details");
            csv.AppendLine("Monday,09:00,11:00,Lecture 1");
            csv.AppendLine("Tuesday,14:00,16:00,Lab Session");
            csv.AppendLine("Wednesday,10:00,12:00,Tutorial");
            csv.AppendLine("Thursday,13:00,15:00,Lecture 2");
            csv.AppendLine("Friday,09:00,11:00,Consultation");

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(buffer, "text/csv", "TeachingTimetable_Template.csv");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PreviewTimetable(IFormFile timetableFile, string? sessionFilter)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var activeSessionName = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            if (timetableFile == null || timetableFile.Length == 0 || !timetableFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please upload a valid CSV file.";
                return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
            }

            var entries = new List<SupervisorTimetableEntry>();
            var errors = new List<string>();
            var validDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

            using (var reader = new StreamReader(timetableFile.OpenReadStream()))
            {
                var headerLine = await reader.ReadLineAsync();
                if (headerLine == null || !headerLine.Contains("Day", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "Invalid CSV format. Missing expected headers (Day, Start Time, End Time).";
                    return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
                }

                int row = 1;
                while (!reader.EndOfStream)
                {
                    row++;
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var values = line.Split(',');
                    if (values.Length < 3)
                    {
                        errors.Add($"Row {row}: Missing required columns.");
                        continue;
                    }

                    string dayStr = values[0].Trim();
                    if (int.TryParse(dayStr, out int dayInt) && dayInt >= 1 && dayInt <= 7)
                    {
                        dayStr = ((DayOfWeek)(dayInt % 7)).ToString();
                    }
                    else if (dayStr.Equals("7", StringComparison.OrdinalIgnoreCase)) dayStr = "Sunday";
                    
                    if (dayStr.Length > 1) dayStr = char.ToUpper(dayStr[0]) + dayStr.Substring(1).ToLower();

                    if (!validDays.Contains(dayStr))
                    {
                        errors.Add($"Row {row}: Invalid day format '{values[0]}'.");
                        continue;
                    }

                    var entry = new SupervisorTimetableEntry
                    {
                        DayOfWeek = dayStr,
                        Details = values.Length > 3 ? values[3].Trim() : ""
                    };
                    
                    TimeSpan? ParseTime(string val)
                    {
                        val = val.Trim();
                        if (string.IsNullOrEmpty(val)) return null;
                        if (int.TryParse(val, out int hour) && hour >= 0 && hour <= 23)
                            return TimeSpan.FromHours(hour);
                        val = val.Replace(".", ":");
                        if (DateTime.TryParse(val, out var dt))
                            return dt.TimeOfDay;
                        if (TimeSpan.TryParse(val, out var ts))
                        {
                            if (ts.Days > 0 && ts.Hours == 0 && ts.Minutes == 0) ts = TimeSpan.FromHours(ts.Days);
                            if (ts >= TimeSpan.Zero && ts < TimeSpan.FromHours(24))
                                return ts;
                        }
                        return null;
                    }

                    var parsedStart = ParseTime(values[1]);
                    var parsedEnd = ParseTime(values[2]);
                    
                    if (!parsedStart.HasValue || !parsedEnd.HasValue)
                    {
                        errors.Add($"Row {row}: Invalid time format.");
                        continue;
                    }

                    if (parsedStart.Value >= parsedEnd.Value)
                    {
                        errors.Add($"Row {row}: Start time must be before end time.");
                        continue;
                    }
                    
                    bool overlap = entries.Any(e => e.DayOfWeek == entry.DayOfWeek && 
                        ((parsedStart.Value >= e.StartTime && parsedStart.Value < e.EndTime) || 
                         (parsedEnd.Value > e.StartTime && parsedEnd.Value <= e.EndTime) ||
                         (parsedStart.Value <= e.StartTime && parsedEnd.Value >= e.EndTime)));

                    if (overlap)
                    {
                        errors.Add($"Row {row}: Overlaps with another period on {entry.DayOfWeek}.");
                        continue;
                    }

                    entry.StartTime = parsedStart.Value;
                    entry.EndTime = parsedEnd.Value;
                    entries.Add(entry);
                }
            }

            if (errors.Any())
            {
                TempData["Error"] = "CSV Validation Failed: " + string.Join(" | ", errors);
                return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
            }

            if (!entries.Any())
            {
                TempData["Error"] = "No valid timetable entries found in the file.";
                return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
            }

            TempData["TimetablePreview"] = System.Text.Json.JsonSerializer.Serialize(entries);
            TempData["TimetablePreviewSession"] = activeSessionName;
            TempData["TimetablePreviewUserId"] = user.Id;

            ViewBag.SelectedSessionName = activeSessionName;
            ViewBag.CurrentSession = activeSessionName;
            return View("~/Views/Supervisor/PreviewTimetable.cshtml", entries);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTimetable(string? sessionFilter)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var activeSessionName = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);
            var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == activeSessionName);

            if (activeSession == null) 
            {
                TempData["Error"] = "No valid academic session found. Cannot save timetable.";
                return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
            }

            var previewJson = TempData["TimetablePreview"] as string;
            var expectedSession = TempData["TimetablePreviewSession"] as string;
            var expectedUserId = TempData["TimetablePreviewUserId"] as int?;

            if (string.IsNullOrEmpty(previewJson) || expectedSession != activeSessionName || expectedUserId != user.Id)
            {
                TempData["Error"] = "Session mismatch or expired. Please upload again.";
                return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
            }

            var parsedEntries = System.Text.Json.JsonSerializer.Deserialize<List<SupervisorTimetableEntry>>(previewJson);
            if (parsedEntries != null && parsedEntries.Any())
            {
                var oldEntries = await _context.SupervisorTimetableEntries
                    .Where(t => t.SupervisorId == user.Id && t.AcademicSessionId == activeSession.Id)
                    .ToListAsync();
                _context.SupervisorTimetableEntries.RemoveRange(oldEntries);

                foreach (var entry in parsedEntries)
                {
                    entry.SupervisorId = user.Id;
                    entry.AcademicSessionId = activeSession.Id;
                    _context.SupervisorTimetableEntries.Add(entry);
                }
                await _context.SaveChangesAsync();
                TempData["Success"] = "Timetable successfully updated.";
            }
            
            return RedirectToAction(nameof(ManageTimetable), new { sessionFilter = activeSessionName });
        }
    }
}
