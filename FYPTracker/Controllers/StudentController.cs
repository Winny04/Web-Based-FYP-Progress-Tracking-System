using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;
using NPOI.XWPF.UserModel;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class StudentController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public StudentController(FYPTrackerContext context, IWebHostEnvironment env, IEmailService emailService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _env     = env;
            _emailService = emailService;
            _hubContext = hubContext;
        }

        // ══════════════════════════════════════════════════════
        //  STUDENT DASHBOARD / HOME PAGE
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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

            // Fetch Upcoming Meeting
            var upcomingMeeting = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status == "Approved" && m.MeetingDate.Date >= DateTime.Today)
                .OrderBy(m => m.MeetingDate)
                .ThenBy(m => m.MeetingTime)
                .FirstOrDefaultAsync();
            ViewBag.UpcomingMeeting = upcomingMeeting;

            // Fetch Pending Meetings count
            var pendingMeetingsCount = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status == "Pending")
                .CountAsync();
            ViewBag.PendingMeetingsCount = pendingMeetingsCount;

            return View(project);
        }


        //  HELPER: current logged-in ApplicationUser
        // ──────────────────────────────────────────────────────
        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userName = HttpContext.User.Identity?.Name;
            return await _context.ApplicationUsers
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
        private async Task AddNotificationAsync(int recipientId, string type, string title, string? message = null, int? relatedRecordId = null)
        {
            _context.FYPNotifications.Add(new FYPNotification
            {
                RecipientId = recipientId,
                Type        = type,
                Title       = title,
                Message     = message,
                IsRead      = false,
                RelatedRecordId = relatedRecordId,
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
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
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
            var supervisors = await _context.ApplicationUsers
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

            var departments = await _context.Department.Select(d => d.Name).Distinct().ToListAsync();

            ViewBag.Supervisors     = supervisors;
            ViewBag.ApplicantCounts = applicantCounts;
            ViewBag.MyRequests      = myRequests;
            ViewBag.CurrentUser     = user;
            ViewBag.Departments     = departments;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupervisorRequest(int supervisorId, string matricNumber, string program, string phoneNumber, string sessionSemester, string projectTopic, string? requestMessage)
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

            if (string.IsNullOrWhiteSpace(matricNumber) || !System.Text.RegularExpressions.Regex.IsMatch(matricNumber, @"^[pP]\d{8}$"))
            {
                TempData["Error"] = "Please provide a valid Matric Number starting with 'P' followed by 8 numbers (e.g., P210244501).";
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

            // Duplicate Matric Number check — no other student should share the same ID
            var duplicateMatric = await _context.ApplicationUsers
                .AnyAsync(u => u.UserType == "Student"
                            && u.Id != user.Id
                            && u.EmployeeId != null
                            && u.EmployeeId.ToLower() == matricNumber.ToLower());
            if (duplicateMatric)
            {
                TempData["Error"] = $"Matric ID '{matricNumber}' is already registered to another student. Please use your own matric ID.";
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
                ProjectTopic    = projectTopic,
                RequestMessage  = requestMessage
            };

            // Update user profile with latest provided data
            user.EmployeeId = matricNumber;
            user.Phone = phoneNumber;
            user.Programme = program;
            user.AcademicSession = sessionSemester;
            _context.ApplicationUsers.Update(user);

            _context.SupervisorRequests.Add(request);
            await _context.SaveChangesAsync();

            // Fetch supervisor to get their name for the activity log
            var supervisor = await _context.ApplicationUsers.FindAsync(supervisorId);
            string supervisorName = supervisor != null ? supervisor.FullName : supervisorId.ToString();

            await LogActivityAsync(user.Id, "General", "Supervisor Request Sent", $"Sent request to {supervisorName}");
            await AddNotificationAsync(user.Id, "General", "Request Submitted", "Your supervision request has been sent.");

            // Notify Supervisor
            if (supervisor != null)
            {
                await AddNotificationAsync(supervisor.Id, "SupervisionRequest", "New Student Request", $"{user.FullName} has requested you as their supervisor for: {projectTopic}.", request.Id);
                
                if (!string.IsNullOrEmpty(supervisor.Email))
                {
                    string subject = "New FYP Supervision Request";
                    string body = $"<p>Dear {supervisor.FullName},</p><p>You have received a new FYP supervision request from <b>{user.FullName}</b>.</p><p><b>Topic:</b> {projectTopic}</p><p>Please log in to the FYP Tracker portal to approve or reject this request.</p>";
                    _emailService.Send(supervisor.Email, subject, body);
                }

                // --- NEW: SignalR Real-Time Notification to Supervisor ---
                await _hubContext.Clients.Group($"User_{supervisorId}").SendAsync("ReceiveNewSupervisorRequest", user.FullName, projectTopic);
            }

            TempData["Success"] = "Your supervisor request has been submitted successfully! Please wait for their approval.";
            return RedirectToAction(nameof(SelectSupervisor));
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitDigitalForm(
            int milestoneId, string formType, string? title, string? detailsJson, IFormFile? attachedFile, List<IFormFile>? attachedFiles,
            string? problemStatement, string? objective1, string? objective2, string? objective3,
            string? scope, string? outcome, string? references,
            DateTime? startDate, DateTime? endDate, string? workDone, string? challenges, string? nextPeriodPlan, string? pctComplete)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var allFiles = new List<IFormFile>();
            if (attachedFile != null && attachedFile.Length > 0) allFiles.Add(attachedFile);
            if (attachedFiles != null && attachedFiles.Any()) allFiles.AddRange(attachedFiles.Where(f => f.Length > 0));

            var savedFileRecords = new List<FYPSubmissionFile>();
            if (allFiles.Any())
            {
                // Validate all files BEFORE proceeding
                var allowedExt = new[] { ".pdf" };
                foreach (var file in allFiles)
                {
                    var extCheck = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExt.Contains(extCheck))
                    {
                        TempData["Error"] = "Only PDF files are allowed.";
                        return RedirectToAction("Forms");
                    }
                    if (file.Length > 20 * 1024 * 1024)
                    {
                        TempData["Error"] = "File size must not exceed 20 MB.";
                        return RedirectToAction("Forms");
                    }
                }

                // Remove existing files for this milestone to prevent confusion
                var existingFiles = await _context.FYPSubmissionFiles.Where(f => f.MilestoneId == milestoneId).ToListAsync();
                if (existingFiles.Any())
                {
                    foreach (var f in existingFiles)
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", f.FilePath.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            try { System.IO.File.Delete(oldFilePath); } catch { /* Ignore locked file errors */ }
                        }
                    }
                    _context.FYPSubmissionFiles.RemoveRange(existingFiles);
                }

                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/submissions");
                Directory.CreateDirectory(uploadDir);

                foreach (var file in allFiles)
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var uniqueName = $"{Guid.NewGuid()}{ext}";
                    var path = Path.Combine(uploadDir, uniqueName);
                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    savedFileRecords.Add(new FYPSubmissionFile
                    {
                        MilestoneId = milestoneId,
                        FileName = file.FileName,
                        FilePath = "/uploads/submissions/" + uniqueName,
                        FileSize = file.Length,
                        UploadedBy = user.Id,
                        UploadedDate = DateTime.Now
                    });
                }
            }

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone != null)
            {
                if (milestone.RejectionCount >= 3 && (formType == "Initial Project Allocation Form (IAF)" || milestone.Title.Contains("IAF")))
                {
                    TempData["Error"] = "This form has been rejected 3 times and is now locked. Kindly arrange a meeting with your supervisor to discuss the necessary revisions.";
                    return RedirectToAction("Forms");
                }
                if (formType == "Progress Report Form")
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Start Period", startDate?.ToString("dd MMM yyyy") ?? ""},
                        {"End Period", endDate?.ToString("dd MMM yyyy") ?? ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                    title = "Progress Report";
                }
                else if (formType == "Initial Project Allocation Form (IAF)")
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Scope of Project", scope ?? ""},
                        {"Problem Statement", problemStatement ?? ""},
                        {"Objective 1", objective1 ?? ""},
                        {"Objective 2", objective2 ?? ""},
                        {"Objective 3", objective3 ?? ""},
                        {"Agreed Supervisor Name", Request.Form.TryGetValue("supervisorName", out var supName) ? supName.ToString() : ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                    title = "Initial Project Allocation Form (IAF)";

                    // Also save these directly to the Project record
                    milestone.Project.ProblemStatement = problemStatement;
                    milestone.Project.Objective1 = objective1;
                    milestone.Project.Objective2 = objective2;
                    milestone.Project.Objective3 = objective3;
                    milestone.Project.Scope = scope;
                }
                else if (formType == "Proposal Form")
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Project Title", title ?? ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                }
                else if (formType == "Final Submission Form")
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Project Title", title ?? ""},
                        {"Abstract", Request.Form.TryGetValue("abstract", out var abs) ? abs.ToString() : ""},
                        {"Acknowledgements", Request.Form.TryGetValue("acknowledgements", out var ack) ? ack.ToString() : ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                }

                milestone.Status = "Submitted";
                milestone.SubmittedDate = DateTime.Now;
                milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                
                // Save specific proposal details directly to the Project record
                if (formType == "Proposal Form")
                {
                    if (!string.IsNullOrEmpty(title)) milestone.Project.Title = title;
                }
                
                if (savedFileRecords.Any())
                {
                    _context.FYPSubmissionFiles.AddRange(savedFileRecords);
                }
                
                await _context.SaveChangesAsync();
                
                await LogActivityAsync(user.Id, "Submission", "Form Submitted", $"Submitted {formType} for {milestone.Title}");
                if (milestone.Project.SupervisorId.HasValue)
                {
                    await AddNotificationAsync(milestone.Project.SupervisorId.Value, "Submission", "Form Submitted", $"Student {user.FullName} submitted {formType} for {milestone.Title}", milestoneId);
                    
                    // --- SignalR Real-Time Notification ---
                    await _hubContext.Clients.Group($"User_{milestone.Project.SupervisorId.Value}").SendAsync("ReceiveMilestoneUpdate", "New Milestone Submission", $"Student {user.FullName} submitted {formType} for {milestone.Title}.");
                }
            }

            TempData["Success"] = "Your form has been submitted successfully!";
            return RedirectToAction("Forms");
        }

        [HttpGet]
        public async Task<IActionResult> ViewFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var file = await _context.FYPSubmissionFiles
                .Include(f => f.Milestone)
                .ThenInclude(m => m.Project)
                .FirstOrDefaultAsync(f => f.Id == fileId);

            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

            // Ensure the student can only view files related to their project
            if (file.Milestone?.Project?.StudentId != user.Id && file.UploadedBy != user.Id)
            {
                return Unauthorized("You do not have permission to view this file.");
            }

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            string mimeType = "application/octet-stream";
            
            if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

            var physicalPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", file.FilePath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(physicalPath)) return NotFound("File not found on disk.");

            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{file.FileName}\"");
            return PhysicalFile(physicalPath, mimeType);
        }

        // ══════════════════════════════════════════════════════
        //  FORMS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Forms()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            ViewBag.CurrentUser = user;

            var globalIaf = await _context.FYPMilestones
                .Where(m => m.Title == "Initial Project Allocation Form (IAF)" && m.DueDate != null)
                .OrderByDescending(m => m.DueDate)
                .FirstOrDefaultAsync();

            if (globalIaf != null && globalIaf.DueDate.HasValue)
            {
                ViewBag.IAFDeadline = globalIaf.DueDate.Value.ToString("dd MMMM yyyy") + " before 11.59pm";
            }
            else
            {
                ViewBag.IAFDeadline = "No deadline set yet.";
            }

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project != null)
            {
                var hasIaf = project.Milestones.Any(m => (m.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase)) && !m.Title.Contains("Industrial", StringComparison.OrdinalIgnoreCase));
                if (!hasIaf)
                {
                    bool shouldAutoApprove = project.Milestones.Any(m => m.Status == "Approved" || m.Status == "Submitted");
                    var iaf = new FYPMilestone
                    {
                        FYPProjectId = project.Id,
                        Title = "Initial Project Allocation Form (IAF)",
                        Description = "Prerequisite Form for FYP Registration and allocation.",
                        DueDate = DateTime.Now.AddDays(14),
                        Status = shouldAutoApprove ? "Approved" : "Pending",
                        Remarks = shouldAutoApprove ? "Auto-approved for existing active project" : null,
                        CreatedDate = DateTime.Now
                    };
                    _context.FYPMilestones.Add(iaf);
                    await _context.SaveChangesAsync();

                    // Reload project with the new milestone
                    project = await _context.FYPProjects
                        .Include(p => p.Milestones)
                            .ThenInclude(m => m.SubmissionFiles)
                        .FirstOrDefaultAsync(p => p.StudentId == user.Id);
                }
            }

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
            ViewBag.StartHour = 8;
            ViewBag.EndHour = 17;

            var firstMeeting = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Postponed")
                .OrderBy(m => m.CreatedDate)
                .FirstOrDefaultAsync();
                
            ViewBag.FixedTime = firstMeeting?.MeetingTime;

            // Auto-Expire Stale Pending Meetings
            var staleMeetings = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status == "Pending" && m.MeetingDate < DateTime.Today)
                .ToListAsync();

            if (staleMeetings.Any())
            {
                foreach (var stale in staleMeetings)
                {
                    stale.Status = "Expired";
                    stale.SupervisorNote = "This meeting request has expired because the date has passed without a response.";
                }
                await _context.SaveChangesAsync();
            }

            var meetings = await _context.MeetingBookings
                .Include(m => m.Supervisor)
                .Include(m => m.MeetingLog)
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

            // Date must be today or in the future
            if (parsedDate.Date < DateTime.Today)
            {
                TempData["Error"] = "Meeting date cannot be in the past.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Time is required
            if (string.IsNullOrWhiteSpace(meetingTime))
            {
                TempData["Error"] = "Please select a time slot for your meeting.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Reason/Agenda required
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            {
                TempData["Error"] = "Please provide a reason or agenda (at least 5 characters).";
                return RedirectToAction(nameof(BookMeeting));
            }

            var startOfWeek = parsedDate.Date.AddDays(-(int)parsedDate.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);
            var existingThisWeek = await _context.MeetingBookings
                .AnyAsync(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Postponed" && m.MeetingDate >= startOfWeek && m.MeetingDate < endOfWeek);

            if (existingThisWeek)
            {
                TempData["Error"] = "You can only book one meeting per week.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var firstMeeting = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status == "Approved")
                .OrderBy(m => m.CreatedDate)
                .FirstOrDefaultAsync();

            if (firstMeeting != null && !string.IsNullOrEmpty(firstMeeting.MeetingTime))
            {
                meetingTime = firstMeeting.MeetingTime;
            }

            // Smart Double-Booking Prevention
            var isSlotTaken = await _context.MeetingBookings.AnyAsync(m => 
                m.SupervisorId == supervisorId && 
                m.MeetingDate.Date == parsedDate.Date && 
                m.MeetingTime == meetingTime &&
                (m.Status == "Approved" || m.Status == "Pending"));

            if (isSlotTaken)
            {
                TempData["Error"] = "This time slot was just booked by another student. Please select a different time.";
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

            _context.FYPNotifications.Add(new FYPNotification
            {
                RecipientId = supervisorId,
                Type = "MeetingRequest",
                Title = "New Meeting Request",
                Message = $"Student {user.FullName} requested a meeting on {parsedDate:dd MMM yyyy} at {meetingTime}. Reason: {(string.IsNullOrEmpty(reason) ? "None provided" : reason)}",
                RelatedRecordId = booking.Id
            });
            await _context.SaveChangesAsync();

            var supervisor = await _context.ApplicationUsers.FindAsync(supervisorId);
            string supName = supervisor?.FullName ?? "your supervisor";

            if (supervisor != null && !string.IsNullOrEmpty(supervisor.Email))
            {
                string supSubject = "New FYP Meeting Booking Request";
                string supBody = $"<p>Dear {supervisor.FullName},</p><p>You have a new meeting booking request from <strong>{user.FullName}</strong>.</p><ul><li><strong>Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>Time:</strong> {meetingTime}</li><li><strong>Reason:</strong> {reason}</li></ul><p>Please log in to the FYP Tracker system to approve or reject this request.</p>";
                _emailService.Send(supervisor.Email, supSubject, supBody);
            }
            
            // --- SignalR Real-Time Notification ---
            await _hubContext.Clients.Group($"User_{supervisorId}").SendAsync("ReceiveMeetingUpdate", "New Meeting Request", $"Student {user.FullName} requested a meeting on {parsedDate:dd MMM yyyy} at {meetingTime}.");

            if (!string.IsNullOrEmpty(user.Email))
            {
                string stuSubject = "Meeting Booking Request Submitted";
                string stuBody = $"<p>Dear {user.FullName},</p><p>Your meeting request with <strong>{supName}</strong> has been successfully submitted and is currently pending approval.</p><ul><li><strong>Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>Time:</strong> {meetingTime}</li><li><strong>Reason:</strong> {reason}</li></ul><p>You will be notified once the supervisor responds.</p>";
                _emailService.Send(user.Email, stuSubject, stuBody);
            }

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

            var firstMeeting = await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Postponed")
                .OrderBy(m => m.CreatedDate)
                .FirstOrDefaultAsync();

            if (firstMeeting != null && !string.IsNullOrEmpty(firstMeeting.MeetingTime))
            {
                meetingTime = firstMeeting.MeetingTime;
            }

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id);

            if (meeting != null)
            {
                // Create a completely new booking so the old one (e.g. Rejected) stays in history
                var newBooking = new MeetingBooking
                {
                    StudentId = user.Id,
                    SupervisorId = meeting.SupervisorId,
                    MeetingDate = parsedDate,
                    MeetingDay = parsedDate.DayOfWeek.ToString(),
                    MeetingTime = meetingTime,
                    Reason = string.IsNullOrEmpty(meeting.Reason) ? "Rescheduled meeting" : meeting.Reason,
                    Status = "Pending",
                    CreatedDate = DateTime.Now
                };
                
                _context.MeetingBookings.Add(newBooking);

                // Mark any previous unread notifications for this meeting as read
                var oldNotifs = await _context.FYPNotifications
                    .Where(n => n.RelatedRecordId == meeting.Id && n.Type == "MeetingRequest" && !n.IsRead)
                    .ToListAsync();
                foreach (var oldNotif in oldNotifs)
                {
                    oldNotif.IsRead = true;
                    oldNotif.ReadDate = DateTime.Now;
                }

                // Create a new notification for the supervisor with the new details
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = meeting.SupervisorId,
                    Type = "MeetingRequest",
                    Title = "Meeting Rescheduled",
                    Message = $"Student {user.FullName} booked a new meeting request to {parsedDate:dd MMM yyyy} at {meetingTime}. Reason: {(string.IsNullOrEmpty(newBooking.Reason) ? "None provided" : newBooking.Reason)}",
                    RelatedRecordId = newBooking.Id // Note: Id will be assigned after SaveChanges, but it's okay for now or we can save first
                });

                await _context.SaveChangesAsync();

                // Update the related record ID after save
                var latestNotif = await _context.FYPNotifications.OrderByDescending(n => n.Id).FirstOrDefaultAsync(n => n.RecipientId == meeting.SupervisorId && n.Type == "MeetingRequest");
                if (latestNotif != null) {
                    latestNotif.RelatedRecordId = newBooking.Id;
                    await _context.SaveChangesAsync();
                }

                await LogActivityAsync(user.Id, "MeetingUpdate", "Meeting Rescheduled", 
                    $"Meeting booked for {parsedDate:dd MMM} at {meetingTime}.");
                
                var supervisor = await _context.ApplicationUsers.FindAsync(meeting.SupervisorId);
                string supName = supervisor?.FullName ?? "your supervisor";

                if (supervisor != null && !string.IsNullOrEmpty(supervisor.Email))
                {
                    string supSubject = "FYP Meeting Rescheduled by Student";
                    string supBody = $"<p>Dear {supervisor.FullName},</p><p><strong>{user.FullName}</strong> has rescheduled a meeting.</p><ul><li><strong>New Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>New Time:</strong> {meetingTime}</li></ul><p>Please log in to the FYP Tracker system to re-approve this request.</p>";
                    _emailService.Send(supervisor.Email, supSubject, supBody);
                }
                
                // --- SignalR Real-Time Notification ---
                await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", "Meeting Rescheduled", $"Student {user.FullName} rescheduled a meeting to {parsedDate:dd MMM yyyy} at {meetingTime}.");

                if (!string.IsNullOrEmpty(user.Email))
                {
                    string stuSubject = "Meeting Rescheduled Successfully";
                    string stuBody = $"<p>Dear {user.FullName},</p><p>Your meeting with <strong>{supName}</strong> has been rescheduled to {parsedDate:dd MMM yyyy} at {meetingTime} and is pending approval.</p>";
                    _emailService.Send(user.Email, stuSubject, stuBody);
                }

                TempData["Success"] = "Meeting rescheduled successfully! Awaiting supervisor re-approval.";
            }

            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMeetingLogStudent(
            int meetingId, 
            IFormFile logbookPdf,
            DateTime? nextMeetingDate,
            string? nextMeetingTime)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var meeting = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && m.Status == "Approved");

            if (meeting == null)
            {
                TempData["Error"] = "Meeting not found or not approved.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.MeetingLog != null && meeting.MeetingLog.LogStatus == "Signed")
            {
                TempData["Error"] = "Progress logbook is already signed by the supervisor.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (nextMeetingDate.HasValue)
            {
                // Inherit time from the current meeting or default to "10:00"
                string defaultTime = meeting.MeetingTime ?? "10:00";
                
                // Prevent duplicate bookings on the exact same date
                bool duplicateExists = await _context.MeetingBookings
                    .AnyAsync(m => m.StudentId == user.Id && m.MeetingDate.Date == nextMeetingDate.Value.Date && m.Status != "Rejected" && m.Status != "Postponed");

                if (!duplicateExists)
                {
                    // Instantly generate a new Meeting Booking so it appears in the list.
                    var newBooking = new MeetingBooking
                    {
                        StudentId = user.Id,
                        SupervisorId = meeting.SupervisorId,
                        MeetingDate = nextMeetingDate.Value,
                        MeetingDay = nextMeetingDate.Value.DayOfWeek.ToString(),
                        MeetingTime = defaultTime,
                        Reason = "Follow-up meeting generated from logbook to confirm next meeting",
                        Status = "Pending",
                        CreatedDate = DateTime.Now
                    };
                    _context.MeetingBookings.Add(newBooking);
                }
            }

            if (meeting.MeetingLog == null)
            {
                meeting.MeetingLog = new MeetingLog
                {
                    MeetingBookingId = meeting.Id,
                    NextMeetingDate = nextMeetingDate,
                    LogStatus = "Draft",
                    CreatedDate = DateTime.Now
                };
            }
            else
            {
                meeting.MeetingLog.NextMeetingDate = nextMeetingDate;
                meeting.MeetingLog.LogStatus = "Draft";
            }

            if (logbookPdf != null && logbookPdf.Length > 0)
            {
                var extension = Path.GetExtension(logbookPdf.FileName).ToLower();
                if (extension != ".pdf")
                {
                    TempData["Error"] = "Only PDF files are allowed for the logbook.";
                    return RedirectToAction(nameof(BookMeeting));
                }
                
                string uniqueFileName = $"logbook_student_{meeting.StudentId}_meeting_{meeting.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "logbooks");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await logbookPdf.CopyToAsync(fileStream);
                }

                meeting.MeetingLog.PdfLogbookPath = $"/uploads/logbooks/{uniqueFileName}";
            }

            await _context.SaveChangesAsync();
            await LogActivityAsync(user.Id, "MeetingLog", "Progress Logbook Drafted", $"You submitted a progress logbook draft for the meeting on {meeting.MeetingDate:dd MMM}.");
            
            // --- SignalR Real-Time Notification ---
            await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", "Meeting Log Submitted", $"Student {user.FullName} submitted a progress logbook for review.");
            
            TempData["Success"] = "Progress logbook successfully submitted to supervisor for review!";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpGet]
        public async Task<IActionResult> ViewMeetingLog(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var log = await _context.MeetingLogs
                .Include(l => l.MeetingBooking)
                .ThenInclude(mb => mb.Supervisor)
                .Include(l => l.MeetingBooking.Student)
                .FirstOrDefaultAsync(l => l.Id == id && l.MeetingBooking.StudentId == user.Id);

            if (log == null || log.LogStatus != "Signed") return NotFound();
            
            if (!string.IsNullOrEmpty(log.SignedPdfLogbookPath))
                return Redirect(log.SignedPdfLogbookPath);
            if (!string.IsNullOrEmpty(log.PdfLogbookPath))
                return Redirect(log.PdfLogbookPath);

            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == log.MeetingBooking.StudentId);
            ViewBag.ProjectName = project?.Title ?? "FYP Project";

            return View("~/Views/Shared/MeetingLogPrintable.cshtml", log);
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

        [HttpGet]
        public async Task<IActionResult> CheckDuplicateMatric(string matricNumber)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || string.IsNullOrWhiteSpace(matricNumber))
            {
                return Json(new { isDuplicate = false });
            }

            var duplicateMatric = await _context.ApplicationUsers
                .AnyAsync(u => u.UserType == "Student"
                            && u.Id != user.Id
                            && u.EmployeeId != null
                            && u.EmployeeId.ToLower() == matricNumber.ToLower());

            return Json(new { isDuplicate = duplicateMatric });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var file = await _context.FYPSubmissionFiles.FindAsync(fileId);
            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

            // Security check: ensure the file belongs to this student's project
            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == user.Id);
            if (project == null) return Unauthorized();
            
            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            string mimeType = "application/octet-stream";
            
            if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

            var physicalPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", file.FilePath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(physicalPath)) return NotFound("File not found on disk.");

            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{file.FileName}\"");
            return PhysicalFile(physicalPath, mimeType);
        }

        // ── Download FYP Proposal Word Template ──────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DownloadProposalTemplate()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            using var ms = new System.IO.MemoryStream();

            var doc = new NPOI.XWPF.UserModel.XWPFDocument();

            // ── Helper to add a styled paragraph ──
            System.Action<string, bool, int, string, bool> addPara = (text, bold, fontSize, color, center) =>
            {
                var para = doc.CreateParagraph();
                if (center) para.Alignment = NPOI.XWPF.UserModel.ParagraphAlignment.CENTER;
                var run = para.CreateRun();
                run.SetText(text);
                run.IsBold = bold;
                run.FontSize = fontSize;
                if (!string.IsNullOrEmpty(color)) run.SetColor(color);
                run.FontFamily = "Times New Roman";
            };

            System.Action<string> addBlankLine = (_) =>
            {
                var p = doc.CreateParagraph();
                var r = p.CreateRun();
                r.SetText("");
                r.FontFamily = "Times New Roman";
                r.FontSize = 12;
            };

            System.Action<string, string> addLabelValue = (label, value) =>
            {
                var table = doc.CreateTable(1, 2);
                table.Width = 5000;
                table.GetCTTbl().tblPr.tblW.type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
                table.GetCTTbl().tblPr.tblW.w = "5000";

                var cell0 = table.GetRow(0).GetCell(0);
                cell0.GetCTTc().AddNewTcPr().AddNewTcW().w = "1500";
                cell0.GetCTTc().AddNewTcPr().AddNewTcW().type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;

                var cell1 = table.GetRow(0).GetCell(1);
                cell1.GetCTTc().AddNewTcPr().AddNewTcW().w = "3500";
                cell1.GetCTTc().AddNewTcPr().AddNewTcW().type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
                
                // Style label cell
                var p0 = cell0.Paragraphs.Count > 0 ? cell0.Paragraphs[0] : cell0.AddParagraph();
                var r0 = p0.Runs.Count > 0 ? p0.Runs[0] : p0.CreateRun();
                r0.SetText(label);
                r0.IsBold = true;
                r0.FontFamily = "Times New Roman";
                r0.FontSize = 12;
                
                // Style value cell
                var p1 = cell1.Paragraphs.Count > 0 ? cell1.Paragraphs[0] : cell1.AddParagraph();
                var r1 = p1.Runs.Count > 0 ? p1.Runs[0] : p1.CreateRun();
                r1.SetText(value ?? "");
                r1.FontFamily = "Times New Roman";
                r1.FontSize = 12;
            };

            // ─── Cover / Header ───────────────────────────────────────────────
            addPara("FACULTY OF COMPUTING AND INFORMATICS", true, 13, "000080", true);
            addPara("FINAL YEAR PROJECT (FYP)", true, 13, "000080", true);
            addPara("PROPOSAL FORM", true, 14, "000080", true);
            addBlankLine("");
            addPara("Academic Year: " + DateTime.Now.Year + "/" + (DateTime.Now.Year + 1), false, 12, "", true);
            addBlankLine("");

            // ─── Section 1: Student Information ──────────────────────────────
            addPara("SECTION 1: STUDENT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Student Name:", user.FullName ?? "");
            addLabelValue("Student ID / Matric No.:", user.EmployeeId ?? "");
            addLabelValue("Programme:", "");
            addLabelValue("Semester:", "");
            addLabelValue("Contact Email:", user.Email ?? "");
            addBlankLine("");

            // ─── Section 2: Project Information ──────────────────────────────
            addPara("SECTION 2: PROJECT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Project Title:", project?.Title ?? "");
            addLabelValue("Scope / Category:", "");
            addBlankLine("");

            // ─── Section 3: Problem Statement ────────────────────────────────
            addPara("SECTION 3: PROBLEM STATEMENT", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Supported with statistics and references. Include in-text citations.)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 0; i < 8; i++) addBlankLine("");

            // ─── Section 4: Objectives ───────────────────────────────────────
            addPara("SECTION 4: PROJECT OBJECTIVES", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Use action verbs. Each objective must be measurable.)", false, 10, "888888", false);
            addBlankLine("");
            addPara("1.", false, 12, "", false);
            addBlankLine("");
            addPara("2.", false, 12, "", false);
            addBlankLine("");
            addPara("3.", false, 12, "", false);
            addBlankLine("");

            // ─── Section 5: Scope ─────────────────────────────────────────────
            addPara("SECTION 5: SCOPE OF PROJECT", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            for (int i = 0; i < 5; i++) addBlankLine("");

            // ─── Section 6: Methodology ──────────────────────────────────────
            addPara("SECTION 6: METHODOLOGY", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Describe your development methodology, tools and technologies.)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 0; i < 5; i++) addBlankLine("");

            // ─── Section 7: Timeline ─────────────────────────────────────────
            addPara("SECTION 7: PROJECT TIMELINE", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            // Timeline table
            var tbl = doc.CreateTable(6, 3);
            tbl.Width = 5000;
            tbl.GetCTTbl().tblPr.tblW.type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
            tbl.GetCTTbl().tblPr.tblW.w = "5000";

            string[] headers = { "Phase / Milestone", "Description", "Target Date" };
            string[] phases = { "Phase 1", "Phase 2", "Phase 3", "Phase 4", "Phase 5" };
            for (int c = 0; c < 3; c++)
            {
                var cell = tbl.GetRow(0).GetCell(c);
                cell.GetCTTc().AddNewTcPr().AddNewTcW().w = c == 1 ? "2000" : "1500";
                cell.GetCTTc().AddNewTcPr().AddNewTcW().type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
                
                var p = cell.Paragraphs.Count > 0 ? cell.Paragraphs[0] : cell.AddParagraph();
                var run = p.Runs.Count > 0 ? p.Runs[0] : p.CreateRun();
                run.SetText(headers[c]);
                run.IsBold = true;
                run.FontFamily = "Times New Roman";
                run.FontSize = 11;
            }
            for (int r = 1; r <= 5; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    var cell = tbl.GetRow(r).GetCell(c);
                    var p = cell.Paragraphs.Count > 0 ? cell.Paragraphs[0] : cell.AddParagraph();
                    var run = p.Runs.Count > 0 ? p.Runs[0] : p.CreateRun();
                    run.SetText(c == 0 ? phases[r - 1] : "");
                    run.FontFamily = "Times New Roman";
                    run.FontSize = 11;
                }
            }
            addBlankLine("");

            // ─── Section 8: References ───────────────────────────────────────
            addPara("SECTION 8: REFERENCES", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(APA 7th Edition format)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 1; i <= 5; i++) { addPara($"[{i}]", false, 12, "", false); addBlankLine(""); }

            // ─── Section 9: Supervisor Agreement ─────────────────────────────
            addPara("SECTION 9: SUPERVISOR AGREEMENT", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Supervisor Name:", project?.Supervisor?.FullName ?? "");
            addLabelValue("Supervisor Email:", project?.Supervisor?.Email ?? "");
            addBlankLine("");
            addPara("I confirm that I have reviewed and agreed to supervise this project.", false, 12, "", false);
            addBlankLine("");
            addLabelValue("Supervisor Signature:", "");
            addLabelValue("Date:", "");
            addBlankLine("");
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addLabelValue("Student Signature:", "");
            addLabelValue("Date:", "");
            addBlankLine("");

            // ─── Footer note ─────────────────────────────────────────────────
            addPara("Generated by FYP Tracking System  |  " + DateTime.Now.ToString("dd MMMM yyyy"), false, 9, "AAAAAA", true);

            doc.Write(ms);
            
            var fileName = $"FYP_Proposal_Template_{(user.EmployeeId ?? "Student").Replace(" ", "_")}.docx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);
        }

        // ── Download FYP Progress Report Word Template ───────────────────────
        [HttpGet]
        public async Task<IActionResult> DownloadProgressTemplate(int? milestoneId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            string milestoneTitle = "PROGRESS REPORT";
            if (milestoneId.HasValue)
            {
                var milestone = await _context.FYPMilestones.FindAsync(milestoneId.Value);
                if (milestone != null) milestoneTitle = milestone.Title.ToUpper();
            }

            using var ms = new System.IO.MemoryStream();
            var doc = new NPOI.XWPF.UserModel.XWPFDocument();

            System.Action<string, bool, int, string, bool> addPara = (text, bold, fontSize, color, center) =>
            {
                var para = doc.CreateParagraph();
                if (center) para.Alignment = NPOI.XWPF.UserModel.ParagraphAlignment.CENTER;
                var run = para.CreateRun();
                run.SetText(text);
                run.IsBold = bold;
                run.FontSize = fontSize;
                if (!string.IsNullOrEmpty(color)) run.SetColor(color);
                run.FontFamily = "Times New Roman";
            };

            System.Action<string> addBlankLine = (_) =>
            {
                var p = doc.CreateParagraph();
                var r = p.CreateRun();
                r.SetText("");
                r.FontFamily = "Times New Roman";
                r.FontSize = 12;
            };

            System.Action<string, string> addLabelValue = (label, value) =>
            {
                var table = doc.CreateTable(1, 2);
                table.Width = 5000;
                table.GetCTTbl().tblPr.tblW.type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
                table.GetCTTbl().tblPr.tblW.w = "5000";

                var cell0 = table.GetRow(0).GetCell(0);
                cell0.GetCTTc().AddNewTcPr().AddNewTcW().w = "1500";
                cell0.GetCTTc().AddNewTcPr().AddNewTcW().type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;

                var cell1 = table.GetRow(0).GetCell(1);
                cell1.GetCTTc().AddNewTcPr().AddNewTcW().w = "3500";
                cell1.GetCTTc().AddNewTcPr().AddNewTcW().type = NPOI.OpenXmlFormats.Wordprocessing.ST_TblWidth.pct;
                
                var p0 = cell0.Paragraphs.Count > 0 ? cell0.Paragraphs[0] : cell0.AddParagraph();
                var r0 = p0.Runs.Count > 0 ? p0.Runs[0] : p0.CreateRun();
                r0.SetText(label);
                r0.IsBold = true;
                r0.FontFamily = "Times New Roman";
                r0.FontSize = 12;
                
                var p1 = cell1.Paragraphs.Count > 0 ? cell1.Paragraphs[0] : cell1.AddParagraph();
                var r1 = p1.Runs.Count > 0 ? p1.Runs[0] : p1.CreateRun();
                r1.SetText(value ?? "");
                r1.FontFamily = "Times New Roman";
                r1.FontSize = 12;
            };

            // Cover
            addPara("FACULTY OF COMPUTING AND INFORMATICS", true, 13, "000080", true);
            addPara("FINAL YEAR PROJECT (FYP)", true, 13, "000080", true);
            addPara(milestoneTitle, true, 14, "000080", true);
            addBlankLine("");
            addPara("Academic Year: " + DateTime.Now.Year + "/" + (DateTime.Now.Year + 1), false, 12, "", true);
            addBlankLine("");

            // Section 1
            addPara("SECTION 1: STUDENT & PROJECT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Student Name:", user.FullName ?? "");
            addLabelValue("Student ID / Matric No.:", user.EmployeeId ?? "");
            addLabelValue("Programme:", "");
            addLabelValue("Project Title:", project?.Title ?? "");
            addLabelValue("Supervisor Name:", project?.Supervisor?.FullName ?? "");
            addBlankLine("");

            // Section 2
            addPara("SECTION 2: REPORTING PERIOD", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Start Date:", "");
            addLabelValue("End Date:", "");
            addBlankLine("");

            // Section 3
            addPara("SECTION 3: PROGRESS SUMMARY", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Describe the tasks completed during this reporting period.)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 0; i < 6; i++) addBlankLine("");

            // Section 4
            addPara("SECTION 4: CHALLENGES & ISSUES", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Describe any challenges faced and how they were resolved or planned to be resolved.)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 0; i < 6; i++) addBlankLine("");

            // Section 5
            addPara("SECTION 5: NEXT STEPS & GOALS", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addPara("(Outline the tasks and goals for the next reporting period.)", false, 10, "888888", false);
            addBlankLine("");
            for (int i = 0; i < 6; i++) addBlankLine("");

            // Section 6
            addPara("SECTION 6: SUPERVISOR SIGN-OFF", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addPara("I confirm that I have reviewed the student's progress for this reporting period.", false, 12, "", false);
            addBlankLine("");
            addLabelValue("Supervisor Signature:", "");
            addLabelValue("Date:", "");
            addBlankLine("");
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addLabelValue("Student Signature:", "");
            addLabelValue("Date:", "");
            addBlankLine("");

            addPara("Generated by FYP Tracking System  |  " + DateTime.Now.ToString("dd MMMM yyyy"), false, 9, "AAAAAA", true);

            doc.Write(ms);

            var fileName = $"FYP_Progress_Report_{(user.EmployeeId ?? "Student").Replace(" ", "_")}.docx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);
        }

        // ── Download FYP Final Thesis Word Template ──────────────────────────
        [HttpGet]
        public async Task<IActionResult> DownloadFinalTemplate()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            using var ms = new System.IO.MemoryStream();
            var doc = new NPOI.XWPF.UserModel.XWPFDocument();

            System.Action<string, bool, int, string, bool> addPara = (text, bold, fontSize, color, center) =>
            {
                var para = doc.CreateParagraph();
                if (center) para.Alignment = NPOI.XWPF.UserModel.ParagraphAlignment.CENTER;
                var run = para.CreateRun();
                run.SetText(text);
                run.IsBold = bold;
                run.FontSize = fontSize;
                if (!string.IsNullOrEmpty(color)) run.SetColor(color);
                run.FontFamily = "Times New Roman";
            };

            System.Action<string> addBlankLine = (_) =>
            {
                var p = doc.CreateParagraph();
                var r = p.CreateRun();
                r.SetText("");
                r.FontFamily = "Times New Roman";
                r.FontSize = 12;
            };

            // Title Page
            addBlankLine("");
            addBlankLine("");
            addPara(project?.Title?.ToUpper() ?? "FINAL YEAR PROJECT TITLE", true, 18, "000000", true);
            for (int i = 0; i < 5; i++) addBlankLine("");
            addPara("by", false, 14, "000000", true);
            addBlankLine("");
            addPara(user.FullName?.ToUpper() ?? "STUDENT NAME", true, 14, "000000", true);
            addPara(user.EmployeeId ?? "STUDENT ID", false, 14, "000000", true);
            for (int i = 0; i < 5; i++) addBlankLine("");
            addPara("A thesis submitted in partial fulfillment", false, 12, "000000", true);
            addPara("of the requirements for the degree of", false, 12, "000000", true);
            addBlankLine("");
            addPara("BACHELOR OF SCIENCE", true, 14, "000000", true);
            addBlankLine("");
            addPara("FACULTY OF COMPUTING AND INFORMATICS", true, 14, "000000", true);
            addPara("COVENTRY UNIVERSITY", true, 14, "000000", true);
            for (int i = 0; i < 4; i++) addBlankLine("");
            addPara(DateTime.Now.ToString("MMMM yyyy"), false, 12, "000000", true);

            // Page Break
            var pb = doc.CreateParagraph();
            pb.CreateRun().AddBreak(NPOI.XWPF.UserModel.BreakType.PAGE);

            // Abstract
            addPara("ABSTRACT", true, 14, "000000", true);
            addBlankLine("");
            addPara("Write your abstract here. It should briefly summarize the problem, methodology, findings, and conclusion of your project in about 200-300 words.", false, 12, "000000", false);
            
            pb = doc.CreateParagraph();
            pb.CreateRun().AddBreak(NPOI.XWPF.UserModel.BreakType.PAGE);

            // Acknowledgements
            addPara("ACKNOWLEDGEMENTS", true, 14, "000000", true);
            addBlankLine("");
            addPara("I would like to express my sincere gratitude to my supervisor, " + (project?.Supervisor?.FullName ?? "Supervisor Name") + ", for their continuous support and guidance during this project.", false, 12, "000000", false);

            pb = doc.CreateParagraph();
            pb.CreateRun().AddBreak(NPOI.XWPF.UserModel.BreakType.PAGE);

            // Table of Contents Placeholder
            addPara("TABLE OF CONTENTS", true, 14, "000000", true);
            addBlankLine("");
            addPara("CHAPTER 1: INTRODUCTION", true, 12, "000000", false);
            addPara("CHAPTER 2: LITERATURE REVIEW", true, 12, "000000", false);
            addPara("CHAPTER 3: METHODOLOGY", true, 12, "000000", false);
            addPara("CHAPTER 4: IMPLEMENTATION & RESULTS", true, 12, "000000", false);
            addPara("CHAPTER 5: CONCLUSION & FUTURE WORK", true, 12, "000000", false);
            addPara("REFERENCES", true, 12, "000000", false);
            addPara("APPENDICES", true, 12, "000000", false);

            pb = doc.CreateParagraph();
            pb.CreateRun().AddBreak(NPOI.XWPF.UserModel.BreakType.PAGE);

            // Chapters layout
            addPara("CHAPTER 1: INTRODUCTION", true, 14, "000000", false);
            addBlankLine("");
            addPara("1.1 Background of Study", true, 12, "000000", false);
            addBlankLine("");
            addPara("1.2 Problem Statement", true, 12, "000000", false);
            addBlankLine("");
            addPara("1.3 Project Objectives", true, 12, "000000", false);
            addBlankLine("");
            addPara("1.4 Project Scope", true, 12, "000000", false);

            doc.Write(ms);

            var fileName = $"FYP_Final_Thesis_{(user.EmployeeId ?? "Student").Replace(" ", "_")}.docx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);
        }

        // ── Get Calendar Data API ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetMeetingsCalendarData()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Json(new object[] { });

            var meetings = await _context.MeetingBookings
                .Include(m => m.Supervisor)
                .Where(m => m.StudentId == user.Id && (m.Status == "Approved" || m.Status == "Pending" || m.Status == "Postponed"))
                .ToListAsync();

            var events = meetings.Select(m => {
                string startTimeStr = m.MeetingTime ?? "09:00 AM";
                DateTime startDt;
                if (!DateTime.TryParse($"{m.MeetingDate:yyyy-MM-dd} {startTimeStr}", out startDt))
                    startDt = m.MeetingDate;

                string color = m.Status == "Approved" ? "#10b981" : (m.Status == "Pending" ? "#f59e0b" : "#ef4444");

                return new {
                    id = m.Id,
                    title = $"Meeting with {m.Supervisor?.FullName ?? "Supervisor"}",
                    start = startDt.ToString("yyyy-MM-ddTHH:mm:00"),
                    description = $"Status: {m.Status}\nLocation: {m.Location ?? "TBA"}\nReason: {m.Reason}",
                    color = color,
                    url = Url.Action("BookMeeting", "Student")
                };
            }).ToList();

            return Json(events);
        }
    }
}


