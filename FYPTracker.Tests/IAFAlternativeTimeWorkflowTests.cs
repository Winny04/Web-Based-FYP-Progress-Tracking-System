using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FYPTracker.Controllers;
using FYPTracker.Models;
using FYPTracker.Services.Defense;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class IAFAlternativeTimeWorkflowTests
    {
        private sealed class WorkflowTestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _values = new();

            public bool IsAvailable => true;
            public string Id => "WorkflowTestSession";
            public IEnumerable<string> Keys => _values.Keys;

            public void Clear() => _values.Clear();
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Remove(string key) => _values.Remove(key);
            public void Set(string key, byte[] value) => _values[key] = value;
            public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
        }

        private sealed class TestHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; }
        }

        private static IHttpContextAccessor CreateMockAccessor(int userId, string role, string activeRole)
        {
            var claims = new List<Claim>
            {
                new Claim("Id", userId.ToString()),
                new Claim(ClaimTypes.Name, $"User_{userId}"),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var context = new DefaultHttpContext { User = principal };
            context.Session = new WorkflowTestSession();
            context.Session.SetString("ActiveRole", activeRole);

            return new TestHttpContextAccessor { HttpContext = context };
        }

        private static string CreateAttemptRemarks(int attemptNumber = 1)
        {
            var history = new IafAttemptHistory
            {
                CurrentAttempt = attemptNumber,
                Attempts = new List<IafAttempt>
                {
                    new IafAttempt { AttemptNumber = attemptNumber, Status = "DefenseRequired" }
                }
            };
            return "MilestoneRemarks JSON: " + System.Text.Json.JsonSerializer.Serialize(history);
        }

        private static FYPTrackerContext CreateInMemoryContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new FYPTrackerContext(options);
            context.FYPSettings.Add(new FYPSetting { Key = "IAFDefenseDurationMinutes", Value = "15" });
            context.FYPSettings.Add(new FYPSetting { Key = "LecturerWorkingHoursStart", Value = "08:00" });
            context.FYPSettings.Add(new FYPSetting { Key = "LecturerWorkingHoursEnd", Value = "17:00" });
            context.AcademicSessions.Add(new AcademicSession { Id = 1, Name = "2026/2027-1", IsActive = true });
            context.SaveChanges();

            return context;
        }

        private static IConfiguration CreateConfig(
            string connStr = "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
            bool flagEnabled = true,
            bool smtpOffline = true)
        {
            var settings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", connStr },
                { "FeatureFlags:IAFAlternativeTimeRequestEnabled", flagEnabled.ToString().ToLower() },
                { "SmtpSettings:Offline", smtpOffline.ToString().ToLower() }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        #region 1. Feature Gate Tests

        [Fact]
        public void FeatureGate_TestingEnvironmentAndDatabase_ReturnsTrue()
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
                flagEnabled: true,
                smtpOffline: true);

            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            bool isEnabled = IAFAlternativeTimeFeatureGate.IsFeatureEnabled(config, env, context);

            Assert.True(isEnabled);
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("Production")]
        [InlineData("Staging")]
        public void FeatureGate_NonTestingEnvironment_ReturnsFalse(string envName)
        {
            var env = new TestWebHostEnvironment { EnvironmentName = envName };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
                flagEnabled: true,
                smtpOffline: true);

            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            bool isEnabled = IAFAlternativeTimeFeatureGate.IsFeatureEnabled(config, env, context);

            Assert.False(isEnabled);
        }

        [Fact]
        public void FeatureGate_NonTestDatabase_ReturnsFalse()
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;",
                flagEnabled: true,
                smtpOffline: true);

            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            bool isEnabled = IAFAlternativeTimeFeatureGate.IsFeatureEnabled(config, env, context);

            Assert.False(isEnabled);
        }

        [Fact]
        public void FeatureGate_SmtpOnline_ReturnsFalse()
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
                flagEnabled: true,
                smtpOffline: false);

            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            bool isEnabled = IAFAlternativeTimeFeatureGate.IsFeatureEnabled(config, env, context);

            Assert.False(isEnabled);
        }

        [Fact]
        public void FeatureGate_FlagDisabled_ReturnsFalse()
        {
            var env = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
                flagEnabled: false,
                smtpOffline: true);

            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            bool isEnabled = IAFAlternativeTimeFeatureGate.IsFeatureEnabled(config, env, context);

            Assert.False(isEnabled);
        }

        #endregion

        #region 2. Authorization and Ownership Tests

        [Fact]
        public async Task AdminAuthorization_NonAdminUser_FailsWithUnauthorized()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
            var accessor = CreateMockAccessor(userId: 50, role: "Student", activeRole: "Student");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 10,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 20,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 50,
                isAdminUser: false,
                supervisorId: 15,
                adminNotes: "Test unauthorized");

            Assert.False(result.Success);
            Assert.Contains(result.ErrorCode, new[] { "INVALID_WORKSPACE", "UNAUTHORIZED_ROLE", "UNAUTHORIZED_ACCESS" });
        }

        [Fact]
        public async Task ExaminerOwnership_DifferentUser_FailsWithForbidden()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
            
            // Seed examiner user
            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var unassignedExaminer = new ApplicationUser
            {
                Id = 99,
                UserName = "unassigned_exm",
                Email = "unassigned@test.com",
                FullName = "Unassigned Examiner",
                IsResign = false
            };
            unassignedExaminer.UserRoles.Add(new UserRoleMapping { UserId = 99, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(unassignedExaminer);

            // Seed milestone & project
            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject { Id = 100, Title = "P1", SupervisorId = 15, AcademicSession = "2026/2027-1" }
            };
            context.FYPMilestones.Add(milestone);

            // Seed request assigned to examiner ID 42
            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 99, role: "Examiner", activeRole: "Examiner");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.SubmitSuggestionAsync(
                requestId: 1,
                currentLecturerUserId: 99,
                suggestedDate: DateTime.Today.AddDays(7),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 15,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(17, 0, 0),
                lecturerNotes: "I should not be able to suggest for this request",
                clientRowVersion: new byte[] { 1, 0, 0, 0 });

            Assert.False(result.Success);
            Assert.Equal("UnauthorizedLecturer", result.ErrorCode);
        }

        #endregion

        #region 3. Business Rule Validation Tests

        [Fact]
        public async Task InvalidExaminer_SupervisorCannotBeExaminer()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            // Seed admin
            var adminRole = new AppRole { Id = 1, Name = "Admin", Description = "Admin Role" };
            var admin = new ApplicationUser { Id = 1, UserName = "admin1", FullName = "Admin", IsResign = false };
            admin.UserRoles.Add(new UserRoleMapping { UserId = 1, RoleId = 1, Role = adminRole });
            context.ApplicationUsers.Add(admin);

            // Seed supervisor who also has examiner role
            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var supervisorExaminer = new ApplicationUser { Id = 15, UserName = "sup15", FullName = "Supervisor 15", IsResign = false };
            supervisorExaminer.UserRoles.Add(new UserRoleMapping { UserId = 15, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(supervisorExaminer);

            // Seed milestone & project
            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject
                {
                    Id = 100,
                    Title = "Test Project",
                    SupervisorId = 15,
                    AcademicSession = "2026/2027-1"
                }
            };
            context.FYPMilestones.Add(milestone);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 1, role: "Admin", activeRole: "Admin");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 10,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 15, // Same as supervisor ID!
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 1,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Attempting to assign supervisor as examiner");

            Assert.False(result.Success);
            Assert.Equal("SUPERVISOR_CANNOT_EXAMINE", result.ErrorCode);
        }

        [Fact]
        public async Task DuplicateActiveRequest_FailsWhenActiveRequestExists()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            // Seed admin
            var adminRole = new AppRole { Id = 1, Name = "Admin", Description = "Admin Role" };
            var admin = new ApplicationUser { Id = 1, UserName = "admin1", FullName = "Admin", IsResign = false };
            admin.UserRoles.Add(new UserRoleMapping { UserId = 1, RoleId = 1, Role = adminRole });
            context.ApplicationUsers.Add(admin);

            // Seed examiner
            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examiner = new ApplicationUser { Id = 20, UserName = "exm20", FullName = "Examiner 20", IsResign = false };
            examiner.UserRoles.Add(new UserRoleMapping { UserId = 20, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examiner);

            // Seed milestone & project
            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject
                {
                    Id = 100,
                    Title = "Test Project",
                    SupervisorId = 15,
                    AcademicSession = "2026/2027-1"
                }
            };
            context.FYPMilestones.Add(milestone);

            // Existing Pending request
            var existingRequest = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 20,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(existingRequest);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 1, role: "Admin", activeRole: "Admin");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 10,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 20,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 1,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Attempting to create duplicate active request");

            Assert.False(result.Success);
            Assert.Equal("DuplicateActiveRequest", result.ErrorCode);
        }

        [Fact]
        public async Task InvalidMilestone_RejectsNonExistentMilestone()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var adminRole = new AppRole { Id = 1, Name = "Admin", Description = "Admin Role" };
            var admin = new ApplicationUser { Id = 1, UserName = "admin1", FullName = "Admin", IsResign = false };
            admin.UserRoles.Add(new UserRoleMapping { UserId = 1, RoleId = 1, Role = adminRole });
            context.ApplicationUsers.Add(admin);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 1, role: "Admin", activeRole: "Admin");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.CreateRequestAsync(
                milestoneId: 9999,
                attemptNumber: 1,
                academicSessionId: 1,
                requestedLecturerId: 20,
                requestedLecturerRole: "Examiner",
                currentAdminUserId: 1,
                isAdminUser: true,
                supervisorId: 15,
                adminNotes: "Non-existent milestone");

            Assert.False(result.Success);
            Assert.Equal("MILESTONE_NOT_FOUND", result.ErrorCode);
        }

        #endregion

        #region 4. Suggestion Validation and Booking Separation Tests

        [Fact]
        public async Task SuggestionValidation_PastDate_ReturnsError()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examiner = new ApplicationUser { Id = 42, UserName = "exm42", FullName = "Examiner 42", IsResign = false };
            examiner.UserRoles.Add(new UserRoleMapping { UserId = 42, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examiner);

            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject { Id = 100, Title = "P1", SupervisorId = 15, AcademicSession = "2026/2027-1" }
            };
            context.FYPMilestones.Add(milestone);

            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 42, role: "Examiner", activeRole: "Examiner");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var pastDate = DateTime.Today.AddDays(-1);

            var result = await service.SubmitSuggestionAsync(
                requestId: 1,
                currentLecturerUserId: 42,
                suggestedDate: pastDate,
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 15,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(17, 0, 0),
                clientRowVersion: new byte[] { 1, 0, 0, 0 });

            Assert.False(result.Success);
            Assert.Equal("PastDate", result.ErrorCode);
        }

        [Fact]
        public async Task SuggestionValidation_OutsideWorkingHours_ReturnsError()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examiner = new ApplicationUser { Id = 42, UserName = "exm42", FullName = "Examiner 42", IsResign = false };
            examiner.UserRoles.Add(new UserRoleMapping { UserId = 42, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examiner);

            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject { Id = 100, Title = "P1", SupervisorId = 15, AcademicSession = "2026/2027-1" }
            };
            context.FYPMilestones.Add(milestone);

            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 42, role: "Examiner", activeRole: "Examiner");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var futureDate = DateTime.Today.AddDays(7);

            // 18:00 is after 17:00 working hours end
            var result = await service.SubmitSuggestionAsync(
                requestId: 1,
                currentLecturerUserId: 42,
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(18, 0, 0),
                durationMinutes: 15,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(17, 0, 0),
                clientRowVersion: new byte[] { 1, 0, 0, 0 });

            Assert.False(result.Success);
            Assert.Equal("OutsideWorkingHours", result.ErrorCode);
        }

        [Fact]
        public async Task NoAutomaticBookingOnSuggestion_TransitionsToSuggestedOnly()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examiner = new ApplicationUser { Id = 42, UserName = "exm42", FullName = "Examiner 42", IsResign = false };
            examiner.UserRoles.Add(new UserRoleMapping { UserId = 42, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examiner);

            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject { Id = 100, Title = "P1", SupervisorId = 15, AcademicSession = "2026/2027-1" }
            };
            context.FYPMilestones.Add(milestone);

            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 42, role: "Examiner", activeRole: "Examiner");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var futureDate = DateTime.Today.AddDays(7);
            var result = await service.SubmitSuggestionAsync(
                requestId: 1,
                currentLecturerUserId: 42,
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 15,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(17, 0, 0),
                lecturerNotes: "I am free at 10 AM",
                clientRowVersion: new byte[] { 1, 0, 0, 0 });

            Assert.True(result.Success);

            // Verify status is Suggested
            var updatedReq = await context.IAFAlternativeTimeRequests.FindAsync(1);
            Assert.NotNull(updatedReq);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, updatedReq!.Status);
            Assert.Equal(futureDate.Date, updatedReq.SuggestedDate);
            Assert.Equal(new TimeSpan(10, 0, 0), updatedReq.SuggestedStartTime);
            Assert.Equal(new TimeSpan(10, 15, 0), updatedReq.SuggestedEndTime);
            Assert.Equal(15, updatedReq.SuggestedDurationMinutes);

            // Verify NO booking was auto-created!
            var bookings = await context.IAFDefenseBookings.ToListAsync();
            Assert.Empty(bookings);
        }

        #endregion

        #region 5. Concurrency and Final Booking Association Tests

        [Fact]
        public async Task ConcurrencyConflict_StaleRowVersion_Fails()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examiner = new ApplicationUser { Id = 42, UserName = "exm42", FullName = "Examiner 42", IsResign = false };
            examiner.UserRoles.Add(new UserRoleMapping { UserId = 42, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examiner);

            var milestone = new FYPMilestone
            {
                Id = 10,
                Status = "DefenseRequired",
                Remarks = CreateAttemptRemarks(1),
                Project = new FYPProject { Id = 100, Title = "P1", SupervisorId = 15, AcademicSession = "2026/2027-1" }
            };
            context.FYPMilestones.Add(milestone);

            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusPending,
                RowVersion = new byte[] { 1, 2, 3, 4 } // Actual DB RowVersion
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 42, role: "Examiner", activeRole: "Examiner");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var futureDate = DateTime.Today.AddDays(7);
            var staleRowVersion = new byte[] { 9, 9, 9, 9 };

            var result = await service.SubmitSuggestionAsync(
                requestId: 1,
                currentLecturerUserId: 42,
                suggestedDate: futureDate,
                suggestedStartTime: new TimeSpan(10, 0, 0),
                durationMinutes: 15,
                workingHoursStart: new TimeSpan(8, 0, 0),
                workingHoursEnd: new TimeSpan(17, 0, 0),
                clientRowVersion: staleRowVersion);

            Assert.False(result.Success);
            Assert.Equal("CONCURRENCY_CONFLICT", result.ErrorCode);
        }

        [Fact]
        public async Task FinalizeAccepted_TransitionsToAccepted_WithBookingAssociation()
        {
            using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

            var adminRole = new AppRole { Id = 1, Name = "Admin", Description = "Admin Role" };
            var admin = new ApplicationUser { Id = 1, UserName = "admin1", FullName = "Admin", IsResign = false };
            admin.UserRoles.Add(new UserRoleMapping { UserId = 1, RoleId = 1, Role = adminRole });
            context.ApplicationUsers.Add(admin);

            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = 42,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = 1,
                Status = IAFAlternativeTimeRequest.StatusSuggested,
                SuggestedDate = new DateTime(2026, 11, 10),
                SuggestedStartTime = new TimeSpan(10, 0, 0),
                SuggestedEndTime = new TimeSpan(10, 15, 0),
                SuggestedDurationMinutes = 15,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.IAFAlternativeTimeRequests.Add(request);

            // Seed matching booking with Id 777
            var booking = new IAFDefenseBooking
            {
                Id = 777,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                ExaminerId = 42,
                DefenseDate = new DateTime(2026, 11, 10),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 15, 0),
                DurationMinutes = 15,
                Venue = "Room 101",
                BookingStatus = "Scheduled"
            };
            context.IAFDefenseBookings.Add(booking);
            await context.SaveChangesAsync();

            var accessor = CreateMockAccessor(userId: 1, role: "Admin", activeRole: "Admin");
            var service = new IAFAlternativeTimeRequestService(context, accessor);

            var result = await service.FinalizeAcceptedAsync(
                requestId: 1,
                currentAdminUserId: 1,
                isAdminUser: true,
                confirmedBookingId: 777,
                clientRowVersion: new byte[] { 1, 0, 0, 0 });

            Assert.True(result.Success);

            var finalizedReq = await context.IAFAlternativeTimeRequests.FindAsync(1);
            Assert.NotNull(finalizedReq);
            Assert.Equal(IAFAlternativeTimeRequest.StatusAccepted, finalizedReq!.Status);
            Assert.Equal(1, finalizedReq.FinalizedByAdminId);
            Assert.NotNull(finalizedReq.FinalizedAt);
            Assert.Contains("777", finalizedReq.DecisionReason);
        }

        #endregion

        #region 6. Controller 503 Guard Tests When Disabled

        [Fact]
        public async Task AdminController_WhenFeatureDisabled_Returns503()
        {
            var controller = new AdminController(
                context: null!,
                emailService: null!,
                hubContext: null!);

            var createRes = controller.CreateIAFAlternativeTimeRequest(milestoneId: 10, examinerId: 42, adminNotes: "Disabled test");
            var createObj = Assert.IsType<ObjectResult>(createRes);
            Assert.Equal(503, createObj.StatusCode);

            var rejectRes = controller.RejectIAFAlternativeTimeSuggestion(requestId: 1, reason: "Disabled test");
            var rejectObj = Assert.IsType<ObjectResult>(rejectRes);
            Assert.Equal(503, rejectObj.StatusCode);

            var cancelRes = controller.CancelIAFAlternativeTimeRequest(requestId: 1, reason: "Disabled test");
            var cancelObj = Assert.IsType<ObjectResult>(cancelRes);
            Assert.Equal(503, cancelObj.StatusCode);

            var confirmRes = await controller.ConfirmIAFAlternativeTimeBooking(requestId: 1, venue: "Room 1");
            var confirmObj = Assert.IsType<ObjectResult>(confirmRes);
            Assert.Equal(503, confirmObj.StatusCode);
        }

        [Fact]
        public void ExaminerController_WhenFeatureDisabled_Returns503()
        {
            var controller = new ExaminerController(null!, null!, null!, null!);

            var submitRes = controller.SubmitIAFAlternativeTimeSuggestion(
                requestId: 1,
                suggestedDate: new DateTime(2026, 11, 10),
                suggestedStartTime: new TimeSpan(10, 0, 0),
                lecturerNotes: "Disabled test");
            var submitObj = Assert.IsType<ObjectResult>(submitRes);
            Assert.Equal(503, submitObj.StatusCode);
        }

        #endregion

        #region 7. Phase B3.5 Booking Consistency Tests

        private sealed class TestFailingAlternativeTimeService : IIAFAlternativeTimeRequestService
        {
            private readonly IIAFAlternativeTimeRequestService _inner;
            public TestFailingAlternativeTimeService(IIAFAlternativeTimeRequestService inner) => _inner = inner;

            public Task<IAFAlternativeTimeRequest?> GetRequestByIdAsync(int requestId) => _inner.GetRequestByIdAsync(requestId);
            public Task<IAFAlternativeTimeRequest?> GetActiveRequestAsync(int milestoneId, int attemptNumber) => _inner.GetActiveRequestAsync(milestoneId, attemptNumber);
            public Task<List<IAFAlternativeTimeRequest>> GetRequestHistoryAsync(int milestoneId, int attemptNumber) => _inner.GetRequestHistoryAsync(milestoneId, attemptNumber);
            public Task<AlternativeTimeRequestResult> CreateRequestAsync(int milestoneId, int attemptNumber, int academicSessionId, int requestedLecturerId, string requestedLecturerRole, int currentAdminUserId, bool isAdminUser, int supervisorId, string? adminNotes) => _inner.CreateRequestAsync(milestoneId, attemptNumber, academicSessionId, requestedLecturerId, requestedLecturerRole, currentAdminUserId, isAdminUser, supervisorId, adminNotes);
            public Task<AlternativeTimeRequestResult> SubmitSuggestionAsync(int requestId, int currentLecturerUserId, DateTime suggestedDate, TimeSpan suggestedStartTime, int durationMinutes, TimeSpan workingHoursStart, TimeSpan workingHoursEnd, string? lecturerNotes = null, byte[]? clientRowVersion = null) => _inner.SubmitSuggestionAsync(requestId, currentLecturerUserId, suggestedDate, suggestedStartTime, durationMinutes, workingHoursStart, workingHoursEnd, lecturerNotes, clientRowVersion);
            public Task<AlternativeTimeRequestResult> FinalizeAcceptedAsync(int requestId, int currentAdminUserId, bool isAdminUser, int confirmedBookingId, byte[]? clientRowVersion = null)
            {
                return Task.FromResult(AlternativeTimeRequestResult.Fail(
                    "SIMULATED_FINALIZATION_CRASH",
                    "Simulated failure finalizing the Alternative Time Request."));
            }
            public Task<AlternativeTimeRequestResult> RejectSuggestionAsync(int requestId, int currentAdminUserId, bool isAdminUser, string reason, byte[]? clientRowVersion = null) => _inner.RejectSuggestionAsync(requestId, currentAdminUserId, isAdminUser, reason, clientRowVersion);
            public Task<AlternativeTimeRequestResult> CancelRequestAsync(int requestId, int currentAdminUserId, bool isAdminUser, string reason, byte[]? clientRowVersion = null) => _inner.CancelRequestAsync(requestId, currentAdminUserId, isAdminUser, reason, clientRowVersion);
        }

        private static string? GetResultCode(IActionResult result)
        {
            object? val = null;
            if (result is ObjectResult obj) val = obj.Value;
            else if (result is JsonResult json) val = json.Value;
            if (val == null) return null;
            var prop = val.GetType().GetProperty("code") ?? val.GetType().GetProperty("Code");
            return prop?.GetValue(val)?.ToString();
        }

        private static bool? GetResultSuccess(IActionResult result)
        {
            object? val = null;
            if (result is ObjectResult obj) val = obj.Value;
            else if (result is JsonResult json) val = json.Value;
            if (val == null) return null;
            var prop = val.GetType().GetProperty("success") ?? val.GetType().GetProperty("Success");
            var res = prop?.GetValue(val);
            return res is bool b ? b : null;
        }

        private static bool? GetResultBoolProp(IActionResult result, string propName)
        {
            object? val = null;
            if (result is ObjectResult obj) val = obj.Value;
            else if (result is JsonResult json) val = json.Value;
            if (val == null) return null;
            var prop = val.GetType().GetProperty(propName);
            var res = prop?.GetValue(val);
            return res is bool b ? b : null;
        }

        private static int? GetResultBookingId(IActionResult result)
        {
            object? val = null;
            if (result is ObjectResult obj) val = obj.Value;
            else if (result is JsonResult json) val = json.Value;
            if (val == null) return null;
            var prop = val.GetType().GetProperty("bookingId") ?? val.GetType().GetProperty("BookingId");
            var res = prop?.GetValue(val);
            return res is int i ? i : null;
        }

        private static async Task<(AdminController controller, FYPTrackerContext context, IAFAlternativeTimeRequest request, byte[] rowVersion)> SetupAdminControllerTestAsync(
            string dbName,
            int adminUserId = 1,
            string activeRole = "Admin",
            string roleClaim = "Admin",
            int examinerId = 42,
            int supervisorId = 15,
            string milestoneStatus = "DefenseRequired",
            bool slotAvailable = true,
            Func<FYPTrackerContext, IHttpContextAccessor, IIAFAlternativeTimeRequestService>? customAltServiceFactory = null)
        {
            var context = CreateInMemoryContext(dbName);
            var accessor = CreateMockAccessor(userId: adminUserId, role: roleClaim, activeRole: activeRole);

            // Seed admin user
            var adminRole = new AppRole { Id = 1, Name = "Admin", Description = "Admin Role" };
            var adminUser = new ApplicationUser
            {
                Id = adminUserId,
                UserName = $"admin_{adminUserId}",
                Email = $"admin_{adminUserId}@test.com",
                FullName = "Admin User",
                IsResign = false
            };
            adminUser.UserRoles.Add(new UserRoleMapping { UserId = adminUserId, RoleId = 1, Role = adminRole });
            context.ApplicationUsers.Add(adminUser);

            // Seed examiner user
            var examinerRole = new AppRole { Id = 3, Name = "Examiner", Description = "Examiner Role" };
            var examinerUser = new ApplicationUser
            {
                Id = examinerId,
                UserName = $"examiner_{examinerId}",
                Email = $"examiner_{examinerId}@test.com",
                FullName = "Examiner User",
                IsResign = false
            };
            examinerUser.UserRoles.Add(new UserRoleMapping { UserId = examinerId, RoleId = 3, Role = examinerRole });
            context.ApplicationUsers.Add(examinerUser);

            // Seed supervisor user (if distinct from examiner)
            if (supervisorId != examinerId)
            {
                var supervisorUser = new ApplicationUser
                {
                    Id = supervisorId,
                    UserName = $"supervisor_{supervisorId}",
                    Email = $"supervisor_{supervisorId}@test.com",
                    FullName = "Supervisor User",
                    IsResign = false
                };
                context.ApplicationUsers.Add(supervisorUser);
            }

            // Seed student user
            var studentUser = new ApplicationUser
            {
                Id = 1000,
                UserName = "student_1",
                Email = "student1@test.com",
                FullName = "Student One",
                IsResign = false
            };
            context.ApplicationUsers.Add(studentUser);

            // Seed project & milestone
            var project = new FYPProject
            {
                Id = 200,
                Title = "FYP Test Project",
                SupervisorId = supervisorId,
                StudentId = 1000,
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var milestone = new FYPMilestone
            {
                Id = 20,
                Status = milestoneStatus,
                Remarks = CreateAttemptRemarks(1),
                Project = project
            };
            context.FYPMilestones.Add(milestone);

            // Seed request
            var initialRowVersion = new byte[] { 0, 0, 0, 1 };
            var request = new IAFAlternativeTimeRequest
            {
                Id = 1,
                MilestoneId = milestone.Id,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                RequestedLecturerId = examinerId,
                RequestedLecturerRole = "Examiner",
                RequestedByAdminId = adminUserId,
                Status = IAFAlternativeTimeRequest.StatusSuggested,
                SuggestedDate = new DateTime(2026, 11, 20),
                SuggestedStartTime = new TimeSpan(10, 0, 0),
                SuggestedEndTime = new TimeSpan(10, 15, 0),
                SuggestedDurationMinutes = 15,
                RowVersion = initialRowVersion
            };
            context.IAFAlternativeTimeRequests.Add(request);
            await context.SaveChangesAsync();

            var env = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            var config = CreateConfig(
                connStr: "Server=Winny\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;",
                flagEnabled: true,
                smtpOffline: true);

            var altService = customAltServiceFactory != null
                ? customAltServiceFactory(context, accessor)
                : new IAFAlternativeTimeRequestService(context, accessor);

            var fakeAvailability = new FakeLecturerAvailabilityService
            {
                IsSlotAvailableFunc = (lecId, date, s, e, sess, excl) => Task.FromResult(slotAvailable)
            };
            var bookingService = new IAFDefenseBookingService(context, fakeAvailability);
            var controller = new AdminController(context, null!, null!, altService, env, config, bookingService);
            controller.ControllerContext = new ControllerContext { HttpContext = accessor.HttpContext! };

            return (controller, context, request, request.RowVersion ?? initialRowVersion);
        }

        [Fact]
        public async Task ConfirmBooking_MissingRowVersion_FailsBeforeBooking()
        {
            var (controller, context, request, _) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());

            var result = await controller.ConfirmIAFAlternativeTimeBooking(requestId: request.Id, venue: "Room 101", rowVersion: null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("ROW_VERSION_REQUIRED", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);
            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, reqAfter!.Status);
        }

        [Fact]
        public async Task ConfirmBooking_InvalidRowVersion_FailsBeforeBooking()
        {
            var (controller, context, request, _) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());

            var result = await controller.ConfirmIAFAlternativeTimeBooking(requestId: request.Id, venue: "Room 101", rowVersion: "!!!not-valid-base64!!!");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("INVALID_ROW_VERSION", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);
            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, reqAfter!.Status);
        }

        [Fact]
        public async Task ConfirmBooking_StaleRowVersion_FailsBeforeBooking()
        {
            var (controller, context, request, _) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());
            var staleRowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9, 9 });

            var result = await controller.ConfirmIAFAlternativeTimeBooking(requestId: request.Id, venue: "Room 101", rowVersion: staleRowVersion);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("CONCURRENCY_CONFLICT", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);
            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, reqAfter!.Status);
        }

        [Fact]
        public async Task ConfirmBooking_NonAdminWorkspace_FailsWithInvalidWorkspace()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(
                Guid.NewGuid().ToString(),
                activeRole: "Examiner");

            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("INVALID_WORKSPACE", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);
        }

        [Fact]
        public async Task ConfirmBooking_SupervisorAsExaminer_FailsWithSupervisorCannotExamine()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(
                Guid.NewGuid().ToString(),
                examinerId: 15,
                supervisorId: 15);

            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("SUPERVISOR_CANNOT_EXAMINE", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);
        }

        [Fact]
        public async Task ConfirmBooking_BookingConflict_FailsAndDoesNotAcceptRequest()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(
                Guid.NewGuid().ToString(),
                slotAvailable: false);

            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("LecturerNotAvailable", GetResultCode(badRequest));
            Assert.Empty(context.IAFDefenseBookings);

            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, reqAfter!.Status);
        }

        [Fact]
        public async Task ConfirmBooking_FinalizationFailure_RollsBackBookingAndDoesNotAcceptRequest()
        {
            var (controller, ctx, request, rowVersion) = await SetupAdminControllerTestAsync(
                Guid.NewGuid().ToString(),
                customAltServiceFactory: (c, a) =>
                {
                    var real = new IAFAlternativeTimeRequestService(c, a);
                    return new TestFailingAlternativeTimeService(real);
                });

            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("SIMULATED_FINALIZATION_CRASH", GetResultCode(badRequest));

            // Booking must have been rolled back / removed atomically
            Assert.Empty(ctx.IAFDefenseBookings);

            // Request must remain Suggested
            var reqAfter = await ctx.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusSuggested, reqAfter!.Status);

            // Milestone status must remain DefenseRequired
            var milestone = await ctx.FYPMilestones.FindAsync(request.MilestoneId);
            Assert.Equal("DefenseRequired", milestone!.Status);
        }

        [Fact]
        public async Task ConfirmBooking_RepeatedConfirmation_ReturnsSuccessWithoutDuplicateBooking()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());

            // 1st confirmation: succeeds
            var firstResult = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            var firstJson = Assert.IsType<JsonResult>(firstResult);
            Assert.True(GetResultSuccess(firstJson));
            var firstBookingId = GetResultBookingId(firstJson);
            Assert.NotNull(firstBookingId);
            Assert.Single(context.IAFDefenseBookings);

            var reqAfterFirst = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusAccepted, reqAfterFirst!.Status);

            // 2nd confirmation with updated rowVersion: idempotent retry returns success without duplicate booking
            var secondResult = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(reqAfterFirst.RowVersion ?? rowVersion));

            var secondJson = Assert.IsType<JsonResult>(secondResult);
            Assert.True(GetResultSuccess(secondJson));
            Assert.True(GetResultBoolProp(secondJson, "alreadyAccepted"));
            Assert.Equal(firstBookingId, GetResultBookingId(secondJson));

            // Must still have exactly 1 booking in database
            Assert.Single(context.IAFDefenseBookings);
        }

        [Fact]
        public async Task ConfirmBooking_PartialSuccessRecovery_ReconcilesExistingBooking()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());

            // Pre-seed an existing matching booking as if a previous disconnected commit succeeded
            var existingBooking = new IAFDefenseBooking
            {
                Id = 888,
                MilestoneId = request.MilestoneId,
                ExaminerId = request.RequestedLecturerId,
                DefenseDate = request.SuggestedDate!.Value,
                StartTime = request.SuggestedStartTime!.Value,
                EndTime = request.SuggestedEndTime!.Value,
                DurationMinutes = request.SuggestedDurationMinutes!.Value,
                Venue = "Room 101",
                BookingStatus = "Scheduled",
                AttemptNumber = request.AttemptNumber,
                AcademicSessionId = request.AcademicSessionId
            };
            context.IAFDefenseBookings.Add(existingBooking);
            await context.SaveChangesAsync();

            var currentReq = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            var currentToken = currentReq?.RowVersion ?? rowVersion;

            // Confirm request: should reconcile by linking to existing booking instead of creating duplicate
            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(currentToken));

            var json = Assert.IsType<JsonResult>(result);
            Assert.True(GetResultSuccess(json));
            Assert.True(GetResultBoolProp(json, "reconciled"));
            Assert.Equal(888, GetResultBookingId(json));

            // Request is now Accepted
            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.Equal(IAFAlternativeTimeRequest.StatusAccepted, reqAfter!.Status);
            Assert.Contains("888", reqAfter.DecisionReason);

            // Exactly 1 booking exists (no duplicates created)
            Assert.Single(context.IAFDefenseBookings);
        }

        [Fact]
        public async Task ConfirmBooking_SuccessfulExecution_AtomicallyCreatesBookingAndAcceptsRequest()
        {
            var (controller, context, request, rowVersion) = await SetupAdminControllerTestAsync(Guid.NewGuid().ToString());

            var result = await controller.ConfirmIAFAlternativeTimeBooking(
                requestId: request.Id,
                venue: "Room 101",
                rowVersion: Convert.ToBase64String(rowVersion));

            if (result is BadRequestObjectResult bad)
            {
                var valObj = bad.Value;
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(valObj);
                Assert.Fail($"Returned BadRequestObjectResult: {jsonStr}");
            }

            var json = Assert.IsType<JsonResult>(result);
            Assert.True(GetResultSuccess(json));
            var bookingId = GetResultBookingId(json);
            Assert.NotNull(bookingId);

            // Verify booking in DB
            var booking = await context.IAFDefenseBookings.FindAsync(bookingId!.Value);
            Assert.NotNull(booking);
            Assert.Equal("Scheduled", booking!.BookingStatus);
            Assert.Equal("Room 101", booking.Venue);

            // Verify request is Accepted
            var reqAfter = await context.IAFAlternativeTimeRequests.FindAsync(request.Id);
            Assert.NotNull(reqAfter);
            Assert.Equal(IAFAlternativeTimeRequest.StatusAccepted, reqAfter!.Status);
            Assert.Contains(bookingId.Value.ToString(), reqAfter.DecisionReason);

            // Verify milestone is DefenseScheduled
            var milestone = await context.FYPMilestones.FindAsync(request.MilestoneId);
            Assert.NotNull(milestone);
            Assert.Equal("DefenseScheduled", milestone!.Status);
            Assert.Contains("DefenseScheduled", milestone.Remarks);
        }

        #endregion
    }
}
