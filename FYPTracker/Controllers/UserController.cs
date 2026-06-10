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
    public class UserController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IUserService _userService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IEmailService _emailService;

        private IWebHostEnvironment Environment;
        public UserController(
            FYPTrackerContext context,
            IWebHostEnvironment _environment,
            IUserService empService,
            IEmailService emailService,
            IAuthenticationService authenticationService
            )
        {
            _context = context;
            Environment = _environment;
            _userService = empService;
            _emailService = emailService;
            _authenticationService = authenticationService;
        }




        
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return RedirectToAction("Login");
        }

        [HttpPost]
        public ActionResult ForgotPassword(string username)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            var title = "Reset Password";
            if (username != null)
            {
                var check = _context.ApplicationUsers.Where(d => d.UserName == username).FirstOrDefault();
                var check1 = _context.ApplicationUsers.Where(d => d.Email == username).FirstOrDefault();
                var user = check ?? check1;

                if (user != null)
                {
                    var token = Guid.NewGuid().ToString();
                    user.ResetToken = token;
                    user.ResetTokenExpiry = DateTime.Now.AddMinutes(30);
                    _context.SaveChanges();

                    var resetLink = $"https://{HttpContext.Request.Host}/User/ResetPassword?token={token}";

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
                    if (!string.IsNullOrEmpty(email))
                    {
                        try
                        {
                            _emailService.Send(email, title, html);
                            if (isAjax) return Json(new { success = true, message = "Password reset link has been sent to your email!" });
                            TempData["Success"] = "Password reset link has been sent to your email!";
                        }
                        catch (Exception)
                        {
                            if (isAjax) return Json(new { success = false, message = "Failed to send reset link due to email server error." });
                            TempData["error"] = "Failed to send reset link due to email server error. Please check configuration.";
                        }
                    }
                    else
                    {
                        if (isAjax) return Json(new { success = false, message = "No email address is associated with this user." });
                        TempData["error"] = "No email address is associated with this user.";
                    }
                    return RedirectToAction("Login");
                }
                else
                {
                    if (isAjax) return Json(new { success = false, message = "Username or Email is invalid!" });
                    TempData["error"] = "Username or Email is invalid!";
                    return RedirectToAction("Login");
                }
            }
            if (isAjax) return Json(new { success = false, message = "Please enter a username or email." });
            return RedirectToAction("Login");
        }

        [AllowAnonymous]
        public ActionResult ResetPassword(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                ViewBag.Error = "Invalid reset token.";
                return View();
            }

            var user = _context.ApplicationUsers.FirstOrDefault(u => u.ResetToken == token && u.ResetTokenExpiry > DateTime.Now);
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

            var user = _context.ApplicationUsers.FirstOrDefault(u => u.ResetToken == token && u.ResetTokenExpiry > DateTime.Now);
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

            var regex = new Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$");
            if (!regex.IsMatch(password))
            {
                ViewBag.Error = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one symbol.";
                ViewBag.Token = token;
                return View();
            }

            if (Encryption.GetHash(password) == user.PassCode)
            {
                ViewBag.Error = "New password cannot be the same as your old password.";
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
            var model = _context.ApplicationUsers.Include(u => u.EmpDepartment).Where(d => d.UserName == name).FirstOrDefault();
            if (model != null) {
                if (model.UserType == "SuperAdmin")
                {
                    model.UserType = "SuperAdmin";
                }

                else
                {
                    var userTypeItem = UserTypes.UserTypeList().FirstOrDefault(p => p.Value == model.UserType);
                    if (userTypeItem != null)
                    {
                        model.UserType = userTypeItem.Text;
                    }
                }
                
                if (model.UserType == "Student")
                {
                    var project = _context.FYPProjects.Include(p => p.Supervisor).FirstOrDefault(p => p.StudentId == model.Id);
                    ViewBag.SupervisorName = project?.Supervisor?.FullName ?? "Not Assigned";
                }
                else if (model.UserType == "Supervisor")
                {
                    var projects = _context.FYPProjects.Include(p => p.Student).Where(p => p.SupervisorId == model.Id).ToList();
                    ViewBag.FilledSlots = projects.Count;
                    ViewBag.AssignedStudents = projects.Select(p => p.Student).Where(s => s != null).ToList();
                }

                return View(model);
            }
            return View();
        }

        [Authorize]
        public ActionResult EditProfile(string name)
        {
            var check = _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
                .Where(d => d.UserName == name).FirstOrDefault();
            if (check != null)
            {
                EmployeeEditVM model = new EmployeeEditVM();
                model.Id = check.Id;
                model.UserName = check.UserName;
                model.IsNotificationOn = check.IsNotificationOn;
                model.FullName = check.FullName;

                model.PassCode = check.PassCode;
                model.Email = check.Email;
                model.Image = check.Image;
                model.UserType = String.IsNullOrEmpty(check.UserType) ? "" : check.UserType.ToUpper().Trim();
                model.Department = check.EmpDepartment?.Name;
                model.EmployeeId = check.EmployeeId;
                
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
        public ActionResult EditProfile(EmployeeEditVM ApplicationUser, IFormFile? picture, string isImgChanged)
        {
            var model = _context.ApplicationUsers.Where(d => d.Id == ApplicationUser.Id).FirstOrDefault();
            var pattern = @"^[a-zA-Z0-9](\.?[a-zA-Z0-9]){2,}@(gmail\.com|student\.newinti\.edu\.my|newinti\.edu\.my)$";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            Match m = r.Match(ApplicationUser.Email ?? "");

            var deptQuery = _context.Department.AsQueryable();
            if (!m.Success)
            {
                var referer = Request.Headers["Referer"].ToString();
                if (referer.Contains("/Profile", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "Please enter a valid email address (e.g., @newinti.edu.my, @student.newinti.edu.my, @gmail.com).";
                    return RedirectToAction("Profile", new { name = ApplicationUser.UserName });
                }
                
                ModelState.AddModelError("Email", "Please enter the correct email! (xxx@gmail.com or xxx@student.newinti.edu.my)");
                EmployeeEditVM model1 = new EmployeeEditVM();
                model1.Id = model.Id;
                model1.UserName = ApplicationUser.UserName;
                model1.FullName = ApplicationUser.FullName;

                model1.PassCode = ApplicationUser.PassCode;
                model.IsNotificationOn = ApplicationUser.IsNotificationOn;
                model1.Email = ApplicationUser.Email;
                model1.Image = ApplicationUser.Image;
                model1.UserType = ApplicationUser.UserType;
                model1.Image = model.Image;
                model1.EmployeeId = model.EmployeeId;

                return View(model1);
            }

            if (model != null)
            {
                model.FullName = ApplicationUser.FullName;

                model.Email = ApplicationUser.Email;
                model.FullName = ApplicationUser.FullName;
                model.DepartmentId = deptQuery.Where(x => x.Name == ApplicationUser.Department).FirstOrDefault()?.Id;

                if (model.PassCode != ApplicationUser.PassCode)
                {
                    model.PassCode = Encryption.GetHash(ApplicationUser.PassCode);
                }
                // Preserve original UserType from DB — never let the form overwrite it
                // (prevents a Student accidentally saving themselves as Admin etc.)
                // model.UserType is unchanged (kept from DB)
                model.EmployeeId = ApplicationUser.EmployeeId;
                model.IsNotificationOn = ApplicationUser.IsNotificationOn;
                
                // New profile fields
                model.Phone = ApplicationUser.Phone;
                // Use null-guard: only overwrite if submitted value is non-empty
                // This prevents readonly display fields from wiping existing data
                if (model.UserType == "Supervisor")
                {
                    if (!string.IsNullOrEmpty(ApplicationUser.Title))
                        model.Title = ApplicationUser.Title;
                    if (ApplicationUser.MaxStudentSlots > 0)
                    {
                        model.MaxStudentSlots = ApplicationUser.MaxStudentSlots;
                    }
                }
                else if (model.UserType == "Student")
                {
                    if (!string.IsNullOrEmpty(ApplicationUser.Programme))
                        model.Programme = ApplicationUser.Programme;
                    if (!string.IsNullOrEmpty(ApplicationUser.AcademicSession))
                        model.AcademicSession = ApplicationUser.AcademicSession;
                }

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
                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction("Profile", new { name = ApplicationUser.UserName });
            }

            var fallbackReferer = Request.Headers["Referer"].ToString();
            if (fallbackReferer.Contains("/Profile", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "User could not be found to update.";
                return RedirectToAction("Profile", new { name = ApplicationUser.UserName });
            }

            return View(ApplicationUser);


        }




        
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public virtual async Task<IActionResult> Login(string username, string password, string returnUrl, bool rmbMe = true)
        {
            if (!await _userService.ValidateEmployeeAsync(username, password))
            {
                TempData["error"] = "Invalid email or password. Please try again.";
                return View();
            }
                
            var emp = await _userService.GetEmpByUserNameAsync(username);

            await _userService.SignInEmployeeAsync(emp, rmbMe);

            if (emp.RequirePasswordChange)
            {
                return RedirectToAction("ForceChangePassword", "User");
            }

            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);

            // Redirect based on role
            if (emp.UserType == "Student")
                return RedirectToAction("Index", "Student");
            else if (emp.UserType == "Supervisor")
                return RedirectToAction("Index", "Supervisor");
            else if (emp.UserType == "SuperAdmin" || emp.UserType == "Admin")
                return RedirectToAction("ManageUsers", "Admin");

            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public virtual async Task<IActionResult> Logout()
        {
            await _authenticationService.SignOutAsync();

            Response.Cookies.Delete(CookieDefaults.CookieImageKey);

            return RedirectToRoute("Login");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ForceChangePassword()
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login");
            if (!user.RequirePasswordChange) return RedirectToAction("Index", "Home");

            ViewBag.User = user;
            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ForceChangePassword(string newPassword, string confirmPassword, string fullName, string phone, string programme, string academicSession, string title)
        {
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
            if (user == null) return RedirectToAction("Login");
            if (!user.RequirePasswordChange) return RedirectToAction("Index", "Home");

            ViewBag.User = user;

            if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match or are empty.";
                return View();
            }

            if (string.IsNullOrEmpty(fullName))
            {
                ViewBag.Error = "Full Name is required.";
                return View();
            }

            user.PassCode = Encryption.GetHash(newPassword);
            user.RequirePasswordChange = false;
            user.FullName = fullName;
            user.Phone = phone;

            if (user.UserType == "Student")
            {
                user.Programme = programme;
                user.AcademicSession = academicSession;
            }
            else if (user.UserType == "Supervisor")
            {
                user.Title = title;
            }
            
            _context.ApplicationUsers.Update(user);
            await _context.SaveChangesAsync();

            if (user.UserType == "Student")
                return RedirectToAction("Index", "Student");
            else if (user.UserType == "Supervisor")
                return RedirectToAction("Index", "Supervisor");
            else if (user.UserType == "SuperAdmin" || user.UserType == "Admin")
                return RedirectToAction("ManageUsers", "Admin");

            return RedirectToAction("Index", "Home");
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
                    "WHERE RolesMember.ApplicationUserId = @ApplicationUserId ";
                    cmd.CommandType = CommandType.Text;
                    cmd.Parameters.Add("@ApplicationUserId", SqlDbType.VarChar).Value = id;
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
        public bool EditProcess(ApplicationUser ApplicationUser, IFormFile picture, string isImgChanged)
        {
            //var sessionUser = (ApplicationUser)Session["user"];
            if (ModelState.IsValid)
            {

                var newEmployee = _context.ApplicationUsers.Where(d=> d.Id == ApplicationUser.Id).FirstOrDefault();
                string columnChanged = "";
                string valueChanged = "";

                newEmployee.FullName = ApplicationUser.FullName;
                newEmployee.Email = ApplicationUser.Email;
                if (newEmployee.PassCode != ApplicationUser.PassCode)
                {
                    newEmployee.PassCode = Encryption.GetHash(ApplicationUser.PassCode);
                }
                newEmployee.UserType = ApplicationUser.UserType;
                newEmployee.IsResign = Convert.ToBoolean(ApplicationUser.IsResign);

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


        
        public ApplicationUser UploadImageToDir(IFormFile? picture)
        {
            var newEmployee = new ApplicationUser();
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


        private void RemoveImage(ApplicationUser ApplicationUser)
        {
            if (!string.IsNullOrEmpty(ApplicationUser.Image))
            {
                string rootPath = this.Environment.WebRootPath;
                var oldPath = rootPath + ApplicationUser.Image;

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
            var user = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == HttpContext.User.Identity.Name);
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

    public class ApplicationUserVM
    {
        [Required(ErrorMessage = "The Full Name is Required!")]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "The User Name is Required!")]
        [MinLength(5, ErrorMessage = "The Username should contain at least 5 characters!")]
        public string? UserName { get; set; }

        [Required(ErrorMessage = "The password is Required!")]
        [MinLength(5, ErrorMessage = "The password should contain at least 5 characters!")]
        public string? PassCode { get; set; }

        //[Required(ErrorMessage = "The Short Name is Required!")]


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
        // Student-specific
        public string? Programme { get; set; }
        public string? AcademicSession { get; set; }
        // Supervisor-specific
        public string? Title { get; set; }
        public string? Phone { get; set; }
    }

    public class EmployeeEditVM
    {

        public int Id { get; set; }

        [Required(ErrorMessage = "The Full Name is Required!")]
        public string? FullName { get; set; }

        //[Required(ErrorMessage = "The Short Name is Required!")]

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
        
        // New Profile Fields
        public string? Phone { get; set; }
        public string? Programme { get; set; }
        public string? AcademicSession { get; set; }
        public string? Title { get; set; }
        public int MaxStudentSlots { get; set; }
    }
}




















