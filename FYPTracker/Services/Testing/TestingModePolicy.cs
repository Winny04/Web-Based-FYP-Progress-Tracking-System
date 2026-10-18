using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FYPTracker.Helpers;
using FYPTracker.Models;
using FYPTracker.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FYPTracker.Services.Testing
{
    public interface ITestingModePolicy
    {
        Task<TestingRelaxedModeSettingsVM> GetSettingsAsync();
        Task SaveSettingsAsync(TestingRelaxedModeSettingsVM settings);

        Task<bool> IsRelaxedModeActiveAsync();
        Task<bool> IsFlexibleDatesAllowedAsync();
        Task<bool> IsManualMeetingOnlyAsync();
        Task<bool> IsImmediateLogbookUnlockAllowedAsync();
        Task<bool> IsAcademicDeadlinesRelaxedAsync();
        Task<bool> IsMeetingFrequencyRelaxedAsync();
        Task<bool> IsTimetableAvailabilityRelaxedAsync();
    }

    public class TestingModePolicy : ITestingModePolicy
    {
        private readonly FYPTrackerContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public const string KeyRelaxedMode = "Testing_RelaxedMode";
        public const string KeyFlexibleDates = "Testing_FlexibleDates";
        public const string KeyManualMeetingOnly = "Testing_ManualMeetingOnly";
        public const string KeyImmediateLogbookUnlock = "Testing_ImmediateLogbookUnlock";
        public const string KeyRelaxDeadlines = "Testing_RelaxAcademicDeadlines";
        public const string KeyRelaxFrequency = "Testing_RelaxMeetingFrequency";
        public const string KeyRelaxTimetable = "Testing_RelaxTimetableAvailability";

        public TestingModePolicy(FYPTrackerContext context, IWebHostEnvironment env, IConfiguration config)
        {
            _context = context;
            _env = env;
            _config = config;
        }

        private bool IsGuardedTestingEnvironment()
        {
            return TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _config);
        }

        public async Task<TestingRelaxedModeSettingsVM> GetSettingsAsync()
        {
            var vm = new TestingRelaxedModeSettingsVM
            {
                IsTestingEnvironment = IsGuardedTestingEnvironment()
            };

            if (!vm.IsTestingEnvironment)
            {
                // In Development or Production, all bypasses are strictly FALSE and cannot be activated
                return vm;
            }

            try
            {
                var keys = new[]
                {
                    KeyRelaxedMode,
                    KeyFlexibleDates,
                    KeyManualMeetingOnly,
                    KeyImmediateLogbookUnlock,
                    KeyRelaxDeadlines,
                    KeyRelaxFrequency,
                    KeyRelaxTimetable
                };

                var settings = await _context.FYPSettings
                    .Where(s => keys.Contains(s.Key))
                    .ToDictionaryAsync(s => s.Key, s => s.Value);

                bool ParseBool(string key) =>
                    settings.TryGetValue(key, out var val) && bool.TryParse(val, out var b) && b;

                vm.IsRelaxedModeEnabled = ParseBool(KeyRelaxedMode);
                vm.AllowFlexibleDates = ParseBool(KeyFlexibleDates);
                vm.ManualMeetingOnly = ParseBool(KeyManualMeetingOnly);
                vm.ImmediateLogbookUnlock = ParseBool(KeyImmediateLogbookUnlock);
                vm.RelaxAcademicDeadlines = ParseBool(KeyRelaxDeadlines);
                vm.RelaxMeetingFrequency = ParseBool(KeyRelaxFrequency);
                vm.RelaxTimetableAvailability = ParseBool(KeyRelaxTimetable);

                try
                {
                    vm.ActiveDatabaseName = _context.Database.GetDbConnection().Database;
                }
                catch
                {
                    vm.ActiveDatabaseName = "FYPTracker_Test";
                }
            }
            catch
            {
                // Defaults to false if settings table is inaccessible
            }

            return vm;
        }

        public async Task SaveSettingsAsync(TestingRelaxedModeSettingsVM settings)
        {
            if (!IsGuardedTestingEnvironment())
            {
                throw new InvalidOperationException("[TESTING POLICY DENIED] Cannot save Testing Mode settings in non-testing environment or non-testing database.");
            }

            async Task UpsertSetting(string key, bool val, string label)
            {
                var entry = await _context.FYPSettings.FirstOrDefaultAsync(s => s.Key == key);
                if (entry == null)
                {
                    entry = new FYPSetting
                    {
                        Key = key,
                        Value = val.ToString().ToLowerInvariant(),
                        Label = label,
                        UpdatedDate = DateTime.Now
                    };
                    _context.FYPSettings.Add(entry);
                }
                else
                {
                    entry.Value = val.ToString().ToLowerInvariant();
                    entry.UpdatedDate = DateTime.Now;
                }
            }

            await UpsertSetting(KeyRelaxedMode, settings.IsRelaxedModeEnabled, "Master switch for Testing Relaxed Validation Mode");
            await UpsertSetting(KeyFlexibleDates, settings.AllowFlexibleDates, "Allow booking on historical and future dates");
            await UpsertSetting(KeyManualMeetingOnly, settings.ManualMeetingOnly, "Disable auto-generating recurring meetings");
            await UpsertSetting(KeyImmediateLogbookUnlock, settings.ImmediateLogbookUnlock, "Unlock logbook drafting immediately");
            await UpsertSetting(KeyRelaxDeadlines, settings.RelaxAcademicDeadlines, "Relax academic milestone deadline cutoffs");
            await UpsertSetting(KeyRelaxFrequency, settings.RelaxMeetingFrequency, "Relax one meeting per week limit");
            await UpsertSetting(KeyRelaxTimetable, settings.RelaxTimetableAvailability, "Relax supervisor timetable requirement");

            await _context.SaveChangesAsync();
        }

        public async Task<bool> IsRelaxedModeActiveAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled;
        }

        public async Task<bool> IsFlexibleDatesAllowedAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            // In Testing, historical booking is also enabled by base flexible testing booking guard.
            // When Relaxed Mode is ON, explicit flexible dates option is honored.
            return settings.IsRelaxedModeEnabled ? settings.AllowFlexibleDates : true;
        }

        public async Task<bool> IsManualMeetingOnlyAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled && settings.ManualMeetingOnly;
        }

        public async Task<bool> IsImmediateLogbookUnlockAllowedAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled && settings.ImmediateLogbookUnlock;
        }

        public async Task<bool> IsAcademicDeadlinesRelaxedAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled && settings.RelaxAcademicDeadlines;
        }

        public async Task<bool> IsMeetingFrequencyRelaxedAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled && settings.RelaxMeetingFrequency;
        }

        public async Task<bool> IsTimetableAvailabilityRelaxedAsync()
        {
            if (!IsGuardedTestingEnvironment()) return false;
            var settings = await GetSettingsAsync();
            return settings.IsRelaxedModeEnabled && settings.RelaxTimetableAvailability;
        }
    }
}
