using System;
using System.Collections.Generic;
using FYPTracker.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace FYPTracker.Tests
{
    public class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "FYPTracker";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    public class FlexibleBookingPolicyTests
    {
        private IWebHostEnvironment CreateEnv(string envName)
        {
            return new TestWebHostEnvironment { EnvironmentName = envName };
        }

        private IConfiguration CreateConfigWithConnectionString(string connStr)
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
        public void IsFlexibleTestingBookingAllowed_WhenTestingAndFYPTracker_Test_ReturnsTrue()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfigWithConnectionString("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.True(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenDevelopment_ReturnsFalse()
        {
            var env = CreateEnv("Development");
            var config = CreateConfigWithConnectionString("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Development;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenTestingWithDevelopmentDatabase_ReturnsFalse()
        {
            var env = CreateEnv("Testing");
            // Even if environment is Testing, if InitialCatalog is FYPTracker_Development, must return false!
            var config = CreateConfigWithConnectionString("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Development;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenProduction_ReturnsFalse()
        {
            var env = CreateEnv("Production");
            var config = CreateConfigWithConnectionString("Server=ProductionServer;Database=FYPTracker_Production;Trusted_Connection=True;");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenConnectionStringMalformed_ReturnsFalse()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfigWithConnectionString("InvalidConnectionString;;;");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenDatabaseNameIsSubstringOnly_ReturnsFalse()
        {
            var env = CreateEnv("Testing");
            // E.g. FYPTracker_Test_Backup or Fake_FYPTracker_Test should not pass
            var config = CreateConfigWithConnectionString("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test_Backup;Trusted_Connection=True;");

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }

        [Fact]
        public void IsFlexibleTestingBookingAllowed_WhenEntitiesEmpty_ReturnsFalse()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfigWithConnectionString(string.Empty);

            var result = TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);

            Assert.False(result);
        }
    }
}
