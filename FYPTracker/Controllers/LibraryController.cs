using FYPTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FYPTracker.Controllers
{
    [Authorize]
    public class LibraryController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _environment;

        public LibraryController(FYPTrackerContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public async Task<IActionResult> Index()
        {
            var projects = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Where(p => p.FinalGrade != null && (p.FinalGrade.Contains("Excellent") || p.FinalGrade.Contains("Good") || p.FinalGrade.Contains("Average")))
                .OrderByDescending(p => p.UpdatedDate ?? p.CreatedDate)
                .ToListAsync();

            return View(projects);
        }

        public async Task<IActionResult> Details(int id)
        {
            var project = await _context.FYPProjects
                .Include(p => p.Student)
                .Include(p => p.Supervisor)
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.SubmissionFiles)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null || string.IsNullOrEmpty(project.FinalGrade))
            {
                TempData["Error"] = "Project not found or not available in the library.";
                return RedirectToAction(nameof(Index));
            }

            return View(project);
        }

        public async Task<IActionResult> DownloadFile(int fileId)
        {
            var fileRecord = await _context.FYPSubmissionFiles
                .Include(f => f.Milestone)
                    .ThenInclude(m => m.Project)
                .FirstOrDefaultAsync(f => f.Id == fileId);

            if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Milestone?.Project?.FinalGrade))
            {
                return NotFound("File not found or project is not available in the library.");
            }

            string filePath = Path.Combine(_environment.WebRootPath, fileRecord.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Physical file not found on the server.");
            }

            string contentType = "application/octet-stream";
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".pdf") contentType = "application/pdf";
            else if (extension == ".doc" || extension == ".docx") contentType = "application/msword";
            else if (extension == ".xlsx") contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            string finalName = fileRecord.FileName + (string.IsNullOrEmpty(Path.GetExtension(fileRecord.FileName)) ? extension : "");
            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{finalName}\"");
            return PhysicalFile(filePath, contentType);
        }
    }
}
