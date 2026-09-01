using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;
using System;

namespace FYPTracker.Controllers
{
    [Route("[controller]")]
    public class SeedController : Controller
    {
        private readonly FYPTrackerContext _context;

        public SeedController(FYPTrackerContext context)
        {
            _context = context;
        }

        [HttpGet("run")]
        public async Task<IActionResult> Run()
        {
            try
            {
                // Create dummy examiner if not exists
                var examiner = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "EXM-001");
                if (examiner == null)
                {
                    examiner = new ApplicationUser { UserName = "EXM-001", Email = "examiner@test.com", FullName = "Dr. Dummy Examiner", UserType = "Examiner", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false, IsNotificationOn = false };
                    _context.ApplicationUsers.Add(examiner);
                    await _context.SaveChangesAsync();
                }

                // Create dummy supervisor if not exists
                var supervisor = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "SUP-001");
                if (supervisor == null)
                {
                    supervisor = new ApplicationUser { UserName = "SUP-001", Email = "super@test.com", FullName = "Dr. Super Visor", UserType = "Supervisor", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false, IsNotificationOn = false };
                    _context.ApplicationUsers.Add(supervisor);
                    await _context.SaveChangesAsync();
                }

                // Create dummy student if not exists
                var student = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == "STU-001");
                if (student == null)
                {
                    student = new ApplicationUser { UserName = "STU-001", Email = "student@test.com", FullName = "Johnny Student", EmployeeId = "S123456", UserType = "Student", PassCode = FYPTracker.Common.Encryption.GetHash("123456"), DateCreated = DateTime.Now, RequirePasswordChange = false, IsResign = false, IsNotificationOn = false };
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
    }
}
