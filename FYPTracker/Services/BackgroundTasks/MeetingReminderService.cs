using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FYPTracker.Services.BackgroundTasks
{
    public class MeetingReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MeetingReminderService> _logger;

        public MeetingReminderService(IServiceProvider serviceProvider, ILogger<MeetingReminderService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try 
            {
                // Wait for application to fully start before running
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckMeetingsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing MeetingReminderService.");
                }

                // Check every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckMeetingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FYPTrackerContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var clock = scope.ServiceProvider.GetService(typeof(FYPTracker.Services.Time.ISystemClock)) as FYPTracker.Services.Time.ISystemClock;

            var now = clock?.Now ?? DateTime.Now;

            var tomorrow = now.AddDays(1).Date;

            // Fetch Approved meetings that haven't sent a reminder yet and are tomorrow
            var upcomingMeetings = await dbContext.MeetingBookings
                .Include(m => m.Student)
                .Include(m => m.Supervisor)
                .Where(m => m.Status == "Approved" && !m.ReminderSent && m.MeetingDate.Date == tomorrow)
                .ToListAsync();

            foreach (var meeting in upcomingMeetings)
            {
                if (string.IsNullOrEmpty(meeting.MeetingTime)) continue;

                if (DateTime.TryParse($"{meeting.MeetingDate:yyyy-MM-dd} {meeting.MeetingTime}", out DateTime meetingStart))
                {
                    var timeDifference = meetingStart - now;

                    // If the meeting starts in 24 hours or less (but hasn't started yet)
                    if (timeDifference.TotalHours <= 24 && timeDifference.TotalHours > 0)
                    {
                        // 1. Send Email to Student
                        if (!string.IsNullOrEmpty(meeting.Student?.Email))
                        {
                            string subject = "FYP Meeting Reminder: Tomorrow";
                            string message = $"<p>Dear {meeting.Student.FullName},</p><br/>" +
                                          $"<p>This is a reminder that your FYP meeting with your supervisor, {meeting.Supervisor?.FullName}, is scheduled for tomorrow.</p>" +
                                          $"<p><strong>Time:</strong> {meetingStart:dd MMM yyyy, hh:mm tt}<br/>" +
                                          $"<strong>Location:</strong> {meeting.Location ?? "TBA"}</p>" +
                                          $"<p>Please be prepared.</p>";
                            string html = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message);
                            await emailService.Send(new System.Collections.Generic.List<string> { meeting.Student.Email }, subject, html);
                        }

                        // 2. Send Email to Supervisor
                        if (!string.IsNullOrEmpty(meeting.Supervisor?.Email))
                        {
                            string subject = "FYP Meeting Reminder: Tomorrow";
                            string message = $"<p>Dear {meeting.Supervisor.FullName},</p><br/>" +
                                          $"<p>This is a reminder that your FYP meeting with your student, {meeting.Student?.FullName}, is scheduled for tomorrow.</p>" +
                                          $"<p><strong>Time:</strong> {meetingStart:dd MMM yyyy, hh:mm tt}<br/>" +
                                          $"<strong>Location:</strong> {meeting.Location ?? "TBA"}</p>";
                            string html = FYPTracker.Helpers.EmailTemplateHelper.GetStandardizedTemplate(subject, message);
                            await emailService.Send(new System.Collections.Generic.List<string> { meeting.Supervisor.Email }, subject, html);
                        }

                        // 3. Create In-App Notifications
                        if (meeting.Student != null)
                        {
                            dbContext.FYPNotifications.Add(new FYPNotification
                            {
                                RecipientId = meeting.Student.Id,
                                Title = "Meeting Reminder",
                                Message = $"Your meeting with {meeting.Supervisor?.FullName} is tomorrow at {meeting.Location ?? "TBA"}.",
                                CreatedDate = now,
                                IsRead = false,
                                Type = "Info"
                            });
                        }

                        if (meeting.Supervisor != null)
                        {
                            dbContext.FYPNotifications.Add(new FYPNotification
                            {
                                RecipientId = meeting.Supervisor.Id,
                                Title = "Meeting Reminder",
                                Message = $"Your meeting with {meeting.Student?.FullName} is tomorrow at {meeting.Location ?? "TBA"}.",
                                CreatedDate = now,
                                IsRead = false,
                                Type = "Info"
                            });
                        }

                        // 4. Mark as sent
                        meeting.ReminderSent = true;
                    }
                }
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
