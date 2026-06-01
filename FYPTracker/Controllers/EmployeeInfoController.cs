using FYPTracker.Common;
using FYPTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using FYPTracker.Services.Employees;
using FYPTracker.Services.Authentication;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Text;
using FYPTracker.Services.Others.Interfaces;
using FYPTracker.Configuration.Auhtorization;
using Microsoft.AspNetCore.Authorization;
using FYPTracker.Configuration;
using FYPTracker.Helpers;
using FYPTracker.ViewModels;
using Newtonsoft.Json;

namespace FYPTracker.Controllers
{
    public class EmployeeInfoController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmployeeService _empService;
        private readonly IAuthenticationService _authenticationService;
        private static IHttpContextAccessor httpContextAccessor;
        private readonly IEmailService _emailService;

        private IWebHostEnvironment Environment;
        public EmployeeInfoController(
            FYPTrackerContext context,
            IWebHostEnvironment _environment,
            IEmployeeService empService,
            IEmailService emailService,
            IAuthenticationService authenticationService
            )
        {
            _context = context;
            Environment = _environment;
            _empService = empService;
            _emailService = emailService;
            _authenticationService = authenticationService;
        }

        [Authorize]
        [ClaimRequirement("Type,Department","PM,HOD,SI,Admin,SuperAdmin")]
        public async Task<IActionResult> Index()
        {
            var user = await _context.EmployeeInfos.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            // System-wide stats for Admin
            ViewBag.TotalStudents    = await _context.EmployeeInfos.CountAsync(u => u.UserType == "Student");
            ViewBag.TotalSupervisors = await _context.EmployeeInfos.CountAsync(u => u.UserType == "Supervisor");
            ViewBag.TotalProjects    = await _context.FYPProjects.CountAsync();
            ViewBag.PendingRequests  = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");

            return View();
        }

        [Authorize]
        [ClaimRequirement("Type,Department","PM,HOD,SI,Admin,SuperAdmin")]
        public async Task<IActionResult> ManageProjects()
        {
            var user = await _context.EmployeeInfos.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            ViewBag.CurrentUser = user;

            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(projects);
        }

        [AllowAnonymous]
        public ActionResult SignUp()
        {
            return View();
        }

