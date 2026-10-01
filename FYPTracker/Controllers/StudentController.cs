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
    [FYPTracker.Filters.AuthorizeUserType("Student")]
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
                .Include(p => p.AssignedExaminers)
                    .ThenInclude(ae => ae.Examiner)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

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

            // Fetch Supervisor Requests for feedback banner
            var myRequests = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .Where(r => r.StudentId == user.Id)
                .ToListAsync();
            ViewBag.MyRequests = myRequests;

            return View(project);
        }

        // ══════════════════════════════════════════════════════
        //  STAGE 2 - FINAL SUBMISSION
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Stage2()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Feedbacks)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project == null || project.Stage1Grade != "Pass")
            {
                TempData["Error"] = "You must pass Stage 1 before accessing Stage 2.";
                return RedirectToAction("Forms");
            }

            ViewBag.CurrentUser = user;
            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitStage2Milestone(string stepTitle, IFormFile file)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id && p.Status != "Completed");

            if (project == null)
            {
                TempData["Error"] = "Active project not found.";
                return RedirectToAction("Stage2");
            }

            var milestone = project.Milestones.FirstOrDefault(m => m.Title.Equals(stepTitle, StringComparison.OrdinalIgnoreCase));
            if (milestone == null)
            {
                // Create it if it doesn't exist
                milestone = new FYPMilestone
                {
                    FYPProjectId = project.Id,
                    Title = stepTitle,
                    Description = "Stage 2 Final Submission Step",
                    Status = "Submitted",
                    CreatedDate = DateTime.Now,
                    SubmittedDate = DateTime.Now,
                    IsReadyForJointEvaluation = stepTitle.StartsWith("Step 5") // Only ready after step 5 is submitted
                };
                _context.FYPMilestones.Add(milestone);
            }
            else
            {
                milestone.Status = "Submitted";
                milestone.SubmittedDate = DateTime.Now;
                if (stepTitle.StartsWith("Step 5")) milestone.IsReadyForJointEvaluation = true;
                _context.FYPMilestones.Update(milestone);
            }

            await _context.SaveChangesAsync();

            // Handle file upload
            if (file != null && file.Length > 0)
            {
                var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "submissions", user.Id.ToString());
                if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                string filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _context.FYPSubmissionFiles.Add(new FYPSubmissionFile
                {
                    MilestoneId = milestone.Id,
                    FileName = file.FileName,
                    FilePath = $"/uploads/submissions/{user.Id}/{fileName}",
                    UploadedDate = DateTime.Now
                });
                
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = $"{stepTitle} has been successfully submitted.";
            return RedirectToAction("Stage2");
        }


        //  HELPER: current logged-in ApplicationUser
        // ──────────────────────────────────────────────────────
        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var user = HttpContext.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated) return null;

            if (int.TryParse(user.FindFirst("Id")?.Value, out int id))
            {
                return await _context.ApplicationUsers
                    .Include(u => u.EmpDepartment)
                    .FirstOrDefaultAsync(u => u.Id == id);
            }
            return null;
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
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

            // Fetch global FYP key dates (DPP, Exhibition)
            var fypSettings = await _context.FYPSettings.ToListAsync();
            string GetSetting(string key) => fypSettings.FirstOrDefault(s => s.Key == key)?.Value ?? "";
            
            ViewBag.DPPDeadline = GetSetting("DPPDeadline");
            ViewBag.ExhibitionDate = GetSetting("ExhibitionDate");
            ViewBag.ExhibitionTimeStart = GetSetting("ExhibitionTimeStart");
            ViewBag.ExhibitionTimeEnd = GetSetting("ExhibitionTimeEnd");

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

            // 1. Auto-Expiry: Read the configured timeout from FYPSettings (default 3 days)
            //    Note: The SupervisorRequestTimeoutService background worker handles daily bulk
            //    rejection. This inline check is a safety net when the student visits the page.
            var timeoutSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "SupervisorRequestTimeoutDays");
            int timeoutDays = (timeoutSetting != null && int.TryParse(timeoutSetting.Value, out int t) && t > 0) ? t : 3;

            var expired = await _context.SupervisorRequests
                .Where(r => r.Status == "Pending" && r.RequestedDate <= DateTime.Now.AddDays(-timeoutDays))
                .ToListAsync();
            foreach (var e in expired)
            {
                e.Status = "Rejected";
                e.ResponseNote = $"Auto-rejected: No supervisor response within the configured {timeoutDays}-day period.";
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

            // 5. Get Student's Requests (exclude Failed so they can re-apply)
            var myRequests = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .Where(r => r.StudentId == user.Id && r.Status != "Failed")
                .OrderBy(r => r.Priority)
                .ToListAsync();

            var departments = await _context.Department.Select(d => d.Name).Distinct().ToListAsync();

            bool isProfileIncomplete = false;

            ViewBag.Supervisors     = supervisors;
            ViewBag.ApplicantCounts = applicantCounts;
            ViewBag.MyRequests      = myRequests;
            ViewBag.CurrentUser     = user;
            ViewBag.Departments     = departments;
            ViewBag.IsProfileIncomplete = isProfileIncomplete;
            ViewBag.SupervisorRequestTimeoutDays = timeoutDays;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RepeatFYP(string newSession)
        {
            try 
            {
                var user = await GetCurrentUserAsync();
                if (user == null) return RedirectToAction("Login", "Authentication");

                if (!string.IsNullOrEmpty(newSession))
                {
                    user.AcademicSession = newSession;
                    // Intentionally NOT calling _context.Update(user) because user is already tracked
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Academic Session updated to {newSession}. You may now select a new supervisor.";
                }

                return RedirectToAction(nameof(SelectSupervisor));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error updating session: " + ex.Message + (ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : "");
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupervisorRequest(int supervisorId, string matricNumber, string program, string phoneNumber, string sessionSemester, string projectTopic, string? requestMessage)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var myRequests = await _context.SupervisorRequests.Where(r => r.StudentId == user.Id && r.Status != "Failed").ToListAsync();
            
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

            // Read the configured timeout to set the correct ExpiryDate on the new request
            var timeoutSettingForNew = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "SupervisorRequestTimeoutDays");
            int timeoutDaysForNew = (timeoutSettingForNew != null && int.TryParse(timeoutSettingForNew.Value, out int td) && td > 0) ? td : 3;

            var request = new SupervisorRequest
            {
                StudentId       = user.Id,
                SupervisorId    = supervisorId,
                Status          = "Pending",
                RequestedDate   = DateTime.Now,
                ExpiryDate      = DateTime.Now.AddDays(timeoutDaysForNew), // Uses configured timeout, not hardcoded
                Priority        = myRequests.Count(r => r.Status == "Pending") + 1,
                
                MatricNumber    = matricNumber,
                Program         = program,
                PhoneNumber     = phoneNumber,
                SessionSemester = sessionSemester,
                ProjectTopic    = projectTopic,
                RequestMessage  = requestMessage
            };

            // Update user profile with latest provided data
            var dbUser = await _context.ApplicationUsers.FindAsync(user.Id);
            if (dbUser != null)
            {
                dbUser.EmployeeId = matricNumber;
                dbUser.Phone = phoneNumber;
                dbUser.Programme = program;
                dbUser.AcademicSession = sessionSemester;
            }

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
                    try 
                    {
                        _emailService.Send(supervisor.Email, subject, body);
                    }
                    catch (Exception ex)
                    {
                        // Log or ignore email sending failures so the main process still succeeds.
                        Console.WriteLine($"Failed to send email to {supervisor.Email}: {ex.Message}");
                    }
                }

                // --- NEW: SignalR Real-Time Notification to Supervisor ---
                var reqData = new {
                    Id = request.Id,
                    StudentName = user.FullName,
                    Matric = user.EmployeeId,
                    Program = user.Programme,
                    Phone = user.Phone,
                    Session = user.AcademicSession,
                    Topic = projectTopic,
                    Message = request.RequestMessage ?? "",
                    Date = request.RequestedDate.ToString("dd MMM yyyy")
                };
                await _hubContext.Clients.Group($"User_{supervisorId}").SendAsync("ReceiveNewSupervisorRequest", reqData);
            }

            TempData["Success"] = "Your supervisor request has been submitted successfully! Please wait for their approval.";
            return RedirectToAction(nameof(SelectSupervisor));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSupervisorRequest(int requestId, string projectTopic, string? requestMessage)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var request = await _context.SupervisorRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.StudentId == user.Id);
            if (request == null)
            {
                TempData["Error"] = "Request not found.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            if (request.Status != "Rejected")
            {
                TempData["Error"] = "Only rejected requests can be edited and resubmitted.";
                return RedirectToAction(nameof(SelectSupervisor));
            }

            request.ProjectTopic = projectTopic;
            request.RequestMessage = requestMessage;
            request.Status = "Pending";
            request.RequestedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "Supervision", "Supervisor Request Resubmitted", $"Resubmitted request to Supervisor ID: {request.SupervisorId}");
            await AddNotificationAsync(request.SupervisorId, "Supervision", "Supervision Request Resubmitted", $"{user.FullName} has resubmitted their supervision request.", request.Id);

            TempData["Success"] = "Supervisor request resubmitted successfully!";
            return RedirectToAction(nameof(SelectSupervisor));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSubmissionFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var file = await _context.FYPSubmissionFiles.Include(f => f.Milestone).FirstOrDefaultAsync(f => f.Id == fileId && f.UploadedBy == user.Id);
            
            if (file == null)
            {
                return Json(new { success = false, message = "File not found or unauthorized." });
            }

            if (file.Milestone.Status == "Approved" || file.Milestone.Status == "Rejected")
            {
                return Json(new { success = false, message = "Cannot delete file for a graded milestone." });
            }

            try
            {
                // Soft delete
                file.IsDeleted = true;
                file.DeletedDate = DateTime.Now;
                _context.FYPSubmissionFiles.Update(file);
                
                var remainingFiles = await _context.FYPSubmissionFiles.CountAsync(f => f.MilestoneId == file.MilestoneId && f.Id != fileId && !f.IsDeleted);
                if (remainingFiles == 0)
                {
                    file.Milestone.Status = "Pending";
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "File deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to delete file. " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitForJointEvaluation(int milestoneId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone == null || milestone.Status != "Approved")
            {
                TempData["Error"] = "Invalid milestone or it is not yet approved by your supervisor.";
                return RedirectToAction("Forms");
            }

            if (milestone.IsReadyForJointEvaluation)
            {
                TempData["Error"] = "This milestone has already been submitted for joint evaluation.";
                return RedirectToAction("Forms");
            }

            milestone.IsReadyForJointEvaluation = true;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Successfully submitted for Final Joint Evaluation! Your Supervisor and Examiner will now mark it.";
            return RedirectToAction("Forms");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitDigitalForm(
            int milestoneId, string formType, string? title, string? detailsJson, IFormFile? attachedFile, List<IFormFile>? attachedFiles,
            string? problemStatement, string? objective1, string? objective2, string? objective3,
            string? scope, string? outcome, string? references,
            DateTime? startDate, DateTime? endDate, string? workDone, string? challenges, string? nextPeriodPlan, string? pctComplete, string? coventryId, string? revisionNotes)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (!string.IsNullOrEmpty(coventryId) && user.CoventryId != coventryId)
            {
                user.CoventryId = coventryId;
                _context.Update(user);
                // We don't save changes here yet, we'll let the milestone logic save it or we can save it now.
                await _context.SaveChangesAsync();
            }

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone == null)
            {
                TempData["Error"] = "Invalid milestone or unauthorized access.";
                return RedirectToAction("Forms");
            }

            // ── Guard: Prevent modifying graded milestones (except Rejected which needs revision) ──
            if (milestone.Status == "Approved" || milestone.Status == "Completed")
            {
                TempData["Error"] = "This milestone has already been graded and cannot be modified or resubmitted.";
                return RedirectToAction("Forms");
            }

            if (milestone.RejectionCount >= 3 && (formType == "Initial Project Allocation Form (IAF)" || milestone.Title.Contains("IAF")))
            {
                TempData["Error"] = "This form has been rejected 3 times and is now locked. Kindly arrange a meeting with your supervisor to discuss the necessary revisions.";
                return RedirectToAction("Forms");
            }

            var allFiles = new List<IFormFile>();
            if (attachedFile != null && attachedFile.Length > 0) allFiles.Add(attachedFile);
            if (attachedFiles != null && attachedFiles.Any()) allFiles.AddRange(attachedFiles.Where(f => f.Length > 0));

            var savedFileRecords = new List<FYPSubmissionFile>();
            if (allFiles.Any())
            {
                // Validate all files BEFORE proceeding
                var allowedExt = new[] { ".pdf", ".pptx", ".ppt" };
                foreach (var file in allFiles)
                {
                    var extCheck = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExt.Contains(extCheck))
                    {
                        TempData["Error"] = "Only PDF and PPT/PPTX files are allowed.";
                        return RedirectToAction("Forms");
                    }
                    if (file.Length > 20 * 1024 * 1024)
                    {
                        TempData["Error"] = "File size must not exceed 20 MB.";
                        return RedirectToAction("Forms");
                    }
                }

                // Determine the new AttemptNumber
                int newAttemptNumber = 1;
                var existingFiles = await _context.FYPSubmissionFiles.Where(f => f.MilestoneId == milestoneId).ToListAsync();
                if (existingFiles.Any())
                {
                    newAttemptNumber = existingFiles.Max(f => f.AttemptNumber) + 1;
                    // Note: We intentionally do NOT delete old files to preserve version history
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
                        AttemptNumber = newAttemptNumber,
                        RevisionNotes = revisionNotes,
                        UploadedBy = user.Id,
                        UploadedDate = DateTime.Now
                    });
                }
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
                    // Clean up existing prefixes if they accidentally get submitted
                    var cleanObj1 = objective1?.StartsWith("1. To ", StringComparison.OrdinalIgnoreCase) == true ? objective1.Substring(6).TrimStart() : objective1;
                    var cleanObj2 = objective2?.StartsWith("2. To ", StringComparison.OrdinalIgnoreCase) == true ? objective2.Substring(6).TrimStart() : objective2;
                    var cleanObj3 = objective3?.StartsWith("3. To ", StringComparison.OrdinalIgnoreCase) == true ? objective3.Substring(6).TrimStart() : objective3;

                    var finalObj1 = string.IsNullOrWhiteSpace(cleanObj1) ? "" : "1. To " + cleanObj1;
                    var finalObj2 = string.IsNullOrWhiteSpace(cleanObj2) ? "" : "2. To " + cleanObj2;
                    var finalObj3 = string.IsNullOrWhiteSpace(cleanObj3) ? "" : "3. To " + cleanObj3;

                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Scope of Project", scope ?? ""},
                        {"Problem Statement", problemStatement ?? ""},
                        {"Objective 1", finalObj1},
                        {"Objective 2", finalObj2},
                        {"Objective 3", finalObj3},
                        {"Agreed Supervisor Name", Request.Form.TryGetValue("supervisorName", out var supName) ? supName.ToString() : ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                    title = "Initial Project Allocation Form (IAF)";

                    // Also save these directly to the Project record
                    milestone.Project.ProblemStatement = problemStatement;
                    milestone.Project.Objective1 = finalObj1;
                    milestone.Project.Objective2 = finalObj2;
                    milestone.Project.Objective3 = finalObj3;
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
                
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    foreach(var f in savedFileRecords)
                    {
                        var physicalPath = System.IO.Path.Combine(_env.WebRootPath, f.FilePath.TrimStart('/', '\\'));
                        if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
                    }
                    TempData["Error"] = "Failed to save submission to database. Please try again.";
                    return RedirectToAction("Forms");
                }
                
                await LogActivityAsync(user.Id, "Submission", "Form Submitted", $"Submitted {formType} for {milestone.Title}");
                if (milestone.Project.SupervisorId.HasValue)
                {
                    await AddNotificationAsync(milestone.Project.SupervisorId.Value, "Submission", "Form Submitted", $"Student {user.FullName} submitted {formType} for {milestone.Title}", milestoneId);
                    
                    // --- SignalR Real-Time Notification ---
                    var submissionPayload = new {
                        Type = "NewMilestoneSubmission",
                        MilestoneId = milestoneId,
                        StudentName = user.FullName,
                        Session = user.AcademicSession,
                        FormType = formType,
                        Title = "New Milestone Submission",
                        Message = $"Student {user.FullName} submitted {formType} for {milestone.Title}."
                    };
                    await _hubContext.Clients.Group($"User_{milestone.Project.SupervisorId.Value}").SendAsync("ReceiveMilestoneUpdate", submissionPayload);
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

            var physicalPath = System.IO.Path.Combine(_env.WebRootPath, file.FilePath.TrimStart('/', '\\'));
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

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project != null)
            {
                var sessionIaf = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .Where(m => m.Project.AcademicSession == project.AcademicSession && (m.Title == "Initial Project Allocation Form (IAF)" || m.Title.Contains("IAF")) && m.DueDate != null)
                    .OrderByDescending(m => m.DueDate)
                    .FirstOrDefaultAsync();

                if (sessionIaf != null && sessionIaf.DueDate.HasValue)
                {
                    ViewBag.IAFDeadline = sessionIaf.DueDate.Value.ToString("dd MMMM yyyy") + " before 11.59pm";
                }
                else
                {
                    ViewBag.IAFDeadline = "No deadline set yet.";
                }

                var hasIaf = project.Milestones.Any(m => (m.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase)) && !m.Title.Contains("Industrial", StringComparison.OrdinalIgnoreCase));
                if (!hasIaf)
                {
                    bool shouldAutoApprove = project.Milestones.Any(m => m.Status == "Approved" || m.Status == "Submitted");
                    
                    var newDeadline = sessionIaf != null && sessionIaf.DueDate.HasValue 
                        ? sessionIaf.DueDate.Value 
                        : DateTime.Now.AddDays(14);
                        
                    var iaf = new FYPMilestone
                    {
                        FYPProjectId = project.Id,
                        Title = "Initial Project Allocation Form (IAF)",
                        Description = "Prerequisite Form for FYP Registration and allocation.",
                        DueDate = newDeadline,
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
                        .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);
                }
            }
            else
            {
                ViewBag.IAFDeadline = "Please wait for project allocation.";
            }

            bool isStage2Unlocked = false;
            var finalMilestone = project?.Milestones?.FirstOrDefault(m => m.Title.Contains("Final", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase));
            
            // Admin gated unlock check based on FYPSettings
            var finalSubmissionStartSetting = await _context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "FinalSubmissionStart");
            if (finalSubmissionStartSetting != null && DateTime.TryParse(finalSubmissionStartSetting.Value, out DateTime finalSubmissionStart))
            {
                if (DateTime.Now.Date >= finalSubmissionStart.Date)
                {
                    isStage2Unlocked = true;
                }
            }

            ViewBag.Stage2Model = new FYPTracker.ViewModels.Stage2ViewModel 
            { 
                IsStage2Unlocked = isStage2Unlocked,
                MilestoneId = finalMilestone?.Id ?? 0
            };

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

            // ── Check if supervisor has configured any availability ──
            bool supervisorHasAvailability = false;
            if (approvedRequest?.Supervisor != null)
            {
                supervisorHasAvailability = await _context.SupervisorAvailabilities
                    .AnyAsync(a => a.SupervisorId == approvedRequest.Supervisor.Id && a.IsAvailable);
            }
            ViewBag.SupervisorHasAvailability = supervisorHasAvailability;

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

            // ── Guard: Ensure supervisor is actually available on this specific date/time ──
            var availabilities = await _context.SupervisorAvailabilities
                .Where(a => a.SupervisorId == supervisorId)
                .ToListAsync();

            if (!availabilities.Any(a => a.IsAvailable))
            {
                TempData["Error"] = "Your supervisor has not yet configured their available time slots. Please contact them.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == parsedDate.Date);
            var weeklyAvail = overrideAvail == null ? availabilities.FirstOrDefault(a => a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == parsedDate.DayOfWeek.ToString()) : null;
            var activeAvail = overrideAvail ?? weeklyAvail;

            if (activeAvail == null || !activeAvail.IsAvailable)
            {
                TempData["Error"] = "Your supervisor is not available on this date. Please select a different day.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // ── Guard: Ensure student is actually supervised by this supervisor ──
            var isMySupervisor = await _context.SupervisorRequests.AnyAsync(r => r.StudentId == user.Id && r.SupervisorId == supervisorId && r.Status == "Approved");
            if (!isMySupervisor)
            {
                TempData["Error"] = "Unauthorized. You can only book meetings with your assigned supervisor.";
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
            var meetingPayload = new {
                Type = "NewMeetingRequest",
                MeetingId = booking.Id,
                StudentId = user.Id,
                StudentName = user.FullName,
                StudentMatric = user.EmployeeId,
                MeetingDate = booking.MeetingDate.ToString("yyyy-MM-dd"),
                MeetingDateFormatted = booking.MeetingDate.ToString("dd MMM yyyy"),
                MeetingTime = booking.MeetingTime,
                Reason = booking.Reason,
                Session = user.AcademicSession,
                Status = "Pending",
                Title = "New Meeting Request",
                Message = $"Student {user.FullName} requested a meeting on {parsedDate:dd MMM yyyy} at {meetingTime}."
            };
            await _hubContext.Clients.Group($"User_{supervisorId}").SendAsync("ReceiveMeetingUpdate", meetingPayload);

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
        public async Task<JsonResult> GetAvailableSlots(int supervisorId, string date)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { slots = new List<string>(), message = "Invalid date" });

            // 1. Get supervisor's schedule logic
            var availabilities = await _context.SupervisorAvailabilities
                .Where(a => a.SupervisorId == supervisorId)
                .ToListAsync();

            // ── If supervisor has NO availability configured at all, block booking ──
            if (!availabilities.Any(a => a.IsAvailable))
            {
                return Json(new { slots = new List<string>(), message = "Your supervisor has not yet set their available time slots. Please ask them to configure their availability first.", noAvailability = true });
            }

            // 2. Find specific date override
            var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == parsedDate.Date);
            
            // 3. Fallback to weekly default
            var weeklyAvail = overrideAvail == null 
                ? availabilities.FirstOrDefault(a => a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == parsedDate.DayOfWeek.ToString()) 
                : null;

            var activeAvail = overrideAvail ?? weeklyAvail;

            // If there's no configuration for this specific day, show message
            if (activeAvail == null)
            {
                return Json(new { slots = new List<string>(), message = "Your supervisor has not set availability for this day. Please select a different date." });
            }

            TimeSpan start = activeAvail.StartTime;
            TimeSpan end = activeAvail.EndTime;
            bool isAvailable = activeAvail.IsAvailable;

            if (!isAvailable)
            {
                return Json(new { slots = new List<string>(), message = "Supervisor is not available on this date." });
            }

            // 4. Generate all 30-min slots
            var allSlots = new List<string>();
            var current = start;
            while (current < end)
            {
                allSlots.Add(current.ToString(@"hh\:mm"));
                current = current.Add(TimeSpan.FromMinutes(30));
            }

            // 5. Get booked slots
            var bookedTimes = await _context.MeetingBookings
                .Where(m => m.SupervisorId == supervisorId 
                         && m.MeetingDate.Date == parsedDate.Date 
                         && m.Status != "Rejected" && m.Status != "Cancelled")
                .Select(m => m.MeetingTime)
                .ToListAsync();

            // 6. Return only slots that aren't booked and aren't in the past (if today)
            var finalSlots = new List<string>();
            var now = DateTime.Now;
            bool isToday = parsedDate.Date == now.Date;

            foreach (var slot in allSlots)
            {
                if (bookedTimes.Contains(slot)) continue;

                if (isToday)
                {
                    var slotTime = TimeSpan.Parse(slot);
                    if (now.TimeOfDay > slotTime) continue;
                }

                finalSlots.Add(slot);
            }

            return Json(new { slots = finalSlots, message = "Success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMeeting(int meetingId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && (m.Status == "Pending" || m.Status == "Postponed"));

            if (meeting == null)
            {
                TempData["Error"] = "Meeting not found or cannot be cancelled.";
                return RedirectToAction(nameof(BookMeeting));
            }

            _context.MeetingBookings.Remove(meeting);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "General", "Meeting Cancelled",
                $"Meeting on {meeting.MeetingDate:dd MMM yyyy} was cancelled.");

            if (meeting.SupervisorId > 0)
            {
                await AddNotificationAsync(meeting.SupervisorId, "Meeting", "Meeting Cancelled",
                    $"Student {user.FullName} cancelled their meeting request on {meeting.MeetingDate:dd MMM}.");

                var cancelPayload = new {
                    Type = "MeetingCancelled",
                    MeetingId = meeting.Id,
                    Session = user.AcademicSession,
                    StudentName = user.FullName,
                    Title = "Meeting Cancelled",
                    Message = $"Student {user.FullName} cancelled their meeting request on {meeting.MeetingDate:dd MMM}."
                };
                await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", cancelPayload);
            }

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
                
                if (meeting.SupervisorId > 0)
                {
                    await AddNotificationAsync(meeting.SupervisorId, "Meeting", "Meeting Date Accepted",
                        $"Student {user.FullName} accepted your proposed new date for the meeting on {meeting.MeetingDate:dd MMM}.");

                    var acceptPayload = new {
                        Type = "MeetingAccepted",
                        MeetingId = meeting.Id,
                        Session = user.AcademicSession,
                        StudentName = user.FullName,
                        Title = "Meeting Accepted",
                        Message = $"Student {user.FullName} accepted your proposed date."
                    };
                    await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", acceptPayload);
                }

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
                MeetingBooking targetBooking;

                if (meeting.Status == "Pending" || meeting.Status == "Approved")
                {
                    // Update existing meeting instead of duplicating it
                    meeting.MeetingDate = parsedDate;
                    meeting.MeetingDay = parsedDate.DayOfWeek.ToString();
                    meeting.MeetingTime = meetingTime;
                    meeting.Status = "Pending";
                    
                    _context.MeetingBookings.Update(meeting);
                    targetBooking = meeting;
                }
                else
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
                    targetBooking = newBooking;
                }

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
                    Message = $"Student {user.FullName} booked a new meeting request to {parsedDate:dd MMM yyyy} at {meetingTime}. Reason: {(string.IsNullOrEmpty(targetBooking.Reason) ? "None provided" : targetBooking.Reason)}",
                    RelatedRecordId = targetBooking.Id // Note: Id will be assigned after SaveChanges, but it's okay for now or we can save first
                });

                await _context.SaveChangesAsync();

                // Update the related record ID after save
                var latestNotif = await _context.FYPNotifications.OrderByDescending(n => n.Id).FirstOrDefaultAsync(n => n.RecipientId == meeting.SupervisorId && n.Type == "MeetingRequest");
                if (latestNotif != null) {
                    latestNotif.RelatedRecordId = targetBooking.Id;
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
                var reschedulePayload = new {
                    Type = "MeetingRescheduled",
                    MeetingId = targetBooking.Id,
                    StudentId = user.Id,
                    StudentName = user.FullName,
                    StudentMatric = user.EmployeeId,
                    MeetingDate = targetBooking.MeetingDate.ToString("yyyy-MM-dd"),
                    MeetingDateFormatted = targetBooking.MeetingDate.ToString("dd MMM yyyy"),
                    MeetingTime = targetBooking.MeetingTime,
                    Session = user.AcademicSession,
                    Status = "Pending",
                    Title = "Meeting Rescheduled",
                    Message = $"Student {user.FullName} rescheduled a meeting to {parsedDate:dd MMM yyyy} at {meetingTime}."
                };
                await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", reschedulePayload);

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

                // Check supervisor availability
                var availabilities = await _context.SupervisorAvailabilities.Where(a => a.SupervisorId == meeting.SupervisorId).ToListAsync();
                var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == nextMeetingDate.Value.Date);
                var weeklyAvail = overrideAvail == null ? availabilities.FirstOrDefault(a => a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == nextMeetingDate.Value.DayOfWeek.ToString()) : null;
                var activeAvail = overrideAvail ?? weeklyAvail;

                if (!duplicateExists && activeAvail != null && activeAvail.IsAvailable)
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
            var logbookPayload = new {
                Type = "MeetingLogSubmitted",
                MeetingId = meeting.Id,
                StudentName = user.FullName,
                Title = "Meeting Log Submitted",
                Message = $"Student {user.FullName} submitted a progress logbook for review."
            };
            await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", logbookPayload);
            
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

            var project = await _context.FYPProjects.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == log.MeetingBooking.StudentId);
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
            var project = await _context.FYPProjects.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);
            if (project == null) return Unauthorized();
            
            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            string mimeType = "application/octet-stream";
            
            if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

            var physicalPath = System.IO.Path.Combine(_env.WebRootPath, file.FilePath.TrimStart('/', '\\'));
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
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

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
            addPara("Academic Session: " + (!string.IsNullOrEmpty(project?.AcademicSession) ? project.AcademicSession : (!string.IsNullOrEmpty(user.AcademicSession) ? user.AcademicSession : (DateTime.Now.Year + "/" + (DateTime.Now.Year + 1)))), false, 12, "", true);
            addBlankLine("");

            // ─── Section 1: Student Information ──────────────────────────────
            addPara("SECTION 1: STUDENT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Student Name:", user.FullName ?? "");
            addLabelValue("Student ID / Matric No.:", user.EmployeeId ?? "");
            addLabelValue("Faculty / Programme:", user.Programme ?? "");
            addLabelValue("Academic Session:", project?.AcademicSession ?? user.AcademicSession ?? "");
            addLabelValue("Contact Email:", user.Email ?? "");
            addBlankLine("");

            // ─── Section 2: Project Information ──────────────────────────────
            addPara("SECTION 2: PROJECT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Project Title:", project?.Title ?? "");
            addLabelValue("Scope / Category:", project?.Scope ?? "");
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
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

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
            addPara("Academic Session: " + (!string.IsNullOrEmpty(project?.AcademicSession) ? project.AcademicSession : (!string.IsNullOrEmpty(user.AcademicSession) ? user.AcademicSession : (DateTime.Now.Year + "/" + (DateTime.Now.Year + 1)))), false, 12, "", true);
            addBlankLine("");

            // Section 1
            addPara("SECTION 1: STUDENT & PROJECT INFORMATION", true, 12, "1F3A7A", false);
            addPara("─────────────────────────────────────────────────────", false, 10, "AAAAAA", false);
            addBlankLine("");
            addLabelValue("Student Name:", user.FullName ?? "");
            addLabelValue("Student ID / Matric No.:", user.EmployeeId ?? "");
            addLabelValue("Faculty / Programme:", user.Programme ?? "");
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
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

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
            addPara(project?.Title ?? "[YOUR THESIS TITLE]", true, 24, "000000", true);
            for (int i = 0; i < 4; i++) addBlankLine("");
            addPara("Name: " + (user.FullName ?? "[STUDENT NAME]"), false, 18, "000000", true);
            addBlankLine("");
            addPara("INTI Student ID: " + (user.EmployeeId ?? "[INTI STUDENT ID]"), false, 18, "000000", true);
            addBlankLine("");
            addPara("Coventry Student ID: " + (user.CoventryId ?? "[COVENTRY STUDENT ID]"), false, 18, "000000", true);
            addBlankLine("");
            addPara("Supervisor Name: " + (project?.Supervisor?.FullName ?? "[Supervisor Name]"), false, 18, "000000", true);
            for (int i = 0; i < 4; i++) addBlankLine("");
            addPara(!string.IsNullOrEmpty(user.Programme) ? user.Programme : "[Program Name]", true, 18, "000000", true);
            addBlankLine("");
            addPara(user.EmpDepartment?.Name ?? "School of Computing", true, 18, "000000", true);
            addBlankLine("");
            addPara(DateTime.Now.ToString("dd MMMM yyyy"), false, 18, "000000", true);

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



