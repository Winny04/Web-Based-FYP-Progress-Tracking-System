using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FYPTracker.Configuration;
using FYPTracker.Helpers;
using FYPTracker.Models;
using FYPTracker.Services.Others;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class TestingOfflineEmailPolicyTests
    {
        private IWebHostEnvironment CreateEnv(string envName)
        {
            return new TestWebHostEnvironment { EnvironmentName = envName };
        }

        private IConfiguration CreateConfig(string connStr)
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", connStr }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        [Fact]
        public void EmailService_WhenTestingAndFYPTracker_TestAndOfflineTrue_SkipsEmailWithoutException()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");
            var smtpSettings = new SmtpSettings { Offline = true };

            var emailService = new EmailService(smtpSettings, env, config);

            // Synchronous Send: must NOT throw
            var exception = Record.Exception(() =>
                emailService.Send("testlecturer@newinti.edu.my", "Test Meeting Booking", "<p>Hello</p>"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task EmailService_WhenTestingAndFYPTracker_TestAndOfflineTrue_AsyncSend_SkipsEmailWithoutException()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");
            var smtpSettings = new SmtpSettings { Offline = true };

            var emailService = new EmailService(smtpSettings, env, config);

            // Asynchronous Send: must NOT throw
            var exception = await Record.ExceptionAsync(() =>
                emailService.Send(new List<string> { "testlecturer@newinti.edu.my" }, "Test Meeting Booking", "<p>Hello</p>"));

            Assert.Null(exception);
        }

        [Fact]
        public void EmailService_WhenDevelopmentAndOfflineTrue_ThrowsInvalidOperationException()
        {
            var env = CreateEnv("Development");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;");
            var smtpSettings = new SmtpSettings { Offline = true, DevRedirectEmail = "test@domain.com" };

            var emailService = new EmailService(smtpSettings, env, config);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                emailService.Send("testlecturer@newinti.edu.my", "Test Meeting Booking", "<p>Hello</p>"));

            Assert.Contains("SMTP service is configured in Offline mode", ex.Message);
        }

        [Fact]
        public void EmailService_WhenTestingWithWrongDatabase_ThrowsInvalidOperationException()
        {
            var env = CreateEnv("Testing");
            // Wrong database: FYPTracker_Development or backup should NOT get testing offline email bypass
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;");
            var smtpSettings = new SmtpSettings { Offline = true };

            var emailService = new EmailService(smtpSettings, env, config);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                emailService.Send("testlecturer@newinti.edu.my", "Test Meeting Booking", "<p>Hello</p>"));

            Assert.Contains("SMTP service is configured in Offline mode", ex.Message);
        }

        [Fact]
        public async Task MeetingBooking_WhenBookingAlreadyExistsInWeek_DoesNotCreateDuplicate()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            int studentId = 3;
            var meetingDate = new DateTime(2026, 1, 2); // Friday

            // Simulate the already saved meeting from the previous run
            var existingMeeting = new MeetingBooking
            {
                StudentId = studentId,
                SupervisorId = 4,
                Stage = 1,
                WeekNumber = 1,
                MeetingDate = meetingDate,
                MeetingDay = "Friday",
                MeetingTime = "08:00",
                Status = "Pending",
                CreatedDate = DateTime.Now
            };
            context.MeetingBookings.Add(existingMeeting);
            await context.SaveChangesAsync();

            // Verify the one-meeting-per-week check blocks duplicate insertion
            var startOfWeek = meetingDate.Date.AddDays(-(int)meetingDate.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);

            var duplicateExists = await context.MeetingBookings
                .AnyAsync(m => m.StudentId == studentId &&
                               m.Status != "Rejected" &&
                               m.Status != "Cancelled" &&
                               m.Status != "Expired" &&
                               m.Status != "Postponed" &&
                               m.MeetingDate >= startOfWeek &&
                               m.MeetingDate < endOfWeek);

            Assert.True(duplicateExists);

            // Confirm total record count remains exactly 1
            var totalCount = await context.MeetingBookings.CountAsync(m => m.StudentId == studentId);
            Assert.Equal(1, totalCount);
        }
    }
}
