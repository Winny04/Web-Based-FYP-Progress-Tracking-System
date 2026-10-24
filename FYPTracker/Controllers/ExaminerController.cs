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
        // B3.9: Prepared dependency.
        private readonly IIAFAlternativeTimeRequestService? _alternativeTimeRequestService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;
        private readonly FYPTracker.Services.Meetings.IMeetingRequirementService? _meetingRequirementService;

        public ExaminerController(
            FYPTrackerContext context,
            IWebHostEnvironment env,
            IEmailService emailService,
            IHubContext<NotificationHub> hubContext,
            IIAFAlternativeTimeRequestService? alternativeTimeRequestService = null,
            Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
            FYPTracker.Services.Meetings.IMeetingRequirementService? meetingRequirementService = null)
        {
            _context = context;
            _env = env;
            _emailService = emailService;
            _hubContext = hubContext;
            _alternativeTimeRequestService = alternativeTimeRequestService;
            _configuration = configuration;
            _meetingRequirementService = meetingRequirementService;
        }

        private bool IsAlternativeTimeFeatureEnabled()
        {
            return IAFAlternativeTimeFeatureGate.IsFeatureEnabled(_configuration, _env, _context) && _alternativeTimeRequestService != null;
        }

        [NonAction]


        #region B3.7 - IAF Alternative Time Request

        [HttpGet]
        [Authorize(Roles = "Examiner")]
        public async Task<IActionResult> MyIAFAlternativeTimeRequests()
        {
            if (!IsAlternativeTimeFeatureEnabled())
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        code = "ALTERNATIVE_TIME_REQUEST_DISABLED",
                        message = "IAF Alternative Time Requests are not yet available."
                    });
            }

            if (!int.TryParse(User.FindFirst("Id")?.Value, out int examinerId) || examinerId <= 0)
            {
                var userName = User.Identity?.Name;
                var u = await _context.ApplicationUsers.FirstOrDefaultAsync(x => x.UserName == userName || x.Email == userName);
                if (u != null) examinerId = u.Id;
            }

            if (examinerId <= 0)
            {
                return Unauthorized(new { success = false, message = "Examiner identity not authenticated." });
            }

            var requests = await _context.IAFAlternativeTimeRequests
                .AsNoTracking()
                .Include(r => r.Milestone).ThenInclude(m => m.Project).ThenInclude(p => p.Student)
                .Include(r => r.Milestone).ThenInclude(m => m.Project).ThenInclude(p => p.Supervisor)
                .Include(r => r.AcademicSession)
                .Include(r => r.RequestedByAdmin)
                .Where(r => r.RequestedLecturerId == examinerId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.MilestoneId,
                    r.AttemptNumber,
                    StudentName = r.Milestone.Project.Student.FullName,
                    StudentId = r.Milestone.Project.Student.EmployeeId ?? r.Milestone.Project.Student.CoventryId,
                    ProjectTitle = r.Milestone.Project.Title,
                    SupervisorName = r.Milestone.Project.Supervisor != null ? r.Milestone.Project.Supervisor.FullName : "N/A",
                    AdminName = r.RequestedByAdmin.FullName,
                    r.AdminNotes,
                    r.Status,
                    SuggestedDate = r.SuggestedDate.HasValue ? r.SuggestedDate.Value.ToString("yyyy-MM-dd") : null,
                    SuggestedStartTime = r.SuggestedStartTime.HasValue ? r.SuggestedStartTime.Value.ToString(@"hh\:mm") : null,
                    SuggestedEndTime = r.SuggestedEndTime.HasValue ? r.SuggestedEndTime.Value.ToString(@"hh\:mm") : null,
                    r.SuggestedDurationMinutes,
                    r.LecturerNotes,
                    r.CreatedAt,
                    r.RespondedAt,
                    r.FinalizedAt,
                    r.DecisionReason,
                    RowVersion = r.RowVersion != null ? Convert.ToBase64String(r.RowVersion) : null
                })
                .ToListAsync();

            return Json(new { success = true, requests });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Examiner")]
        public IActionResult SubmitIAFAlternativeTimeSuggestion(
            int requestId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            string? lecturerNotes,
            string? rowVersion = null)
        {
            if (!IsAlternativeTimeFeatureEnabled())
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        code = "ALTERNATIVE_TIME_SUGGESTION_DISABLED",
                        message = "IAF alternative time suggestions are not yet available."
                    });
            }

            return SubmitIAFAlternativeTimeSuggestionCoreAsync(requestId, suggestedDate, suggestedStartTime, lecturerNotes, rowVersion).GetAwaiter().GetResult();
        }

        private async Task<IActionResult> SubmitIAFAlternativeTimeSuggestionCoreAsync(
            int requestId,
            DateTime suggestedDate,
            TimeSpan suggestedStartTime,
            string? lecturerNotes,
            string? rowVersion = null)
        {

            if (requestId <= 0)
            {
                return BadRequest(new { success = false, code = "INVALID_REQUEST_ID", message = "A valid request ID is required." });
            }

            if (!int.TryParse(User.FindFirst("Id")?.Value, out int currentExaminerUserId) || currentExaminerUserId <= 0)
            {
                var userName = User.Identity?.Name;
                var u = await _context.ApplicationUsers.FirstOrDefaultAsync(x => x.UserName == userName || x.Email == userName);
                if (u != null) currentExaminerUserId = u.Id;
            }

            if (currentExaminerUserId <= 0)
            {
                return Unauthorized(new { success = false, code = "UNAUTHORIZED_ACCESS", message = "A valid authenticated Examiner identity is required." });
            }

            // Resolve authoritative duration from FYPSetting
            var durationSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "IAFDefenseDurationMinutes");

            int authoritativeDurationMinutes = 45;
            if (durationSetting != null &&
                int.TryParse(durationSetting.Value, out int configuredDuration) &&
                configuredDuration >= 15 &&
                configuredDuration <= 180)
            {
                authoritativeDurationMinutes = configuredDuration;
            }

            // Resolve authoritative working hours
            var startSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");

            var endSetting = await _context.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");

            if (!FYPTracker.Services.LecturerAvailabilityService.TryParseWorkingHours(
                startSetting?.Value,
                endSetting?.Value,
                out TimeSpan authoritativeWorkStart,
                out TimeSpan authoritativeWorkEnd))
            {
                authoritativeWorkStart = new TimeSpan(8, 0, 0);
                authoritativeWorkEnd = new TimeSpan(17, 0, 0);
            }

            byte[]? clientRowVersion = null;
            if (!string.IsNullOrEmpty(rowVersion))
            {
                try
                {
                    clientRowVersion = Convert.FromBase64String(rowVersion);
                }
                catch
                {
                    return BadRequest(new { success = false, code = "INVALID_ROW_VERSION", message = "Invalid concurrency token format." });
                }
            }
            else
            {
                var req = await _context.IAFAlternativeTimeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId);
                clientRowVersion = req?.RowVersion;
            }

            var result = await _alternativeTimeRequestService!.SubmitSuggestionAsync(
                requestId: requestId,
                currentLecturerUserId: currentExaminerUserId,
                suggestedDate: suggestedDate,
                suggestedStartTime: suggestedStartTime,
                durationMinutes: authoritativeDurationMinutes,
                workingHoursStart: authoritativeWorkStart,
                workingHoursEnd: authoritativeWorkEnd,
                lecturerNotes: lecturerNotes,
                clientRowVersion: clientRowVersion);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    code = result.ErrorCode,
                    message = result.ErrorMessage
                });
            }

            // In-app notification to the requesting administrator
            try
            {
                var adminNotification = new FYPNotification
                {
                    RecipientId = result.Request!.RequestedByAdminId,
                    Type = "IAFAlternativeTimeSuggested",
                    Title = "IAF Defense Alternative Time Proposed",
                    Message = $"An Examiner has proposed an alternative defense time for request #{result.Request.Id} (Date: {suggestedDate:dd MMM yyyy}, Time: {suggestedStartTime:hh\\:mm}). Please review in Smart Schedule.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                };
                _context.FYPNotifications.Add(adminNotification);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SubmitIAFAlternativeTimeSuggestion] Notification warning: {ex.Message}");
            }

            return Json(new
            {
                success = true,
                message = "Alternative time suggestion submitted successfully."
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
                .Include(p => p.Milestones)
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
                    .Where(b => b.ExaminerId == user.Id && (b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled" || b.BookingStatus == "Completed"));

                if (!string.IsNullOrEmpty(activeSession) && activeSession != "All")
                {
                    defensesQuery = defensesQuery.Where(b => b.AcademicSession.Name == activeSession || b.Milestone.Project.AcademicSession == activeSession);
                }

                scheduledDefenses = await defensesQuery
                    .OrderByDescending(b => b.BookingStatus == "Scheduled" || b.BookingStatus == "Rescheduled")
                    .ThenBy(b => b.DefenseDate)
                    .ThenBy(b => b.StartTime)
                    .ToListAsync();
            }

            ViewBag.ScheduledDefenses = scheduledDefenses;

            if (IsAlternativeTimeFeatureEnabled())
            {
                var altQuery = _context.IAFAlternativeTimeRequests
                    .AsNoTracking()
                    .Include(r => r.Milestone).ThenInclude(m => m.Project).ThenInclude(p => p.Student)
                    .Include(r => r.Milestone).ThenInclude(m => m.Project).ThenInclude(p => p.Supervisor)
                    .Include(r => r.AcademicSession)
                    .Include(r => r.RequestedByAdmin)
                    .Where(r => r.RequestedLecturerId == user.Id);

                if (!string.IsNullOrEmpty(activeSession) && activeSession != "All")
                {
                    altQuery = altQuery.Where(r => (r.AcademicSession != null && r.AcademicSession.Name == activeSession) ||
                                                   (r.Milestone != null && r.Milestone.Project != null && r.Milestone.Project.AcademicSession == activeSession));
                }

                ViewBag.AlternativeTimeRequests = await altQuery
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                var durationSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "IAFDefenseDurationMinutes");
                ViewBag.DefenseDurationMinutes = (durationSetting != null && int.TryParse(durationSetting.Value, out int dur)) ? dur : 45;

                var startSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursStart");
                var endSetting = await _context.FYPSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "LecturerWorkingHoursEnd");
                ViewBag.WorkingHoursStart = startSetting?.Value ?? "08:00";
                ViewBag.WorkingHoursEnd = endSetting?.Value ?? "17:00";
            }

            return View(projects);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordDefenseAssessment(
            int bookingId,
            string? result,
            string? recommendation,
            string feedback,
            [FromServices] IIAFDefenseBookingService defenseBookingService,
            [FromServices] FYPTracker.Services.Time.ISystemClock clock)
        {
            var user = await _context.ApplicationUsers
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);

            if (user == null) return RedirectToAction("Login", "User");

            // Server-side permission check: Active user with Examiner/Lecturer role
            if (user.IsResign || (!user.UserRoles.Any(r => r.Role.Name == "Examiner") && !user.UserRoles.Any(r => r.Role.Name == "Lecturer")))
            {
                TempData["Error"] = "Access denied: Unauthorized examiner role.";
                return RedirectToAction(nameof(Index));
            }

            var booking = await _context.IAFDefenseBookings
                .Include(b => b.Milestone)
                    .ThenInclude(m => m.Project)
                        .ThenInclude(p => p.Student)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
            {
                TempData["Error"] = "Defense booking not found.";
                return RedirectToAction(nameof(Index));
            }

            // Server-side permission check: Only the assigned examiner can finalize the defense result
            if (booking.ExaminerId != user.Id)
            {
                TempData["Error"] = "Access denied: You are not the assigned examiner for this defense session.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Milestone == null || booking.Milestone.Project == null)
            {
                TempData["Error"] = "Associated milestone or project record was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.BookingStatus != "Scheduled" && booking.BookingStatus != "Rescheduled")
            {
                TempData["Error"] = $"Cannot finalize defense for booking with status '{booking.BookingStatus}'.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.AcademicOutcome != "Pending")
            {
                TempData["Error"] = $"An academic outcome has already been finalized ({booking.AcademicOutcome}).";
                return RedirectToAction(nameof(Index));
            }

            // Face-to-face date/time safeguard: Defense must have concluded under authorized system clock
            var defenseEndDateTime = booking.DefenseDate.Date.Add(booking.EndTime);
            if (defenseEndDateTime > clock.Now)
            {
                TempData["Error"] = $"The physical defense session has not yet concluded. Scheduled until {booking.DefenseDate:dd MMM yyyy} {new DateTime(booking.EndTime.Ticks):hh:mm tt}.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(feedback))
            {
                TempData["Error"] = "Meaningful examiner feedback is required following the face-to-face defense.";
                return RedirectToAction(nameof(Index));
            }

            // Determine final academic outcome: Pass or Fail
            string rawOutcome = !string.IsNullOrWhiteSpace(result) ? result : recommendation;
            bool isPass = string.Equals(rawOutcome, "Pass", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(rawOutcome, "AdminApproved", StringComparison.OrdinalIgnoreCase);
            bool isFail = string.Equals(rawOutcome, "Fail", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(rawOutcome, "RevisionRequired", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(rawOutcome, "ChangeOfTopicRequired", StringComparison.OrdinalIgnoreCase);

            if (!isPass && !isFail)
            {
                TempData["Error"] = "Invalid result selected. Please select Pass or Fail.";
                return RedirectToAction(nameof(Index));
            }

            string cleanFeedback = feedback.Trim();
            string evidencePrefix = clock.IsSimulated ? "[Testing Simulation Evidence] " : "";
            string outcomeStr = isPass ? "Pass" : "Fail";
            string documentedAssessment = $"{evidencePrefix}Final Examiner ({user.FullName}) Outcome: {outcomeStr}. Feedback: {cleanFeedback}";

            // Authoritative transactional finalization
            var outcomeResult = await defenseBookingService.RecordDefenseOutcomeAsync(
                booking.MilestoneId,
                outcomeStr,
                documentedAssessment,
                user.Id,
                isEarlyOutcomeTestOverride: false);

            if (!outcomeResult.Success)
            {
                TempData["Error"] = outcomeResult.ErrorMessage;
                return RedirectToAction(nameof(Index));
            }

            // Create notifications only after successful outcome persistence
            var studentId = booking.Milestone.Project.StudentId;
            var studentName = booking.Milestone.Project.Student?.FullName ?? "Student";
            int attemptNum = outcomeResult.Booking?.AttemptNumber ?? booking.AttemptNumber;
            string finalStatus = outcomeResult.Booking?.AcademicOutcome ?? (isPass ? "AdminApproved" : "PanelRevisionRequired");

            if (isPass)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = studentId,
                    Type = "MilestoneCompleted",
                    Title = "IAF Defense Passed",
                    Message = "Congratulations! Your IAF defense was successful and your project title is accepted. You may now proceed to Stage 1 submissions.",
                    IsRead = false,
                    CreatedDate = clock.Now
                });
            }
            else
            {
                if (finalStatus == "PanelRevisionRequired")
                {
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = studentId,
                        Type = "RevisionRequest",
                        Title = "IAF Defense Outcome: Revision Required",
                        Message = "Your IAF defense requires revisions. Please review the examiner feedback and submit a revised Initial Project Allocation Form.",
                        IsRead = false,
                        CreatedDate = clock.Now
                    });
                }
                else
                {
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = studentId,
                        Type = "MilestoneRejected",
                        Title = "IAF Defense Outcome: Change of Topic Required",
                        Message = "Your IAF defense was unsuccessful on Attempt 2. A change of topic is required for your project.",
                        IsRead = false,
                        CreatedDate = clock.Now
                    });
                }
            }

            // Notify Admins
            var adminUsers = await _context.ApplicationUsers
                .Where(u => !u.IsResign && u.UserRoles.Any(ur => ur.Role.Name == "Admin" || ur.Role.Name == "SuperAdmin"))
                .ToListAsync();

            foreach (var admin in adminUsers)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = admin.Id,
                    Type = "DefenseOutcomeFinalized",
                    Title = "IAF Defense Finalized by Examiner",
                    Message = $"Examiner {user.FullName} has finalized the IAF defense result for {studentName}: {(isPass ? "Passed" : "Failed")}. Feedback has been recorded.",
                    IsRead = false,
                    CreatedDate = clock.Now
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Final defense outcome and feedback successfully submitted and saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizeExistingDefenseAssessment(
            int bookingId,
            [FromServices] IIAFDefenseBookingService defenseBookingService,
            [FromServices] FYPTracker.Services.Time.ISystemClock clock)
        {
            var user = await _context.ApplicationUsers
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);

            if (user == null) return RedirectToAction("Login", "User");

            if (user.IsResign || (!user.UserRoles.Any(r => r.Role.Name == "Examiner") && !user.UserRoles.Any(r => r.Role.Name == "Lecturer")))
            {
                TempData["Error"] = "Access denied: Unauthorized examiner role.";
                return RedirectToAction(nameof(Index));
            }

            var booking = await _context.IAFDefenseBookings
                .Include(b => b.Milestone)
                    .ThenInclude(m => m.Project)
                        .ThenInclude(p => p.Student)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
            {
                TempData["Error"] = "Defense booking not found.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.ExaminerId != user.Id)
            {
                TempData["Error"] = "Access denied: You are not the assigned examiner for this defense session.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Milestone == null || booking.Milestone.Project == null)
            {
                TempData["Error"] = "Associated milestone or project record was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.BookingStatus != "Scheduled" && booking.BookingStatus != "Rescheduled")
            {
                TempData["Error"] = $"Cannot finalize defense for booking with status '{booking.BookingStatus}'.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.AcademicOutcome != "Pending")
            {
                TempData["Error"] = $"An academic outcome has already been finalized ({booking.AcademicOutcome}).";
                return RedirectToAction(nameof(Index));
            }

            var defenseEndDateTime = booking.DefenseDate.Date.Add(booking.EndTime);
            if (defenseEndDateTime > clock.Now)
            {
                TempData["Error"] = $"The physical defense session has not yet concluded. Scheduled until {booking.DefenseDate:dd MMM yyyy} {new DateTime(booking.EndTime.Ticks):hh:mm tt}.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(booking.OutcomeComments))
            {
                TempData["Error"] = "No recorded assessment or feedback found to finalize.";
                return RedirectToAction(nameof(Index));
            }

            // Infer outcome from existing assessment comments
            string comments = booking.OutcomeComments;
            bool isPass = comments.IndexOf("Recommendation: Pass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          comments.IndexOf("Pass", StringComparison.OrdinalIgnoreCase) >= 0;
            string outcomeStr = isPass ? "Pass" : "Fail";

            string finalizedComments = comments;
            if (!finalizedComments.Contains("[Official Finalized Result]"))
            {
                finalizedComments = $"{finalizedComments} [Official Finalized Result]";
            }

            var outcomeResult = await defenseBookingService.RecordDefenseOutcomeAsync(
                booking.MilestoneId,
                outcomeStr,
                finalizedComments,
                user.Id,
                isEarlyOutcomeTestOverride: false);

            if (!outcomeResult.Success)
            {
                TempData["Error"] = outcomeResult.ErrorMessage;
                return RedirectToAction(nameof(Index));
            }

            var studentId = booking.Milestone.Project.StudentId;
            var studentName = booking.Milestone.Project.Student?.FullName ?? "Student";
            string finalStatus = outcomeResult.Booking?.AcademicOutcome ?? (isPass ? "AdminApproved" : "PanelRevisionRequired");

            if (isPass)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = studentId,
                    Type = "MilestoneCompleted",
                    Title = "IAF Defense Passed",
                    Message = "Congratulations! Your IAF defense was successful and your project title is accepted. You may now proceed to Stage 1 submissions.",
                    IsRead = false,
                    CreatedDate = clock.Now
                });
            }
            else
            {
                if (finalStatus == "PanelRevisionRequired")
                {
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = studentId,
                        Type = "RevisionRequest",
                        Title = "IAF Defense Outcome: Revision Required",
                        Message = "Your IAF defense requires revisions. Please review the examiner feedback and submit a revised Initial Project Allocation Form.",
                        IsRead = false,
                        CreatedDate = clock.Now
                    });
                }
                else
                {
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = studentId,
                        Type = "MilestoneRejected",
                        Title = "IAF Defense Outcome: Change of Topic Required",
                        Message = "Your IAF defense was unsuccessful on Attempt 2. A change of topic is required for your project.",
                        IsRead = false,
                        CreatedDate = clock.Now
                    });
                }
            }

            var adminUsers = await _context.ApplicationUsers
                .Where(u => !u.IsResign && u.UserRoles.Any(ur => ur.Role.Name == "Admin" || ur.Role.Name == "SuperAdmin"))
                .ToListAsync();

            foreach (var admin in adminUsers)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = admin.Id,
                    Type = "DefenseOutcomeFinalized",
                    Title = "IAF Defense Finalized by Examiner",
                    Message = $"Examiner {user.FullName} has finalized the IAF defense result for {studentName}: {(isPass ? "Passed" : "Failed")}. Feedback has been recorded.",
                    IsRead = false,
                    CreatedDate = clock.Now
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Successfully finalized existing assessment as official academic defense result.";
            return RedirectToAction(nameof(Index));
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

            var stage1Milestone = FYPTracker.Utils.GradingHelper.GetStage1FinalMilestone(project.Milestones);
            if (stage1Milestone == null || !FYPTracker.Utils.GradingHelper.IsStage1DppApproved(stage1Milestone))
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

            var stage1Milestone = FYPTracker.Utils.GradingHelper.GetStage1FinalMilestone(project.Milestones);
            if (stage1Milestone == null || !FYPTracker.Utils.GradingHelper.IsStage1DppApproved(stage1Milestone))
            {
                TempData["Error"] = "The supervisor must approve the Stage 1 Final Submission milestone before you can evaluate the proposal rubric.";
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

            int completedMeetings = 0;
            int? requiredMeetings = null;
            if (_meetingRequirementService != null)
            {
                var progress = await _meetingRequirementService.GetStudentProgressAsync(project.StudentId, 2);
                completedMeetings = progress?.Completed ?? 0;
                requiredMeetings = progress?.Required;
            }
            else
            {
                completedMeetings = await _context.MeetingBookings
                    .Include(m => m.MeetingLog)
                    .Where(m => m.StudentId == project.StudentId && m.Stage == 2 && m.Status == "Approved" && m.MeetingLog != null && m.MeetingLog.LogStatus == "Signed")
                    .CountAsync();
            }

            var unlockStatus = FYPTracker.Utils.GradingHelper.CanUnlockStage2Grading(project, completedMeetings, requiredMeetings);
            if (!unlockStatus.IsUnlocked)
            {
                TempData["Error"] = unlockStatus.Reason;
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
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            int completedMeetingsPost = 0;
            int? requiredMeetingsPost = null;
            if (_meetingRequirementService != null)
            {
                var progress = await _meetingRequirementService.GetStudentProgressAsync(project.StudentId, 2);
                completedMeetingsPost = progress?.Completed ?? 0;
                requiredMeetingsPost = progress?.Required;
            }
            else
            {
                completedMeetingsPost = await _context.MeetingBookings
                    .Include(m => m.MeetingLog)
                    .Where(m => m.StudentId == project.StudentId && m.Stage == 2 && m.Status == "Approved" && m.MeetingLog != null && m.MeetingLog.LogStatus == "Signed")
                    .CountAsync();
            }

            var unlockStatusPost = FYPTracker.Utils.GradingHelper.CanUnlockStage2Grading(project, completedMeetingsPost, requiredMeetingsPost);
            if (!unlockStatusPost.IsUnlocked)
            {
                TempData["Error"] = unlockStatusPost.Reason;
                return RedirectToAction(nameof(StudentDetails), new { projectId });
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

        [HttpGet]
        public async Task<IActionResult> ViewMeetingLog(int id)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var log = await _context.MeetingLogs
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Student)
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Supervisor)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (log == null || log.MeetingBooking == null)
                return NotFound();

            // Authorize: ensure the logged in Examiner is assigned to this meeting's project
            bool isAssigned = await _context.FYPProjects
                .AnyAsync(p => p.Id == log.MeetingBooking.FYPProjectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (!isAssigned)
                return Forbid();

            if (log.LogStatus != "Signed")
                return NotFound();

            // The PDF lives outside wwwroot: never Redirect() to App_Data.
            string? stored = log.SignedPdfLogbookPath?.Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(stored))
            {
                string? relativeDir = null;
                string? name = null;

                const string testingPrefix = "App_Data/MeetingLogSnapshots/Testing/";
                const string standardPrefix = "App_Data/MeetingLogSnapshots/";

                if (stored.StartsWith(testingPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = "App_Data/MeetingLogSnapshots/Testing";
                    name = stored.Substring(testingPrefix.Length);
                }
                else if (stored.StartsWith(standardPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = "App_Data/MeetingLogSnapshots";
                    name = stored.Substring(standardPrefix.Length);
                }

                if (!string.IsNullOrWhiteSpace(relativeDir) &&
                    !string.IsNullOrWhiteSpace(name) &&
                    name == Path.GetFileName(name) &&
                    name.StartsWith($"Signed_Log_{log.MeetingBookingId}_", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    string physicalPath = Path.Combine(
                        _env.ContentRootPath, relativeDir.Replace('/', Path.DirectorySeparatorChar), name);
                    if (System.IO.File.Exists(physicalPath))
                        return PhysicalFile(physicalPath, "application/pdf", enableRangeProcessing: true);
                }
            }

            // Fallback for legacy signed records without a signed PDF snapshot: show printable view
            var proj = await _context.FYPProjects.FirstOrDefaultAsync(p => p.Id == log.MeetingBooking.FYPProjectId);
            ViewBag.ProjectName = proj?.Title ?? "FYP Project";
            return View("~/Views/Shared/MeetingLogPrintable.cshtml", log);
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
