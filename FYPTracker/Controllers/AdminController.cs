using FYPTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Linq;
using FYPTracker.ViewModels;
using FYPTracker.Services.Others.Interfaces;
using FYPTracker.Configuration.Auhtorization;
using FYPTracker.Helpers;
using ClosedXML.Excel;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using System;
using System.Collections.Generic;
using FYPTracker.Common;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;


namespace FYPTracker.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Admin", "SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public AdminController(FYPTrackerContext context, IEmailService emailService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _emailService = emailService;
            _hubContext = hubContext;
        }
        
        private void RemoveImage(ApplicationUser model)
        {
            if (!string.IsNullOrEmpty(model.Image))
            {
                var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model.Image.TrimStart('/'));
                if (System.IO.File.Exists(imagePath))
                {
                    System.IO.File.Delete(imagePath);
                }
            }
        }

        private ApplicationUser UploadImageToDir(IFormFile picture)
        {
            if (picture != null && picture.Length > 0)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(picture.FileName);
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "media", "avatars");
                Directory.CreateDirectory(uploadsFolder);
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    picture.CopyTo(fileStream);
                }
                return new ApplicationUser { Image = $"/assets/media/avatars/{fileName}" };
            }
            return new ApplicationUser { Image = "/assets/media/avatars/blank.png" };
        }

        /// <summary>
        /// Calculates account expiry from a session string like "2026 Aug" → 2027-01-31.
        /// Jan → Jul 31 same year; April → Oct 31 same year; Aug → Jan 31 next year.
        /// </summary>
        private static DateTime? CalculateAccountExpiry(string? academicSession)
        {
            if (string.IsNullOrWhiteSpace(academicSession)) return null;
            var parts = academicSession.Trim().Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int year)) return null;
            string intake = parts[1].ToLower();
            return intake switch
            {
                "jan"   => new DateTime(year, 7, 31),
                "april" => new DateTime(year, 10, 31),
                "aug"   => new DateTime(year + 1, 1, 31),
                _       => null
            };
        }

        /// <summary>Generates a random 8-character alphanumeric password.</summary>
        private static string GenerateTempPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
            var rng = new Random();
            return new string(Enumerable.Range(0, 8).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
        }


        public async Task<IActionResult> Index()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;
            
            // 1. Calculate Stats
            var users = await _context.ApplicationUsers.Include(u => u.UserRoles).ThenInclude(r => r.Role).ToListAsync();
            ViewBag.TotalUsers = users.Count;
            ViewBag.TotalStudents = users.Count(u => u.UserRoles.Any(r => r.Role.Name == "Student"));
            ViewBag.TotalSupervisors = users.Count(u => u.UserRoles.Any(r => r.Role.Name == "Supervisor"));

            var projects = await _context.FYPProjects.Include(p => p.Milestones).ToListAsync();
            ViewBag.TotalProjects = projects.Count;
            
            ViewBag.PendingRequests = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");

            // Completion Rate: % of projects where all 3 milestones are "Approved"
            if (projects.Any())
            {
                var completed = projects.Count(p => p.Milestones.All(m => m.Status == "Approved") && p.Milestones.Count >= 3);
                ViewBag.CompletionRate = (int)((double)completed / projects.Count * 100);
            }
            else
            {
                ViewBag.CompletionRate = 0;
            }

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> RespondRepeatRequest(int studentId, string status)
        {
            int.TryParse(HttpContext.User.FindFirst("Id")?.Value, out int adminUserId);
            var student = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == studentId);
            if (student != null)
            {
                if (status == "Approved")
                {
                    var currentProject = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == studentId && p.Status != "Archived");
                    if (currentProject != null) {
                        string oldStatus = currentProject.Status;
                        currentProject.Status = "Archived";
                        _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                        {
                            ProjectId = currentProject.Id,
                            OldStatus = oldStatus,
                            NewStatus = "Archived",
                            ChangedById = adminUserId > 0 ? adminUserId : null,
                            Remarks = "Project archived due to repeat request approval."
                        });
                    }
                    var currentRequests = await _context.SupervisorRequests.Where(r => r.StudentId == studentId && r.Status == "Pending").ToListAsync();
                    foreach (var r in currentRequests) {
                        r.Status = "Archived";
                    }
                }
                await _context.SaveChangesAsync();
                TempData["Success"] = "Repeat request processed.";
            }
            return RedirectToAction(nameof(ManageUsers));
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ArchiveAcademicSession(string sessionName)
        {
            int.TryParse(HttpContext.User.FindFirst("Id")?.Value, out int adminUserId);
            if (string.IsNullOrWhiteSpace(sessionName)) {
                TempData["Error"] = "Session name cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            var projects = await _context.FYPProjects.Include(p => p.Student).Where(p => p.Student.AcademicSession == sessionName && p.Status != "Archived").ToListAsync();
            foreach (var p in projects)
            {
                string oldStatus = p.Status;
                p.Status = "Archived";
                _context.FYPProjectStatusHistories.Add(new FYPProjectStatusHistory
                {
                    ProjectId = p.Id,
                    OldStatus = oldStatus,
                    NewStatus = "Archived",
                    ChangedById = adminUserId > 0 ? adminUserId : null,
                    Remarks = $"Archived via batch academic session archiving ({sessionName})."
                });
            }

            var requests = await _context.SupervisorRequests.Include(r => r.Student).Where(r => r.Student.AcademicSession == sessionName && r.Status == "Pending").ToListAsync();
            foreach (var r in requests)
            {
                r.Status = "Archived";
            }

            await _context.SaveChangesAsync();
            
            TempData["Success"] = $"Successfully archived session {sessionName}. {projects.Count} projects archived.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> ExportSummary()
        {
            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                .ToListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("ProjectID,Student,Matric,Supervisor,MilestonesCompleted,Status");

            foreach (var p in projects)
            {
                int completedCount = p.Milestones.Count(m => m.Status == "Approved");
                csv.AppendLine($"{p.Id},{p.Student?.FullName},{p.Student?.EmployeeId},{p.Supervisor?.FullName},{completedCount}/{p.Milestones.Count},{p.Status}");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(csv.ToString());
            return File(buffer, "text/csv", $"FYP_Summary_Report_{DateTime.Now:yyyyMMdd}.csv");
        }
            [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ManageUsers()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            // System-wide stats for Admin
            ViewBag.TotalStudents    = await _context.ApplicationUsers.CountAsync(u => u.UserRoles.Any(r => r.Role.Name == "Student"));
            ViewBag.TotalSupervisors = await _context.ApplicationUsers.CountAsync(u => u.UserRoles.Any(r => r.Role.Name == "Supervisor"));
            ViewBag.TotalExaminers   = await _context.ApplicationUsers.CountAsync(u => u.UserRoles.Any(r => r.Role.Name == "Examiner"));
            ViewBag.TotalProjects    = await _context.FYPProjects.CountAsync();
            ViewBag.PendingRequests  = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");
            ViewBag.Supervisors = await _context.ApplicationUsers
                .Where(u => (u.UserRoles.Any(r => r.Role.Name == "Supervisor") || u.UserRoles.Any(r => r.Role.Name == "SUPERVISOR")) && !u.IsResign)
                .Select(u => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = u.Id.ToString(), Text = u.FullName })
                .ToListAsync();

            ViewBag.AcademicSessions = await _context.AcademicSessions
                .Where(x => x.IsActive)
                .Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Name, Text = x.Name })
                .ToListAsync();

            return View(new ApplicationUserVM());
        }

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ManageProjects()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var students = await _context.ApplicationUsers
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Student"))
                .ToListAsync();

            var projects = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .Include(p => p.AssignedExaminers)
                    .ThenInclude(ae => ae.Examiner)
                .ToListAsync();

            ViewBag.AvailableExaminers = await _context.ApplicationUsers
                .Include(u => u.EmpDepartment)
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Examiner") && !u.IsResign)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var viewModels = students.Select(s => new FYPTracker.ViewModels.ManageProjectItemVM
            {
                Student = s,
                Project = projects.FirstOrDefault(p => p.StudentId == s.Id)
            })
            .OrderByDescending(v => v.Project?.CreatedDate ?? DateTime.MinValue)
            .ToList();

            return View(viewModels);
        }

        // ── Admin: View project details & schedule Viva ──────────────
        public async Task<IActionResult> ProjectDetails(int id)
        {
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                .Include(p => p.AssignedExaminers).ThenInclude(ae => ae.Examiner)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null) return NotFound();
            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetVivaDate(int projectId, DateTime vivaDate, string? vivaVenue)
        {
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.AssignedExaminers).ThenInclude(ae => ae.Examiner)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
            {
                TempData["Error"] = "Project not found.";
                return RedirectToAction(nameof(ManageProjects));
            }

            if (project.AssignedExaminers == null || !project.AssignedExaminers.Any())
            {
                TempData["Error"] = "Cannot set Viva date. Please assign an Examiner first.";
                return RedirectToAction(nameof(ManageProjects));
            }

            project.VivaDate = vivaDate;
            project.VivaVenue = vivaVenue;
            await _context.SaveChangesAsync();

            string formattedDate = vivaDate.ToString("dd MMMM yyyy, hh:mm tt");
            string venueDisplay = string.IsNullOrWhiteSpace(vivaVenue) ? "TBA" : vivaVenue;

            // ── In-app notification: Student ─────────────────────────
            if (project.StudentId > 0)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = project.StudentId,
                    Type = "VivaScheduled",
                    Title = "Viva Scheduled",
                    Message = $"Your Viva Oral Examination has been scheduled for {formattedDate} at {venueDisplay}.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });
            }

            // ── In-app notification: Supervisor ──────────────────────
            if (project.SupervisorId.HasValue)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = project.SupervisorId.Value,
                    Type = "VivaScheduled",
                    Title = "Viva Scheduled",
                    Message = $"Viva for {project.Student?.FullName} has been scheduled for {formattedDate} at {venueDisplay}.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });
            }

            // ── In-app notification: Examiner ────────────────────────
            var examiner = project.AssignedExaminers?.FirstOrDefault()?.Examiner;
            if (examiner != null)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = examiner.Id,
                    Type = "VivaScheduled",
                    Title = "Viva Scheduled",
                    Message = $"Viva for {project.Student?.FullName} has been scheduled for {formattedDate} at {venueDisplay}.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            // ── Email: Student ───────────────────────────────────────
            if (project.Student != null && !string.IsNullOrEmpty(project.Student.Email))
            {
                string subject = "FYP Viva Oral Examination Scheduled";
                string message = $@"<p>Dear <strong>{project.Student.FullName}</strong>,</p><br/>
                    <p>Your Final Year Project Viva Oral Examination has been scheduled by the Admin.</p>
                    <ul>
                        <li><strong>Date &amp; Time:</strong> {formattedDate}</li>
                        <li><strong>Venue:</strong> {venueDisplay}</li>
                        <li><strong>Project Title:</strong> {project.Title}</li>
                    </ul>
                    <p>Please ensure you are fully prepared.</p>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                try { _emailService.Send(project.Student.Email, subject, body); } catch { }
            }

            // ── Email: Supervisor ────────────────────────────────────
            if (project.Supervisor != null && !string.IsNullOrEmpty(project.Supervisor.Email))
            {
                string subject = "FYP Viva Scheduled – Student Notification";
                string message = $@"<p>Dear <strong>{project.Supervisor.FullName}</strong>,</p><br/>
                    <p>The Admin has scheduled the Viva Oral Examination for your student.</p>
                    <ul>
                        <li><strong>Student:</strong> {project.Student?.FullName} ({project.Student?.EmployeeId})</li>
                        <li><strong>Project Title:</strong> {project.Title}</li>
                        <li><strong>Date &amp; Time:</strong> {formattedDate}</li>
                        <li><strong>Venue:</strong> {venueDisplay}</li>
                    </ul>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                try { _emailService.Send(project.Supervisor.Email, subject, body); } catch { }
            }

            // ── Email: Examiner ──────────────────────────────────────
            if (examiner != null && !string.IsNullOrEmpty(examiner.Email))
            {
                string subject = "FYP Viva Scheduled – Examiner Notification";
                string message = $@"<p>Dear <strong>{examiner.FullName}</strong>,</p><br/>
                    <p>The Admin has scheduled the Viva Oral Examination for a student you are examining.</p>
                    <ul>
                        <li><strong>Student:</strong> {project.Student?.FullName} ({project.Student?.EmployeeId})</li>
                        <li><strong>Project Title:</strong> {project.Title}</li>
                        <li><strong>Date &amp; Time:</strong> {formattedDate}</li>
                        <li><strong>Venue:</strong> {venueDisplay}</li>
                    </ul>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string body = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                try { _emailService.Send(examiner.Email, subject, body); } catch { }
            }

            // ── SignalR real-time push to Student ────────────────────
            var payload = new { Type = "VivaScheduled", Message = $"Your Viva has been scheduled for {formattedDate} at {venueDisplay}." };
            await _hubContext.Clients.Group($"User_{project.StudentId}").SendAsync("ReceiveMilestoneUpdate", payload);

            TempData["Success"] = $"Viva date set to {formattedDate}. The student, supervisor, and examiner have been notified.";
            
            string referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && referer.Contains("ManageProjects", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(ManageProjects));
            }
            
            return RedirectToAction(nameof(ProjectDetails), new { id = projectId });
        }

        [HttpPost]
        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignExaminer(int projectId, int examinerId)
        {
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.AssignedExaminers)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
            {
                TempData["Error"] = "Project not found.";
                return RedirectToAction(nameof(ManageProjects));
            }

            var examiner = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == examinerId && u.UserRoles.Any(r => r.Role.Name == "Examiner") && !u.IsResign);

            if (examiner == null)
            {
                TempData["Error"] = "Invalid or inactive Examiner selected.";
                return RedirectToAction(nameof(ManageProjects));
            }

            var existingAssignments = await _context.FYPProjectExaminers
                .Where(pe => pe.FYPProjectId == projectId)
                .ToListAsync();

            if (existingAssignments.Any())
            {
                _context.FYPProjectExaminers.RemoveRange(existingAssignments);
            }

            _context.FYPProjectExaminers.Add(new FYPProjectExaminer
            {
                FYPProjectId = project.Id,
                ExaminerId = examiner.Id,
                AssignedDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            try
            {
                var notification = new FYPNotification
                {
                    RecipientId = examiner.Id,
                    Title = "New Project Assignment",
                    Message = $"Admin has assigned you as the Examiner for project: '{project.Title}'.",
                    Type = "ExaminerAssignment",
                    RelatedRecordId = project.Id,
                    CreatedDate = DateTime.Now,
                    IsRead = false
                };
                _context.FYPNotifications.Add(notification);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(examiner.Email))
                {
                    string subject = "FYP Tracking System - Examiner Assignment";
                    string message = $"<p>Dear {examiner.FullName},</p><br/><p>You have been assigned by the Administrator as the Examiner for project: <strong>{project.Title}</strong> (Student: <strong>{project.Student?.FullName ?? "Unknown"}</strong>).</p>";
                    var request = HttpContext.Request;
                    string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                    string content = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                    _emailService.Send(examiner.Email, subject, content);
                }
            }
            catch { /* Continue on notification error */ }

            TempData["Success"] = $"Successfully assigned {examiner.FullName} as Examiner for '{project.Title}'.";
            return RedirectToAction(nameof(ManageProjects));
        }

        // ============================================================
        // AI AUTO-ASSIGN EXAMINER (Rule-Based Keyword Matching Engine)
        // ============================================================
        [HttpPost]
        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoAssignExaminer(int projectId)
        {
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.AssignedExaminers)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
            {
                TempData["Error"] = "Project not found.";
                return RedirectToAction(nameof(ManageProjects));
            }

            // Build a searchable corpus from the project
            var projectCorpus = string.Join(" ",
                project.Title ?? "",
                project.Description ?? "",
                project.ProblemStatement ?? "",
                project.Scope ?? "",
                project.Objective1 ?? "",
                project.Objective2 ?? "",
                project.Objective3 ?? "",
                project.Student?.Specialization ?? ""
            ).ToLowerInvariant();

            // Get all active examiners with their specialisation
            var examiners = await _context.ApplicationUsers
                .Where(u =>
                    u.UserRoles.Any(r => r.Role.Name == "Examiner") &&
                    !u.IsResign)
                .ToListAsync();

            // Examiner assignment count for each lecturer
            var examinerCounts = await _context.FYPProjectExaminers
                .GroupBy(x => x.ExaminerId)
                .Select(g => new
                {
                    ExaminerId = g.Key,
                    Count = g.Count()
                })
                .ToDictionaryAsync(x => x.ExaminerId, x => x.Count);

            // Eligible:
            // 1. Cannot examine own supervised student
            // 2. Must still have examiner quota
            examiners = examiners
                .Where(e =>
                    e.Id != project.SupervisorId &&
                    examinerCounts.GetValueOrDefault(e.Id, 0) < e.MaxStudentSlots)
                .ToList();

            if (!examiners.Any())
            {
                TempData["Error"] = "No examiner is available. All eligible examiners may have reached their quota.";
                return RedirectToAction(nameof(ManageProjects));
            }

            // Score each examiner by keyword overlap
            var scored = examiners.Select(e =>
            {
                var examinerKeywords = string.Join(" ",
                    e.Specialization ?? "",
                    e.FullName ?? ""
                ).ToLowerInvariant();

                var projectTokens = projectCorpus
                    .Split(new[] { ' ', ',', '.', ';', '/', '\\', '-', '(', ')' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length > 3)
                    .ToHashSet();

                var examinerTokens = examinerKeywords
                    .Split(new[] { ' ', ',', '.', ';', '/', '\\', '-', '(', ')' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length > 3)
                    .ToHashSet();

                int matchScore = projectTokens.Intersect(examinerTokens).Count();

                int currentExaminerCount =
                    examinerCounts.GetValueOrDefault(e.Id, 0);

                return new
                {
                    Examiner = e,
                    Score = matchScore,
                    CurrentCount = currentExaminerCount
                };
            })
            .OrderByDescending(x => x.Score)   // specialization first
            .ThenBy(x => x.CurrentCount)       // then choose lower workload
            .ThenBy(x => x.Examiner.FullName)
            .ToList();

            var bestMatch = scored.FirstOrDefault();
            if (bestMatch == null)
            {
                TempData["Error"] = "Could not find a suitable examiner.";
                return RedirectToAction(nameof(ManageProjects));
            }

            var selectedExaminer = bestMatch.Examiner;
            int aiScore = bestMatch.Score;

            // Remove old assignments
            var existing = await _context.FYPProjectExaminers
                .Where(pe => pe.FYPProjectId == projectId)
                .ToListAsync();
            if (existing.Any()) _context.FYPProjectExaminers.RemoveRange(existing);

            // Assign the best matched examiner
            _context.FYPProjectExaminers.Add(new FYPProjectExaminer
            {
                FYPProjectId = project.Id,
                ExaminerId = selectedExaminer.Id,
                AssignedDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            // Send notification + email
            try
            {
                var notification = new FYPNotification
                {
                    RecipientId = selectedExaminer.Id,
                    Title = "New Project Assignment (AI Auto-Assigned)",
                    Message = $"You have been AI-matched and assigned as the Examiner for project: '{project.Title}' based on your specialisation.",
                    Type = "ExaminerAssignment",
                    RelatedRecordId = project.Id,
                    CreatedDate = DateTime.Now,
                    IsRead = false
                };
                _context.FYPNotifications.Add(notification);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(selectedExaminer.Email))
                {
                    string subject = "FYP Tracking System - AI Examiner Assignment";
                    string message = $"<p>Dear {selectedExaminer.FullName},</p><br/><p>Our AI matching engine has identified you as the best-matched Examiner for project: <strong>{project.Title}</strong> (Student: <strong>{project.Student?.FullName ?? "Unknown"}</strong>) based on your specialisation.</p>";
                    var request = HttpContext.Request;
                    string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                    string content = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);
                    _emailService.Send(selectedExaminer.Email, subject, content);
                }
            }
            catch { /* Continue on notification error */ }

            string matchMsg = aiScore > 0
                ? $"AI matched <strong>{selectedExaminer.FullName}</strong> with <strong>{aiScore} keyword(s)</strong> in common with the project scope."
                : $"No strong keyword match found. AI assigned <strong>{selectedExaminer.FullName}</strong> as the best available examiner (you may override manually).";

            TempData["Success"] = $"🤖 AI Auto-Assign Complete! {matchMsg} Assigned to '{project.Title}'.";
            return RedirectToAction(nameof(ManageProjects));
        }

        [HttpPost]
        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveExaminer(int projectId)
        {
            var project = await _context.FYPProjects
                .Include(p => p.AssignedExaminers)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project != null)
            {
                var existing = await _context.FYPProjectExaminers
                    .Where(pe => pe.FYPProjectId == projectId)
                    .ToListAsync();

                if (existing.Any())
                {
                    _context.FYPProjectExaminers.RemoveRange(existing);
                    await _context.SaveChangesAsync();
                }
                TempData["Success"] = "Examiner assignment removed successfully.";
            }
            return RedirectToAction(nameof(ManageProjects));
        }

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ExportProjectsExcel()
        {
            var students = await _context.ApplicationUsers
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Student") && !u.IsResign)
                .ToListAsync();

            var projects = await _context.FYPProjects
                .Include(p => p.Supervisor)
                .Include(p => p.AssignedExaminers).ThenInclude(ae => ae.Examiner)
                .ToListAsync();

            var withSupervisor = new List<FYPTracker.ViewModels.ManageProjectItemVM>();
            var withoutSupervisor = new List<FYPTracker.ViewModels.ManageProjectItemVM>();

            foreach(var s in students)
            {
                var p = projects.FirstOrDefault(pr => pr.StudentId == s.Id);
                if (p?.Supervisor != null)
                {
                    withSupervisor.Add(new FYPTracker.ViewModels.ManageProjectItemVM { Student = s, Project = p });
                }
                else
                {
                    withoutSupervisor.Add(new FYPTracker.ViewModels.ManageProjectItemVM { Student = s, Project = p });
                }
            }

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var ws1 = workbook.Worksheets.Add("With Supervisor");
                FormatExcelSheet(ws1, withSupervisor);

                var ws2 = workbook.Worksheets.Add("Without Supervisor");
                FormatExcelSheet(ws2, withoutSupervisor);

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "FYP_Projects_Report.xlsx");
                }
            }
        }

