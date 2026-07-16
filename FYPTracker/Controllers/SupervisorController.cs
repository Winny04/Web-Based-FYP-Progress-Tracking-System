using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;

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
            var username = HttpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return null;
            return await _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
                .FirstOrDefaultAsync(u => u.UserName == username || u.Email == username);
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
            var req = await _context.SupervisorRequests.Include(r => r.Student).Include(r => r.Supervisor).FirstOrDefaultAsync(r => r.Id == requestId);
            if (req != null && req.Status == "Pending")
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

                        // Create Default Milestones
                        var milestones = new List<FYPMilestone>
                        {
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 1: Initial Assessment Form (IAF)", DueDate = DateTime.Now.AddDays(14), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 1: Proposal", DueDate = DateTime.Now.AddMonths(1), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 1: Interim Report", DueDate = DateTime.Now.AddMonths(3), Status = "Pending" },
                            new FYPMilestone { FYPProjectId = proj.Id, Title = "FYP 2: Final Dissertation", DueDate = DateTime.Now.AddMonths(6), Status = "Pending" }
                        };
                        _context.FYPMilestones.AddRange(milestones);
                    }
                    else
                    {
                        proj.SupervisorId = req.SupervisorId;
                        proj.Status = "Approved";
                        proj.AcademicSession = req.Student.AcademicSession; // Sync session just in case
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
                
                // --- NEW: SignalR Real-Time Notification ---
                await _hubContext.Clients.Group($"User_{req.StudentId}").SendAsync("ReceiveRequestStatusUpdate", req.SupervisorId, req.Status);

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
                .Include(p => p.Milestones)
                .FirstOrDefaultAsync(p => p.Id == id && p.SupervisorId == user.Id);

            if (project == null) return NotFound();

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
                await _hubContext.Clients.Group($"User_{project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "New Milestone Assigned", $"You have been assigned a new milestone: {title}");

                TempData["Success"] = "Milestone added successfully.";
            }
            return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAddMilestone(string title, string description, DateTime? dueDate, string sessionFilter)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Authentication");

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Milestone title cannot be empty.";
                return RedirectToAction(nameof(MyProjects), new { sessionFilter });
            }

            var activeSession = await GetAndSetSessionFilterAsync(user.Id, sessionFilter);

            // Check for duplicate milestone titles across this supervisor's projects in the active session
            bool isDuplicate = await _context.FYPMilestones
                .Include(m => m.Project)
                .AnyAsync(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (activeSession == null || m.Project.AcademicSession == activeSession) && m.Title.ToLower() == title.ToLower());

            if (isDuplicate)
            {
                TempData["AddModalError"] = $"A milestone with the title '{title}' already exists for your students in the current session. Please choose a different name or edit the existing one.";
                return Redirect($"/Supervisor/MyProjects?action=bulk-add");
            }

            if (dueDate.HasValue)
            {
                var firstProject = await _context.FYPProjects.FirstOrDefaultAsync(p => p.SupervisorId == user.Id && !p.Student.IsResign && (activeSession == null || p.AcademicSession == activeSession));
                if (firstProject != null)
                {
                    var existingMilestones = await _context.FYPMilestones.Where(m => m.FYPProjectId == firstProject.Id).ToListAsync();
                    string validationError = ValidateChronology(existingMilestones, title, dueDate);
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
                    Title = title,
                    Description = description,
                    DueDate = dueDate,
                    Status = "Pending",
                    CreatedDate = DateTime.Now
                }).ToList();

                _context.FYPMilestones.AddRange(milestones);
                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                foreach (var p in projects)
                {
                    await _hubContext.Clients.Group($"User_{p.StudentId}").SendAsync("ReceiveMilestoneUpdate", "New Milestone Assigned", $"You have been assigned a new milestone: {title}");
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
                    bool isSystemMilestone = isIaf || milestone.Title.Contains("Proposal") || milestone.Title.Contains("Progress") || milestone.Title.Contains("Final");

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
                        await _hubContext.Clients.Group($"User_{m.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "Milestone Updated", $"Your milestone '{targetTitle}' has been updated.");
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
                if (targetTitle.Contains("Proposal", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("Final", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("IAF", StringComparison.OrdinalIgnoreCase) || targetTitle.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase))
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
                        await _hubContext.Clients.Group($"User_{m.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "Milestone Deleted", $"Your milestone '{targetTitle}' has been deleted.");
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
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "Milestone Updated", $"Your milestone '{title}' has been updated.");
                
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
                if (milestone.Title.Contains("Proposal", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("Final", StringComparison.OrdinalIgnoreCase))
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
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "Milestone Deleted", $"Your milestone '{milestone.Title}' has been deleted.");
                
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
                .Where(m => m.Project.SupervisorId == user.Id && !m.Project.Student.IsResign && (m.Status == "Submitted" || m.Status == "Pending" || m.Status == "Approved" || m.Status == "Revision" || m.Status == "Rejected" || m.Status == "UnderReview") && (activeSession == null || m.Project.AcademicSession == activeSession))
                .OrderBy(m => m.Status == "Submitted" ? 0 : 1)
                .ThenBy(m => m.DueDate)
                .ToListAsync();

            return View(submissions);
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
        public async Task<IActionResult> ReviewSubmission(int milestoneId, string status, string? remarks, string? supervisorSignature, string? pctComplete, DateTime? vivaDate, IFormFile? gradedFile)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones.Include(m => m.Project).FirstOrDefaultAsync(m => m.Id == milestoneId);

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

                if ((milestone.Title.Contains("Final", StringComparison.OrdinalIgnoreCase) || milestone.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase)) && vivaDate.HasValue)
                {
                    milestone.Project.VivaDate = vivaDate.Value;
                }

                if (gradedFile != null && gradedFile.Length > 0)
                {
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
                }

                await _context.SaveChangesAsync();
                
                // --- SignalR Real-Time Notification ---
                string actionStr = status == "Approved" ? "graded" : "reviewed";
                await _hubContext.Clients.Group($"User_{milestone.Project.StudentId}").SendAsync("ReceiveMilestoneUpdate", "Milestone Reviewed", $"Your milestone '{milestone.Title}' was {actionStr}.");

                if (status == "Approved") {
                    await AddNotificationAsync(milestone.Project.StudentId, "SubmissionFeedback", $"{milestone.Title} Approved", remarks);
                    await LogActivityAsync(milestone.Project.StudentId, "MilestoneApproved", $"{milestone.Title} Approved");
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

            return View(meetings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondMeeting(int meetingId, string status, string? note, DateTime? proposedDate, string? newMeetingTime, string? location)
        {
            var user = await GetCurrentUserAsync();
            var meeting = await _context.MeetingBookings.FirstOrDefaultAsync(m => m.Id == meetingId && m.SupervisorId == user.Id);
            
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
                await _context.SaveChangesAsync();

                await AddNotificationAsync(meeting.StudentId, "MeetingUpdate", $"Meeting {status}", note);
                await LogActivityAsync(meeting.StudentId, "MeetingBooked", $"Meeting {status}");
                
                // --- SignalR Real-Time Notification ---
                await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", $"Meeting {status}", $"Your meeting request was {status.ToLower()}. {(string.IsNullOrEmpty(note) ? "" : "Note: " + note)}");

                TempData["Success"] = $"Meeting {status.ToLower()}.";
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
                await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", $"Meeting {status}", notifMessage);

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
            await _hubContext.Clients.Group($"User_{meeting.StudentId}").SendAsync("ReceiveMeetingUpdate", "Logbook Signed", $"Your supervisor has signed your progress logbook.");
            
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
            
            // Remove the target if it exists (for edit)
            if (originalTitle != null) {
                simulated.RemoveAll(m => m.Title == originalTitle);
            } else {
                simulated.RemoveAll(m => m.Title == targetTitle);
            }
            
            // Add the new state
            simulated.Add(new { Title = targetTitle, DueDate = newDueDate });

            var iaf = simulated.FirstOrDefault(m => m.Title.Contains("IAF") || m.Title.Contains("Initial Project Allocation"));
            var proposal = simulated.FirstOrDefault(m => m.Title.Contains("Proposal"));
            var final = simulated.FirstOrDefault(m => m.Title.Contains("Final"));
            var progressList = simulated.Where(m => 
                !(m.Title.Contains("IAF") || m.Title.Contains("Initial Project Allocation") || m.Title.Contains("Proposal") || m.Title.Contains("Final")))
                .Where(m => m.DueDate.HasValue).ToList();



            if (proposal?.DueDate.HasValue == true)
            {
                foreach (var p in progressList)
                {
                    if (p.DueDate.Value.Date < proposal.DueDate.Value.Date)
                        return $"Date Validation Error: '{p.Title}' due date cannot be before the FYP 1: Proposal due date.";
                }
            }

            if (final?.DueDate.HasValue == true)
            {
                foreach (var p in progressList)
                {
                    if (p.DueDate.Value.Date > final.DueDate.Value.Date)
                        return $"Date Validation Error: '{p.Title}' due date cannot be after the FYP 2: Final Dissertation due date.";
                }

                if (proposal?.DueDate.HasValue == true && proposal.DueDate.Value.Date > final.DueDate.Value.Date)
                {
                    return "Date Validation Error: FYP 1: Proposal due date cannot be after the FYP 2: Final Dissertation due date.";
                }
            }

            return null;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFinalGrade(int projectId, string? finalGrade)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "Account");

            var project = await _context.FYPProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.SupervisorId == user.Id);
            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction("MyProjects");
            }
            
            if (project.IsGradingLocked)
            {
                TempData["Error"] = "Grading for this session has been locked by the Administrator. You can no longer edit this grade.";
                return RedirectToAction("ProjectDetails", new { id = projectId });
            }

            project.FinalGrade = finalGrade;
            
            if (!string.IsNullOrEmpty(finalGrade))
            {
                if (finalGrade == "C-" || finalGrade == "D" || finalGrade == "F")
                {
                    project.Status = "Failed";
                    // Change the latest approved request to "Failed" so the student can apply again
                    var prevRequest = await _context.SupervisorRequests
                        .OrderByDescending(r => r.RequestedDate)
                        .FirstOrDefaultAsync(r => r.StudentId == project.StudentId && r.Status == "Approved");
                    if (prevRequest != null)
                    {
                        prevRequest.Status = "Failed";
                    }
                }
                else
                {
                    project.Status = "Completed";
                }
            }
            else
            {
                // If the grade is cleared out (which the UI might not allow, but just in case)
                project.Status = "InProgress";
            }

            await _context.SaveChangesAsync();
            await LogActivityAsync(user.Id, "Grading", $"Updated Final Grade", $"Assigned Final Grade {finalGrade ?? "Pending"} to project {project.Title}.");

            TempData["Success"] = "Final grade updated successfully!";
            return RedirectToAction("ProjectDetails", new { id = projectId });
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
    }
}


