using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class StudentController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;

        public StudentController(FYPTrackerContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env     = env;
        }

        // ──────────────────────────────────────────────────────
        //  HELPER: current logged-in EmployeeInfo
        // ──────────────────────────────────────────────────────
        private async Task<EmployeeInfo?> GetCurrentUserAsync()
        {
            var userName = HttpContext.User.Identity?.Name;
            return await _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .FirstOrDefaultAsync(x => x.UserName == userName);
        }

        // ──────────────────────────────────────────────────────
        //  HELPER: log an activity entry
        // ──────────────────────────────────────────────────────
        private async Task LogActivityAsync(int studentId, string type, string title, string? desc = null)
        {
            _context.FYPActivities.Add(new FYPActivity
            {
                StudentId   = studentId,
                Type        = type,
                Title       = title,
                Description = desc,
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // ──────────────────────────────────────────────────────
        //  HELPER: create a notification
        // ──────────────────────────────────────────────────────
        private async Task AddNotificationAsync(int recipientId, string type, string title, string? message = null)
        {
            _context.FYPNotifications.Add(new FYPNotification
            {
                RecipientId = recipientId,
                Type        = type,
                Title       = title,
                Message     = message,
                IsRead      = false,
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════
        //  PROJECT DETAILS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> ProjectDetails()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Feedbacks)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            return View(project);
        }

        // ══════════════════════════════════════════════════════
        //  SELECT SUPERVISOR
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> SelectSupervisor()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            // Auto-reject expired pending requests
            var expiredRequests = await _context.SupervisorRequests
                .Where(r => r.StudentId == user.Id && r.Status == "Pending" && r.ExpiryDate < DateTime.Now)
                .ToListAsync();
            foreach (var req in expiredRequests)
            {
                req.Status       = "Rejected";
                req.ResponseNote = "Auto-rejected: no response within 7 days.";
                req.RespondedDate = DateTime.Now;
            }
            if (expiredRequests.Any()) await _context.SaveChangesAsync();

            // All active supervisors
            var supervisors = await _context.EmployeeInfos
                .Include(e => e.EmpDepartment)
                .Where(e => e.UserType == "Supervisor" && e.IsResign == false)
                .ToListAsync();

            // Count how many students each supervisor has approved
            var approvedCountMap = await _context.SupervisorRequests
                .Where(r => r.Status == "Approved")
                .GroupBy(r => r.SupervisorId)
                .Select(g => new { SupervisorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SupervisorId, x => x.Count);

            // Student's existing requests
            var myRequests = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .Where(r => r.StudentId == user.Id)
                .OrderByDescending(r => r.RequestedDate)
                .ToListAsync();

            ViewBag.Supervisors     = supervisors;
            ViewBag.MyRequests      = myRequests;
            ViewBag.ApprovedCounts  = approvedCountMap;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupervisorRequest(int supervisorId, string? requestMessage)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            bool alreadyExists = await _context.SupervisorRequests
                .AnyAsync(r => r.StudentId == user.Id
                            && r.SupervisorId == supervisorId
                            && (r.Status == "Pending" || r.Status == "Approved"));

            if (alreadyExists)
            {
                TempData["Error"] = "You already have a pending or approved request for this supervisor.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            var request = new SupervisorRequest
            {
                StudentId      = user.Id,
                SupervisorId   = supervisorId,
                RequestMessage = requestMessage,
                RequestedDate  = DateTime.Now,
                ExpiryDate     = DateTime.Now.AddDays(7),
                Status         = "Pending"
            };
            _context.SupervisorRequests.Add(request);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "General", "Supervisor Request Sent",
                "You sent a supervision request.");
            await AddNotificationAsync(user.Id, "General", "Supervisor Request Submitted",
                "Your request has been sent. You will be notified within 7 days.");

            TempData["Success"] = "Supervisor request submitted! The supervisor has 7 days to respond.";
            return RedirectToAction(nameof(SelectSupervisor));
        }

        // ══════════════════════════════════════════════════════
        //  FORMS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Forms()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadSubmission(int milestoneId, IFormFile file)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction(nameof(Forms));
            }

            // Validate allowed types
            var allowedExt = new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".zip" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExt.Contains(ext))
            {
                TempData["Error"] = "Only PDF, DOC, DOCX, PPT, PPTX, ZIP files are allowed.";
                return RedirectToAction(nameof(Forms));
            }

            // Max 20 MB
            if (file.Length > 20 * 1024 * 1024)
            {
                TempData["Error"] = "File size must not exceed 20 MB.";
                return RedirectToAction(nameof(Forms));
            }

            // Save file
            var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "submissions");
            Directory.CreateDirectory(uploadDir);
            var uniqueName = $"{Guid.NewGuid()}{ext}";
            var filePath   = Path.Combine(uploadDir, uniqueName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var submission = new FYPSubmissionFile
            {
                MilestoneId  = milestoneId,
                FileName     = file.FileName,
                FilePath     = $"/uploads/submissions/{uniqueName}",
                FileSize     = file.Length,
                UploadedBy   = user.Id,
                UploadedDate = DateTime.Now
            };
            _context.FYPSubmissionFiles.Add(submission);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "DocumentUploaded", "Document Uploaded", $"{file.FileName} was uploaded.");
            await AddNotificationAsync(user.Id, "General", "File Uploaded Successfully",
                $"{file.FileName} was uploaded to your submission.");

            TempData["Success"] = $"'{file.FileName}' uploaded successfully!";
            return RedirectToAction(nameof(Forms));
        }

        // ══════════════════════════════════════════════════════
        //  BOOK MEETING
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> BookMeeting()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            var approvedRequest = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .FirstOrDefaultAsync(r => r.StudentId == user.Id && r.Status == "Approved");

            ViewBag.AssignedSupervisor = approvedRequest?.Supervisor;

            var meetings = await _context.MeetingBookings
                .Include(m => m.Supervisor)
                .Where(m => m.StudentId == user.Id)
                .OrderByDescending(m => m.CreatedDate)
                .ToListAsync();

            ViewBag.Meetings = meetings;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMeetingBooking(
            int supervisorId, string meetingDate, string meetingTime, string? reason)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            if (!DateTime.TryParse(meetingDate, out var parsedDate))
            {
                TempData["Error"] = "Invalid date format.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var booking = new MeetingBooking
            {
                StudentId    = user.Id,
                SupervisorId = supervisorId,
                MeetingDate  = parsedDate,
                MeetingDay   = parsedDate.DayOfWeek.ToString(),
                MeetingTime  = meetingTime,
                Reason       = reason,
                Status       = "Pending",
                CreatedDate  = DateTime.Now
            };
            _context.MeetingBookings.Add(booking);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "MeetingBooked", "Meeting Request Sent",
                $"Meeting on {parsedDate:dd MMM yyyy} at {meetingTime} requested.");
            await AddNotificationAsync(user.Id, "MeetingUpdate", "Meeting Request Submitted",
                $"Your meeting request for {parsedDate:dd MMM yyyy} at {meetingTime} is pending approval.");

            TempData["Success"] = "Meeting request submitted! Awaiting supervisor approval.";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMeeting(int meetingId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && m.Status == "Pending");

            if (meeting == null)
            {
                TempData["Error"] = "Meeting not found or cannot be cancelled.";
                return RedirectToAction(nameof(BookMeeting));
            }

            _context.MeetingBookings.Remove(meeting);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "General", "Meeting Cancelled",
                $"Meeting on {meeting.MeetingDate:dd MMM yyyy} was cancelled.");

            TempData["Success"] = "Meeting request cancelled.";
            return RedirectToAction(nameof(BookMeeting));
        }

        // ══════════════════════════════════════════════════════
        //  NOTIFICATIONS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Notifications()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            var notifications = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id)
                .OrderByDescending(n => n.CreatedDate)
                .ToListAsync();

            ViewBag.UnreadCount = notifications.Count(n => !n.IsRead);
            return View(notifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var notification = await _context.FYPNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientId == user.Id);

            if (notification != null)
            {
                notification.IsRead  = true;
                notification.ReadDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Notifications));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var unread = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id && !n.IsRead)
                .ToListAsync();

            foreach (var n in unread) { n.IsRead = true; n.ReadDate = DateTime.Now; }
            await _context.SaveChangesAsync();

            TempData["Success"] = "All notifications marked as read.";
            return RedirectToAction(nameof(Notifications));
        }
    }
}
