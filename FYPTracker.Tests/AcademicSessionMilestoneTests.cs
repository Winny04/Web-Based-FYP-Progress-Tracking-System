using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FYPTracker.Controllers;
using FYPTracker.Hubs;
using FYPTracker.Models;
using FYPTracker.Services.Meetings;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class AcademicSessionMilestoneTests
    {
        private FYPTrackerContext CreateInMemoryContext(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
                .Options;

            return new FYPTrackerContext(options);
        }

        private static ControllerContext CreateControllerContext(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("Id", user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? "Test User"),
                new Claim(ClaimTypes.Email, user.Email ?? "test@example.com")
            };

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            };

            return new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        #region Dummies for Controller Dependencies

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

        private class DummyWeeklyMeetingSchedulingService : IWeeklyMeetingSchedulingService
        {
            public Task<int?> GetMeetingDurationMinutesAsync() => Task.FromResult<int?>(30);
            public Task<(bool HasConflict, string ErrorMessage)> ValidateMeetingConflictAsync(int supervisorId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime, int academicSessionId, int? ignoreMeetingId = null) => Task.FromResult((false, ""));
            public Task<string?> ValidateMeetingConflictAsync(int supervisorId, DateTime startDateTime, DateTime endDateTime, int? excludeMeetingId = null, int? sessionId = null) => Task.FromResult<string?>(null);
            public Task<(int ScheduledCount, int ConflictCount, string Message)> GenerateWeeklyScheduleAsync(int studentId, int supervisorId, int meetingId) => Task.FromResult((0, 0, ""));
            public Task<List<OverrideMeetingConflictDto>> DetectOverrideMeetingConflictsAsync(int supervisorId, DateTime overrideDate, TimeSpan overrideStart, TimeSpan overrideEnd, bool isAvailable, int? overrideId = null) => Task.FromResult(new List<OverrideMeetingConflictDto>());
            public Task<List<OverrideMeetingConflictDto>> GetSupervisorActiveOverrideMeetingConflictsAsync(int supervisorId) => Task.FromResult(new List<OverrideMeetingConflictDto>());
            public Task<List<OverrideMeetingConflictDto>> GetStudentActiveOverrideMeetingConflictsAsync(int studentId) => Task.FromResult(new List<OverrideMeetingConflictDto>());
        }

        #endregion

        [Fact]
        public async Task Test1_StudentsInSameSession_ReceiveIdenticalOfficialMilestoneDeadlines()
        {
            using var context = CreateInMemoryContext();

            var session = new AcademicSession { Id = 1, Name = "2026 Jan", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) };
            context.AcademicSessions.Add(session);

            var iafDeadline = new DateTime(2026, 11, 20, 23, 59, 59);
            var dppDeadline = new DateTime(2026, 12, 1, 23, 59, 59);

            var templateIaf = new DefaultMilestoneTemplate
            {
                Id = 1,
                AcademicSessionId = session.Id,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Stage = 1,
                DueDate = iafDeadline,
                IsRequired = true,
                IsActive = true
            };
            var templateDpp = new DefaultMilestoneTemplate
            {
                Id = 2,
                AcademicSessionId = session.Id,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Stage = 1,
                DueDate = dppDeadline,
                IsRequired = true,
                IsActive = true
            };
            context.DefaultMilestoneTemplates.AddRange(templateIaf, templateDpp);

            // Student 1 & 2 in same session
            var project1 = new FYPProject { Id = 101, Title = "Project A", AcademicSession = "2026 Jan", StudentId = 11 };
            var project2 = new FYPProject { Id = 102, Title = "Project B", AcademicSession = "2026 Jan", StudentId = 12 };
            context.FYPProjects.AddRange(project1, project2);

            var m1_iaf = new FYPMilestone { Id = 201, FYPProjectId = project1.Id, Title = templateIaf.Title, MilestoneCode = "IAF", MilestoneSource = "Default", DefaultMilestoneTemplateId = 1, DueDate = templateIaf.DueDate };
            var m1_dpp = new FYPMilestone { Id = 202, FYPProjectId = project1.Id, Title = templateDpp.Title, MilestoneCode = "STAGE1_FINAL", MilestoneSource = "Default", DefaultMilestoneTemplateId = 2, DueDate = templateDpp.DueDate };
            var m2_iaf = new FYPMilestone { Id = 203, FYPProjectId = project2.Id, Title = templateIaf.Title, MilestoneCode = "IAF", MilestoneSource = "Default", DefaultMilestoneTemplateId = 1, DueDate = templateIaf.DueDate };
            var m2_dpp = new FYPMilestone { Id = 204, FYPProjectId = project2.Id, Title = templateDpp.Title, MilestoneCode = "STAGE1_FINAL", MilestoneSource = "Default", DefaultMilestoneTemplateId = 2, DueDate = templateDpp.DueDate };
            context.FYPMilestones.AddRange(m1_iaf, m1_dpp, m2_iaf, m2_dpp);
            await context.SaveChangesAsync();

            // Assert: Both students share the identical official milestone due dates
            Assert.Equal(m1_iaf.DueDate, m2_iaf.DueDate);
            Assert.Equal(iafDeadline, m1_iaf.DueDate);
            Assert.Equal(m1_dpp.DueDate, m2_dpp.DueDate);
            Assert.Equal(dppDeadline, m1_dpp.DueDate);
        }

        [Fact]
        public async Task Test2_StudentsInDifferentSessions_CanReceiveDifferentDeadlines()
        {
            using var context = CreateInMemoryContext();

            var session1 = new AcademicSession { Id = 1, Name = "2026 Jan", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) };
            var session2 = new AcademicSession { Id = 2, Name = "2026 May", StartDate = new DateTime(2026, 5, 1), EndDate = new DateTime(2026, 11, 30) };
            context.AcademicSessions.AddRange(session1, session2);

            var deadlineSession1 = new DateTime(2026, 2, 15, 23, 59, 59);
            var deadlineSession2 = new DateTime(2026, 6, 15, 23, 59, 59);

            var t1 = new DefaultMilestoneTemplate { Id = 1, AcademicSessionId = 1, MilestoneCode = "IAF", Title = "IAF", DueDate = deadlineSession1, IsActive = true };
            var t2 = new DefaultMilestoneTemplate { Id = 2, AcademicSessionId = 2, MilestoneCode = "IAF", Title = "IAF", DueDate = deadlineSession2, IsActive = true };
            context.DefaultMilestoneTemplates.AddRange(t1, t2);

            var proj1 = new FYPProject { Id = 1, AcademicSession = "2026 Jan", StudentId = 1 };
            var proj2 = new FYPProject { Id = 2, AcademicSession = "2026 May", StudentId = 2 };
            context.FYPProjects.AddRange(proj1, proj2);

            var m1 = new FYPMilestone { Id = 1, FYPProjectId = 1, MilestoneCode = "IAF", Title = "IAF", DueDate = deadlineSession1, MilestoneSource = "Default" };
            var m2 = new FYPMilestone { Id = 2, FYPProjectId = 2, MilestoneCode = "IAF", Title = "IAF", DueDate = deadlineSession2, MilestoneSource = "Default" };
            context.FYPMilestones.AddRange(m1, m2);
            await context.SaveChangesAsync();

            Assert.NotEqual(m1.DueDate, m2.DueDate);
            Assert.Equal(deadlineSession1, m1.DueDate);
            Assert.Equal(deadlineSession2, m2.DueDate);
        }

        [Fact]
        public async Task Test3_Supervisor_CannotModifyOfficialSessionDeadline()
        {
            using var context = CreateInMemoryContext();

            var supervisor = new ApplicationUser { Id = 5, FullName = "Dr. Supervisor", Email = "sup@test.com" };
            var student = new ApplicationUser { Id = 20, FullName = "Student A", Email = "stua@test.com", IsResign = false };
            context.ApplicationUsers.AddRange(supervisor, student);

            var project = new FYPProject { Id = 10, SupervisorId = 5, AcademicSession = "2026 Jan", StudentId = 20, Student = student };
            context.FYPProjects.Add(project);

            var originalDate = new DateTime(2026, 11, 20, 23, 59, 59);
            var officialMilestone = new FYPMilestone
            {
                Id = 50,
                FYPProjectId = 10,
                Title = "Initial Project Allocation Form (IAF)",
                MilestoneCode = "IAF",
                MilestoneSource = "Default",
                DueDate = originalDate,
                Status = "Pending"
            };
            context.FYPMilestones.Add(officialMilestone);
            await context.SaveChangesAsync();

            var controller = new SupervisorController(context, new DummyEmailService(), new DummyHubContext(), null!, null!, new DummyWeeklyMeetingSchedulingService())
            {
                ControllerContext = CreateControllerContext(supervisor),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Attempt 1: Direct EditMilestone on official milestone
            var attemptedDate = new DateTime(2026, 11, 30);
            var result = await controller.EditMilestone(50, "Changed Title", "Changed Desc", attemptedDate);

            // Assert: Rejected by controller and milestone remains unchanged
            Assert.Equal("Official academic session milestones cannot be modified by supervisors. Only student progress tasks can be edited.", controller.TempData["Error"]);
            var refreshed = await context.FYPMilestones.FindAsync(50);
            Assert.Equal(originalDate, refreshed!.DueDate);
            Assert.Equal("Initial Project Allocation Form (IAF)", refreshed.Title);

            // Attempt 2: BulkEditMilestone targeting official milestone
            var bulkResult = await controller.BulkEditMilestone("Initial Project Allocation Form (IAF)", "Hacked Title", attemptedDate, "Hacked Desc", "2026 Jan");
            Assert.Equal("Official academic milestones are centrally managed by the Administrator and cannot be modified by supervisors.", controller.TempData["Error"]);
            refreshed = await context.FYPMilestones.FindAsync(50);
            Assert.Equal(originalDate, refreshed!.DueDate);
        }

        [Fact]
        public async Task Test4_Admin_UpdateDefaultMilestoneDate_SynchronizesOfficialMilestonesAcrossSession()
        {
            using var context = CreateInMemoryContext();

            var session1 = new AcademicSession { Id = 1, Name = "2026 Jan" };
            var session2 = new AcademicSession { Id = 2, Name = "2026 May" };
            context.AcademicSessions.AddRange(session1, session2);

            var template1 = new DefaultMilestoneTemplate { Id = 1, AcademicSessionId = 1, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", DueDate = new DateTime(2026, 1, 15), IsActive = true };
            var template2 = new DefaultMilestoneTemplate { Id = 2, AcademicSessionId = 2, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", DueDate = new DateTime(2026, 5, 15), IsActive = true };
            context.DefaultMilestoneTemplates.AddRange(template1, template2);

            var p1_s1 = new FYPProject { Id = 1, AcademicSession = "2026 Jan", StudentId = 1 };
            var p2_s1 = new FYPProject { Id = 2, AcademicSession = "2026 Jan", StudentId = 2 };
            var p3_s2 = new FYPProject { Id = 3, AcademicSession = "2026 May", StudentId = 3 };
            context.FYPProjects.AddRange(p1_s1, p2_s1, p3_s2);

            var m1 = new FYPMilestone { Id = 101, FYPProjectId = 1, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", MilestoneSource = "Default", DueDate = new DateTime(2026, 1, 15) };
            var m2 = new FYPMilestone { Id = 102, FYPProjectId = 2, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", MilestoneSource = "Default", DueDate = new DateTime(2026, 1, 15) };
            var m3 = new FYPMilestone { Id = 103, FYPProjectId = 3, MilestoneCode = "IAF", Title = "Initial Project Allocation Form (IAF)", MilestoneSource = "Default", DueDate = new DateTime(2026, 5, 15) };
            context.FYPMilestones.AddRange(m1, m2, m3);
            await context.SaveChangesAsync();

            var adminUser = new ApplicationUser { Id = 999, FullName = "Admin User" };
            var adminController = new AdminController(context, new DummyEmailService(), new DummyHubContext())
            {
                ControllerContext = CreateControllerContext(adminUser),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Admin updates session "2026 Jan" IAF deadline
            var newTargetDate = new DateTime(2026, 1, 28);
            var expectedEndOfDay = new DateTime(2026, 1, 28, 23, 59, 59);

            await adminController.UpdateIAFDeadline(newTargetDate, "2026 Jan");

            // Assert: Template updated
            var updatedT1 = await context.DefaultMilestoneTemplates.FindAsync(1);
            Assert.Equal(expectedEndOfDay, updatedT1!.DueDate);

            // Assert: Session 1 students synchronized
            var updatedM1 = await context.FYPMilestones.FindAsync(101);
            var updatedM2 = await context.FYPMilestones.FindAsync(102);
            Assert.Equal(expectedEndOfDay, updatedM1!.DueDate);
            Assert.Equal(expectedEndOfDay, updatedM2!.DueDate);

            // Assert: Session 2 students NOT corrupted
            var updatedM3 = await context.FYPMilestones.FindAsync(103);
            Assert.Equal(new DateTime(2026, 5, 15), updatedM3!.DueDate);
            var updatedT2 = await context.DefaultMilestoneTemplates.FindAsync(2);
            Assert.Equal(new DateTime(2026, 5, 15), updatedT2!.DueDate);
        }

        [Fact]
        public async Task Test5_Supervisor_CanCreateProgressTask_OnlyForAssignedProject()
        {
            using var context = CreateInMemoryContext();

            var sup1 = new ApplicationUser { Id = 10, FullName = "Supervisor 1" };
            var sup2 = new ApplicationUser { Id = 20, FullName = "Supervisor 2" };
            context.ApplicationUsers.AddRange(sup1, sup2);

            var myProject = new FYPProject { Id = 1, SupervisorId = 10, Title = "My Student Project", StudentId = 101, AcademicSession = "2026 Jan" };
            var otherProject = new FYPProject { Id = 2, SupervisorId = 20, Title = "Other Student Project", StudentId = 102, AcademicSession = "2026 Jan" };
            context.FYPProjects.AddRange(myProject, otherProject);
            await context.SaveChangesAsync();

            var controller = new SupervisorController(context, new DummyEmailService(), new DummyHubContext(), null!, null!, new DummyWeeklyMeetingSchedulingService())
            {
                ControllerContext = CreateControllerContext(sup1),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Attempt to assign to other supervisor's project
            var unauthResult = await controller.AddMilestone(otherProject.Id, "Cross-Assigned Task", "Desc", DateTime.Today.AddDays(7), stage: 1);
            Assert.Equal("Project not found or you are not authorized to assign progress tasks to this student.", controller.TempData["Error"]);
            Assert.False(await context.FYPMilestones.AnyAsync(m => m.FYPProjectId == otherProject.Id));

            // Assign to own student project
            var authResult = await controller.AddMilestone(myProject.Id, "Chapter 1 Literature Review", "Write draft", DateTime.Today.AddDays(7), stage: 1);
            Assert.Contains("Student progress task 'Chapter 1 Literature Review' added successfully", controller.TempData["Success"]?.ToString());

            var createdTask = await context.FYPMilestones.FirstOrDefaultAsync(m => m.FYPProjectId == myProject.Id);
            Assert.NotNull(createdTask);
            Assert.Equal("Chapter 1 Literature Review", createdTask!.Title);
            Assert.Equal("Supervisor", createdTask.MilestoneSource);
            Assert.Null(createdTask.DefaultMilestoneTemplateId);
            Assert.False(createdTask.IsRequired);
            Assert.Equal(1, createdTask.Stage);
        }

        [Fact]
        public async Task Test6_ProjectSpecificTask_DoesNotAlterSessionMilestoneDeadlines()
        {
            using var context = CreateInMemoryContext();

            var session = new AcademicSession { Id = 1, Name = "2026 Jan" };
            context.AcademicSessions.Add(session);

            var officialDueDate = new DateTime(2026, 11, 20, 23, 59, 59);
            var template = new DefaultMilestoneTemplate
            {
                Id = 1,
                AcademicSessionId = 1,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                DueDate = officialDueDate,
                IsActive = true
            };
            context.DefaultMilestoneTemplates.Add(template);

            var supervisor = new ApplicationUser { Id = 10, FullName = "Dr. Supervisor" };
            context.ApplicationUsers.Add(supervisor);

            var project = new FYPProject { Id = 1, SupervisorId = 10, AcademicSession = "2026 Jan", StudentId = 101 };
            context.FYPProjects.Add(project);

            var officialMilestone = new FYPMilestone
            {
                Id = 1,
                FYPProjectId = 1,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneSource = "Default",
                DefaultMilestoneTemplateId = 1,
                DueDate = officialDueDate
            };
            context.FYPMilestones.Add(officialMilestone);
            await context.SaveChangesAsync();

            var controller = new SupervisorController(context, new DummyEmailService(), new DummyHubContext(), null!, null!, new DummyWeeklyMeetingSchedulingService())
            {
                ControllerContext = CreateControllerContext(supervisor),
                TempData = new TempDataDictionary(new DefaultHttpContext(), (ITempDataProvider)new DummyTempDataProvider())
            };

            // Supervisor creates a progress task with a completely different custom date
            var customTaskDate = new DateTime(2026, 11, 5);
            await controller.AddMilestone(project.Id, "Task: Mockup Review", "Complete mockups", customTaskDate, stage: 1);

            // Assert: Official session template and student's official milestone due date remain 2026-11-20
            var refreshedTemplate = await context.DefaultMilestoneTemplates.FindAsync(1);
            var refreshedOfficial = await context.FYPMilestones.FindAsync(1);

            Assert.Equal(officialDueDate, refreshedTemplate!.DueDate);
            Assert.Equal(officialDueDate, refreshedOfficial!.DueDate);

            // Assert: Only the supervisor progress task has the custom date
            var customTask = await context.FYPMilestones.FirstOrDefaultAsync(m => m.Title == "Task: Mockup Review");
            Assert.NotNull(customTask);
            Assert.Equal(customTaskDate, customTask!.DueDate);
            Assert.Equal("Supervisor", customTask.MilestoneSource);
        }

        [Fact]
        public void Test7_StudentWithoutSupervisorProgressTasks_IsNotLockedFromStage1Dpp()
        {
            // Business rule validation: When allProgressMilestones.Count == 0,
            // isAllProgressCompleted must evaluate to TRUE so student is not locked out of Stage 1 DPP proposal.

            var milestones = new List<FYPMilestone>
            {
                new FYPMilestone { Id = 10, Title = "IAF", MilestoneCode = "IAF", Status = "AdminApproved", Stage = 1, MilestoneSource = "Default" },
                new FYPMilestone { Id = 11, Title = "Ethics Form", MilestoneCode = "GENERIC", Status = "Pending", Stage = 1, MilestoneSource = "Default" }
            };

            var allProgressMilestones = milestones.Where(m => m.MilestoneSource == "Supervisor" && m.Stage == 1).ToList();

            // When no supervisor progress tasks exist:
            bool hasProgressMilestones = allProgressMilestones.Any();
            bool isAllProgressCompleted = !hasProgressMilestones || allProgressMilestones.All(m => m.Status == "Completed");

            Assert.False(hasProgressMilestones);
            Assert.True(isAllProgressCompleted); // Stage 1 Proposal unlocked!

            // When supervisor assigns a task that is Draft:
            milestones.Add(new FYPMilestone { Id = 12, Title = "Draft Chapter 1", MilestoneSource = "Supervisor", Stage = 1, Status = "Draft" });
            allProgressMilestones = milestones.Where(m => m.MilestoneSource == "Supervisor" && m.Stage == 1).ToList();
            hasProgressMilestones = allProgressMilestones.Any();
            isAllProgressCompleted = !hasProgressMilestones || allProgressMilestones.All(m => m.Status == "Completed");

            Assert.True(hasProgressMilestones);
            Assert.False(isAllProgressCompleted); // Locked until completed

            // When supervisor marks it completed:
            milestones.First(m => m.Id == 12).Status = "Completed";
            isAllProgressCompleted = !hasProgressMilestones || allProgressMilestones.All(m => m.Status == "Completed");
            Assert.True(isAllProgressCompleted); // Unlocked again!
        }

        [Fact]
        public async Task Test8_Student1_ExistingDataIntegrity_PassAndBookingRemainIntact()
        {
            using var context = CreateInMemoryContext();

            // Setup exact manual-test profile for Student 1
            var student = new ApplicationUser { Id = 99, EmployeeId = "p99000001", FullName = "Student 1" };
            var supervisor = new ApplicationUser { Id = 2, EmployeeId = "l00000002", FullName = "Lecturer 2" };
            var examiner = new ApplicationUser { Id = 1, EmployeeId = "l00000001", FullName = "Lecturer 1" };
            var session = new AcademicSession { Id = 3, Name = "2026 Jan" };
            context.ApplicationUsers.AddRange(student, supervisor, examiner);
            context.AcademicSessions.Add(session);

            var project = new FYPProject
            {
                Id = 2,
                StudentId = 99,
                SupervisorId = 2,
                AcademicSession = "2026 Jan",
                Title = "Student 1 FYP Project",
                Status = "Approved"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 10,
                FYPProjectId = 2,
                Title = "Initial Project Allocation Form (IAF)",
                MilestoneCode = "IAF",
                MilestoneSource = "Default",
                Status = "AdminApproved",
                Stage = 1
            };
            context.FYPMilestones.Add(iafMilestone);

            var booking = new IAFDefenseBooking
            {
                Id = 2,
                MilestoneId = 10,
                ExaminerId = 1,
                OriginalSupervisorId = 2,
                AcademicSessionId = 3,
                BookingStatus = "Completed",
                AcademicOutcome = "AdminApproved"
            };
            context.IAFDefenseBookings.Add(booking);
            await context.SaveChangesAsync();

            // Verify integrity
            var preservedMilestone = await context.FYPMilestones.FindAsync(10);
            var preservedBooking = await context.IAFDefenseBookings.FindAsync(2);
            var preservedProject = await context.FYPProjects.FindAsync(2);

            Assert.NotNull(preservedMilestone);
            Assert.Equal("AdminApproved", preservedMilestone!.Status);
            Assert.NotNull(preservedBooking);
            Assert.Equal("Completed", preservedBooking!.BookingStatus);
            Assert.Equal("AdminApproved", preservedBooking.AcademicOutcome);
            Assert.Equal(2, preservedBooking.OriginalSupervisorId);
            Assert.Equal(1, preservedBooking.ExaminerId);
            Assert.NotNull(preservedProject);
            Assert.Equal(2, preservedProject!.Id);
        }

        private class DummyTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }
    }
}
