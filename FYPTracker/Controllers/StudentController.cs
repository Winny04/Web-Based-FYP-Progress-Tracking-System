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

        // ══════════════════════════════════════════════════════
        //  STUDENT DASHBOARD / HOME PAGE
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "EmployeeInfo");

            ViewBag.CurrentUser = user;

            // Fetch the student's project
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            // Fetch recent activities
            var activities = await _context.FYPActivities
                .Where(a => a.StudentId == user.Id)
                .OrderByDescending(a => a.CreatedDate)
                .Take(10)
                .ToListAsync();

            ViewBag.Activities = activities;

            // Fetch unread notifications
            var unreadNotifCount = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id && !n.IsRead)
                .CountAsync();
            ViewBag.UnreadNotificationCount = unreadNotifCount;

            return View(project);
        }

        // ──────────────────────────────────────────────────────
        //  DIGITAL eFORMS
        // ──────────────────────────────────────────────────────
        public async Task<IActionResult> IAF()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "EmployeeInfo");

            // Check if user already has an IAF submission
            var existing = await _context.FYPIAForms.FirstOrDefaultAsync(f => f.StudentId == user.Id);
            ViewBag.CurrentUser = user;
            
            return View(existing);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitIAF(FYPIAForm form)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            form.StudentId = user.Id;
            form.SubmittedDate = DateTime.Now;
            form.Status = "Pending";

            var existing = await _context.FYPIAForms.FirstOrDefaultAsync(f => f.StudentId == user.Id);
            if (existing != null)
            {
                // Update existing
                existing.CompanyName = form.CompanyName;
                existing.CompanyAddress = form.CompanyAddress;
                existing.CompanySupervisorName = form.CompanySupervisorName;
                existing.CompanySupervisorEmail = form.CompanySupervisorEmail;
                existing.CompanySupervisorPhone = form.CompanySupervisorPhone;
                existing.StartDate = form.StartDate;
                existing.EndDate = form.EndDate;
                existing.ProjectTitle = form.ProjectTitle;
                existing.ProjectScope = form.ProjectScope;
                existing.MonthlyAllowance = form.MonthlyAllowance;
                existing.Status = "Pending";
                existing.SubmittedDate = DateTime.Now;
                _context.FYPIAForms.Update(existing);
            }
            else
            {
                _context.FYPIAForms.Add(form);
            }

            await _context.SaveChangesAsync();
            await AddNotificationAsync(user.Id, "eForm", "IAF Submitted", "Your Industrial Attachment Form has been submitted for review.");
            
            TempData["Success"] = "IAF Form submitted successfully!";
            return RedirectToAction("IAF");
        }

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

            // If student already has a confirmed supervisor, redirect to dashboard
            var hasApproved = await _context.SupervisorRequests
                .AnyAsync(r => r.StudentId == user.Id && r.Status == "Approved");
            if (hasApproved)
            {
                TempData["Info"] = "You already have a confirmed supervisor.";
                return RedirectToAction("Index");
            }

            // 1. Auto-Expiry (2 Days)
            var expired = await _context.SupervisorRequests
                .Where(r => r.Status == "Pending" && r.ExpiryDate < DateTime.Now)
                .ToListAsync();
            foreach (var e in expired)
            {
                e.Status = "Rejected";
                e.ResponseNote = "Auto-rejected: No response within 2 days.";
                e.RespondedDate = DateTime.Now;
            }
            if (expired.Any()) await _context.SaveChangesAsync();

            // 2. Identify Rejectors
            var rejectedIds = await _context.SupervisorRequests
                .Where(r => r.StudentId == user.Id && r.Status == "Rejected")
                .Select(r => r.SupervisorId)
                .ToListAsync();

            // 3. Get Filtered Supervisors
            var supervisors = await _context.EmployeeInfos
                .Include(e => e.EmpDepartment)
                .Where(e => e.UserType == "Supervisor" && !e.IsResign && !rejectedIds.Contains(e.Id))
                .ToListAsync();

            // 4. Calculate Applicant Counts (Pending + Approved)
            var applicantCounts = await _context.SupervisorRequests
                .Where(r => r.Status == "Pending" || r.Status == "Approved")
                .GroupBy(r => r.SupervisorId)
                .Select(g => new { SupervisorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SupervisorId, x => x.Count);

            // 5. Get Student's Requests
            var myRequests = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .Where(r => r.StudentId == user.Id)
                .OrderBy(r => r.Priority)
                .ToListAsync();

            ViewBag.Supervisors     = supervisors;
            ViewBag.ApplicantCounts = applicantCounts;
            ViewBag.MyRequests      = myRequests;
            ViewBag.CurrentUser     = user;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupervisorRequest(int supervisorId, string matricNumber, string program, string phoneNumber, string sessionSemester, string yearOfStudy, string projectTopic, string? requestMessage)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var myRequests = await _context.SupervisorRequests.Where(r => r.StudentId == user.Id).ToListAsync();
            
            if (myRequests.Any(r => r.Status == "Approved"))
            {
                TempData["Error"] = "You already have an approved supervisor.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (myRequests.Count(r => r.Status == "Pending") >= 3)
            {
                TempData["Error"] = "You can only have up to 3 pending requests at a time.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (myRequests.Any(r => r.SupervisorId == supervisorId && r.Status == "Pending"))
            {
                TempData["Error"] = "You already have a pending request for this supervisor.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (string.IsNullOrWhiteSpace(sessionSemester))
            {
                TempData["Error"] = "Session / Semester cannot be empty.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (string.IsNullOrWhiteSpace(phoneNumber) || !System.Text.RegularExpressions.Regex.IsMatch(phoneNumber, @"^(\+?60|0)1[0-9][\-\s]?[0-9]{3,4}[\-\s]?[0-9]{4}$"))
            {
                TempData["Error"] = "Please provide a valid Malaysia Phone Number (e.g. 012-3456789).";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (string.IsNullOrWhiteSpace(projectTopic) || projectTopic.Length < 5)
            {
                TempData["Error"] = "Project Topic must be at least 5 characters long.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (string.IsNullOrWhiteSpace(requestMessage) || requestMessage.Length < 10)
            {
                TempData["Error"] = "Brief Proposal / Message must be at least 10 characters long.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            var request = new SupervisorRequest
            {
                StudentId       = user.Id,
                SupervisorId    = supervisorId,
                Status          = "Pending",
                RequestedDate   = DateTime.Now,
                ExpiryDate      = DateTime.Now.AddDays(2),
                Priority        = myRequests.Count(r => r.Status == "Pending") + 1,
                
                MatricNumber    = matricNumber,
                Program         = program,
                PhoneNumber     = phoneNumber,
                SessionSemester = sessionSemester,
                YearOfStudy     = yearOfStudy,
                ProjectTopic    = projectTopic,
                RequestMessage  = requestMessage
            };

            _context.SupervisorRequests.Add(request);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "General", "Supervisor Request Sent", $"Sent request to supervisor {supervisorId}");
            await AddNotificationAsync(user.Id, "General", "Request Submitted", "Your supervision request has been sent.");

            TempData["Success"] = "Supervision request sent successfully!";
            return RedirectToAction(nameof(SelectSupervisor));
        }

        // ────────────────────────────────────────────────────────
        //  SUBMISSIONS (REPORT UPLOAD)
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Submissions()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var project = await _context.FYPProjects
                .Include(p => p.Milestones)
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project == null)
            {
                TempData["Error"] = "You don't have an approved project yet. Please apply for a supervisor first.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadSubmission(int milestoneId, IFormFile reportFile)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            if (reportFile == null || reportFile.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction(nameof(Submissions));
            }

            // 1. Validate File type & Size
            var allowedExt = new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".zip" };
            var ext = Path.GetExtension(reportFile.FileName).ToLowerInvariant();
            if (!allowedExt.Contains(ext))
            {
                TempData["Error"] = "Only PDF, DOC, PPT, and ZIP files are allowed.";
                return RedirectToAction(nameof(Submissions));
            }

            if (reportFile.Length > 20 * 1024 * 1024) // 20 MB
            {
                TempData["Error"] = "File size must not exceed 20 MB.";
                return RedirectToAction(nameof(Submissions));
            }

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone == null) return NotFound();

            // 2. Handle File Saving
            var uniqueName = $"{Guid.NewGuid()}{ext}";
            var uploadDir  = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/submissions");
            Directory.CreateDirectory(uploadDir);
            
            var path = Path.Combine(uploadDir, uniqueName);
            using (var stream = new FileStream(path, FileMode.Create))
            {
                await reportFile.CopyToAsync(stream);
            }

            // 3. Save DB record
            var submissionFile = new FYPSubmissionFile
            {
                MilestoneId  = milestoneId,
                FileName     = reportFile.FileName,
                FilePath     = "/uploads/submissions/" + uniqueName,
                FileSize     = reportFile.Length,
                UploadedBy   = user.Id,
                UploadedDate = DateTime.Now
            };
            _context.FYPSubmissionFiles.Add(submissionFile);

            milestone.Status = "Submitted";
            milestone.SubmittedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "Submission", "File Uploaded", $"Uploaded {reportFile.FileName} for {milestone.Title}");
            await AddNotificationAsync(milestone.Project.SupervisorId ?? 0, "Submission", "New Submission", $"Student {user.FullName} uploaded a report for {milestone.Title}");

            TempData["Success"] = "Report uploaded successfully!";
            return RedirectToAction(nameof(Submissions));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitDigitalForm(
            int milestoneId, string formType, string? title, string? detailsJson, IFormFile? attachedFile)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "EmployeeInfo");

            string? filePath = null;
            string? fileName = null;
            long? fileSize = null;
            if (attachedFile != null && attachedFile.Length > 0)
            {
                var ext = Path.GetExtension(attachedFile.FileName).ToLowerInvariant();
                var uniqueName = $"{Guid.NewGuid()}{ext}";
                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/submissions");
                Directory.CreateDirectory(uploadDir);
                var path = Path.Combine(uploadDir, uniqueName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await attachedFile.CopyToAsync(stream);
                }
                filePath = "/uploads/submissions/" + uniqueName;
                fileName = attachedFile.FileName;
                fileSize = attachedFile.Length;
            }

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone != null)
            {
                milestone.Status = "Submitted";
                milestone.SubmittedDate = DateTime.Now;
                milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                
                if (filePath != null)
                {
                    var fileRec = new FYPSubmissionFile
                    {
                        MilestoneId = milestoneId,
                        FileName = fileName ?? "AttachedFile",
                        FilePath = filePath,
                        FileSize = fileSize,
                        UploadedBy = user.Id,
                        UploadedDate = DateTime.Now
                    };
                    _context.FYPSubmissionFiles.Add(fileRec);
                }
                
                await _context.SaveChangesAsync();
                
                await LogActivityAsync(user.Id, "Submission", "Form Submitted", $"Submitted {formType} for {milestone.Title}");
                await AddNotificationAsync(milestone.Project.SupervisorId ?? 0, "Submission", "Form Submitted", $"Student {user.FullName} submitted {formType} for {milestone.Title}");
            }

            TempData["Success"] = "Your form has been submitted successfully!";
            return RedirectToAction("Forms");
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

            var supervisor = await _context.EmployeeInfos.FindAsync(supervisorId);
            string supName = supervisor?.FullName ?? "your supervisor";
            TempData["Success"] = $"Meeting request sent to {supName}!";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpGet]
        public async Task<JsonResult> GetBookedSlots(int supervisorId, string date)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new List<string>());

            var bookedTimes = await _context.MeetingBookings
                .Where(m => m.SupervisorId == supervisorId 
                         && m.MeetingDate.Date == parsedDate.Date 
                         && m.Status != "Rejected")
                .Select(m => m.MeetingTime)
                .ToListAsync();

            return Json(bookedTimes);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptPostponedMeeting(int meetingId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && m.Status == "Postponed");

            if (meeting != null && meeting.ProposedNewDate.HasValue)
            {
                meeting.MeetingDate = meeting.ProposedNewDate.Value;
                meeting.MeetingDay  = meeting.ProposedNewDate.Value.DayOfWeek.ToString();
                meeting.Status      = "Approved";
                meeting.ProposedNewDate = null;
                meeting.RespondedDate = DateTime.Now;
                
                await _context.SaveChangesAsync();

                await LogActivityAsync(user.Id, "MeetingUpdate", "Meeting Date Accepted", 
                    $"You accepted the suggested date for the meeting on {meeting.MeetingDate:dd MMM}.");
                
                TempData["Success"] = "New meeting date accepted and confirmed!";
            }

            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RescheduleMeeting(int meetingId, string meetingDate, string meetingTime)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            if (!DateTime.TryParse(meetingDate, out var parsedDate))
            {
                TempData["Error"] = "Invalid date format.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id);

            if (meeting != null)
            {
                meeting.MeetingDate = parsedDate;
                meeting.MeetingDay  = parsedDate.DayOfWeek.ToString();
                meeting.MeetingTime = meetingTime;
                meeting.Status      = "Pending"; // Set to Pending so supervisor can re-approve
                meeting.RespondedDate = null;
                meeting.SupervisorNote = "Student rescheduled this meeting.";
                
                await _context.SaveChangesAsync();

                await LogActivityAsync(user.Id, "MeetingUpdate", "Meeting Rescheduled", 
                    $"Meeting moved to {parsedDate:dd MMM} at {meetingTime}.");
                
                TempData["Success"] = "Meeting rescheduled successfully! Awaiting supervisor re-approval.";
            }

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
