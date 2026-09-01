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


namespace FYPTracker.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    [FYPTracker.Filters.AuthorizeUserType("Admin", "SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;

        public AdminController(FYPTrackerContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
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
            var users = await _context.ApplicationUsers.ToListAsync();
            ViewBag.TotalUsers = users.Count;
            ViewBag.TotalStudents = users.Count(u => u.UserType == "Student");
            ViewBag.TotalSupervisors = users.Count(u => u.UserType == "Supervisor");

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
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> RespondRepeatRequest(int studentId, string status)
        {
            var student = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == studentId);
            if (student != null)
            {
                if (status == "Approved")
                {
                    var currentProject = await _context.FYPProjects.FirstOrDefaultAsync(p => p.StudentId == studentId && p.Status != "Archived");
                    if (currentProject != null) {
                        currentProject.Status = "Archived";
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
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ArchiveAcademicSession(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName)) {
                TempData["Error"] = "Session name cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            var projects = await _context.FYPProjects.Include(p => p.Student).Where(p => p.Student.AcademicSession == sessionName && p.Status != "Archived").ToListAsync();
            foreach (var p in projects)
            {
                p.Status = "Archived";
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
            ViewBag.TotalStudents    = await _context.ApplicationUsers.CountAsync(u => u.UserType == "Student");
            ViewBag.TotalSupervisors = await _context.ApplicationUsers.CountAsync(u => u.UserType == "Supervisor");
            ViewBag.TotalExaminers   = await _context.ApplicationUsers.CountAsync(u => u.UserType == "Examiner");
            ViewBag.TotalProjects    = await _context.FYPProjects.CountAsync();
            ViewBag.PendingRequests  = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");

            return View(new ApplicationUserVM());
        }

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ManageProjects()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var students = await _context.ApplicationUsers
                .Where(u => u.UserType == "Student")
                .ToListAsync();

            var projects = await _context.FYPProjects
                .Include(p => p.Supervisor)
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

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ExportProjectsExcel()
        {
            var students = await _context.ApplicationUsers
                .Where(u => u.UserType == "Student" && !u.IsResign)
                .ToListAsync();

            var projects = await _context.FYPProjects
                .Include(p => p.Supervisor)
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
            ws.Cell(1, 3).Value = "Supervisor Name";
            ws.Cell(1, 4).Value = "Project Title";
            ws.Cell(1, 5).Value = "Status";
            ws.Cell(1, 6).Value = "Started Date";

            var headerRange = ws.Range("A1:F1");
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkSlateBlue;
            headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach(var item in data)
            {
                ws.Cell(row, 1).Value = item.Student.FullName;
                ws.Cell(row, 2).Value = item.Student.EmployeeId;
                ws.Cell(row, 3).Value = item.Project?.Supervisor?.FullName ?? "Not Assigned";
                ws.Cell(row, 4).Value = item.Project?.Title ?? "No Project";
                
                string status = item.Student.IsResign ? "Inactive" : (item.Project != null ? item.Project.Status : "Inactive");
                ws.Cell(row, 5).Value = status;
                ws.Cell(row, 6).Value = item.Project?.StartDate?.ToString("dd MMM yyyy") ?? "-";
                
                // Alternate row coloring
                if (row % 2 == 0)
                {
                    ws.Range($"A{row}:F{row}").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.AliceBlue;
                }

                // Status coloring
                var statusCell = ws.Cell(row, 5);
                statusCell.Style.Font.Bold = true;
                if (status == "Active" || status == "Approved" || status == "Completed")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.ForestGreen;
                else if (status == "Inactive")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.DimGray;
                else if (status == "Proposed")
                    statusCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.DarkOrange;
                
                row++;
            }

            ws.Columns().AdjustToContents();
            // Give columns a bit more breathing room
            foreach (var col in ws.Columns()) col.Width += 2;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserDetails(int id)
        {
            var user = await _context.ApplicationUsers.Include(x => x.EmpDepartment).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Json(new { success = false, msg = "User not found." });
            return Json(new {
                success = true,
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phone = user.Phone,
                userType = user.UserType,
                department = user.EmpDepartment?.Name,
                programme = user.Programme,
                specialization = user.Specialization,
                academicSession = user.AcademicSession,
                title = user.Title
            });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserModal(int id, string fullName, string email, string phone, string department, string programme, string academicSession, string title, string specialization)
        {
            try
            {
                var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == id);
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
                
                if (user.UserType == "Student" || user.UserType == "STUDENT" || 
                    user.UserType == "Supervisor" || user.UserType == "SUPERVISOR" || 
                    user.UserType == "Examiner" || user.UserType == "EXAMINER")
                {
                    var deptId = await _context.Department.Where(x => x.Name == department).Select(x => x.Id).FirstOrDefaultAsync();
                    user.DepartmentId = deptId > 0 ? deptId : user.DepartmentId;
                    
                    if (user.UserType != "Student" && user.UserType != "STUDENT")
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

                if (user.UserType == "Supervisor" || user.UserType == "SUPERVISOR")
                {
                    user.Title = title;
                }
                else if (user.UserType == "Student" || user.UserType == "STUDENT")
                {
                    // Save AcademicSession for students from the edit modal
                    if (!string.IsNullOrWhiteSpace(academicSession))
                        user.AcademicSession = academicSession;
                }

                _context.Update(user);
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
                model.UserType = String.IsNullOrEmpty(check.UserType) ? "" : check.UserType.ToUpper().Trim();
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
        public ActionResult EditUser(EmployeeEditVM ApplicationUser, IFormFile? picture, string isImgChanged)
        {
            try {
            var model = _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
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

            if (model != null && model.UserType == "Student" && !string.IsNullOrWhiteSpace(ApplicationUser.EmployeeId))
            {
                var duplicateMatric = _context.ApplicationUsers.Any(u => u.UserType == "Student" && u.Id != model.Id && u.EmployeeId != null && u.EmployeeId.ToLower() == ApplicationUser.EmployeeId.ToLower());
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
                model1.UserType = ApplicationUser.UserType;
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
                    if (model.UserType != "Student" && model.UserType != "STUDENT")
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
                return Content("CRASH OCCURRED: " + ex.Message + " | " + ex.StackTrace);
            }
        }

        [HttpPost]
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
                            UserType = String.IsNullOrEmpty(newEmployee.UserType) ? "Requestor" : newEmployee.UserType,
                            Image = "/assets/media/avatars/blank.png",
                            DepartmentId = deptQuery.Where(x => x.Name == newEmployee.Department).FirstOrDefault()?.Id,
                            Phone = newEmployee.Phone,
                            Programme = newEmployee.Programme,
                            AcademicSession = newEmployee.AcademicSession,
                            Title = newEmployee.Title,
                            MaxStudentSlots = newEmployee.UserType == "Supervisor" ? 5 : null,
                            RequirePasswordChange = true
                    };
                        _context.ApplicationUsers.Add(model);
                        _context.SaveChanges();

                        try
                        {
                            var request = HttpContext.Request;
                            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";

                            string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                            string htmlBody = $@"
                                <h3>Welcome, {newEmployee.FullName}!</h3>
                                <p>Your account has been successfully created. Below are your login credentials:</p>
                                <ul>
                                    <li><strong>Username:</strong> {newEmployee.UserName}</li>
                                    <li><strong>Password:</strong> {generatedPassword}</li>
                                </ul>
                                <p style='margin: 20px 0;'>
                                    <a href='{loginUrl}' style='display: inline-block; padding: 10px 20px; color: white; background-color: #009ef7; text-decoration: none; border-radius: 6px; font-weight: bold; font-family: sans-serif;'>Click Here to Login</a>
                                </p>
                                <p>Please log in and change your password as soon as possible.</p>
                                <br/>
                                <p>Best Regards,<br/>FYP Tracking System Admin</p>
                            ";
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
                        if (check != null)
                        {
                            //ModelState.AddModelError(nameof(ApplicationUserVM.UserName), "This Username is exists, please try another one!");
                            return Json(new { status = false, msg = "This Username is exists, please try another one!" });
                        }
                        if (check1 != null)
                        {
                            //ModelState.AddModelError(nameof(ApplicationUserVM.Email), "This Email is exists, please try another one!");
                            return Json(new { status = false, msg = "This Email is exists, please try another one!" });
                        }
                        //return RedirectToAction("Index", newEmployee);
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
            var users = _context.ApplicationUsers.Where(u => u.UserType != "Admin" && u.UserType != "SuperAdmin").ToList();
            var projects = _context.FYPProjects.Include(p => p.Supervisor).Include(p => p.Student).ToList();

            var empList = users.Select(u => new {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email,
                employeeId = u.EmployeeId,
                userType = u.UserType,
                dateCreated = u.DateCreated,
                isResign = u.IsResign,
                image = u.Image,
                supervisorName = u.UserType == "Student" 
                    ? (projects.FirstOrDefault(p => p.StudentId == u.Id)?.Supervisor?.FullName ?? "Not Assigned")
                    : "-",
                assignedStudents = u.UserType == "Supervisor"
                    ? string.Join(", ", projects.Where(p => p.SupervisorId == u.Id && p.Student != null).Select(p => p.Student.FullName).DefaultIfEmpty("No Students"))
                    : u.UserType == "Examiner" 
                        ? string.Join(", ", projects.Where(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == u.Id) && p.Student != null).Select(p => p.Student.FullName).DefaultIfEmpty("No Students"))
                        : "-",
                assignedStudentCount = u.UserType == "Supervisor"
                    ? projects.Count(p => p.SupervisorId == u.Id && p.Student != null).ToString()
                    : u.UserType == "Examiner"
                        ? projects.Count(p => p.AssignedExaminers.Any(ae => ae.ExaminerId == u.Id) && p.Student != null).ToString()
                        : "-",
                specialization = u.Specialization,
                requirePasswordChange = u.RequirePasswordChange,
                accountExpiry = u.AccountExpiry,
                isProfileIncomplete = ((u.UserType == "Supervisor" || u.UserType == "Examiner") && 
                                      (u.DepartmentId == null || 
                                       string.IsNullOrWhiteSpace(u.Programme) || 
                                       string.IsNullOrWhiteSpace(u.Specialization) || 
                                       string.IsNullOrWhiteSpace(u.Title)))
            }).ToList();

            return Json(new { data = empList, recordsTotal = empList.Count(), recordsFiltered = empList.Count() });
        }

        [HttpPost]
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
        public async Task<IActionResult> HardDelete(int id)
        {
            try
            {
                var user = await _context.ApplicationUsers.FirstOrDefaultAsync(x => x.Id == id);
                if (user == null) return Json(new { status = false, msg = "User not found." });

                // 1. ChatGroupMembers — Restrict FK (student or supervisor as member)
                var groupMembers = await _context.ChatGroupMembers
                    .Where(m => m.StudentId == id).ToListAsync();
                _context.ChatGroupMembers.RemoveRange(groupMembers);

                // 2. ChatGroups owned by supervisor — Restrict FK (messages cascade via Group)
                if (user.UserType == "Supervisor" || user.UserType == "SUPERVISOR")
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
                var chatMessages = await _context.ChatMessages
                    .Where(m => m.SenderId == id || m.ReceiverId == id).ToListAsync();
                _context.ChatMessages.RemoveRange(chatMessages);

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

                // 7. InvitationLogs — NoAction FK (must delete manually)
                var inviteLogs = await _context.InvitationLogs
                    .Where(l => l.StudentId == id || l.AdminId == id).ToListAsync();
                _context.InvitationLogs.RemoveRange(inviteLogs);

                if (user.UserType == "Student" || user.UserType == "STUDENT")
                {
                    // Student's project: Milestones → Feedbacks/Files cascade automatically
                    var project = await _context.FYPProjects
                        .FirstOrDefaultAsync(p => p.StudentId == id);
                    if (project != null)
                        _context.FYPProjects.Remove(project);
                }
                else if (user.UserType == "Supervisor" || user.UserType == "SUPERVISOR")
                {
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
                else if (user.UserType == "Examiner" || user.UserType == "EXAMINER")
                {
                    // Nullify examiner on projects by removing mapping
                    var examinerAssignments = await _context.FYPProjectExaminers
                        .Where(pe => pe.ExaminerId == id).ToListAsync();
                    _context.FYPProjectExaminers.RemoveRange(examinerAssignments);
                }

                // 8. Submission files uploaded by this user — Restrict FK
                var uploadedFiles = await _context.FYPSubmissionFiles
                    .Where(f => f.UploadedBy == id).ToListAsync();
                _context.FYPSubmissionFiles.RemoveRange(uploadedFiles);

                // 9. Remove user (FYPNotifications + FYPActivities cascade automatically)
                _context.ApplicationUsers.Remove(user);
                await _context.SaveChangesAsync();

                return Json(new { status = true, msg = "User permanently deleted." });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return Json(new { status = false, msg = "Error: " + inner });
            }
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

            // Load FYP global settings (DPP deadline, exhibition, etc.)
            var fypSettings = await _context.FYPSettings.ToListAsync();
            ViewBag.FYPSettings = fypSettings;

            // Helper: get setting value by key
            string GetSetting(string key) => fypSettings.FirstOrDefault(s => s.Key == key)?.Value ?? "";
            ViewBag.DPPDeadline       = GetSetting("DPPDeadline");
            ViewBag.ExhibitionDate    = GetSetting("ExhibitionDate");
            ViewBag.ExhibitionTimeStart = GetSetting("ExhibitionTimeStart");
            ViewBag.ExhibitionTimeEnd   = GetSetting("ExhibitionTimeEnd");

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
            catch (Exception ex)
            {
                TempData["Error"] = $"Crash Details: {ex.Message} | Inner: {ex.InnerException?.Message}";
                return RedirectToAction(nameof(Settings), new { sessionFilter });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFYPSettings(string dppDeadline, string exhibitionDate, string exhibitionTimeStart, string exhibitionTimeEnd)
        {
            try
            {
                var updates = new Dictionary<string, (string value, string label)>
                {
                    { "DPPDeadline",         (dppDeadline ?? "",         "DPP Submission Deadline") },
                    { "ExhibitionDate",      (exhibitionDate ?? "",      "FYP Exhibition Date") },
                    { "ExhibitionTimeStart", (exhibitionTimeStart ?? "", "Exhibition Start Time") },
                    { "ExhibitionTimeEnd",   (exhibitionTimeEnd ?? "",   "Exhibition End Time") },
                };

                foreach (var kv in updates)
                {
                    var existing = await _context.FYPSettings.FirstOrDefaultAsync(s => s.Key == kv.Key);
                    if (existing != null)
                    {
                        existing.Value = kv.Value.value;
                        existing.UpdatedDate = DateTime.Now;
                        _context.FYPSettings.Update(existing);
                    }
                    else
                    {
                        _context.FYPSettings.Add(new FYPTracker.Models.FYPSetting
                        {
                            Key = kv.Key,
                            Value = kv.Value.value,
                            Label = kv.Value.label,
                            UpdatedDate = DateTime.Now
                        });
                    }
                }

                await _context.SaveChangesAsync();
                TempData["FYPSettingSuccess"] = "FYP submission dates updated successfully.";
            }
            catch (Exception ex)
            {
                TempData["FYPSettingError"] = $"Error saving settings: {ex.Message}";
            }

            return RedirectToAction(nameof(Settings));
        }


        public async Task<IActionResult> DefenseList()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var defenseList = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Where(m => m.Title == "Initial Project Allocation Form (IAF)" && m.RejectionCount >= 3)
                .ToListAsync();

            return View(defenseList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyDefenseList(DateTime defenseDate, TimeSpan startTime, string venue)
        {
            var defenseList = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Include(m => m.Project)
                    .ThenInclude(p => p.Supervisor)
                .Where(m => m.Title == "Initial Project Allocation Form (IAF)" && m.RejectionCount >= 3)
                .ToListAsync();

            int count = 0;
            var currentTime = startTime;

            foreach (var milestone in defenseList)
            {
                var student = milestone.Project?.Student;
                if (student != null)
                {
                    // Format the scheduled datetime
                    var formattedDate = defenseDate.ToString("dd MMM yyyy");
                    var formattedTime = new DateTime(currentTime.Ticks).ToString("hh:mm tt");
                    
                    var notificationMessage = $"Your Initial Project Allocation Form has been rejected 3 times. You are required to attend a defense session.\n\nDate: {formattedDate}\nTime: {formattedTime}\nVenue: {venue}";
                    var emailHtml = $@"
                        <p>Dear {student.FullName},</p>
                        <p>Your Initial Project Allocation Form has been rejected 3 times. You are required to attend a defense session.</p>
                        <ul>
                            <li><strong>Date:</strong> {formattedDate}</li>
                            <li><strong>Time:</strong> {formattedTime}</li>
                            <li><strong>Venue:</strong> {venue}</li>
                        </ul>
                        <p>Please be prepared for a 15-minute presentation.</p>";

                    // Add in-app notification
                    var notification = new FYPNotification
                    {
                        RecipientId = student.Id,
                        Type = "RevisionRequest",
                        Title = "Defense Scheduled",
                        Message = notificationMessage,
                        IsRead = false,
                        CreatedDate = DateTime.Now
                    };
                    _context.FYPNotifications.Add(notification);

                    // Optional: Email
                    if (!string.IsNullOrEmpty(student.Email))
                    {
                        try
                        {
                            _emailService.Send(
                                to: student.Email,
                                subject: "Defense Scheduled for FYP",
                                html: emailHtml
                            );
                        }
                        catch (Exception ex)
                        {
                            // Log email failure if needed, continue notifying others
                        }
                    }

                    // Notify Supervisor
                    var supervisor = milestone.Project?.Supervisor;
                    if (supervisor != null && !string.IsNullOrEmpty(supervisor.Email))
                    {
                        var supEmailHtml = $@"
                            <p>Dear {supervisor.FullName},</p>
                            <p>Your student <strong>{student.FullName}</strong> is required to attend a defense session for their Initial Project Allocation Form.</p>
                            <ul>
                                <li><strong>Date:</strong> {formattedDate}</li>
                                <li><strong>Time:</strong> {formattedTime}</li>
                                <li><strong>Venue:</strong> {venue}</li>
                            </ul>
                            <p>As their supervisor, your attendance is highly encouraged.</p>";

                        try
                        {
                            _emailService.Send(
                                to: supervisor.Email,
                                subject: "Student Defense Scheduled for FYP",
                                html: supEmailHtml
                            );
                        }
                        catch (Exception ex) {}
                    }

                    // Save schedule to database
                    milestone.DefenseDate = defenseDate;
                    milestone.DefenseTime = formattedTime;
                    milestone.DefenseVenue = venue;

                    // Increment time by 15 minutes for the next student
                    currentTime = currentTime.Add(TimeSpan.FromMinutes(15));
                    count++;
                }
            }
            
            if (count > 0)
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Successfully scheduled defense and sent notifications to {count} student(s).";
            }
            else
            {
                TempData["Error"] = "No students found in the defense list to notify.";
            }

            return RedirectToAction(nameof(DefenseList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AllowIafResubmission(int milestoneId)
        {
            var milestone = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .FirstOrDefaultAsync(m => m.Id == milestoneId);

            if (milestone != null)
            {
                // Reset RejectionCount so they can submit again
                milestone.RejectionCount = 0;
                milestone.DefenseDate = null;
                milestone.DefenseTime = null;
                milestone.DefenseVenue = null;
                milestone.Status = "Pending";
                milestone.Remarks = "Defense completed. Student is allowed to resubmit the IAF with a new title/proposal.";

                var student = milestone.Project?.Student;
                if (student != null)
                {
                    _context.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = student.Id,
                        Type = "General",
                        Title = "IAF Resubmission Allowed",
                        Message = "Following your defense, you are now permitted to propose a new title and resubmit your Initial Project Allocation Form.",
                        IsRead = false,
                        CreatedDate = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Successfully granted resubmission access to {student?.FullName}.";
            }
            else
            {
                TempData["Error"] = "Milestone not found.";
            }

            return RedirectToAction(nameof(DefenseList));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkInvite(List<string> inviteNames, List<string> inviteEmails, string bulkUserType, string bulkProgramme, string bulkAcademicSession)
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

            foreach (var invite in invites)
            {
                var email = invite.Email;
                var exists = await _context.ApplicationUsers.AnyAsync(u => u.Email == email || u.UserName == email);
                if (exists)
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
                    UserType = bulkUserType,
                    Image = "/assets/media/avatars/blank.png",
                    RequirePasswordChange = true, // Forces onboarding flow
                    // Store student-specific profile fields supplied by admin
                    Programme = bulkUserType == "Student" ? bulkProgramme : null,
                    AcademicSession = bulkUserType == "Student" ? bulkAcademicSession : null,
                    AccountExpiry = bulkUserType == "Student" ? CalculateAccountExpiry(bulkAcademicSession) : null
                };

                _context.ApplicationUsers.Add(newUser);
                await _context.SaveChangesAsync(); // save so we have newUser.Id for the log

                // ── Log the invitation ──
                var adminUser = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
                if (adminUser != null)
                {
                    _context.InvitationLogs.Add(new InvitationLog
                    {
                        StudentId = newUser.Id,
                        StudentEmail = email,
                        AdminId = adminUser.Id,
                        Action = "Invited",
                        Status = "Sent",
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        CreatedAt = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }

                successCount++;

                // ── Prepare Email ──
                string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                string htmlBody = $@"
                    <h3>Welcome!</h3>
                    <p>Your account has been successfully created by the administrator. Below are your login credentials:</p>
                    <ul>
                        <li><strong>Username:</strong> {newId}</li>
                        <li><strong>Email:</strong> {email}</li>
                        <li><strong>Temporary Password:</strong> {tempPwd}</li>
                    </ul>
                    <p style='margin: 20px 0;'>
                        <a href='{loginUrl}' style='display: inline-block; padding: 10px 20px; color: white; background-color: #009ef7; text-decoration: none; border-radius: 6px; font-weight: bold; font-family: sans-serif;'>Click Here to Login</a>
                    </p>
                    <p>Upon your first login, you will be required to set up your profile and change your password.</p>
                    <br/>
                    <p>Best Regards,<br/>FYP Tracking System Admin</p>
                ";
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

            // Log the resend action
            _context.InvitationLogs.Add(new InvitationLog
            {
                StudentId = user.Id,
                StudentEmail = user.Email,
                AdminId = admin.Id,
                Action = "Resend",
                Status = "Sent",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            // Resend email
            var request = HttpContext.Request;
            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";
            string subject = "FYP Tracking System - Your Updated Login Credentials";
            string htmlBody = $@"
                <h3>Login Credentials Resent</h3>
                <p>Dear <strong>{user.FullName}</strong>,</p>
                <p>Your login credentials have been refreshed by the administrator. Please use the details below to log in:</p>
                <ul>
                    <li><strong>Username:</strong> {user.UserName}</li>
                    <li><strong>Email:</strong> {user.Email}</li>
                    <li><strong>New Temporary Password:</strong> {newTempPwd}</li>
                </ul>
                <p style='margin: 20px 0;'>
                    <a href='{loginUrl}' style='display: inline-block; padding: 10px 20px; color: white; background-color: #009ef7; text-decoration: none; border-radius: 6px; font-weight: bold; font-family: sans-serif;'>Click Here to Login</a>
                </p>
                <p>You will be required to change your password upon first login.</p>
                <br/>
                <p>Best Regards,<br/>FYP Tracking System Admin</p>
            ";

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

                var exists = await _context.ApplicationUsers.AnyAsync(u => u.Email == email || u.UserName == email);
                if (exists) { dupCount++; continue; }

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
                    UserType = csvUserType,
                    Image = "/assets/media/avatars/blank.png",
                    RequirePasswordChange = true,
                    Programme = csvUserType == "Student" ? csvProgramme : null,
                    AcademicSession = csvUserType == "Student" ? csvAcademicSession : null,
                    AccountExpiry = csvUserType == "Student" ? CalculateAccountExpiry(csvAcademicSession) : null
                };
                _context.ApplicationUsers.Add(newUser);
                await _context.SaveChangesAsync();

                if (admin != null)
                {
                    _context.InvitationLogs.Add(new InvitationLog
                    {
                        StudentId = newUser.Id, StudentEmail = email,
                        AdminId = admin.Id, Action = "Invited", Status = "Sent",
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        CreatedAt = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }

                // Send welcome email
                try
                {
                    string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                    string htmlBody = $@"<h3>Welcome!</h3>
<p>Your account has been created. Here are your login credentials:</p>
<ul><li><strong>Username:</strong> {newId}</li><li><strong>Email:</strong> {email}</li><li><strong>Temporary Password:</strong> {tempPwd}</li></ul>
<p style='margin:20px 0'><a href='{loginUrl}' style='display:inline-block;padding:10px 20px;color:white;background:#009ef7;text-decoration:none;border-radius:6px;font-weight:bold'>Click Here to Login</a></p>
<p>You will be asked to change your password on first login.</p><p>Best Regards,<br/>FYP Tracking System Admin</p>";
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
        public async Task<IActionResult> Reporting()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name || u.Email == HttpContext.User.Identity.Name);
            if (user == null || (user.UserType != "Admin" && user.UserType != "SuperAdmin")) return RedirectToAction("Login", "User");
            ViewBag.CurrentUser = user;

            // 1. Supervisor Capacity vs Workload
            var supervisors = await _context.ApplicationUsers
                .Where(u => u.UserType == "Supervisor" && !u.IsResign)
                .Select(s => new {
                    Name = s.FullName ?? s.UserName,
                    MaxSlots = s.MaxStudentSlots ?? 5,
                    CurrentStudents = _context.FYPProjects.Count(p => p.SupervisorId == s.Id && !p.Student.IsResign && p.Status == "Approved")
                })
                .ToListAsync();

            // 2. Global Student Allocation Status
            var allActiveStudents = await _context.ApplicationUsers
                .Where(u => u.UserType == "Student" && !u.IsResign)
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
            var programCounts = await _context.ApplicationUsers
                .Where(u => u.UserType == "Student" && !u.IsResign && !string.IsNullOrEmpty(u.Programme))
                .GroupBy(u => u.Programme)
                .Select(g => new { Program = g.Key, Count = g.Count() })
                .ToListAsync();

            // 4. Global Milestone Bottlenecks
            var milestones = await _context.FYPMilestones
                .Where(m => !m.Project.Student.IsResign)
                .Select(m => new { m.Title, m.Status })
                .ToListAsync();

            var milestoneStatusData = milestones
                .Select(m => new {
                    Category = m.Title.Contains("IAF") || m.Title.Contains("Initial") ? "IAF" :
                               m.Title.Contains("Proposal") ? "Proposal" :
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
                return Json(new { data = new List<object>() });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddLookup(string type, string name)
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
                _context.SaveChanges();
                return Json(new { status = true });
            }
            catch(Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult EditLookup(string type, int id, string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return Json(new { status = false, msg = "Name cannot be empty." });

                if (type == "Department")
                {
                    var entity = _context.Department.Find(id);
                    if (entity != null) { entity.Name = name; _context.SaveChanges(); }
                }
                else if (type == "Programme")
                {
                    var entity = _context.Programme.Find(id);
                    if (entity != null) { entity.Name = name; _context.SaveChanges(); }
                }
                else if (type == "Specialization")
                {
                    var entity = _context.Specialization.Find(id);
                    if (entity != null) { entity.Name = name; _context.SaveChanges(); }
                }
                return Json(new { status = true });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }

        [HttpPost]
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
                return Json(new { status = true });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }
    }
}


