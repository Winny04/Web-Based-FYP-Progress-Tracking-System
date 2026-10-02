using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;
using ClosedXML.Excel;

namespace FYPTracker.Controllers
{
    [Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Supervisor")]
    public class SupervisorController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public SupervisorController(FYPTrackerContext context, IEmailService emailService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _emailService = emailService;
            _hubContext = hubContext;
        }

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
            var sessions = await _context.FYPProjects
                .Where(p => p.SupervisorId == supervisorId && !string.IsNullOrEmpty(p.AcademicSession))
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "Authentication");
            ViewBag.CurrentUser = user;

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
                            && m.RejectionCount >= 3 
                            && m.DefenseDate.HasValue
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
                    // Ignore Completed or Failed projects so we create a new one for a restarting student
                    var proj = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == req.StudentId && p.Status != "Failed" && p.Status != "Completed");
                    
                    // Update student's academic session based on their request
                    if (!string.IsNullOrWhiteSpace(req.SessionSemester))
                    {
                        req.Student.AcademicSession = req.SessionSemester;
                    }

                    if (proj == null)
                    {
                        proj = new FYPProject
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

                        // Create Default Milestones
                        var milestones = new List<FYPMilestone>
                        {
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 1: Initial Assessment Form (IAF)", DueDate = DateTime.Now.AddDays(14), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Progress Report: Chapter 1", DueDate = DateTime.Now.AddDays(21), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Progress Report: Chapter 2", DueDate = DateTime.Now.AddDays(28), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Progress Report: Chapter 3", DueDate = DateTime.Now.AddDays(35), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Submission of Ethic Form", DueDate = DateTime.Now.AddMonths(1), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Turnitin Checking - DPP", DueDate = DateTime.Now.AddMonths(1), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 1: Stage 1 Final Submission (DPP)", DueDate = DateTime.Now.AddMonths(1), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 1: Turnitin Checking", DueDate = DateTime.Now.AddMonths(5), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 2: Final Report Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 3: Marksheet Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 4: Record of Supervisor Meeting Form Submission", DueDate = DateTime.Now.AddMonths(5), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "Step 5: Presentation Slides Submission", DueDate = DateTime.Now.AddMonths(6), Status = "Pending" }
                        };
                        _context.FYPMilestones.AddRange(milestones);
                    }
                    else
                    {
                        string oldStatus = proj.Status;
                        proj.SupervisorId = req.SupervisorId;
                        proj.Status = "Approved";
                        proj.AcademicSession = req.Student.AcademicSession; // Sync session just in case

                        if (oldStatus != "Approved")
                        {
                            _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                            {
                                ProjectId = proj.Id,
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
                    string body = $"<p>Dear {req.Student.FullName},</p><p>Your request to be supervised by {req.Supervisor?.FullName} has been <strong>{status}</strong>.</p>";
                    if (!string.IsNullOrEmpty(responseNote))
                    {
                        body += $"<p><strong>Supervisor's Note:</strong> {responseNote}</p>";
                    }
                    body += "<p>Please log in to the FYP Tracking System for more details.</p>";
                    
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.AssignedExaminers).ThenInclude(ae => ae.Examiner)
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.Id == id && p.SupervisorId == user.Id);

            if (project == null) return NotFound();

            ViewBag.AvailableExaminers = await _context.ApplicationUsers
                .Include(u => u.EmpDepartment)
                .Where(u => u.UserType == "Examiner" && !u.IsResign)
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
                    Status = "Pending",
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAddMilestone(string title, string description, DateTime? dueDate, string sessionFilter, string stageSelection = "1")
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

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
                    Status = "Pending",
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
            if (user == null) return RedirectToAction("Login", "Authentication");

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
                    bool isSystemMilestone = isIaf || milestone.Title.Contains("Stage 1 Final Submission") || milestone.Title.Contains("Progress") || milestone.Title.Contains("Final");

                    if (isIaf && newDueDate.HasValue)
                    {
                        // Do not allow updating IAF due date via bulk
                    }
                    else if (newDueDate.HasValue) 
                    {
                        milestone.DueDate = newDueDate.Value;
                    }

                    if (milestone.Status != "Pending")
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
            if (user == null) return RedirectToAction("Login", "Authentication");

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
                if (targetTitle.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("Final", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("IAF", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "Core system milestones (IAF, Proposal, and Final Report) cannot be deleted.";
                    return RedirectToAction(nameof(MyProjects));
                }

                var toDelete = milestones.Where(m => m.Status == "Pending").ToList();
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
        public async Task<IActionResult> ViewFile(int fileId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");

            var file = await _context.FYPSubmissionFiles.FindAsync(fileId);
            if (file == null || string.IsNullOrEmpty(file.FilePath)) return NotFound();

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

        [HttpGet]
        public async Task<IActionResult> DownloadMarksheet(int projectId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

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

        public async Task<IActionResult> ReviewSubmissions(string? sessionFilter = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "Authentication");
            ViewBag.CurrentUser = user;

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            // Fetch pending milestones for all projects supervised by this user
            var submissions = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Feedbacks)
                .Include(m => m.SubmissionFiles)
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (m.Status == "Submitted" || m.Status == "Pending" || m.Status == "Approved" || m.Status == "Completed" || m.Status == "Revision" || m.Status == "Rejected" || m.Status == "UnderReview") && (activeSession == null || m.Project.AcademicSession == activeSession))
                .OrderBy(m => m.Status == "Submitted" ? 0 : 1)
                .ThenBy(m => m.DueDate)
                .ToListAsync();

            return View(submissions);
        }

        [HttpGet]
        public async Task<IActionResult> GradeMarksheetStage2(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");

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
                    using var supDoc = System.Text.Json.JsonDocument.Parse(marksheetData);
                    using var exDoc = System.Text.Json.JsonDocument.Parse(project.Stage2ExaminerMarksheetData);
                    
                    if (supDoc.RootElement.TryGetProperty("Total", out var supTotalProp) && exDoc.RootElement.TryGetProperty("Total", out var exTotalProp))
                    {
                        double supTotal = 0;
                        if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(supTotalProp.GetString(), out double s1)) { supTotal = s1; }
                        else if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { supTotal = supTotalProp.GetDouble(); }

                        double exTotal = 0;
                        if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(exTotalProp.GetString(), out double e1)) { exTotal = e1; }
                        else if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { exTotal = exTotalProp.GetDouble(); }

                        // Using the stakeholder formula: examiner component = 70%; supervisor component = 80%; combined value = 150%, then divide by 100% (1.5) to obtain the total mark.
                        double finalScore = (exTotal + supTotal) / 2.0;
                        
                        project.FinalGrade = GetGradeLabel(finalScore);
                        TempData["Success"] = $"Stage 2 grading complete! Final joint score calculated as {finalScore:F1}%.";
                    }
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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

            return View(milestone);
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
                milestone.Status = status;
                milestone.SupervisorFeedback = remarks;

                if ((milestone.Title.Contains("Initial Project Allocation") || milestone.Title.Contains("IAF")) && !string.IsNullOrEmpty(supervisorSignature))
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
                                dict["Supervisor Signature"] = supervisorSignature;
                                var updatedJson = System.Text.Json.JsonSerializer.Serialize(dict);
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
                    string body = $"<p>Dear {milestone.Project.Student.FullName},</p><p>Your milestone '<strong>{milestone.Title}</strong>' has been reviewed by your supervisor and marked as <strong>{status}</strong>.</p>";
                    if (!string.IsNullOrEmpty(remarks))
                    {
                        body += $"<p><strong>Feedback:</strong> {remarks}</p>";
                    }
                    body += "<p>Please log in to the FYP Tracking System to view any graded files or Turnitin scores.</p>";
                    
                    try { _emailService.Send(milestone.Project.Student.Email, subject, body); } catch {}
                }

                // --- SignalR Real-Time Notification ---
                string actionStr = status == "Approved" ? "graded" : "reviewed";
                var reviewPayload = new { Type = "ReviewMilestone", MilestoneId = milestone.Id, Title = milestone.Title, Status = status, Message = $"Your milestone '{milestone.Title}' was {actionStr}." };
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", reviewPayload);

                if (status == "Approved") {
                    await AddNotificationAsync(milestone.Project.StudentId, "SubmissionFeedback", $"{milestone.Title} Approved", remarks);
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
                    await AddNotificationAsync(milestone.Project.StudentId, "RevisionRequest", $"{milestone.Title} Rejected", remarks);
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");

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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");

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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
                meeting.Status = status;
                meeting.SupervisorNote = note;
                meeting.ProposedNewDate = proposedDate;
                if (!string.IsNullOrEmpty(newMeetingTime))
                {
                    meeting.MeetingTime = newMeetingTime;
                }
                meeting.Location = location;
                meeting.RespondedDate = DateTime.Now;
                
                if (status == "Approved" && location == "Online")
                {
                    // Automatically generate a Jitsi Meet link instead of Microsoft Teams
                    string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string safeStudentName = meeting.Student?.FullName != null ? new string(meeting.Student.FullName.Where(char.IsLetterOrDigit).ToArray()) : "Student";
                    meeting.MeetingLink = $"https://meet.jit.si/FYP_Meeting_{safeStudentName}_{uniqueId}";
                }

                await _context.SaveChangesAsync();
                if (status == "Approved" && recurringWeeks > 1)
                {
                    // Check if it's the first meeting by looking at how many approved meetings exist before we auto-generate
                    var existingApprovedCount = await _context.MeetingBookings
                        .CountAsync(m => m.StudentId == meeting.StudentId && m.Status == "Approved");

                    if (existingApprovedCount <= 1) // Only this current one is approved
                    {
                        var newMeetings = new List<MeetingBooking>();
                        for (int i = 1; i < recurringWeeks; i++)
                        {
                            var nextDate = meeting.MeetingDate.AddDays(7 * i);

                            // 1. Check if supervisor is available on this specific date
                            var availabilities = await _context.SupervisorAvailabilities.Where(a => a.SupervisorId == user.Id).ToListAsync();
                            var overrideAvail = availabilities.FirstOrDefault(a => a.ScheduleType == "DateOverride" && a.SpecificDate?.Date == nextDate.Date);
                            var weeklyAvail = overrideAvail == null ? availabilities.FirstOrDefault(a => a.ScheduleType == "WeeklyDefault" && a.DayOfWeek == nextDate.DayOfWeek.ToString()) : null;
                            var activeAvail = overrideAvail ?? weeklyAvail;

                            if (activeAvail == null || !activeAvail.IsAvailable) continue; // Skip this week due to unavailability

                            // 2. Check for overlapping meeting on this date/time
                            bool timeConflict = await _context.MeetingBookings.AnyAsync(m => m.SupervisorId == user.Id && m.MeetingDate.Date == nextDate.Date && m.MeetingTime == meeting.MeetingTime && m.Status != "Rejected" && m.Status != "Cancelled");
                            if (timeConflict) continue; // Skip this week due to conflict

                            newMeetings.Add(new MeetingBooking
                            {
                                StudentId = meeting.StudentId,
                                SupervisorId = meeting.SupervisorId,
                                MeetingDate = nextDate,
                                MeetingDay = nextDate.DayOfWeek.ToString(),
                                MeetingTime = meeting.MeetingTime,
                                Reason = "Auto-generated weekly recurring meeting.",
                                Status = "Approved",
                                Location = location,
                                CreatedDate = DateTime.Now,
                                RespondedDate = DateTime.Now
                            });
                        }
                        if (newMeetings.Any())
                        {
                            _context.MeetingBookings.AddRange(newMeetings);
                            await _context.SaveChangesAsync();
                        }
                    }
                }

                // Send email notification to student
                if (meeting.Student != null && !string.IsNullOrEmpty(meeting.Student.Email))
                {
                    string subject = $"FYP Meeting {status}";
                    string body = $@"
                        <div style='font-family: Arial, sans-serif;'>
                            <h3>FYP Meeting Update</h3>
                            <p>Your supervisor <strong>{user?.FullName}</strong> has <strong>{status.ToLower()}</strong> your meeting request.</p>
                            <ul>
                                <li><strong>Date:</strong> {meeting.MeetingDate:dd MMM yyyy}</li>
                                <li><strong>Time:</strong> {meeting.MeetingTime}</li>
                                {(status == "Approved" && !string.IsNullOrEmpty(location) ? $"<li><strong>Location:</strong> {location}</li>" : "")}
                                {(status == "Approved" && recurringWeeks > 1 ? $"<li><strong>Note:</strong> Auto-generated for the next {recurringWeeks - 1} weeks.</li>" : "")}
                            </ul>
                            {(string.IsNullOrEmpty(note) ? "" : $"<p><strong>Message from Supervisor:</strong> {note}</p>")}
                            <p>Please log in to the FYP Tracker system for more details.</p>
                        </div>";
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

                TempData["Success"] = $"Meeting {status.ToLower()}." + (recurringWeeks > 1 && status == "Approved" ? $" Auto-generated for {recurringWeeks} weeks." : "");
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

                await _context.SaveChangesAsync();

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewMeetingLogSupervisor(
            int meetingId, 
            IFormFile? signedLogbookPdf)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            var meeting = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .FirstOrDefaultAsync(m => m.Id == meetingId && m.SupervisorId == user.Id);

            if (meeting == null || meeting.MeetingLog == null)
            {
                TempData["Error"] = "Progress logbook not found.";
                return RedirectToAction(nameof(ManageMeetings));
            }

            meeting.MeetingLog.LogStatus = "Signed";
            meeting.MeetingLog.SignatureDate = DateTime.Now;

            if (signedLogbookPdf != null && signedLogbookPdf.Length > 0)
            {
                var extension = Path.GetExtension(signedLogbookPdf.FileName).ToLower();
                if (extension != ".pdf")
                {
                    TempData["Error"] = "Only PDF files are allowed for the signed logbook.";
                    return RedirectToAction(nameof(ManageMeetings));
                }
                
                string uniqueFileName = $"logbook_signed_student_{meeting.StudentId}_meeting_{meeting.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "logbooks");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await signedLogbookPdf.CopyToAsync(fileStream);
                }

                meeting.MeetingLog.SignedPdfLogbookPath = $"/uploads/logbooks/{uniqueFileName}";
            }

            await _context.SaveChangesAsync();
            await AddNotificationAsync(meeting.StudentId, "MeetingLogSigned", "Progress Logbook Signed", $"Your supervisor has signed the progress logbook for the meeting on {meeting.MeetingDate:dd MMM yyyy}.");
            
            // --- SignalR Real-Time Notification ---
            var logbookSignedPayload = new {
                Type = "MeetingLogSigned",
                MeetingId = meeting.Id,
                Title = "Logbook Signed",
                Message = $"Your supervisor has signed your progress logbook."
            };
            await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", logbookSignedPayload);
            
            TempData["Success"] = "Progress logbook signed and approved successfully!";
            return RedirectToAction(nameof(ManageMeetings));
        }

        [HttpGet]
        public async Task<IActionResult> ViewMeetingLog(int id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");

            var log = await _context.MeetingLogs
                .Include(l => l.MeetingBooking)
                .ThenInclude(mb => mb.Student)
                .Include(l => l.MeetingBooking.Supervisor)
                .FirstOrDefaultAsync(l => l.Id == id && l.MeetingBooking.SupervisorId == user.Id);

            if (log == null || log.LogStatus != "Signed") return NotFound();
            
            if (!string.IsNullOrEmpty(log.SignedPdfLogbookPath))
                return Redirect(log.SignedPdfLogbookPath);
            if (!string.IsNullOrEmpty(log.PdfLogbookPath))
                return Redirect(log.PdfLogbookPath);
            
            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == log.MeetingBooking.StudentId);
            ViewBag.ProjectName = project?.Title ?? "FYP Project";

            return View("~/Views/Shared/MeetingLogPrintable.cshtml", log);
        }

        // ────────────────────────────────────────────────────────
        // Notifications
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Notifications()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "User");
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

            var examiner = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == examinerId && u.UserType == "Examiner");
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
                string content = $"<p>Dear {examiner.FullName},</p><p>You have been assigned as the Examiner for the project <strong>{project.Title}</strong> by student <strong>{project.Student?.FullName ?? "Unknown"}</strong>.</p><p>Please log in to the FYP Tracking System to view details and evaluate the project.</p>";
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
            if (user == null) return RedirectToAction("Login", "Authentication");

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

            var stage1Milestones = await _context.FYPMilestones
                .Where(m => m.FYPProjectId == milestone.FYPProjectId && !m.Title.StartsWith("Step"))
                .ToListAsync();
            bool isAllStage1Approved = stage1Milestones.Count >= 2 && stage1Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!isAllStage1Approved && milestone.Status != "Completed")
            {
                TempData["Error"] = "Both Stage 1 milestones must be approved before joint evaluation.";
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
            if (user == null) return RedirectToAction("Login", "Authentication");

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
                    using var supDoc = System.Text.Json.JsonDocument.Parse(marksheetData);
                    using var exDoc = System.Text.Json.JsonDocument.Parse(milestone.ExaminerMarksheetData);
                    
                    if (supDoc.RootElement.TryGetProperty("Total", out var supTotalProp) && exDoc.RootElement.TryGetProperty("Total", out var exTotalProp))
                    {
                        double supTotal = 0;
                        if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(supTotalProp.GetString(), out double s)) { supTotal = s; }
                        else if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { supTotal = supTotalProp.GetDouble(); }

                        double exTotal = 0;
                        if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(exTotalProp.GetString(), out double e)) { exTotal = e; }
                        else if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { exTotal = exTotalProp.GetDouble(); }
                        milestone.Project.Stage1TotalScore = (supTotal + exTotal) / 2.0;
                        milestone.Project.Stage1Grade = milestone.Project.Stage1TotalScore >= 40.0 ? "Pass" : "Fail";
                    }
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
    }
}