        [HttpPost]
        public ActionResult SignUp(EmployeeInfoVM newEmployee)
        {
            var pattern = @"^[a-z0-9](\.?[a-z0-9]){2,}@fyp\.edu\.my$";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            Match m = r.Match(newEmployee.Email ?? "");
            var deptQuery = _context.Department.AsQueryable();

            if (!m.Success)
            {
                ModelState.AddModelError("Email", "Please enter the correct email! (xxx@fyp.edu.my)");
            }
            if (ModelState.IsValid)
            {
                var check = _context.EmployeeInfos.Where(d => d.UserName == newEmployee.UserName).FirstOrDefault();
                var check1 = _context.EmployeeInfos.Where(d => d.Email == newEmployee.Email).FirstOrDefault();
                if (check == null && check1 == null)
                {
                    var model = new EmployeeInfo
                    {
                        UserName = newEmployee.UserName,
                        FullName = newEmployee.FullName,
                        Email = newEmployee.Email,
                        ShortName = newEmployee.ShortName,
                        PassCode = Encryption.GetHash(newEmployee.PassCode),
                        EmployeeId = newEmployee.EmplooyeeInfoID,
                        DateCreated = DateTime.Now,
                        DateJoin = DateTime.Now,
                        AddedBy = "sys",
                        UserType = "Student",
                        Image = "/assets/media/avatars/student_avatar.png",
                        DepartmentId = deptQuery.FirstOrDefault(x => x.Name == newEmployee.Department).Id
                    };
                    _context.EmployeeInfos.Add(model);
                    _context.SaveChanges();

                    return RedirectToAction("Login");
                }
                else
                {
                    if (check != null)
                    {
                        ModelState.AddModelError(nameof(EmployeeInfoVM.UserName), "This Username is exists, please try another one!");
                    }
                    if(check1 != null)
                    {
                        ModelState.AddModelError(nameof(EmployeeInfoVM.Email), "This Email is exists, please try another one!");
                    }
                    return View(newEmployee);
                }

            }
            return View(newEmployee);
        }

        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ForgotPassword(string username)
        {
            var title = "Reset Password";
            if (username != null)
            {
                var check = _context.EmployeeInfos.Where(d => d.UserName == username).FirstOrDefault();
                var check1 = _context.EmployeeInfos.Where(d => d.Email == username).FirstOrDefault();
                var user = check ?? check1;

                if (user != null)
                {
                    var token = Guid.NewGuid().ToString();
                    user.ResetToken = token;
                    user.ResetTokenExpiry = DateTime.Now.AddMinutes(30);
                    _context.SaveChanges();

                    var resetLink = $"https://{HttpContext.Request.Host}/EmployeeInfo/ResetPassword?token={token}";

                    string message = $@"<p>Dear {user.FullName},</p><br />
                                <p>You have requested to reset your password for the FYP Tracking System.</p>
                                <p>Please click the button below to reset your password. This link is valid for 30 minutes.</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='{resetLink}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#0d9488;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Reset Password</a>
						        </div>";
                    string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
		                        <tbody>
			                        <tr>
				                        <td align=""center"" valign=""center"" style=""text-align:center;"">
						                        <span style=""font-size: 24px; font-weight: bold; color: #0d9488;"">FYP Tracking System</span>
                                        </td>
			                        </tr>
			                        <tr>
				                        <td align=""left"" valign=""center"">
					                        <div style=""text-align:left; margin: 0 0px; padding: 40px; background-color:#ffffff; border-radius: 6px"">
						                        <!--begin:Email content-->
						                        <div style=""padding-bottom: 30px; font-size: 17px;"">
							                        <h3 style=""text-align:center;"">{title}</h3>
						                        </div>
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

                    var email = user.Email;
                    if(!string.IsNullOrEmpty(email))
                        _emailService.Send(email, title, html);

                    TempData["Success"] = "Password reset link has been sent to your email!";
                    ViewBag.Success = "Reset link sent! Please check your email.";
                    return View();
                }
                else
                {
                    TempData["error"] = "Username or Email is invalid!";
                    return View();
                }
            }
            return View();
        }

        [AllowAnonymous]
        public ActionResult ResetPassword(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                ViewBag.Error = "Invalid reset token.";
                return View();
            }

            var user = _context.EmployeeInfos.FirstOrDefault(u => u.ResetToken == token && u.ResetTokenExpiry > DateTime.Now);
            if (user == null)
            {
                ViewBag.Error = "The password reset link is invalid or has expired.";
                return View();
            }

            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult ResetPassword(string token, string password, string confirmPassword)
        {
            if (string.IsNullOrEmpty(token))
            {
                ViewBag.Error = "Invalid reset token.";
                return View();
            }

            var user = _context.EmployeeInfos.FirstOrDefault(u => u.ResetToken == token && u.ResetTokenExpiry > DateTime.Now);
            if (user == null)
            {
                ViewBag.Error = "The password reset link is invalid or has expired.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                ViewBag.Token = token;
                return View();
            }

            user.PassCode = Encryption.GetHash(password);
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            _context.SaveChanges();

            ViewBag.Success = "Password updated successfully! Redirecting to login page...";
            return View();
        }

        public string CreatePassword(int length)
        {
            const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            StringBuilder res = new StringBuilder();
            Random rnd = new Random();
            while (0 < length--)
            {
                res.Append(valid[rnd.Next(valid.Length)]);
            }
            return res.ToString();
        }

        [Authorize]
        public ActionResult Profile(string name)
        {
            var model = _context.EmployeeInfos.Where(d => d.UserName == name).FirstOrDefault();
            if (model != null) {
                if (model.UserType == "SuperAdmin")
                {
                    model.UserType = "SuperAdmin";
                }
                else if (model.UserType == "SuperAdminAssistant")
                {
                    model.UserType = "SuperAdminAssistant";
                }
                else
                {
                    var newUserType = UserTypes.UserTypeList().First(p => p.Value == model.UserType).Text;
                    model.UserType = newUserType;
                }
                return View(model);
            }
            return View();
        }

