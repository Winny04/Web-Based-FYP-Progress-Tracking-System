using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;
using Quartz;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Common;

namespace FYPTracker.Services.Quartz
{
    /// <summary>
    /// Quartz background job: sends reminder emails for overdue FYP milestones.
    /// </summary>
    public class SendEmailJobs : IJob
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;

        public SendEmailJobs(FYPTrackerContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await SendOverdueMilestoneReminders();
        }

        /// <summary>
        /// Emails supervisors about milestones that are past their due date and still Pending/Submitted.
        /// </summary>
        private async Task SendOverdueMilestoneReminders()
        {
            try
            {
                var today = DateTime.Now.Date;

                var overdueMilestones = await _context.FYPMilestones
                    .Include(m => m.Project)
                        .ThenInclude(p => p!.Supervisor)
                    .Include(m => m.Project)
                        .ThenInclude(p => p!.Student)
                    .Where(m =>
                        m.DueDate.HasValue &&
                        m.DueDate.Value < today &&
                        (m.Status == "Pending" || m.Status == "Submitted"))
                    .ToListAsync();

                if (!overdueMilestones.Any()) return;

                // Group by supervisor
                var bySupervisor = overdueMilestones
                    .Where(m => m.Project?.Supervisor?.Email != null)
                    .GroupBy(m => m.Project!.Supervisor!.Email!);

                foreach (var group in bySupervisor)
                {
                    var supervisorEmail = group.Key;
                    var supervisorName  = group.First().Project!.Supervisor!.FullName ?? "Supervisor";
                    var html = BuildOverdueEmailHtml(supervisorName, group.ToList());
                    await _emailService.Send(
                        new List<string> { supervisorEmail },
                        "FYP Milestone Overdue Reminder",
                        html);
                }
            }
            catch (Exception ex)
            {
                // Log and swallow so the scheduler keeps running
                Console.WriteLine($"[SendEmailJobs] Error: {ex.Message}");
            }
        }

        private string BuildOverdueEmailHtml(string supervisorName, List<FYPMilestone> milestones)
        {
            var rows = string.Join("", milestones.Select(m =>
                $"<tr>" +
                $"<td style='border:1px solid #ddd;padding:8px'>{m.Project?.Title}</td>" +
                $"<td style='border:1px solid #ddd;padding:8px'>{m.Title}</td>" +
                $"<td style='border:1px solid #ddd;padding:8px'>{m.Project?.Student?.FullName}</td>" +
                $"<td style='border:1px solid #ddd;padding:8px'>{m.DueDate:dd MMM yyyy}</td>" +
                $"<td style='border:1px solid #ddd;padding:8px'>{m.Status}</td>" +
                $"</tr>"));

            return $@"
<div style='font-family:Arial,sans-serif;font-size:15px;color:#333;max-width:800px;margin:auto'>
  <h2 style='color:#d9534f'>⚠ FYP Milestone Overdue Reminder</h2>
  <p>Dear {supervisorName},</p>
  <p>The following milestones under your supervision are past their due date:</p>
  <table style='border-collapse:collapse;width:100%'>
    <thead>
      <tr style='background:#f5f5f5'>
        <th style='border:1px solid #ddd;padding:8px'>Project</th>
        <th style='border:1px solid #ddd;padding:8px'>Milestone</th>
        <th style='border:1px solid #ddd;padding:8px'>Student</th>
        <th style='border:1px solid #ddd;padding:8px'>Due Date</th>
        <th style='border:1px solid #ddd;padding:8px'>Status</th>
      </tr>
    </thead>
    <tbody>{rows}</tbody>
  </table>
  <p style='margin-top:20px'>Please follow up with the respective students.</p>
  <p>— FYP Progress Tracker System</p>
</div>";
        }
    }
}
