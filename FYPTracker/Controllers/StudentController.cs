using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;
using NPOI.XWPF.UserModel;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;
using FYPTracker.Helpers;

namespace FYPTracker.Controllers
{
    [Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Student")]
    public class StudentController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly FYPTracker.Services.Meetings.IMeetingRequirementService _meetingRequirementService;
        private readonly FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService _weeklyMeetingSchedulingService;
        private readonly FYPTracker.Services.Time.ISystemClock _clock;
        private readonly FYPTracker.Services.Meetings.IAcademicSessionResolver _academicSessionResolver;

        public StudentController(
            FYPTrackerContext context, 
            IWebHostEnvironment env, 
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IEmailService emailService, 
            IHubContext<NotificationHub> hubContext,
            FYPTracker.Services.Meetings.IMeetingRequirementService meetingRequirementService,
            FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService weeklyMeetingSchedulingService,
            FYPTracker.Services.Time.ISystemClock clock,
            FYPTracker.Services.Meetings.IAcademicSessionResolver? academicSessionResolver = null)
        {
            _context = context;
            _env     = env;
            _configuration = configuration;
            _emailService = emailService;
            _hubContext = hubContext;
            _meetingRequirementService = meetingRequirementService;
            _weeklyMeetingSchedulingService = weeklyMeetingSchedulingService;
            _clock = clock;
            _academicSessionResolver = academicSessionResolver ?? new FYPTracker.Services.Meetings.AcademicSessionResolver(context);
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
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Feedbacks)
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project?.Milestones != null)
            {
                var iaf = FYPTracker.Utils.GradingHelper.GetIafMilestone(project.Milestones);
                if (iaf != null)
                {
                    try
                    {
                        var activeBooking = await _context.IAFDefenseBookings
                            .AsNoTracking()
                            .Include(b => b.Examiner)
                            .Include(b => b.AcademicSession)
                            .Where(b => b.MilestoneId == iaf.Id && b.BookingStatus != "Cancelled")
                            .OrderByDescending(b => b.AttemptNumber)
                            .ThenByDescending(b => b.Id)
                            .FirstOrDefaultAsync();

                        ViewBag.ScheduledIafBooking = activeBooking;
                    }
                    catch
                    {
                        // Safely handle environments or in-memory contexts where table is absent
                    }
                }
            }

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

