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

        public ExaminerController(FYPTrackerContext context, IWebHostEnvironment env, IEmailService emailService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _env = env;
            _emailService = emailService;
            _hubContext = hubContext;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Seed()
        {
            try
            {
                // Create dummy examiner if not exists
                var examiner = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "EXM-001");
                if (examiner == null)
                {
                    examiner = new ApplicationUser { UserName = "EXM-001", Email = "examiner@test.com", FullName = "Dr. Dummy Examiner", UserType = "Examiner", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(examiner);
                    await _context.SaveChangesAsync();
                }

                // Create dummy supervisor if not exists
                var supervisor = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "SUP-001");
                if (supervisor == null)
                {
                    supervisor = new ApplicationUser { UserName = "SUP-001", Email = "super@test.com", FullName = "Dr. Super Visor", UserType = "Supervisor", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(supervisor);
                    await _context.SaveChangesAsync();
                }

                // Create dummy student if not exists
                var student = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "STU-001");
                if (student == null)
                {
                    student = new ApplicationUser { UserName = "STU-001", Email = "student@test.com", FullName = "Johnny Student", EmployeeId = "S123456", UserType = "Student", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false };
                    _context.ApplicationUsers.Add(student);
                    await _context.SaveChangesAsync();
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

        public async Task<IActionResult> Index()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null || user.UserType != "Examiner") return RedirectToAction("Login", "User");

            ViewBag.CurrentUser = user;

            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Where(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id))
                .ToListAsync();

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

            // Get meeting logs
            var meetingLogs = await _context.MeetingBookings
                .Include(m => m.MeetingLog)
                .Where(m => m.StudentId == project.StudentId && m.SupervisorId == project.SupervisorId && m.Status == "Approved" && m.MeetingLog != null)
                .Select(m => m.MeetingLog)
                .OrderByDescending(log => log.CreatedDate)
                .ToListAsync();

            ViewBag.MeetingLogs = meetingLogs;

            string idempotencyToken = Guid.NewGuid().ToString();
            TempData["IdempotencyToken"] = idempotencyToken;
            ViewBag.IdempotencyToken = idempotencyToken;

            return View(project);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMarksheetStage1(int projectId, IFormFile marksheetFile, string idempotencyToken)
        {
            if (string.IsNullOrEmpty(idempotencyToken) || TempData["IdempotencyToken"]?.ToString() != idempotencyToken)
            {
                TempData["Error"] = "Duplicate submission detected. Your request has already been processed.";
                return RedirectToAction(nameof(Index));
            }
            TempData.Remove("IdempotencyToken");

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

            if (project.Stage1MarksheetUploadCount >= 5)
            {
                TempData["Error"] = "You have reached the maximum allowed number of Stage 1 marksheet uploads (5) for this project.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            if (marksheetFile != null && marksheetFile.Length > 0)
            {
                var extension = Path.GetExtension(marksheetFile.FileName).ToLower();
                if (extension != ".xls" && extension != ".xlsx")
                {
                    TempData["Error"] = "Only Excel files (.xls, .xlsx) are allowed.";
                    return RedirectToAction(nameof(StudentDetails), new { projectId });
                }

                project.Stage1MarksheetUploadCount++;

                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "marksheets");
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

                // Keep the Proposal milestone up to date for Examiner's end
                var proposalMilestone = project.Milestones.FirstOrDefault(m => m.Title.Contains("Proposal", StringComparison.OrdinalIgnoreCase));
                
                // Extract final grade from J16 for Stage 1
                try
                {
                    using (var workbook = new XLWorkbook(filePath))
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
                                    TempData["Error"] = "Invalid Marksheet: Could not extract a valid numeric score. Please ensure you are using the predefined template and have filled in the marks.";
                                    System.IO.File.Delete(filePath);
                                    project.Stage1MarksheetUploadCount--;
                                    return RedirectToAction(nameof(StudentDetails), new { projectId });
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
                                proposalMilestone.ExaminerMarksheetData = System.Text.Json.JsonSerializer.Serialize(feedbackData);
                                
                                // Calculate Joint Evaluation
                                double exScore = 0;
                                if (!string.IsNullOrEmpty(proposalMilestone.ExaminerMarksheetData)) {
                                    if (proposalMilestone.ExaminerMarksheetData.StartsWith("{")) {
                                        using var doc = System.Text.Json.JsonDocument.Parse(proposalMilestone.ExaminerMarksheetData);
                                        if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double e)) exScore = e;
                                    } else {
                                        double.TryParse(proposalMilestone.ExaminerMarksheetData, out exScore);
                                    }
                                }

                                double supScore = 0;
                                bool hasSupScore = false;
                                if (!string.IsNullOrEmpty(proposalMilestone.SupervisorMarksheetData)) {
                                    if (proposalMilestone.SupervisorMarksheetData.StartsWith("{")) {
                                        using var doc = System.Text.Json.JsonDocument.Parse(proposalMilestone.SupervisorMarksheetData);
                                        if (doc.RootElement.TryGetProperty("Score", out var prop) && double.TryParse(prop.GetString(), out double s)) { supScore = s; hasSupScore = true; }
                                    } else {
                                        hasSupScore = double.TryParse(proposalMilestone.SupervisorMarksheetData, out supScore);
                                    }
                                }

                                if (exScore > 0)
                                {
                                    if (hasSupScore)
                                    {
                                        project.Stage1TotalScore = (supScore + exScore) / 2.0;
                                        project.Stage1Grade = project.Stage1TotalScore >= 40.0 ? "Pass" : "Fail";
                                    }
                                    else
                                    {
                                        project.Stage1TotalScore = exScore;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Invalid Marksheet: The uploaded Excel file is either corrupted or not the predefined template.";
                    System.IO.File.Delete(filePath);
                    project.Stage1MarksheetUploadCount--;
                    return RedirectToAction(nameof(StudentDetails), new { projectId });
                }

                project.Stage1ExaminerMarksheetPath = "/uploads/marksheets/" + uniqueFileName;

                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Stage 1 marksheet uploaded successfully" + (project.Stage1TotalScore.HasValue ? $" and score {project.Stage1TotalScore} recorded." : ".");
            }
            else
            {
                TempData["Error"] = "Please select a file to upload.";
            }

            return RedirectToAction(nameof(StudentDetails), new { projectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMarksheet(int projectId, IFormFile marksheetFile, string idempotencyToken)
        {
            if (string.IsNullOrEmpty(idempotencyToken) || TempData["IdempotencyToken"]?.ToString() != idempotencyToken)
            {
                TempData["Error"] = "Duplicate submission detected. Your request has already been processed.";
                return RedirectToAction(nameof(Index));
            }
            TempData.Remove("IdempotencyToken"); // Consume token

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

            var stage2Milestones = project.Milestones.Where(m => m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase)).ToList();
            bool isAllStage2Approved = stage2Milestones.Any() && stage2Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!isAllStage2Approved)
            {
                TempData["Error"] = "The supervisor must approve ALL Stage 2 milestones (including the Final Report and Turnitin Score) before you can submit the final Excel marksheet.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            if (project.MarksheetUploadCount >= 5)
            {
                TempData["Error"] = "You have reached the maximum allowed number of marksheet uploads (5) for this project.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            if (marksheetFile != null && marksheetFile.Length > 0)
            {
                var extension = Path.GetExtension(marksheetFile.FileName).ToLower();
                if (extension != ".xls" && extension != ".xlsx")
                {
                    TempData["Error"] = "Only Excel files (.xls, .xlsx) are allowed.";
                    return RedirectToAction(nameof(StudentDetails), new { projectId });
                }

                project.MarksheetUploadCount++;

                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "marksheets");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(marksheetFile.FileName);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await marksheetFile.CopyToAsync(fileStream);
                }

                // Extract final grade from J16
                try
                {
                    using (var workbook = new XLWorkbook(filePath))
                    {
                        var worksheet = workbook.Worksheets.FirstOrDefault();
                        if (worksheet != null)
                        {
                            var cellValue = worksheet.Cell("J16").Value;
                            if (cellValue != null)
                            {
                                string stringValue = cellValue.ToString();
                                if (double.TryParse(stringValue, out double numericScore))
                                {
                                    project.FinalGrade = GetGradeLabel(numericScore);
                                }
                                else
                                {
                                    if (stringValue.Contains("-"))
                                    {
                                        project.FinalGrade = stringValue.Split('-').Last().Trim();
                                    }
                                    else
                                    {
                                        project.FinalGrade = stringValue.Trim();
                                    }
                                }
                            }
                            
                            // Read feedback text from C19, C22, and C25
                            var strengthsCell = worksheet.Cell("C19").Value;
                            if (strengthsCell != null) project.ExaminerStrengths = strengthsCell.ToString().Trim();
                            
                            var improvementsCell = worksheet.Cell("C22").Value;
                            if (improvementsCell != null) project.ExaminerImprovements = improvementsCell.ToString().Trim();
                            
                            var commentsCell = worksheet.Cell("C25").Value;
                            if (commentsCell != null) project.ExaminerComments = commentsCell.ToString().Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Ignore parsing errors, just don't set the grade
                }

                project.ExaminerMarksheetPath = "/uploads/marksheets/" + uniqueFileName;
                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Marksheet uploaded successfully" + (string.IsNullOrEmpty(project.FinalGrade) ? "." : $" and grade {project.FinalGrade} recorded.");
            }
            else
            {
                TempData["Error"] = "Please select a file to upload.";
            }

            return RedirectToAction(nameof(StudentDetails), new { projectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetVivaDate(int projectId, DateTime vivaDate)
        {
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login", "User");

            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.AssignedExaminers.Any(ae => ae.ExaminerId == user.Id));

            if (project == null)
            {
                TempData["Error"] = "Project not found or unauthorized.";
                return RedirectToAction(nameof(StudentDetails), new { projectId });
            }

            project.VivaDate = vivaDate;
            await _context.SaveChangesAsync();

            string formattedDate = vivaDate.ToString("dd MMMM yyyy, hh:mm tt");

            // --- In-app notification for Student ---
            if (project.StudentId > 0)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = project.StudentId,
                    Type = "VivaScheduled",
                    Title = "Viva Date Scheduled",
                    Message = $"Your Viva Oral Examination has been scheduled for {formattedDate}. Please be prepared.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });
            }

            // --- In-app notification for Supervisor ---
            if (project.SupervisorId.HasValue)
            {
                _context.FYPNotifications.Add(new FYPNotification
                {
                    RecipientId = project.SupervisorId.Value,
                    Type = "VivaScheduled",
                    Title = "Viva Date Scheduled",
                    Message = $"Viva for {project.Student?.FullName} ({project.Student?.EmployeeId}) has been scheduled for {formattedDate}.",
                    IsRead = false,
                    CreatedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            // --- Email to Student ---
            if (project.Student != null && !string.IsNullOrEmpty(project.Student.Email))
            {
                string subject = "FYP Viva Oral Examination Scheduled";
                string body = $@"
                    <div style='font-family: Arial, sans-serif;'>
                        <h3>Viva Oral Examination Scheduled</h3>
                        <p>Dear <strong>{project.Student.FullName}</strong>,</p>
                        <p>Your Final Year Project Viva Oral Examination has been scheduled.</p>
                        <ul>
                            <li><strong>Date & Time:</strong> {formattedDate}</li>
                            <li><strong>Project Title:</strong> {project.Title}</li>
                            <li><strong>Examiner:</strong> {user.FullName}</li>
                        </ul>
                        <p>Please ensure you are fully prepared for your presentation. Log in to the FYP Tracker system for more details.</p>
                    </div>";
                _emailService.Send(project.Student.Email, subject, body);
            }

            // --- Email to Supervisor ---
            if (project.Supervisor != null && !string.IsNullOrEmpty(project.Supervisor.Email))
            {
                string subject = "FYP Viva Date Scheduled – Student Notification";
                string body = $@"
                    <div style='font-family: Arial, sans-serif;'>
                        <h3>Viva Date Scheduled</h3>
                        <p>Dear <strong>{project.Supervisor.FullName}</strong>,</p>
                        <p>The examiner <strong>{user.FullName}</strong> has scheduled the Viva Oral Examination for your student.</p>
                        <ul>
                            <li><strong>Student:</strong> {project.Student?.FullName} ({project.Student?.EmployeeId})</li>
                            <li><strong>Project Title:</strong> {project.Title}</li>
                            <li><strong>Date & Time:</strong> {formattedDate}</li>
                        </ul>
                        <p>Please log in to the FYP Tracker system for more details.</p>
                    </div>";
                _emailService.Send(project.Supervisor.Email, subject, body);
            }

            // --- SignalR real-time push to Student ---
            var payload = new { Type = "VivaScheduled", Message = $"Your Viva has been scheduled for {formattedDate}." };
            await _hubContext.Clients.Group($"User_{project.StudentId}").SendAsync("ReceiveMilestoneUpdate", payload);

            TempData["Success"] = $"Viva date set to {formattedDate}. The student and supervisor have been notified.";
            return RedirectToAction(nameof(StudentDetails), new { projectId });
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

            var stage1Milestones = await _context.FYPMilestones
                .Where(m => m.FYPProjectId == milestone.FYPProjectId && !m.Title.StartsWith("Step"))
                .ToListAsync();
            bool isAllStage1Approved = stage1Milestones.Count >= 2 && stage1Milestones.All(m => m.Status == "Approved" || m.Status == "Completed");

            if (!isAllStage1Approved)
            {
                TempData["Error"] = "Both Stage 1 milestones must be approved by the supervisor before joint evaluation.";
                return RedirectToAction("StudentDetails", new { projectId = milestone.FYPProjectId });
            }

            milestone.IsReadyForJointEvaluation = true;
            milestone.ExaminerMarksheetData = marksheetData;
            
            if (!string.IsNullOrEmpty(milestone.SupervisorMarksheetData))
            {
                milestone.Status = "Completed";
                try
                {
                    using var supDoc = System.Text.Json.JsonDocument.Parse(milestone.SupervisorMarksheetData);
                    using var exDoc = System.Text.Json.JsonDocument.Parse(marksheetData);
                    
                    if (supDoc.RootElement.TryGetProperty("Total", out var supTotalProp) && exDoc.RootElement.TryGetProperty("Total", out var exTotalProp))
                    {
                        double supTotal = 0;
                        if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(supTotalProp.GetString(), out double s)) { supTotal = s; }
                        else if (supTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { supTotal = supTotalProp.GetDouble(); }

                        double exTotal = 0;
                        if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(exTotalProp.GetString(), out double e)) { exTotal = e; }
                        else if (exTotalProp.ValueKind == System.Text.Json.JsonValueKind.Number) { exTotal = exTotalProp.GetDouble(); }
                        
                        milestone.Project.Stage1TotalScore = (supTotal + exTotal) / 2.0;
                    }
                }
                catch { }
            }
            
            if (!string.IsNullOrEmpty(stage1Grade))
            {
                milestone.Project.Stage1Grade = stage1Grade;
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
