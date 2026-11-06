using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
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
    public class IafReferencesValidationTests
    {
        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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

        private (StudentController controller, FYPTrackerContext context, string tempDir) CreateStudentController(
            string dbName,
            int userId,
            string username,
            bool configureProject53ForReferences = true)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var context = new FYPTrackerContext(options);
            string tempDir = Path.Combine(Path.GetTempPath(), "FYPTracker_RefTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

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

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = tempDir, WebRootPath = tempDir };
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test;Trusted_Connection=True;" },
                { "DemoUiOverrides:EnforceIafReferencesValidationProjectIds:0", configureProject53ForReferences ? "53" : "999" }
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

        [Fact]
        public void Unit_ValidateIafReferences_RejectsEmpty()
        {
            string err1 = DemoUiOverrideHelper.ValidateIafReferences(null);
            Assert.Contains("required", err1, StringComparison.OrdinalIgnoreCase);

            string err2 = DemoUiOverrideHelper.ValidateIafReferences("");
            Assert.Contains("required", err2, StringComparison.OrdinalIgnoreCase);

            string err3 = DemoUiOverrideHelper.ValidateIafReferences("   \r\n   ");
            Assert.Contains("required", err3, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Unit_ValidateIafReferences_RejectsInvalidYears()
        {
            // Year 2019 is too old (pre-2021)
            string errOld = DemoUiOverrideHelper.ValidateIafReferences("Smith, J. (2019). Machine Learning Frameworks. Journal of AI.");
            Assert.Contains("2021–2026", errOld);

            // Year 2028 is in the future (post-2026)
            string errFuture = DemoUiOverrideHelper.ValidateIafReferences("Taylor, R. (2028). Quantum Computing Trends. IEEE Proceedings.");
            Assert.Contains("2021–2026", errFuture);

            // Missing year
            string errMissing = DemoUiOverrideHelper.ValidateIafReferences("Brown, K. Advanced Systems and Architectures.");
            Assert.Contains("missing a publication year", errMissing);
        }

        [Fact]
        public void Unit_ValidateIafReferences_AcceptsValidFormats()
        {
            // Standard APA-style
            string valid1 = "Lim, B., & Tan, C. (2024). Web-based academic progress tracking. IEEE Transactions on Education, 12(3), 45-56.";
            string res1 = DemoUiOverrideHelper.ValidateIafReferences(valid1);
            Assert.Empty(res1);

            // Common IEEE bracket style with year
            string valid2 = "[1] J. Doe, \"Cloud architecture for student submissions\", Springer 2023.";
            string res2 = DemoUiOverrideHelper.ValidateIafReferences(valid2);
            Assert.Empty(res2);

            // Multiline references (all valid within 2021-2026)
            string valid3 = "1. Johnson, M. (2022). Usability in Academic Portals. ACM Press.\r\n2. Wong, E. (2025). Automated evaluation pipelines. Software Pract., 10(2).";
            string res3 = DemoUiOverrideHelper.ValidateIafReferences(valid3);
            Assert.Empty(res3);
        }

        [Fact]
        public async Task EndToEnd_References_EmptyAllowedDuringSaveDraft()
        {
            string dbName = Guid.NewGuid().ToString();
            var (ctrl, ctx, tempDir) = CreateStudentController(dbName, 69, "p26010051", configureProject53ForReferences: true);

            try
            {
                var student = new ApplicationUser { Id = 69, UserName = "p26010051", FullName = "FYP Demo Student 01" };
                var project = new FYPProject { Id = 53, StudentId = 69, Title = "Tracking System", Status = "Approved" };
                var milestone = new FYPMilestone { Id = 321, FYPProjectId = 53, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", Stage = 1, IsRequired = true, Status = "Draft", MilestoneSource = "Default" };
                ctx.ApplicationUsers.Add(student);
                ctx.FYPProjects.Add(project);
                ctx.FYPMilestones.Add(milestone);
                await ctx.SaveChangesAsync();

                ctrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "draft" },
                    { "title", "Tracking System" },
                    { "scope", "Development" },
                    { "problem", "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds." },
                    { "objective1", "Build web application" },
                    { "objective2", "Test web application" }
                });

                // Calling SubmitDigitalForm with isDraft = true and references = null
                var result = await ctrl.SubmitDigitalForm(
                    milestoneId: 321,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Tracking System",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds.",
                    objective1: "Build web application",
                    objective2: "Test web application",
                    objective3: null,
                    scope: "Development",
                    outcome: null,
                    references: "",
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

                Assert.Equal("Draft saved successfully.", ctrl.TempData["Success"]);
                var savedMs = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("Draft", savedMs!.Status);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task EndToEnd_References_EmptyRejectedDuringSupervisorReviewSubmit()
        {
            string dbName = Guid.NewGuid().ToString();
            var (ctrl, ctx, tempDir) = CreateStudentController(dbName, 69, "p26010051", configureProject53ForReferences: true);

            try
            {
                var student = new ApplicationUser { Id = 69, UserName = "p26010051", FullName = "FYP Demo Student 01" };
                var project = new FYPProject { Id = 53, StudentId = 69, Title = "Tracking System", Status = "Approved" };
                var milestone = new FYPMilestone { Id = 321, FYPProjectId = 53, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", Stage = 1, IsRequired = true, Status = "Draft", MilestoneSource = "Default" };
                ctx.ApplicationUsers.Add(student);
                ctx.FYPProjects.Add(project);
                ctx.FYPMilestones.Add(milestone);
                await ctx.SaveChangesAsync();

                ctrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "supervisor_review" },
                    { "title", "Tracking System" },
                    { "scope", "Development" },
                    { "problem", "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds." },
                    { "objective1", "Build web application" },
                    { "objective2", "Test web application" }
                });

                // Submit for supervisor review with empty references
                var result = await ctrl.SubmitDigitalForm(
                    milestoneId: 321,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Tracking System",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds.",
                    objective1: "Build web application",
                    objective2: "Test web application",
                    objective3: null,
                    scope: "Development",
                    outcome: null,
                    references: "",
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false
                );

                // Should be redirected and have Error in TempData
                var redir = Assert.IsType<RedirectToActionResult>(result);
                Assert.Equal("Forms", redir.ActionName);
                Assert.True(ctrl.TempData.ContainsKey("Error"));
                Assert.Contains("At least one academic reference is required", ctrl.TempData["Error"]!.ToString());

                // Milestone status must NOT have transitioned to PendingSupervisorReview
                var savedMs = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("Draft", savedMs!.Status);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task EndToEnd_References_ValidReferencesAccepted_And_OtherProjectsUnaffected()
        {
            string dbName = Guid.NewGuid().ToString();
            var (ctrl, ctx, tempDir) = CreateStudentController(dbName, 69, "p26010051", configureProject53ForReferences: true);

            try
            {
                var student = new ApplicationUser { Id = 69, UserName = "p26010051", FullName = "FYP Demo Student 01" };
                var project = new FYPProject { Id = 53, StudentId = 69, Title = "Tracking System", Status = "Approved" };
                var milestone = new FYPMilestone { Id = 321, FYPProjectId = 53, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", Stage = 1, IsRequired = true, Status = "Draft", MilestoneSource = "Default" };
                ctx.ApplicationUsers.Add(student);
                ctx.FYPProjects.Add(project);
                ctx.FYPMilestones.Add(milestone);
                await ctx.SaveChangesAsync();

                ctrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "supervisor_review" },
                    { "title", "Tracking System" },
                    { "scope", "Development" },
                    { "problem", "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds." },
                    { "objective1", "Build web application" },
                    { "objective2", "Test web application" },
                    { "references", "Lim, B. (2024). Web-Based FYP Tracking Systems. IEEE Software Engineering, 40(2), 101-112." }
                });

                var result = await ctrl.SubmitDigitalForm(
                    milestoneId: 321,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Tracking System",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds.",
                    objective1: "Build web application",
                    objective2: "Test web application",
                    objective3: null,
                    scope: "Development",
                    outcome: null,
                    references: "Lim, B. (2024). Web-Based FYP Tracking Systems. IEEE Software Engineering, 40(2), 101-112.",
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false
                );

                var savedMs = await ctx.FYPMilestones.FindAsync(321);
                Assert.Equal("PendingSupervisorReview", savedMs!.Status);

                // --- Test other student/project (Project 99, not configured) is unaffected ---
                var otherProject = new FYPProject { Id = 99, StudentId = 69, Title = "Other Project", Status = "Approved" };
                var otherMs = new FYPMilestone { Id = 500, FYPProjectId = 99, Title = "Initial Project Allocation Form (IAF)", MilestoneCode = "IAF", Stage = 1, IsRequired = true, Status = "Draft", MilestoneSource = "Default" };
                ctx.FYPProjects.Add(otherProject);
                ctx.FYPMilestones.Add(otherMs);
                await ctx.SaveChangesAsync();

                // Other project submits with empty references
                ctrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "iafAction", "supervisor_review" },
                    { "title", "Other Project" }
                });

                var otherResult = await ctrl.SubmitDigitalForm(
                    milestoneId: 500,
                    formType: "Initial Project Allocation Form (IAF)",
                    title: "Other Project",
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: "Problem statement with citation (Author, 2024) of sufficient length to pass character thresholds.",
                    objective1: "Build other project",
                    objective2: "Test other project",
                    objective3: null,
                    scope: "Development",
                    outcome: null,
                    references: "", // Empty references
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false
                );

                var otherSavedMs = await ctx.FYPMilestones.FindAsync(500);
                // Other project proceeds through standard flow without reference restriction
                Assert.Equal("PendingSupervisorReview", otherSavedMs!.Status);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
