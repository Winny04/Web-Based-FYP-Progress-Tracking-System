using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FYPTracker.Helpers;
using FYPTracker.Models;
using FYPTracker.Services.Defense;
using FYPTracker.Services.Time;
using FYPTracker.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class IAFDefenseOutcomeRecordingTests
    {
        private IWebHostEnvironment CreateTestingEnv()
        {
            return new TestWebHostEnvironment { EnvironmentName = "Testing" };
        }

        private IConfiguration CreateTestingConfig()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;" }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        private FYPTrackerContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new FYPTrackerContext(options);
        }

        private (FYPMilestone milestone, IAFDefenseBooking booking) SeedMilestoneAndBooking(
            FYPTrackerContext context,
            int attemptNumber = 1,
            DateTime? defenseDate = null,
            TimeSpan? startTime = null,
            TimeSpan? endTime = null)
        {
            var student = new ApplicationUser
            {
                Id = 101,
                UserName = "stu01",
                FullName = "Student Test",
                EmployeeId = "S101"
            };

            var examiner = new ApplicationUser
            {
                Id = 201,
                UserName = "exm01",
                FullName = "Examiner Test",
                EmployeeId = "E201"
            };

            var session = new AcademicSession
            {
                Id = 1,
                Name = "2026/2027 Semester 1",
                IsCurrent = true
            };

            var project = new FYPProject
            {
                Id = 1,
                Title = "Cloud Security Tracking",
                StudentId = student.Id,
                AcademicSession = session.Name
            };

            var attempts = new List<IafAttempt>();
            for (int i = 1; i <= attemptNumber; i++)
            {
                attempts.Add(new IafAttempt
                {
                    AttemptNumber = i,
                    Status = i == attemptNumber ? "DefenseScheduled" : "PanelRevisionRequired",
                    FormData = new Dictionary<string, string> { { "title", "Cloud Security Tracking" } }
                });
            }

            var attemptHistory = new IafAttemptHistory
            {
                CurrentAttempt = attemptNumber,
                Attempts = attempts
            };

            var milestone = new FYPMilestone
            {
                Id = 10,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                FYPProjectId = project.Id,
                Project = project,
                Status = "DefenseScheduled",
                Remarks = $"Form: Initial Project Allocation Form (IAF). Details: Initial Project Allocation Form (IAF). JSON: {JsonSerializer.Serialize(attemptHistory)}"
            };

            var dDate = defenseDate ?? new DateTime(2026, 10, 30);
            var sTime = startTime ?? new TimeSpan(10, 0, 0);
            var eTime = endTime ?? new TimeSpan(11, 0, 0);

            var booking = new IAFDefenseBooking
            {
                Id = 2,
                MilestoneId = milestone.Id,
                Milestone = milestone,
                AttemptNumber = attemptNumber,
                AcademicSessionId = session.Id,
                AcademicSession = session,
                ExaminerId = examiner.Id,
                Examiner = examiner,
                DefenseDate = dDate,
                StartTime = sTime,
                EndTime = eTime,
                DurationMinutes = 60,
                Venue = "Room B-201",
                BookingStatus = "Scheduled",
                AcademicOutcome = "Pending"
            };

            context.ApplicationUsers.AddRange(student, examiner);
            context.AcademicSessions.Add(session);
            context.FYPProjects.Add(project);
            context.FYPMilestones.Add(milestone);
            context.IAFDefenseBookings.Add(booking);
            context.SaveChanges();

            return (milestone, booking);
        }

        [Fact]
        public async Task Problem1_RecordDefenseOutcome_UnderTestingClock_BlockedBeforeEnd_AllowedAfterEnd()
        {
            using var context = CreateInMemoryContext();
            var defenseDate = new DateTime(2026, 10, 30);
            var startTime = new TimeSpan(10, 0, 0);
            var endTime = new TimeSpan(11, 0, 0);

            var (milestone, _) = SeedMilestoneAndBooking(context, attemptNumber: 1, defenseDate, startTime, endTime);

            var clock = new TestingSystemClock(CreateTestingEnv(), CreateTestingConfig());
            var availabilityService = new FakeLecturerAvailabilityService();
            var service = new IAFDefenseBookingService(context, availabilityService, clock);

            try
            {
                // 1. Simulate clock BEFORE defense ends (10:30 AM)
                clock.SetTime(new DateTime(2026, 10, 30, 10, 30, 0));

                var blockedResult = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    "AdminApproved",
                    "Early attempt",
                    currentUserId: 1);

                Assert.False(blockedResult.Success);
                Assert.Equal("DefenseNotConcludedYet", blockedResult.ErrorCode);

                // 2. Simulate clock AFTER defense ends (11:15 AM)
                clock.SetTime(new DateTime(2026, 10, 30, 11, 15, 0));

                var allowedResult = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    "AdminApproved",
                    "Passed physical defense",
                    currentUserId: 1);

                Assert.True(allowedResult.Success);
                Assert.NotNull(allowedResult.Booking);
                Assert.Equal("Completed", allowedResult.Booking.BookingStatus);
                Assert.Equal("AdminApproved", allowedResult.Booking.AcademicOutcome);
            }
            finally
            {
                clock.Reset();
            }
        }

        [Fact]
        public async Task Problem2_RecordDefenseOutcome_Attempt1Fail_RecordsPanelRevisionRequired_BlocksStage1Dpp()
        {
            using var context = CreateInMemoryContext();
            var defenseDate = new DateTime(2026, 10, 30);
            var (milestone, booking) = SeedMilestoneAndBooking(context, attemptNumber: 1, defenseDate);

            var clock = new TestingSystemClock(CreateTestingEnv(), CreateTestingConfig());
            var availabilityService = new FakeLecturerAvailabilityService();
            var service = new IAFDefenseBookingService(context, availabilityService, clock);

            try
            {
                // Advance clock past defense completion
                clock.SetTime(new DateTime(2026, 10, 30, 12, 0, 0));

                var result = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    outcome: "Fail", // Caller requests failure
                    outcomeComments: "Major scope adjustments needed",
                    currentUserId: 1);

                Assert.True(result.Success);

                // Reload from context
                var updatedMilestone = await context.FYPMilestones.FindAsync(milestone.Id);
                var updatedBooking = await context.IAFDefenseBookings.FindAsync(booking.Id);

                Assert.NotNull(updatedMilestone);
                Assert.NotNull(updatedBooking);

                // Verify Academic Workflow for Attempt 1: Must be PanelRevisionRequired, NOT ChangeOfTopicRequired
                Assert.Equal("PanelRevisionRequired", updatedMilestone.Status);
                Assert.Equal("PanelRevisionRequired", updatedBooking.AcademicOutcome);

                // Verify attempt history in remarks
                var attemptResult = IAFDefenseBookingService.ResolveAuthoritativeAttemptNumber(updatedMilestone.Remarks);
                Assert.True(attemptResult.Success);
                Assert.Equal(1, attemptResult.AttemptNumber);

                var json = updatedMilestone.Remarks!.Split(new[] { "JSON: " }, StringSplitOptions.None)[1];
                var history = IafAttemptHistory.ParseOrMigrate(json);
                var attempt1 = history.Attempts.Find(a => a.AttemptNumber == 1);
                Assert.NotNull(attempt1);
                Assert.Equal("PanelRevisionRequired", attempt1.Status);

                // Verify Stage 1 DPP progression is BLOCKED
                bool canProgressDpp = GradingHelper.CanProgressToStage1Dpp(updatedMilestone, updatedBooking);
                Assert.False(canProgressDpp);
            }
            finally
            {
                clock.Reset();
            }
        }

        [Fact]
        public async Task Problem2_RecordDefenseOutcome_Attempt2Fail_RecordsChangeOfTopicRequired_BlocksStage1Dpp()
        {
            using var context = CreateInMemoryContext();
            var defenseDate = new DateTime(2026, 11, 15);
            var (milestone, booking) = SeedMilestoneAndBooking(context, attemptNumber: 2, defenseDate);

            var clock = new TestingSystemClock(CreateTestingEnv(), CreateTestingConfig());
            var availabilityService = new FakeLecturerAvailabilityService();
            var service = new IAFDefenseBookingService(context, availabilityService, clock);

            try
            {
                clock.SetTime(new DateTime(2026, 11, 15, 14, 0, 0));

                var result = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    outcome: "Fail",
                    outcomeComments: "Failed Attempt 2. Change of topic required.",
                    currentUserId: 1);

                Assert.True(result.Success);

                var updatedMilestone = await context.FYPMilestones.FindAsync(milestone.Id);
                var updatedBooking = await context.IAFDefenseBookings.FindAsync(booking.Id);

                Assert.NotNull(updatedMilestone);
                Assert.NotNull(updatedBooking);

                // Attempt 2 Fail -> ChangeOfTopicRequired
                Assert.Equal("ChangeOfTopicRequired", updatedMilestone.Status);
                Assert.Equal("ChangeOfTopicRequired", updatedBooking.AcademicOutcome);

                // Stage 1 DPP progression is BLOCKED
                bool canProgressDpp = GradingHelper.CanProgressToStage1Dpp(updatedMilestone, updatedBooking);
                Assert.False(canProgressDpp);
            }
            finally
            {
                clock.Reset();
            }
        }

        [Fact]
        public async Task Problem2_RecordDefenseOutcome_Pass_RecordsAdminApproved_AllowsStage1Dpp()
        {
            using var context = CreateInMemoryContext();
            var defenseDate = new DateTime(2026, 10, 30);
            var (milestone, booking) = SeedMilestoneAndBooking(context, attemptNumber: 1, defenseDate);

            var clock = new TestingSystemClock(CreateTestingEnv(), CreateTestingConfig());
            var availabilityService = new FakeLecturerAvailabilityService();
            var service = new IAFDefenseBookingService(context, availabilityService, clock);

            try
            {
                clock.SetTime(new DateTime(2026, 10, 30, 13, 0, 0));

                var result = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    outcome: "AdminApproved",
                    outcomeComments: "Satisfactory defense session.",
                    currentUserId: 1);

                Assert.True(result.Success);

                var updatedMilestone = await context.FYPMilestones.FindAsync(milestone.Id);
                var updatedBooking = await context.IAFDefenseBookings.FindAsync(booking.Id);

                Assert.NotNull(updatedMilestone);
                Assert.NotNull(updatedBooking);

                Assert.Equal("AdminApproved", updatedMilestone.Status);
                Assert.Equal("AdminApproved", updatedBooking.AcademicOutcome);

                // Stage 1 DPP progression is ALLOWED
                bool canProgressDpp = GradingHelper.CanProgressToStage1Dpp(updatedMilestone, updatedBooking);
                Assert.True(canProgressDpp);
            }
            finally
            {
                clock.Reset();
            }
        }

        [Fact]
        public async Task Problem3_RecordDefenseOutcome_UnderSimulatedClock_ClearlyIdentifiedAsTestingEvidence()
        {
            using var context = CreateInMemoryContext();
            var defenseDate = new DateTime(2026, 10, 30);
            var (milestone, booking) = SeedMilestoneAndBooking(context, attemptNumber: 1, defenseDate);

            var clock = new TestingSystemClock(CreateTestingEnv(), CreateTestingConfig());
            var availabilityService = new FakeLecturerAvailabilityService();
            var service = new IAFDefenseBookingService(context, availabilityService, clock);

            try
            {
                clock.SetTime(new DateTime(2026, 10, 30, 15, 0, 0));

                var result = await service.RecordDefenseOutcomeAsync(
                    milestone.Id,
                    outcome: "AdminApproved",
                    outcomeComments: "Verified test pass",
                    currentUserId: 1);

                Assert.True(result.Success);
                Assert.NotNull(result.HistoryRecord);
                Assert.NotNull(result.Booking);

                // Must be clearly identified as testing simulation evidence, not a claim that physical defense occurred
                Assert.Contains("[Testing Simulation Evidence]", result.Booking.OutcomeComments);
                Assert.Contains("[Testing Simulation Evidence]", result.HistoryRecord.ReasonForChange);
            }
            finally
            {
                clock.Reset();
            }
        }
    }
}