            // Fetch unread Viva notifications specifically for the Student Dashboard
            var unreadVivaNotifications = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id && !n.IsRead && n.Type == "VivaScheduled")
                .OrderByDescending(n => n.CreatedDate)
                .ToListAsync();
            ViewBag.UnreadVivaNotifications = unreadVivaNotifications;

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

            var timeoutSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "SupervisorRequestTimeoutDays");
            int timeoutDays = (timeoutSetting != null && int.TryParse(timeoutSetting.Value, out int t) && t > 0) ? t : 3;
            ViewBag.SupervisorRequestTimeoutDays = timeoutDays;

            var expiredRequests = myRequests.Where(r => r.Status == "Pending" && r.RequestedDate <= DateTime.Now.AddDays(-timeoutDays)).ToList();
            foreach (var e in expiredRequests)
            {
                e.Status = "Rejected";
                e.ResponseNote = $"Auto-rejected: No supervisor response within the configured {timeoutDays}-day period.";
                e.RespondedDate = DateTime.Now;
            }
            if (expiredRequests.Any()) await _context.SaveChangesAsync();

            ViewBag.MyRequests = myRequests;

            // Fetch authorized feedback for student dashboard
            ViewBag.LatestFeedbacks = FYPTracker.Utils.GradingHelper.GetAuthorizedStudentFeedback(project);

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
                .Where(p => p.StudentId == user.Id && p.Status != "Archived")
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync();

            var stage2Eligibility = FYPTracker.Utils.GradingHelper.CheckStage2AcademicEligibility(project, project?.Milestones);
            if (!stage2Eligibility.IsEligible)
            {
                TempData["Error"] = stage2Eligibility.Reason;
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
                .Where(p => p.StudentId == user.Id && p.Status != "Archived")
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync();

            if (project == null)
            {
                TempData["Error"] = "Active project not found.";
                return RedirectToAction("Stage2");
            }

            var stage2Eligibility = FYPTracker.Utils.GradingHelper.CheckStage2AcademicEligibility(project, project.Milestones);
            if (!stage2Eligibility.IsEligible)
            {
                TempData["Error"] = stage2Eligibility.Reason;
                return RedirectToAction("Forms");
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
                    IsReadyForJointEvaluation = stepTitle.StartsWith("Step 5") || stepTitle.Contains("Presentation", StringComparison.OrdinalIgnoreCase)
                };
                _context.FYPMilestones.Add(milestone);
            }
            else
            {
                milestone.Status = "Submitted";
                milestone.SubmittedDate = DateTime.Now;
                if (stepTitle.StartsWith("Step 5") || stepTitle.Contains("Presentation", StringComparison.OrdinalIgnoreCase)) milestone.IsReadyForJointEvaluation = true;
                _context.FYPMilestones.Update(milestone);
            }

            await _context.SaveChangesAsync();

            // Handle file upload
            if (file != null && file.Length > 0)
            {
                var baseUploadDir = FYPTracker.Helpers.TestingStorageProvider.GetSubmissionsDirectory(_env.ContentRootPath, _env, _configuration);
                var uploadDir = Path.Combine(baseUploadDir, user.Id.ToString());
                if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                string filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string urlPrefix = FYPTracker.Helpers.TestingStorageProvider.GetSubmissionsUrlPrefix(_env, _configuration);
                _context.FYPSubmissionFiles.Add(new FYPSubmissionFile
                {
                    MilestoneId = milestone.Id,
                    FileName = file.FileName,
                    FilePath = $"{urlPrefix}{user.Id}/{fileName}",
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
            var principal = HttpContext.User;

            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            if (!int.TryParse(principal.FindFirst("Id")?.Value, out int id))
                return null;

            return await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == id);
        }
        private async Task<AcademicSession?> GetStudentSessionAsync(int studentId)
        {
            return await _academicSessionResolver.GetStudentSessionAsync(studentId);
        }

        private async Task<AcademicSession?> GetStageSessionAsync(int studentId, int stage)
        {
            return await _academicSessionResolver.GetStageSessionAsync(studentId, stage);
        }

        private async Task<DateTime?> GetMeetingBookingCutoffAsync(int studentId, int stage = 1)
        {
            return await _academicSessionResolver.GetMeetingBookingCutoffAsync(studentId, stage);
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
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.AssignedExaminers)
                    .ThenInclude(ae => ae.Examiner)
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
        //  GET MILESTONE FEEDBACK (STUDENT READ-ONLY)
        // ══════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetMilestoneFeedback(int milestoneId)
        {
            if (milestoneId <= 0)
            {
                return BadRequest(new { success = false, message = "A valid milestone ID is required." });
            }

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "You must be logged in to view feedback." });

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .Include(m => m.Feedbacks)
                    .ThenInclude(f => f.Supervisor)
                .Include(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone == null)
            {
                return NotFound(new { success = false, message = "Milestone not found or you do not have permission to view its feedback." });
            }

            var supervisorName = milestone.Project?.Supervisor?.FullName ?? "Supervisor";
            var attemptsList = new List<object>();

            // 1. IAF Milestones: read attempt history from Remarks JSON
            bool isIaf = milestone.MilestoneCode == "IAF" || 
                         (!string.IsNullOrEmpty(milestone.Title) && (milestone.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase)));

            if (isIaf)
            {
                string rawJson = "";
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    rawJson = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None)[1];
                }
                var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(rawJson);
                if (history?.Attempts != null && history.Attempts.Any())
                {
                    foreach (var att in history.Attempts.OrderBy(a => a.AttemptNumber))
                    {
                        attemptsList.Add(new
                        {
                            attemptNumber = att.AttemptNumber,
                            status = att.Status ?? (att.AttemptNumber == history.CurrentAttempt ? milestone.Status : "Archived"),
                            submittedAt = att.SubmittedAt?.ToString("dd MMM yyyy, hh:mm tt"),
                            reviewedAt = att.SupervisorReviewedAt?.ToString("dd MMM yyyy, hh:mm tt") ?? att.SubmittedAt?.ToString("dd MMM yyyy, hh:mm tt"),
                            supervisorFeedback = att.SupervisorFeedback,
                            panelFeedback = att.PanelComments,
                            revisionNotes = (string?)null,
                            files = new List<string>()
                        });
                    }
                }
            }

            // 2. Progress / Task milestones with FYPSubmissionFile version history
            if (!isIaf && milestone.SubmissionFiles != null && milestone.SubmissionFiles.Any(f => !f.IsDeleted))
            {
                var fileGroups = milestone.SubmissionFiles
                    .Where(f => !f.IsDeleted)
                    .GroupBy(f => f.AttemptNumber)
                    .OrderBy(g => g.Key)
                    .ToList();

                int maxAttempt = fileGroups.Max(g => g.Key);
                foreach (var grp in fileGroups)
                {
                    int attNum = grp.Key;
                    var fileNames = grp.Select(f => f.FileName).Distinct().ToList();
                    var revNotes = grp.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.RevisionNotes))?.RevisionNotes;
                    var subDate = grp.Min(f => f.UploadedDate);

                    string? fb = grp.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Remarks))?.Remarks;
                    DateTime? revDate = null;
                    if (attNum == maxAttempt)
                    {
                        if (string.IsNullOrWhiteSpace(fb)) fb = milestone.SupervisorFeedback;
                        revDate = milestone.SubmittedDate;
                    }

                    attemptsList.Add(new
                    {
                        attemptNumber = attNum,
                        status = attNum == maxAttempt ? milestone.Status : "ReturnedForChanges",
                        submittedAt = subDate.ToString("dd MMM yyyy, hh:mm tt"),
                        reviewedAt = revDate?.ToString("dd MMM yyyy, hh:mm tt"),
                        supervisorFeedback = fb,
                        panelFeedback = (string?)null,
                        revisionNotes = revNotes,
                        files = fileNames
                    });
                }
            }

            // 3. Fallback if no multi-attempt items exist
            if (!attemptsList.Any())
            {
                var files = milestone.SubmissionFiles?.Where(f => !f.IsDeleted).Select(f => f.FileName).Distinct().ToList() ?? new List<string>();
                var revNotes = milestone.SubmissionFiles?.FirstOrDefault(f => !f.IsDeleted && !string.IsNullOrWhiteSpace(f.RevisionNotes))?.RevisionNotes;
                var fbComment = milestone.Feedbacks?.OrderByDescending(f => f.CreatedDate).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Comment))?.Comment;
                var fb = !string.IsNullOrWhiteSpace(milestone.SupervisorFeedback) ? milestone.SupervisorFeedback : fbComment;

                attemptsList.Add(new
                {
                    attemptNumber = 1,
                    status = milestone.Status,
                    submittedAt = milestone.SubmittedDate?.ToString("dd MMM yyyy, hh:mm tt"),
                    reviewedAt = milestone.SubmittedDate?.ToString("dd MMM yyyy, hh:mm tt") ?? milestone.DueDate?.ToString("dd MMM yyyy, hh:mm tt"),
                    supervisorFeedback = fb,
                    panelFeedback = (string?)null,
                    revisionNotes = revNotes,
                    files = files
                });
            }

            // 4. Online Rubric Evaluation Remarks (SupervisorMarksheetData)
            string? rubricFeedback = null;
            if (!string.IsNullOrWhiteSpace(milestone.SupervisorMarksheetData) && !FYPTracker.Utils.GradingHelper.IsMarksheetDraft(milestone.SupervisorMarksheetData))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(milestone.SupervisorMarksheetData);
                    var root = doc.RootElement;
                    var parts = new List<string>();
                    if (root.TryGetProperty("Strengths", out var s) && !string.IsNullOrWhiteSpace(s.GetString()))
                        parts.Add($"Strengths: {s.GetString()!.Trim()}");
                    if (root.TryGetProperty("Improvements", out var imp) && !string.IsNullOrWhiteSpace(imp.GetString()))
                        parts.Add($"Improvements: {imp.GetString()!.Trim()}");
                    if (root.TryGetProperty("Comments", out var c) && !string.IsNullOrWhiteSpace(c.GetString()))
                        parts.Add($"Comments: {c.GetString()!.Trim()}");

                    if (parts.Any())
                    {
                        rubricFeedback = string.Join("\n\n", parts);
                    }
                }
                catch { }
            }

            // 5. Stage 2 project-level marksheet feedback if applicable
            if (milestone.Stage == 2 && milestone.Project != null && !string.IsNullOrWhiteSpace(milestone.Project.Stage2SupervisorMarksheetData))
            {
                bool isFinal = milestone.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase) || 
                               milestone.Title.Contains("Final", StringComparison.OrdinalIgnoreCase) ||
                               milestone.MilestoneCode == "STAGE2_FINAL";
                if (isFinal)
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(milestone.Project.Stage2SupervisorMarksheetData);
                        if (doc.RootElement.TryGetProperty("Feedback", out var fb) && !string.IsNullOrWhiteSpace(fb.GetString()))
                        {
                            var stage2Fb = fb.GetString()!.Trim();
                            if (!string.IsNullOrEmpty(rubricFeedback))
                                rubricFeedback += "\n\n" + stage2Fb;
                            else
                                rubricFeedback = stage2Fb;
                        }
                    }
                    catch { }
                }
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    milestoneId = milestone.Id,
                    milestoneTitle = milestone.Title,
                    stage = milestone.Stage,
                    status = milestone.Status,
                    supervisorName = supervisorName,
                    latestFeedback = milestone.SupervisorFeedback,
                    rubricFeedback = rubricFeedback,
                    latestReviewDate = milestone.SubmittedDate?.ToString("dd MMM yyyy, hh:mm tt"),
                    attempts = attemptsList
                }
            });
        }

        // ══════════════════════════════════════════════════════
        //  SELECT SUPERVISOR
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> SelectSupervisor()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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
                .Where(e => e.UserRoles.Any(r => r.Role.Name == "Supervisor") && !e.IsResign && !rejectedIds.Contains(e.Id))
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
                if (user == null) return RedirectToAction("Login", "User");

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
            if (user == null) return RedirectToAction("Login", "User");

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
                .AnyAsync(u => u.UserRoles.Any(r => r.Role.Name == "Student")
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
                    string message = $"<p>Dear {supervisor.FullName},</p><br/><p>You have received a new FYP supervision request from <strong>{user.FullName}</strong>.</p><p><strong>Project Title:</strong> {projectTopic}</p>";
                    var httpRequest = HttpContext.Request;
                    string loginUrl = $"{httpRequest.Scheme}://{httpRequest.Host}/User/Login";
                    string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Request", loginUrl);
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
            if (user == null) return RedirectToAction("Login", "User");

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
            DateTime? startDate, DateTime? endDate, string? workDone, string? challenges, string? nextPeriodPlan, string? pctComplete, string? coventryId, string? revisionNotes, bool isDraft = false)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Milestones)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.StudentId == user.Id);

            if (milestone == null)
            {
                TempData["Error"] = "Invalid milestone or unauthorized access.";
                return RedirectToAction("Forms");
            }

            // ── Guard: Prevent modifying graded milestones (except Rejected which needs revision) ──
            bool isStage1Passed = milestone.Project?.Stage1Grade == "Pass";
            bool isStage1FinalMilestone = milestone.MilestoneCode == "STAGE1_FINAL" || 
                                          (!string.IsNullOrEmpty(milestone.Title) && milestone.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase)) ||
                                          (formType == "Proposal Form");

            if (milestone.Status == "Approved" || milestone.Status == "Completed" || (isStage1FinalMilestone && isStage1Passed))
            {
                TempData["Error"] = "This milestone has already been finalized/graded and cannot be modified or resubmitted.";
                return RedirectToAction("Forms");
            }

            // ── Guard: Stage 2 Progression Gate (Must be academically eligible for Stage 2) ──
            if (milestone.Stage == 2 || formType == "Stage 2 Progress Milestone")
            {
                var s2Eligibility = FYPTracker.Utils.GradingHelper.CheckStage2AcademicEligibility(milestone.Project, milestone.Project?.Milestones);
                if (!s2Eligibility.IsEligible)
                {
                    TempData["Error"] = s2Eligibility.Reason;
                    return RedirectToAction("Forms");
                }
            }

            // ── Guard: Stage 1 Progression Gate (IAF must be passed/approved before DPP or subsequent Stage 1 submissions) ──
            if (milestone.Stage == 1 && formType != "Initial Project Allocation Form (IAF)")
            {
                var iaf = FYPTracker.Utils.GradingHelper.GetIafMilestone(milestone.Project?.Milestones);
                IAFDefenseBooking? activeBooking = null;
                if (iaf != null)
                {
                    try
                    {
                        activeBooking = await _context.IAFDefenseBookings
                            .AsNoTracking()
                            .Where(b => b.MilestoneId == iaf.Id && b.BookingStatus != "Cancelled")
                            .OrderByDescending(b => b.AttemptNumber)
                            .ThenByDescending(b => b.Id)
                            .FirstOrDefaultAsync();
                    }
                    catch
                    {
                        // Safely handle environments or in-memory contexts where table is absent
                    }
                }

                if (!FYPTracker.Utils.GradingHelper.CanProgressToStage1Dpp(iaf, activeBooking))
                {
                    string reason = iaf == null ? "IAF milestone not found." :
                        (iaf.Status == "DefenseScheduled" ? "Your IAF Defense session is scheduled and awaiting assessment." :
                        (iaf.Status == "DefenseRequired" ? "IAF Defense is required and awaiting scheduling." :
                        (iaf.Status == "ChangeOfTopicRequired" ? "IAF Defense was unsuccessful; change of topic is required." :
                        $"IAF is currently in '{iaf.Status}' status and has not been approved.")));

                    TempData["Error"] = $"Cannot submit Stage 1 Proposal. {reason}";
                    return RedirectToAction("Forms");
                }
            }

            // ── Guard: Stage 1 Final DPP Prerequisite (Supervisor-assigned Stage 1 DPP progress tasks mandatory and approved) ──
            if (isStage1FinalMilestone && !isDraft)
            {
                var allProjectMilestones = milestone.Project?.Milestones;
                if (allProjectMilestones == null || !allProjectMilestones.Any())
                {
                    allProjectMilestones = await _context.FYPMilestones
                        .Where(m => m.FYPProjectId == milestone.FYPProjectId)
                        .ToListAsync();
                }

                var iaf = FYPTracker.Utils.GradingHelper.GetIafMilestone(allProjectMilestones);
                IAFDefenseBooking? activeBooking = null;
                if (iaf != null)
                {
                    try
                    {
                        activeBooking = await _context.IAFDefenseBookings
                            .AsNoTracking()
                            .Where(b => b.MilestoneId == iaf.Id && b.BookingStatus != "Cancelled")
                            .OrderByDescending(b => b.AttemptNumber)
                            .ThenByDescending(b => b.Id)
                            .FirstOrDefaultAsync();
                    }
                    catch { }
                }

                var dppPrereq = FYPTracker.Utils.GradingHelper.CanSubmitStage1FinalDpp(allProjectMilestones, iaf, activeBooking);
                if (!dppPrereq.Allowed)
                {
                    TempData["Error"] = dppPrereq.Reason;
                    return RedirectToAction("Forms");
                }
            }


            // ── Guard: Stage 1 IAF server-side state validation ──
            if (formType == "Initial Project Allocation Form (IAF)")
            {
                string guardAction = Request.Form["iafAction"].ToString();
                string guardStatus = string.IsNullOrEmpty(milestone.Status) ? "Draft" : milestone.Status;

                // Editability policy:
                // - Draft, Pending, ReturnedForEdits: editable
                // - PanelRevisionRequired: editable only through authorized revision workflow
                // - ChangeOfTopicRequired: editable only for new Attempt 2 workflow
                // - PendingSupervisorReview, PendingPanelReview, SupervisorSigned, DefenseRequired, DefenseScheduled, AdminApproved, Approved, Completed: strictly locked!
                var lockedStates = new[] {
                    "PendingSupervisorReview", "PendingPanelReview", "SupervisorSigned",
                    "DefenseRequired", "DefenseScheduled", "AdminApproved", "Approved", "Completed"
                };

                if (lockedStates.Contains(guardStatus))
                {
                    TempData["Error"] = $"Your IAF is currently locked in '{guardStatus}' status and cannot be modified.";
                    return RedirectToAction("Forms");
                }

                string guardJson = "";
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                    guardJson = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None)[1];
                var guardHistory = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(guardJson);
                var guardAttempt = guardHistory.Attempts.Find(a => a.AttemptNumber == guardHistory.CurrentAttempt);

                bool allowed = false;
                if (guardAction == "draft" || guardAction == "supervisor_review")
                {
                    // Core Workflow Fix 2A: Enforce maximum 2 IAF attempts server-side
                    if (guardStatus == "ChangeOfTopicRequired" && guardHistory.CurrentAttempt >= 2)
                    {
                        TempData["Error"] = "Maximum 2 IAF attempts reached. Please contact Admin regarding the FYP repeat process.";
                        return RedirectToAction("Forms");
                    }

                    var editableStates = new[] { "Draft", "Pending", "ReturnedForEdits", "PanelRevisionRequired", "ChangeOfTopicRequired" };
                    allowed = editableStates.Contains(guardStatus)
                              && (guardAttempt == null || string.IsNullOrEmpty(guardAttempt.SupervisorSignature) || guardStatus == "PanelRevisionRequired" || guardStatus == "ChangeOfTopicRequired");
                }
                else if (guardAction == "submit" || guardAction == "submit_new")
                {
                    // Submit buttons are only for SupervisorSigned legacy flows
                    allowed = false;
                }

                if (!allowed)
                {
                    TempData["Error"] = "This IAF action is not allowed in its current status.";
                    return RedirectToAction("Forms");
                }
            }

            // Only update user's Coventry ID if the submission is verified and authorized
            if (!string.IsNullOrEmpty(coventryId) && user.CoventryId != coventryId)
            {
                user.CoventryId = coventryId;
                _context.Update(user);
                await _context.SaveChangesAsync();
            }

            // ── Guard: Supervisor-added progress milestones (Stage 1 & Stage 2) ──
            if (milestone.MilestoneSource == "Supervisor" && (milestone.Stage == 1 || milestone.Stage == 2))
            {
                bool isAllowedFormType = formType == "DPP Work & Progress" || formType == "Stage 2 Progress Milestone";
                if (!isAllowedFormType || !(milestone.Status == "Draft" || milestone.Status == "Pending" || milestone.Status == "ReturnedForChanges"))
                {
                    TempData["Error"] = "This progress milestone cannot be modified in its current status.";
                    return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                }
            }

            // ── Server-side validation for DPP Work & Progress ──
            if (formType == "DPP Work & Progress")
            {
                if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
                {
                    TempData["Error"] = "End Period must be equal to or after the Start Period.";
                    return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                }

                if (milestone.DueDate.HasValue && endDate.HasValue && endDate.Value.Date > milestone.DueDate.Value.Date)
                {
                    TempData["Error"] = $"End Period cannot exceed the Milestone Due Date ({milestone.DueDate.Value:dd MMM yyyy}).";
                    return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                }

                if (!isDraft)
                {
                    if (!startDate.HasValue || !endDate.HasValue)
                    {
                        TempData["Error"] = "Reporting period start and end dates are required.";
                        return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                    }

                    if (milestone.Status == "ReturnedForChanges" && string.IsNullOrWhiteSpace(revisionNotes))
                    {
                        TempData["Error"] = "Revision details are required when resubmitting a returned milestone.";
                        return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                    }

                    bool hasNewFiles = (attachedFile != null && attachedFile.Length > 0) || (attachedFiles != null && attachedFiles.Any(f => f.Length > 0));
                    bool hasExistingFiles = await _context.FYPSubmissionFiles.AnyAsync(f => f.MilestoneId == milestoneId);
                    if (!hasNewFiles && !hasExistingFiles)
                    {
                        TempData["Error"] = "Please upload a progress document (PDF).";
                        return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                    }
                }
            }

            var allFiles = new List<IFormFile>();
            if (attachedFile != null && attachedFile.Length > 0) allFiles.Add(attachedFile);
            if (attachedFiles != null && attachedFiles.Any()) allFiles.AddRange(attachedFiles.Where(f => f.Length > 0));

            var savedFileRecords = new List<FYPSubmissionFile>();
            if (allFiles.Any())
            {
                // Validate all files BEFORE proceeding
                if (formType == "DPP Work & Progress")
                {
                    foreach (var file in allFiles)
                    {
                        var extCheck = Path.GetExtension(file.FileName).ToLowerInvariant();
                        if (extCheck != ".pdf")
                        {
                            TempData["Error"] = "Only PDF files are allowed for DPP Work & Progress.";
                            return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                        }
                        if (file.Length > 100L * 1024 * 1024)
                        {
                            TempData["Error"] = "File size must not exceed 100 MB.";
                            return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                        }
                    }
                }
                else if (formType == "Proposal Form" || isStage1FinalMilestone)
                {
                    foreach (var file in allFiles)
                    {
                        var extCheck = Path.GetExtension(file.FileName).ToLowerInvariant();
                        if (extCheck != ".pdf" || file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase))
                        {
                            TempData["Error"] = "Only PDF files are allowed for Stage 1 Final DPP submission. Word documents (.docx) and other file formats are strictly rejected.";
                            return RedirectToAction("Forms");
                        }
                        if (file.Length > 100L * 1024 * 1024)
                        {
                            TempData["Error"] = "File size must not exceed 100 MB.";
                            return RedirectToAction("Forms");
                        }

                        // Validate file content magic bytes if file has content
                        if (file.Length >= 4)
                        {
                            byte[] header = new byte[4];
                            using var stream = file.OpenReadStream();
                            int bytesRead = stream.Read(header, 0, 4);
                            if (bytesRead >= 4 && System.Text.Encoding.ASCII.GetString(header) != "%PDF")
                            {
                                TempData["Error"] = "The uploaded file is not a valid PDF document. Please export your combined submission as a true PDF.";
                                return RedirectToAction("Forms");
                            }
                        }
                    }
                }
                else
                {
                    var allowedExt = new[] { ".pdf", ".pptx", ".ppt" };
                    foreach (var file in allFiles)
                    {
                        var extCheck = Path.GetExtension(file.FileName).ToLowerInvariant();
                        if (!allowedExt.Contains(extCheck))
                        {
                            TempData["Error"] = "Only PDF and PPT/PPTX files are allowed.";
                            return RedirectToAction("Forms");
                        }
                        if (file.Length > 100L * 1024 * 1024)
                        {
                            TempData["Error"] = "File size must not exceed 100 MB.";
                            return RedirectToAction("Forms");
                        }
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

                var uploadDir = FYPTracker.Helpers.TestingStorageProvider.GetSubmissionsDirectory(_env.ContentRootPath, _env, _configuration);
                string urlPrefix = FYPTracker.Helpers.TestingStorageProvider.GetSubmissionsUrlPrefix(_env, _configuration);

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
                        FilePath = $"{urlPrefix}{uniqueName}",
                        FileSize = file.Length,
                        AttemptNumber = newAttemptNumber,
                        RevisionNotes = revisionNotes,
                        UploadedBy = user.Id,
                        UploadedDate = DateTime.Now
                    });
                }
            }

            bool isNewAttemptNeeded = false;

                if (formType == "DPP Work & Progress")
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string> {
                        {"Start Period", startDate?.ToString("yyyy-MM-dd") ?? ""},
                        {"End Period", endDate?.ToString("yyyy-MM-dd") ?? ""}
                    };
                    detailsJson = System.Text.Json.JsonSerializer.Serialize(dict);
                    title = "DPP Work & Progress";
                }
                else if (formType == "Initial Project Allocation Form (IAF)")
                {
                    string CleanInputObj(string? val, int num) =>
                        string.IsNullOrWhiteSpace(val) ? "" :
                        System.Text.RegularExpressions.Regex.Replace(
                            val,
                            $@"^\s*({num}\.\s*)?(To\s+)+",
                            "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                    var cleanObj1 = CleanInputObj(objective1, 1);
                    var cleanObj2 = CleanInputObj(objective2, 2);
                    var cleanObj3 = CleanInputObj(objective3, 3);

                    var finalObj1 = string.IsNullOrWhiteSpace(cleanObj1) ? "" : "1. To " + cleanObj1;
                    var finalObj2 = string.IsNullOrWhiteSpace(cleanObj2) ? "" : "2. To " + cleanObj2;
                    var finalObj3 = string.IsNullOrWhiteSpace(cleanObj3) ? "" : "3. To " + cleanObj3;

                    string rawJson = "";
                    if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                    {
                        var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                        rawJson = parts[1];
                    }
                    var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(rawJson);

                    string submittedTitle = Request.Form["title"].ToString();
                    string iafActionForTitle = Request.Form["iafAction"].ToString();
                    if (!string.IsNullOrWhiteSpace(submittedTitle) && (iafActionForTitle == "draft" || iafActionForTitle == "supervisor_review")) 
                    {
                        milestone.Project.Title = submittedTitle;
                    }

                    var currentFormData = new System.Collections.Generic.Dictionary<string, string> {
                        {"title", milestone.Project?.Title ?? ""},
                        {"scope", scope ?? ""},
                        {"problem", problemStatement ?? ""},
                        {"obj1", finalObj1},
                        {"obj2", finalObj2},
                        {"obj3", finalObj3},
                        {"references", references ?? ""},
                        {"studentName", user.FullName ?? ""},
                        {"coventryId", user.CoventryId ?? ""},
                        {"program", user.Programme ?? ""},
                        {"agreedSupervisor", Request.Form.TryGetValue("supervisorName", out var supName) ? supName.ToString() : ""}
                    };

                    var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                    string iafAction = Request.Form["iafAction"].ToString();
                    
                    isNewAttemptNeeded = currentAttempt != null && (currentAttempt.Status == "ChangeOfTopicRequired" || currentAttempt.Status == "PanelRevisionRequired") && (iafAction == "draft" || iafAction == "supervisor_review");

                    if (isNewAttemptNeeded)
                    {
                        // Core Workflow Fix 2A: Enforce maximum 2 IAF attempts server-side
                        if (history.CurrentAttempt >= 2)
                        {
                            TempData["Error"] = "Maximum 2 IAF attempts reached. Please contact Admin regarding the FYP repeat process.";
                            return RedirectToAction("Forms");
                        }

                        history.CurrentAttempt++;
                        currentAttempt = new FYPTracker.Models.IafAttempt
                        {
                            AttemptNumber = history.CurrentAttempt
                        };
                        history.Attempts.Add(currentAttempt);
                    }
                    else if (currentAttempt == null)
                    {
                        currentAttempt = new FYPTracker.Models.IafAttempt { AttemptNumber = history.CurrentAttempt };
                        history.Attempts.Add(currentAttempt);
                    }

                    // Save the Admin IAF template used by this attempt.
                    const string iafSnapshotKey = "__iafTemplateSnapshot";

                    if (currentAttempt.FormData != null &&
                        currentAttempt.FormData.TryGetValue(
                            iafSnapshotKey, out var priorTemplateSnapshot) &&
                        !string.IsNullOrWhiteSpace(priorTemplateSnapshot))
                    {
                        // Keep the original template for this attempt.
                        currentFormData[iafSnapshotKey] = priorTemplateSnapshot;
                    }
                    else
                    {
                        var iafTemplateFile = System.IO.Path.Combine(
                            _env.ContentRootPath,
                            "FormTemplates",
                            "InitialProjectAllocationForm.default.json");

                        if (!System.IO.File.Exists(iafTemplateFile))
                        {
                            TempData["Error"] = "IAF template file is missing.";
                            return RedirectToAction("Forms");
                        }

                        var templateJson =
                            await System.IO.File.ReadAllTextAsync(iafTemplateFile);

                        var templateModel =
                            System.Text.Json.JsonSerializer.Deserialize<
                                FYPTracker.ViewModels.MeetingRecordTemplateVM>(
                                    templateJson,
                                    new System.Text.Json.JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true
                                    });

                        if (templateModel?.Fields == null ||
                            templateModel.Fields.Count == 0)
                        {
                            TempData["Error"] = "IAF template content is invalid.";
                            return RedirectToAction("Forms");
                        }

                        currentFormData[iafSnapshotKey] = templateJson;
                    }

                    // KEEP THIS ORIGINAL LINE.
                    string existingSignature = currentAttempt.SupervisorSignature ?? "";
                    if (!string.IsNullOrEmpty(existingSignature))
                    {
                        currentFormData["Supervisor Signature"] = existingSignature;
                    }
                    
                    // Validate References Listing if configured for this project in Development/Testing
                    if (iafAction != "draft" && DemoUiOverrideHelper.ShouldEnforceIafReferencesValidationForProject(milestone.FYPProjectId, _env, _configuration))
                    {
                        string refError = DemoUiOverrideHelper.ValidateIafReferences(references);
                        if (!string.IsNullOrEmpty(refError))
                        {
                            TempData["Error"] = $"References Listing Validation Error: {refError}";
                            return RedirectToAction("Forms");
                        }
                    }

                    bool isFormalSubmit = iafAction == "submit" || iafAction == "submit_new";
                    if (!isFormalSubmit)
                    {
                        currentAttempt.FormData = currentFormData;
                    }

                    bool isPanelRevision = milestone.Status == "PanelRevisionRequired" && !isNewAttemptNeeded;
                    if (iafAction == "draft") {
                        currentAttempt.Status = isPanelRevision ? "PanelRevisionRequired" : "Draft";
                        isDraft = true;
                    } else if (iafAction == "supervisor_review") {
                        currentAttempt.Status = "PendingSupervisorReview";
                        isDraft = true;
                    } else if (iafAction == "submit") {
                        currentAttempt.Status = "PendingPanelReview";
                        currentAttempt.SubmittedAt = DateTime.Now;
                        isDraft = false; 
                    } else if (iafAction == "submit_new") {
                        // Formal Submission #2: formalize the SAME attempt; no Panel loop, proceeds to DPP.
                        currentAttempt.Status = "AdminApproved";
                        currentAttempt.SubmittedAt = DateTime.Now;
                        isDraft = false; 
                    }

                    detailsJson = System.Text.Json.JsonSerializer.Serialize(history);
                    title = "Initial Project Allocation Form (IAF)";

                    if (!isFormalSubmit)
                    {
                        milestone.Project.ProblemStatement = problemStatement;
                        milestone.Project.Objective1 = finalObj1;
                        milestone.Project.Objective2 = finalObj2;
                        milestone.Project.Objective3 = finalObj3;
                        milestone.Project.Scope = scope;
                    }
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

                if (isDraft)
                {
                    if (formType == "Initial Project Allocation Form (IAF)") {
                        string iafAction = Request.Form["iafAction"].ToString();
                        if (iafAction == "draft") {
                            if (isNewAttemptNeeded || (milestone.Status != "PanelRevisionRequired" && milestone.Status != "ReturnedForEdits")) milestone.Status = "Draft";
                            TempData["Success"] = "Draft saved successfully.";
                        } else if (iafAction == "supervisor_review") {
                            milestone.Status = "PendingSupervisorReview";
                            TempData["Success"] = "Sent for Supervisor Review successfully.";
                        }
                    } else {
                        if (milestone.MilestoneSource == "Supervisor" && (milestone.Stage == 1 || milestone.Stage == 2))
                        {
                            if (milestone.Status != "ReturnedForChanges") milestone.Status = "Draft";
                        }
                        else if (milestone.Status != "Rejected" && milestone.Status != "PanelRevisionRequired" && milestone.Status != "ChangeOfTopicRequired")
                        {
                            milestone.Status = "Draft";
                        }
                        TempData["Success"] = "Draft saved successfully.";
                    }
                    milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                }
                else
                {
                    if (formType == "Initial Project Allocation Form (IAF)") {
                        string iafAction = Request.Form["iafAction"].ToString();
                        if (iafAction == "submit") {
                            milestone.Status = "PendingPanelReview";
                        } else if (iafAction == "submit_new") {
                            milestone.Status = "AdminApproved";
                        }
                        milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                    } else if (milestone.MilestoneSource == "Supervisor" && (milestone.Stage == 1 || milestone.Stage == 2)) {
                        // Supervisor progress milestone (Stage 1 / Stage 2): not a formal submission, awaits supervisor review
                        milestone.Status = "PendingSupervisorReview";
                        milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                    } else {
                        if (milestone.Status == "PanelRevisionRequired")
                        {
                            milestone.Status = "DefenseRequired";
                            milestone.Remarks = $"Form: {formType}. Details: {title} (Panel Revision). JSON: {detailsJson}";
                        }
                        else if (milestone.Status == "ChangeOfTopicRequired")
                        {
                            milestone.Status = "Submitted";
                            milestone.Remarks = $"Form: {formType}. Details: {title} (Change of Topic). JSON: {detailsJson}";
                        }
                        else
                        {
                            milestone.Status = "Submitted";
                            milestone.Remarks = $"Form: {formType}. Details: {title}. JSON: {detailsJson}";
                        }
                    }
                    milestone.SubmittedDate = DateTime.Now;
                    TempData["Success"] = "Form submitted successfully.";
                }
                
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
                    return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
                }
                
                await LogActivityAsync(user.Id, "Submission", isDraft ? "Draft Saved" : "Form Submitted", isDraft ? $"Saved draft of {formType} for {milestone.Title}" : $"Submitted {formType} for {milestone.Title}");
                if (!isDraft && milestone.Project.SupervisorId.HasValue)
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
                    if (_hubContext != null)
                    {
                        await _hubContext.Clients.Group($"User_{milestone.Project.SupervisorId.Value}").SendAsync("ReceiveMilestoneUpdate", submissionPayload);
                    }
                }

            if (!isDraft) TempData["Success"] = "Your form has been submitted successfully!";
            return milestone.Stage == 2 ? RedirectToAction("Stage2") : RedirectToAction("Forms");
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
            if (string.IsNullOrEmpty(ext)) ext = System.IO.Path.GetExtension(file.FilePath).ToLowerInvariant();

            string mimeType = "application/octet-stream";
            
            if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

            var physicalPath = System.IO.Path.Combine(_env.WebRootPath, file.FilePath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(physicalPath)) return NotFound("File not found on disk.");

            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{file.FileName}{(string.IsNullOrEmpty(System.IO.Path.GetExtension(file.FileName)) ? ext : "")}\"");
            return PhysicalFile(physicalPath, mimeType);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadStage1ExaminerMarksheet(int projectId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.StudentId == user.Id);

            if (project == null || string.IsNullOrWhiteSpace(project.Stage1ExaminerMarksheetPath))
            {
                return NotFound("Examiner marksheet is not available for this project.");
            }

            var physicalPath = System.IO.Path.Combine(_env.WebRootPath, project.Stage1ExaminerMarksheetPath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound("Marksheet file not found on disk.");
            }

            var ext = System.IO.Path.GetExtension(project.Stage1ExaminerMarksheetPath).ToLowerInvariant();
            string mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            if (ext == ".xls") mimeType = "application/vnd.ms-excel";

            string fileName = $"Stage1_Examiner_Marksheet_{project.Id}{ext}";
            return PhysicalFile(physicalPath, mimeType, fileName);
        }

        // ══════════════════════════════════════════════════════
        //  FORMS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Forms()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project != null)
            {
                DateTime? authoritativeIafDueDate = null;
                if (!string.IsNullOrEmpty(project.AcademicSession))
                {
                    var sessionObj = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == project.AcademicSession);
                    if (sessionObj != null)
                    {
                        authoritativeIafDueDate = await _context.DefaultMilestoneTemplates
                            .Where(t => t.AcademicSessionId == sessionObj.Id && t.MilestoneCode == "IAF" && t.DueDate != null)
                            .Select(t => t.DueDate)
                            .FirstOrDefaultAsync();
                    }
                }

                var sessionIaf = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .Where(m => m.Project.AcademicSession == project.AcademicSession && (m.Title == "Initial Project Allocation Form (IAF)" || m.Title.Contains("IAF")) && m.DueDate != null)
                    .OrderByDescending(m => m.DueDate)
                    .FirstOrDefaultAsync();

                var effectiveIafDueDate = authoritativeIafDueDate ?? sessionIaf?.DueDate;

                if (effectiveIafDueDate.HasValue)
                {
                    ViewBag.IAFDeadline = effectiveIafDueDate.Value.ToString("dd MMMM yyyy") + " before 11.59pm";
                }
                else
                {
                    ViewBag.IAFDeadline = "No deadline set yet.";
                }

                var hasIaf = project.Milestones.Any(m => (m.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase)) && !m.Title.Contains("Industrial", StringComparison.OrdinalIgnoreCase));
                if (!hasIaf)
                {
                    bool shouldAutoApprove = project.Milestones.Any(m => m.Status == "Approved" || m.Status == "Submitted");
                    
                    var newDeadline = effectiveIafDueDate.HasValue 
                        ? effectiveIafDueDate.Value 
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
                
                var iafMilestone = project?.Milestones.FirstOrDefault(m => m.Title.Contains("Initial Project Allocation Form (IAF)", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase));
                if (iafMilestone != null)
                {
                    FYPTracker.Utils.IafPdfGenerator.EnsurePdfSnapshots(iafMilestone, System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot"), _context);

                    try
                    {
                        var activeBooking = await _context.IAFDefenseBookings
                            .AsNoTracking()
                            .Include(b => b.Examiner)
                            .Include(b => b.AcademicSession)
                            .Where(b => b.MilestoneId == iafMilestone.Id && b.BookingStatus != "Cancelled")
                            .OrderByDescending(b => b.AttemptNumber)
                            .ThenByDescending(b => b.Id)
                            .FirstOrDefaultAsync();

                        ViewBag.ScheduledIafBooking = activeBooking;
                    }
                    catch
                    {
                        // Safely handle environments or in-memory contexts where table is absent
                    }
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

        
        [HttpGet]
        public async Task<IActionResult> ViewDigitalForm(
            int milestoneId,
            int attemptNumber)
        {
            var user = await GetCurrentUserAsync();

            if (user == null)
                return RedirectToAction("Login", "User");

            if (attemptNumber < 1)
                return NotFound();

            // Only allow the logged-in Student to access
            // IAF milestones belonging to their own project.
            var milestone = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .FirstOrDefaultAsync(m =>
                    m.Id == milestoneId &&
                    m.Project.StudentId == user.Id);

            if (milestone == null)
                return NotFound();



            // Read the existing attempt history.
            string raw = milestone.Remarks ?? "";

            int jsonIndex = raw.IndexOf(
                "JSON: ",
                StringComparison.Ordinal);

            string parsedJson = jsonIndex >= 0
                ? raw.Substring(jsonIndex + "JSON: ".Length)
                : raw;

            var history = IafAttemptHistory.ParseOrMigrate(
                parsedJson);

            if (!history.Attempts.Any(
                a => a.AttemptNumber == attemptNumber))
                return NotFound();

            ViewData["AttemptNumber"] = attemptNumber;
            ViewBag.CurrentUser = user;

            // Reuse the generic digital form viewer.
            return View(
                "~/Views/Supervisor/ViewDigitalForm.cshtml",
                milestone);
        }



        private async Task<AcademicSession?> GetStudentAcademicSessionAsync(int studentId)
        {
            var sessionName = await _context.FYPProjects
                .Where(p => p.StudentId == studentId)
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => p.AcademicSession)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(sessionName))
                return null;

            return await _context.AcademicSessions
                .FirstOrDefaultAsync(s => s.Name == sessionName);
        }

        // ══════════════════════════════════════════════════════
        //  BOOK MEETING
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> BookMeeting()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUser = user;

            var approvedRequest = await _context.SupervisorRequests
                .Include(r => r.Supervisor)
                .FirstOrDefaultAsync(r => r.StudentId == user.Id && r.Status == "Approved");

            ViewBag.AssignedSupervisor = approvedRequest?.Supervisor;
            ViewBag.StartHour = 8;
            ViewBag.EndHour = 17;

            // Determine active stage for student:
            // When Stage1Grade == "Pass", Stage 2 is automatically the active stage for new bookings.
            var project = await _context.FYPProjects
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);
            int activeStage = (project?.Stage1Grade == "Pass") ? 2 : 1;
            ViewBag.ActiveStage = activeStage;
            ViewBag.Stage1Passed = (project?.Stage1Grade == "Pass");

            // ── Check if supervisor has configured any availability ──
            bool supervisorHasAvailability = false;
            var activeSession = await GetStageSessionAsync(user.Id, activeStage) ?? await GetStudentSessionAsync(user.Id);
            ViewBag.ActiveSession = activeSession;
            var bookingWindow = await _academicSessionResolver.GetStageBookingWindowAsync(user.Id, activeStage);
            ViewBag.BookingWindow = bookingWindow;
            ViewBag.BookingStartDate = bookingWindow.IsValid ? bookingWindow.StartDate : activeSession?.StartDate;
            ViewBag.MeetingBookingCutoff = bookingWindow.IsValid ? bookingWindow.EndDate : await GetMeetingBookingCutoffAsync(user.Id, activeStage);
            ViewBag.BookingWindowError = bookingWindow.IsValid ? null : bookingWindow.ErrorMessage;
            
            if (approvedRequest?.Supervisor != null)
            {
                if (activeSession != null)
                {
                    supervisorHasAvailability = await _context.SupervisorTimetableEntries
                        .AnyAsync(t => t.SupervisorId == approvedRequest.Supervisor.Id && t.AcademicSessionId == activeSession.Id);
                }
            }
            ViewBag.SupervisorHasAvailability = supervisorHasAvailability;

            var activeSchedule = await _context.WeeklyMeetingSchedules
                .FirstOrDefaultAsync(s => s.StudentId == user.Id && 
                                          (activeSession == null || s.AcademicSessionId == activeSession.Id) && 
                                          s.Stage == activeStage &&
                                          s.IsActive);
            ViewBag.ActiveSchedule = activeSchedule;
            ViewBag.FixedTime = null;

            // Auto-Expire Stale Pending Meetings (disabled in Testing on FYPTracker_Test to allow testing historical meeting workflow)
            bool isFlexibleTesting = FYPTracker.Helpers.TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _configuration);
            if (!isFlexibleTesting)
            {
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
            }

            var meetings = await _context.MeetingBookings
                .Include(m => m.Supervisor)
                .Include(m => m.MeetingLog)
                .Where(m => m.StudentId == user.Id)
                .OrderByDescending(m => m.CreatedDate)
                .ToListAsync();

            ViewBag.Meetings = meetings;
            
            ViewBag.ProgressStage1 = await _meetingRequirementService.GetStudentProgressAsync(user.Id, 1);
            ViewBag.ProgressStage2 = await _meetingRequirementService.GetStudentProgressAsync(user.Id, 2);

            var pendingReschedules = await _context.MeetingRescheduleRequests
                .Where(r => r.StudentId == user.Id && (r.Status == "Pending" || r.Status == "CounterProposed"))
                .ToListAsync();
            ViewBag.PendingReschedules = pendingReschedules;

            // Detect and attach all unresolved Busy Override conflicts for this student
            var activeConflicts = await _weeklyMeetingSchedulingService.GetStudentActiveOverrideMeetingConflictsAsync(user.Id);
            ViewBag.ActiveOverrideConflicts = activeConflicts;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMeetingBooking(
            int supervisorId, string meetingDate, string meetingTime, string? reason, int stage = 1)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (!DateTime.TryParse(meetingDate, out var parsedDate))
            {
                TempData["Error"] = "Invalid date format.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Date must be today or in the future (In Testing with FYPTracker_Test, historical dates within the academic session are permitted)
            bool isFlexibleTesting = FYPTracker.Helpers.TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _configuration);
            if (!isFlexibleTesting && parsedDate.Date < DateTime.Today)
            {
                TempData["Error"] = "Meeting date cannot be in the past.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // ── Academic Session & Stage Booking Window Validation ──
            var project = await _context.FYPProjects.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);
            
            // Server-side stage enforcement:
            // When Stage 1 is passed, all new bookings must belong to Stage 2.
            // Stage 1 meetings remain preserved and cannot accept new bookings once passed.
            int currentStage = (project?.Stage1Grade == "Pass") ? 2 : stage;

            var activeSession = await GetStageSessionAsync(user.Id, currentStage) ?? await GetStudentSessionAsync(user.Id);
            if (activeSession == null || !activeSession.StartDate.HasValue || !activeSession.EndDate.HasValue)
            {
                TempData["Error"] = $"There is no active Academic Session configured with valid dates for Stage {currentStage}. Please contact an admin.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var bookingWindow = await _academicSessionResolver.GetStageBookingWindowAsync(user.Id, currentStage);
            if (!bookingWindow.IsValid || !bookingWindow.StartDate.HasValue || !bookingWindow.EndDate.HasValue)
            {
                TempData["Error"] = bookingWindow.ErrorMessage ?? "Meeting booking window is not properly configured. Please contact Admin.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (parsedDate.Date < bookingWindow.StartDate.Value.Date)
            {
                if (currentStage == 2)
                {
                    TempData["Error"] = $"Stage 2 meetings cannot be scheduled before the Stage 2 academic period starts ({bookingWindow.StartDate.Value:dd MMM yyyy}).";
                }
                else
                {
                    TempData["Error"] = $"Meeting date cannot be before the academic session start date ({bookingWindow.StartDate.Value:dd MMM yyyy}).";
                }
                return RedirectToAction(nameof(BookMeeting));
            }

            if (parsedDate.Date > bookingWindow.EndDate.Value.Date)
            {
                TempData["Error"] = $"Meeting date cannot be after the academic session end date ({bookingWindow.EndDate.Value:dd MMM yyyy}).";
                return RedirectToAction(nameof(BookMeeting));
            }

            // ── Guard checks for new Timetable availability are handled below ──

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

            // Validations handled above
            bool hasTimetable = await _context.SupervisorTimetableEntries.AnyAsync(t => t.SupervisorId == supervisorId && t.AcademicSessionId == activeSession.Id);
            if (!hasTimetable)
            {
                TempData["Error"] = $"Supervisor has not uploaded a teaching timetable for the Stage {currentStage} academic session ({activeSession.Name}) yet.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var availabilities = await _context.SupervisorAvailabilities.Where(a => a.SupervisorId == supervisorId).ToListAsync();
            var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == parsedDate.Date);

            // 1. Retrieve Admin-Configured Meeting Duration (Strict: Never assume fallback)
            var durationMinutes = await _weeklyMeetingSchedulingService.GetMeetingDurationMinutesAsync();
            if (!durationMinutes.HasValue)
            {
                TempData["Error"] = "Supervisor meeting duration is not configured in Admin Settings. Please contact an administrator.";
                return RedirectToAction(nameof(BookMeeting));
            }

            int meetingDur = durationMinutes.Value;

            if (!TimeSpan.TryParse(meetingTime, out var reqTime))
            {
                TempData["Error"] = "Selected time format is invalid.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (!isFlexibleTesting && parsedDate.Date == DateTime.Now.Date && reqTime < DateTime.Now.TimeOfDay)
            {
                TempData["Error"] = "Cannot book a meeting in the past.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var reqEnd = reqTime.Add(TimeSpan.FromMinutes(meetingDur));

            // 2. Comprehensive Server-Side Conflict Validation
            var (hasConflict, conflictReason) = await _weeklyMeetingSchedulingService.ValidateMeetingConflictAsync(
                supervisorId, parsedDate.Date, reqTime, reqEnd, activeSession.Id);

            if (hasConflict)
            {
                TempData["Error"] = conflictReason;
                return RedirectToAction(nameof(BookMeeting));
            }

            // Academic Policy: Maximum 14 meetings per stage.
            var activeBookingsInStage = await _context.MeetingBookings
                .CountAsync(m => m.StudentId == user.Id && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired");

            if (activeBookingsInStage >= 14)
            {
                TempData["Error"] = $"Maximum limit reached: You cannot book more than 14 meetings for Stage {currentStage}.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var startOfWeek = parsedDate.Date.AddDays(-(int)parsedDate.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);
            var existingThisWeek = await _context.MeetingBookings
                .AnyAsync(m => m.StudentId == user.Id && m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired" && m.Status != "Postponed" && m.MeetingDate >= startOfWeek && m.MeetingDate < endOfWeek);

            if (existingThisWeek)
            {
                TempData["Error"] = "You can only book one meeting per week.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Check active weekly recurring schedule day and time constraints
            {
                var activeSchedule = await _context.WeeklyMeetingSchedules
                    .FirstOrDefaultAsync(s => s.StudentId == user.Id && s.AcademicSessionId == activeSession.Id && s.Stage == currentStage && s.IsActive);
                if (activeSchedule != null)
                {
                    if (parsedDate.DayOfWeek != activeSchedule.LockedDayOfWeek)
                    {
                        TempData["Error"] = $"Your weekly recurring meeting is scheduled for {activeSchedule.LockedDayOfWeek}s. You cannot book on {parsedDate.DayOfWeek}. If you need to meet on a different day, please request a reschedule.";
                        return RedirectToAction(nameof(BookMeeting));
                    }

                    if (reqTime != activeSchedule.LockedTime)
                    {
                        string displayLocked = DateTime.Today.Add(activeSchedule.LockedTime).ToString("h:mm tt");
                        TempData["Error"] = $"Your weekly recurring meeting is locked to {displayLocked}. You cannot book at {meetingTime}.";
                        return RedirectToAction(nameof(BookMeeting));
                    }
                }
            }

            // Calculate WeekNumber for this stage (starts at Week 1 independent of other stages)
            // Each approved/pending meeting takes the next available sequential week number up to 14.
            int calculatedWeekNumber = 1;
            var maxExistingWeekInStage = (await _context.MeetingBookings
                .Where(m => m.StudentId == user.Id && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled")
                .MaxAsync(m => (int?)m.WeekNumber)) ?? 0;

            if (maxExistingWeekInStage > 0)
            {
                calculatedWeekNumber = maxExistingWeekInStage + 1;
            }

            if (calculatedWeekNumber > 14)
            {
                TempData["Error"] = $"Maximum limit reached: All 14 weekly meeting slots for Stage {currentStage} are already allocated.";
                return RedirectToAction(nameof(BookMeeting));
            }

            int? projectId = project?.Id;
            DateTime startDateTime = parsedDate.Date.Add(reqTime);
            DateTime endDateTime = parsedDate.Date.Add(reqEnd);

            var booking = new MeetingBooking
            {
                StudentId         = user.Id,
                SupervisorId      = supervisorId,
                FYPProjectId      = projectId,
                Stage             = currentStage,
                WeekNumber        = calculatedWeekNumber,
                MeetingDate       = parsedDate.Date,
                MeetingDay        = parsedDate.DayOfWeek.ToString(),
                MeetingTime       = reqTime.ToString(@"hh\:mm"),
                StartDateTime     = startDateTime,
                EndDateTime       = endDateTime,
                Reason            = reason,
                Status            = "Pending",
                AcademicSessionId = activeSession.Id,
                CreatedDate       = DateTime.Now
            };
            _context.MeetingBookings.Add(booking);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "MeetingBooked", "Meeting Request Sent",
                $"Meeting for Stage {currentStage} (Week {calculatedWeekNumber}) on {parsedDate:dd MMM yyyy} at {meetingTime} requested.");
            await AddNotificationAsync(user.Id, "MeetingUpdate", "Meeting Request Submitted",
                $"Your Stage {currentStage} meeting request for {parsedDate:dd MMM yyyy} at {meetingTime} is pending approval.");

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
                string message = $"<p>Dear {supervisor.FullName},</p><br/><p>You have a new meeting booking request from <strong>{user.FullName}</strong>.</p><ul><li><strong>Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>Time:</strong> {meetingTime}</li><li><strong>Reason:</strong> {reason}</li></ul>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string supBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(supSubject, message, "Log In to View Request", loginUrl);
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
                string message = $"<p>Dear {user.FullName},</p><br/><p>Your meeting request with <strong>{supName}</strong> has been successfully submitted and is currently pending approval.</p><ul><li><strong>Date:</strong> {parsedDate:dd MMM yyyy}</li><li><strong>Time:</strong> {meetingTime}</li><li><strong>Reason:</strong> {reason}</li></ul><p>You will be notified once the supervisor responds.</p>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string stuBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(stuSubject, message, "Log In to View Details", loginUrl);
                _emailService.Send(user.Email, stuSubject, stuBody);
            }

            TempData["Success"] = $"Meeting request sent to {supName}!";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpGet]
        public async Task<JsonResult> GetAvailableSlots(int supervisorId, string date, int stage = 1, int? excludeMeetingId = null)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { slots = new List<string>(), message = "Invalid date" });

            // Get supervisor's schedule logic
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Json(new { slots = new List<string>(), message = "Please log in." });

            // Validate meeting date against session and final milestone cutoff
            var fypProject = await _context.FYPProjects.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);
            
            // If student has passed Stage 1 and stage wasn't explicitly specified as 1 (e.g. for a stage 1 reschedule), default to Stage 2
            int currentStage = stage;
            if (fypProject?.Stage1Grade == "Pass" && !excludeMeetingId.HasValue)
            {
                currentStage = 2;
            }
            else if (excludeMeetingId.HasValue)
            {
                var existingMeeting = await _context.MeetingBookings.FindAsync(excludeMeetingId.Value);
                if (existingMeeting?.Stage != null)
                {
                    currentStage = existingMeeting.Stage.Value;
                }
            }

            var activeSession = await GetStageSessionAsync(user.Id, currentStage) ?? await GetStudentSessionAsync(user.Id);
            if (activeSession == null)
                return Json(new { slots = new List<string>(), message = $"No active academic session found for Stage {currentStage}." });

            var bookingWindow = await _academicSessionResolver.GetStageBookingWindowAsync(user.Id, currentStage);
            bool isFlexibleTesting = FYPTracker.Helpers.TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _configuration);

            if (!bookingWindow.IsValid ||
                !bookingWindow.StartDate.HasValue ||
                !bookingWindow.EndDate.HasValue ||
                (!isFlexibleTesting && parsedDate.Date < DateTime.Today) ||
                parsedDate.Date < bookingWindow.StartDate.Value.Date ||
                parsedDate.Date > bookingWindow.EndDate.Value.Date)
            {
                return Json(new
                {
                    slots = new List<string>(),
                    noAvailability = true,
                    message = bookingWindow.ErrorMessage ?? "Selected date is outside the allowed meeting period."
                });
            }

            bool hasTimetable = await _context.SupervisorTimetableEntries.AnyAsync(t => t.SupervisorId == supervisorId && t.AcademicSessionId == activeSession.Id);
            if (!hasTimetable)
                return Json(new { slots = new List<string>(), message = $"Supervisor has not uploaded a teaching timetable for {activeSession.Name} yet." });

            var availabilities = await _context.SupervisorAvailabilities.Where(a => a.SupervisorId == supervisorId).ToListAsync();
            var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == parsedDate.Date);

            if (overrideAvail != null && !overrideAvail.IsAvailable)
            {
                return Json(new { slots = new List<string>(), message = "Supervisor is unavailable on this date." });
            }

            var durationMinutes = await _weeklyMeetingSchedulingService.GetMeetingDurationMinutesAsync();
            if (!durationMinutes.HasValue)
            {
                return Json(new
                {
                    slots = new List<string>(),
                    noAvailability = true,
                    message = "Supervisor meeting duration is not configured in Admin Settings. Please contact an administrator."
                });
            }

            int meetingDur = durationMinutes.Value;

            // Strict: Configured Lecturer Working Hours
            var startSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");
            var endSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");

            if (startSetting == null || string.IsNullOrWhiteSpace(startSetting.Value) ||
                !TimeSpan.TryParse(startSetting.Value, out TimeSpan workStart) ||
                endSetting == null || string.IsNullOrWhiteSpace(endSetting.Value) ||
                !TimeSpan.TryParse(endSetting.Value, out TimeSpan workEnd) ||
                workStart >= workEnd)
            {
                return Json(new
                {
                    slots = new List<string>(),
                    noAvailability = true,
                    message = "Lecturer working hours are not configured in Admin Settings."
                });
            }

            // Validate excludeMeetingId ownership and status (do not trust unverified client-provided ID)
            int? validExcludeMeetingId = null;
            if (excludeMeetingId.HasValue)
            {
                var meetingToExclude = await _context.MeetingBookings
                    .Include(m => m.MeetingLog)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == excludeMeetingId.Value && m.StudentId == user.Id);

                // Only allow excluding if the meeting belongs to this student and is in Approved, not-yet-completed status
                if (meetingToExclude != null && 
                    meetingToExclude.Status == "Approved" && 
                    meetingToExclude.MeetingDate.Date >= DateTime.Today &&
                    (meetingToExclude.MeetingLog == null || meetingToExclude.MeetingLog.LogStatus != "Signed"))
                {
                    validExcludeMeetingId = meetingToExclude.Id;
                }
            }

            // Academic Policy: Maximum 14 meetings per stage
            var activeBookingsInStage = await _context.MeetingBookings
                .CountAsync(m => m.StudentId == user.Id && m.Stage == currentStage && m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired");

            if (!excludeMeetingId.HasValue && activeBookingsInStage >= 14)
            {
                return Json(new
                {
                    slots = new List<string>(),
                    noAvailability = true,
                    message = $"Maximum limit reached: You cannot book more than 14 meetings for Stage {currentStage}."
                });
            }

            // 1. Validate One-Meeting-Per-Week Rule
            {
                // 1. Validate One-Meeting-Per-Week Rule
                int diff = (7 + (parsedDate.Date.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfWeek = parsedDate.Date.AddDays(-diff);
                var endOfWeek = startOfWeek.AddDays(7);

                var existingThisWeek = await _context.MeetingBookings
                    .Include(m => m.MeetingLog)
                    .FirstOrDefaultAsync(m => m.StudentId == user.Id && 
                                              (validExcludeMeetingId == null || m.Id != validExcludeMeetingId.Value) &&
                                              m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired" && m.Status != "Postponed" &&
                                              m.MeetingDate >= startOfWeek && m.MeetingDate < endOfWeek);

                if (existingThisWeek != null)
                {
                    string formattedTime = existingThisWeek.MeetingTime;
                    try {
                        if (TimeSpan.TryParse(existingThisWeek.MeetingTime, out var ts))
                            formattedTime = DateTime.Today.Add(ts).ToString("h:mm tt");
                    } catch {}

                    bool canReschedule = existingThisWeek.Status == "Approved" &&
                                         existingThisWeek.MeetingDate.Date >= DateTime.Today &&
                                         (existingThisWeek.MeetingLog == null || existingThisWeek.MeetingLog.LogStatus != "Signed");

                    string weekMessage = existingThisWeek.Status == "Pending"
                        ? $"You already have a Pending meeting request for this week on {existingThisWeek.MeetingDate:dd MMM yyyy} at {formattedTime}. Please wait for your supervisor's approval or rejection before requesting changes. You can only book one meeting per week."
                        : $"You already have an {existingThisWeek.Status} meeting scheduled for this week on {existingThisWeek.MeetingDate:dd MMM yyyy} at {formattedTime}. You can only book one meeting per week.";

                    return Json(new
                    {
                        slots = new List<string>(),
                        hasExistingMeetingThisWeek = true,
                        existingMeetingId = existingThisWeek.Id,
                        existingMeetingStatus = existingThisWeek.Status,
                        existingMeetingDate = existingThisWeek.MeetingDate.ToString("yyyy-MM-dd"),
                        existingMeetingDateDisplay = existingThisWeek.MeetingDate.ToString("dd MMM yyyy"),
                        existingMeetingTime = formattedTime,
                        canReschedule = canReschedule,
                        message = weekMessage
                    });
                }

                // 2. Validate Active Recurring Weekly Meeting Schedule (when not rescheduling)
                if (!validExcludeMeetingId.HasValue)
                {
                    var activeSchedule = await _context.WeeklyMeetingSchedules
                        .FirstOrDefaultAsync(s => s.StudentId == user.Id && 
                                                  s.AcademicSessionId == activeSession.Id && 
                                                  s.Stage == currentStage && 
                                                  s.IsActive);

                    DayOfWeek? lockedDay = activeSchedule?.LockedDayOfWeek;
                    TimeSpan? lockedTime = activeSchedule?.LockedTime;

                    if (!lockedDay.HasValue || !lockedTime.HasValue)
                    {
                        var approvedInitial = await _context.MeetingBookings
                            .Where(m => m.StudentId == user.Id && 
                                        m.AcademicSessionId == activeSession.Id && 
                                        m.Stage == currentStage && 
                                        m.Status == "Approved" && 
                                        m.ParentMeetingBookingId == null)
                            .OrderBy(m => m.MeetingDate)
                            .FirstOrDefaultAsync();

                        if (approvedInitial != null && TimeSpan.TryParse(approvedInitial.MeetingTime, out var appTime))
                        {
                            lockedDay = approvedInitial.MeetingDate.DayOfWeek;
                            lockedTime = appTime;
                        }
                    }

                    if (lockedDay.HasValue && lockedTime.HasValue)
                    {
                        // If selected day does not match locked recurring weekday
                        if (parsedDate.DayOfWeek != lockedDay.Value)
                        {
                            string displayLocked = DateTime.Today.Add(lockedTime.Value).ToString("h:mm tt");
                            return Json(new
                            {
                                slots = new List<string>(),
                                isDayMismatch = true,
                                lockedDay = lockedDay.Value.ToString(),
                                lockedTime = displayLocked,
                                message = $"Your weekly meeting schedule is locked to {lockedDay.Value}s at {displayLocked}. {parsedDate.DayOfWeek} is not your recurring meeting day. If you need to meet on a different day, please request a reschedule for that specific week."
                            });
                        }

                        // Matching weekday: revalidate the locked slot on this date against timetable/overrides/defenses
                        var lStart = lockedTime.Value;
                        var lEnd = lStart.Add(TimeSpan.FromMinutes(meetingDur));
                        var (hasConflict, conflictReason) = await _weeklyMeetingSchedulingService.ValidateMeetingConflictAsync(
                            supervisorId, parsedDate.Date, lStart, lEnd, activeSession.Id, validExcludeMeetingId);

                        string displayLockedTime = DateTime.Today.Add(lStart).ToString("h:mm tt");
                        string rawLockedTime = lStart.ToString(@"hh\:mm");

                        if (hasConflict)
                        {
                            return Json(new
                            {
                                slots = new List<string>(),
                                isLockedTimeConflicted = true,
                                lockedTime = rawLockedTime,
                                lockedTimeDisplay = displayLockedTime,
                                conflictReason = conflictReason,
                                message = $"Your recurring meeting time ({displayLockedTime}) is unavailable on {parsedDate:dd MMM yyyy}: {conflictReason}. Please request a reschedule for this week."
                            });
                        }
                        else
                        {
                            return Json(new
                            {
                                slots = new List<string> { rawLockedTime },
                                displaySlots = new List<string> { displayLockedTime },
                                isLocked = true,
                                lockedTime = rawLockedTime,
                                lockedTimeDisplay = displayLockedTime,
                                message = "Success"
                            });
                        }
                    }
                }
            }

            // 3. Normal / Reschedule Available Slots
            TimeSpan allowedStart = (overrideAvail != null && overrideAvail.StartTime > workStart) ? overrideAvail.StartTime : workStart;
            TimeSpan allowedEnd = (overrideAvail != null && overrideAvail.EndTime < workEnd) ? overrideAvail.EndTime : workEnd;

            var finalSlots = new List<string>();
            var displaySlots = new List<string>();
            var now = _clock.Now;
            bool isToday = parsedDate.Date == now.Date;

            var current = allowedStart;
            while (current.Add(TimeSpan.FromMinutes(meetingDur)) <= allowedEnd)
            {
                var slotEnd = current.Add(TimeSpan.FromMinutes(meetingDur));

                if (isFlexibleTesting || !(isToday && now.TimeOfDay > current))
                {
                    var (hasConflict, _) = await _weeklyMeetingSchedulingService.ValidateMeetingConflictAsync(
                        supervisorId, parsedDate.Date, current, slotEnd, activeSession.Id, validExcludeMeetingId);

                    if (!hasConflict)
                    {
                        finalSlots.Add(current.ToString(@"hh\:mm"));
                        displaySlots.Add(DateTime.Today.Add(current).ToString("h:mm tt"));
                    }
                }

                current = current.Add(TimeSpan.FromMinutes(meetingDur));
            }

            return Json(new { slots = finalSlots, displaySlots = displaySlots, message = "Success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMeeting(int meetingId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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
            if (user == null) return RedirectToAction("Login", "User");

            var meeting = await _context.MeetingBookings
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && m.Status == "Postponed");

            if (meeting != null && meeting.ProposedNewDate.HasValue)
            {
                // Validate proposed date before accepting
                int stageToUse = meeting.Stage ?? 1;
                var bookingWindow = await _academicSessionResolver.GetStageBookingWindowAsync(user.Id, stageToUse);
                var proposedDate = meeting.ProposedNewDate.Value.Date;
                bool isFlexibleTesting = FYPTracker.Helpers.TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _configuration);

                if (!bookingWindow.IsValid ||
                    !bookingWindow.StartDate.HasValue ||
                    !bookingWindow.EndDate.HasValue ||
                    (!isFlexibleTesting && proposedDate < DateTime.Today) ||
                    proposedDate < bookingWindow.StartDate.Value.Date ||
                    proposedDate > bookingWindow.EndDate.Value.Date)
                {
                    TempData["Error"] =
                        bookingWindow.ErrorMessage ?? "Proposed meeting date is outside the allowed meeting period.";

                    return RedirectToAction(nameof(BookMeeting));
                }

                var reqTimeStr = meeting.MeetingTime;
                if (!TimeSpan.TryParse(reqTimeStr, out var reqTime)) {
                    TempData["Error"] = "Invalid meeting time.";
                    return RedirectToAction(nameof(BookMeeting));
                }
                var reqEnd = reqTime.Add(TimeSpan.FromMinutes(30));

                var bookedMeetings = await _context.MeetingBookings
                    .Where(m => m.SupervisorId == meeting.SupervisorId && 
                                m.MeetingDate.Date == proposedDate && 
                                m.Id != meeting.Id &&
                                m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired")
                    .ToListAsync();

                bool isSlotTaken = bookedMeetings.Any(b => 
                {
                    if (!TimeSpan.TryParse(b.MeetingTime, out var bStart)) return false;
                    var bEnd = bStart.Add(TimeSpan.FromMinutes(30));
                    return (reqTime >= bStart && reqTime < bEnd) || 
                           (reqEnd > bStart && reqEnd <= bEnd) ||
                           (reqTime <= bStart && reqEnd >= bEnd);
                });

                if (isSlotTaken)
                {
                    TempData["Error"] = "This time slot was just booked by another student. Please cancel and propose a new meeting.";
                    return RedirectToAction(nameof(BookMeeting));
                }

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
        public async Task<IActionResult> RescheduleMeeting(int meetingId, string meetingDate, string meetingTime, string reason)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (!DateTime.TryParse(meetingDate, out var parsedDate))
            {
                TempData["Error"] = "Invalid date format.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
            {
                TempData["Error"] = "Please provide a valid reason (minimum 10 characters).";
                return RedirectToAction(nameof(BookMeeting));
            }

            var meeting = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id);

            if (meeting == null)
            {
                TempData["Error"] = "Meeting not found or you are not authorized to reschedule it.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Strict Status Validation: Only Approved, not-yet-completed meetings can be rescheduled
            if (meeting.Status == "Pending")
            {
                TempData["Error"] = "Cannot reschedule a pending meeting request. You must wait for your supervisor's approval or rejection.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.Status == "Rejected")
            {
                TempData["Error"] = "Cannot reschedule a rejected meeting. Please book a new meeting instead.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.Status == "Cancelled")
            {
                TempData["Error"] = "Cannot reschedule a cancelled meeting.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.Status != "Approved")
            {
                TempData["Error"] = $"Cannot reschedule meeting: only Approved, not-yet-completed meetings can be rescheduled. Current status is '{meeting.Status}'.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Not-yet-completed verification
            if (meeting.MeetingDate.Date < DateTime.Today)
            {
                TempData["Error"] = "Cannot reschedule a past or completed meeting.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.MeetingLog != null && meeting.MeetingLog.LogStatus == "Signed")
            {
                TempData["Error"] = "Cannot reschedule a meeting that has already been completed with a signed logbook.";
                return RedirectToAction(nameof(BookMeeting));
            }

            int stageToUse = meeting.Stage ?? 1;
            var activeSession = await GetStageSessionAsync(user.Id, stageToUse) ?? await GetStudentSessionAsync(user.Id);

            if (activeSession == null || meeting.SupervisorId <= 0)
            {
                TempData["Error"] = "Meeting has invalid supervisor or academic session assignment.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var bookingWindow = await _academicSessionResolver.GetStageBookingWindowAsync(user.Id, stageToUse);
            bool isFlexibleTesting = FYPTracker.Helpers.TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _configuration);

            if (!bookingWindow.IsValid ||
                !bookingWindow.StartDate.HasValue ||
                !bookingWindow.EndDate.HasValue ||
                (!isFlexibleTesting && parsedDate.Date < DateTime.Today) ||
                parsedDate.Date < bookingWindow.StartDate.Value.Date ||
                parsedDate.Date > bookingWindow.EndDate.Value.Date)
            {
                TempData["Error"] = bookingWindow.ErrorMessage ?? "Rescheduled meeting date is outside the allowed academic session period.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var durationMinutes = await _weeklyMeetingSchedulingService.GetMeetingDurationMinutesAsync();
            if (!durationMinutes.HasValue)
            {
                TempData["Error"] = "Supervisor meeting duration is not configured in Admin Settings. Please contact an administrator.";
                return RedirectToAction(nameof(BookMeeting));
            }

            int meetingDur = durationMinutes.Value;

            if (!CommonExtensions.TryParseMeetingTime(meetingTime, out var reqTime)) {
                TempData["Error"] = "Invalid meeting time.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Reject no-op reschedule requests where proposed date and time are identical to current booking
            if (CommonExtensions.TryParseMeetingTime(meeting.MeetingTime, out var currentBookingTime))
            {
                if (parsedDate.Date == meeting.MeetingDate.Date && currentBookingTime == reqTime)
                {
                    TempData["Error"] = "The proposed date and time are the same as your current meeting. Please select a different date or time.";
                    return RedirectToAction(nameof(BookMeeting));
                }
            }
            var reqEnd = reqTime.Add(TimeSpan.FromMinutes(meetingDur));

            // Validate against lecturer timetable, date overrides, other meetings, defenses, vivas
            var (hasConflict, conflictReason) = await _weeklyMeetingSchedulingService.ValidateMeetingConflictAsync(
                meeting.SupervisorId, parsedDate.Date, reqTime, reqEnd, activeSession.Id, ignoreMeetingId: meeting.Id);

            if (hasConflict)
            {
                TempData["Error"] = conflictReason;
                return RedirectToAction(nameof(BookMeeting));
            }

            // Prevent multiple active reschedule requests
            var existingRequest = await _context.MeetingRescheduleRequests
                .AnyAsync(r => r.MeetingBookingId == meeting.Id && (r.Status == "Pending" || r.Status == "CounterProposed"));
            
            if (existingRequest)
            {
                TempData["Error"] = "There is already an active reschedule request for this meeting.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // Create a Reschedule Request instead of editing the booking directly
            var request = new MeetingRescheduleRequest
            {
                MeetingBookingId = meeting.Id,
                StudentId = user.Id,
                SupervisorId = meeting.SupervisorId,
                OriginalDate = meeting.MeetingDate,
                OriginalTime = meeting.MeetingTime,
                ProposedDate = parsedDate.Date,
                ProposedTime = reqTime.ToString(@"hh\:mm"),
                Reason = reason,
                Status = "Pending",
                CreatedDate = DateTime.Now
            };

            _context.MeetingRescheduleRequests.Add(request);
            await _context.SaveChangesAsync();

            await LogActivityAsync(user.Id, "MeetingUpdate", "Meeting Reschedule Requested", 
                $"You submitted a request to reschedule the meeting on {meeting.MeetingDate:dd MMM yyyy}.");

            if (meeting.SupervisorId > 0)
            {
                await AddNotificationAsync(meeting.SupervisorId, "Meeting", "Reschedule Request",
                    $"Student {user.FullName} requested to reschedule their meeting on {meeting.MeetingDate:dd MMM yyyy}.");

                var reschedulePayload = new {
                    Type = "MeetingRescheduled",
                    MeetingId = meeting.Id,
                    Session = user.AcademicSession,
                    StudentName = user.FullName,
                    Title = "Reschedule Request",
                    Message = $"Student {user.FullName} requested to reschedule."
                };
                await _hubContext.Clients.Group($"User_{meeting.SupervisorId}").SendAsync("ReceiveMeetingUpdate", reschedulePayload);
            }

            TempData["Success"] = "Reschedule request submitted. Pending supervisor approval.";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptRescheduleProposal(int requestId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var request = await _context.MeetingRescheduleRequests
                .Include(r => r.MeetingBooking)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.StudentId == user.Id && r.Status == "CounterProposed");
            
            if (request == null)
            {
                TempData["Error"] = "Reschedule proposal not found, already processed, or expired.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (request.MeetingBooking == null || (request.MeetingBooking.Status != "Approved" && request.MeetingBooking.Status != "Pending"))
            {
                TempData["Error"] = "The meeting associated with this proposal is no longer active.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (request.ProposedDate.Date < DateTime.Today)
            {
                TempData["Error"] = "Cannot accept proposal: The proposed date has already passed.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var studentSessionName = await _context.FYPProjects
                .Where(p => p.StudentId == request.StudentId)
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => p.AcademicSession)
                .FirstOrDefaultAsync();
            var studentSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == studentSessionName)
                                 ?? await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Id == request.MeetingBooking.AcademicSessionId);

            if (studentSession == null || (studentSession.StartDate.HasValue && request.ProposedDate.Date < studentSession.StartDate.Value.Date))
            {
                TempData["Error"] = "The proposed date is invalid or before the academic session starts.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var meetingDur = await _weeklyMeetingSchedulingService.GetMeetingDurationMinutesAsync();
            if (!meetingDur.HasValue)
            {
                TempData["Error"] = "The supervisor meeting duration setting is not configured by the Admin. Please contact system administrator.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (!CommonExtensions.TryParseMeetingTime(request.ProposedTime, out var startTimeSpan))
            {
                TempData["Error"] = "The proposed time is invalid.";
                return RedirectToAction(nameof(BookMeeting));
            }

            string normalizedMeetingTime = startTimeSpan.ToString(@"hh\:mm");
            var startDateTime = request.ProposedDate.Date.Add(startTimeSpan);
            var endDateTime = startDateTime.AddMinutes(meetingDur.Value);

            // Validate one meeting per week policy (excluding this meeting)
            int diff = (7 + (request.ProposedDate.DayOfWeek - DayOfWeek.Monday)) % 7;
            var startOfProposedWeek = request.ProposedDate.Date.AddDays(-1 * diff);
            var endOfProposedWeek = startOfProposedWeek.AddDays(7);

            var otherMeetingInProposedWeek = await _context.MeetingBookings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.StudentId == user.Id &&
                                          m.Id != request.MeetingBookingId &&
                                          m.MeetingDate >= startOfProposedWeek &&
                                          m.MeetingDate < endOfProposedWeek &&
                                          m.Status != "Rejected" && m.Status != "Cancelled" && m.Status != "Expired" && m.Status != "Postponed");

            if (otherMeetingInProposedWeek != null)
            {
                TempData["Error"] = $"Cannot accept proposal: You already have another meeting (Booking #{otherMeetingInProposedWeek.Id} on {otherMeetingInProposedWeek.MeetingDate:dd MMM yyyy}) scheduled for that week. FYP policy allows only one meeting per week.";
                return RedirectToAction(nameof(BookMeeting));
            }

            var conflictReason = await _weeklyMeetingSchedulingService.ValidateMeetingConflictAsync(
                request.SupervisorId,
                startDateTime,
                endDateTime,
                excludeMeetingId: request.MeetingBooking.Id,
                sessionId: studentSession?.Id ?? request.MeetingBooking.AcademicSessionId);

            if (conflictReason != null)
            {
                TempData["Error"] = $"Cannot accept reschedule: {conflictReason}";
                return RedirectToAction(nameof(BookMeeting));
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                request.MeetingBooking.MeetingDate = request.ProposedDate;
                request.MeetingBooking.MeetingTime = normalizedMeetingTime;
                request.MeetingBooking.MeetingDay = request.ProposedDate.DayOfWeek.ToString();
                request.MeetingBooking.StartDateTime = startDateTime;
                request.MeetingBooking.EndDateTime = endDateTime;
                if (studentSession != null)
                {
                    request.MeetingBooking.AcademicSessionId = studentSession.Id;
                }
                if (request.MeetingBooking.Status == "Pending")
                {
                    request.MeetingBooking.Status = "Approved";
                }

                request.Status = "Approved";
                request.RespondedDate = DateTime.Now;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await AddNotificationAsync(request.SupervisorId, "Meeting", "Counter Proposal Accepted",
                $"Student {user.FullName} accepted your proposed date of {request.ProposedDate:dd MMM yyyy} at {request.ProposedTime}.");

            TempData["Success"] = "You accepted the supervisor's proposed date. The meeting has been rescheduled.";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineRescheduleProposal(int requestId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var request = await _context.MeetingRescheduleRequests
                .Include(r => r.MeetingBooking)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.StudentId == user.Id && r.Status == "CounterProposed");
            
            if (request == null)
            {
                TempData["Error"] = "Reschedule proposal not found, already processed, or expired.";
                return RedirectToAction(nameof(BookMeeting));
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                request.Status = "Rejected";
                request.RespondedDate = DateTime.Now;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await AddNotificationAsync(request.SupervisorId, "Meeting", "Counter Proposal Rejected",
                $"Student {user.FullName} rejected your proposed date of {request.ProposedDate:dd MMM yyyy} at {request.ProposedTime}.");

            TempData["Success"] = "You rejected the supervisor's proposed date. The original meeting schedule remains unchanged.";
            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRescheduleRequest(int requestId)
        {
            var user = await GetCurrentUserAsync();
            var request = await _context.MeetingRescheduleRequests
                .FirstOrDefaultAsync(r => r.Id == requestId && r.StudentId == user.Id && (r.Status == "Pending" || r.Status == "CounterProposed"));
            
            if (request != null)
            {
                request.Status = "Cancelled";
                request.RespondedDate = DateTime.Now;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Reschedule request cancelled successfully.";
            }

            return RedirectToAction(nameof(BookMeeting));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMeetingLogStudent(
            [FromServices] FYPTracker.Services.Forms.MeetingLogTemplateService templateService,
            int meetingId, 
            string keyTopicsDiscussed,
            string actionPointsForNextMeeting,
            string completedActionsAndNotes,
            DateTime? nextMeetingDate,
            string? nextMeetingTime)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var meeting = await _context.MeetingBookings
                .Include(m => m.Student)
                .Include(m => m.Supervisor)
                .Include(m => m.MeetingLog)
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.StudentId == user.Id && m.Status == "Approved");

            if (meeting == null)
            {
                TempData["Error"] = "Meeting not found or not approved.";
                return RedirectToAction(nameof(BookMeeting));
            }

            if (meeting.MeetingDate.Date > _clock.Today)
            {
                TempData["Error"] = $"This logbook will be available on {meeting.MeetingDate:dd MMM yyyy}.";
                return RedirectToAction(nameof(BookMeeting));
            }

        if (meeting.MeetingLog != null && meeting.MeetingLog.LogStatus == "Signed")
        {
            TempData["Error"] = "Progress logbook is already signed by the supervisor.";
            return RedirectToAction(nameof(BookMeeting));
        }

        // =====================================================
        // AUTO-FILL NEXT MEETING DATE
        // Get the next APPROVED meeting from the database.
        // Student cannot manually override this date.
        // =====================================================

        nextMeetingDate = await _context.MeetingBookings
            .AsNoTracking()
            .Where(n =>
                n.StudentId == user.Id &&
                n.SupervisorId == meeting.SupervisorId &&
                n.Stage == meeting.Stage &&
                n.Status == "Approved" &&
                n.MeetingDate > meeting.MeetingDate)
            .OrderBy(n => n.MeetingDate)
            .ThenBy(n => n.MeetingTime)
            .Select(n => (DateTime?)n.MeetingDate)
            .FirstOrDefaultAsync();

        // =====================================================
        // SAVE MEETING LOGBOOK - KEEP EXISTING CODE
        // =====================================================

            if (meeting.MeetingLog == null)
            {
                meeting.MeetingLog = new MeetingLog
                {
                    MeetingBookingId = meeting.Id,
                    NextMeetingDate = nextMeetingDate,
                    KeyTopicsDiscussed = keyTopicsDiscussed,
                    ActionPointsForNextMeeting = actionPointsForNextMeeting,
                    CompletedActionsAndNotes = completedActionsAndNotes,
                    LogStatus = "Draft",
                    CreatedDate = DateTime.Now
                };
            }
            else
            {
                meeting.MeetingLog.NextMeetingDate = nextMeetingDate;
                meeting.MeetingLog.KeyTopicsDiscussed = keyTopicsDiscussed;
                meeting.MeetingLog.ActionPointsForNextMeeting = actionPointsForNextMeeting;
                meeting.MeetingLog.CompletedActionsAndNotes = completedActionsAndNotes;
                meeting.MeetingLog.LogStatus = "Draft";
            }

            // 1. Determine Stage
            int stage = meeting.Stage ?? 1;
            
            // 2. Load active template
            FYPTracker.ViewModels.MeetingRecordTemplateVM activeTemplate;
            try
            {
                activeTemplate = await templateService.LoadAsync(stage);
            }
            catch (Exception)
            {
                TempData["Error"] = "Failed to load the active meeting template.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // 3. Freeze exactly what we're going to generate
            byte[] pdfBytes;
            try
            {
                pdfBytes = FYPTracker.Utils.MeetingLogPdfGenerator.GeneratePdfBytes(meeting, activeTemplate, isSigned: false);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Failed to generate PDF snapshot: " + ex.Message;
                return RedirectToAction(nameof(BookMeeting));
            }

            string serializedTemplate;
            try 
            {
                serializedTemplate = System.Text.Json.JsonSerializer.Serialize(activeTemplate);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Failed to serialize the meeting template: " + ex.Message;
                return RedirectToAction(nameof(BookMeeting));
            }
            
            string uuid = Guid.NewGuid().ToString().Substring(0, 8);
            string uniquePdfFileName = $"Draft_Log_{meeting.Id}_{uuid}.pdf";
            string uniqueJsonFileName = $"Draft_Template_{meeting.Id}_{uuid}.json";
            string relativePrefix = FYPTracker.Helpers.TestingStorageProvider.GetMeetingLogRelativePrefix(_env, _configuration);
            string newRelativePdfPath = $"{relativePrefix}{uniquePdfFileName}";

            // 4. Safely write files to disk FIRST (isolated in Testing when using FYPTracker_Test)
            string directoryPath = FYPTracker.Helpers.TestingStorageProvider.GetMeetingLogSnapshotsDirectory(_env.ContentRootPath, _env, _configuration);
            string absolutePdfPath = System.IO.Path.Combine(directoryPath, uniquePdfFileName);
            string absoluteJsonPath = System.IO.Path.Combine(directoryPath, uniqueJsonFileName);

            try
            {
                await System.IO.File.WriteAllBytesAsync(absolutePdfPath, pdfBytes);
                await System.IO.File.WriteAllTextAsync(absoluteJsonPath, serializedTemplate);
            }
            catch (Exception)
            {
                if (System.IO.File.Exists(absolutePdfPath)) System.IO.File.Delete(absolutePdfPath);
                if (System.IO.File.Exists(absoluteJsonPath)) System.IO.File.Delete(absoluteJsonPath);
                
                TempData["Error"] = "Failed to save snapshot files to disk.";
                return RedirectToAction(nameof(BookMeeting));
            }

            // 5. Update DB now that files are fully accessible
            meeting.MeetingLog.PdfLogbookPath = newRelativePdfPath;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                // DB Save failed. Clean up the unreferenced files.
                if (System.IO.File.Exists(absolutePdfPath)) System.IO.File.Delete(absolutePdfPath);
                if (System.IO.File.Exists(absoluteJsonPath)) System.IO.File.Delete(absoluteJsonPath);
                
                TempData["Error"] = "Failed to save meeting log to the database.";
                return RedirectToAction(nameof(BookMeeting));
            }

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
            // 1. Verify logged-in student
            var user = await GetCurrentUserAsync();

            if (user == null)
                return RedirectToAction("Login", "User");

            // 2. Student can only view their own signed logbook
            var log = await _context.MeetingLogs
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Supervisor)
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Student)
                .FirstOrDefaultAsync(l =>
                    l.Id == id &&
                    l.MeetingBooking.StudentId == user.Id);

            if (log == null || log.LogStatus != "Signed")
                return NotFound();

            // 3. Read signed PDF from private storage
            string? savedPath = log.SignedPdfLogbookPath?
                .Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                string? relativeDir = null;
                string? fileName = null;

                const string testingPrefix = "App_Data/MeetingLogSnapshots/Testing/";
                const string standardPrefix = "App_Data/MeetingLogSnapshots/";

                if (savedPath.StartsWith(testingPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = "App_Data/MeetingLogSnapshots/Testing";
                    fileName = savedPath.Substring(testingPrefix.Length);
                }
                else if (savedPath.StartsWith(standardPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = "App_Data/MeetingLogSnapshots";
                    fileName = savedPath.Substring(standardPrefix.Length);
                }

                // Prevent invalid paths, directory traversal and access to other files
                if (!string.IsNullOrWhiteSpace(relativeDir) &&
                    !string.IsNullOrWhiteSpace(fileName) &&
                    fileName == Path.GetFileName(fileName) &&
                    fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
                    fileName.StartsWith($"Signed_Log_{log.MeetingBookingId}_", StringComparison.OrdinalIgnoreCase))
                {
                    string physicalPath = Path.Combine(
                        _env.ContentRootPath,
                        relativeDir.Replace('/', Path.DirectorySeparatorChar),
                        fileName
                    );

                    if (System.IO.File.Exists(physicalPath))
                    {
                        // Display PDF directly in the browser
                        return PhysicalFile(
                            physicalPath,
                            "application/pdf",
                            enableRangeProcessing: true
                        );
                    }
                }
            }

            // 4. Fallback if signed PDF was not generated
            var project = await _context.FYPProjects
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync(p =>
                    p.StudentId == log.MeetingBooking.StudentId);

            ViewBag.ProjectName = project?.Title ?? "FYP Project";

            return View(
                "~/Views/Shared/MeetingLogPrintable.cshtml",
                log
            );
        }

        // ══════════════════════════════════════════════════════
        //  NOTIFICATIONS
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Notifications()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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
            if (user == null) return RedirectToAction("Login", "User");

            var notification = await _context.FYPNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientId == user.Id);

            if (notification != null)
            {
                notification.IsRead  = true;
                notification.ReadDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            string referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && referer.Contains("/Student", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(referer);
            }
            return RedirectToAction(nameof(Notifications));
        }

        [HttpGet]
        public async Task<IActionResult> OpenNotification(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var notification = await _context.FYPNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == user.Id);

            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            if (notification != null)
            {
                switch (notification.Type)
                {
                    case "SupervisorApproval": return RedirectToAction(nameof(Index));
                    case "MeetingUpdate": return RedirectToAction(nameof(BookMeeting));
                    case "DeadlineReminder": return RedirectToAction(nameof(ProjectDetails));
                    case "SubmissionFeedback":
                    case "RevisionRequest": return RedirectToAction(nameof(Forms));
                    case "VivaScheduled": return RedirectToAction(nameof(Index));
                }
            }

            return RedirectToAction(nameof(Notifications));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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
                .AnyAsync(u => u.UserRoles.Any(r => r.Role.Name == "Student")
                            && u.Id != user.Id
                            && u.EmployeeId != null
                            && u.EmployeeId.ToLower() == matricNumber.ToLower());

            return Json(new { isDuplicate = duplicateMatric });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var file = await _context.FYPSubmissionFiles.FindAsync(fileId);
            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

            // Security check: ensure the file belongs to this student's project
            var project = await _context.FYPProjects.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.StudentId == user.Id);
            if (project == null) return Unauthorized();
            
            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = System.IO.Path.GetExtension(file.FilePath).ToLowerInvariant();

            string mimeType = "application/octet-stream";
            
            if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

            var physicalPath = System.IO.Path.Combine(_env.WebRootPath, file.FilePath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(physicalPath)) return NotFound("File not found on disk.");

            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{file.FileName}{(string.IsNullOrEmpty(System.IO.Path.GetExtension(file.FileName)) ? ext : "")}\"");
            return PhysicalFile(physicalPath, mimeType);
        }

        // ── Download Official Thesis Cover Page DOCX ─────────────────────────
        [HttpGet]
        public async Task<IActionResult> DownloadThesisCoverPage()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .OrderByDescending(p => p.CreatedDate)
                .FirstOrDefaultAsync(p => p.StudentId == user.Id);

            if (project == null)
            {
                TempData["Error"] = "No active project found for your student account.";
                return RedirectToAction("Forms");
            }

            string baseDir = Path.Combine(_env?.ContentRootPath ?? Directory.GetCurrentDirectory(), "FormTemplates");
            string templateJsonPath = Path.Combine(baseDir, "ThesisCoverPage.default.json");
            if (!System.IO.File.Exists(templateJsonPath))
            {
                TempData["Error"] = "The official Thesis Cover Page template has not been published yet. Please contact the administrator.";
                return RedirectToAction("Forms");
            }

            FYPTracker.ViewModels.MeetingRecordTemplateVM? template = null;
            try
            {
                string json = await System.IO.File.ReadAllTextAsync(templateJsonPath);
                template = System.Text.Json.JsonSerializer.Deserialize<FYPTracker.ViewModels.MeetingRecordTemplateVM>(
                    json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // Fallback: helper generates default structure
            }

            // Retrieve DPPDeadline from FYPSettings
            string? deadlineValue = await _context.FYPSettings
                .AsNoTracking()
                .Where(s => s.Key == "DPPDeadline")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();

            string documentDateDisplay = "";
            string[] acceptedFormats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff" };
            if (DateTime.TryParseExact(deadlineValue?.Trim(), acceptedFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dueDate))
            {
                int day = dueDate.Day;
                string suffix = day % 100 >= 11 && day % 100 <= 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
                documentDateDisplay = $"{day}{suffix} {dueDate:MMMM yyyy}";
            }

            string customDocxPath = Path.Combine(baseDir, "ThesisCoverPage.template.docx");

            byte[] docxBytes = FYPTracker.Utils.CoverPageDocxHelper.GenerateCoverPageDocx(
                template: template,
                thesisTitle: project.Title,
                studentName: user.FullName,
                coventryId: user.CoventryId,
                supervisorName: project.Supervisor?.FullName,
                programmeName: user.Programme,
                documentDate: documentDateDisplay,
                customDocxTemplatePath: System.IO.File.Exists(customDocxPath) ? customDocxPath : null
            );

            string idPart = !string.IsNullOrWhiteSpace(user.CoventryId) ? user.CoventryId : (!string.IsNullOrWhiteSpace(user.EmployeeId) ? user.EmployeeId : "Student");
            string safeId = string.Join("_", idPart.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName = $"Thesis_Cover_Page_{safeId}.docx";

            return File(
                docxBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);
        }

        // ── Download FYP Proposal Word Template ──────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DownloadProposalTemplate()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

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
            if (user == null) return RedirectToAction("Login", "User");

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

            // Chapter 1
            addPara("Chapter 1: Introduction", true, 14, "1F3A7A", false);
            addPara("� Problem Statement", false, 12, "", false);
            addPara("� Detailed Research Question", false, 12, "", false);
            addPara("� Objective", false, 12, "", false);
            addPara("� Clients, Audience, Motivation", false, 12, "", false);
            addPara("� Scope", false, 12, "", false);
            addBlankLine("");
            for (int i = 0; i < 3; i++) addBlankLine("");

            // Chapter 2
            addPara("Chapter 2: Literature Review", true, 14, "1F3A7A", false);
            addPara("� Define the topic", false, 12, "", false);
            addPara("� Study on existing system/existing technologies", false, 12, "", false);
            addPara("� Comparison/Summary of existing technologies/system", false, 12, "", false);
            addPara("� How it has helped derive the proposed work", false, 12, "", false);
            addPara("� Discussion on the sustainability component", false, 12, "", false);
            addBlankLine("");
            for (int i = 0; i < 3; i++) addBlankLine("");

            // Chapter 3
            addPara("Chapter 3: Software Methodology", true, 14, "1F3A7A", false);
            addPara("� Primary Research Plan", false, 12, "", false);
            addPara("� Use Case Diagram (minimum requirement)", false, 12, "", false);
            addPara("� High-fidelity design/prototype", false, 12, "", false);
            addPara("� Gantt Chart", false, 12, "", false);
            addPara("� Survey with respondents, if applicable", false, 12, "", false);
            addBlankLine("");
            for (int i = 0; i < 3; i++) addBlankLine("");

            addPara("Maximum pages: 150�200 pages. Please ensure this requirement is followed.", true, 12, "FF0000", true);
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
            if (user == null) return RedirectToAction("Login", "User");

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
                DateTime startDt;
                if (m.StartDateTime.HasValue)
                {
                    startDt = m.StartDateTime.Value;
                }
                else if (CommonExtensions.TryParseMeetingTime(m.MeetingTime, out var ts))
                {
                    startDt = m.MeetingDate.Date.Add(ts);
                }
                else
                {
                    startDt = m.MeetingDate.Date.AddHours(9);
                }

                DateTime endDt = m.EndDateTime.HasValue ? m.EndDateTime.Value : startDt.AddMinutes(30);
                if (endDt <= startDt)
                {
                    endDt = startDt.AddMinutes(30);
                }

                string color = m.Status == "Approved" ? "#10b981" : "#f59e0b";
                string supName = m.Supervisor?.FullName ?? "Supervisor";
                string title = $"Meeting with {supName}";

                string displayTime = !string.IsNullOrEmpty(m.MeetingTime) ? m.MeetingTime : startDt.ToString("hh:mm tt");
                if (CommonExtensions.TryParseMeetingTime(m.MeetingTime, out var dTs))
                {
                    displayTime = DateTime.Today.Add(dTs).ToString("h:mm tt");
                }

                return new {
                    id = m.Id.ToString(),
                    title = title,
                    start = startDt.ToString("yyyy-MM-ddTHH:mm:ss"),
                    end = endDt.ToString("yyyy-MM-ddTHH:mm:ss"),
                    allDay = false,
                    color = color,
                    textColor = "#ffffff",
                    extendedProps = new {
                        meetingId = m.Id,
                        status = m.Status,
                        location = m.Location ?? "TBA",
                        meetingLink = m.MeetingLink,
                        reason = m.Reason ?? "None",
                        supervisorNote = m.SupervisorNote ?? "None",
                        meetingDate = m.MeetingDate.ToString("yyyy-MM-dd"),
                        meetingDateFormatted = m.MeetingDate.ToString("dd MMM yyyy"),
                        meetingTime = displayTime,
                        timeRange = $"{startDt:h:mm tt} - {endDt:h:mm tt}",
                        supervisorName = supName,
                        stage = m.Stage ?? 1,
                        weekNumber = m.WeekNumber ?? 0
                    }
                };
            }).ToList();

            return Json(events);
        }
    }
}



