using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FYPTracker.Controllers;
using FYPTracker.Helpers;
using FYPTracker.Hubs;
using FYPTracker.Models;
using FYPTracker.Services.Meetings;
using FYPTracker.Services.Others.Interfaces;
using FYPTracker.Services.Time;
using FYPTracker.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class EndToEndWorkflowVerificationTests
    {
        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        #region Test Dummies
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

        private class DummyMeetingRequirementService : IMeetingRequirementService
        {
            public Task<FYPTracker.ViewModels.MeetingProgressVM?> GetStudentProgressAsync(int studentId, int stage) => Task.FromResult<FYPTracker.ViewModels.MeetingProgressVM?>(null);
        }
        #endregion

        private DbContextOptions<FYPTrackerContext> CreateInMemoryOptions(string dbName)
        {
            return new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        }

        private (StudentController controller, FYPTrackerContext context, string tempDir) CreateStudentController(string dbName, int userId, string username)
        {
            var options = CreateInMemoryOptions(dbName);
            var context = new FYPTrackerContext(options);
            string tempDir = Path.Combine(Path.GetTempPath(), "FYPTracker_E2ETests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            // Copy FormTemplates into tempDir
            string sourceFormTemplates = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FYPTracker", "FormTemplates");
            if (Directory.Exists(sourceFormTemplates))
            {
                string destFormTemplates = Path.Combine(tempDir, "FormTemplates");
                Directory.CreateDirectory(destFormTemplates);
                foreach (var file in Directory.GetFiles(sourceFormTemplates, "*.json"))
                {
                    File.Copy(file, Path.Combine(destFormTemplates, Path.GetFileName(file)), true);
                }
            }

            var env = new TestWebHostEnvironment { EnvironmentName = "Testing", ContentRootPath = tempDir, WebRootPath = tempDir };
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var controller = new StudentController(
                context,
                env,
                config,
                new DummyEmailService(),
                new DummyHubContext(),
                new DummyMeetingRequirementService(),
                new DummyWeeklyMeetingSchedulingService(),
                new RealSystemClock(),
                null
            );

            var claims = new List<Claim>
            {
                new Claim("Id", userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "Student")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());

            return (controller, context, tempDir);
        }

        private (SupervisorController controller, FYPTrackerContext context) CreateSupervisorController(FYPTrackerContext context, int supervisorId, string username, string tempDir)
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing", ContentRootPath = tempDir, WebRootPath = tempDir };
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var controller = new SupervisorController(
                context,
                new DummyEmailService(),
                new DummyHubContext(),
                env,
                config,
                new DummyWeeklyMeetingSchedulingService(),
                new DummyMeetingRequirementService()
            );

            var claims = new List<Claim>
            {
                new Claim("Id", supervisorId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, supervisorId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "Supervisor")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());

            return (controller, context);
        }

        private (AdminController controller, FYPTrackerContext context) CreateAdminController(FYPTrackerContext context, int adminId, string username, string tempDir)
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing", ContentRootPath = tempDir, WebRootPath = tempDir };
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var controller = new AdminController(
                context,
                new DummyEmailService(),
                new DummyHubContext(),
                null,
                env,
                config,
                null
            );

            var claims = new List<Claim>
            {
                new Claim("Id", adminId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());

            return (controller, context);
        }

        [Fact]
        public void Objective1And2_Stage1DppPrerequisite_EnforcedStrictly()
        {
            // Case 1: IAF not approved -> Cannot submit Stage 1 Final DPP
            var milestones = new List<FYPMilestone>
            {
                new FYPMilestone { Id = 1, Title = "IAF", MilestoneCode = "IAF", Status = "Draft", MilestoneSource = "Default", Stage = 1 }
            };
            var result1 = GradingHelper.CanSubmitStage1FinalDpp(milestones);
            Assert.False(result1.Allowed);

            // Case 2: IAF Approved, but NO supervisor tasks assigned -> Cannot submit Stage 1 Final DPP
            milestones[0].Status = "AdminApproved";
            var result2 = GradingHelper.CanSubmitStage1FinalDpp(milestones);
            Assert.False(result2.Allowed);
            Assert.False(result2.HasTasks);
            Assert.Contains("Waiting for your Supervisor to assign", result2.Reason);

            // Case 3: IAF Approved, Supervisor task assigned but status is Draft / Pending -> Cannot submit
            var supTask = new FYPMilestone { Id = 10, Title = "Literature Review Task", MilestoneSource = "Supervisor", Stage = 1, Status = "Draft" };
            milestones.Add(supTask);
            var result3 = GradingHelper.CanSubmitStage1FinalDpp(milestones);
            Assert.False(result3.Allowed);
            Assert.True(result3.HasTasks);
            Assert.False(result3.AllApproved);
            Assert.Contains("Waiting for Supervisor approval", result3.Reason);

            // Case 4: IAF Approved AND Supervisor task Completed/Approved -> Can submit Stage 1 Final DPP
            supTask.Status = "Completed";
            var result4 = GradingHelper.CanSubmitStage1FinalDpp(milestones);
            Assert.True(result4.Allowed);
            Assert.True(result4.HasTasks);
            Assert.True(result4.AllApproved);
        }

        [Fact]
        public async Task EndToEnd_IafLifecycle_FromStudentSaveDraftToAdminFinalApproval()
        {
            string dbName = Guid.NewGuid().ToString();
            var (studentCtrl, ctx, tempDir) = CreateStudentController(dbName, 69, "p26010051");

            try
            {
                var studentRole = new AppRole { Id = 1, Name = "Student", Description = "Student" };
                var supRole = new AppRole { Id = 2, Name = "Supervisor", Description = "Supervisor" };
                var adminRole = new AppRole { Id = 3, Name = "Admin", Description = "Admin" };
                ctx.AppRoles.AddRange(studentRole, supRole, adminRole);

                var studentUser = new ApplicationUser { Id = 69, UserName = "p26010051", FullName = "FYP Demo Student 01", AcademicSession = "Session 2026/2027" };
                var supUser = new ApplicationUser { Id = 10, UserName = "bernardlim", FullName = "Dr. Bernard Lim" };
                var adminUser = new ApplicationUser { Id = 1, UserName = "admin", FullName = "Module Leader Admin" };
                ctx.ApplicationUsers.AddRange(studentUser, supUser, adminUser);

                ctx.UserRoleMappings.Add(new UserRoleMapping { UserId = 69, RoleId = 1 });
                ctx.UserRoleMappings.Add(new UserRoleMapping { UserId = 10, RoleId = 2 });
                ctx.UserRoleMappings.Add(new UserRoleMapping { UserId = 1, RoleId = 3 });

                var project = new FYPProject
                {
                    Id = 53,
                    Title = "Web-Based FYP Progress Tracking System",
                    StudentId = 69,
                    SupervisorId = 10,
                    AcademicSession = "Session 2026/2027",
                    Status = "Approved"
                };
                ctx.FYPProjects.Add(project);

                var iafMilestone = new FYPMilestone
                {
                    Id = 321,
                    FYPProjectId = 53,
                    Title = "FYP 1: Initial Assessment Form (IAF)",
                    MilestoneCode = "IAF",
                    Stage = 1,
                    IsRequired = true,
                    Status = "Pending",
                    MilestoneSource = "Default"
                };
                ctx.FYPMilestones.Add(iafMilestone);
                await ctx.SaveChangesAsync();

                // STEP 1: Student saves Draft of IAF
                var draftForm = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "draft" },
                    { "title", "Web-Based FYP Progress Tracking System" },
                    { "objective1", "To design and develop a centralised Web-Based FYP Progress Tracking System" },
                    { "objective2", "To implement supervisor selection and role-based dashboards" },
                    { "objective3", "To conduct validation and usability testing" },
                    { "problem", "Fragmented FYP tracking across multiple disparate documents and emails." },
                    { "scope", "Four roles: Student, Supervisor, Examiner, Admin at INTI." },
                    { "references", "IEEE FYP Management standards 2025." }
                });
                studentCtrl.ControllerContext.HttpContext.Request.Form = draftForm;

                var draftResult = await studentCtrl.SubmitDigitalForm(
                    milestoneId: 321,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Web-Based FYP Progress Tracking System",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Fragmented FYP tracking across multiple disparate documents and emails.",
                    objective1: "To design and develop a centralised Web-Based FYP Progress Tracking System",
                    objective2: "To implement supervisor selection and role-based dashboards",
                    objective3: "To conduct validation and usability testing",
                    scope: "Four roles: Student, Supervisor, Examiner, Admin at INTI.",
                    outcome: null,
                    references: "IEEE FYP Management standards 2025.",
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: "Cov12345",
                    revisionNotes: null,
                    isDraft: true
                );

                var msAfterDraft = await ctx.FYPMilestones.FindAsync(321);
                Assert.NotNull(msAfterDraft);
                Assert.Equal("Draft", msAfterDraft.Status);

                // STEP 2: Student sends IAF for Supervisor Review
                var reviewForm = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "supervisor_review" },
                    { "title", "Web-Based FYP Progress Tracking System" },
                    { "objective1", "To design and develop a centralised Web-Based FYP Progress Tracking System" },
                    { "objective2", "To implement supervisor selection and role-based dashboards" },
                    { "objective3", "To conduct validation and usability testing" }
                });
                studentCtrl.ControllerContext.HttpContext.Request.Form = reviewForm;

                var reviewResult = await studentCtrl.SubmitDigitalForm(
                    milestoneId: 321,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Web-Based FYP Progress Tracking System",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Fragmented FYP tracking.",
                    objective1: "To design and develop a centralised Web-Based FYP Progress Tracking System",
                    objective2: "To implement supervisor selection and role-based dashboards",
                    objective3: "To conduct validation and usability testing",
                    scope: "Scope text.",
                    outcome: null,
                    references: "Ref text.",
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: true
                );

                var msAfterReview = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("PendingSupervisorReview", msAfterReview!.Status);

                // STEP 3: Supervisor reviews and signs IAF (Transitions to PendingPanelReview)
                var (supCtrl, _) = CreateSupervisorController(ctx, 10, "bernardlim", tempDir);
                var supReviewResult = await supCtrl.ReviewSubmission(
                    milestoneId: 321,
                    status: "Approved",
                    remarks: "Objectives and scope are rigorous and approved for panel submission.",
                    supervisorSignature: "Dr. Bernard Lim (Digital Sig)",
                    pctComplete: "100",
                    turnitinScore: null,
                    gradedFile: null
                );

                var msAfterSupSign = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("PendingPanelReview", msAfterSupSign!.Status);
                Assert.Contains("Objectives and scope are rigorous", msAfterSupSign.SupervisorFeedback);
                Assert.Contains("Dr. Bernard Lim", msAfterSupSign.Remarks);

                // STEP 4: Admin reviews and grants final approval (AdminApproved)
                var (adminCtrl, _) = CreateAdminController(ctx, 1, "admin", tempDir);
                var adminApproveResult = await adminCtrl.AdminFinalApproveIaf(milestoneId: 321);

                var msAfterAdminApprove = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("AdminApproved", msAfterAdminApprove!.Status);

                // Verify IAF is now valid and enables progression
                bool canProg = GradingHelper.CanProgressToStage1Dpp(msAfterAdminApprove);
                Assert.True(canProg);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task EndToEnd_SupervisorTasks_CRUD_And_DppGating_Lifecycle()
        {
            string dbName = Guid.NewGuid().ToString();
            var (studentCtrl, ctx, tempDir) = CreateStudentController(dbName, 69, "p26010051");

            try
            {
                var studentUser = new ApplicationUser { Id = 69, UserName = "p26010051", FullName = "FYP Demo Student 01" };
                var supUser = new ApplicationUser { Id = 10, UserName = "bernardlim", FullName = "Dr. Bernard Lim" };
                ctx.ApplicationUsers.AddRange(studentUser, supUser);

                var project = new FYPProject
                {
                    Id = 53,
                    Title = "Web-Based FYP Progress Tracking System",
                    StudentId = 69,
                    SupervisorId = 10,
                    AcademicSession = "Session 2026/2027",
                    Status = "Approved"
                };
                ctx.FYPProjects.Add(project);

                var iafMilestone = new FYPMilestone
                {
                    Id = 321,
                    FYPProjectId = 53,
                    Title = "FYP 1: Initial Assessment Form (IAF)",
                    MilestoneCode = "IAF",
                    Stage = 1,
                    IsRequired = true,
                    Status = "AdminApproved",
                    MilestoneSource = "Default"
                };
                var dppMilestone = new FYPMilestone
                {
                    Id = 324,
                    FYPProjectId = 53,
                    Title = "FYP 1: Stage 1 Final Submission (DPP)",
                    MilestoneCode = "STAGE1_FINAL",
                    Stage = 1,
                    IsRequired = true,
                    Status = "Pending",
                    MilestoneSource = "Default"
                };
                ctx.FYPMilestones.AddRange(iafMilestone, dppMilestone);
                await ctx.SaveChangesAsync();

                // STEP 1: Supervisor creates Stage 1 Progress Task
                var (supCtrl, _) = CreateSupervisorController(ctx, 10, "bernardlim", tempDir);
                var addResult = await supCtrl.AddMilestone(
                    projectId: 53,
                    title: "Literature Review & Problem Analysis Draft",
                    description: "Submit comprehensive literature review matrix.",
                    dueDate: DateTime.Now.AddDays(14),
                    stage: 1
                );

                var createdTask = await ctx.FYPMilestones.FirstOrDefaultAsync(m => m.FYPProjectId == 53 && m.MilestoneSource == "Supervisor");
                Assert.NotNull(createdTask);
                Assert.Equal("Literature Review & Problem Analysis Draft", createdTask!.Title);
                Assert.Equal("Draft", createdTask.Status);
                Assert.Equal(1, createdTask.Stage);

                // Negative check: Student cannot submit Stage 1 Final DPP yet because the supervisor task is not Completed
                var allMs = await ctx.FYPMilestones.Where(m => m.FYPProjectId == 53).ToListAsync();
                var dppCheck1 = GradingHelper.CanSubmitStage1FinalDpp(allMs, iafMilestone);
                Assert.False(dppCheck1.Allowed);

                // STEP 2: Student submits progress document for Supervisor task
                byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 Mock literature review content");
                var stream = new MemoryStream(pdfBytes);
                var file = new FormFile(stream, 0, pdfBytes.Length, "attachedFile", "LitReview.pdf");

                studentCtrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
                var submitTaskResult = await studentCtrl.SubmitDigitalForm(
                    milestoneId: createdTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: file,
                    attachedFiles: null,
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: DateTime.Now.AddDays(-7),
                    endDate: DateTime.Now,
                    workDone: "Completed literature review matrix.",
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: "50",
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false
                );

                var taskAfterStudentSubmit = await ctx.FYPMilestones.FindAsync(createdTask.Id);
                Assert.Equal("PendingSupervisorReview", taskAfterStudentSubmit!.Status);

                // STEP 3: Supervisor reviews and approves the progress task (Mark as Completed)
                var reviewTaskResult = await supCtrl.ReviewProgressMilestone(
                    milestoneId: createdTask.Id,
                    decision: "complete",
                    feedback: "Excellent literature matrix analysis. Approved."
                );

                var taskAfterSupApproval = await ctx.FYPMilestones.FindAsync(createdTask.Id);
                Assert.Equal("Completed", taskAfterSupApproval!.Status);

                // STEP 4: Gating Check: With task approved, Stage 1 Final DPP is unlocked
                var allMsAfter = await ctx.FYPMilestones.Where(m => m.FYPProjectId == 53).ToListAsync();
                var dppCheck2 = GradingHelper.CanSubmitStage1FinalDpp(allMsAfter, iafMilestone);
                Assert.True(dppCheck2.Allowed);
                Assert.True(dppCheck2.AllApproved);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