private void FormatExcelSheet(ClosedXML.Excel.IXLWorksheet ws, List<FYPTracker.ViewModels.ManageProjectItemVM> data)
        {
            // Headers
            ws.Cell(1, 1).Value = "Student Name";
            ws.Cell(1, 2).Value = "Student ID";
            ws.Cell(1, 3).Value = "Academic Session";
            ws.Cell(1, 4).Value = "Supervisor Name";
            ws.Cell(1, 5).Value = "Examiner Name";
            ws.Cell(1, 6).Value = "Project Title";
            ws.Cell(1, 7).Value = "Status";
            ws.Cell(1, 8).Value = "Stage 1 Score";
            ws.Cell(1, 9).Value = "Stage 1 Grade";
            ws.Cell(1, 10).Value = "Stage 2 Final Grade";
            ws.Cell(1, 11).Value = "Started Date";

            var headerRange = ws.Range("A1:K1");
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkSlateBlue;
            headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach(var item in data)
            {
                var assignedExaminer = item.Project?.AssignedExaminers?.FirstOrDefault()?.Examiner;
                ws.Cell(row, 1).Value = item.Student.FullName;
                ws.Cell(row, 2).Value = item.Student.EmployeeId;
                ws.Cell(row, 3).Value = item.Student.AcademicSession ?? "-";
                ws.Cell(row, 4).Value = item.Project?.Supervisor?.FullName ?? "Not Assigned";
                ws.Cell(row, 5).Value = assignedExaminer?.FullName ?? "Not Assigned";
                ws.Cell(row, 6).Value = item.Project?.Title ?? "No Project";
                
                string status = item.Student.IsResign ? "Inactive" : (item.Project != null ? item.Project.Status : "Inactive");
                ws.Cell(row, 7).Value = status;
                
                ws.Cell(row, 8).Value = item.Project?.Stage1TotalScore?.ToString("0.0") ?? "-";
                ws.Cell(row, 9).Value = item.Project?.Stage1Grade ?? "-";
                ws.Cell(row, 10).Value = item.Project?.FinalGrade ?? "-";
                
                ws.Cell(row, 11).Value = item.Project?.StartDate?.ToString("dd MMM yyyy") ?? "-";
                
                // Alternate row coloring
                if (row % 2 == 0)
                {
                    ws.Range($"A{row}:K{row}").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.AliceBlue;
                }

                // Status coloring
                var statusCell = ws.Cell(row, 7);
                statusCell.Style.Font.Bold = true;
                if (status == "Active" || status == "Approved" || status == "Completed")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.ForestGreen;
                else if (status == "Inactive")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.DimGray;
                else if (status == "Proposed")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.DarkOrange;
                
                // Stage 1 Grade coloring
                var stg1Cell = ws.Cell(row, 9);
                if (stg1Cell.GetString() == "Pass") stg1Cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.ForestGreen;
                else if (stg1Cell.GetString() == "Fail") stg1Cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                
                row++;
            }

            ws.Columns().AdjustToContents();
            // Give columns a bit more breathing room
            foreach (var col in ws.Columns()) col.Width += 2;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserDetails(int id)
        {
            var user = await _context.ApplicationUsers.Include(x => x.EmpDepartment).Include(x => x.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Json(new { success = false, msg = "User not found." });
            return Json(new {
                success = true,
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phone = user.Phone,
                userType = RoleDisplay.Resolve(user.UserRoles.Select(ur => ur.Role.Name), null),
                department = user.EmpDepartment?.Name,
                programme = user.Programme,
                specialization = user.Specialization,
                academicSession = user.AcademicSession,
                title = user.Title,
                maxStudentSlots = user.MaxStudentSlots
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserModal(int id, string fullName, string email, string phone, string department, string programme, string academicSession, string title, string specialization, int maxStudentSlots = 0, List<string> roles = null)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == id);
                if (user == null) return Json(new { status = false, msg = "User not found." });

                // Check email uniqueness if email changed
                if (user.Email != email)
                {
                    var emailExists = await _context.ApplicationUsers.AnyAsync(u => u.Email == email && u.Id != id);
                    if (emailExists) return Json(new { status = false, msg = "Email already in use by another account." });
                }

                user.FullName = fullName;
                user.Email = email;
                user.Phone = phone;
                
                if (user.UserRoles.Any(r => r.Role.Name == "Student") || user.UserRoles.Any(r => r.Role.Name == "STUDENT") || 
                    user.UserRoles.Any(r => r.Role.Name == "Supervisor") || user.UserRoles.Any(r => r.Role.Name == "SUPERVISOR") || 
                    user.UserRoles.Any(r => r.Role.Name == "Examiner") || user.UserRoles.Any(r => r.Role.Name == "EXAMINER"))
                {
                    var deptId = await _context.Department.Where(x => x.Name == department).Select(x => x.Id).FirstOrDefaultAsync();
                    user.DepartmentId = deptId > 0 ? deptId : user.DepartmentId;
                    
                    if (!user.UserRoles.Any(r => r.Role.Name == "Student") && !user.UserRoles.Any(r => r.Role.Name == "STUDENT"))
                    {
                        user.Programme = programme;
                        user.Specialization = specialization;
                    }
                    else
                    {
                        // For students: save Programme from the edit student dropdown
                        if (!string.IsNullOrWhiteSpace(programme))
                            user.Programme = programme;
                    }
                }

                if (user.UserRoles.Any(r => r.Role.Name == "Supervisor") || 
                    user.UserRoles.Any(r => r.Role.Name == "SUPERVISOR"))
                {
                    user.Title = title;

                    if (maxStudentSlots < 1)
                    {
                        return Json(new
                        {
                            status = false,
                            msg = "Quota must be at least 1."
                        });
                    }

                    user.MaxStudentSlots = maxStudentSlots;
                }
                else if (user.UserRoles.Any(r => r.Role.Name == "Student") || 
                        user.UserRoles.Any(r => r.Role.Name == "STUDENT"))
                {
                    // Save AcademicSession for students from the edit modal
                    if (!string.IsNullOrWhiteSpace(academicSession))
                    {
                        bool isValidSession = await _context.AcademicSessions
                            .AnyAsync(s => s.Name == academicSession);

                        if (!isValidSession)
                        {
                            return Json(new
                            {
                                success = false,
                                msg = $"Academic Session '{academicSession}' is invalid."
                            });
                        }

                        user.AcademicSession = academicSession;
                    }
                }

                _context.Update(user);

                if (roles != null && roles.Any() && !user.UserRoles.Any(r => r.Role.Name.Equals("Student", StringComparison.OrdinalIgnoreCase)))
                {
                    var allRoles = await _context.AppRoles.ToListAsync();
                    var supRole = allRoles.FirstOrDefault(r => r.Name.Equals("Supervisor", StringComparison.OrdinalIgnoreCase));
                    var exmRole = allRoles.FirstOrDefault(r => r.Name.Equals("Examiner", StringComparison.OrdinalIgnoreCase));

                    // Remove missing roles
                    if (!roles.Contains("Supervisor") && supRole != null)
                    {
                        var mapping = user.UserRoles.FirstOrDefault(ur => ur.RoleId == supRole.Id);
                        if (mapping != null) _context.UserRoleMappings.Remove(mapping);
                    }
                    if (!roles.Contains("Examiner") && exmRole != null)
                    {
                        var mapping = user.UserRoles.FirstOrDefault(ur => ur.RoleId == exmRole.Id);
                        if (mapping != null) _context.UserRoleMappings.Remove(mapping);
                    }

                    // Add new roles
                    if (roles.Contains("Supervisor") && supRole != null)
                    {
                        if (!user.UserRoles.Any(ur => ur.RoleId == supRole.Id))
                        {
                            _context.UserRoleMappings.Add(new UserRoleMapping { UserId = user.Id, RoleId = supRole.Id });
                        }
                    }
                    if (roles.Contains("Examiner") && exmRole != null)
                    {
                        if (!user.UserRoles.Any(ur => ur.RoleId == exmRole.Id))
                        {
                            _context.UserRoleMappings.Add(new UserRoleMapping { UserId = user.Id, RoleId = exmRole.Id });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                
                return Json(new { status = true, msg = "User details updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "Server error: " + ex.Message });
            }
        }

        [Authorize]
        public ActionResult EditUser(int id)
        {
            // Set the logged-in admin as CurrentUser so the menu shows the correct navigation
            var loggedInUser = _context.ApplicationUsers.FirstOrDefault(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = loggedInUser;

            var check = _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
                .Include(x => x.UserRoles).ThenInclude(ur => ur.Role)
                .Where(d => d.Id == id).FirstOrDefault();
            if (check != null)
            {
                EmployeeEditVM model = new EmployeeEditVM();
                model.Id = check.Id;
                model.UserName = check.UserName;
                model.FullName = check.FullName;

                model.PassCode = check.PassCode;
                model.Email = check.Email;
                model.Image = check.Image;
                model.UserType = RoleDisplay.Resolve(check.UserRoles.Select(ur => ur.Role.Name), null).ToUpper().Trim();
                model.Department = check.EmpDepartment?.Name;
                model.EmployeeId = check.EmployeeId;
                model.IsResign = check.IsResign.ToString();
                
                model.Phone = check.Phone;
                model.Programme = check.Programme;
                model.AcademicSession = check.AcademicSession;
                model.Title = check.Title;

                
                if (model.UserType == "SUPERVISOR")
                {
                    var projects = _context.FYPProjects.Where(p => p.SupervisorId == model.Id).ToList();
                    ViewBag.FilledSlots = projects.Count;
                }

                return View(model);
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditUser(EmployeeEditVM ApplicationUser, IFormFile? picture, string isImgChanged)
        {
            try {
            var model = _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
                .Include(x => x.UserRoles).ThenInclude(ur => ur.Role)
                .Where(d => d.Id == ApplicationUser.Id).FirstOrDefault();
            var pattern = @"^[a-z0-9](\.?[a-z0-9]){2,}@(gmail\.com|student\.newinti\.edu\.my)$";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            Match m = r.Match(ApplicationUser.Email ?? "");
            var deptQuery = _context.Department.AsQueryable();
            var check = _context.ApplicationUsers.Where(x => x.Email == ApplicationUser.Email).FirstOrDefault();

            if (!m.Success)
            {
                ModelState.AddModelError("Email", "Please enter the correct email! (xxx@gmail.com or xxx@student.newinti.edu.my)");
            }

            bool modelIsStudent = model != null && model.UserRoles.Any(ur => ur.Role.Name == "Student");
            if (model != null && modelIsStudent && !string.IsNullOrWhiteSpace(ApplicationUser.EmployeeId))
            {
                var duplicateMatric = _context.ApplicationUsers.Any(u => u.UserRoles.Any(ur => ur.Role.Name == "Student") && u.Id != model.Id && u.EmployeeId != null && u.EmployeeId.ToLower() == ApplicationUser.EmployeeId.ToLower());
                if (duplicateMatric)
                {
                    ModelState.AddModelError("EmployeeId", $"Matric ID '{ApplicationUser.EmployeeId}' is already registered to another student.");
                }
            }

            if (!ModelState.IsValid)
            {
                EmployeeEditVM model1 = new EmployeeEditVM();
                model1.Id = model?.Id ?? ApplicationUser.Id;
                model1.UserName = ApplicationUser.UserName;
                model1.FullName = ApplicationUser.FullName;
                model1.PassCode = ApplicationUser.PassCode;
                model1.Email = ApplicationUser.Email;
                model1.Image = ApplicationUser.Image;
                model1.UserType = model != null ? RoleDisplay.Resolve(model.UserRoles.Select(ur => ur.Role.Name), null).ToUpper().Trim() : "";
                model1.Image = model?.Image ?? ApplicationUser.Image;
                model1.Department = model?.EmpDepartment?.Name;
                model1.EmployeeId = model?.EmployeeId ?? ApplicationUser.EmployeeId;
                model1.IsResign = model?.IsResign.ToString();
                
                var projects = _context.FYPProjects.Include(p => p.Supervisor).Include(p => p.Student).ToList();
                var filledSlots = projects.Count(p => p.SupervisorId == model1.Id);
                ViewBag.FilledSlots = filledSlots;
                
                var currentUser = _context.ApplicationUsers.FirstOrDefault(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
                ViewBag.CurrentUser = currentUser;

                return View(model1);
            }
            if (ModelState.IsValid)
            {
                if (model != null)
                {
                    model.FullName = ApplicationUser.FullName;
                    model.IsResign = ApplicationUser.IsResign == "on";

                    model.Email = ApplicationUser.Email;
                    if (model.PassCode != ApplicationUser.PassCode)
                    {
                        model.PassCode = Encryption.GetHash(ApplicationUser.PassCode);
                    }
                    // Keep the original DB UserType (don't overwrite with uppercased version from form)
                    // The GET action uppercases for display only — saving ADMIN breaks claims auth which expects "Admin"
                    // model.UserType stays as-is (already loaded from DB above)
                    model.DepartmentId = deptQuery.Where(x => x.Name == ApplicationUser.Department).FirstOrDefault()?.Id;
                    model.EmployeeId = ApplicationUser.EmployeeId;
                    
                    model.Phone = ApplicationUser.Phone;
                    if (!modelIsStudent)
                    {
                        model.Programme = ApplicationUser.Programme;
                        model.AcademicSession = ApplicationUser.AcademicSession;
                    }
                    model.Title = ApplicationUser.Title;


                    if (isImgChanged == "true")
                    {
                        if (model.Image != "/assets/media/avatars/blank.png")
                        {
                            RemoveImage(model);
                        }
                        var uploadedImg = UploadImageToDir(picture);
                        model.Image = uploadedImg.Image;
                    }
                    _context.ApplicationUsers.Update(model);
                    _context.SaveChanges();
                    TempData["Success"] = "User updated successfully!";
                    return RedirectToAction("Index");
                }
            }

            return View(ApplicationUser);
            } catch (Exception ex) {
                // Log exception properly here in a real production environment
                TempData["Error"] = "An unexpected error occurred while updating the user profile. Please try again.";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Create(ApplicationUserVM newEmployee)
        {
            try
            {
                if (newEmployee != null)
                {
                    // ── Validate required student profile fields before sending credentials ──
                    if (newEmployee.UserType == "Student")
                    {
                        if (string.IsNullOrWhiteSpace(newEmployee.Programme))
                            return Json(new { status = false, msg = "Programme is required before sending login credentials to a student." });

                        if (string.IsNullOrWhiteSpace(newEmployee.AcademicSession))
                            return Json(new { status = false, msg = "Academic Session is required before sending login credentials to a student." });
                    }

                    if (string.IsNullOrWhiteSpace(newEmployee.FullName))
                        return Json(new { status = false, msg = "Full Name is required." });

                    if (string.IsNullOrWhiteSpace(newEmployee.Email))
                        return Json(new { status = false, msg = "Email is required." });

                    string newId;
                    if (!string.IsNullOrEmpty(newEmployee.Email) && 
                        (newEmployee.Email.EndsWith("@student.newinti.edu.my", StringComparison.OrdinalIgnoreCase) || 
                         newEmployee.Email.EndsWith("@newinti.edu.my", StringComparison.OrdinalIgnoreCase)))
                    {
                        newId = newEmployee.Email.Split('@')[0];
                    }
                    else
                    {
                        if (newEmployee.UserType == "Student")
                        {
                            var random = new Random();
                            string randomId;
                            do
                            {
                                randomId = "P" + random.Next(10000000, 99999999).ToString();
                            } while (_context.ApplicationUsers.Any(u => u.UserName == randomId || u.EmployeeId == randomId));
                            newId = randomId;
                        }
                        else
                        {
                            string prefix = "SUP-";
                            var maxNums = _context.ApplicationUsers
                                .Where(u => u.UserName != null && u.UserName.StartsWith(prefix))
                                .Select(u => u.UserName.Substring(prefix.Length))
                                .ToList();
                                
                            int nextIdNum = 1;
                            var validNums = maxNums.Select(n => int.TryParse(n, out int num) ? num : 0);
                            if (validNums.Any())
                            {
                                nextIdNum = validNums.Max() + 1;
                            }
                            
                            newId = $"{prefix}{nextIdNum:D3}";
                        }
                    }

                    newEmployee.EmplooyeeInfoID = newId;
                    newEmployee.UserName = newId;

                    var check = _context.ApplicationUsers.Where(d => d.UserName == newEmployee.UserName).FirstOrDefault();
                    var check1 = _context.ApplicationUsers.Where(d => d.Email == newEmployee.Email).FirstOrDefault();
                    var deptQuery = _context.Department.AsQueryable();
                    if (check == null && check1 == null)
                    {
                        string generatedPassword = Guid.NewGuid().ToString("N").Substring(0, 8);

                        var model = new ApplicationUser
                        {
                            UserName = newEmployee.UserName,
                            FullName = newEmployee.FullName,

                            Email = newEmployee.Email,
                            PassCode = Encryption.GetHash(generatedPassword),
                            EmployeeId = newEmployee.EmplooyeeInfoID,
                            DateCreated = DateTime.Now,
                            DateJoin = DateTime.Now,
                            AddedBy = "sys",
                            Image = "/assets/media/avatars/blank.png",
                            DepartmentId = deptQuery.Where(x => x.Name == newEmployee.Department).FirstOrDefault()?.Id,
                            Phone = newEmployee.Phone,
                            Programme = newEmployee.Programme,
                            AcademicSession = newEmployee.AcademicSession,
                            Title = newEmployee.Title,
                            MaxStudentSlots =
                                newEmployee.UserType == "Supervisor" ||
                                newEmployee.UserType == "AcademicStaff"
                                    ? 5
                                    : null,
                            RequirePasswordChange = true
                    };
                        _context.ApplicationUsers.Add(model);
                        _context.SaveChanges();

                        // Role membership is stored in UserRoleMappings (source of truth)
                        var requestedRole = (newEmployee.UserType ?? "").Trim();
                        var rolesToAddNew = requestedRole == "AcademicStaff" ? new[] { "Supervisor", "Examiner" } : new[] { requestedRole };
                        foreach (var r in rolesToAddNew)
                        {
                            var roleEntity = _context.AppRoles.FirstOrDefault(ar => ar.Name == r);
                            if (roleEntity != null)
                            {
                                _context.UserRoleMappings.Add(new UserRoleMapping { UserId = model.Id, RoleId = roleEntity.Id });
                            }
                        }
                        _context.SaveChanges();

                        try
                        {
                            var request = HttpContext.Request;
                            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";

                            string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                            string message = $@"<p>Welcome, <strong>{newEmployee.FullName}</strong>!</p><br/><p>Your account has been successfully created. Below are your login credentials:</p>
                                <ul>
                                    <li><strong>Username:</strong> {newEmployee.UserName}</li>
                                    <li><strong>Password:</strong> {generatedPassword}</li>
                                </ul>
                                <p>Please log in and change your password as soon as possible.</p>";
                            string htmlBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Click Here to Login", loginUrl);
                            _emailService.Send(newEmployee.Email, subject, htmlBody);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send email: {ex.Message}");
                        }

                        return Json(new { status = true, msg = "User created successfully!", generatedPassword = generatedPassword, userName = model.UserName });
                    }
                    else
                    {
                        if (check != null) return Json(new { status = false, msg = "This Username is exists, please try another one!" });
                        if (check1 != null) return Json(new { status = false, msg = "This Email is exists, please try another one!" });
                    }

                }
                //return RedirectToAction("Index", newEmployee);
                return Json(new { status = false, msg = "Invalid data enter. Please try again." });
            }
            catch(Exception ex)
            {
                return Json(new { status = false, msg = "An error occur. Msg: " + ex.Message });
            }
        }

        public ActionResult ManageUsersData()
        {
            var users = _context.ApplicationUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .Where(u => !u.UserRoles.Any(r => r.Role.Name == "Admin") && !u.UserRoles.Any(r => r.Role.Name == "SuperAdmin"))
                .ToList();

            var projects = _context.FYPProjects.Include(p => p.Supervisor).Include(p => p.Student).ToList();

            var empList = users.Select(u => new {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email,
                employeeId = u.EmployeeId,
                userType = RoleDisplay.Resolve(u.UserRoles.Select(ur => ur.Role.Name), null), // Preserved for frontend DataTable exact string matching compatibility
                dateCreated = u.DateCreated,
                isResign = u.IsResign,
                image = u.Image,
                supervisorName = u.UserRoles.Any(r => r.Role.Name == "Student")
                    ? (projects.FirstOrDefault(p => p.StudentId == u.Id)?.Supervisor?.FullName ?? "Not Assigned")
                    : "-",
                assignedStudents = u.UserRoles.Any(r => r.Role.Name == "Supervisor")
                    ? string.Join(", ", projects.Where(p => p.SupervisorId == u.Id && p.Student != null).Select(p => p.Student.FullName).DefaultIfEmpty("No Students"))
                    : u.UserRoles.Any(r => r.Role.Name == "Examiner") 
                        ? string.Join(", ", projects.Where(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == u.Id) && p.Student != null).Select(p => p.Student.FullName).DefaultIfEmpty("No Students"))
                        : "-",
                assignedStudentCount = u.UserRoles.Any(r => r.Role.Name == "Supervisor")
                    ? projects.Count(p => p.SupervisorId == u.Id && p.Student != null).ToString()
                    : u.UserRoles.Any(r => r.Role.Name == "Examiner")
                        ? projects.Count(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == u.Id) && p.Student != null).ToString()
                        : "-",
                specialization = u.Specialization,
                requirePasswordChange = u.RequirePasswordChange,
                accountExpiry = u.AccountExpiry,
                isProfileIncomplete = (u.UserRoles.Any(r => r.Role.Name == "Supervisor" || r.Role.Name == "Examiner") && 
                                      (u.DepartmentId == null || 
                                       string.IsNullOrWhiteSpace(u.Programme) || 
                                       string.IsNullOrWhiteSpace(u.Specialization) || 
                                       string.IsNullOrWhiteSpace(u.Title)))
            }).ToList();

            return Json(new { data = empList, recordsTotal = empList.Count(), recordsFiltered = empList.Count() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public bool Delete(int id)
        {
            try
            {
                ApplicationUser ApplicationUser = _context.ApplicationUsers.FirstOrDefault(x => x.Id ==id);
                if(ApplicationUser != null)
                {
                    ApplicationUser.IsResign = true;
                    _context.SaveChanges();
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public bool Activate(int id)
        {
            try
            {
                ApplicationUser ApplicationUser = _context.ApplicationUsers.FirstOrDefault(x => x.Id ==id);
                if(ApplicationUser != null)
                {
                    ApplicationUser.IsResign = false;
                    _context.SaveChanges();
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HardDelete(int id)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .Include(u => u.UserRoles).ThenInclude(r => r.Role)
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (user == null) return Json(new { status = false, msg = "User not found." });

                // 1. ChatGroupMembers — Restrict FK (student or supervisor as member)
                var groupMembers = await _context.ChatGroupMembers
                    .Where(m => m.StudentId == id).ToListAsync();
                _context.ChatGroupMembers.RemoveRange(groupMembers);

                // 2. ChatGroups owned by supervisor — Restrict FK (messages cascade via Group)
                if (user.UserRoles.Any(r => r.Role.Name == "Supervisor") || user.UserRoles.Any(r => r.Role.Name == "SUPERVISOR"))
                {
                    var ownedGroups = await _context.ChatGroups
                        .Include(g => g.Messages)
                        .Include(g => g.Members)
                        .Where(g => g.SupervisorId == id).ToListAsync();
                    foreach (var g in ownedGroups)
                    {
                        _context.ChatMessages.RemoveRange(g.Messages);
                        _context.ChatGroupMembers.RemoveRange(g.Members);
                    }
                    _context.ChatGroups.RemoveRange(ownedGroups);
                }

                // 3. ChatMessages (direct 1-to-1 messages) — Restrict FK
                // 3. ChatMessages — Preserve received history by nullifying ReceiverId, delete sent messages (Restrict FK)
                var sentMessages = await _context.ChatMessages
                    .Where(m => m.SenderId == id).ToListAsync();
                _context.ChatMessages.RemoveRange(sentMessages);

                var receivedMessages = await _context.ChatMessages
                    .Where(m => m.ReceiverId == id && m.SenderId != id).ToListAsync();
                foreach (var msg in receivedMessages)
                {
                    msg.ReceiverId = null;
                }

                // FYPNotifications — No FK restrict but needs cleanup to avoid orphans
                var notifications = await _context.FYPNotifications
                    .Where(n => n.RecipientId == id).ToListAsync();
                _context.FYPNotifications.RemoveRange(notifications);


                // 4. SupervisorRequests — Restrict FK
                var requests = await _context.SupervisorRequests
                    .Where(r => r.StudentId == id || r.SupervisorId == id).ToListAsync();
                _context.SupervisorRequests.RemoveRange(requests);

                // 5. MeetingBookings — Restrict FK (MeetingLogs cascade automatically)
                var meetings = await _context.MeetingBookings
                    .Where(b => b.StudentId == id || b.SupervisorId == id).ToListAsync();
                _context.MeetingBookings.RemoveRange(meetings);

                // 6. SupervisorAvailabilities — FK on supervisor
                var availabilities = await _context.SupervisorAvailabilities
                    .Where(a => a.SupervisorId == id).ToListAsync();
                _context.SupervisorAvailabilities.RemoveRange(availabilities);


                // 8. UserRoleMappings
                var userRoles = await _context.UserRoleMappings
                    .Where(r => r.UserId == id).ToListAsync();
                _context.UserRoleMappings.RemoveRange(userRoles);

                if (user.UserRoles.Any(r => r.Role.Name == "Student") || user.UserRoles.Any(r => r.Role.Name == "STUDENT"))
                {
                    var chatGroupMembers = await _context.ChatGroupMembers
                        .Where(m => m.StudentId == id).ToListAsync();
                    _context.ChatGroupMembers.RemoveRange(chatGroupMembers);

                    var projects = await _context.FYPProjects
                        .Where(p => p.StudentId == id).ToListAsync();
                    
                    if (projects.Any())
                    {
                        var projectIds = projects.Select(p => p.Id).ToList();

                        // Remove Project Examiners
                        var projectExaminers = await _context.FYPProjectExaminers
                            .Where(e => projectIds.Contains(e.FYPProjectId)).ToListAsync();
                        _context.FYPProjectExaminers.RemoveRange(projectExaminers);

                        // Remove Milestones
                        var milestones = await _context.FYPMilestones
                            .Where(m => projectIds.Contains(m.FYPProjectId)).ToListAsync();
                        _context.FYPMilestones.RemoveRange(milestones);

                        _context.FYPProjects.RemoveRange(projects);
                    }
                }
                if (user.UserRoles.Any(r => r.Role.Name == "Supervisor") || user.UserRoles.Any(r => r.Role.Name == "SUPERVISOR"))
                {
                    var chatGroups = await _context.ChatGroups
                        .Where(g => g.SupervisorId == id).ToListAsync();
                    _context.ChatGroups.RemoveRange(chatGroups);

                    // Nullify supervisor on students' projects (preserve students' data)
                    var supervisorProjects = await _context.FYPProjects
                        .Where(p => p.SupervisorId == id).ToListAsync();
                    foreach (var p in supervisorProjects)
                        p.SupervisorId = null;

                    // Remove supervisor's feedbacks — Restrict FK
                    var feedbacks = await _context.FYPFeedbacks
                        .Where(f => f.SupervisorId == id).ToListAsync();
                    _context.FYPFeedbacks.RemoveRange(feedbacks);
                }
                if (user.UserRoles.Any(r => r.Role.Name == "Examiner") || user.UserRoles.Any(r => r.Role.Name == "EXAMINER"))
                {
                    // Nullify examiner on projects by removing mapping
                    var examinerAssignments = await _context.FYPProjectExaminers
                        .Where(pe => pe.ExaminerId == id).ToListAsync();
                    _context.FYPProjectExaminers.RemoveRange(examinerAssignments);
                }

                // 9. Submission files uploaded by this user — Restrict FK
                var uploadedFiles = await _context.FYPSubmissionFiles
                    .Where(f => f.UploadedBy == id).ToListAsync();
                _context.FYPSubmissionFiles.RemoveRange(uploadedFiles);

                // Save all child deletions first
                await _context.SaveChangesAsync();

                // 10. Remove user via Raw SQL to completely bypass EF Core tracking conflicts
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM ApplicationUser WHERE Id = {0}", id);


                return Json(new { status = true, msg = "User permanently deleted." });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return Json(new { status = false, msg = "Error: " + inner });
            }
        }

        // ============================================================
        // ADMIN - MULTIPLE FORM TEMPLATES
        // GET / POST / AUDIT HISTORY
        // ============================================================

        private static readonly System.Threading.SemaphoreSlim _templateLock =
            new System.Threading.SemaphoreSlim(1, 1);

        // Template ID is used for navigation and file selection.
        // The JSON TemplateKey is preserved separately.
        private static readonly Dictionary<
            string, (string FileName, string HistoryFolder)>
            _formTemplateFiles = new(
                StringComparer.Ordinal)
        {
            ["stage1-meeting"] = (
                "Stage1RecordOfSupervisorMeeting6000CEM6002CMD.default.json",
                "RecordOfSupervisorMeeting"
            ),

            ["stage2-meeting"] = (
                "Stage2RecordOfSupervisorMeeting6001CEM303COM.default.json",
                "Stage2RecordOfSupervisorMeeting"
            ),

            ["iaf"] = (
                "InitialProjectAllocationForm.default.json",
                "InitialProjectAllocationForm"
            ),

            ["thesis-cover"] = (
                "ThesisCoverPage.default.json",
                "ThesisCoverPage"
            ),

            ["group-genai"] = (
                "GroupGenAiDeclaration.default.json",
                "GroupGenAiDeclaration"
            )
        };

        private static readonly System.Text.Json.JsonSerializerOptions
            _formTemplateJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // Generate the next version without resetting decimal versions.
        // Example: 1.011111 -> 1.011112
        private static string GetNextFormTemplateVersion(
            string oldVersion,
            string versionsDirectory)
        {
            var match = Regex.Match(
                oldVersion ?? "",
                @"^(v?)(\d+)(?:\.(\d+))?$",
                RegexOptions.CultureInvariant);

            if (!match.Success)
                throw new InvalidDataException(
                    "Unsupported template version format.");

            string prefix = match.Groups[1].Value;
            string majorText = match.Groups[2].Value;

            long major = long.Parse(
                majorText,
                System.Globalization.CultureInfo.InvariantCulture);

            bool hasMinor = match.Groups[3].Success;
            string minorText = hasMinor
                ? match.Groups[3].Value
                : "";

            long minor = hasMinor
                ? long.Parse(
                    minorText,
                    System.Globalization.CultureInfo.InvariantCulture)
                : 0;

            for (int attempt = 0; attempt < 100000; attempt++)
            {
                string candidate;

                if (hasMinor)
                {
                    minor = checked(minor + 1);

                    string paddedMinor = minor.ToString(
                        "D" + minorText.Length,
                        System.Globalization.CultureInfo.InvariantCulture);

                    candidate = prefix + majorText + "." + paddedMinor;
                }
                else
                {
                    major = checked(major + 1);
                    candidate = prefix +
                        major.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                }

                string snapshotPath = Path.Combine(
                    versionsDirectory,
                    $"version-{candidate}.json");

                if (!System.IO.File.Exists(snapshotPath))
                    return candidate;
            }

            throw new InvalidOperationException(
                "Unable to allocate a unique template version.");
        }

        // Create snapshots without overwriting existing versions.
        private static async Task WriteTemplateSnapshotAsync(
            string path,
            string json)
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false));

            await writer.WriteAsync(json);
        }

        // ============================================================
        // GET - OPEN SELECTED TEMPLATE
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> FormTemplateSettings(
            string? templateId = null,
            string? templateKey = null)
        {
            var loginName = HttpContext.User.Identity?.Name;

            var currentUser = await _context.ApplicationUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.UserName == loginName ||
                    u.Email == loginName);

            if (currentUser == null)
                return RedirectToAction("Login", "User");

            ViewBag.CurrentUser = currentUser;

            // Backward compatibility with previous templateKey URLs.
            if (string.IsNullOrWhiteSpace(templateId) &&
                !string.IsNullOrWhiteSpace(templateKey))
            {
                templateId = templateKey switch
                {
                    "record-of-supervisor-meeting" =>
                        "stage1-meeting",

                    "record-of-supervisor-meeting-6001cem-303com" =>
                        "stage2-meeting",

                    "thesis-cover-page" =>
                        "thesis-cover",

                    "group-genai-declaration" =>
                        "group-genai",

                    _ => templateKey
                };
            }

            string selectedId = string.IsNullOrWhiteSpace(templateId)
                ? "stage1-meeting"
                : templateId.Trim();

            if (!_formTemplateFiles.TryGetValue(
                selectedId,
                out var definition))
            {
                return BadRequest("Unsupported form template.");
            }

            string baseDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "FormTemplates");

            string templatePath = Path.Combine(
                baseDir,
                definition.FileName);

            string historyPath = Path.Combine(
                baseDir,
                "History",
                definition.HistoryFolder,
                "history.json");

            if (!System.IO.File.Exists(templatePath))
                return NotFound(
                    "Template file not found: " + definition.FileName);

            try
            {
                string json = await System.IO.File
                    .ReadAllTextAsync(templatePath);

                var template =
                    System.Text.Json.JsonSerializer.Deserialize<
                        MeetingRecordTemplateVM>(
                            json,
                            _formTemplateJsonOptions);

                if (template == null ||
                    string.IsNullOrWhiteSpace(template.TemplateKey) ||
                    template.Page == null ||
                    template.Fields == null ||
                    template.Fields.Count == 0)
                {
                    return StatusCode(
                        500,
                        "Form template configuration is invalid.");
                }

                // Read existing audit records, but never modify them
                // simply by opening the page.
                var history = new List<TemplateAuditEvent>();

                if (System.IO.File.Exists(historyPath))
                {
                    string historyJson = await System.IO.File
                        .ReadAllTextAsync(historyPath);

                    history =
                        System.Text.Json.JsonSerializer.Deserialize<
                            List<TemplateAuditEvent>>(
                                historyJson,
                                _formTemplateJsonOptions)
                        ?? throw new InvalidDataException(
                            "Template audit history is invalid.");
                }

                var vm = new FormTemplateSettingsViewModel
                {
                    CurrentTemplate = template,
                    History = history
                        .OrderByDescending(h => h.DateModifiedUtc)
                        .ToList()
                };
                
                ViewBag.SelectedTemplateId = selectedId;

                // Thesis Cover Page date comes from Admin System Settings.
                // Do not use today's date or a hardcoded cover-page date.
                ViewBag.CoverPageDeadlineDisplay = "";

                if (selectedId == "thesis-cover")
                {
                    string? deadlineValue = await _context.FYPSettings
                        .AsNoTracking()
                        .Where(s => s.Key == "DPPDeadline")
                        .Select(s => s.Value)
                        .FirstOrDefaultAsync();

                    string[] acceptedFormats =
                    {
                        "yyyy-MM-dd",
                        "yyyy-MM-ddTHH:mm:ss",
                        "yyyy-MM-ddTHH:mm:ss.fff"
                    };

                    if (DateTime.TryParseExact(
                        deadlineValue?.Trim(),
                        acceptedFormats,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out DateTime dueDate))
                    {
                        int day = dueDate.Day;

                        string suffix = day % 100 >= 11 && day % 100 <= 13
                            ? "th"
                            : (day % 10) switch
                            {
                                1 => "st",
                                2 => "nd",
                                3 => "rd",
                                _ => "th"
                            };

                        ViewBag.CoverPageDeadlineDisplay =
                            day.ToString() +
                            suffix + " " +
                            dueDate.ToString(
                                "MMMM yyyy",
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                return View(vm);

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                return StatusCode(
                    500,
                    "Error loading form template.");
            }
        }

        // ============================================================
        // POST - SAVE SELECTED TEMPLATE
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFormTemplate(
            string templateId,
            MeetingRecordTemplateVM model)
        {
            if (string.IsNullOrWhiteSpace(templateId) ||
                !_formTemplateFiles.TryGetValue(
                    templateId,
                    out var definition))
            {
                return BadRequest("Invalid template selection.");
            }

            if (model == null ||
                model.Fields == null ||
                model.Fields.Count == 0 ||
                !ModelState.IsValid)
            {
                return BadRequest("Invalid template data.");
            }

            var loginName = HttpContext.User.Identity?.Name;

            var currentUser = await _context.ApplicationUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.UserName == loginName ||
                    u.Email == loginName);

            if (currentUser == null)
                return Unauthorized();

            await _templateLock.WaitAsync();

            try
            {
                string baseDir = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "FormTemplates");

                string templatePath = Path.Combine(
                    baseDir,
                    definition.FileName);

                string historyDir = Path.Combine(
                    baseDir,
                    "History",
                    definition.HistoryFolder);

                string versionsDir = Path.Combine(
                    historyDir,
                    "Versions");

                string historyPath = Path.Combine(
                    historyDir,
                    "history.json");

                if (!System.IO.File.Exists(templatePath))
                    return NotFound("Original template not found.");

                string oldJson = await System.IO.File
                    .ReadAllTextAsync(templatePath);

                var original =
                    System.Text.Json.JsonSerializer.Deserialize<
                        MeetingRecordTemplateVM>(
                            oldJson,
                            _formTemplateJsonOptions);

                if (original == null ||
                    original.Fields == null ||
                    original.Fields.Count == 0 ||
                    string.IsNullOrWhiteSpace(original.TemplateKey) ||
                    string.IsNullOrWhiteSpace(original.Version))
                {
                    return StatusCode(
                        500,
                        "Original template is invalid.");
                }

                // Never trust TemplateKey from the browser.
                if (!string.Equals(
                    model.TemplateKey,
                    original.TemplateKey,
                    StringComparison.Ordinal))
                {
                    return BadRequest("Template Key mismatch.");
                }

                // Prevent stale browser tabs from overwriting updates.
                if (!string.Equals(
                    model.Version,
                    original.Version,
                    StringComparison.Ordinal))
                {
                    return Conflict(
                        "This template was updated elsewhere. " +
                        "Refresh the page before saving.");
                }

                // Ensure all immutable field keys match the saved JSON.
                if (model.Fields.Count != original.Fields.Count)
                    return BadRequest("Field count mismatch.");

                var submittedFields =
                    new Dictionary<string, TemplateField>(
                        StringComparer.Ordinal);

                foreach (var field in model.Fields)
                {
                    if (field == null ||
                        string.IsNullOrWhiteSpace(field.Key) ||
                        !submittedFields.TryAdd(field.Key, field))
                    {
                        return BadRequest(
                            "Duplicate or missing Field Key.");
                    }

                    if (string.IsNullOrWhiteSpace(field.Label) ||
                        field.Label.Length > 250)
                    {
                        return BadRequest(
                            "Field Label is invalid.");
                    }

                    if (field.Columns < 3 || field.Columns > 12)
                        return BadRequest(
                            "Field Width must be between 3 and 12.");

                    if (field.Order < 1 || field.Order > 1000)
                        return BadRequest(
                            "Display Order is invalid.");
                }

                var existingKeys = original.Fields
                    .Select(f => f.Key)
                    .ToHashSet(StringComparer.Ordinal);

                if (submittedFields.Keys.Any(
                    key => !existingKeys.Contains(key)))
                {
                    return BadRequest(
                        "Unknown Field Key detected.");
                }

                if (submittedFields.Values
                    .Select(f => f.Order)
                    .Distinct()
                    .Count() != submittedFields.Count)
                {
                    return BadRequest(
                        "Display Order values must be unique.");
                }

                // Update only editable properties.
                // Keep Title, TemplateKey, Page, Type and MinHeightMm
                // from the original server-side JSON.
                var changes = new List<TemplateFieldChange>();

                foreach (var existingField in original.Fields)
                {
                    var submitted = submittedFields[existingField.Key];

                    string newLabel = submitted.Label.Trim();

                    if (existingField.Label != newLabel)
                    {
                        changes.Add(new TemplateFieldChange
                        {
                            FieldKey = existingField.Key,
                            PropertyName = "Label",
                            PreviousValue = existingField.Label,
                            NewValue = newLabel
                        });
                    }

                    if (existingField.Order != submitted.Order)
                    {
                        changes.Add(new TemplateFieldChange
                        {
                            FieldKey = existingField.Key,
                            PropertyName = "Order",
                            PreviousValue =
                                existingField.Order.ToString(),
                            NewValue =
                                submitted.Order.ToString()
                        });
                    }

                    if (existingField.Columns != submitted.Columns)
                    {
                        changes.Add(new TemplateFieldChange
                        {
                            FieldKey = existingField.Key,
                            PropertyName = "Columns",
                            PreviousValue =
                                existingField.Columns.ToString(),
                            NewValue =
                                submitted.Columns.ToString()
                        });
                    }

                    existingField.Label = newLabel;
                    existingField.Order = submitted.Order;
                    existingField.Columns = submitted.Columns;
                }

                if (changes.Count == 0)
                {
                    TempData["SuccessMessage"] =
                        "No template changes detected.";

                    return RedirectToAction(
                        nameof(FormTemplateSettings),
                        new { templateId });
                }

                // Read audit history BEFORE writing any changes.
                var history = new List<TemplateAuditEvent>();

                bool hasHistory =
                    System.IO.File.Exists(historyPath);

                if (hasHistory)
                {
                    string historyJson = await System.IO.File
                        .ReadAllTextAsync(historyPath);

                    history =
                        System.Text.Json.JsonSerializer.Deserialize<
                            List<TemplateAuditEvent>>(
                                historyJson,
                                _formTemplateJsonOptions)
                        ?? throw new InvalidDataException(
                            "Existing audit history is invalid.");
                }
                else
                {
                    // Initial audit event for a newly introduced template.
                    history.Add(new TemplateAuditEvent
                    {
                        ActionType = "Created",
                        ModifiedBy = "System Baseline",
                        TemplateKey = original.TemplateKey,
                        TemplateVersion = original.Version,
                        DateModifiedUtc = DateTime.UtcNow
                    });
                }

                Directory.CreateDirectory(versionsDir);

                string previousVersion = original.Version;

                string newVersion = GetNextFormTemplateVersion(
                    previousVersion,
                    versionsDir);

                original.Version = newVersion;

                history.Add(new TemplateAuditEvent
                {
                    TemplateKey = original.TemplateKey,
                    TemplateVersion = newVersion,
                    PreviousVersion = previousVersion,
                    ActionType = "Updated",
                    ModifiedBy =
                        currentUser.FullName ?? currentUser.UserName,
                    DateModifiedUtc = DateTime.UtcNow,
                    FieldChanges = changes
                });

                string newJson =
                    System.Text.Json.JsonSerializer.Serialize(
                        original,
                        _formTemplateJsonOptions);

                string newHistoryJson =
                    System.Text.Json.JsonSerializer.Serialize(
                        history,
                        _formTemplateJsonOptions);

                string oldSnapshotPath = Path.Combine(
                    versionsDir,
                    $"version-{previousVersion}.json");

                string newSnapshotPath = Path.Combine(
                    versionsDir,
                    $"version-{newVersion}.json");

                // Never overwrite a historical snapshot.
                if (!System.IO.File.Exists(oldSnapshotPath))
                {
                    await WriteTemplateSnapshotAsync(
                        oldSnapshotPath,
                        oldJson);
                }

                // Prepare the updated active template and audit log.
                string tempTemplatePath = templatePath + "." +
                    Guid.NewGuid().ToString("N") + ".tmp";

                string tempHistoryPath = historyPath + "." +
                    Guid.NewGuid().ToString("N") + ".tmp";

                try
                {
                    await System.IO.File.WriteAllTextAsync(
                        tempTemplatePath,
                        newJson);

                    await System.IO.File.WriteAllTextAsync(
                        tempHistoryPath,
                        newHistoryJson);

                    // FileMode.CreateNew prevents overwriting
                    // an existing version snapshot.
                    await WriteTemplateSnapshotAsync(
                        newSnapshotPath,
                        newJson);

                    // Replace current files after preparation.
                    System.IO.File.Move(
                        tempHistoryPath,
                        historyPath,
                        true);

                    System.IO.File.Move(
                        tempTemplatePath,
                        templatePath,
                        true);
                }
                finally
                {
                    if (System.IO.File.Exists(tempTemplatePath))
                        System.IO.File.Delete(tempTemplatePath);

                    if (System.IO.File.Exists(tempHistoryPath))
                        System.IO.File.Delete(tempHistoryPath);
                }

                TempData["SuccessMessage"] =
                    "Form Template successfully updated to version " +
                    newVersion + ".";

                return RedirectToAction(
                    nameof(FormTemplateSettings),
                    new { templateId });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                return StatusCode(
                    500,
                    "Error saving form template. " +
                    "Please check the application logs.");
            }
            finally
            {
                _templateLock.Release();
            }
        }

        // ============================================================
        // LEGACY HISTORY ENDPOINTS
        // Disabled in the UI. Historical JSON files are preserved.
        // ============================================================

        [HttpGet]
        public IActionResult ViewHistoricalTemplate(string version)
        {
            return NotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RestoreFormTemplate(string version)
        {
            return NotFound();
        }


        public async Task<IActionResult> Settings(string sessionFilter = null)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            // Fetch distinct academic sessions
            var distinctSessions = await _context.FYPProjects
                .Where(p => !string.IsNullOrEmpty(p.AcademicSession))
                .Select(p => p.AcademicSession)
                .Distinct()
                .OrderByDescending(s => s)
                .ToListAsync();

            ViewBag.DistinctSessions = distinctSessions;

            if (string.IsNullOrEmpty(sessionFilter))
            {
                sessionFilter = distinctSessions.FirstOrDefault() ?? "2024/2025";
            }
            
            ViewBag.CurrentSessionFilter = sessionFilter;
            
            var currentSessionObj = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == sessionFilter);
            if (currentSessionObj != null)
            {
                ViewBag.DefaultMilestoneTemplates = await _context.DefaultMilestoneTemplates
                    .Where(t => t.AcademicSessionId == currentSessionObj.Id && t.Stage == 1)
                    .OrderBy(t => t.DisplayOrder)
                    .ToListAsync();
            }
            else
            {
                ViewBag.DefaultMilestoneTemplates = new List<DefaultMilestoneTemplate>();
            }

            var existingIaf = await _context.FYPMilestones
                .Include(m => m.Project)
                .Where(m => m.Project.AcademicSession == sessionFilter && (m.Title == "Initial Project Allocation Form (IAF)" || m.Title.Contains("IAF")) && m.DueDate != null)
                .OrderByDescending(m => m.DueDate)
                .FirstOrDefaultAsync();
                
            if (existingIaf != null && existingIaf.DueDate.HasValue)
            {
                ViewBag.IAFDeadline = existingIaf.DueDate.Value.ToString("yyyy-MM-dd");
            }
            else
            {
                ViewBag.IAFDeadline = "";
            }

            var existingStep1 = await _context.FYPMilestones
                .Include(m => m.Project)
                .Where(m => m.Project.AcademicSession == sessionFilter && m.Title.StartsWith("Step 1") && m.DueDate != null)
                .OrderByDescending(m => m.DueDate)
                .FirstOrDefaultAsync();
                
            ViewBag.Step1to4Deadline = existingStep1?.DueDate?.ToString("yyyy-MM-dd") ?? "";

            var existingStep5 = await _context.FYPMilestones
                .Include(m => m.Project)
                .Where(m => m.Project.AcademicSession == sessionFilter && m.Title.StartsWith("Step 5") && m.DueDate != null)
                .OrderByDescending(m => m.DueDate)
                .FirstOrDefaultAsync();
                
            ViewBag.Step5Deadline = existingStep5?.DueDate?.ToString("yyyy-MM-dd") ?? "";
            // Load FYP global settings (DPP deadline, exhibition, etc.)
            var fypSettings = await _context.FYPSettings.ToListAsync();
            ViewBag.FYPSettings = fypSettings;

            // Helper: get setting value by key
            string GetSetting(string key) => fypSettings.FirstOrDefault(s => s.Key == key)?.Value ?? "";
            ViewBag.DPPDeadline       = GetSetting("DPPDeadline");
            ViewBag.ExhibitionDate    = GetSetting("ExhibitionDate");
            ViewBag.ExhibitionTimeStart = GetSetting("ExhibitionTimeStart");
            ViewBag.ExhibitionTimeEnd   = GetSetting("ExhibitionTimeEnd");
            // Configurable supervisor request auto-rejection timeout (in days, default 3)
            ViewBag.SupervisorRequestTimeoutDays = GetSetting("SupervisorRequestTimeoutDays") is string v && v != "" ? v : "3";

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateIAFDeadline(DateTime? deadline, string sessionFilter)
        {
            try 
            {
                if (!deadline.HasValue)
                {
                    TempData["Error"] = "The date provided was invalid or empty.";
                    return RedirectToAction(nameof(Settings), new { sessionFilter });
                }
                
                if (string.IsNullOrEmpty(sessionFilter))
                {
                    TempData["Error"] = "An academic session must be selected.";
                    return RedirectToAction(nameof(Settings));
                }

                // Set the time to 11:59:59 PM of the selected date
                var endOfDayDeadline = new DateTime(deadline.Value.Year, deadline.Value.Month, deadline.Value.Day, 23, 59, 59);

                var milestones = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .Where(m => m.Project.AcademicSession == sessionFilter && (m.Title == "Initial Project Allocation Form (IAF)" || m.Title.Contains("IAF")))
                    .ToListAsync();

                int count = 0;
                foreach(var m in milestones)
                {
                    m.DueDate = endOfDayDeadline;
                    count++;
                }

                if (count > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Successfully updated the IAF deadline for {count} student project(s) in {sessionFilter}.";
                }
                else
                {
                    TempData["Info"] = $"No IAF milestones found in {sessionFilter} to update.";
                }

                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
            catch (Exception)
            {
                TempData["Error"] = "An unexpected error occurred while processing your request. Please check the logs.";
                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDefaultMilestoneDate(DateTime? deadline, string sessionFilter, string milestoneTitle)
        {
            try
            {
                if (!deadline.HasValue)
                {
                    TempData["BulkUpdateError"] = "The date provided was invalid or empty.";
                    return RedirectToAction(nameof(Settings), new { sessionFilter });
                }

                if (string.IsNullOrEmpty(sessionFilter))
                {
                    TempData["BulkUpdateError"] = "An academic session must be selected.";
                    return RedirectToAction(nameof(Settings));
                }
                
                if (string.IsNullOrEmpty(milestoneTitle))
                {
                    TempData["BulkUpdateError"] = "A milestone title must be selected.";
                    return RedirectToAction(nameof(Settings), new { sessionFilter });
                }

                // Set the time to 11:59:59 PM of the selected date
                var endOfDayDeadline = new DateTime(deadline.Value.Year, deadline.Value.Month, deadline.Value.Day, 23, 59, 59);

                var milestones = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .Where(m => m.Project.AcademicSession == sessionFilter && m.Title.Contains(milestoneTitle))
                    .ToListAsync();

                int count = 0;
                foreach (var m in milestones)
                {
                    m.DueDate = endOfDayDeadline;
                    count++;
                }

                if (count > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["BulkUpdateSuccess"] = $"Successfully updated the deadline for '{milestoneTitle}' for {count} student project(s) in {sessionFilter}.";
                }
                else
                {
                    TempData["BulkUpdateInfo"] = $"No '{milestoneTitle}' milestones found in {sessionFilter} to update.";
                }

                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
            catch (Exception)
            {
                TempData["BulkUpdateError"] = "An unexpected error occurred while updating the milestone due date.";
                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeedFinalEvaluationSteps(string sessionFilter)
        {
            try
            {
                // Find all projects in the session that have an approved Final/Thesis milestone but no Step milestones
                var projects = await _context.FYPProjects
                    .Include(p => p.Milestones)
                    .Where(p => p.AcademicSession == sessionFilter || string.IsNullOrEmpty(sessionFilter))
                    .ToListAsync();

                int count = 0;
                var toAdd = new List<FYPMilestone>();

                foreach (var project in projects)
                {
                    bool hasFinalApproved = project.Milestones.Any(m =>
                        (m.Title.Contains("Final", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase))
                        && m.Status == "Approved");

                    bool hasSteps = project.Milestones.Any(m => m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase));

                    if (hasFinalApproved && !hasSteps)
                    {
                        toAdd.AddRange(new[]
                        {
                            new FYPMilestone { FYPProjectId = project.Id, Title = "Step 1: Turnitin", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = project.Id, Title = "Step 2: DPP report full", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = project.Id, Title = "Step 3: Marksheet Submission", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = project.Id, Title = "Step 4: Record of Supervisor Meeting Form Submission", Status = "Pending" },
                            new FYPMilestone { FYPProjectId = project.Id, Title = "Step 5: Presentation Slides Submission", Status = "Pending" },
                        });
                        count++;
                    }
                }

                if (toAdd.Any())
                {
                    _context.FYPMilestones.AddRange(toAdd);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Successfully created Step milestones for {count} project(s).";
                }
                else
                {
                    TempData["Info"] = "No eligible projects found. Either milestones already exist or no projects have an approved Final milestone.";
                }

                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFinalEvaluationDeadlines(DateTime? step1to4Deadline, DateTime? step5Deadline, string sessionFilter)
        {
            try 
            {
                if (!step1to4Deadline.HasValue || !step5Deadline.HasValue)
                {
                    TempData["Error"] = "The dates provided were invalid or empty.";
                    return RedirectToAction(nameof(Settings), new { sessionFilter });
                }
                
                if (string.IsNullOrEmpty(sessionFilter))
                {
                    TempData["Error"] = "An academic session must be selected.";
                    return RedirectToAction(nameof(Settings));
                }

                var endOfDay1to4 = new DateTime(step1to4Deadline.Value.Year, step1to4Deadline.Value.Month, step1to4Deadline.Value.Day, 23, 59, 59);
                var endOfDay5 = new DateTime(step5Deadline.Value.Year, step5Deadline.Value.Month, step5Deadline.Value.Day, 23, 59, 59);

                var milestones = await _context.FYPMilestones
                    .Include(m => m.Project)
                    .Where(m => m.Project.AcademicSession == sessionFilter && m.Title.StartsWith("Step"))
                    .ToListAsync();

                int count = 0;
                foreach(var m in milestones)
                {
                    if (m.Title.Contains("Step 5"))
                    {
                        m.DueDate = endOfDay5;
                    }
                    else if (m.Title.Contains("Step 1") || m.Title.Contains("Step 2") || m.Title.Contains("Step 3") || m.Title.Contains("Step 4"))
                    {
                        m.DueDate = endOfDay1to4;
                    }
                    count++;
                }

                if (count > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Successfully updated the Final Evaluation deadlines for {count} milestone(s) in {sessionFilter}.";
                }
                else
                {
                    TempData["Info"] = $"No Final Evaluation steps found in {sessionFilter} to update.";
                }

                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
            catch (Exception)
            {
                TempData["Error"] = "An unexpected error occurred while processing your request. Please check the logs.";
                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
        }

        
        // ============================================================
        // ADMIN - INDEPENDENT GLOBAL SETTINGS SAVE ACTIONS
        // ============================================================

        // Shared helper: update ONE FYPSetting key.
        // The caller is responsible for SaveChangesAsync().
        private async Task SetGlobalSettingAsync(
            string key,
            string value,
            string label)
        {
            var setting = await _context.FYPSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting != null)
            {
                setting.Value = value;
                setting.UpdatedDate = DateTime.Now;
            }
            else
            {
                _context.FYPSettings.Add(
                    new FYPTracker.Models.FYPSetting
                    {
                        Key = key,
                        Value = value,
                        Label = label,
                        UpdatedDate = DateTime.Now
                    });
            }
        }

        // ============================================================
        // POST - SAVE DPP SETTINGS ONLY
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDppSettings(
            string? dppDeadline,
            string? sessionFilter)
        {
            try
            {
                string value = (dppDeadline ?? "").Trim();

                // Empty is permitted to explicitly clear the
                // deadline. The UI asks for confirmation.
                if (!string.IsNullOrEmpty(value) &&
                    !DateTime.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out _))
                {
                    TempData["FYPSettingError"] =
                        "Invalid DPP deadline. Please select a valid date.";

                    return RedirectToAction(
                        nameof(Settings),
                        new { sessionFilter });
                }

                await SetGlobalSettingAsync(
                    "DPPDeadline",
                    value,
                    "DPP Submission Deadline");

                await _context.SaveChangesAsync();

                TempData["FYPSettingSuccess"] =
                    "DPP Settings saved successfully.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                TempData["FYPSettingError"] =
                    "Unable to save DPP Settings.";
            }

            return RedirectToAction(
                nameof(Settings),
                new { sessionFilter });
        }

        // ============================================================
        // POST - SAVE EXHIBITION SETTINGS ONLY
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateExhibitionSettings(
            string? exhibitionDate,
            string? exhibitionTimeStart,
            string? exhibitionTimeEnd,
            string? sessionFilter)
        {
            try
            {
                string dateValue = (exhibitionDate ?? "").Trim();
                string startValue = (exhibitionTimeStart ?? "").Trim();
                string endValue = (exhibitionTimeEnd ?? "").Trim();

                if (!string.IsNullOrEmpty(dateValue) &&
                    !DateTime.TryParseExact(
                        dateValue,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out _))
                {
                    TempData["FYPSettingError"] =
                        "Invalid Exhibition Date.";

                    return RedirectToAction(
                        nameof(Settings),
                        new { sessionFilter });
                }

                string[] timeFormats =
                {
                    "HH:mm",
                    "HH:mm:ss"
                };

                if (!string.IsNullOrEmpty(startValue) &&
                    !DateTime.TryParseExact(
                        startValue,
                        timeFormats,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out _))
                {
                    TempData["FYPSettingError"] =
                        "Invalid Exhibition Start Time.";

                    return RedirectToAction(
                        nameof(Settings),
                        new { sessionFilter });
                }

                if (!string.IsNullOrEmpty(endValue) &&
                    !DateTime.TryParseExact(
                        endValue,
                        timeFormats,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out _))
                {
                    TempData["FYPSettingError"] =
                        "Invalid Exhibition End Time.";

                    return RedirectToAction(
                        nameof(Settings),
                        new { sessionFilter });
                }

                await SetGlobalSettingAsync(
                    "ExhibitionDate",
                    dateValue,
                    "FYP Exhibition Date");

                await SetGlobalSettingAsync(
                    "ExhibitionTimeStart",
                    startValue,
                    "Exhibition Start Time");

                await SetGlobalSettingAsync(
                    "ExhibitionTimeEnd",
                    endValue,
                    "Exhibition End Time");

                await _context.SaveChangesAsync();

                TempData["FYPSettingSuccess"] =
                    "Exhibition Settings saved successfully.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                TempData["FYPSettingError"] =
                    "Unable to save Exhibition Settings.";
            }

            return RedirectToAction(
                nameof(Settings),
                new { sessionFilter });
        }

        // ============================================================
        // POST - SAVE WORKFLOW POLICY ONLY
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateWorkflowPolicySettings(
            int? supervisorRequestTimeoutDays,
            string? sessionFilter)
        {
            if (!supervisorRequestTimeoutDays.HasValue ||
                supervisorRequestTimeoutDays.Value < 1 ||
                supervisorRequestTimeoutDays.Value > 30)
            {
                TempData["FYPSettingError"] =
                    "Supervisor Request Timeout must be between 1 and 30 days.";

                return RedirectToAction(
                    nameof(Settings),
                    new { sessionFilter });
            }

            try
            {
                await SetGlobalSettingAsync(
                    "SupervisorRequestTimeoutDays",
                    supervisorRequestTimeoutDays.Value.ToString(),
                    "Supervisor Request Timeout (Days)");

                await _context.SaveChangesAsync();

                TempData["FYPSettingSuccess"] =
                    "Workflow Policy saved successfully.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                TempData["FYPSettingError"] =
                    "Unable to save Workflow Policy.";
            }

            return RedirectToAction(
                nameof(Settings),
                new { sessionFilter });
        }

        public async Task<IActionResult> ApprovedIafList()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var approvedList = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .Include(m => m.Project)
                    .ThenInclude(p => p.AssignedExaminers)
                        .ThenInclude(e => e.Examiner)
                .Where(m => (m.Title.Contains("IAF") || m.Title.Contains("Initial Project Allocation")) && (m.Status == "PendingPanelReview" || m.Status == "AdminApproved" || m.Status == "DefenseScheduled" || m.Status == "DefenseRequired" || m.Status == "PanelRevisionRequired" || m.Status == "ChangeOfTopicRequired"))
                .ToListAsync();

            return View(approvedList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminFinalApproveIaf(int milestoneId)
        {
            var milestone = await _context.FYPMilestones.FindAsync(milestoneId);
            if (milestone != null)
            {
                milestone.Status = "AdminApproved";
                
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                    var formInfo = parts[0].Trim();
                    try
                    {
                        var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                        var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                        if (currentAttempt != null)
                        {
                            currentAttempt.Status = "AdminApproved";
                            milestone.Remarks = $"{formInfo} JSON: {System.Text.Json.JsonSerializer.Serialize(history)}";
                        }
                    }
                    catch {}
                }
                
                await _context.SaveChangesAsync();
                TempData["Success"] = "Successfully granted final approval for the IAF.";
            }
            return RedirectToAction(nameof(ApprovedIafList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminRejectIaf(int milestoneId, string? panelComments)
        {
            var milestone = await _context.FYPMilestones.Include(m => m.Project).ThenInclude(p => p.Student).FirstOrDefaultAsync(m => m.Id == milestoneId);
            if (milestone != null)
            {
                milestone.Status = "PanelRevisionRequired";

                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                    var formInfo = parts[0].Trim();
                    try
                    {
                        var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                        var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                        if (currentAttempt != null)
                        {
                            currentAttempt.Status = "PanelRevisionRequired";
                            currentAttempt.PanelComments = panelComments;
                            milestone.Remarks = $"{formInfo} JSON: {System.Text.Json.JsonSerializer.Serialize(history)}";
                        }
                    }
                    catch {}
                }

                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = milestone.Project.StudentId,
                    Type = "RevisionRequest",
                    Title = "IAF Revision Required",
                    Message = "The panel has requested revisions for your Initial Allocation Form. Please review the comments and resubmit.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "IAF rejected. Revision is now required.";
            }
            return RedirectToAction(nameof(ApprovedIafList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminScheduleDefense(int milestoneId, DateTime defenseDate, TimeSpan defenseTime, string venue)
        {
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .Include(m => m.Project)
                    .ThenInclude(p => p.AssignedExaminers)
                        .ThenInclude(e => e.Examiner)
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone != null && milestone.Status == "DefenseRequired")
            {
                var formattedDate = defenseDate.ToString("dd MMM yyyy");
                var formattedTime = new DateTime(defenseTime.Ticks).ToString("hh:mm tt");
                
                milestone.Status = "DefenseScheduled";
                milestone.DefenseDate = defenseDate;
                milestone.DefenseTime = formattedTime;
                milestone.DefenseVenue = venue;
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                    var formInfo = parts[0].Trim();
                    try
                    {
                        var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                        var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                        if (currentAttempt != null)
                        {
                            currentAttempt.Status = "DefenseScheduled";
                            milestone.Remarks = $"{formInfo} JSON: {System.Text.Json.JsonSerializer.Serialize(history)}";
                        }
                    }
                    catch {}
                }
                
                var notificationMessage = $"Your IAF requires a defense session.\nDate: {formattedDate}\nTime: {formattedTime}\nVenue: {venue}";
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = milestone.Project.StudentId,
                    Type = "RevisionRequest",
                    Title = "IAF Defense Scheduled",
                    Message = notificationMessage,
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });

                var student = milestone.Project?.Student;
                var supervisor = milestone.Project?.Supervisor;
                var examiner = milestone.Project?.AssignedExaminers?.FirstOrDefault()?.Examiner;

                string subject = "FYP IAF Defense Scheduled";
                string message = $@"<p>An Initial Allocation Form (IAF) defense session has been scheduled.</p>
                    <ul>
                        <li><strong>Student:</strong> {student?.FullName ?? "Unknown"}</li>
                        <li><strong>Date:</strong> {formattedDate}</li>
                        <li><strong>Time:</strong> {formattedTime}</li>
                        <li><strong>Venue/Link:</strong> {venue}</li>
                    </ul>
                    <p>Please ensure your attendance.</p>";
                var request = HttpContext.Request;
                string loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
                string htmlBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Log In to View Details", loginUrl);

                if (student != null && !string.IsNullOrEmpty(student.Email))
                {
                    try { _emailService.Send(student.Email, subject, htmlBody); } catch { }
                }

                if (supervisor != null && !string.IsNullOrEmpty(supervisor.Email))
                {
                    try { _emailService.Send(supervisor.Email, subject, htmlBody); } catch { }
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = supervisor.Id,
                        Type = "Meeting",
                        Title = "Student Defense Scheduled",
                        Message = $"Defense for {student?.FullName} is scheduled on {formattedDate} at {formattedTime}.",
                        IsRead = false,
                        CreatedDate = DateTime.Now
                    });
                }

                if (examiner != null && !string.IsNullOrEmpty(examiner.Email))
                {
                    try { _emailService.Send(examiner.Email, subject, htmlBody); } catch { }
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = examiner.Id,
                        Type = "Meeting",
                        Title = "Examiner Duty: IAF Defense",
                        Message = $"Defense for {student?.FullName} is scheduled on {formattedDate} at {formattedTime}.",
                        IsRead = false,
                        CreatedDate = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Successfully scheduled defense and sent invitations to the student, supervisor, and examiner.";
            }
            return RedirectToAction(nameof(ApprovedIafList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminPassDefense(int milestoneId)
        {
            var milestone = await _context.FYPMilestones.Include(m => m.Project).ThenInclude(p => p.Student).FirstOrDefaultAsync(m => m.Id == milestoneId);
            if (milestone != null)
            {
                milestone.Status = "AdminApproved";
                
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                    var formInfo = parts[0].Trim();
                    try
                    {
                        var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                        var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                        if (currentAttempt != null)
                        {
                            currentAttempt.Status = "AdminApproved";
                            currentAttempt.PanelComments = "Passed Defense Session. Title accepted.";
                            milestone.Remarks = $"{formInfo} JSON: {System.Text.Json.JsonSerializer.Serialize(history)}";
                        }
                    }
                    catch {}
                }

                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = milestone.Project.StudentId,
                    Type = "MilestoneCompleted",
                    Title = "IAF Defense Passed",
                    Message = "Congratulations! Your IAF defense was successful and your title is accepted. You may now proceed to the next stage.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "Successfully recorded defense pass. Project title accepted.";
            }
            return RedirectToAction(nameof(ApprovedIafList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminFailDefense(int milestoneId)
        {
            var milestone = await _context.FYPMilestones.Include(m => m.Project).ThenInclude(p => p.Student).FirstOrDefaultAsync(m => m.Id == milestoneId);
            if (milestone != null)
            {
                milestone.Status = "ChangeOfTopicRequired";
                
                if (!string.IsNullOrEmpty(milestone.Remarks) && milestone.Remarks.Contains("JSON: "))
                {
                    var parts = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                    var formInfo = parts[0].Trim();
                    try
                    {
                        var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                        var currentAttempt = history.Attempts.Find(a => a.AttemptNumber == history.CurrentAttempt);
                        if (currentAttempt != null)
                        {
                            currentAttempt.Status = "ChangeOfTopicRequired";
                            currentAttempt.PanelComments = "Defense Failed. Please submit a new Initial Allocation Form with a change of topic.";
                            milestone.Remarks = $"{formInfo} JSON: {System.Text.Json.JsonSerializer.Serialize(history)}";
                        }
                    }
                    catch {}
                }
                
                milestone.DefenseDate = null;
                milestone.DefenseTime = null;
                milestone.DefenseVenue = null;
                
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = milestone.Project.StudentId,
                    Type = "RevisionRequest",
                    Title = "IAF Defense Failed",
                    Message = "Your IAF defense was unsuccessful. You are required to submit a new Initial Allocation Form with a change of topic.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "Successfully recorded defense failure. The student must now submit a new IAF topic.";
            }
            return RedirectToAction(nameof(ApprovedIafList));
        }

        [HttpPost]
        [Authorize]
        [IgnoreAntiforgeryToken] // Allow AJAX calls easily
        public async Task<IActionResult> CheckBulkEmails([FromBody] List<string> emails)
        {
            if (emails == null || !emails.Any())
                return Json(new Dictionary<string, string>());

            var results = new Dictionary<string, string>();
            
            // Safely extract all possible search terms
            var searchTerms = new List<string>();
            foreach (var email in emails)
            {
                var normEmail = email.Trim().ToLower();
                searchTerms.Add(normEmail);
                if (normEmail.Contains("@"))
                {
                    searchTerms.Add(normEmail.Split('@')[0]);
                }
            }
            
            // Do a massive OR query on the server to grab any potential matches.
            // SQL Server is case-insensitive by default, so we don't need .ToLower() in the query.
            var existingUsers = await _context.ApplicationUsers
                .Where(u => searchTerms.Contains(u.Email) || searchTerms.Contains(u.UserName))
                .ToListAsync();
            
            foreach (var email in emails)
            {
                var normEmail = email.Trim().ToLower();
                var normUsername = normEmail.Contains("@") ? normEmail.Split('@')[0] : normEmail;
                
                // Do the precise case-insensitive match in memory
                var user = existingUsers.FirstOrDefault(u => 
                    (u.Email != null && u.Email.ToLower() == normEmail) || 
                    (u.UserName != null && u.UserName.ToLower() == normUsername) || 
                    (u.UserName != null && u.UserName.ToLower() == normEmail)
                );
                
                if (user != null)
                {
                    if (user.RequirePasswordChange)
                        results[email] = "Pending";
                    else
                        results[email] = "Registered";
                }
            }
            
            return Json(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkInvite(List<string> inviteNames, List<string> inviteEmails, string bulkUserType, List<string> bulkAcademicRoles, string bulkProgramme, string bulkAcademicSession)
        {
            if (inviteEmails == null || !inviteEmails.Any() || string.IsNullOrWhiteSpace(bulkUserType))
            {
                TempData["Error"] = "Please provide emails and select a user type.";
                return RedirectToAction(nameof(ManageUsers));
            }

            // ── Validate required student profile fields before sending credentials ──
            if (bulkUserType == "Student")
            {
                if (string.IsNullOrWhiteSpace(bulkProgramme))
                {
                    TempData["Error"] = "Programme is required before sending login credentials to students.";
                    return RedirectToAction(nameof(ManageUsers));
                }
                if (string.IsNullOrWhiteSpace(bulkAcademicSession))
                {
                    TempData["Error"] = "Academic Session is required before sending login credentials to students.";
                    return RedirectToAction(nameof(ManageUsers));
                }
                
                // Validate that the session is active in the DB
                bool isValidSession = await _context.AcademicSessions.AnyAsync(s => s.Name == bulkAcademicSession && s.IsActive);
                if (!isValidSession)
                {
                    TempData["Error"] = $"Academic Session '{bulkAcademicSession}' is invalid or inactive.";
                    return RedirectToAction(nameof(ManageUsers));
                }
            }

            var invites = new List<(string Name, string Email)>();
            var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

            for (int i = 0; i < inviteEmails.Count; i++)
            {
                var email = inviteEmails[i]?.Trim();
                if (!string.IsNullOrEmpty(email) && emailRegex.IsMatch(email))
                {
                    string name = "";
                    if (inviteNames != null && i < inviteNames.Count && !string.IsNullOrWhiteSpace(inviteNames[i]))
                    {
                        name = inviteNames[i].Trim();
                    }
                    invites.Add((name, email));
                }
            }

            invites = invites.GroupBy(x => x.Email).Select(g => g.First()).ToList();

            if (!invites.Any())
            {
                TempData["Error"] = "No valid emails found.";
                return RedirectToAction(nameof(ManageUsers));
            }

            int successCount = 0;
            int duplicateCount = 0;
            
            string prefix = bulkUserType == "Examiner" ? "EXM-" : "SUP-";
            var maxNums = await _context.ApplicationUsers
                .Where(u => u.UserName != null && u.UserName.StartsWith(prefix))
                .Select(u => u.UserName.Substring(prefix.Length))
                .ToListAsync();

            int nextIdNum = 1;
            var validNums = maxNums.Select(n => int.TryParse(n, out int num) ? num : 0);
            if (validNums.Any())
            {
                nextIdNum = validNums.Max() + 1;
            }

            var request = HttpContext.Request;
            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";

            // Pre-fetch existing users for precise check
            var searchTerms = new List<string>();
            foreach (var inv in invites)
            {
                var normEmail = inv.Email.Trim().ToLower();
                searchTerms.Add(normEmail);
                if (normEmail.Contains("@")) searchTerms.Add(normEmail.Split('@')[0]);
            }
            var existingUsers = await _context.ApplicationUsers
                .Where(u => searchTerms.Contains(u.Email) || searchTerms.Contains(u.UserName))
                .ToListAsync();

            foreach (var invite in invites)
            {
                var email = invite.Email.Trim().ToLower(); // Domain is case-insensitive, we normalize early.
                var username = email.Split('@')[0];
                
                // Server-side format validation to prevent frontend bypass
                bool isValidFormat = true;
                if (bulkUserType == "Student") {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^p\d{8}$")) isValidFormat = false;
                } else {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9.-]+$")) isValidFormat = false;
                }
                
                if (!isValidFormat) continue; // Skip entirely, it will not count towards success
                
                // Precise case-insensitive match in memory
                var existingUser = existingUsers.FirstOrDefault(u => 
                    (u.Email != null && u.Email.ToLower() == email) || 
                    (u.UserName != null && u.UserName.ToLower() == username) ||
                    (u.UserName != null && u.UserName.ToLower() == email)
                );
                
                if (existingUser != null)
                {
                    duplicateCount++;
                    continue;
                }

                string newId;
                if (email.EndsWith("@student.newinti.edu.my", StringComparison.OrdinalIgnoreCase) || 
                    email.EndsWith("@newinti.edu.my", StringComparison.OrdinalIgnoreCase))
                {
                    newId = email.Split('@')[0];
                }
                else if (bulkUserType == "Student")
                {
                    var random = new Random();
                    string randomId;
                    do
                    {
                        randomId = "P" + random.Next(10000000, 99999999).ToString();
                    } while (await _context.ApplicationUsers.AnyAsync(u => u.UserName == randomId || u.EmployeeId == randomId));
                    newId = randomId;
                }
                else
                {
                    newId = $"{prefix}{nextIdNum:D3}";
                    nextIdNum++;
                }

                // ── Generate a unique temp password ──
                string tempPwd = GenerateTempPassword();

                var newUser = new ApplicationUser
                {
                    UserName = newId,
                    FullName = !string.IsNullOrWhiteSpace(invite.Name) ? invite.Name : email.Split('@')[0],
                    Email = email,
                    PassCode = FYPTracker.Common.Encryption.GetHash(tempPwd),
                    TempPassword = tempPwd,
                    EmployeeId = newId,
                    DateCreated = DateTime.Now,
                    DateJoin = DateTime.Now,
                    AddedBy = "sys",
                    Image = "/assets/media/avatars/blank.png",
                    RequirePasswordChange = true, // Forces onboarding flow
                    // Store student-specific profile fields supplied by admin
                    Programme = bulkUserType == "Student" ? bulkProgramme : null,
                    AcademicSession = bulkUserType == "Student" ? bulkAcademicSession : null,
                    AccountExpiry = bulkUserType == "Student" ? CalculateAccountExpiry(bulkAcademicSession) : null
                };

                _context.ApplicationUsers.Add(newUser);
                await _context.SaveChangesAsync(); // save so we have newUser.Id for the log

                var rolesToAddNew = bulkUserType == "AcademicStaff" ? bulkAcademicRoles.ToArray() : new[] { bulkUserType };
                foreach (var r in rolesToAddNew)
                {
                    var bulkRoleEntity = await _context.AppRoles.FirstOrDefaultAsync(ar => ar.Name == r);
                    if (bulkRoleEntity != null)
                    {
                        _context.UserRoleMappings.Add(new UserRoleMapping { UserId = newUser.Id, RoleId = bulkRoleEntity.Id });
                    }
                }
                await _context.SaveChangesAsync();



                successCount++;

                // ── Prepare Email ──
                string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                string message = $@"<p>Your account has been successfully created by the administrator. Below are your login credentials:</p>
                    <ul>
                        <li><strong>Username:</strong> {newId}</li>
                        <li><strong>Email:</strong> {email}</li>
                        <li><strong>Temporary Password:</strong> {tempPwd}</li>
                    </ul>
                    <p>Upon your first login, you will be required to set up your profile and change your password.</p>";
                var httpRequest = HttpContext.Request;
                string emailLoginUrl = $"{httpRequest.Scheme}://{httpRequest.Host}/User/Login";
                string htmlBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Click Here to Login", emailLoginUrl);
                try
                {
                    _emailService.Send(email, subject, htmlBody);
                }
                catch { /* Ignore email failures during bulk insert */ }
            }

            if (successCount > 0)
            {
                TempData["Success"] = $"Successfully invited {successCount} users. {duplicateCount} duplicates ignored.";
            }
            else
            {
                TempData["Info"] = $"No new users created. {duplicateCount} emails were already registered.";
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        // ────────────────────────────────────────────────────────
        // Resend Invite
        // ────────────────────────────────────────────────────────
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ResendInvite(int userId)
        {
            var admin = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (admin == null) return Json(new { success = false, message = "Unauthorized." });

            var user = await _context.ApplicationUsers.FindAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found." });

            if (!user.RequirePasswordChange)
                return Json(new { success = false, message = "This user has already activated their account." });

            // Generate a fresh temp password
            string newTempPwd = GenerateTempPassword();
            user.PassCode = FYPTracker.Common.Encryption.GetHash(newTempPwd);
            user.TempPassword = newTempPwd;



            await _context.SaveChangesAsync();

            // Resend email
            var request = HttpContext.Request;
            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
            string subject = "FYP Tracking System - Your Updated Login Credentials";
            string message = $@"<p>Dear <strong>{user.FullName}</strong>,</p><br/>
                <p>Your login credentials have been refreshed by the administrator. Please use the details below to log in:</p>
                <ul>
                    <li><strong>Username:</strong> {user.UserName}</li>
                    <li><strong>Email:</strong> {user.Email}</li>
                    <li><strong>New Temporary Password:</strong> {newTempPwd}</li>
                </ul>
                <p>You will be required to change your password upon first login.</p>";
            string htmlBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Click Here to Login", loginUrl);

            try { _emailService.Send(user.Email!, subject, htmlBody); }
            catch { /* email failure is non-fatal */ }

            return Json(new { success = true, message = $"Credentials resent to {user.Email}." });
        }

        // ────────────────────────────────────────────────────────
        // CSV Bulk Upload
        // ────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadCSV(IFormFile csvFile, string csvUserType, string csvProgramme, string csvAcademicSession)
        {
            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["Error"] = "Please select a CSV file to upload.";
                return RedirectToAction(nameof(ManageUsers));
            }

            var ext = Path.GetExtension(csvFile.FileName).ToLowerInvariant();
            if (ext != ".csv")
            {
                TempData["Error"] = "Only .csv files are supported.";
                return RedirectToAction(nameof(ManageUsers));
            }

            if (string.IsNullOrWhiteSpace(csvUserType))
            {
                TempData["Error"] = "Please select a User Type.";
                return RedirectToAction(nameof(ManageUsers));
            }

            if (csvUserType == "Student")
            {
                if (string.IsNullOrWhiteSpace(csvProgramme))
                { TempData["Error"] = "Programme is required for student CSV uploads."; return RedirectToAction(nameof(ManageUsers)); }
                if (string.IsNullOrWhiteSpace(csvAcademicSession))
                { TempData["Error"] = "Academic Session is required for student CSV uploads."; return RedirectToAction(nameof(ManageUsers)); }
                
                // Validate session exists and is active
                bool isValidSession = await _context.AcademicSessions.AnyAsync(s => s.Name == csvAcademicSession && s.IsActive);
                if (!isValidSession)
                {
                    TempData["Error"] = $"Academic Session '{csvAcademicSession}' is invalid or inactive.";
                    return RedirectToAction(nameof(ManageUsers));
                }
            }

            var admin = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            var request = HttpContext.Request;
            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";

            int successCount = 0, dupCount = 0, errorCount = 0;

            string prefix = csvUserType == "Examiner" ? "EXM-" : "SUP-";
            var maxNums = await _context.ApplicationUsers
                .Where(u => u.UserName != null && u.UserName.StartsWith(prefix))
                .Select(u => u.UserName!.Substring(prefix.Length))
                .ToListAsync();
            int nextIdNum = maxNums.Select(n => int.TryParse(n, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1;

            using var reader = new System.IO.StreamReader(csvFile.OpenReadStream());
            bool isFirstLine = true;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Skip header row
                if (isFirstLine) { isFirstLine = false; continue; }

                var cols = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
                // Expected: Full Name, Email  (min 2 columns)
                if (cols.Length < 2) { errorCount++; continue; }

                string name  = cols[0];
                string email = cols[1];

                if (!emailRegex.IsMatch(email)) { errorCount++; continue; }

                var existingUser = await _context.ApplicationUsers
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == email || u.UserName == email);
                    
                if (existingUser != null)
                {
                    dupCount++;
                    continue;
                }

                string newId;
                if (email.EndsWith("@student.newinti.edu.my", StringComparison.OrdinalIgnoreCase) ||
                    email.EndsWith("@newinti.edu.my", StringComparison.OrdinalIgnoreCase))
                {
                    newId = email.Split('@')[0];
                }
                else if (csvUserType == "Student")
                {
                    var rng = new Random();
                    string rid;
                    do { rid = "P" + rng.Next(10000000, 99999999); }
                    while (await _context.ApplicationUsers.AnyAsync(u => u.UserName == rid || u.EmployeeId == rid));
                    newId = rid;
                }
                else
                {
                    newId = $"{prefix}{nextIdNum:D3}";
                    nextIdNum++;
                }

                string tempPwd = GenerateTempPassword();
                var newUser = new ApplicationUser
                {
                    UserName = newId,
                    FullName = !string.IsNullOrWhiteSpace(name) ? name : email.Split('@')[0],
                    Email = email,
                    PassCode = FYPTracker.Common.Encryption.GetHash(tempPwd),
                    TempPassword = tempPwd,
                    EmployeeId = newId,
                    DateCreated = DateTime.Now,
                    DateJoin = DateTime.Now,
                    AddedBy = "sys",
                    Image = "/assets/media/avatars/blank.png",
                    RequirePasswordChange = true,
                    Programme = csvUserType == "Student" ? csvProgramme : null,
                    AcademicSession = csvUserType == "Student" ? csvAcademicSession : null,
                    AccountExpiry = csvUserType == "Student" ? CalculateAccountExpiry(csvAcademicSession) : null
                };
                _context.ApplicationUsers.Add(newUser);
                await _context.SaveChangesAsync();

                var rolesToAddNew = csvUserType == "AcademicStaff" ? new[] { "Supervisor", "Examiner" } : new[] { csvUserType };
                foreach (var r in rolesToAddNew)
                {
                    var csvRoleEntity = await _context.AppRoles.FirstOrDefaultAsync(ar => ar.Name == r);
                    if (csvRoleEntity != null)
                    {
                        _context.UserRoleMappings.Add(new UserRoleMapping { UserId = newUser.Id, RoleId = csvRoleEntity.Id });
                    }
                }
                await _context.SaveChangesAsync();



                // Send welcome email
                try
                {
                    string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                    string message = $@"<p>Your account has been created. Here are your login credentials:</p>
<ul><li><strong>Username:</strong> {newId}</li><li><strong>Email:</strong> {email}</li><li><strong>Temporary Password:</strong> {tempPwd}</li></ul>
<p>You will be asked to change your password on first login.</p>";
                    string htmlBody = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message, "Click Here to Login", loginUrl);
                    _emailService.Send(email, subject, htmlBody);
                }
                catch { }

                successCount++;
            }

            if (successCount > 0)
                TempData["Success"] = $"CSV imported: {successCount} user(s) created. {dupCount} duplicate(s) skipped. {errorCount} invalid row(s) ignored.";
            else
                TempData["Info"] = $"No new users created from CSV. {dupCount} duplicate(s), {errorCount} invalid row(s).";

            return RedirectToAction(nameof(ManageUsers));
        }

        // ────────────────────────────────────────────────────────
        // Reporting (Admin)
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Reporting(string sessionFilter)
        {
            var user = await _context.ApplicationUsers
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null || (!user.UserRoles.Any(r => r.Role.Name == "Admin") && !user.UserRoles.Any(r => r.Role.Name == "SuperAdmin"))) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            // Fetch distinct sessions
            var distinctSessions = await _context.ApplicationUsers
                .Where(u => !string.IsNullOrEmpty(u.AcademicSession))
                .Select(u => u.AcademicSession)
                .Distinct()
                .OrderByDescending(s => s)
                .ToListAsync();

            ViewBag.DistinctSessions = distinctSessions;
            ViewBag.CurrentSessionFilter = sessionFilter;

            // 1. Supervisor Capacity vs Workload
            var supervisors = await _context.ApplicationUsers
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Supervisor") && !u.IsResign)
                .Select(s => new {
                    Name = s.FullName ?? s.UserName,
                    MaxSlots = s.MaxStudentSlots ?? 5,
                    CurrentStudents = _context.FYPProjects.Count(p => p.SupervisorId == s.Id && !p.Student.IsResign && p.Status == "Approved" && (string.IsNullOrEmpty(sessionFilter) || p.Student.AcademicSession == sessionFilter))
                })
                .ToListAsync();

            // 2. Global Student Allocation Status
            var studentsQuery = _context.ApplicationUsers
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Student") && !u.IsResign);

            if (!string.IsNullOrEmpty(sessionFilter))
            {
                studentsQuery = studentsQuery.Where(u => u.AcademicSession == sessionFilter);
            }

            var allActiveStudents = await studentsQuery
                .Select(s => new {
                    s.Id,
                    HasApprovedProject = _context.FYPProjects.Any(p => p.StudentId == s.Id && p.Status == "Approved"),
                    HasPendingProject = _context.FYPProjects.Any(p => p.StudentId == s.Id && p.Status == "Pending")
                })
                .ToListAsync();

            var allocationStatusCounts = new[] {
                new { Status = "Allocated", Count = allActiveStudents.Count(s => s.HasApprovedProject) },
                new { Status = "Pending", Count = allActiveStudents.Count(s => !s.HasApprovedProject && s.HasPendingProject) },
                new { Status = "No Project", Count = allActiveStudents.Count(s => !s.HasApprovedProject && !s.HasPendingProject) }
            };

            // 3. Faculty Demographics / Program Distribution
            var programCountsQuery = _context.ApplicationUsers
                .Where(u => u.UserRoles.Any(r => r.Role.Name == "Student") && !u.IsResign && !string.IsNullOrEmpty(u.Programme));

            if (!string.IsNullOrEmpty(sessionFilter))
            {
                programCountsQuery = programCountsQuery.Where(u => u.AcademicSession == sessionFilter);
            }

            var programCounts = await programCountsQuery
                .GroupBy(u => u.Programme)
                .Select(g => new { Program = g.Key, Count = g.Count() })
                .ToListAsync();

            // 4. Global Milestone Bottlenecks
            var milestonesQuery = _context.FYPMilestones
                .Where(m => !m.Project.Student.IsResign);

            if (!string.IsNullOrEmpty(sessionFilter))
            {
                milestonesQuery = milestonesQuery.Where(m => m.Project.Student.AcademicSession == sessionFilter);
            }

            var milestones = await milestonesQuery
                .Select(m => new { m.Title, m.Status })
                .ToListAsync();

            var milestoneStatusData = milestones
                .Select(m => new {
                    Category = m.Title.Contains("IAF") || m.Title.Contains("Initial") ? "IAF" :
                               m.Title.Contains("Stage 1 Final Submission") ? "Proposal" :
                               m.Title.Contains("Final") ? "Final Report" : "Other",
                    m.Status
                })
                .Where(m => m.Category != "Other")
                .GroupBy(m => new { m.Category, m.Status })
                .Select(g => new { g.Key.Category, g.Key.Status, Count = g.Count() })
                .ToList();

            ViewBag.SupervisorCapacityJson = System.Text.Json.JsonSerializer.Serialize(supervisors);
            ViewBag.AllocationStatsJson = System.Text.Json.JsonSerializer.Serialize(allocationStatusCounts);
            ViewBag.ProgramStatsJson = System.Text.Json.JsonSerializer.Serialize(programCounts);
            ViewBag.MilestoneBottlenecksJson = System.Text.Json.JsonSerializer.Serialize(milestoneStatusData);

            return View();
        }

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ManageLookups()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;
            return View();
        }

        [HttpPost]
        public IActionResult LookupData(string type)
        {
            try
            {
                if (type == "Department")
                {
                    var data = _context.Department.Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive }).ToList();
                    return Json(new { data = data });
                }
                else if (type == "Programme")
                {
                    var data = _context.Programme.Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive }).ToList();
                    return Json(new { data = data });
                }
                else if (type == "Specialization")
                {
                    var data = _context.Specialization.Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive }).ToList();
                    return Json(new { data = data });
                }
                else if (type == "AcademicSession")
                {
                    var data = _context.AcademicSessions.Select(x => new { id = x.Id, name = x.Name, isActive = x.IsActive, startDate = x.StartDate, endDate = x.EndDate, isCurrent = x.IsCurrent }).ToList();
                    return Json(new { data = data });
                }
                return Json(new { data = new List<object>() });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddLookup(string type, string name, DateTime? startDate = null, DateTime? endDate = null, bool isCurrent = false, bool isActive = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return Json(new { status = false, msg = "Name cannot be empty." });

                if (type == "Department")
                {
                    if (_context.Department.Any(x => x.Name.ToLower() == name.ToLower())) return Json(new { status = false, msg = "Exists!" });
                    _context.Department.Add(new Department { Name = name, IsActive = true });
                }
                else if (type == "Programme")
                {
                    if (_context.Programme.Any(x => x.Name.ToLower() == name.ToLower())) return Json(new { status = false, msg = "Exists!" });
                    _context.Programme.Add(new Programme { Name = name, IsActive = true });
                }
                else if (type == "Specialization")
                {
                    if (_context.Specialization.Any(x => x.Name.ToLower() == name.ToLower())) return Json(new { status = false, msg = "Exists!" });
                    _context.Specialization.Add(new Specialization { Name = name, IsActive = true });
                }
                else if (type == "AcademicSession")
                {
                    if (startDate.HasValue && endDate.HasValue && endDate <= startDate) return Json(new { status = false, msg = "End Date must be after Start Date." });
                    if (_context.AcademicSessions.Any(x => x.Name.ToLower() == name.ToLower())) return Json(new { status = false, msg = "Exists!" });
                    
                    if (isCurrent)
                    {
                        var currents = _context.AcademicSessions.Where(s => s.IsCurrent == true).ToList();
                        foreach (var curr in currents) curr.IsCurrent = false;
                    }
                    
                    _context.AcademicSessions.Add(new AcademicSession { Name = name, IsActive = true, StartDate = startDate, EndDate = endDate, IsCurrent = isCurrent });
                }
                _context.SaveChanges();
                return Json(new { status = true });
            }
            catch(Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditLookup(string type, int id, string name, DateTime? startDate = null, DateTime? endDate = null, bool isCurrent = false, bool isActive = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return Json(new { status = false, msg = "Name cannot be empty." });

                if (type == "Department")
                {
                    var entity = _context.Department.Find(id);
                    if (entity != null) { entity.Name = name; entity.IsActive = isActive; _context.SaveChanges(); }
                }
                else if (type == "Programme")
                {
                    var entity = _context.Programme.Find(id);
                    if (entity != null) { entity.Name = name; entity.IsActive = isActive; _context.SaveChanges(); }
                }
                else if (type == "Specialization")
                {
                    var entity = _context.Specialization.Find(id);
                    if (entity != null) { entity.Name = name; entity.IsActive = isActive; _context.SaveChanges(); }
                }
                else if (type == "AcademicSession")
                {
                    if (startDate.HasValue && endDate.HasValue && endDate <= startDate) return Json(new { status = false, msg = "End Date must be after Start Date." });
                    var entity = _context.AcademicSessions.Find(id);
                    if (entity != null) 
                    { 
                        if (isCurrent && entity.IsCurrent != true)
                        {
                            var currents = _context.AcademicSessions.Where(s => s.IsCurrent == true && s.Id != id).ToList();
                            foreach (var curr in currents) curr.IsCurrent = false;
                        }
                        
                        entity.Name = name; 
                        entity.IsActive = isActive; 
                        entity.StartDate = startDate; 
                        entity.EndDate = endDate; 
                        entity.IsCurrent = isCurrent; 
                        _context.SaveChanges(); 
                    }
                }
                return Json(new { status = true });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TransferStudents(int fromSupervisorId, int toSupervisorId)
        {
            if (fromSupervisorId == 0 || toSupervisorId == 0 || fromSupervisorId == toSupervisorId)
            {
                TempData["Error"] = "Invalid supervisor selection.";
                return RedirectToAction(nameof(ManageUsers));
            }

            try
            {
                // Transfer from SupervisorRequests
                var requests = await _context.SupervisorRequests
                    .Where(r => r.SupervisorId == fromSupervisorId && (r.Status == "Approved" || r.Status == "Pending"))
                    .ToListAsync();
                foreach(var req in requests)
                {
                    req.SupervisorId = toSupervisorId;
                }

                // Transfer from FYPProjects
                var projects = await _context.FYPProjects
                    .Where(p => p.SupervisorId == fromSupervisorId && p.Status != "Completed")
                    .ToListAsync();
                foreach (var p in projects)
                {
                    p.SupervisorId = toSupervisorId;
                }
                
                // Transfer active meeting bookings (Pending or Approved)
                var meetings = await _context.MeetingBookings
                    .Where(m => m.SupervisorId == fromSupervisorId && (m.Status == "Pending" || m.Status == "Approved"))
                    .ToListAsync();
                foreach (var m in meetings)
                {
                    m.SupervisorId = toSupervisorId;
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Successfully transferred {projects.Count} projects and {requests.Count} requests to the new supervisor.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred during transfer: " + ex.Message;
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleLookupStatus(string type, int id)
        {
            try
            {
                if (type == "Department")
                {
                    var entity = _context.Department.Find(id);
                    if (entity != null) { entity.IsActive = !entity.IsActive; _context.SaveChanges(); }
                }
                else if (type == "Programme")
                {
                    var entity = _context.Programme.Find(id);
                    if (entity != null) { entity.IsActive = !entity.IsActive; _context.SaveChanges(); }
                }
                else if (type == "Specialization")
                {
                    var entity = _context.Specialization.Find(id);
                    if (entity != null) { entity.IsActive = !entity.IsActive; _context.SaveChanges(); }
                }
                else if (type == "AcademicSession")
                {
                    var entity = _context.AcademicSessions.Find(id);
                    if (entity != null) { entity.IsActive = !entity.IsActive; _context.SaveChanges(); }
                }
                return Json(new { status = true });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        // === Default Milestone Template Management ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDefaultMilestoneTemplate(DefaultMilestoneTemplate model, string sessionFilter)
        {
            if (ModelState.IsValid)
            {
                var sessionObj = await _context.AcademicSessions.FirstOrDefaultAsync(s => s.Name == sessionFilter);
                if (sessionObj != null)
                {
                    model.AcademicSessionId = sessionObj.Id;

                    bool exists = await _context.DefaultMilestoneTemplates.AnyAsync(t => 
                        t.AcademicSessionId == model.AcademicSessionId && 
                        t.Stage == model.Stage &&
                        ((t.MilestoneCode == model.MilestoneCode && (model.MilestoneCode == "IAF" || model.MilestoneCode == "STAGE1_FINAL")) || 
                         (t.Title == model.Title))
                    );

                    if (exists)
                    {
                        TempData["Error"] = "A template with this Milestone Code (if IAF/STAGE1_FINAL) or Title already exists for this stage and session.";
                        return RedirectToAction(nameof(Settings), new { sessionFilter });
                    }

                    _context.DefaultMilestoneTemplates.Add(model);
                    await _context.SaveChangesAsync();

                    if (model.IsActive && model.Stage == 1)
                    {
                        var projects = await _context.FYPProjects
                            .Include(p => p.Milestones)
                            .Where(p => p.AcademicSession == sessionFilter && p.Status != "Failed" && p.Status != "Completed")
                            .ToListAsync();

                        var newMilestones = projects
                            .Where(p => !p.Milestones.Any(m => m.DefaultMilestoneTemplateId == model.Id))
                            .Select(p => new FYPMilestone
                            {
                                FYPProjectId = p.Id,
                                Title = model.Title,
                                Description = model.Description,
                                DueDate = model.DueDate,
                                Status = "Pending",
                                MilestoneSource = "Default",
                                MilestoneCode = model.MilestoneCode,
                                Stage = model.Stage,
                                IsRequired = model.IsRequired,
                                DefaultMilestoneTemplateId = model.Id
                            }).ToList();

                        if (newMilestones.Any())
                        {
                            _context.FYPMilestones.AddRange(newMilestones);
                            await _context.SaveChangesAsync();
                        }
                        await _context.SaveChangesAsync();
                    }

                    TempData["Success"] = "Default Milestone added successfully.";
                }
            }
            return RedirectToAction(nameof(Settings), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDefaultMilestoneTemplate(int id, DefaultMilestoneTemplate model, string sessionFilter)
        {
            var existing = await _context.DefaultMilestoneTemplates.FindAsync(id);
            if (existing != null)
            {
                existing.Title = model.Title;
                existing.MilestoneCode = model.MilestoneCode;
                existing.Stage = model.Stage;
                existing.IsRequired = model.IsRequired;
                existing.DueDate = model.DueDate;
                existing.DisplayOrder = model.DisplayOrder;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Template updated successfully.";
            }
            return RedirectToAction(nameof(Settings), new { sessionFilter });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDefaultMilestoneTemplate(int id, string sessionFilter)
        {
            var existing = await _context.DefaultMilestoneTemplates
                .Include(t => t.Session)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (existing != null)
            {
                existing.IsActive = !existing.IsActive;
                await _context.SaveChangesAsync();
                
                if (existing.IsActive && existing.Stage == 1 && existing.Session != null)
                {
                    var projects = await _context.FYPProjects
                        .Include(p => p.Milestones)
                        .Where(p => p.AcademicSession == existing.Session.Name && p.Status != "Failed" && p.Status != "Completed")
                        .ToListAsync();

                    var newMilestones = projects
                        .Where(p => !p.Milestones.Any(m => m.DefaultMilestoneTemplateId == existing.Id))
                        .Select(p => new FYPMilestone
                        {
                            FYPProjectId = p.Id,
                            Title = existing.Title,
                            Description = existing.Description,
                            DueDate = existing.DueDate,
                            Status = "Pending",
                            MilestoneSource = "Default",
                            MilestoneCode = existing.MilestoneCode,
                            Stage = existing.Stage,
                            IsRequired = existing.IsRequired,
                            DefaultMilestoneTemplateId = existing.Id
                        }).ToList();

                    if (newMilestones.Any())
                    {
                        _context.FYPMilestones.AddRange(newMilestones);
                        await _context.SaveChangesAsync();
                    }
                }
                TempData["Success"] = $"Template {(existing.IsActive ? "activated" : "deactivated")}.";
            }
            return RedirectToAction(nameof(Settings), new { sessionFilter });
        }
    }
}




