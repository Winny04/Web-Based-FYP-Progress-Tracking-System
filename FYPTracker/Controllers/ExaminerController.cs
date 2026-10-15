using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;
using FYPTracker.Services.Defense;

namespace FYPTracker.Controllers
{
    [Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Examiner")]
    public class ExaminerController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        // B3.9: Prepared dependency. Do not invoke while feature is disabled.
        private readonly IIAFAlternativeTimeRequestService? _alternativeTimeRequestService;

        public ExaminerController(FYPTrackerContext context, IWebHostEnvironment env, IEmailService emailService, IHubContext<NotificationHub> hubContext, IIAFAlternativeTimeRequestService? alternativeTimeRequestService = null)
        {
            _context = context;
            _env = env;
            _emailService = emailService;
            _hubContext = hubContext;
            _alternativeTimeRequestService = alternativeTimeRequestService;
        }

        [NonAction]


        #region B3.7 - IAF Alternative Time Request

        [HttpGet]
        [Authorize(Roles = "Examiner")]
        public IActionResult MyIAFAlternativeTimeRequests()
        {
            // B3.7.2: Examiner read endpoint foundation only.
            //
            // SECURITY:
            // - Do not query IAFAlternativeTimeRequests yet.
            // - Do not accept a caller-supplied Examiner ID.
            // - Do not expose other examiners' requests.
            // - Keep disabled until database deployment and
            //   authoritative ownership validation are approved.

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    success = false,
                    code = "ALTERNATIVE_TIME_REQUEST_DISABLED",
                    message = "IAF Alternative Time Requests are not yet available."
                });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Examiner")]
        public IActionResult SubmitIAFAlternativeTimeSuggestion(
            int requestId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            string? lecturerNotes)
        {
            // B3.7.3: Examiner Suggest Time API foundation.
            //
            // SECURITY:
            // - Feature is not activated.
            // - Do not trust a caller-supplied Examiner ID.
            // - Request ownership must be verified server-side.
            // - Date, time, duration, and RowVersion must be
            //   validated before future activation.
            // - Do not call SubmitSuggestionAsync yet.
            // - No database access, booking, or notifications.

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    success = false,
                    code = "ALTERNATIVE_TIME_SUGGESTION_DISABLED",
                    message = "IAF alternative time suggestions are not yet available."
                });
        }


        #endregion

        public async Task<IActionResult> Seed()
        {
            try
            {
                // Create dummy examiner if not exists
                var examiner = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "EXM-001");
                if (examiner == null)
                {
                    examiner = new ApplicationUser { UserName = "EXM-001", Email = "examiner@test.com", FullName = "Dr. Dummy Examiner", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(examiner);
                    await _context.SaveChangesAsync();
                    var exmRole = await _context.AppRoles.FirstOrDefaultAsync(r => r.Name == "Examiner");
                    if (exmRole != null) { _context.UserRoleMappings.Add(new UserRoleMapping { UserId = examiner.Id, RoleId = exmRole.Id }); await _context.SaveChangesAsync(); }
                }

                // Create dummy supervisor if not exists
                var supervisor = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "SUP-001");
                if (supervisor == null)
                {
                    supervisor = new ApplicationUser { UserName = "SUP-001", Email = "super@test.com", FullName = "Dr. Super Visor", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(supervisor);
                    await _context.SaveChangesAsync();
                    var supRole = await _context.AppRoles.FirstOrDefaultAsync(r => r.Name == "Supervisor");
                    if (supRole != null) { _context.UserRoleMappings.Add(new UserRoleMapping { UserId = supervisor.Id, RoleId = supRole.Id }); await _context.SaveChangesAsync(); }
                }

                // Create dummy student if not exists
                var student = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "STU-001");
                if (student == null)
                {
                    student = new ApplicationUser { UserName = "STU-001", Email = "student@test.com", FullName = "Johnny Student", EmployeeId = "S123456", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(student);
                    await _context.SaveChangesAsync();
                    var stuRole = await _context.AppRoles.FirstOrDefaultAsync(r => r.Name == "Student");
                    if (stuRole != null) { _context.UserRoleMappings.Add(new UserRoleMapping { UserId = student.Id, RoleId = stuRole.Id }); await _context.SaveChangesAsync(); }
                }

                // Create project
                var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.Title == "AI Grading System");
                if (project == null)
                {
                    project = new FYPProject { Title = "AI Grading System", Description = "A system to grade students.", StudentId = student.Id, SupervisorId = supervisor.Id, Status = "InProgress" };
                    project.AssignedExaminers.Add(new FYPProjectExaminer { ExaminerId = examiner.Id });
                    _context.FYPProjects.Add(project);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (!project.AssignedExaminers.Any(ae => ae.ExaminerId == examiner.Id))
                    {
                        project.AssignedExaminers.Add(new FYPProjectExaminer { ExaminerId = examiner.Id });
                        await _context.SaveChangesAsync();
                    }
                }

                // Create Final Report milestone
                var milestone = await _context.FYPMilestones.FirstOrDefaultAsync(m => m.FYPProjectId == project.Id && m.Title == "Final Report");
                if (milestone == null)
                {
                    milestone = new FYPMilestone { FYPProjectId = project.Id, Title = "Final Report", Description = "Submit your final report.", Status = "Submitted", CreatedDate = DateTime.Now };
                    _context.FYPMilestones.Add(milestone);
                    await _context.SaveChangesAsync();
                }

                // Create a Meeting Booking and Log
                var meeting = await _context.MeetingBookings.FirstOrDefaultAsync(m => m.StudentId == student.Id);
                if (meeting == null)
                {
                    meeting = new MeetingBooking { StudentId = student.Id, SupervisorId = supervisor.Id, MeetingDate = DateTime.Now, Status = "Approved" };
                    _context.MeetingBookings.Add(meeting);
                    await _context.SaveChangesAsync();

                    var log = new MeetingLog { MeetingBookingId = meeting.Id, KeyTopicsDiscussed = "Discussed Final Report", SupervisorComments = "Good progress.", CreatedDate = DateTime.Now };
                    _context.MeetingLogs.Add(log);
                    await _context.SaveChangesAsync();
                }

                return Content("Seed completed! Log in as EXM-001 / 123456");
            }
            catch (Exception ex)
            {
                return Content("Error: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private async Task<bool> IsIAFDefenseTableAvailableAsync()
        {
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IAFDefenseBooking';";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IActionResult> Index(string? sessionFilter = null)
        {
            var user = await _context.ApplicationUsers
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Examiner")) return RedirectToAction("AccessDenied", "Home");

            ViewBag.CurrentUser = user;

            var sessions = await _context.AcademicSessions.OrderByDescending(s => s.StartDate).ToListAsync();
            ViewBag.AcademicSessions = sessions;

            var currentSessionObj = sessions.FirstOrDefault(s => s.IsCurrent == true);
            string activeSession = sessionFilter ?? currentSessionObj?.Name;
            ViewBag.CurrentSession = activeSession ?? "All";

            var projectsQuery = _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Where(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (!string.IsNullOrEmpty(activeSession) && activeSession != "All")
            {
                projectsQuery = projectsQuery.Where(p => p.AcademicSession == activeSession);
            }

            var projects = await projectsQuery.ToListAsync();

            // Load scheduled IAF Defenses assigned to this examiner
            var scheduledDefenses = new List<IAFDefenseBooking>();
            if (await IsIAFDefenseTableAvailableAsync())
            {
                var defensesQuery = _context.IAFDefenseBookings
                    .AsNoTracking()
                    .Include(b => b.Milestone)
                        .ThenInclude(m => m.Project)
                            .ThenInclude(p => p.Student)
                    .Include(b => b.Milestone)
                        .ThenInclude(m => m.Project)
                            .ThenInclude(p => p.Supervisor)
                    .Include(b => b.AcademicSession)
                    .Where(b => b.ExaminerId == user.Id && (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled"));

                if (!string.IsNullOrEmpty(activeSession) && activeSession != "All")
                {
                    defensesQuery = defensesQuery.Where(b => b.AcademicSession.Name == activeSession || b.Milestone.Project.AcademicSession == activeSession);
                }

                scheduledDefenses = await defensesQuery
                    .OrderBy(b => b.DefenseDate)
                    .ThenBy(b => b.StartTime)
                    .ToListAsync();
            }

            ViewBag.ScheduledDefenses = scheduledDefenses;

            return View(projects);
        }

        public async Task<IActionResult> Notifications()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var notifications = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id)
                .OrderByDescending(n => n.CreatedDate)
                .ToListAsync();

            return View(notifications);
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationAsRead(int notificationId)
        {
            var notif = await _context.FYPNotifications.FindAsync(notificationId);
            if (notif != null)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Notifications));
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsAsRead()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user != null)
            {
                var unread = await _context.FYPNotifications.Where(n => n.RecipientId == user.Id && !n.IsRead).ToListAsync();
                foreach(var n in unread) n.IsRead = true;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Notifications));
        }

        public async Task<IActionResult> StudentDetails(int projectId)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones).ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (project == null) return NotFound("Project not found or not assigned to you.");

            // Get meeting logs (fetching MeetingBooking objects that include MeetingLog)
            var meetingBookings = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .Where(m => m.FYPProjectId == project.Id && m.Status == "Approved")
                .OrderBy(m => m.Stage).ThenBy(m => m.WeekNumber).ThenByDescending(m => m.MeetingDate)
                .ToListAsync();

            ViewBag.MeetingBookings = meetingBookings;

            string idempotencyToken = Guid.NewGuid().ToString();
            TempData["IdempotencyToken"] = idempotencyToken;
            ViewBag.IdempotencyToken = idempotencyToken;

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMarksheetStage1(int projectId, IFormFile marksheetFile, string idempotencyToken)
        {
            // Legacy Stage 1 Excel upload endpoint is retired in favor of the official Stage 1 Online Rubric.
            TempData["Error"] = "Excel marksheet upload is deprecated. Please evaluate the student using the official Stage 1 Online Rubric.";
            return RedirectToAction(nameof(GradeMarksheetStage1), new { id = projectId });
        }

        [HttpGet]
        public async Task<IActionResult> GradeMarksheetStage1(int id)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones)
                .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.Id == id && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            var stage1Milestone = project.Milestones.FirstOrDefault(m => m.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("DPP", StringComparison.OrdinalIgnoreCase));
            if (stage1Milestone == null || (stage1Milestone.Status != "Approved" && stage1Milestone.Status != "Completed"))
            {
                TempData["Error"] = "The supervisor must approve the Stage 1 Final Submission milestone before you can evaluate the proposal rubric.";
                return RedirectToAction(nameof(StudentDetails), new { projectId = id });
            }

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeMarksheetStage1(int projectId, string marksheetData, bool isDraft = false)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            var stage1Milestone = project.Milestones.FirstOrDefault(m => m.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("DPP", StringComparison.OrdinalIgnoreCase));
            if (stage1Milestone == null)
            {
                TempData["Error"] = "Stage 1 Proposal milestone not found.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            if (!isDraft)
            {
                if (!FYPTracker.Utils.GradingHelper.ValidateStage1RubricMarks(marksheetData, out string? valErr))
                {
                    TempData["Error"] = $"Validation error: {valErr}";
                    return RedirectToAction(nameof(GradeMarksheetStage1), new { id = projectId });
                }
            }

            stage1Milestone.ExaminerMarksheetData = marksheetData;

            // Only calculate joint score if this submission is finalized and Supervisor has also finalized
            bool supFinalized = !string.IsNullOrEmpty(stage1Milestone.SupervisorMarksheetData) && !FYPTracker.Utils.GradingHelper.IsMarksheetDraft(stage1Milestone.SupervisorMarksheetData);
            
            if (!isDraft && supFinalized)
            {
                // Only calculate if not already finalized to prevent recalculating historical/manually approved scores
                if (project.Id != 1 || !project.Stage1TotalScore.HasValue)
                {
                    try
                    {
                        double exScore = FYPTracker.Utils.GradingHelper.CalculateStage1Score(marksheetData, "Examiner");
                        double supScore = FYPTracker.Utils.GradingHelper.CalculateStage1Score(stage1Milestone.SupervisorMarksheetData!, "Supervisor");

                        double? jointScore = FYPTracker.Utils.GradingHelper.CalculateJointFinalPercentage(supScore, exScore);
                        if (jointScore.HasValue)
                        {
                            project.Stage1TotalScore = jointScore.Value;
                            project.Stage1Grade = jointScore.Value >= 40.0 ? "Pass" : "Fail";
                            stage1Milestone.Status = "Completed";
                        }
                    }
                    catch { }
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = isDraft 
                ? "Stage 1 evaluation draft saved successfully." 
                : "Stage 1 evaluation marks submitted successfully!";

            return RedirectToAction(nameof(StudentDetails), new { projectId });
        }

        [HttpGet]
        public async Task<IActionResult> GradeMarksheetStage2(int id)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones)
                .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.Id == id && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            var stage2Milestones = project.Milestones.Where(m => m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase)).ToList();
            bool isAllStage2Approved = stage2Milestones.Any() && stage2Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!isAllStage2Approved)
            {
                TempData["Error"] = "The supervisor must approve ALL Stage 2 milestones (including the Final Report and Turnitin Score) before you can submit the final marksheet.";
                return RedirectToAction(nameof(StudentDetails), new { projectId = id });
            }

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeMarksheetStage2(int projectId, string marksheetData, string finalGrade, string examinerComments)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            // Rule 1: Reject attempts to overwrite an existing unversioned/ambiguous historical Examiner marksheet
            if (FYPTracker.Utils.GradingHelper.IsLegacyUnversionedExaminerRecord(project.Stage2ExaminerMarksheetData))
            {
                TempData["Error"] = "Action rejected: Historical grading format requires review and cannot be modified or overwritten.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            // Rule 2: Validate new Version 2 Examiner criteria server-side (each 0–4)
            if (!FYPTracker.Utils.GradingHelper.ValidateStage2Version2ExaminerMarks(marksheetData, out string? validationError))
            {
                TempData["Error"] = $"Marksheet submission failed: {validationError}";
                return RedirectToAction(nameof(GradeMarksheetStage2), new { id = projectId });
            }

            project.Stage2ExaminerMarksheetData = marksheetData;
            project.ExaminerComments = examinerComments;

            // Optional: Calculate Joint Final Grade if Supervisor has also submitted
            if (!string.IsNullOrEmpty(project.Stage2SupervisorMarksheetData))
            {
                try
                {
                    double? exTotal = FYPTracker.Utils.GradingHelper.CalculateStage2Score(marksheetData, "Examiner");
                    double? supTotal = FYPTracker.Utils.GradingHelper.CalculateStage2Score(project.Stage2SupervisorMarksheetData, "Supervisor");
                    
                    if (exTotal.HasValue && supTotal.HasValue)
                    {
                        // Stage 2 Unified Formula: (0.8 * SupervisorPercentage + 0.7 * ExaminerPercentage) / 1.5
                        double? jointPercentage = FYPTracker.Utils.GradingHelper.CalculateJointFinalPercentage(supTotal.Value, exTotal.Value);
                        if (jointPercentage.HasValue)
                        {
                            double finalScore = jointPercentage.Value;
                            project.FinalGrade = FYPTracker.Utils.GradingHelper.DetermineFinalGradeBand(finalScore);
                            TempData["Success"] = $"Stage 2 grading complete! Final joint score calculated as {finalScore:F2}%.";
                        }
                    }
                    else
                    {
                        TempData["Warning"] = "Stage 2 marks submitted. Historical grading format requires review before joint score can be calculated.";
                    }
                }
                catch (Exception)
                {
                    // Fallback to submitted finalGrade if JSON parsing fails
                    project.FinalGrade = finalGrade;
                    TempData["Success"] = "Stage 2 marks submitted. Supervisor marks could not be parsed for joint calculation.";
                }
            }
            else
            {
                project.FinalGrade = finalGrade;
                TempData["Success"] = "Stage 2 marks submitted successfully! Waiting for Supervisor to submit their marks for the final joint calculation.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(StudentDetails), new { projectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMarksheet(int projectId, IFormFile marksheetFile, string idempotencyToken)
        {
            // Legacy Stage 2 Excel upload endpoint is retired in favor of the official Stage 2 Online Rubric.
            TempData["Error"] = "Excel marksheet upload is deprecated. Please evaluate the student using the official Stage 2 Online Rubric.";
            return RedirectToAction(nameof(GradeMarksheetStage2), new { id = projectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetVivaDate(int projectId, DateTime vivaDate)
        {
            // Fix 1: Only administrators are authorized to schedule or modify Final Viva sessions.
            // Examiners have read-only visibility into the scheduled Viva date and venue.
            return Forbid();
        }

        [HttpGet]
        public async Task<IActionResult> DownloadMarksheet(int projectId)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (project == null) return NotFound();

            string templatePath = Path.Combine(_env.WebRootPath, "documents", "6002CMD - Marksheet.xlsx");
            if (!System.IO.File.Exists(templatePath))
            {
                TempData["Error"] = "Marksheet template not found on server.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            using (var workbook = new XLWorkbook(templatePath))
            {
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet != null)
                {
                    // Fill data
                    worksheet.Cell("D2").Value = project.Student?.FullName ?? "";
                    worksheet.Cell("D3").Value = project.Student?.EmployeeId ?? "";
                    worksheet.Cell("D4").Value = project.Title ?? "";
                    worksheet.Cell("D5").Value = project.Supervisor?.FullName ?? "";
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string safeTitle = string.IsNullOrWhiteSpace(project.Title) ? "Project" : string.Join("_", project.Title.Split(Path.GetInvalidFileNameChars()));
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Marksheet_{safeTitle}.xlsx");
                }
            }
        }

        // --- Joint Evaluation Marksheet ---
        [HttpGet]
        public async Task<IActionResult> GradeMarksheet(int milestoneId)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .ThenInclude(p => p.Student)
                .Include(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (milestone == null)
            {
                TempData["Error"] = "Milestone not found or unauthorized.";
                return RedirectToAction("Index");
            }

            var stage1Milestones = await _context.FYPMilestones
                .Where(m => m.FYPProjectId == milestone.FYPProjectId && !m.Title.StartsWith("Step"))
                .ToListAsync();
            bool isAllStage1Approved = stage1Milestones.Count >= 2 && stage1Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!milestone.IsReadyForJointEvaluation && !isAllStage1Approved)
            {
                TempData["Error"] = "Both Stage 1 milestones must be approved by the supervisor before joint evaluation.";
                return RedirectToAction("StudentDetails", new { projectId = milestone.FYPProjectId });
            }

            ViewBag.Role = "Examiner";
            return View("~/Views/Shared/GradeMarksheet.cshtml", milestone);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeMarksheet(int milestoneId, string marksheetData, string stage1Grade)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (milestone == null)
            {
                TempData["Error"] = "Milestone not found or unauthorized.";
                return RedirectToAction("Index");
            }

            var stage1RequiredMilestones = await _context.FYPMilestones
                .Where(m => m.FYPProjectId == milestone.FYPProjectId && 
                            m.Stage == 1 && 
                            m.MilestoneSource == "Default" && 
                            m.IsRequired)
                .ToListAsync();

            bool isAllStage1Approved = stage1RequiredMilestones.Count >= 2 && stage1RequiredMilestones.All(m => 
                m.Status == "Approved" || 
                m.Status == "Completed" || 
                (m.Status == "AdminApproved" && (m.Title.Contains("IAF") || m.Title.Contains("Initial Assessment Form"))));

            if (!isAllStage1Approved)
            {
                TempData["Error"] = "Required Stage 1 milestones (IAF & Final Submission) must be approved before joint evaluation.";
                return RedirectToAction("StudentDetails", new { projectId = milestone.FYPProjectId });
            }

            milestone.IsReadyForJointEvaluation = true;
            milestone.ExaminerMarksheetData = marksheetData;
            
            if (!string.IsNullOrEmpty(milestone.SupervisorMarksheetData))
            {
                milestone.Status = "Completed";
                try
                {
                    double supTotal = FYPTracker.Utils.GradingHelper.CalculateStage1Score(milestone.SupervisorMarksheetData, "Supervisor");
                    double exTotal = FYPTracker.Utils.GradingHelper.CalculateStage1Score(marksheetData, "Examiner");

                    // Stage 1 Unified Formula:
                    // (0.8 * SupervisorPercentage + 0.7 * ExaminerPercentage) / 1.5
                    double? jointPercentage = FYPTracker.Utils.GradingHelper.CalculateJointFinalPercentage(supTotal, exTotal);
                    if (jointPercentage.HasValue)
                    {
                        milestone.Project.Stage1TotalScore = jointPercentage.Value;
                        milestone.Project.Stage1Grade = jointPercentage.Value >= 40.0 ? "Pass" : "Fail";
                    }
                }
                catch { }
            }
    
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your joint evaluation marks have been saved successfully.";
            return RedirectToAction("StudentDetails", new { projectId = milestone.FYPProjectId });
        }

        private string GetGradeLabel(double score)
        {
            if (score >= 80) return "Excellent";
            if (score >= 60) return "Good";
            if (score >= 40) return "Average";
            if (score > 0) return "Poor";
            return "Fail";
        }

    }
}
