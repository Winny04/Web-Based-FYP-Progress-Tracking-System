using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FYPTracker.Controllers
{
    [Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Supervisor")]
    public class SupervisorController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;
        private readonly FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService _weeklyMeetingSchedulingService;

        public SupervisorController(FYPTrackerContext context, IEmailService emailService, IHubContext<NotificationHub> hubContext, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env, FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService weeklyMeetingSchedulingService)
        {
            _context = context;
            _emailService = emailService;
            _hubContext = hubContext;
            _env = env;
            _weeklyMeetingSchedulingService = weeklyMeetingSchedulingService;
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var user = HttpContext.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated) return null;

            if (int.TryParse(user.FindFirst("Id")?.Value, out int id))
            {
                return await _context.ApplicationUsers
                    .Include(u => u.EmpDepartment)
                    .Include(u => u.UserRoles).ThenInclude(r => r.Role)
                    .FirstOrDefaultAsync(u => u.Id == id);
            }
            return null;
        }

        private async Task AddNotificationAsync(int recipientId, string type, string title, string? message = null)
        {
            _context.FYPNotifications.Add(new FYPNotification
            {
                RecipientId = recipientId,
                Type = type,
                Title = title,
                Message = message,
                IsRead = false,
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        private async Task LogActivityAsync(int studentId, string type, string title, string? desc = null)
        {
            _context.FYPActivities.Add(new FYPActivity
            {
                StudentId = studentId,
                Type = type,
                Title = title,
                Description = desc,
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        private async Task<string?> GetAndSetSessionFilterAsync(int supervisorId, string? sessionFilter)
        {
            // Load all configured academic sessions
            var sessions = await _context.FYPProjects
                .Where(p => p.SupervisorId == supervisorId &&
                            !string.IsNullOrEmpty(p.AcademicSession))
                .Select(p => p.AcademicSession)
                .Distinct()
                .ToListAsync();

            var reqSessions = await _context.SupervisorRequests
                .Where(r => r.SupervisorId == supervisorId && !string.IsNullOrEmpty(r.Student.AcademicSession))
                .Select(r => r.Student.AcademicSession)
                .Distinct()
                .ToListAsync();

            var allSessions = sessions.Concat(reqSessions).Distinct().OrderByDescending(s => s).ToList();

            if (!allSessions.Any()) return null;

            string selectedSession;
            var sessionCookie = HttpContext.Request.Cookies["SupervisorSessionFilter"];

            if (!string.IsNullOrEmpty(sessionFilter) && allSessions.Contains(sessionFilter))
            {
                selectedSession = sessionFilter;
                HttpContext.Response.Cookies.Append("SupervisorSessionFilter", selectedSession, new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(7) });
            }
            else if (!string.IsNullOrEmpty(sessionCookie) && allSessions.Contains(sessionCookie))
            {
                selectedSession = sessionCookie;
            }
            else
            {
                selectedSession = allSessions.First()!;
                HttpContext.Response.Cookies.Append("SupervisorSessionFilter", selectedSession, new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(7) });
            }
            
            ViewBag.AllSessions = allSessions;
            ViewBag.CurrentSession = selectedSession;

            return selectedSession;
        }

        // ══════════════════════════════════════════════════════
        //  DASHBOARD (INDEX)
        // ══════════════════════════════════════════════════════
        public async Task<IActionResult> Index(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var activeSessionConfig = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true);
            bool hasAvailability = activeSessionConfig != null && await _context.SupervisorTimetableEntries.AnyAsync(a => a.SupervisorId == user.Id && a.AcademicSessionId == activeSessionConfig.Id);
            if (!hasAvailability)
            {
                ViewBag.MissingAvailability = true;
            }

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            // Stats
            ViewBag.TotalProjects     = await _context.FYPProjects.CountAsync(p => p.SupervisorId == user.Id && (activeSession == null || p.AcademicSession == activeSession));
            ViewBag.PendingRequests   = await _context.SupervisorRequests.CountAsync(r => r.SupervisorId == user.Id && r.Status == "Pending" && (activeSession == null || r.Student.AcademicSession == activeSession));
            ViewBag.PendingReviews    = await _context.FYPMilestones.CountAsync(m => m.Project.SupervisorId == user.Id && m.Status == "Submitted" && (activeSession == null || m.Project.AcademicSession == activeSession));
            ViewBag.UpcomingMeetings  = await _context.MeetingBookings.CountAsync(m => m.SupervisorId == user.Id && m.MeetingDate >= DateTime.Today && m.Status == "Approved" && (activeSession == null || m.Student.AcademicSession == activeSession));
            ViewBag.PendingExaminers  = await _context.FYPProjects.CountAsync(p => p.SupervisorId == user.Id && !p.AssignedExaminers.Any() && p.Status != "Failed" && (activeSession == null || p.AcademicSession == activeSession));

            // Recent Requests (Top 5)
            var recentRequests = await _context.SupervisorRequests
                .Include(r => r.Student)
                .Where(r => r.SupervisorId == user.Id && r.Status == "Pending" && !r.Student.IsResign && (activeSession == null || r.Student.AcademicSession == activeSession))
                .OrderByDescending(r => r.RequestedDate)
                .Take(5)
                .ToListAsync();

            // Upcoming Meetings (Next 5)
            var nextMeetings = await _context.MeetingBookings
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == user.Id && m.MeetingDate >= DateTime.Today && m.Status == "Approved" && !m.Student.IsResign && (activeSession == null || m.Student.AcademicSession == activeSession))
                .OrderBy(m => m.MeetingDate)
                .ThenBy(m => m.MeetingTime)
                .Take(5)
                .ToListAsync();

            // Pending Milestones (Top 5 awaiting review)
            var pendingMilestones = await _context.FYPMilestones
                .Include(m => m.Project)
                .ThenInclude(p => p.Student)
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (m.Status == "Submitted" || (m.Status == "Pending" && m.SubmittedDate.HasValue)) && (activeSession == null || m.Project.AcademicSession == activeSession))
                .OrderBy(m => m.SubmittedDate ?? DateTime.MaxValue)
                .Take(5)
                .ToListAsync();

            // Pending Meetings to Approve (Top 5)
            var pendingMeetingsList = await _context.MeetingBookings
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == user.Id && m.Status == "Pending" && !m.Student.IsResign && (activeSession == null || m.Student.AcademicSession == activeSession))
                .OrderBy(m => m.MeetingDate)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentRequests = recentRequests;
            ViewBag.NextMeetings   = nextMeetings;
            ViewBag.PendingMilestones = pendingMilestones;
            ViewBag.PendingMeetingsList = pendingMeetingsList;

            var upcomingDefenses = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Where(m => m.Project.SupervisorId == user.Id 
                            && m.Status == "DefenseScheduled"
                            && (activeSession == null || m.Project.AcademicSession == activeSession))
                .OrderBy(m => m.DefenseDate)
                .ToListAsync();
            ViewBag.UpcomingDefenses = upcomingDefenses;

            // Unread Notification Count (for bell icon)
            ViewBag.UnreadNotificationCount = await _context.FYPNotifications
                .CountAsync(n => n.RecipientId == user.Id && !n.IsRead);

            return View();
        }

        // ────────────────────────────────────────────────────────
        // Student Requests
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> StudentRequests(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var requests = await _context.SupervisorRequests
                .Include(r => r.Student)
                .Where(r => r.SupervisorId == user.Id && !r.Student.IsResign && (activeSession == null || r.Student.AcademicSession == activeSession))
                .OrderByDescending(r => r.RequestedDate)
                .ToListAsync();

            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondRequest(int requestId, string status, string? responseNote)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var req = await _context.SupervisorRequests.Include(r => r.Student).Include(r => r.Supervisor).FirstOrDefaultAsync(r => r.Id == requestId);
            if (req != null && req.Status == "Pending" && req.SupervisorId == user.Id)
            {
                req.Status = status; // Approved or Rejected
                req.RespondedDate = DateTime.Now;
                req.ResponseNote = responseNote;

                if (status == "Approved")
                {
                    // Fetch the absolute latest project to check its status and grade
                    var latestProj = await _context.FYPProjects
                        .OrderByDescending(p => p.CreatedDate)
                        .FirstOrDefaultAsync(p => p.StudentId == req.StudentId);
                    
                    // Update student's academic session based on their request
                    if (!string.IsNullOrWhiteSpace(req.SessionSemester))
                    {
                        req.Student.AcademicSession = req.SessionSemester;
                    }

                    string targetSession = req.SessionSemester ?? req.Student.AcademicSession;
                    var sessionObj = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == targetSession);
                    int? sessionId = sessionObj?.Id;
                    
                    var templates = new List<DefaultMilestoneTemplate>();
                    if (sessionId.HasValue)
                    {
                        templates = await _context.DefaultMilestoneTemplates
                            .Where(t => t.AcademicSessionId == sessionId.Value && t.Stage == 1 && t.IsActive)
                            .OrderBy(t => t.DisplayOrder)
                            .ToListAsync();
                    }
                    
                    bool isRepeat = latestProj != null && latestProj.Stage1Grade == "Fail" && latestProj.Status != "Completed";
                    bool isNew = latestProj == null || latestProj.Status == "Failed" || latestProj.Status == "Completed";
                    
                    if ((isRepeat || isNew) && templates.Count == 0)
                    {
                        TempData["Error"] = $"Cannot approve request. No active Stage 1 Default Milestones configured for session '{targetSession}'. Please ask an Admin to configure them.";
                        return RedirectToAction(nameof(StudentRequests));
                    }

                    if (isRepeat)
                    {
                        // OPTION B: Create a new project for the repeat attempt, copying metadata and seeding milestones concurrently
                        var repeatProj = new FYPProject
                        {
                            PreviousProjectId = latestProj.Id,
                            Title = latestProj.Title,
                            Description = latestProj.Description,
                            ProblemStatement = latestProj.ProblemStatement,
                            Objective1 = latestProj.Objective1,
                            Objective2 = latestProj.Objective2,
                            Objective3 = latestProj.Objective3,
                            Scope = latestProj.Scope,
                            ExpectedOutcome = latestProj.ExpectedOutcome,
                            References = latestProj.References,
                            AcademicSession = req.SessionSemester ?? req.Student.AcademicSession,
                            StudentId = req.StudentId,
                            SupervisorId = req.SupervisorId,
                            Status = "Approved",
                            StartDate = DateTime.Now,
                            CreatedDate = DateTime.Now
                        };
                        _context.FYPProjects.Add(repeatProj);
                        await _context.SaveChangesAsync(); // Get ID

                        _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                        {
                            ProjectId = repeatProj.Id,
                            OldStatus = "None",
                            NewStatus = "Approved",
                            ChangedById = req.SupervisorId,
                            Remarks = "Repeat project created and approved from Supervisor Request."
                        });

                        // Seed Stage 1 from DefaultMilestoneTemplates and Stage 2 hardcoded concurrently
                        var milestones = templates.Select(t => new FYPMilestone
                        {
                            FYPProjectId = repeatProj.Id,
                            Title = t.Title,
                            Description = t.Description,
                            DueDate = t.DueDate ?? DateTime.Now.AddMonths(1),
                            Status = "Pending",
                            MilestoneSource = "Default",
                            MilestoneCode = t.MilestoneCode,
                            Stage = t.Stage,
                            IsRequired = t.IsRequired,
                            DefaultMilestoneTemplateId = t.Id
                        }).ToList();

                        milestones.AddRange(new List<FYPMilestone>
                        {
                            // Stage 2 Milestones
                            new FYPMilestone { FYPProjectId = repeatProj.Id, Title = "Step 1: Turnitin Checking", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = repeatProj.Id, Title = "Step 2: Final Report Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = repeatProj.Id, Title = "Step 3: Marksheet Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = repeatProj.Id, Title = "Step 4: Record of Supervisor Meeting Form Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = repeatProj.Id, Title = "Step 5: Presentation Slides Submission", DueDate = DateTime.Now.AddMonths(6), Status = "Pending", Stage = 2 }
                        });
                        _context.FYPMilestones.AddRange(milestones);
                    }
                    else if (latestProj == null || latestProj.Status == "Failed" || latestProj.Status == "Completed")
                    {
                        var proj = new FYPProject
                        {
                            Title = req.ProjectTopic ?? "New FYP Project",
                            Description = req.RequestMessage ?? "Pending student updates.",
                            AcademicSession = req.Student.AcademicSession,
                            StudentId = req.StudentId,
                            SupervisorId = req.SupervisorId,
                            Status = "Approved",
                            StartDate = DateTime.Now,
                            CreatedDate = DateTime.Now
                        };
                        _context.FYPProjects.Add(proj);
                        await _context.SaveChangesAsync(); // Get ID

                        _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                        {
                            ProjectId = proj.Id,
                            OldStatus = "None",
                            NewStatus = "Approved",
                            ChangedById = req.SupervisorId,
                            Remarks = "Project created and approved from Supervisor Request."
                        });

                        // Seed Stage 1 from DefaultMilestoneTemplates and Stage 2 hardcoded

                        var milestones = templates.Select(t => new FYPMilestone
                        {
                            FYPProjectId = proj.Id,
                            Title = t.Title,
                            Description = t.Description,
                            DueDate = t.DueDate ?? DateTime.Now.AddMonths(1),
                            Status = "Pending",
                            MilestoneSource = "Default",
                            MilestoneCode = t.MilestoneCode,
                            Stage = t.Stage,
                            IsRequired = t.IsRequired,
                            DefaultMilestoneTemplateId = t.Id
                        }).ToList();

                        milestones.AddRange(new List<FYPMilestone>
                        {
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 1: Turnitin Checking", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 2: Final Report Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 3: Marksheet Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 4: Record of Supervisor Meeting Form Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending", Stage = 2 },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 5: Presentation Slides Submission", DueDate = DateTime.Now.AddMonths(6), Status = "Pending", Stage = 2 }
                        });
                        _context.FYPMilestones.AddRange(milestones);
                    }
                    else
                    {
                        string oldStatus = latestProj.Status;
                        latestProj.SupervisorId = req.SupervisorId;
                        latestProj.Status = "Approved";
                        latestProj.AcademicSession = req.Student.AcademicSession; // Sync session just in case

                        if (oldStatus != "Approved")
                        {
                            _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                            {
                                ProjectId = latestProj.Id,
                                OldStatus = oldStatus,
                                NewStatus = "Approved",
                                ChangedById = req.SupervisorId,
                                Remarks = "Project approved from Supervisor Request."
                            });
                        }
                    }

                    // --- NEW: Reject all other pending requests for this student ---
                    var otherPending = await _context.SupervisorRequests
                        .Where(r => r.StudentId == req.StudentId && r.Id != req.Id && r.Status == "Pending")
                        .ToListAsync();
                    
                    foreach (var other in otherPending)
                    {
                        other.Status = "Rejected";
                        other.RespondedDate = DateTime.Now;
                        other.ResponseNote = "System: Student has been accepted by another supervisor.";
                    }

                    await AddNotificationAsync(req.StudentId, "SupervisorApproval", "Request Approved", $"Your request to {req.Supervisor?.FullName} was approved. Project and milestones initialized.");
                    await LogActivityAsync(req.StudentId, "SupervisorAssigned", "Supervisor Assigned", $"{req.Supervisor?.FullName} is your supervisor.");
                }
                else
                {
                    await AddNotificationAsync(req.StudentId, "SupervisorApproval", "Request Declined", $"Your request to {req.Supervisor?.FullName} was declined.");
                }

                await _context.SaveChangesAsync();
                
                // --- NEW: Email Notification to Student ---
                if (req.Student != null && !string.IsNullOrEmpty(req.Student.Email))
                {
                    string subject = status == "Approved" ? "FYP Supervisor Request Approved!" : "FYP Supervisor Request Declined";
                    string message = $"<p>Dear {req.Student.FullName},</p><p>Your request to be supervised by <strong>{req.Supervisor?.FullName}</strong> has been <strong>{status}</strong>.</p>";
                    if (!string.IsNullOrEmpty(responseNote))
                    {
                        message += $"<p><strong>Supervisor Feedback:</strong> {responseNote}</p>";
                    }
                    message += "<p>Please log in to the FYP Tracking System for more details.</p>";
                    var request = HttpContext.Request;
                    string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                    string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                    
                    try { _emailService.Send(req.Student.Email, subject, body); } catch {}
                }

                // --- NEW: SignalR Real-Time Notification ---
                await _hubContext.Clients.Group($"User_{req.StudentId}").SendAsync("ReceiveRequestStatusUpdate", req.SupervisorId, req.Status, req.Id);

                TempData["Success"] = $"Request {status.ToLower()}.";
            }
            return RedirectToAction(nameof(StudentRequests));
        }

        // ────────────────────────────────────────────────────────
        // My Projects
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> MyProjects(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones)
                .Include(p => p.AssignedExaminers)
                    .ThenInclude(ae => ae.Examiner)
                .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession))
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(projects);
        }

        public async Task<IActionResult> ProjectDetails(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.AssignedExaminers).ThenInclude(ae => ae.Examiner)
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.Id == id && p.SupervisorId == user.Id);

            if (project == null) return NotFound();

            ViewBag.AvailableExaminers = await _context.ApplicationUsers
                .Include(u => u.EmpDepartment)
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Examiner") && !u.IsResign)
                .ToListAsync();

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMilestone(int projectId, string title, string description, DateTime? dueDate)
        {
            var user = await GetCurrentUserAsync();
            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user!.Id);
            
            if (project != null)
            {
                var milestone = new FYPMilestone
                {
                    FYPProjectId = projectId,
                    Title = title,
                    Description = description,
                    DueDate = dueDate,
                    Status = "Draft",
                    MilestoneSource = "Supervisor",
                    IsRequired = false,
                    Stage = 1,
                    CreatedDate = DateTime.Now
                };
                _context.FYPMilestones.Add(milestone);
                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                var assignPayload = new { Type = "AssignMilestone", MilestoneId = milestone.Id, Title = title, Message = $"You have been assigned a new milestone: {title}" };
                await _hubContext.Clients.Group($"User_{project.StudentId}").SendAsync("ReceiveMilestoneUpdate", assignPayload);

                TempData["Success"] = "Milestone added successfully.";
            }
            return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
        }

        // DPP Work & Progress: Supervisor-added Stage 1 milestones are reviewed (not graded).
        // Flow: Draft → PendingSupervisorReview → ReturnedForChanges | Completed
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewProgressMilestone(int milestoneId, string decision, string? feedback)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.SupervisorId == user.Id);

            if (milestone == null || milestone.MilestoneSource != "Supervisor" || milestone.Stage != 1)
            {
                TempData["Error"] = "Invalid progress milestone.";
                return RedirectToAction(nameof(MyProjects));
            }
            if (milestone.Status != "PendingSupervisorReview")
            {
                TempData["Error"] = "This progress milestone is not awaiting your review.";
                return RedirectToAction("GradeSubmission", new { id = milestoneId });
            }

            if (decision == "return")
            {
                if (string.IsNullOrWhiteSpace(feedback))
                {
                    TempData["Error"] = "Please provide feedback when returning for changes.";
                    return RedirectToAction("GradeSubmission", new { id = milestoneId });
                }
                milestone.Status = "ReturnedForChanges";
                milestone.RejectionCount++;
            }
            else if (decision == "complete")
            {
                milestone.Status = "Completed";
            }
            else
            {
                TempData["Error"] = "Invalid action.";
                return RedirectToAction("GradeSubmission", new { id = milestoneId });
            }

            milestone.SupervisorFeedback = feedback;
            await _context.SaveChangesAsync();

            string label = milestone.Status == "Completed" ? "marked as Completed" : "returned for changes";
            await AddNotificationAsync(milestone.Project.StudentId, "SubmissionFeedback", milestone.Title, $"Your progress work was {label}. {feedback}");
            var payload = new { Type = "ReviewMilestone", MilestoneId = milestone.Id, Title = milestone.Title, Status = milestone.Status, Message = $"Your progress milestone '{milestone.Title}' was {label}." };
            await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", payload);

            TempData["Success"] = $"Progress milestone {label}.";
            return RedirectToAction("GradeSubmission", new { id = milestoneId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAddMilestone(string title, string description, DateTime? dueDate, string sessionFilter, string stageSelection = "1")
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Milestone title cannot be empty.";
                return RedirectToAction(nameof(MyProjects), new { sessionFilter });
            }

            // Apply prefix based on selected stage
            string finalTitle = title.Trim();
            if (stageSelection == "1")
            {
                finalTitle = "FYP 1: " + finalTitle;
            }
            else if (stageSelection == "2")
            {
                finalTitle = "Step - " + finalTitle;
            }

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            // Check for duplicate milestone titles across this supervisor's projects in the active session
            bool isDuplicate = await _context.FYPMilestones
                .Include(m => m.Project)
                .AnyAsync(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession) && m.Title.ToLower() == finalTitle.ToLower());

            if (isDuplicate)
            {
                TempData["AddModalError"] = $"A milestone with the title '{finalTitle}' already exists for your students in the current session. Please choose a different name or edit the existing one.";
                return Redirect($"/Supervisor/MyProjects?action=bulk-add");
            }

            if (dueDate.HasValue)
            {
                var firstProject = await _context.FYPProjects.FirstOrDefaultAsync(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession));
                if (firstProject != null)
                {
                    var existingMilestones = await _context.FYPMilestones.Where(m => m.FYPProjectId == firstProject.Id).ToListAsync();
                    string validationError = ValidateChronology(existingMilestones, finalTitle, dueDate);
                    if (validationError != null)
                    {
                        TempData["AddModalError"] = validationError;
                        return Redirect($"/Supervisor/MyProjects?action=bulk-add");
                    }
                }
            }

            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession))
                .ToListAsync();

            if (projects.Any())
            {
                var milestones = projects.Select(p => new FYPMilestone
                {
                    FYPProjectId = p.Id,
                    Title = finalTitle,
                    Description = description,
                    DueDate = dueDate,
                    Status = stageSelection == "2" ? "Pending" : "Draft",
                    MilestoneSource = "Supervisor",
                    IsRequired = false,
                    Stage = stageSelection == "2" ? 2 : 1,
                    CreatedDate = DateTime.Now
                }).ToList();

                _context.FYPMilestones.AddRange(milestones);
                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                foreach (var m in milestones)
                {
                    var assignPayload = new { Type = "AssignMilestone", MilestoneId = m.Id, Title = title, Message = $"You have been assigned a new milestone: {title}" };
                    await _hubContext.Clients.Group($"User_{m.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", assignPayload);
                }
                
                TempData["Success"] = $"Successfully assigned milestone to all {projects.Count} active student(s).";
            }
            else
            {
                TempData["Error"] = "No active projects found under your supervision.";
            }

            return RedirectToAction(nameof(MyProjects), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkEditMilestone(string targetTitle, string? newTitle, DateTime? newDueDate, string? newDescription, string sessionFilter)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (string.IsNullOrEmpty(targetTitle))
            {
                TempData["Error"] = "Invalid request.";
                return RedirectToAction(nameof(MyProjects));
            }

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, null);

            if (!string.IsNullOrWhiteSpace(newTitle) && newTitle.ToLower() != targetTitle.ToLower())
            {
                bool isDuplicate = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .AnyAsync(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession) && m.Title.ToLower() == newTitle.ToLower());

                if (isDuplicate)
                {
                    TempData["EditModalError"] = $"Cannot rename to '{newTitle}' because a milestone with that name already exists in the current session.";
                    return Redirect($"/Supervisor/MyProjects?action=bulk-edit&target={Uri.EscapeDataString(targetTitle)}");
                }
            }

            if (newDueDate.HasValue)
            {
                var firstProject = await _context.FYPProjects.FirstOrDefaultAsync(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession));
                if (firstProject != null)
                {
                    var existingMilestones = await _context.FYPMilestones.Where(m => m.FYPProjectId == firstProject.Id).ToListAsync();
                    string effectiveTitle = string.IsNullOrWhiteSpace(newTitle) ? targetTitle : newTitle;
                    string validationError = ValidateChronology(existingMilestones, effectiveTitle, newDueDate, targetTitle);
                    if (validationError != null)
                    {
                        TempData["EditModalError"] = validationError;
                        return Redirect($"/Supervisor/MyProjects?action=bulk-edit&target={Uri.EscapeDataString(targetTitle)}");
                    }
                }
            }

            var milestones = await _context.FYPMilestones
                .Include(m => m.Project)
                .ThenInclude(p => p.Student)
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession) && m.Title == targetTitle)
                .ToListAsync();

            if (milestones.Any())
            {
                int successCount = 0;
                int errorCount = 0;

                foreach (var milestone in milestones)
                {
                    bool isIaf = milestone.Title.Contains("IAF") || milestone.Title.Contains("Initial Project Allocation");
                    bool isSystemMilestone = milestone.MilestoneSource != "Supervisor";

                    if (isIaf && newDueDate.HasValue)
                    {
                        // Do not allow updating IAF due date via bulk
                    }
                    else if (newDueDate.HasValue) 
                    {
                        milestone.DueDate = newDueDate.Value;
                    }

                    if (milestone.Status != "Pending" && !(milestone.MilestoneSource == "Supervisor" && (milestone.Status == "Draft" || milestone.Status == "ReturnedForChanges")))
                    {
                        // For Submitted/UnderReview/Rejected, only allow editing the Due Date (handled above)
                        successCount++;
                    }
                    else
                    {
                        // Full edit allowed for pending
                        if (!string.IsNullOrWhiteSpace(newTitle) && !isSystemMilestone) 
                        {
                            milestone.Title = newTitle;
                        }
                        if (!string.IsNullOrWhiteSpace(newDescription)) 
                        {
                            milestone.Description = newDescription;
                        }
                        successCount++;
                    }
                }
                
                if (successCount > 0)
                {
                    await _context.SaveChangesAsync();
                    
                    // --- SignalR Real-Time Notification ---
                    foreach (var m in milestones.Where(m => m.Status != "Approved"))
                    {
                        var editPayload = new { Type = "EditMilestone", MilestoneId = m.Id, Title = targetTitle, Message = $"Your milestone '{targetTitle}' has been updated." };
                        await _hubContext.Clients.Group($"User_{m.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", editPayload);
                    }
                }

                if (errorCount > 0)
                {
                    TempData["Success"] = $"Updated '{targetTitle}' for all {successCount} active student(s). Skipped {errorCount} as they are already Approved.";
                }
                else
                {
                    TempData["Success"] = $"Successfully updated '{targetTitle}' for all {successCount} active student(s).";
                }
            }
            else
            {
                TempData["Error"] = "No active student projects have this milestone.";
            }

            return RedirectToAction(nameof(MyProjects), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteMilestone(string targetTitle, string sessionFilter)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (string.IsNullOrEmpty(targetTitle))
            {
                TempData["Error"] = "Invalid request.";
                return RedirectToAction(nameof(MyProjects), new { sessionFilter });
            }

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var milestones = await _context.FYPMilestones
                .Include(m => m.Project)
                .ThenInclude(p => p.Student)
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession) && m.Title == targetTitle)
                .ToListAsync();

            if (milestones.Any())
            {
                if (milestones.Any(m => m.MilestoneSource != "Supervisor"))
                {
                    TempData["Error"] = "Default system milestones cannot be deleted. Only Supervisor-added milestones can be removed.";
                    return RedirectToAction(nameof(MyProjects));
                }

                var toDelete = milestones.Where(m => m.Status == "Pending" || m.Status == "Draft").ToList();
                int skippedCount = milestones.Count - toDelete.Count;

                if (toDelete.Any())
                {
                    _context.FYPMilestones.RemoveRange(toDelete);
                    await _context.SaveChangesAsync();
                    
                    // --- SignalR Real-Time Notification ---
                    foreach (var m in toDelete)
                    {
                        var delPayload = new { Type = "DeleteMilestone", MilestoneId = m.Id, Title = targetTitle, Message = $"Your milestone '{targetTitle}' has been deleted." };
                        await _hubContext.Clients.Group($"User_{m.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", delPayload);
                    }
                }

                if (skippedCount > 0)
                {
                    TempData["Success"] = $"Deleted '{targetTitle}' from all {toDelete.Count} active student(s). Skipped {skippedCount} because they were already submitted.";
                }
                else
                {
                    TempData["Success"] = $"Successfully deleted '{targetTitle}' from all {toDelete.Count} active student(s).";
                }
            }
            else
            {
                TempData["Error"] = "No active student projects have this milestone.";
            }

            return RedirectToAction(nameof(MyProjects), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMilestone(int milestoneId, string title, string description, DateTime? dueDate)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.SupervisorId == user!.Id);

            if (milestone != null)
            {
                if (milestone.Status == "Approved")
                {
                    TempData["Error"] = "Cannot edit an approved milestone.";
                    return RedirectToAction(nameof(ProjectDetails), new { id = milestone.FYPProjectId });
                }

                if (milestone.Status != "Pending")
                {
                    // For Submitted/UnderReview/Rejected, only allow editing the Due Date
                    milestone.DueDate = dueDate;
                    TempData["Success"] = "Milestone due date extended.";
                }
                else
                {
                    milestone.Title = title;
                    milestone.Description = description;
                    milestone.DueDate = dueDate;
                    TempData["Success"] = "Milestone updated successfully.";
                }

                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                var editPayload = new { Type = "EditMilestone", MilestoneId = milestone.Id, Title = title, Message = $"Your milestone '{title}' has been updated." };
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", editPayload);
                
                return RedirectToAction(nameof(ProjectDetails), new { id = milestone.FYPProjectId });
            }
            return RedirectToAction(nameof(MyProjects));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMilestone(int milestoneId)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.SupervisorId == user!.Id);

            if (milestone != null)
            {
                if (milestone.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("Final", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "Core system milestones (Proposal and Final Report) cannot be deleted.";
                    return RedirectToAction(nameof(ProjectDetails), new { id = milestone.FYPProjectId });
                }

                if (milestone.Status != "Pending")
                {
                    TempData["Error"] = "Cannot delete a milestone that has already been submitted by the student.";
                    return RedirectToAction(nameof(ProjectDetails), new { id = milestone.FYPProjectId });
                }

                int projectId = milestone.FYPProjectId;
                _context.FYPMilestones.Remove(milestone);
                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                var delPayload = new { Type = "DeleteMilestone", MilestoneId = milestone.Id, Title = milestone.Title, Message = $"Your milestone '{milestone.Title}' has been deleted." };
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", delPayload);
                
                TempData["Success"] = "Milestone deleted successfully.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
            }
            return RedirectToAction(nameof(MyProjects));
        }

        // ────────────────────────────────────────────────────────
        // Review Submissions
        // ────────────────────────────────────────────────────────
         [HttpGet]
        public async Task<IActionResult> DownloadFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");

            var file = await _context.FYPSubmissionFiles.FindAsync(fileId);
            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

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

            Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{file.FileName}{(string.IsNullOrEmpty(System.IO.Path.GetExtension(file.FileName)) ? ext : "")}\"");
            return PhysicalFile(physicalPath, mimeType);
        }

       [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ViewFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");

            var file = await _context.FYPSubmissionFiles.FindAsync(fileId);
            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

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
        public async Task<IActionResult> DownloadMarksheet(int projectId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user.Id);

            if (project == null) return NotFound();

            string templatePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "documents", "6002CMD - Marksheet.xlsx");
            if (!System.IO.File.Exists(templatePath))
            {
                TempData["Error"] = "Marksheet template not found on server.";
                return RedirectToAction(nameof(ReviewSubmissions));
            }

            using (var workbook = new ClosedXML.Excel.XLWorkbook(templatePath))
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

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string safeTitle = string.IsNullOrWhiteSpace(project.Title) ? "Project" : string.Join("_", project.Title.Split(System.IO.Path.GetInvalidFileNameChars()));
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Marksheet_{safeTitle}.xlsx");
                }
            }
        }

        public async Task<IActionResult> ReviewSubmissions(string? sessionFilter = null, string tab = "pending", int page = 1, int pageSize = 10, string searchQuery = "", string milestoneFilter = "", string statusFilter = "", string stageFilter = "")
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var baseQuery = _context.FYPMilestones
                .Include(m => m.Project).ThenInclude(p => p.Student)
                .Include(m => m.SubmissionFiles.Where(f => !f.IsDeleted))
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession));

            ViewBag.TotalStudents = await _context.FYPProjects.CountAsync(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession));
            ViewBag.PendingReviewsCount = await baseQuery.CountAsync(m => m.Status == "Submitted" || m.Status == "PendingSupervisorReview" || m.Status == "UnderReview");
            ViewBag.ReviewedCount = await baseQuery.CountAsync(m => m.Status == "Approved" || m.Status == "ReturnedForChanges" || m.Status == "ReturnedForEdits" || m.Status == "Rejected" || m.Status == "Completed" || m.Status == "SupervisorSigned");

            var pendingQuery = baseQuery.Where(m => m.Status == "Submitted" || m.Status == "PendingSupervisorReview" || m.Status == "UnderReview");
            
            var allStudentsQuery = _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones).ThenInclude(m => m.SubmissionFiles.Where(f => !f.IsDeleted))
                .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession));

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                pendingQuery = pendingQuery.Where(m => m.Project.Student.FullName.Contains(searchQuery) || m.Project.Student.EmployeeId.Contains(searchQuery));
                allStudentsQuery = allStudentsQuery.Where(p => p.Student.FullName.Contains(searchQuery) || p.Student.EmployeeId.Contains(searchQuery));
            }
            if (!string.IsNullOrWhiteSpace(milestoneFilter))
            {
                pendingQuery = pendingQuery.Where(m => m.Title == milestoneFilter);
                allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => m.Title == milestoneFilter));
            }
            if (!string.IsNullOrWhiteSpace(stageFilter) && int.TryParse(stageFilter, out int stg))
            {
                pendingQuery = pendingQuery.Where(m => m.Stage == stg);
                allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => m.Stage == stg));
            }
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (statusFilter == "Not Submitted")
                {
                    allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => !m.SubmittedDate.HasValue && (string.IsNullOrEmpty(m.Status) || m.Status == "Pending")));
                }
                else if (statusFilter == "Approved")
                {
                    allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => m.Status == "Approved" || m.Status == "Completed" || m.Status == "SupervisorSigned" || m.Status == "AdminApproved" || m.Status == "PendingPanelReview"));
                }
                else if (statusFilter == "Revision Required")
                {
                    allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => m.Status == "ReturnedForChanges" || m.Status == "ReturnedForEdits" || m.Status == "Rejected"));
                }
                else if (statusFilter == "Locked")
                {
                    allStudentsQuery = allStudentsQuery.Where(p => p.Milestones.Any(m => m.Status == "Locked"));
                }
            }

            int totalPending = await pendingQuery.CountAsync();
            var pendingList = await pendingQuery
                .OrderBy(m => m.SubmittedDate ?? DateTime.MaxValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int totalAll = await allStudentsQuery.CountAsync();
            var allStudentsList = await allStudentsQuery
                .OrderBy(p => p.Student.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.PendingList = pendingList;
            ViewBag.AllStudentsList = allStudentsList;
            ViewBag.TotalPending = totalPending;
            ViewBag.TotalAll = totalAll;
            ViewBag.ActiveTab = tab;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.MilestoneFilter = milestoneFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.StageFilter = stageFilter;
            ViewBag.AllMilestoneTitles = await baseQuery.Select(m => m.Title).Distinct().OrderBy(t => t).ToListAsync();

            return View(new List<FYPMilestone>()); // Passing empty model, since we use ViewBag
        }

        [HttpGet]
        public async Task<IActionResult> GradeMarksheetStage2(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones)
                .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.Id == id && p.SupervisorId == user.Id);
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            var stage2Milestones = project.Milestones.Where(m => m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase)).ToList();
            bool isAllStage2Approved = stage2Milestones.Any() && stage2Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!isAllStage2Approved)
            {
                TempData["Error"] = "You must approve ALL Stage 2 milestones (including the Final Report and Turnitin Score) before you can submit the final marksheet.";
                return RedirectToAction(nameof(ProjectDetails), new { id = id });
            }

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeMarksheetStage2(int projectId, string marksheetData, string finalGrade)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");

            var project = await _context.FYPProjects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user.Id);
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(Index));
            }

            project.Stage2SupervisorMarksheetData = marksheetData;

            // Optional: Calculate Joint Final Grade if Examiner has also submitted
            if (!string.IsNullOrEmpty(project.Stage2ExaminerMarksheetData))
            {
                try
                {
                    double supTotal = FYPTracker.Utils.GradingHelper.CalculateStage2Score(marksheetData);
                    double exTotal = FYPTracker.Utils.GradingHelper.CalculateStage2Score(project.Stage2ExaminerMarksheetData);

                    // Using the stakeholder formula: examiner component = 70%; supervisor component = 80%; combined value = 150%, then divide by 100% (1.5) to obtain the total mark.
                    // Wait, audit says "(SupervisorScore + ExaminerScore) / 2"
                    double finalScore = (exTotal + supTotal) / 2.0;
                    
                    project.FinalGrade = GetGradeLabel(finalScore);
                    TempData["Success"] = $"Stage 2 grading complete! Final joint score calculated as {finalScore:F1}%.";
                }
                catch (Exception)
                {
                    // Fallback
                    project.FinalGrade = finalGrade;
                    TempData["Success"] = "Stage 2 marks submitted. Examiner marks could not be parsed for joint calculation.";
                }
            }
            else
            {
                project.FinalGrade = finalGrade;
                TempData["Success"] = "Stage 2 marks submitted successfully! Waiting for Examiner to submit their marks for the final joint calculation.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
        }

        [HttpGet]
        public async Task<IActionResult> GradeSubmission(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project).ThenInclude(p => p.Student)
                .Include(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(m => m.Id == id && m.Project.SupervisorId == user.Id);

            if (milestone == null)
            {
                TempData["Error"] = "Submission not found or unauthorized.";
                return RedirectToAction(nameof(ReviewSubmissions));
            }

            if (milestone.Title.Contains("Initial Project Allocation Form (IAF)", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase))
            {
                FYPTracker.Utils.IafPdfGenerator.EnsurePdfSnapshots(milestone, System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot"), _context);
            }

            return View(milestone);
        }

        // =====================================================
        // SUPERVISOR - READ ONLY IAF VIEWER
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> ViewDigitalForm(
            int milestoneId,
            int attemptNumber)
        {
            // 1. Verify logged-in Supervisor
            var user = await GetCurrentUserAsync();

            if (user == null)
                return RedirectToAction("Login", "User");

            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor"))
                return RedirectToAction("AccessDenied", "Home");

            // 2. Only access milestones belonging to this Supervisor
            var milestone = await _context.FYPMilestones
                .AsNoTracking()
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .Include(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(m =>
                    m.Id == milestoneId &&
                    m.Project.SupervisorId == user.Id);

            if (milestone == null)
                return NotFound();

            // 3. Ensure this is an IAF milestone


            // 4. Read IAF Attempt History
            string raw = milestone.Remarks ?? "";

            int jsonIndex = raw.IndexOf(
                "JSON: ",
                StringComparison.Ordinal);

            string parsedJson = jsonIndex >= 0
                ? raw.Substring(jsonIndex + "JSON: ".Length)
                : raw;

            var history =
                IafAttemptHistory.ParseOrMigrate(parsedJson);

            // 5. Validate requested Attempt Number
            bool attemptExists = history.Attempts.Any(
                a => a.AttemptNumber == attemptNumber);

            if (!attemptExists)
                return NotFound();

            // 6. Pass Attempt Number to View
            ViewData["AttemptNumber"] = attemptNumber;
            ViewBag.CurrentUser = user;

            // Read-only page
            return View("ViewDigitalForm", milestone);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewSubmission(int milestoneId, string status, string? remarks, string? supervisorSignature, string? pctComplete, int? turnitinScore, IFormFile? gradedFile)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone != null && milestone.Project.SupervisorId == user?.Id)
            {
                if (milestone.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase))
                {
                    if (status == "Approved") {
                        status = "SupervisorSigned";
                    } else if (status == "Rejected") {
                        status = "ReturnedForEdits";
                    }
                }

                bool isIafSubmission =
                    milestone.Title.Contains(
                        "Initial Project Allocation",
                        StringComparison.OrdinalIgnoreCase) ||
                    milestone.Title.Contains(
                        "IAF",
                        StringComparison.OrdinalIgnoreCase);

                if (isIafSubmission)
                {
                    string raw = milestone.Remarks ?? "";
                    int index = raw.IndexOf("JSON: ", StringComparison.Ordinal);

                    string json = index >= 0
                        ? raw.Substring(index + "JSON: ".Length)
                        : raw;

                    var history = IafAttemptHistory.ParseOrMigrate(json);

                    var attempt = history.Attempts.FirstOrDefault(
                        a => a.AttemptNumber == history.CurrentAttempt);

                    bool alreadySigned =
                        !string.IsNullOrWhiteSpace(
                            attempt?.SupervisorSignature);

                    if (alreadySigned)
                    {
                        return Conflict(
                            "This IAF has already been signed and cannot be signed again.");
                    }
                }

                milestone.Status = status;
                milestone.SupervisorFeedback = remarks;

                if (milestone.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = milestone.Remarks ?? "";
                    if (raw.Contains("JSON: "))
                    {
                        var parts = raw.Split(new[] { "JSON: " }, StringSplitOptions.None);
                        var formInfo = parts[0].Trim();
                        var parsedJson = parts[1];
                        try
                        {
                            var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parsedJson);
                            var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                            
                            if (currentAttempt != null)
                            {
                                currentAttempt.SupervisorFeedback = remarks;
                                currentAttempt.Status = status;
                                currentAttempt.SupervisorReviewedAt = DateTime.Now;
                                
                                if (status == "SupervisorSigned" &&
                                    !string.IsNullOrWhiteSpace(supervisorSignature))
                                {
                                    currentAttempt.SupervisorSignature = supervisorSignature;

                                    currentAttempt.FormData ??=
                                        new Dictionary<string, string>();

                                    // Preserve the actual signer's identity
                                    // in this specific IAF attempt.
                                    if (!currentAttempt.FormData.ContainsKey("__iafSignedByName"))
                                    {
                                        currentAttempt.FormData["__iafSignedByName"] =
                                            user!.FullName?.Trim() ?? "";
                                    }

                                    if (!currentAttempt.FormData.ContainsKey("__iafSignedById"))
                                    {
                                        currentAttempt.FormData["__iafSignedById"] =
                                            user!.Id.ToString();
                                    }
                                }
                                
                                if (status == "SupervisorSigned")
                                {
                                    // Generate Immutable PDF Snapshot
                                    try
                                    {
                                        var pdfBytes = FYPTracker.Utils.IafPdfGenerator.GenerateIafPdf(milestone, currentAttempt, milestone.Project?.Student, System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot"));
                                        string uniqueFileName = $"IAF_Snapshot_{milestone.Project?.Student?.EmployeeId ?? "Student"}_Attempt_{currentAttempt.AttemptNumber}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
                                        string folderPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "uploads", "submissions");
                                        if (!System.IO.Directory.Exists(folderPath)) System.IO.Directory.CreateDirectory(folderPath);
                                        string filePath = System.IO.Path.Combine(folderPath, uniqueFileName);
                                        System.IO.File.WriteAllBytes(filePath, pdfBytes);
                                        
                                        var subFile = new FYPSubmissionFile
                                        {
                                            MilestoneId = milestone.Id,
                                            FileName = $"Signed IAF (Attempt {currentAttempt.AttemptNumber})",
                                            FilePath = $"/uploads/submissions/{uniqueFileName}",
                                            FileSize = pdfBytes.Length,
                                            UploadedBy = user?.Id,
                                            UploadedDate = DateTime.Now,
                                            AttemptNumber = currentAttempt.AttemptNumber,
                                            Remarks = "System Generated Snapshot"
                                        };
                                        _context.FYPSubmissionFiles.Add(subFile);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Log error, but continue approval
                                        System.Diagnostics.Debug.WriteLine($"Error generating PDF: {ex.Message}");
                                    }
                                }
                                
                                var updatedJson = System.Text.Json.JsonSerializer.Serialize(history);
                                milestone.Remarks = $"{formInfo} JSON: {updatedJson}";
                            }
                        }
                        catch {}
                    }
                }

                if ((milestone.Title.Contains("Progress", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(pctComplete))
                {
                    string raw = milestone.Remarks ?? "";
                    if (raw.Contains("JSON: "))
                    {
                        var parts = raw.Split(new[] { "JSON: " }, StringSplitOptions.None);
                        var formInfo = parts[0].Trim();
                        var parsedJson = parts[1];
                        try
                        {
                            var dict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(parsedJson);
                            if (dict != null)
                            {
                                dict["Completion"] = pctComplete;
                                var updatedJson = System.Text.Json.JsonSerializer.Serialize(dict);
                                milestone.Remarks = $"{formInfo} JSON: {updatedJson}";
                            }
                        }
                        catch {}
                    }
                }

                if (milestone.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase))
                {
                    
                    if (turnitinScore.HasValue)
                    {
                        milestone.Project.TurnitinScore = turnitinScore.Value;
                        if (status == "Approved")
                        {
                            if (turnitinScore.Value <= 20)
                            {
                                milestone.Project.IsTurnitinApproved = true;
                            }
                            else
                            {
                                status = "Rejected"; // Override local status variable
                                milestone.Status = "Rejected"; // Override entity status
                                TempData["Error"] = $"Turnitin score ({turnitinScore.Value}%) exceeds the 20% limit. Approval blocked. Status changed to Rejected.";
                                // Do not return early, let it flow to send emails and increment RejectionCount
                            }
                        }
                        else
                        {
                            milestone.Project.IsTurnitinApproved = false;
                        }
                    }
                }

                if (gradedFile != null && gradedFile.Length > 0)
                {
                    var extCheck = System.IO.Path.GetExtension(gradedFile.FileName).ToLowerInvariant();
                    bool isMarksheetStage = milestone.Title.Contains("Final Report", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase);
                    
                    var allowedExt = isMarksheetStage 
                        ? new[] { ".xls", ".xlsx" } 
                        : new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };

                    if (!allowedExt.Contains(extCheck))
                    {
                        string msg = isMarksheetStage ? "Only Excel (.xls, .xlsx) files are allowed for the graded marksheet." : "Only PDF, Word, and Excel documents are allowed for graded files.";
                        return BadRequest(new { message = msg });
                    }
                    if (gradedFile.Length > 20 * 1024 * 1024)
                    {
                        return BadRequest(new { message = "File size must not exceed 20 MB." });
                    }

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + gradedFile.FileName;
                    string folderPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "uploads", "submissions");
                    if (!System.IO.Directory.Exists(folderPath))
                        System.IO.Directory.CreateDirectory(folderPath);

                    string filePath = System.IO.Path.Combine(folderPath, uniqueFileName);
                    using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
                    {
                        await gradedFile.CopyToAsync(stream);
                    }

                    _context.FYPSubmissionFiles.Add(new FYPSubmissionFile
                    {
                        MilestoneId = milestone.Id,
                        FileName = gradedFile.FileName,
                        FilePath = $"/uploads/submissions/{uniqueFileName}",
                        FileSize = gradedFile.Length,
                        UploadedBy = user.Id,
                        UploadedDate = DateTime.Now
                    });

                    // Parse the Proposal/Marksheet score if applicable
                    if (milestone.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase) && (extCheck == ".xls" || extCheck == ".xlsx"))
                    {
                        try
                        {
                            using (var workbook = new XLWorkbook(filePath))
                            {
                                var worksheet = workbook.Worksheets.FirstOrDefault();
                                if (worksheet != null)
                                {
                                    var cellValue = worksheet.Cell("J16").Value;
                                    string extractedScore = "";
                                    if (cellValue != null)
                                    {
                                        string stringValue = cellValue.ToString();
                                        if (double.TryParse(stringValue, out double numericScore)) extractedScore = numericScore.ToString();
                                        else if (stringValue.Contains("-") && double.TryParse(stringValue.Split('-').Last().Trim(), out double scoreFromText)) extractedScore = scoreFromText.ToString();
                                        else extractedScore = stringValue.Trim();
                                    }
                                    
                                    if (!double.TryParse(extractedScore, out _))
                                    {
                                        System.IO.File.Delete(filePath);
                                        return BadRequest(new { message = "Invalid Marksheet: Could not extract a valid numeric score. Please ensure you are using the predefined template and have filled in the marks." });
                                    }
                                    
                                    var strengthsCell = worksheet.Cell("C19").Value;
                                    var improvementsCell = worksheet.Cell("C22").Value;
                                    var commentsCell = worksheet.Cell("C25").Value;
                                    
                                    var feedbackData = new {
                                        Score = extractedScore,
                                        Strengths = strengthsCell?.ToString().Trim() ?? "",
                                        Improvements = improvementsCell?.ToString().Trim() ?? "",
                                        Comments = commentsCell?.ToString().Trim() ?? ""
                                    };
                                    milestone.SupervisorMarksheetData = System.Text.Json.JsonSerializer.Serialize(feedbackData);
                                }
                            }

                            // Calculate Joint Evaluation
                            double supScore = 0;
                            if (!string.IsNullOrEmpty(milestone.SupervisorMarksheetData)) {
                                if (milestone.SupervisorMarksheetData.StartsWith("{")) {
                                    using var doc = System.Text.Json.JsonDocument.Parse(milestone.SupervisorMarksheetData);
                                    if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double s)) supScore = s;
                                } else {
                                    double.TryParse(milestone.SupervisorMarksheetData, out supScore);
                                }
                            }

                            double exScore = 0;
                            bool hasExScore = false;
                            if (!string.IsNullOrEmpty(milestone.ExaminerMarksheetData)) {
                                if (milestone.ExaminerMarksheetData.StartsWith("{")) {
                                    using var doc = System.Text.Json.JsonDocument.Parse(milestone.ExaminerMarksheetData);
                                    if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double e)) { exScore = e; hasExScore = true; }
                                } else {
                                    hasExScore = double.TryParse(milestone.ExaminerMarksheetData, out exScore);
                                }
                            }

                            if (supScore > 0)
                            {
                                if (hasExScore)
                                {
                                    milestone.Project.Stage1TotalScore = (supScore + exScore) / 2.0;
                                    milestone.Project.Stage1Grade = milestone.Project.Stage1TotalScore >= 40.0 ? "Pass" : "Fail";
                                }
                                else 
                                {
                                    milestone.Project.Stage1TotalScore = supScore;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.Delete(filePath);
                            return BadRequest(new { message = "Invalid Marksheet: The uploaded Excel file is either corrupted or not the predefined template." });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                
                // --- Email Notification ---
                if (milestone.Project.Student != null && !string.IsNullOrEmpty(milestone.Project.Student.Email))
                {
                    string subject = $"Milestone Reviewed: {milestone.Title}";
                    string message = $"<p>Dear {milestone.Project.Student.FullName},</p><p>Your milestone '<strong>{milestone.Title}</strong>' for project '<strong>{milestone.Project.Title}</strong>' has been reviewed by your supervisor <strong>{milestone.Project.Supervisor?.FullName}</strong> and marked as <strong>{status}</strong>.</p>";
                    if (!string.IsNullOrEmpty(remarks))
                    {
                        message += $"<p><strong>Supervisor Feedback:</strong> {remarks}</p>";
                    }
                    message += "<p>Please log in to the FYP Tracking System to view any graded files or Turnitin scores.</p>";
                    var request = HttpContext.Request;
                    string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                    string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                    
                    try { _emailService.Send(milestone.Project.Student.Email, subject, body); } catch {}
                }

                // --- SignalR Real-Time Notification ---
                string actionStr = status == "Approved" ? "graded" : "reviewed";
                var reviewPayload = new { Type = "ReviewMilestone", MilestoneId = milestone.Id, Title = milestone.Title, Status = status, Message = $"Your milestone '{milestone.Title}' was {actionStr}." };
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", reviewPayload);

                if (status == "Approved") {
                    await AddNotificationAsync(milestone.Project.StudentId, "SubmissionFeedback", $"{milestone.Title} Approved", remarks ?? "No remarks provided.");
                    await LogActivityAsync(milestone.Project.StudentId, "MilestoneApproved", $"{milestone.Title} Approved");

                    // ── Seed Final Evaluation Steps when Final/Thesis milestone is approved ──
                    bool isFinalMilestone = milestone.Title.Contains("Final", StringComparison.OrdinalIgnoreCase) 
                                        || milestone.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase);
                    bool stepsAlreadyExist = await _context.FYPMilestones
                        .AnyAsync(m => m.FYPProjectId == milestone.FYPProjectId && m.Title.StartsWith("Step"));

                    if (isFinalMilestone && !stepsAlreadyExist)
                    {
                        var stepMilestones = new List<FYPMilestone>
                        {
                            new FYPMilestone { FYPProjectId = milestone.FYPProjectId, Title = "Step 1: Turnitin", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = milestone.FYPProjectId, Title = "Step 2: DPP report full", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = milestone.FYPProjectId, Title = "Step 3: Marksheet Submission", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = milestone.FYPProjectId, Title = "Step 4: Record of Supervisor Meeting Form Submission", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = milestone.FYPProjectId, Title = "Step 5: Presentation Slides Submission", Status = "Pending" }
                        };
                        _context.FYPMilestones.AddRange(stepMilestones);
                        await _context.SaveChangesAsync();
                    }
                } 
                else if (status == "Rejected") {
                    milestone.RejectionCount++;
                    await AddNotificationAsync(milestone.Project.StudentId, "RevisionRequest", $"{milestone.Title} Rejected", remarks ?? "No remarks provided.");
                    await LogActivityAsync(milestone.Project.StudentId, "FeedbackReceived", $"Revision needed for {milestone.Title}");
                }

                TempData["Success"] = "Review saved.";
            }
            return RedirectToAction(nameof(ReviewSubmissions));
        }

        // ────────────────────────────────────────────────────────
        // Manage Availability
        [HttpGet]
        public async Task<IActionResult> ManageAvailability()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            var availabilities = await _context.SupervisorAvailabilities
                .Where(a => a.SupervisorId == user.Id)
                .ToListAsync();

            return View(availabilities);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAvailability(SupervisorAvailability model, List<string> SelectedDays)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");

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
                            return RedirectToAction(nameof(ManageAvailability));
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
                            return RedirectToAction(nameof(ManageAvailability));
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
            TempData["Success"] = "Availability saved successfully!";
            return RedirectToAction(nameof(ManageAvailability));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAvailability(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");

            var availability = await _context.SupervisorAvailabilities.FindAsync(id);
            if (availability != null && availability.SupervisorId == user.Id)
            {
                _context.SupervisorAvailabilities.Remove(availability);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Availability removed.";
            }

            return RedirectToAction(nameof(ManageAvailability));
        }

        // ────────────────────────────────────────────────────────
        // Manage Meetings
        [HttpGet]
        public async Task<IActionResult> GetBookedSlots(int supervisorId, string date)
        {
            if (!DateTime.TryParse(date, out var d)) return Json(new List<string>());

            var bookedTimes = await _context.MeetingBookings
                .Where(m => m.SupervisorId == supervisorId && m.MeetingDate.Date == d.Date && m.Status != "Rejected" && m.Status != "Cancelled")
                .Select(m => m.MeetingTime)
                .ToListAsync();

            return Json(bookedTimes);
        }

        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> ManageMeetings(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;
            ViewBag.StartHour = 8;
            ViewBag.EndHour = 17;

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            // Auto-Expire Stale Pending Meetings
            var staleMeetings = await _context.MeetingBookings
                .Where(m => m.SupervisorId == user.Id && m.Status == "Pending" && m.MeetingDate < DateTime.Today)
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
                .Include(m => m.Student)
                .Include(m => m.MeetingLog)
                .Where(m => m.SupervisorId == user.Id && !m.Student.IsResign && (activeSession == null || m.Student.AcademicSession == activeSession))
                .OrderByDescending(m => m.CreatedDate)
                .ToListAsync();

            ViewBag.MsAccountConnected = !string.IsNullOrEmpty(user.MsAccessToken);

            return View(meetings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondMeeting(int meetingId, string status, string? note, DateTime? proposedDate, string? newMeetingTime, string? location, int recurringWeeks = 1)
        {
            var user = await GetCurrentUserAsync();
            var meeting = await _context.MeetingBookings.Include(m => m.Student).FirstOrDefaultAsync(m => m.Id == meetingId && m.SupervisorId == user.Id);
            
            if (meeting != null && meeting.Status == "Pending")
            {
                var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true && s.IsActive);
                


                meeting.Status = status;
                meeting.SupervisorNote = note;
                meeting.ProposedNewDate = proposedDate;
                if (!string.IsNullOrEmpty(newMeetingTime))
                {
                    meeting.MeetingTime = newMeetingTime;
                }
                meeting.Location = location;
                meeting.AcademicSessionId = activeSession?.Id;
                meeting.RespondedDate = DateTime.Now;
                
                if (status == "Approved" && location == "Online")
                {
                    // Automatically generate a Jitsi Meet link instead of Microsoft Teams
                    string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string safeStudentName = meeting.Student?.FullName != null ? new string(meeting.Student.FullName.Where(char.IsLetterOrDigit).ToArray()) : "Student";
                    meeting.MeetingLink = $"https://meet.jit.si/FYP_Meeting_{safeStudentName}_{uniqueId}";
                }

                int generatedCount = 0;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    
                    await _context.SaveChangesAsync();
                    
                    if (status == "Approved")
                    {
                        var result = await _weeklyMeetingSchedulingService.GenerateWeeklyScheduleAsync(meeting.StudentId, user.Id, meeting.Id);
                        if (!string.IsNullOrEmpty(result.Message))
                        {
                            TempData["Success"] += $" {result.Message}";
                        }
                        generatedCount = result.ScheduledCount + result.ConflictCount;
                    }
                    
                    await transaction.CommitAsync();
                });

                // Send email notification to student
                if (meeting.Student != null && !string.IsNullOrEmpty(meeting.Student.Email))
                {
                    string subject = $"FYP Meeting {status}";
                    string message = $@"<p>Dear {meeting.Student.FullName},</p><br/>
                        <p>Your supervisor <strong>{user?.FullName}</strong> has <strong>{status.ToLower()}</strong> your meeting request.</p>
                        <ul>
                            <li><strong>Date:</strong> {meeting.MeetingDate:dd MMM yyyy}</li>
                            <li><strong>Time:</strong> {meeting.MeetingTime}</li>
                            {(status == "Approved" && !string.IsNullOrEmpty(location) ? $"<li><strong>Location:</strong> {location}</li>" : "")}
                            {(status == "Approved" && generatedCount > 0 ? $"<li><strong>Note:</strong> Auto-generated for the next {generatedCount} weeks.</li>" : "")}
                        </ul>
                        {(string.IsNullOrEmpty(note) ? "" : $"<p><strong>Supervisor Feedback:</strong> {note}</p>")}";
                    var request = HttpContext.Request;
                    string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                    string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                    _emailService.Send(meeting.Student.Email, subject, body);
                }

                await AddNotificationAsync(meeting.StudentId, "MeetingUpdate", $"Meeting {status}", note);
                await LogActivityAsync(meeting.StudentId, "MeetingBooked", $"Meeting {status}");
                
                // --- SignalR Real-Time Notification ---
                var responsePayload = new {
                    Type = "MeetingStatusUpdate",
                    MeetingId = meeting.Id,
                    Status = status,
                    MeetingDate = meeting.MeetingDate.ToString("yyyy-MM-dd"),
                    MeetingTime = meeting.MeetingTime,
                    Title = $"Meeting {status}",
                    Message = $"Your meeting request was {status.ToLower()}. {(string.IsNullOrEmpty(note) ? "" : "Note: " + note)}"
                };
                await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", responsePayload);

                if (TempData["Success"] == null)
                {
                    TempData["Success"] = $"Meeting {status.ToLower()}.";
                }
            }
            return RedirectToAction(nameof(ManageMeetings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondMeetingFromNotification(int meetingId, int notificationId, string status, string? note)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var meeting = await _context.MeetingBookings.FirstOrDefaultAsync(m => m.Id == meetingId && m.SupervisorId == user.Id);
            
            if (meeting != null && meeting.Status == "Pending")
            {
                meeting.Status = status;
                meeting.SupervisorNote = note;
                meeting.RespondedDate = DateTime.Now;

                // Mark notification as read
                var notification = await _context.FYPNotifications.FirstOrDefaultAsync(n => n.Id == notificationId);
                if (notification != null)
                {
                    notification.IsRead = true;
                    notification.ReadDate = DateTime.Now;
                }

                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    
                    await _context.SaveChangesAsync();
                    
                    if (status == "Approved")
                    {
                        var result = await _weeklyMeetingSchedulingService.GenerateWeeklyScheduleAsync(meeting.StudentId, user.Id, meeting.Id);
                        if (!string.IsNullOrEmpty(result.Message))
                        {
                            TempData["Success"] += $" {result.Message}";
                        }
                    }
                    
                    await transaction.CommitAsync();
                });

                string notifMessage = $"Your meeting request for {meeting.MeetingDate:dd MMM yyyy} was {status.ToLower()}.";
                if (!string.IsNullOrEmpty(note))
                {
                    notifMessage += $" Supervisor Note: {note}";
                }

                await AddNotificationAsync(meeting.StudentId, "MeetingUpdate", $"Meeting {status}", notifMessage);
                await LogActivityAsync(meeting.StudentId, "MeetingBooked", $"Meeting {status}");
                
                // --- SignalR Real-Time Notification ---
                var responsePayload2 = new {
                    Type = "MeetingStatusUpdate",
                    MeetingId = meeting.Id,
                    Status = status,
                    MeetingDate = meeting.MeetingDate.ToString("yyyy-MM-dd"),
                    MeetingTime = meeting.MeetingTime,
                    Title = $"Meeting {status}",
                    Message = notifMessage
                };
                await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", responsePayload2);

                TempData["Success"] = $"Meeting {status.ToLower()}.";
            }
            else if (meeting != null && meeting.Status != "Pending")
            {
                TempData["Info"] = $"Meeting was already {meeting.Status.ToLower()}.";
                
                var notification = await _context.FYPNotifications.FirstOrDefaultAsync(n => n.Id == notificationId);
                if (notification != null)
                {
                    notification.IsRead = true;
                    notification.ReadDate = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }
            
            return RedirectToAction(nameof(Notifications));
        }

        // Generates the immutable signed PDF BEFORE committing the Signed status.
        // The draft's frozen JSON template is reused, never the current Admin template.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewMeetingLogSupervisor(
            int meetingId,
            string? supervisorComments,
            string? supervisorSignature)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login", "User");

            var meeting = await _context.MeetingBookings
                .Include(m => m.Student)
                .Include(m => m.Supervisor)
                .Include(m => m.MeetingLog)
                .FirstOrDefaultAsync(m =>
                    m.Id == meetingId && m.SupervisorId == user.Id);

            if (meeting == null || meeting.Status != "Approved" || meeting.MeetingLog == null)
            {
                TempData["Error"] = "Approved meeting logbook not found.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            var log = meeting.MeetingLog;
            if (log.LogStatus == "Signed")
            {
                TempData["Error"] = "This logbook is already signed and cannot be signed again.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            // Canvas sends a PNG data URL. Validate on the server, not only in JavaScript.
            const string pngPrefix = "data:image/png;base64,";
            if (string.IsNullOrWhiteSpace(supervisorSignature) ||
                !supervisorSignature.StartsWith(pngPrefix, StringComparison.OrdinalIgnoreCase) ||
                supervisorSignature.Length > 2_800_000)
            {
                TempData["Error"] = "Please draw a valid supervisor signature before signing.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(
                    supervisorSignature.Substring(pngPrefix.Length));

                byte[] pngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };
                if (imageBytes.Length < pngHeader.Length ||
                    !imageBytes.AsSpan(0, pngHeader.Length).SequenceEqual(pngHeader))
                {
                    TempData["Error"] = "Invalid PNG supervisor signature.";
                    return RedirectToAction(nameof(ManageMeetings));
                }
            }
            catch (FormatException)
            {
                TempData["Error"] = "Invalid signature image data. Please sign again.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            // Must use the template frozen when the student submitted this draft.
            // Do NOT silently use the current Admin template for historical records.
            var frozenTemplate = FYPTracker.Utils.MeetingLogPdfGenerator
                .GetFrozenTemplateSnapshot(
                    _env.ContentRootPath,
                    meeting.Id,
                    log.PdfLogbookPath);

            if (frozenTemplate == null ||
                frozenTemplate.Fields == null ||
                frozenTemplate.Fields.Count == 0)
            {
                TempData["Error"] =
                    "The original logbook template snapshot is missing. " +
                    "Signing has been cancelled to protect the original record. " +
                    "Please restore the draft template snapshot before trying again.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            // The MeetingLog model stores the signature after |||SIG|||.
            // Apply the signing data in memory so the PDF contains the final values.
            string safeComments = (supervisorComments ?? string.Empty)
                .Replace("|||SIG|||", string.Empty)
                .Trim();
            log.SupervisorComments = safeComments + "|||SIG|||" + supervisorSignature;
            log.SignatureDate = DateTime.Now;
            log.LogStatus = "Signed";

            string? signedPdfPath = null;
            try
            {
                // If PDF generation fails, SaveChangesAsync() is never reached.
                signedPdfPath = FYPTracker.Utils.MeetingLogPdfGenerator.SavePdfSnapshot(
                    _env.ContentRootPath,
                    meeting,
                    frozenTemplate,
                    isSigned: true);

                if (string.IsNullOrWhiteSpace(signedPdfPath))
                    throw new InvalidOperationException("Signed PDF path was not returned.");

                log.SignedPdfLogbookPath = signedPdfPath;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                HttpContext.RequestServices
                    .GetRequiredService<ILogger<SupervisorController>>()
                    .LogError(ex, "Failed to sign MeetingLog {MeetingLogId} (booking {MeetingId}).",
                        log.Id, meeting.Id);

                // Avoid orphan PDF if the file was created but the DB update failed.
                if (!string.IsNullOrWhiteSpace(signedPdfPath))
                {
                    string name = System.IO.Path.GetFileName(signedPdfPath);
                    if (name.StartsWith($"Signed_Log_{meeting.Id}_", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = System.IO.Path.Combine(
                            _env.ContentRootPath, "App_Data", "MeetingLogSnapshots", name);
                        try { System.IO.File.Delete(path); }
                        catch (IOException) { /* Logged error above; clean up manually if needed. */ }
                        catch (UnauthorizedAccessException) { /* Do not hide original error. */ }
                    }
                }

                TempData["Error"] =
                    "Signing was not completed because the signed PDF could not be saved. " +
                    "Please check the application logs and try again.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            // Notification delivery is non-critical: the signed PDF is already saved.
            bool notificationFailed = false;
            try
            {
                await AddNotificationAsync(meeting.StudentId,
                    "MeetingLogSigned", "Progress Logbook Signed",
                    $"Your supervisor has signed the progress logbook for the meeting on {meeting.MeetingDate:dd MMM yyyy}.");
            }
            catch (Exception ex)
            {
                notificationFailed = true;
                HttpContext.RequestServices
                    .GetRequiredService<ILogger<SupervisorController>>()
                    .LogError(ex, "In-app notification failed for signed MeetingLog {MeetingLogId}.", log.Id);
            }

            try
            {
                var payload = new
                {
                    Type = "MeetingLogSigned",
                    MeetingId = meeting.Id,
                    Title = "Logbook Signed",
                    Message = "Your supervisor has signed your progress logbook."
                };
                await _hubContext.Clients.Group($"User_{meeting.StudentId}")
                    .SendAsync("ReceiveMeetingUpdate", payload);
            }
            catch (Exception ex)
            {
                notificationFailed = true;
                HttpContext.RequestServices
                    .GetRequiredService<ILogger<SupervisorController>>()
                    .LogError(ex, "Real-time notification failed for signed MeetingLog {MeetingLogId}.", log.Id);
            }

            TempData["Success"] = notificationFailed
                ? "Logbook signed and PDF saved. A notification may be delayed."
                : "Progress logbook signed and PDF saved successfully!";
            return RedirectToAction(nameof(ManageMeetings));
        }

        // Supervisor's secure, read-only view of the signed PDF.
        [HttpGet]
        public async Task<IActionResult> ViewMeetingLog(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor"))
                return RedirectToAction("AccessDenied", "Home");

            var log = await _context.MeetingLogs
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Student)
                .Include(l => l.MeetingBooking)
                    .ThenInclude(m => m.Supervisor)
                .FirstOrDefaultAsync(l =>
                    l.Id == id && l.MeetingBooking.SupervisorId == user.Id);

            if (log == null || log.LogStatus != "Signed")
                return NotFound();

            // The PDF lives outside wwwroot: never Redirect() to App_Data.
            string? stored = log.SignedPdfLogbookPath?.Replace('\\', '/');
            const string allowedPrefix = "App_Data/MeetingLogSnapshots/";

            if (!string.IsNullOrWhiteSpace(stored) &&
                stored.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string name = stored.Substring(allowedPrefix.Length);
                if (!string.IsNullOrWhiteSpace(name) &&
                    name == System.IO.Path.GetFileName(name) &&
                    name.StartsWith($"Signed_Log_{log.MeetingBookingId}_", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    string physicalPath = System.IO.Path.Combine(
                        _env.ContentRootPath, "App_Data", "MeetingLogSnapshots", name);
                    if (System.IO.File.Exists(physicalPath))
                        return PhysicalFile(physicalPath, "application/pdf", enableRangeProcessing: true);
                }
            }

            // For legacy signed records without a signed PDF, show printable HTML.
            // This is not a substitute for the immutable PDF snapshot.
            var project = await _context.FYPProjects
                .FirstOrDefaultAsync(p => p.StudentId == log.MeetingBooking.StudentId);
            ViewBag.ProjectName = project?.Title ?? "FYP Project";
            return View("~/Views/Shared/MeetingLogPrintable.cshtml", log);
        }

        // ────────────────────────────────────────────────────────
        // Notifications
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Notifications()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
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

            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadDate = DateTime.Now;
            }

            if (unread.Any())
            {
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "All notifications marked as read.";
            return RedirectToAction(nameof(Notifications));
        }

        private string ValidateChronology(List<FYPMilestone> existingMilestones, string targetTitle, DateTime? newDueDate, string originalTitle = null)
        {
            if (!newDueDate.HasValue) return null;

            var simulated = existingMilestones.Select(m => new { Title = m.Title, DueDate = m.DueDate }).ToList();
            
            if (originalTitle != null) {
                simulated.RemoveAll(m => m.Title == originalTitle);
            } else {
                simulated.RemoveAll(m => m.Title == targetTitle);
            }
            
            simulated.Add(new { Title = targetTitle, DueDate = newDueDate });

            var iaf = simulated.FirstOrDefault(m => m.Title.Contains("IAF") || m.Title.Contains("Initial Project Allocation"));
            var proposal = simulated.FirstOrDefault(m => m.Title.Contains("Stage 1 Final Submission"));
            var final = simulated.FirstOrDefault(m => m.Title.Contains("Final"));
            var step1 = simulated.FirstOrDefault(m => m.Title.Contains("Step 1: Turnitin"));
            var step5 = simulated.FirstOrDefault(m => m.Title.Contains("Step 5"));
            
            var targetMilestone = simulated.FirstOrDefault(m => m.Title == targetTitle);
            if (targetMilestone == null || !targetMilestone.DueDate.HasValue) return null;

            bool isCore = targetTitle.Contains("IAF") || targetTitle.Contains("Initial Project Allocation") || targetTitle.Contains("Stage 1 Final Submission") || targetTitle.Contains("Final") || targetTitle.Contains("Step 5");

            if (!isCore)
            {
                bool isStage2 = targetTitle.StartsWith("Step ");
                if (!isStage2)
                {
                    // Stage 1 Custom Milestones
                    // Custom Stage 1 milestones must be AFTER IAF.
                    if (iaf?.DueDate.HasValue == true && targetMilestone.DueDate.Value.Date < iaf.DueDate.Value.Date)
                    {
                        return $"Date Validation Error: '{targetTitle}' due date cannot be before the FYP 1: Initial Assessment Form due date ({iaf.DueDate.Value:dd MMM yyyy}).";
                    }
                    if (proposal?.DueDate.HasValue == true && targetMilestone.DueDate.Value.Date > proposal.DueDate.Value.Date)
                    {
                        return $"Date Validation Error: '{targetTitle}' due date cannot be after the FYP 1: Stage 1 Final Submission due date ({proposal.DueDate.Value:dd MMM yyyy}).";
                    }
                }
                else
                {
                    // Stage 2 Custom Milestones
                    if (step1?.DueDate.HasValue == true && !targetTitle.Contains("Step 1") && targetMilestone.DueDate.Value.Date < step1.DueDate.Value.Date)
                    {
                        return $"Date Validation Error: '{targetTitle}' due date cannot be before the Step 1: Turnitin Checking due date ({step1.DueDate.Value:dd MMM yyyy}).";
                    }
                    if (step5?.DueDate.HasValue == true && targetMilestone.DueDate.Value.Date > step5.DueDate.Value.Date)
                    {
                        return $"Date Validation Error: '{targetTitle}' due date cannot be after the Step 5: Presentation Slides Submission due date ({step5.DueDate.Value:dd MMM yyyy}).";
                    }
                }
            }
            else
            {
                if (proposal?.DueDate.HasValue == true && final?.DueDate.HasValue == true && proposal.DueDate.Value.Date > final.DueDate.Value.Date)
                {
                    return $"Date Validation Error: FYP 1: Stage 1 Final Submission due date cannot be after the Step 2: Final Report Submission due date ({final.DueDate.Value:dd MMM yyyy}).";
                }
            }

            return null;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockSubmission(int id)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == id && m.Project.SupervisorId == user!.Id);

            if (milestone != null)
            {
                milestone.RejectionCount = 0;
                await _context.SaveChangesAsync();
                TempData["Success"] = "The submission has been unlocked and the rejection counter reset to 0. The student can now re-submit the form.";
            }
            return RedirectToAction(nameof(GradeSubmission), new { id = id });
        }

        // ────────────────────────────────────────────────────────
        // Reporting
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Reporting()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");
            if (!user.UserRoles.Any(r => r.Role.Name == "Supervisor")) return RedirectToAction("AccessDenied", "Home");
            ViewBag.CurrentUser = user;

            // 1. Milestone Status Distribution
            var milestones = await _context.FYPMilestones
                .Include(m => m.Project)
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign)
                .ToListAsync();

            var milestoneStatusCounts = milestones
                .GroupBy(m => m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            // 2. Student Program Distribution
            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign)
                .ToListAsync();

            var programCounts = projects
                .Where(p => !string.IsNullOrEmpty(p.Student?.Programme))
                .GroupBy(p => p.Student.Programme)
                .Select(g => new { Program = g.Key, Count = g.Count() })
                .ToList();

            // 3. Meeting Request Status
            var meetings = await _context.MeetingBookings
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == user.Id && !m.Student.IsResign)
                .ToListAsync();

            var meetingStatusCounts = meetings
                .GroupBy(m => m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            // 4. Monthly Meeting Activity (Current Year)
            var currentYear = DateTime.Today.Year;
            var monthlyMeetings = meetings
                .Where(m => m.MeetingDate.Year == currentYear)
                .GroupBy(m => m.MeetingDate.Month)
                .Select(g => new { Month = g.Key, Count = g.Count() })
                .OrderBy(x => x.Month)
                .ToList();

            // Serialize to JSON for frontend Chart.js
            ViewBag.MilestoneStatsJson = System.Text.Json.JsonSerializer.Serialize(milestoneStatusCounts);
            ViewBag.ProgramStatsJson = System.Text.Json.JsonSerializer.Serialize(programCounts);
            ViewBag.MeetingStatsJson = System.Text.Json.JsonSerializer.Serialize(meetingStatusCounts);
            ViewBag.MonthlyMeetingStatsJson = System.Text.Json.JsonSerializer.Serialize(monthlyMeetings);

            return View();
        }



        // ── Get Calendar Data API ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetMeetingsCalendarData(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Json(new object[] { });

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            var meetings = await _context.MeetingBookings
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == user.Id && (m.Status == "Approved" || m.Status == "Pending" || m.Status == "Postponed") && (activeSession == null || m.Student.AcademicSession == activeSession))
                .ToListAsync();

            var events = meetings.Select(m => {
                string startTimeStr = m.MeetingTime ?? "09:00 AM";
                DateTime startDt;
                if (!DateTime.TryParse($"{m.MeetingDate:yyyy-MM-dd} {startTimeStr}", out startDt))
                    startDt = m.MeetingDate;

                string color = m.Status == "Approved" ? "#10b981" : (m.Status == "Pending" ? "#f59e0b" : "#ef4444");

                return new {
                    id = m.Id,
                    title = $"Meeting: {m.Student?.FullName ?? "Student"}",
                    start = startDt.ToString("yyyy-MM-ddTHH:mm:00"),
                    description = $"Status: {m.Status}\nLocation: {m.Location ?? "TBA"}\nReason: {m.Reason}",
                    color = color,
                    url = Url.Action("ManageMeetings", "Supervisor")
                };
            }).ToList();

            return Json(events);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteExaminer(int projectId, int examinerId)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.AssignedExaminers)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == currentUser.Id);

            if (project == null)
            {
                TempData["Error"] = "Project not found or you are not authorized to manage it.";
                return RedirectToAction("MyProjects");
            }

            var examiner = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == examinerId && u.UserRoles.Any(r => r.Role.Name == "Examiner"));
            if (examiner == null)
            {
                TempData["Error"] = "Invalid Examiner selected.";
                return RedirectToAction("ProjectDetails", new { id = projectId });
            }

            if (!project.AssignedExaminers.Any(ae => ae.ExaminerId == examinerId))
            {
                _context.FYPProjectExaminers.Add(new FYPProjectExaminer
                {
                    FYPProjectId = project.Id,
                    ExaminerId = examinerId,
                    AssignedDate = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }

            // Send Email to Examiner
            if (!string.IsNullOrEmpty(examiner.Email))
            {
                string subject = "FYP Tracking System - New Evaluation Assignment";
                string message = $"<p>Dear {examiner.FullName},</p><br/><p>You have been assigned as the Examiner for the project <strong>{project.Title}</strong> by student <strong>{project.Student?.FullName ?? "Unknown"}</strong>.</p>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string content = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                try
                {
                    _emailService.Send(examiner.Email, subject, content);
                }
                catch (Exception) { /* Log error, continue */ }
            }

            // Create Notification
            var notification = new FYPNotification
            {
                RecipientId = examinerId,
                Title = "New Project Assignment",
                Message = $"You have been assigned as the Examiner for the project: {project.Title}.",
                Type = "ExaminerAssignment",
                RelatedRecordId = projectId,
                CreatedDate = DateTime.Now,
                IsRead = false
            };
            _context.FYPNotifications.Add(notification);
            await _context.SaveChangesAsync();

            // Push Notification via SignalR
            await _hubContext.Clients.User(examinerId.ToString())
                .SendAsync("ReceiveNotification", notification.Title, notification.Message);

            TempData["Success"] = $"Examiner {examiner.FullName} successfully assigned to this project!";
            return RedirectToAction("ProjectDetails", new { id = projectId });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTurnitinStatus(int projectId, int score)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user.Id);
            if (project == null) return NotFound();

            if (score > 20)
            {
                TempData["Error"] = $"Similarity index ({score}%) exceeds the 20% limit. You cannot approve this. Please reject the Final Submission milestone instead.";
                return RedirectToAction("ProjectDetails", new { projectId = projectId });
            }

            project.TurnitinScore = score;
            project.IsTurnitinApproved = true;
            await _context.SaveChangesAsync();

            await AddNotificationAsync(project.StudentId, "SubmissionFeedback", "Turnitin Approved", $"Your supervisor has approved your Turnitin score ({score}%).");
            await LogActivityAsync(project.StudentId, "MilestoneApproved", $"Turnitin Score Approved ({score}%)");

            TempData["Success"] = $"Turnitin score ({score}%) approved successfully! The examiner can now upload the final grade.";
            return RedirectToAction("ProjectDetails", new { projectId = projectId });
        }

        // --- Joint Evaluation Marksheet ---
        [HttpGet]
        public async Task<IActionResult> GradeMarksheet(int milestoneId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .ThenInclude(p => p.Student)
                .Include(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.SupervisorId == user.Id);

            if (milestone == null)
            {
                TempData["Error"] = "Milestone not found or unauthorized.";
                return RedirectToAction("ReviewSubmissions");
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

            if (!isAllStage1Approved && milestone.Status != "Completed")
            {
                TempData["Error"] = "Required Stage 1 milestones (IAF & Final Submission) must be approved before joint evaluation.";
                return RedirectToAction("ReviewSubmissions");
            }

            ViewBag.Role = "Supervisor";
            return View("~/Views/Shared/GradeMarksheet.cshtml", milestone);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeMarksheet(int milestoneId, string marksheetData)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.SupervisorId == user.Id);

            if (milestone == null)
            {
                TempData["Error"] = "Invalid milestone.";
                return RedirectToAction("ReviewSubmissions");
            }

            milestone.SupervisorMarksheetData = marksheetData;
            
            if (!string.IsNullOrEmpty(milestone.ExaminerMarksheetData))
            {
                milestone.Status = "Completed";
                try
                {
                    double supTotal = FYPTracker.Utils.GradingHelper.CalculateStage1Score(marksheetData, "Supervisor");
                    double exTotal = FYPTracker.Utils.GradingHelper.CalculateStage1Score(milestone.ExaminerMarksheetData, "Examiner");

                    milestone.Project.Stage1TotalScore = ((supTotal + exTotal) / 150.0) * 100.0;
                    milestone.Project.Stage1Grade = milestone.Project.Stage1TotalScore >= 40.0 ? "Pass" : "Fail";
                }
                catch { }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Your joint evaluation marks have been saved successfully.";
            return RedirectToAction("ReviewSubmissions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMarksheetStage1(int projectId, IFormFile marksheetFile, string idempotencyToken)
        {
            if (string.IsNullOrEmpty(idempotencyToken) || TempData["IdempotencyToken"]?.ToString() != idempotencyToken)
            {
                TempData["Error"] = "Duplicate submission detected. Your request has already been processed.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
            }
            TempData.Remove("IdempotencyToken");

            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Milestones)
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user.Id);
                
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(MyProjects));
            }

            if (project.Stage1MarksheetUploadCount >= 5)
            {
                TempData["Error"] = "You have reached the maximum allowed number of Stage 1 marksheet uploads (5) for this project.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
            }

            if (marksheetFile != null && marksheetFile.Length > 0)
            {
                var extension = Path.GetExtension(marksheetFile.FileName).ToLower();
                if (extension != ".xls" && extension != ".xlsx")
                {
                    TempData["Error"] = "Only Excel files (.xls, .xlsx) are allowed.";
                    return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
                }

                project.Stage1MarksheetUploadCount++;

                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "marksheets");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string uniqueFileName = Guid.NewGuid().ToString() + "_Stage1_" + Path.GetFileName(marksheetFile.FileName);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await marksheetFile.CopyToAsync(fileStream);
                }

                var proposalMilestone = project.Milestones.FirstOrDefault(m => m.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase));
                
                try
                {
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(filePath))
                    {
                        var worksheet = workbook.Worksheets.FirstOrDefault();
                        if (worksheet != null)
                        {
                            var cellValue = worksheet.Cell("J16").Value;
                            if (cellValue != null && proposalMilestone != null)
                            {
                                string extractedScore = "";
                                string stringValue = cellValue.ToString();
                                if (double.TryParse(stringValue, out double numericScore)) extractedScore = numericScore.ToString();
                                else if (stringValue.Contains("-") && double.TryParse(stringValue.Split('-').Last().Trim(), out double scoreFromText)) extractedScore = scoreFromText.ToString();
                                else extractedScore = stringValue.Trim();
                                
                                if (!double.TryParse(extractedScore, out _))
                                {
                                    TempData["Error"] = "Invalid Marksheet: Could not extract a valid numeric score.";
                                    System.IO.File.Delete(filePath);
                                    project.Stage1MarksheetUploadCount--;
                                    return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
                                }
                                
                                var strengthsCell = worksheet.Cell("C19").Value;
                                var improvementsCell = worksheet.Cell("C22").Value;
                                var commentsCell = worksheet.Cell("C25").Value;
                                
                                var feedbackData = new {
                                    Score = extractedScore,
                                    Strengths = strengthsCell?.ToString().Trim() ?? "",
                                    Improvements = improvementsCell?.ToString().Trim() ?? "",
                                    Comments = commentsCell?.ToString().Trim() ?? "",
                                    FilePath = "/uploads/marksheets/" + uniqueFileName
                                };
                                proposalMilestone.SupervisorMarksheetData = System.Text.Json.JsonSerializer.Serialize(feedbackData);
                                
                                double supScore = 0;
                                if (!string.IsNullOrEmpty(proposalMilestone.SupervisorMarksheetData)) {
                                    if (proposalMilestone.SupervisorMarksheetData.StartsWith("{")) {
                                        using var doc = System.Text.Json.JsonDocument.Parse(proposalMilestone.SupervisorMarksheetData);
                                        if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double e)) supScore = e;
                                    } else {
                                        double.TryParse(proposalMilestone.SupervisorMarksheetData, out supScore);
                                    }
                                }

                                double exScore = 0;
                                bool hasExScore = false;
                                if (!string.IsNullOrEmpty(proposalMilestone.ExaminerMarksheetData)) {
                                    if (proposalMilestone.ExaminerMarksheetData.StartsWith("{")) {
                                        using var doc = System.Text.Json.JsonDocument.Parse(proposalMilestone.ExaminerMarksheetData);
                                        if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double s)) { exScore = s; hasExScore = true; }
                                    } else {
                                        hasExScore = double.TryParse(proposalMilestone.ExaminerMarksheetData, out exScore);
                                    }
                                }

                                if (supScore > 0)
                                {
                                    if (hasExScore)
                                    {
                                        project.Stage1TotalScore = (supScore + exScore) / 2.0;
                                        project.Stage1Grade = project.Stage1TotalScore >= 40.0 ? "Pass" : "Fail";
                                    }
                                    else
                                    {
                                        project.Stage1TotalScore = supScore;
                                    }
                                }
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Stage 1 marksheet uploaded successfully" + (project.Stage1TotalScore.HasValue ? $" and score {project.Stage1TotalScore} recorded." : ".");
                }
                catch (Exception)
                {
                    TempData["Error"] = "Error processing the Excel file. Please ensure it is not corrupted and follows the template.";
                    System.IO.File.Delete(filePath);
                    project.Stage1MarksheetUploadCount--;
                }
            }
            else
            {
                TempData["Error"] = "Please select a valid file to upload.";
            }

            return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
        }

        private string GetGradeLabel(double score)
        {
            if (score >= 80) return "Excellent";
            if (score >= 60) return "Good";
            if (score >= 40) return "Average";
            if (score > 0) return "Poor";
            return "Fail";
        }

        // --- Timetable Management ---
        [HttpGet]
        public async Task<IActionResult> ManageTimetable()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true);
            var entries = new List<SupervisorTimetableEntry>();
            if (activeSession == null)
            {
                TempData["Error"] = "No active academic session found. You cannot upload a timetable without an active session.";
                ViewBag.ActiveSessionName = "None";
            }
            else
            {
                entries = await _context.SupervisorTimetableEntries
                    .Where(t => t.SupervisorId == user.Id && t.AcademicSessionId == activeSession.Id)
                    .ToListAsync();
                ViewBag.ActiveSessionName = activeSession.Name;
            }

            return View(entries);
        }

        [HttpPost]
        public async Task<IActionResult> PreviewTimetable(IFormFile timetableFile)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            if (timetableFile == null || timetableFile.Length == 0)
            {
                TempData["Error"] = "Please upload a valid CSV file.";
                return RedirectToAction(nameof(ManageTimetable));
            }

            var entries = new List<SupervisorTimetableEntry>();
            using (var reader = new StreamReader(timetableFile.OpenReadStream()))
            {
                // skip header
                if (!reader.EndOfStream) await reader.ReadLineAsync();

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var values = line.Split(',');
                    if (values.Length >= 3)
                    {
                        string NormalizeDay(string d)
                        {
                            if (int.TryParse(d, out int dayInt) && dayInt >= 1 && dayInt <= 7)
                            {
                                return ((DayOfWeek)(dayInt % 7)).ToString();
                            }
                            return d;
                        }

                        var entry = new SupervisorTimetableEntry
                        {
                            DayOfWeek = NormalizeDay(values[0].Trim()),
                            Details = values.Length > 3 ? values[3].Trim() : ""
                        };
                        
                        TimeSpan? ParseTime(string val)
                        {
                            val = val.Trim();
                            if (string.IsNullOrEmpty(val)) return null;

                            if (int.TryParse(val, out int hour) && hour >= 0 && hour <= 23)
                            {
                                return TimeSpan.FromHours(hour);
                            }

                            val = val.Replace(".", ":");

                            if (DateTime.TryParse(val, out var dt))
                            {
                                return dt.TimeOfDay;
                            }

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
                        
                        if (parsedStart.HasValue && parsedEnd.HasValue)
                        {
                            entry.StartTime = parsedStart.Value;
                            entry.EndTime = parsedEnd.Value;
                            entries.Add(entry);
                        }
                    }
                }
            }

            // Save to temp storage for preview
            TempData["TimetablePreview"] = System.Text.Json.JsonSerializer.Serialize(entries);
            return View("PreviewTimetable", entries);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTimetable()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var activeSession = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent == true);
            if (activeSession == null) 
            {
                TempData["Error"] = "No active academic session found. Cannot save timetable.";
                return RedirectToAction(nameof(ManageTimetable));
            }

            var previewJson = TempData["TimetablePreview"] as string;
            if (string.IsNullOrEmpty(previewJson))
            {
                TempData["Error"] = "Session expired. Please upload again.";
                return RedirectToAction(nameof(ManageTimetable));
            }

            var parsedEntries = System.Text.Json.JsonSerializer.Deserialize<List<SupervisorTimetableEntry>>(previewJson);
            if (parsedEntries != null && parsedEntries.Any())
            {
                // Delete old ones
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
            
            return RedirectToAction(nameof(ManageTimetable));
        }
    }
}