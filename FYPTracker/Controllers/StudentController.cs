using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class StudentController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailService _emailService;

        public StudentController(FYPTrackerContext context, IWebHostEnvironment env, IEmailService emailService)
        {
            _context = context;
            _env     = env;
            _emailService = emailService;
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

            await LogActivityAsync(user.Id, "General", "Supervisor Request Sent", $"Sent request to supervisor {supervisorId}");
            await AddNotificationAsync(user.Id, "General", "Request Submitted", "Your supervision request has been sent.");

            // Notify Supervisor
            var supervisor = await _context.ApplicationUsers.FindAsync(supervisorId);
            if (supervisor != null)
            {
                await AddNotificationAsync(supervisor.Id, "SupervisionRequest", "New Student Request", $"{user.FullName} has requested you as their supervisor for: {projectTopic}.", request.Id);
                
                if (!string.IsNullOrEmpty(supervisor.Email))
                {
                    string subject = "New FYP Supervision Request";
                    string body = $"<p>Dear {supervisor.FullName},</p><p>You have received a new FYP supervision request from <b>{user.FullName}</b>.</p><p><b>Topic:</b> {projectTopic}</p><p>Please log in to the FYP Tracker portal to approve or reject this request.</p>";
                    _emailService.Send(supervisor.Email, subject, body);
                }
            }

            TempData["Success"] = "Supervision request sent successfully!";
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
                        {"End Period", endDate?.ToString("dd MMM yyyy") ?? ""},
                        {"Work Completed", workDone ?? ""},
                        {"Challenges", challenges ?? ""},
                        {"Next Plan", nextPeriodPlan ?? ""},
                        {"Completion", pctComplete ?? ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                    title = $"Progress Report: {pctComplete} Complete";
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
                        {"Project Title", title ?? ""},
                        {"Problem Statement", problemStatement ?? ""},
                        {"Scope of Work", scope ?? ""},
                        {"Expected Outcome", outcome ?? ""}
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
                    milestone.Project.ProblemStatement = problemStatement;
                    milestone.Project.Objective1 = objective1;
                    milestone.Project.Objective2 = objective2;
                    milestone.Project.Objective3 = objective3;
                    milestone.Project.Scope = scope;
                    milestone.Project.ExpectedOutcome = outcome;
                    milestone.Project.References = references;
                }
                
                if (savedFileRecords.Any())
                {
                    _context.FYPSubmissionFiles.AddRange(savedFileRecords);
                }
                
                await _context.SaveChangesAsync();
                
                await LogActivityAsync(user.Id, "Submission", "Form Submitted", $"Submitted {formType} for {milestone.Title}");
                if (milestone.Project.SupervisorId.HasValue)
                    await AddNotificationAsync(milestone.Project.SupervisorId.Value, "Submission", "Form Submitted", $"Student {user.FullName} submitted {formType} for {milestone.Title}", milestoneId);
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
                .Where(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Postponed")
                .OrderBy(m => m.CreatedDate)
                .FirstOrDefaultAsync();

            if (firstMeeting != null && !string.IsNullOrEmpty(firstMeeting.MeetingTime))
            {
                meetingTime = firstMeeting.MeetingTime;
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
                meeting.MeetingDate = parsedDate;
                meeting.MeetingDay  = parsedDate.DayOfWeek.ToString();
                meeting.MeetingTime = meetingTime;
                meeting.Status      = "Pending"; // Set to Pending so supervisor can re-approve
                meeting.RespondedDate = null;
                meeting.SupervisorNote = "Student rescheduled this meeting.";
                
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
                    Message = $"Student {user.FullName} rescheduled their meeting request to {parsedDate:dd MMM yyyy} at {meetingTime}. Reason: {(string.IsNullOrEmpty(meeting.Reason) ? "None provided" : meeting.Reason)}",
                    RelatedRecordId = meeting.Id
                });

                await _context.SaveChangesAsync();

                await LogActivityAsync(user.Id, "MeetingUpdate", "Meeting Rescheduled", 
                    $"Meeting moved to {parsedDate:dd MMM} at {meetingTime}.");
                
                var supervisor = await _context.ApplicationUsers.FindAsync(meeting.SupervisorId);
                string supName = supervisor?.FullName ?? "your supervisor";

                if (supervisor != null && !string.IsNullOrEmpty(supervisor.Email))
                {
                    string supSubject = "FYP Meeting Rescheduled by Student";
                    string supBody = $"<p>Dear {supervisor.FullName},</p><p><strong>{user.FullName}</strong> has rescheduled a meeting.</p><ul><li><strong>New Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>New Time:</strong> {meetingTime}</li></ul><p>Please log in to the FYP Tracker system to re-approve this request.</p>";
                    _emailService.Send(supervisor.Email, supSubject, supBody);
                }

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
            string keyTopics, 
            string actionPoints, 
            string completedActions,
            DateTime? nextMeetingDate)
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
                var firstMeeting = await _context.MeetingBookings
                    .Where(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Postponed")
                    .OrderBy(m => m.CreatedDate)
                    .FirstOrDefaultAsync();

                if (firstMeeting != null && !string.IsNullOrEmpty(firstMeeting.MeetingTime) && TimeSpan.TryParse(firstMeeting.MeetingTime, out var ts))
                {
                    nextMeetingDate = nextMeetingDate.Value.Date.Add(ts);
                }
            }

            if (meeting.MeetingLog == null)
            {
                meeting.MeetingLog = new MeetingLog
                {
                    MeetingBookingId = meeting.Id,
                    KeyTopicsDiscussed = keyTopics,
                    ActionPointsForNextMeeting = actionPoints,
                    CompletedActionsAndNotes = completedActions,
                    NextMeetingDate = nextMeetingDate,
                    LogStatus = "Draft",
                    CreatedDate = DateTime.Now
                };
            }
            else
            {
                meeting.MeetingLog.KeyTopicsDiscussed = keyTopics;
                meeting.MeetingLog.ActionPointsForNextMeeting = actionPoints;
                meeting.MeetingLog.CompletedActionsAndNotes = completedActions;
                meeting.MeetingLog.NextMeetingDate = nextMeetingDate;
                meeting.MeetingLog.LogStatus = "Draft";
            }

            await _context.SaveChangesAsync();
            await LogActivityAsync(user.Id, "MeetingLog", "Progress Logbook Drafted", $"You submitted a progress logbook draft for the meeting on {meeting.MeetingDate:dd MMM}.");
            
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
    }
}