        [Authorize]
        public ActionResult EditProfile(string name)
        {
            var check = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(d => d.UserName == name).FirstOrDefault();
            if (check != null)
            {
                EmployeeEditVM model = new EmployeeEditVM();
                model.Id = check.Id;
                model.UserName = check.UserName;
                model.IsNotificationOn = check.IsNotificationOn;
                model.FullName = check.FullName;
                model.ShortName = check.ShortName;
                model.PassCode = check.PassCode;
                model.Email = check.Email;
                model.Image = check.Image;
                model.UserType = String.IsNullOrEmpty(check.UserType) ? "" : check.UserType.ToUpper().Trim();
                model.Department = check.EmpDepartment.Name;
                model.EmployeeId = check.EmployeeId;

                return View(model);
            }
            return View();
        }

        [Authorize]
        public ActionResult EditUser(int id)
        {
            var check = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(d => d.Id == id).FirstOrDefault();
            if (check != null)
            {
                EmployeeEditVM model = new EmployeeEditVM();
                model.Id = check.Id;
                model.UserName = check.UserName;
                model.FullName = check.FullName;
                model.ShortName = check.ShortName;
                model.PassCode = check.PassCode;
                model.Email = check.Email;
                model.Image = check.Image;
                model.UserType = String.IsNullOrEmpty(check.UserType) ? "" : check.UserType.ToUpper().Trim();
                model.Department = check.EmpDepartment.Name;
                model.EmployeeId = check.EmployeeId;
                model.IsResign = check.IsResign.ToString();

                return View(model);
            }
            return View();
        }

        [HttpPost]
        public ActionResult EditUser(EmployeeEditVM employeeInfo, IFormFile? picture, string isImgChanged)
        {
            var model = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(d => d.UserName == employeeInfo.UserName).FirstOrDefault();
            var pattern = @"^[a-z0-9](\.?[a-z0-9]){2,}@fyp\.edu\.my$";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            Match m = r.Match(employeeInfo.Email ?? "");
            var deptQuery = _context.Department.AsQueryable();
            var check = _context.EmployeeInfos.Where(x => x.Email == employeeInfo.Email).FirstOrDefault();

            if (!m.Success)
            {
                ModelState.AddModelError("Email", "Please enter the correct email! (xxx@fyp.edu.my)");
            }

            if (!ModelState.IsValid)
            {
                EmployeeEditVM model1 = new EmployeeEditVM();
                model1.Id = model.Id;
                model1.UserName = employeeInfo.UserName;
                model1.FullName = employeeInfo.FullName;
                model1.PassCode = employeeInfo.PassCode;
                model1.Email = employeeInfo.Email;
                model1.Image = employeeInfo.Image;
                model1.UserType = employeeInfo.UserType;
                model1.Image = model.Image;
                model1.Department = model.EmpDepartment.Name;
                model1.EmployeeId = model.EmployeeId;
                model.IsResign = model.IsResign;
                return View(model1);
            }
            if (ModelState.IsValid)
            {
                if (model != null)
                {
                    model.FullName = employeeInfo.FullName;
                    model.IsResign = employeeInfo.IsResign == "on";
                    model.ShortName = employeeInfo.ShortName;
                    model.Email = employeeInfo.Email;
                    if (model.PassCode != employeeInfo.PassCode)
                    {
                        model.PassCode = Encryption.GetHash(employeeInfo.PassCode);
                    }
                    model.UserType = employeeInfo.UserType;
                    model.DepartmentId = deptQuery.Where(x => x.Name == employeeInfo.Department).FirstOrDefault().Id;
                    model.EmployeeId = employeeInfo.EmployeeId;

                    if (isImgChanged == "true")
                    {
                        if (model.Image != "/assets/media/avatars/blank.png")
                        {
                            RemoveImage(model);
                        }
                        var uploadedImg = UploadImageToDir(picture);
                        model.Image = uploadedImg.Image;
                    }
                    _context.EmployeeInfos.Update(model);
                    _context.SaveChanges();
                    return RedirectToAction("Index");
                }
            }

            return View(employeeInfo);


        }

