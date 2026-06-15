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

        public async Task<IActionResult> Index()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
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
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            // System-wide stats for Admin
            ViewBag.TotalStudents    = await _context.ApplicationUsers.CountAsync(u => u.UserType == "Student");
            ViewBag.TotalSupervisors = await _context.ApplicationUsers.CountAsync(u => u.UserType == "Supervisor");
            ViewBag.TotalProjects    = await _context.FYPProjects.CountAsync();
            ViewBag.PendingRequests  = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");

            return View(new ApplicationUserVM());
        }

        [Authorize]
        [ClaimRequirement("Type", "Admin")]
        public async Task<IActionResult> ManageProjects()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
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
                academicSession = user.AcademicSession,
                title = user.Title
            });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUserModal(int id, string email, string phone, string department, string programme, string academicSession, string title)
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

                user.Email = email;
                user.Phone = phone;
                
                if (user.UserType == "Supervisor" || user.UserType == "SUPERVISOR")
                {
                    var deptId = await _context.Department.Where(x => x.Name == department).Select(x => x.Id).FirstOrDefaultAsync();
                    user.DepartmentId = deptId > 0 ? deptId : user.DepartmentId;
                    user.Title = title;
                }
                else if (user.UserType == "Student" || user.UserType == "STUDENT")
                {
                    user.Programme = programme;
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
            var loggedInUser = _context.ApplicationUsers.FirstOrDefault(u => u.UserName == HttpContext.User.Identity.Name);
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
                
                var currentUser = _context.ApplicationUsers.FirstOrDefault(u => u.UserName == HttpContext.User.Identity.Name);
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
                    model.Programme = ApplicationUser.Programme;
                    model.AcademicSession = ApplicationUser.AcademicSession;
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
                    string newId;
                    if (!string.IsNullOrEmpty(newEmployee.Email) && 
                        (newEmployee.Email.EndsWith("@student.newinti.edu.my", StringComparison.OrdinalIgnoreCase) || 
                         newEmployee.Email.EndsWith("@newinti.edu.my", StringComparison.OrdinalIgnoreCase)))
                    {
                        newId = newEmployee.Email.Split('@')[0];
                    }
                    else
                    {
                        string prefix = newEmployee.UserType == "Student" ? "STU-" : "SUP-";
                        var highestUser = _context.ApplicationUsers
                            .Where(u => u.EmployeeId != null && u.EmployeeId.StartsWith(prefix))
                            .OrderByDescending(u => u.EmployeeId)
                            .FirstOrDefault();
                            
                        int nextIdNum = 1;
                        if (highestUser != null && !string.IsNullOrEmpty(highestUser.EmployeeId) && highestUser.EmployeeId.Length > 4)
                        {
                            if (int.TryParse(highestUser.EmployeeId.Substring(4), out int lastNum))
                            {
                                nextIdNum = lastNum + 1;
                            }
                        }
                        
                        newId = $"{prefix}{nextIdNum:D3}";
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
                            Image = "/assets/media/avatars/blank.svg",
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

                        return Json(new { status = true , msg = "User created successfully!"});
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
                    : "-"
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
        public async Task<IActionResult> Settings()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var existingIaf = await _context.FYPMilestones
                .Where(m => m.Title == "Initial Project Allocation Form (IAF)" && m.DueDate != null)
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

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateIAFDeadline(DateTime? deadline)
        {
            try 
            {
                if (!deadline.HasValue)
                {
                    TempData["Error"] = "The date provided was invalid or empty.";
                    return RedirectToAction(nameof(Settings));
                }

                // Set the time to 11:59:59 PM of the selected date
                var endOfDayDeadline = new DateTime(deadline.Value.Year, deadline.Value.Month, deadline.Value.Day, 23, 59, 59);

                var milestones = await _context.FYPMilestones
                    .Where(m => m.Title == "Initial Project Allocation Form (IAF)" || m.Title.Contains("IAF"))
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
                    TempData["Success"] = $"Successfully updated the IAF deadline for {count} student project(s).";
                }
                else
                {
                    TempData["Info"] = "No IAF milestones found to update.";
                }

                return RedirectToAction(nameof(Settings));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Crash Details: {ex.Message} | Inner: {ex.InnerException?.Message}";
                return RedirectToAction(nameof(Settings));
            }
        }

        public async Task<IActionResult> DefenseList()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
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
        public async Task<IActionResult> BulkInvite(string bulkEmails, string bulkUserType)
        {
            if (string.IsNullOrWhiteSpace(bulkEmails) || string.IsNullOrWhiteSpace(bulkUserType))
            {
                TempData["Error"] = "Please provide emails and select a user type.";
                return RedirectToAction(nameof(ManageUsers));
            }

            var emails = bulkEmails.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(e => e.Trim())
                                   .Where(e => !string.IsNullOrEmpty(e) && new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$").IsMatch(e))
                                   .Distinct()
                                   .ToList();

            if (!emails.Any())
            {
                TempData["Error"] = "No valid emails found.";
                return RedirectToAction(nameof(ManageUsers));
            }

            int successCount = 0;
            int duplicateCount = 0;
            
            // Get highest ID suffix to increment properly
            string prefix = bulkUserType == "Student" ? "STU-" : "SUP-";
            var highestUser = await _context.ApplicationUsers
                .Where(u => u.EmployeeId != null && u.EmployeeId.StartsWith(prefix))
                .OrderByDescending(u => u.EmployeeId)
                .FirstOrDefaultAsync();

            int nextIdNum = 1;
            if (highestUser != null && !string.IsNullOrEmpty(highestUser.EmployeeId) && highestUser.EmployeeId.Length > 4)
            {
                if (int.TryParse(highestUser.EmployeeId.Substring(4), out int lastNum))
                {
                    nextIdNum = lastNum + 1;
                }
            }

            var request = HttpContext.Request;
            var loginUrl = $"{request.Scheme}://{request.Host}/User/Login";

            foreach (var email in emails)
            {
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
                else
                {
                    newId = $"{prefix}{nextIdNum:D3}";
                    nextIdNum++;
                }

                var newUser = new ApplicationUser
                {
                    UserName = newId,
                    FullName = newId, // Placeholder until they set it
                    Email = email,
                    PassCode = FYPTracker.Common.Encryption.GetHash("defaultpassword"),
                    EmployeeId = newId,
                    DateCreated = DateTime.Now,
                    DateJoin = DateTime.Now,
                    AddedBy = "sys",
                    UserType = bulkUserType,
                    Image = "/assets/media/avatars/blank.svg",
                    RequirePasswordChange = true // Forces onboarding flow
                };

                _context.ApplicationUsers.Add(newUser);
                successCount++;

                // Prepare Email
                string subject = "Welcome to FYP Tracking System - Your Login Credentials";
                string htmlBody = $@"
                    <h3>Welcome!</h3>
                    <p>Your account has been successfully created by the administrator. Below are your login credentials:</p>
                    <ul>
                        <li><strong>Username:</strong> {newId}</li>
                        <li><strong>Email:</strong> {email}</li>
                        <li><strong>Password:</strong> defaultpassword</li>
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
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Successfully invited {successCount} users. {duplicateCount} duplicates ignored.";
            }
            else
            {
                TempData["Info"] = $"No new users created. {duplicateCount} emails were already registered.";
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        // ────────────────────────────────────────────────────────
        // Reporting (Admin)
        // ────────────────────────────────────────────────────────
        public async Task<IActionResult> Reporting()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
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
    }
}


