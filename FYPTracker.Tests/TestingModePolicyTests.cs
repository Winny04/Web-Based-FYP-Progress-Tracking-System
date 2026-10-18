using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FYPTracker.Models;
using FYPTracker.Services.Testing;
using FYPTracker.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class TestingModePolicyTests
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

        private FYPTrackerContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: "TestingModePolicyDb_" + Guid.NewGuid())
                .Options;

            return new FYPTrackerContext(options);
        }

        [Fact]
        public async Task DevelopmentEnvironment_AlwaysReturnsFalse_AndDeniesSavingSettings()
        {
            using var context = CreateInMemoryContext();
            var env = CreateEnv("Development");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;");

            var policy = new TestingModePolicy(context, env, config);

            // In Development, all checks must strictly return false
            Assert.False(await policy.IsRelaxedModeActiveAsync());
            Assert.False(await policy.IsManualMeetingOnlyAsync());
            Assert.False(await policy.IsImmediateLogbookUnlockAllowedAsync());
            Assert.False(await policy.IsAcademicDeadlinesRelaxedAsync());
            Assert.False(await policy.IsMeetingFrequencyRelaxedAsync());
            Assert.False(await policy.IsTimetableAvailabilityRelaxedAsync());

            // Saving settings in Development must strictly throw InvalidOperationException
            var attempt = new TestingRelaxedModeSettingsVM { IsRelaxedModeEnabled = true };
            await Assert.ThrowsAsync<InvalidOperationException>(() => policy.SaveSettingsAsync(attempt));
        }

        [Fact]
        public async Task TestingEnvironment_WithWrongDatabase_DeniesSavingSettings()
        {
            using var context = CreateInMemoryContext();
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;"); // Wrong database!

            var policy = new TestingModePolicy(context, env, config);

            Assert.False(await policy.IsRelaxedModeActiveAsync());
            var attempt = new TestingRelaxedModeSettingsVM { IsRelaxedModeEnabled = true };
            await Assert.ThrowsAsync<InvalidOperationException>(() => policy.SaveSettingsAsync(attempt));
        }

        [Fact]
        public async Task TestingEnvironment_DefaultSettingsAreAllOff()
        {
            using var context = CreateInMemoryContext();
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");

            var policy = new TestingModePolicy(context, env, config);

            var settings = await policy.GetSettingsAsync();

            Assert.True(settings.IsTestingEnvironment);
            Assert.False(settings.IsRelaxedModeEnabled);
            Assert.False(settings.AllowFlexibleDates);
            Assert.False(settings.ManualMeetingOnly);
            Assert.False(settings.ImmediateLogbookUnlock);
            Assert.False(settings.RelaxAcademicDeadlines);
            Assert.False(settings.RelaxMeetingFrequency);
            Assert.False(settings.RelaxTimetableAvailability);

            // Bypasses must report false when master switch is off
            Assert.False(await policy.IsRelaxedModeActiveAsync());
            Assert.False(await policy.IsManualMeetingOnlyAsync());
            Assert.False(await policy.IsImmediateLogbookUnlockAllowedAsync());
            Assert.False(await policy.IsAcademicDeadlinesRelaxedAsync());
            Assert.False(await policy.IsMeetingFrequencyRelaxedAsync());
            Assert.False(await policy.IsTimetableAvailabilityRelaxedAsync());
        }

        [Fact]
        public async Task TestingEnvironment_WhenAdminEnablesToggles_SavesAndPersistsCorrectly()
        {
            using var context = CreateInMemoryContext();
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");

            var policy = new TestingModePolicy(context, env, config);

            var model = new TestingRelaxedModeSettingsVM
            {
                IsRelaxedModeEnabled = true,
                AllowFlexibleDates = true,
                ManualMeetingOnly = true,
                ImmediateLogbookUnlock = true,
                RelaxAcademicDeadlines = true,
                RelaxMeetingFrequency = true,
                RelaxTimetableAvailability = true
            };

            await policy.SaveSettingsAsync(model);

            // Verify settings are active
            Assert.True(await policy.IsRelaxedModeActiveAsync());
            Assert.True(await policy.IsManualMeetingOnlyAsync());
            Assert.True(await policy.IsImmediateLogbookUnlockAllowedAsync());
            Assert.True(await policy.IsAcademicDeadlinesRelaxedAsync());
            Assert.True(await policy.IsMeetingFrequencyRelaxedAsync());
            Assert.True(await policy.IsTimetableAvailabilityRelaxedAsync());

            // When master switch is turned OFF, all bypasses immediately evaluate to false
            model.IsRelaxedModeEnabled = false;
            await policy.SaveSettingsAsync(model);

            Assert.False(await policy.IsRelaxedModeActiveAsync());
            Assert.False(await policy.IsManualMeetingOnlyAsync());
            Assert.False(await policy.IsImmediateLogbookUnlockAllowedAsync());
            Assert.False(await policy.IsAcademicDeadlinesRelaxedAsync());
            Assert.False(await policy.IsMeetingFrequencyRelaxedAsync());
            Assert.False(await policy.IsTimetableAvailabilityRelaxedAsync());
        }
    }
}