        [HttpPost]
        public ActionResult EditProfile(EmployeeEditVM employeeInfo, IFormFile? picture, string isImgChanged)
        {
            var model = _context.EmployeeInfos.Where(d => d.UserName == employeeInfo.UserName).FirstOrDefault();
            var pattern = @"^[a-z0-9](\.?[a-z0-9]){2,}@fyp\.edu\.my$";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            Match m = r.Match(employeeInfo.Email ?? "");

            var deptQuery = _context.Department.AsQueryable();
            if (!m.Success)
            {
                ModelState.AddModelError("Email", "Please enter the correct email! (xxx@fyp.edu.my)");
                EmployeeEditVM model1 = new EmployeeEditVM();
                model1.Id = model.Id;
                model1.UserName = employeeInfo.UserName;
                model1.FullName = employeeInfo.FullName;
                model1.ShortName = employeeInfo.ShortName;
                model1.PassCode = employeeInfo.PassCode;
                model.IsNotificationOn = employeeInfo.IsNotificationOn;
                model1.Email = employeeInfo.Email;
                model1.Image = employeeInfo.Image;
                model1.UserType = employeeInfo.UserType;
                model1.Image = model.Image;
                model1.EmployeeId = model.EmployeeId;
                model1.Department = employeeInfo.Department;
                return View(model1);
            }

            if (model != null)
            {
                model.FullName = employeeInfo.FullName;
                model.ShortName = employeeInfo.ShortName;
                model.Email = employeeInfo.Email;
                model.FullName = employeeInfo.FullName;
                model.DepartmentId = deptQuery.Where(x => x.Name == employeeInfo.Department).FirstOrDefault().Id;

                if (model.PassCode != employeeInfo.PassCode)
                {
                    model.PassCode = Encryption.GetHash(employeeInfo.PassCode);
                }
                model.UserType = employeeInfo.UserType;
                model.EmployeeId = employeeInfo.EmployeeId;
                model.IsNotificationOn = employeeInfo.IsNotificationOn;

                if (isImgChanged == "true")
                {
                    if (model.Image != "/assets/media/avatars/blank.png")
                    {
                        RemoveImage(model);
                    }
                    var uploadedImg = UploadImageToDir(picture);
                    model.Image = uploadedImg.Image;
                }
                _context.SaveChanges();
                return RedirectToAction("Profile", new { name = employeeInfo.UserName });
            }

            return View(employeeInfo);


        }


