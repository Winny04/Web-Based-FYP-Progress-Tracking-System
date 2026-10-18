using System;
using System.Collections.Generic;
using FYPTracker.Controllers;
using FYPTracker.Helpers;
using FYPTracker.Services.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class TestingClockTests
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
        public void RealSystemClock_AlwaysReturnsSystemDateTime()
        {
            var clock = new RealSystemClock();

            var before = DateTime.Now;
            var now = clock.Now;
            var after = DateTime.Now;

            Assert.InRange(now, before, after);
            Assert.False(clock.IsSimulated);
            Assert.Equal(DateTime.Today, clock.Today);
        }

        [Fact]
        public void TestingSystemClock_WhenPermitted_AllowsSettingSimulatedTime()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");
            var clock = new TestingSystemClock(env, config);

            var targetDate = new DateTime(2026, 3, 15, 10, 30, 0);
            clock.SetTime(targetDate);

            Assert.True(clock.IsSimulated);
            Assert.Equal(targetDate, clock.Now);
            Assert.Equal(targetDate.Date, clock.Today);

            // Clean up by resetting
            clock.Reset();
            Assert.False(clock.IsSimulated);
        }

        [Fact]
        public void TestingSystemClock_WhenPermitted_AllowsAdvancingTime()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");
            var clock = new TestingSystemClock(env, config);

            var targetDate = new DateTime(2026, 4, 1, 9, 0, 0);
            clock.SetTime(targetDate);
            clock.Advance(TimeSpan.FromDays(7));

            Assert.Equal(new DateTime(2026, 4, 8, 9, 0, 0), clock.Now);

            clock.Reset();
        }

        [Fact]
        public void TestingSystemClock_InDevelopment_RejectsSetTimeAndFallsBackToRealTime()
        {
            var env = CreateEnv("Development");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;");
            var clock = new TestingSystemClock(env, config);

            Assert.Throws<InvalidOperationException>(() => clock.SetTime(new DateTime(2026, 5, 1)));
            Assert.False(clock.IsSimulated);

            var before = DateTime.Now;
            var now = clock.Now;
            var after = DateTime.Now;
            Assert.InRange(now, before, after);
        }

        [Fact]
        public void TestingClockController_WhenNotPermitted_ReturnsForbid()
        {
            var env = CreateEnv("Development");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Development;Trusted_Connection=True;");
            var clock = new RealSystemClock();
            var controller = new TestingClockController(clock, env, config);

            var result = controller.SetSimulatedTime(new DateTime(2026, 5, 1), "10:00", null);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public void TestingClockController_WhenPermitted_SetsTimeAndRedirects()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=Boon\\SQLEXPRESS;Database=FYPTracker_Test;Trusted_Connection=True;");
            var clock = new TestingSystemClock(env, config);
            var controller = new TestingClockController(clock, env, config)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider())
            };

            var targetDate = new DateTime(2026, 6, 20);
            var result = controller.SetSimulatedTime(targetDate, "14:30", null);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.True(clock.IsSimulated);
            Assert.Equal(new DateTime(2026, 6, 20, 14, 30, 0), clock.Now);

            clock.Reset();
        }

        private class TestTempDataProvider : ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }
    }
}
