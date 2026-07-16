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
                .FirstOrDefaultAsync(x => x.UserName == currentUsername);

            if (getUser == null)
            {
                return RedirectToAction("Login", "User");
            }

            ViewBag.CurrentUser = getUser;

            // Fetch FYP projects based on user type
            List<FYPProject> projects;
            if (getUser.UserType == "Admin" || getUser.UserType == "SuperAdmin")
            {
                return RedirectToAction("Index", "Admin");
            }
            else if (getUser.UserType == "Supervisor")
            {
                projects = await _context.FYPProjects.Include(p => p.Student).Include(p => p.Supervisor).Where(p => p.SupervisorId == getUser.Id).ToListAsync();
            }
            else // Student
            {
                projects = await _context.FYPProjects.Include(p => p.Student).Include(p => p.Supervisor).Where(p => p.StudentId == getUser.Id).ToListAsync();
            }

            return View(projects);
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