        [HttpPost]
        public JsonResult Create(EmployeeInfoVM newEmployee)
        {
            try
            {
                if (newEmployee != null)
                {
                    var check = _context.EmployeeInfos.Where(d => d.UserName == newEmployee.UserName).FirstOrDefault();
                    var check1 = _context.EmployeeInfos.Where(d => d.Email == newEmployee.Email).FirstOrDefault();
                    var deptQuery = _context.Department.AsQueryable();
                    if (check == null && check1 == null)
                    {
                        var model = new EmployeeInfo
                        {
                            UserName = newEmployee.UserName,
                            FullName = newEmployee.FullName,
                            ShortName = newEmployee.ShortName,
                            Email = newEmployee.Email,
                            PassCode = Encryption.GetHash(newEmployee.PassCode),
                            EmployeeId = newEmployee.EmplooyeeInfoID,
                            DateCreated = DateTime.Now,
                            DateJoin = DateTime.Now,
                            AddedBy = "sys",
                            UserType = String.IsNullOrEmpty(newEmployee.UserType) ? "Requestor" : newEmployee.UserType,
                            Image = "/assets/media/avatars/student_avatar.png",
                            DepartmentId = deptQuery.Where(x => x.Name == newEmployee.Department).FirstOrDefault().Id
                    };
                        _context.EmployeeInfos.Add(model);
                        _context.SaveChanges();

                        return Json(new { status = true , msg = "User created successfully!"});
                    }
                    else
                    {
                        if (check != null)
                        {
                            //ModelState.AddModelError(nameof(EmployeeInfoVM.UserName), "This Username is exists, please try another one!");
                            return Json(new { status = false, msg = "This Username is exists, please try another one!" });
                        }
                        if (check1 != null)
                        {
                            //ModelState.AddModelError(nameof(EmployeeInfoVM.Email), "This Email is exists, please try another one!");
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


        public ActionResult Data()
        {
            var empList = _context.EmployeeInfos.ToList();

            return Json(new { data = empList, recordsTotal = empList.Count(), recordsFiltered = empList.Count() });
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public virtual async Task<IActionResult> Login(string username, string password, string returnUrl, bool rmbMe = true)
        {
            if (!await _empService.ValidateEmployeeAsync(username, password))
            {
                TempData["error"] = "Invalid email or password. Please try again.";
                return View();
            }
                
            var emp = await _empService.GetEmpByUserNameAsync(username);

            await _empService.SignInEmployeeAsync(emp, rmbMe);

            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);

            // Redirect based on role
            if (emp.UserType == "Student")
                return RedirectToAction("Index", "Student");
            else if (emp.UserType == "Supervisor")
                return RedirectToAction("Index", "Supervisor");
            else if (emp.UserType == "SuperAdmin" || emp.UserType == "Admin")
                return RedirectToAction("Index", "EmployeeInfo");

            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public virtual async Task<IActionResult> Logout()
        {
            await _authenticationService.SignOutAsync();

            Response.Cookies.Delete(CookieDefaults.CookieImageKey);

            return RedirectToRoute("Login");
        }


        public int UserDepartmentCount(int id)
        {
            var poskuList = new List<object>();
            var count = 0;
            using (var con = new SqlConnection(ConnectionBuilder.GetConnString()))
            {
                try
                {
                    con.Open();

                    SqlCommand cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(DISTINCT DepartmentRoles.DepartmentId) AS DepartmentCount FROM DepartmentRoles " +
                    "JOIN RolesMember ON RolesMember.RolesId = DepartmentRoles.RolesId " +
                    "WHERE RolesMember.EmployeeInfoId = @EmployeeInfoId ";
                    cmd.CommandType = CommandType.Text;
                    cmd.Parameters.Add("@EmployeeInfoId", SqlDbType.VarChar).Value = id;
                    var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        count = (int)reader["DepartmentCount"];
                    }
                    con.Close();
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            return count;
        }


        [HttpPost]
        public bool EditProcess(EmployeeInfo employeeInfo, IFormFile picture, string isImgChanged)
        {
            //var sessionUser = (EmployeeInfo)Session["user"];
            if (ModelState.IsValid)
            {

                var newEmployee = _context.EmployeeInfos.Where(d=> d.Id == employeeInfo.Id).FirstOrDefault();
                string columnChanged = "";
                string valueChanged = "";

                newEmployee.FullName = employeeInfo.FullName;
                newEmployee.Email = employeeInfo.Email;
                if (newEmployee.PassCode != employeeInfo.PassCode)
                {
                    newEmployee.PassCode = Encryption.GetHash(employeeInfo.PassCode);
                }
                newEmployee.UserType = employeeInfo.UserType;

                if (isImgChanged == "true")
                {
                    if (newEmployee.Image != "/assets/media/avatars/blank.png")
                    {
                        RemoveImage(newEmployee);
                    }
                    var uploadedImg = UploadImageToDir(picture);
                    newEmployee.Image = uploadedImg.Image;
                }

                _context.SaveChanges();

                //TempData["success"] = "Successfully saved!";

                return true;
            }

            return false;
        }

        [HttpPost]
        public bool Delete(int id)
        {
            try
            {
                EmployeeInfo employeeInfo = _context.EmployeeInfos.FirstOrDefault(x => x.Id ==id);
                if(employeeInfo != null)
                {
                    employeeInfo.IsResign = true;
                    _context.SaveChanges();
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        
        [Authorize]
        public async Task<IActionResult> Notification()
        {
            var empId = int.Parse(HttpContext.GetUserId());
            DateTime lastMonth = DateTime.Now.AddMonths(-1);

            var viewData = new AuditTrailsViewModel<AuditTag>();

            var query = _context.AuditTag
                .Include(x => x.AuditTrail)
                .Include(x => x.AuditTrail)
                .ThenInclude(x => x.Employee)
                .Where(x => x.EmployeeId == empId && 
            x.CreateAt >= lastMonth)
                .OrderByDescending(x => x.CreateAt)
                .ToList();

            var q1 = query.Where(x => x.CreateAt.Day.Equals(DateTime.Today.Day)).ToList();
            viewData.Today.AddRange(q1);

            //week
            var startDate = DateTime.Now.StartOfWeek(DayOfWeek.Monday);
            var endDate = startDate.AddDays(6);

            var q2 = query.Where(x => x.CreateAt >= startDate && x.CreateAt <= endDate).ToList();
            viewData.Week.AddRange(q2);

            //month
            var q3 = query.Where(x => x.CreateAt.Year == DateTime.Now.Year && x.CreateAt.Month == DateTime.Now.Month).ToList();
            viewData.Month.AddRange(q3);

            return View(viewData);
        }


        public EmployeeInfo UploadImageToDir(IFormFile? picture)
        {
            var newEmployee = new EmployeeInfo();
            if (picture != null)
            {
                var FileDic = "UploadedContent";
                string rootPath = this.Environment.WebRootPath; //wwwroot folder
                string FilePath = System.IO.Path.Combine(rootPath + "/" + FileDic);

                if (!Directory.Exists(FilePath))
                    Directory.CreateDirectory(FilePath);
                var filename = Guid.NewGuid().ToString() + picture.FileName;
                var filePath = System.IO.Path.Combine(FilePath, filename);

                using (FileStream fs = System.IO.File.Create(filePath))
                {
                    picture.CopyTo(fs);
                }

                newEmployee.Image = "/UploadedContent/" + filename;
            }
            return newEmployee;
        }


        private void RemoveImage(EmployeeInfo employeeInfo)
        {
            if (!string.IsNullOrEmpty(employeeInfo.Image))
            {
                string rootPath = this.Environment.WebRootPath;
                var oldPath = rootPath + employeeInfo.Image;

                if (System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Delete(oldPath);
                }
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var user = await _context.EmployeeInfos.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            if (user == null) return Unauthorized();

            if (user.PassCode != Encryption.GetHash(currentPassword))
            {
                TempData["Error"] = "Current password is incorrect.";
                return RedirectToAction("Profile", new { name = user.UserName });
            }

            if (newPassword != confirmPassword)
            {
                TempData["Error"] = "New passwords do not match.";
                return RedirectToAction("Profile", new { name = user.UserName });
            }

            user.PassCode = Encryption.GetHash(newPassword);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Password updated successfully!";
            return RedirectToAction("Profile", new { name = user.UserName });
        }
    }

    public class EmployeeInfoVM
    {
        [Required(ErrorMessage = "The Full Name is Required!")]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "The User Name is Required!")]
        [MinLength(5, ErrorMessage = "The Username should contain at least 5 characters!")]
        public string? UserName { get; set; }

        [Required(ErrorMessage = "The password is Required!")]
        [MinLength(5, ErrorMessage = "The password should contain at least 5 characters!")]
        public string? PassCode { get; set; }

        [Required(ErrorMessage = "The Short Name is Required!")]
        public string? ShortName { get; set; }

        [Required(ErrorMessage = "The email is Required!")]
        [EmailAddress(ErrorMessage = "Please enter correct email address!")]
        public string? Email { get; set; }

        /*        [Required(ErrorMessage = "The Phone No is Required!")]
                [RegularExpression(@"^(\+?6?01)[02-46-9]-*[0-9]{7}$|^(\+?6?01)[1]-*[0-9]{8}$", ErrorMessage = "Please enter correct Phone No!")]
                public string? Contact { get; set; }
        */
        [Required(ErrorMessage = "The Employee ID is Required!")]
        public string? EmplooyeeInfoID { get; set; }

        public string? AddedBy { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateJoin { get; set; }
        public string? Image { get; set; }
        public string? UserType { get; set; }
        public string? Department { get; set; }
    }

    public class EmployeeEditVM
    {

        public int Id { get; set; }

        [Required(ErrorMessage = "The Full Name is Required!")]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "The Short Name is Required!")]
        public string? ShortName { get; set; }
        public string? UserName { get; set; }

        [Required(ErrorMessage = "The password is Required!")]
        [MinLength(5, ErrorMessage = "The password should contain at least 5 characters!")]
        public string? PassCode { get; set; }

        [Required(ErrorMessage = "The Employee ID is Required!")]
        public string? EmployeeId { get; set; }

        [Required(ErrorMessage = "The email is Required!")]
        [EmailAddress(ErrorMessage = "Please enter correct email address!")]
        public string? Email { get; set; }

        public string? Image { get; set; }
        public bool IsNotificationOn { get; set; }
        public string? UserType { get; set; }
        public string? Department { get; set; }
        public string? IsResign { get; set; }
    }
}

