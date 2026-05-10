using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class SupervisorController : Controller
    {
        private readonly FYPTrackerContext _context;

        public SupervisorController(FYPTrackerContext context)
        {
            _context = context;
        }

        private async Task<EmployeeInfo?> GetCurrentUserAsync()
        {
            var username = HttpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return null;
            return await _context.EmployeeInfos.FirstOrDefaultAsync(u => u.UserName == username);
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

        // ────────────────────────────────────────────────────────
        // Student Requests
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> StudentRequests()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "EmployeeInfo");
            ViewBag.CurrentUser = user;

            var requests = await _context.SupervisorRequests
                .Include(r => r.Student)
                .Where(r => r.SupervisorId == user.Id)
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
                    var proj = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == req.StudentId);
                    if (proj == null)
                    {
                        _context.FYPProjects.Add(new FYPProject
                        {
                            Title = "New FYP Project",
                            Description = "Pending student updates.",
                            StudentId = req.StudentId,
                            SupervisorId = req.SupervisorId,
                            Status = "Proposed",
                            StartDate = DateTime.Now,
                            CreatedDate = DateTime.Now
                        });
                    }
                    else
                    {
                        proj.SupervisorId = req.SupervisorId;
                    }

                    await AddNotificationAsync(req.StudentId, "SupervisorApproval", "Request Approved", $"Your request to {req.Supervisor?.FullName} was approved.");
                    await LogActivityAsync(req.StudentId, "SupervisorAssigned", "Supervisor Assigned", $"{req.Supervisor?.FullName} is your supervisor.");
                }
                else
                {
                    await AddNotificationAsync(req.StudentId, "SupervisorApproval", "Request Declined", $"Your request to {req.Supervisor?.FullName} was declined.");
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Request {status.ToLower()}.";
            }
            return RedirectToAction(nameof(StudentRequests));
        }

        // ────────────────────────────────────────────────────────
        // My Projects
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> MyProjects()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "EmployeeInfo");
            ViewBag.CurrentUser = user;

            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Milestones)
                .Where(p => p.SupervisorId == user.Id)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(projects);
        }

        // ────────────────────────────────────────────────────────
        // Review Submissions
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> ReviewSubmissions()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "EmployeeInfo");
            ViewBag.CurrentUser = user;

            var submissions = await _context.FYPMilestones
                .Include(m => m.Project).ThenInclude(p => p.Student)
                .Include(m => m.SubmissionFiles)
                .Where(m => m.Project.SupervisorId == user.Id && m.SubmissionFiles.Any())
                .OrderByDescending(m => m.SubmittedDate ?? m.CreatedDate)
                .ToListAsync();

            return View(submissions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewSubmission(int milestoneId, string status, string? remarks)
        {
            var user = await GetCurrentUserAsync();
            var milestone = await _context.FYPMilestones.Include(m => m.Project).FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone != null && milestone.Project.SupervisorId == user?.Id)
            {
                milestone.Status = status;
                milestone.Remarks = remarks;
                await _context.SaveChangesAsync();

                if (status == "Approved") {
                    await AddNotificationAsync(milestone.Project.StudentId, "SubmissionFeedback", $"{milestone.Title} Approved", remarks);
                    await LogActivityAsync(milestone.Project.StudentId, "MilestoneApproved", $"{milestone.Title} Approved");
                } 
                else if (status == "Rejected") {
                    await AddNotificationAsync(milestone.Project.StudentId, "RevisionRequest", $"{milestone.Title} Rejected", remarks);
                    await LogActivityAsync(milestone.Project.StudentId, "FeedbackReceived", $"Revision needed for {milestone.Title}");
                }

                TempData["Success"] = "Review saved.";
            }
            return RedirectToAction(nameof(ReviewSubmissions));
        }

        // ────────────────────────────────────────────────────────
        // Manage Meetings
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> ManageMeetings()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "EmployeeInfo");
            ViewBag.CurrentUser = user;

            var meetings = await _context.MeetingBookings
                .Include(m => m.Student)
                .Where(m => m.SupervisorId == user.Id)
                .OrderByDescending(m => m.CreatedDate)
                .ToListAsync();

            return View(meetings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondMeeting(int meetingId, string status, string? note, DateTime? proposedDate)
        {
            var user = await GetCurrentUserAsync();
            var meeting = await _context.MeetingBookings.FirstOrDefaultAsync(m => m.Id == meetingId && m.SupervisorId == user.Id);
            
            if (meeting != null && meeting.Status == "Pending")
            {
                meeting.Status = status;
                meeting.SupervisorNote = note;
                meeting.ProposedNewDate = proposedDate;
                meeting.RespondedDate = DateTime.Now;
                await _context.SaveChangesAsync();

                await AddNotificationAsync(meeting.StudentId, "MeetingUpdate", $"Meeting {status}", note);
                await LogActivityAsync(meeting.StudentId, "MeetingBooked", $"Meeting {status}");

                TempData["Success"] = $"Meeting {status.ToLower()}.";
            }
            return RedirectToAction(nameof(ManageMeetings));
        }

        // ────────────────────────────────────────────────────────
        // Notifications
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Notifications()
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return RedirectToAction("Login", "EmployeeInfo");
            ViewBag.CurrentUser = user;

            var notifications = await _context.FYPNotifications
                .Where(n => n.RecipientId == user.Id)
                .OrderByDescending(n => n.CreatedDate)
                .ToListAsync();

            return View(notifications);
        }
    }
}
