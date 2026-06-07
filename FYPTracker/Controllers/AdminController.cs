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
        [ClaimRequirement("Type,Department","PM,HOD,SI,Admin,SuperAdmin")]
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
        [ClaimRequirement("Type,Department","PM,HOD,SI,Admin,SuperAdmin")]
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
        [ClaimRequirement("Type,Department","PM,HOD,SI,Admin,SuperAdmin")]
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
        public IActionResult Settings()
        {
            return View();
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
        public async Task<IActionResult> NotifyDefenseList()
        {
            var defenseList = await _context.FYPMilestones
                .Include(m => m.Project)
                    .ThenInclude(p => p.Student)
                .Where(m => m.Title == "Initial Project Allocation Form (IAF)" && m.RejectionCount >= 3)
                .ToListAsync();

            int count = 0;
            foreach (var milestone in defenseList)
            {
                var student = milestone.Project?.Student;
                if (student != null)
                {
                    // Add in-app notification
                    var notification = new FYPNotification
                    {
                        RecipientId = student.Id,
                        Type = "RevisionRequest",
                        Title = "Defense Required",
                        Message = "Your Initial Project Allocation Form has been rejected 3 times. You are required to attend a defense session.",
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
                                subject: "Defense Required for FYP",
                                html: "<p>Your Initial Project Allocation Form has been rejected 3 times. You are required to attend a defense session.</p>"
                            );
                        }
                        catch (Exception ex)
                        {
                            // Log email failure if needed, continue notifying others
                        }
                    }
                    count++;
                }
            }
            
            if (count > 0)
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Successfully sent defense notifications to {count} student(s).";
            }
            else
            {
                TempData["Error"] = "No students found in the defense list to notify.";
            }

            return RedirectToAction(nameof(DefenseList));
        }
    }
}


