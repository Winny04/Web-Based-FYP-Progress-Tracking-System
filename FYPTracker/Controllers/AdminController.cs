using FYPTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Linq;

namespace FYPTracker.Controllers
{
    public class AdminController : Controller
    {
        private readonly FYPTrackerContext _context;

        public AdminController(FYPTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Calculate Stats
            var users = await _context.EmployeeInfos.ToListAsync();
            ViewBag.TotalUsers = users.Count;
            ViewBag.TotalStudents = users.Count(u => u.UserType == "Student");
            ViewBag.TotalSupervisors = users.Count(u => u.UserType == "Supervisor");

            var projects = await _context.FYPProjects.Include(p => p.Milestones).ToListAsync();
            ViewBag.ActiveProjects = projects.Count;
            
            ViewBag.PendingApprovals = await _context.SupervisorRequests.CountAsync(r => r.Status == "Pending");

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
                csv.AppendLine($"{p.Id},{p.Student?.FullName},{p.Student?.EmployeeId},{p.Supervisor?.FullName},{completedCount}/3,{p.Status}");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(csv.ToString());
            return File(buffer, "text/csv", $"FYP_Summary_Report_{DateTime.Now:yyyyMMdd}.csv");
        }
    }
}
