using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FYPTracker.Services.BackgroundTasks
{
    /// <summary>
    /// Background service that runs once per day and auto-rejects any supervisor requests
    /// that have been Pending beyond the configured timeout period.
    ///
    /// The timeout period (in days) is read from the FYPSettings table using the key
    /// "SupervisorRequestTimeoutDays". If the setting is missing or invalid, it defaults
    /// to 3 days as per the current business requirement.
    ///
    /// After auto-rejection:
    ///  - The request Status is set to "Rejected"
    ///  - A ResponseNote records that it was auto-rejected and how many days elapsed
    ///  - An in-app notification is sent to the student so they know they can re-apply
    ///  - An email is sent to the student informing them of the timeout
    /// </summary>
    public class SupervisorRequestTimeoutService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SupervisorRequestTimeoutService> _logger;

        // Fallback default: 3 days (matches business requirement when DB setting is absent)
        private const int DefaultTimeoutDays = 3;

        public SupervisorRequestTimeoutService(
            IServiceProvider serviceProvider,
            ILogger<SupervisorRequestTimeoutService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait 30 seconds after app startup before the first run,
            // giving the DB time to become fully available.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessTimeoutsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SupervisorRequestTimeoutService] Unhandled error during timeout processing.");
                }

                // Run once every 24 hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task ProcessTimeoutsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<FYPTrackerContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            // ── Step 1: Read the configurable timeout from the settings table ──────────
            int timeoutDays = DefaultTimeoutDays;
            var setting = await db.FYPSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "SupervisorRequestTimeoutDays");

            if (setting != null && int.TryParse(setting.Value, out int parsed) && parsed > 0)
            {
                timeoutDays = parsed;
            }
            else
            {
                _logger.LogWarning(
                    "[SupervisorRequestTimeoutService] 'SupervisorRequestTimeoutDays' setting not found or invalid. " +
                    "Using default of {DefaultDays} days.", DefaultTimeoutDays);
            }

            _logger.LogInformation(
                "[SupervisorRequestTimeoutService] Running timeout check. Configured timeout = {Days} day(s).",
                timeoutDays);

            var cutoff = DateTime.Now.AddDays(-timeoutDays);

            // ── Step 2: Find all Pending requests whose RequestedDate is older than the cutoff ──
            // We use RequestedDate (not ExpiryDate) as the authoritative clock so that
            // any old requests created before the ExpiryDate column was reliable are
            // still caught by this service.
            var timedOutRequests = await db.SupervisorRequests
                .Include(r => r.Student)
                .Include(r => r.Supervisor)
                .Where(r => r.Status == "Pending" && r.RequestedDate <= cutoff)
                .ToListAsync();

            if (!timedOutRequests.Any())
            {
                _logger.LogInformation("[SupervisorRequestTimeoutService] No timed-out requests found.");
                return;
            }

            _logger.LogInformation(
                "[SupervisorRequestTimeoutService] Found {Count} timed-out request(s) to reject.",
                timedOutRequests.Count);

            foreach (var req in timedOutRequests)
            {
                // ── Mark as Rejected ─────────────────────────────────────────────────
                req.Status       = "Rejected";
                req.RespondedDate = DateTime.Now;
                req.ResponseNote = $"Auto-rejected: No supervisor response within the configured {timeoutDays}-day period.";

                // ── In-app notification to the student ───────────────────────────────
                if (req.Student != null)
                {
                    db.FYPNotifications.Add(new FYPNotification
                    {
                        RecipientId = req.StudentId,
                        Title       = "Supervisor Request Timed Out",
                        Message     = $"Your request to {req.Supervisor?.FullName ?? "the supervisor"} " +
                                      $"was not responded to within {timeoutDays} day(s) and has been automatically rejected. " +
                                      $"You may select a new supervisor.",
                        CreatedDate = DateTime.Now,
                        IsRead      = false,
                        Type        = "Warning"
                    });

                    // ── Email notification to the student ────────────────────────────
                    if (!string.IsNullOrEmpty(req.Student.Email))
                    {
                        try
                        {
                            string subject = "FYP Supervisor Request: No Response – Auto Rejected";
                            string body =
                                $"<p>Dear {req.Student.FullName},</p>" +
                                $"<p>Your FYP supervisor request to <strong>{req.Supervisor?.FullName ?? "the supervisor"}</strong> " +
                                $"has not received a response within the allowed <strong>{timeoutDays} day(s)</strong>.</p>" +
                                $"<p>The request has been <strong>automatically rejected</strong> so you can proceed to select another supervisor.</p>" +
                                $"<p>Please log in to the FYP Tracking System and submit a new request to a different supervisor.</p>" +
                                $"<p>If you believe this is an error, please contact the FYP coordinator.</p>";

                            await emailService.Send(
                                new System.Collections.Generic.List<string> { req.Student.Email },
                                subject,
                                body);
                        }
                        catch (Exception mailEx)
                        {
                            // Do not let a mail failure block the DB save
                            _logger.LogWarning(mailEx,
                                "[SupervisorRequestTimeoutService] Could not send email to student {StudentId}.",
                                req.StudentId);
                        }
                    }
                }
            }

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "[SupervisorRequestTimeoutService] Auto-rejected {Count} request(s) successfully.",
                timedOutRequests.Count);
        }
    }
}
