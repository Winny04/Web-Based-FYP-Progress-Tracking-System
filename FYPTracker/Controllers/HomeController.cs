using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly FYPTrackerContext _context;

        public HomeController(ILogger<HomeController> logger, FYPTrackerContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var currentUsername = HttpContext.User.Identity?.Name;
            var getUser = await _context.ApplicationUsers
                .Include(x => x.EmpDepartment)
                .Include(x => x.UserRoles).ThenInclude(r => r.Role)
                .FirstOrDefaultAsync(x => x.UserName == currentUsername);

            if (getUser == null)
            {
                return RedirectToAction("Login", "User");
            }

            ViewBag.CurrentUser = getUser;

            var activeRole = HttpContext.Session.GetString("ActiveRole");

            // Fetch FYP projects based on user type / active role
            if (activeRole == "Admin" || activeRole == "SuperAdmin" || getUser.UserRoles.Any(r => r.Role.Name == "Admin" || r.Role.Name == "SuperAdmin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            else if (activeRole == "Supervisor" || (string.IsNullOrEmpty(activeRole) && getUser.UserRoles.Any(r => r.Role.Name == "Supervisor")))
            {
                return RedirectToAction("Index", "Supervisor");
            }
            else if (activeRole == "Examiner" || (string.IsNullOrEmpty(activeRole) && getUser.UserRoles.Any(r => r.Role.Name == "Examiner")))
            {
                return RedirectToAction("Index", "Examiner");
            }
            else // Student
            {
                return RedirectToAction("Index", "Student");
            }
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public IActionResult Error404()
        {
            return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}

