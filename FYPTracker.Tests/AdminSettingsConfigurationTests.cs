using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FYPTracker.Controllers;
using FYPTracker.Filters;
using FYPTracker.Hubs;
using FYPTracker.Models;
using FYPTracker.Services;
using FYPTracker.Services.Defense;
using FYPTracker.Services.Meetings;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FYPTracker.Tests
{
    public class AdminSettingsConfigurationTests
    {
        private FYPTrackerContext CreateInMemoryContext(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
                .Options;

            return new FYPTrackerContext(options);
        }

        private static ControllerContext CreateAdminControllerContext(ApplicationUser adminUser)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, adminUser.Id.ToString()),
                new Claim("Id", adminUser.Id.ToString()),
                new Claim(ClaimTypes.Name, adminUser.UserName ?? adminUser.Email ?? "admin"),
                new Claim(ClaimTypes.Email, adminUser.Email ?? "admin@test.com"),
                new Claim(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claimsPrincipal
                }
            };
        }

        #region Dummies

        private class DummyEmailService : IEmailService
        {
            public void Send(string to, string subject, string html, string? filePath = null, string? from = null, string? cc = null) { }
            public Task Send(List<string> to, string subject, string html, string? filePath = null, string? from = null) => Task.CompletedTask;
        }

        private class DummyHubContext : IHubContext<NotificationHub>
        {
            public IHubClients Clients => new DummyHubClients();
            public IGroupManager Groups => throw new NotImplementedException();
        }

        private class DummyHubClients : IHubClients
        {
            public IClientProxy All => new DummyClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
            public IClientProxy Client(string connectionId) => new DummyClientProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new DummyClientProxy();
            public IClientProxy Group(string groupName) => new DummyClientProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new DummyClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
            public IClientProxy User(string userId) => new DummyClientProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new DummyClientProxy();
        }

        private class DummyClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class DummyTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        #endregion

        [Fact]
        public async Task Test1_SaveAndReload_IAFDefenseAttendancePolicy()
        {
            using var context = CreateInMemoryContext();
            var adminUser = new ApplicationUser { Id = 1, UserName = "admin@test.com", Email = "admin@test.com", FullName = "System Admin" };
            context.ApplicationUsers.Add(adminUser);
            await context.SaveChangesAsync();

            var controller = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateAdminControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // 1. Save Required policy
            var resultRequired = await controller.UpdateIafDefenseSettings(
                durationMinutes: 45,
                supervisorRequired: "true",
                workingHoursStart: "09:00",
                workingHoursEnd: "17:00",
                vivaDurationMinutes: 60,
                meetingDurationMinutes: 30,
                sessionFilter: "2026 Jan");

            var settingAfterRequired = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "IAFDefenseSupervisorRequired");
            Assert.NotNull(settingAfterRequired);
            Assert.Equal("true", settingAfterRequired!.Value);

            // 2. Save Optional policy
            var resultOptional = await controller.UpdateIafDefenseSettings(
                durationMinutes: 45,
                supervisorRequired: "false",
                workingHoursStart: "09:00",
                workingHoursEnd: "17:00",
                vivaDurationMinutes: 60,
                meetingDurationMinutes: 30,
                sessionFilter: "2026 Jan");

            var settingAfterOptional = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "IAFDefenseSupervisorRequired");
            Assert.NotNull(settingAfterOptional);
            Assert.Equal("false", settingAfterOptional!.Value);
        }

        [Fact]
        public void Test2_RequiredSupervisorAttendance_IsResolvedInLecturerIds()
        {
            // Verifies that when IsSupervisorRequired is true, the supervisor ID is resolved as a mandatory participant
            var resolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 4,
                isSupervisorRequired: true,
                supervisorId: 5);

            Assert.True(resolution.Success);
            Assert.Contains(4, resolution.LecturerIds);
            Assert.Contains(5, resolution.LecturerIds);
            Assert.Equal(2, resolution.LecturerIds.Count);
        }

        [Fact]
        public void Test3_OptionalSupervisorAttendance_ExcludesSupervisorFromMandatoryList()
        {
            // Verifies that when IsSupervisorRequired is false, only examiner is mandatory
            var resolution = IAFDefenseBookingService.ResolveRequiredLecturerIds(
                examinerId: 4,
                isSupervisorRequired: false,
                supervisorId: 5);

            Assert.True(resolution.Success);
            Assert.Contains(4, resolution.LecturerIds);
            Assert.DoesNotContain(5, resolution.LecturerIds);
            Assert.Single(resolution.LecturerIds);
        }

        [Fact]
        public async Task Test4_ExistingCompletedBookings_RemainUnchangedWhenPolicyChanges()
        {
            using var context = CreateInMemoryContext();

            // Setup existing completed booking #2 and milestone #10
            var milestone = new FYPMilestone { Id = 10, FYPProjectId = 2, Title = "Initial Project Allocation Form (IAF)", Status = "AdminApproved", MilestoneSource = "Default" };
            var booking = new IAFDefenseBooking
            {
                Id = 2,
                MilestoneId = 10,
                ExaminerId = 4,
                OriginalSupervisorId = 5,
                AcademicSessionId = 1,
                BookingStatus = "Completed",
                AcademicOutcome = "AdminApproved",
                IsSupervisorRequired = false
            };
            context.FYPMilestones.Add(milestone);
            context.IAFDefenseBookings.Add(booking);
            await context.SaveChangesAsync();

            var adminUser = new ApplicationUser { Id = 1, UserName = "admin@test.com", Email = "admin@test.com" };
            context.ApplicationUsers.Add(adminUser);
            await context.SaveChangesAsync();

            var controller = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateAdminControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Change policy to true
            await controller.UpdateIafDefenseSettings(45, "true", "09:00", "17:00", 60, 30, "2026 Jan");

            // Verify historical booking #2 and milestone #10 remain 100% unaltered
            var refreshedBooking = await context.IAFDefenseBookings.FindAsync(2);
            var refreshedMilestone = await context.FYPMilestones.FindAsync(10);

            Assert.NotNull(refreshedBooking);
            Assert.Equal("Completed", refreshedBooking!.BookingStatus);
            Assert.Equal("AdminApproved", refreshedBooking.AcademicOutcome);
            Assert.Equal(4, refreshedBooking.ExaminerId);
            Assert.Equal(5, refreshedBooking.OriginalSupervisorId);

            Assert.NotNull(refreshedMilestone);
            Assert.Equal("AdminApproved", refreshedMilestone!.Status);
        }

        [Fact]
        public async Task Test5_SessionSpecificOfficialDeadlines_RemainIsolatedBetweenSessions()
        {
            using var context = CreateInMemoryContext();

            var sessionA = new AcademicSession { Id = 1, Name = "2026 Jan" };
            var sessionB = new AcademicSession { Id = 2, Name = "2026 May" };
            context.AcademicSessions.AddRange(sessionA, sessionB);

            var templateA = new DefaultMilestoneTemplate { Id = 1, AcademicSessionId = 1, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", DueDate = new DateTime(2026, 1, 15), IsActive = true };
            var templateB = new DefaultMilestoneTemplate { Id = 2, AcademicSessionId = 2, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", DueDate = new DateTime(2026, 5, 15), IsActive = true };
            context.DefaultMilestoneTemplates.AddRange(templateA, templateB);

            var projA = new FYPProject { Id = 1, AcademicSession = "2026 Jan", StudentId = 10 };
            var projB = new FYPProject { Id = 2, AcademicSession = "2026 May", StudentId = 20 };
            context.FYPProjects.AddRange(projA, projB);

            var milestoneA = new FYPMilestone { Id = 100, FYPProjectId = 1, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", MilestoneSource = "Default", DueDate = new DateTime(2026, 1, 15) };
            var milestoneB = new FYPMilestone { Id = 200, FYPProjectId = 2, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", MilestoneSource = "Default", DueDate = new DateTime(2026, 5, 15) };
            context.FYPMilestones.AddRange(milestoneA, milestoneB);
            await context.SaveChangesAsync();

            var adminUser = new ApplicationUser { Id = 1, UserName = "admin@test.com", Email = "admin@test.com" };
            context.ApplicationUsers.Add(adminUser);
            await context.SaveChangesAsync();

            var controller = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateAdminControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Admin updates deadline for Session A ONLY
            var newDateA = new DateTime(2026, 1, 30);
            await controller.UpdateDefaultMilestoneDate(newDateA, "2026 Jan", "IAF");

            // Verify Session A updated
            var refreshedMilestoneA = await context.FYPMilestones.FindAsync(100);
            var refreshedTemplateA = await context.DefaultMilestoneTemplates.FindAsync(1);
            Assert.Equal(new DateTime(2026, 1, 30, 23, 59, 59), refreshedMilestoneA!.DueDate);
            Assert.Equal(new DateTime(2026, 1, 30, 23, 59, 59), refreshedTemplateA!.DueDate);

            // Verify Session B completely untouched
            var refreshedMilestoneB = await context.FYPMilestones.FindAsync(200);
            var refreshedTemplateB = await context.DefaultMilestoneTemplates.FindAsync(2);
            Assert.Equal(new DateTime(2026, 5, 15), refreshedMilestoneB!.DueDate);
            Assert.Equal(new DateTime(2026, 5, 15), refreshedTemplateB!.DueDate);
        }

        [Fact]
        public void Test6_AdminAuthorization_AttributesEnforcedOnAdminController()
        {
            var type = typeof(AdminController);
            var authAttr = type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).FirstOrDefault();
            var roleAttr = type.GetCustomAttributes(typeof(AuthorizeUserTypeAttribute), inherit: true).FirstOrDefault() as AuthorizeUserTypeAttribute;

            Assert.NotNull(authAttr);
            Assert.NotNull(roleAttr);
        }

        [Fact]
        public async Task Test7_IndependentSaveEndpoints_PersistCorrectSettingKeys()
        {
            using var context = CreateInMemoryContext();
            var adminUser = new ApplicationUser { Id = 1, UserName = "admin@test.com", Email = "admin@test.com" };
            context.ApplicationUsers.Add(adminUser);
            await context.SaveChangesAsync();

            var controller = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateAdminControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // 1. DPP Settings
            await controller.UpdateDppSettings("2026-11-25", "2026 Jan");
            var dppSetting = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "DPPDeadline");
            Assert.Equal("2026-11-25", dppSetting!.Value);

            // 2. Supervisor Meeting Settings
            await controller.UpdateMeetingDurationSetting(45, "2026 Jan");
            var meetingSetting = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "MeetingDurationMinutes");
            Assert.Equal("45", meetingSetting!.Value);

            // 3. Final Viva Settings
            await controller.UpdateFinalVivaSettings(75, "2026 Jan");
            var vivaSetting = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "VivaDurationMinutes");
            Assert.Equal("75", vivaSetting!.Value);

            // 4. Exhibition Settings
            await controller.UpdateExhibitionSettings("2026-12-10", "10:00", "16:00", "2026 Jan");
            var exhDate = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "ExhibitionDate");
            var exhStart = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "ExhibitionTimeStart");
            var exhEnd = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "ExhibitionTimeEnd");
            Assert.Equal("2026-12-10", exhDate!.Value);
            Assert.Equal("10:00", exhStart!.Value);
            Assert.Equal("16:00", exhEnd!.Value);

            // 5. Workflow Policies
            await controller.UpdateWorkflowPolicySettings(5, "2026 Jan");
            var timeoutSetting = await context.FYPSettings.FirstOrDefaultAsync(s => s.Key == "SupervisorRequestTimeoutDays");
            Assert.Equal("5", timeoutSetting!.Value);
        }

        [Fact]
        public async Task Test8_SettingsGet_PopulatesViewBagWithoutErrors()
        {
            using var context = CreateInMemoryContext();
            var session = new AcademicSession { Id = 1, Name = "2026 Jan", IsActive = true, IsCurrent = true };
            context.AcademicSessions.Add(session);

            context.FYPSettings.AddRange(
                new FYPSetting { Key = "IAFDefenseDurationMinutes", Value = "45", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "IAFDefenseSupervisorRequired", Value = "true", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "LecturerWorkingHoursStart", Value = "09:00", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "LecturerWorkingHoursEnd", Value = "17:00", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "VivaDurationMinutes", Value = "60", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "MeetingDurationMinutes", Value = "30", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "DPPDeadline", Value = "2026-11-20", UpdatedDate = DateTime.Now },
                new FYPSetting { Key = "SupervisorRequestTimeoutDays", Value = "3", UpdatedDate = DateTime.Now }
            );

            var adminUser = new ApplicationUser { Id = 1, UserName = "admin@test.com", Email = "admin@test.com" };
            context.ApplicationUsers.Add(adminUser);
            await context.SaveChangesAsync();

            var controller = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateAdminControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            var viewResult = await controller.Settings("2026 Jan") as ViewResult;
            Assert.NotNull(viewResult);

            Assert.Equal("45", controller.ViewBag.IAFDefenseDurationMinutes);
            Assert.Equal("true", controller.ViewBag.IAFDefenseSupervisorRequired);
            Assert.Equal("09:00", controller.ViewBag.LecturerWorkingHoursStart);
            Assert.Equal("17:00", controller.ViewBag.LecturerWorkingHoursEnd);
            Assert.Equal("60", controller.ViewBag.VivaDurationMinutes);
            Assert.Equal("30", controller.ViewBag.MeetingDurationMinutes);
            Assert.Equal("2026-11-20", controller.ViewBag.DPPDeadline);
            Assert.Equal("3", controller.ViewBag.SupervisorRequestTimeoutDays);
        }
    }
}
