using System;
using System.Collections.Generic;
using System.IO;
using FYPTracker.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class TestingStorageProviderTests
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
        public void DevelopmentEnvironment_UsesDefaultDirectories()
        {
            var env = CreateEnv("Development");
            var config = CreateConfig("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Development;Trusted_Connection=True;");

            string contentRoot = Path.Combine(Path.GetTempPath(), "DevRoot_" + Guid.NewGuid());
            try
            {
                bool isActive = TestingStorageProvider.IsTestingStorageActive(env, config);
                string snapshotsDir = TestingStorageProvider.GetMeetingLogSnapshotsDirectory(contentRoot, env, config);
                string relativePrefix = TestingStorageProvider.GetMeetingLogRelativePrefix(env, config);
                string submissionsDir = TestingStorageProvider.GetSubmissionsDirectory(contentRoot, env, config);
                string submissionsUrl = TestingStorageProvider.GetSubmissionsUrlPrefix(env, config);

                Assert.False(isActive);
                Assert.EndsWith(Path.Combine("App_Data", "MeetingLogSnapshots"), snapshotsDir);
                Assert.Equal("App_Data/MeetingLogSnapshots/", relativePrefix);
                Assert.EndsWith(Path.Combine("wwwroot", "uploads", "submissions"), submissionsDir);
                Assert.Equal("/uploads/submissions/", submissionsUrl);
            }
            finally
            {
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
            }
        }

        [Fact]
        public void TestingEnvironment_WithFYPTrackerTest_UsesIsolatedTestingDirectories()
        {
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test;Trusted_Connection=True;");

            string contentRoot = Path.Combine(Path.GetTempPath(), "TestRoot_" + Guid.NewGuid());
            try
            {
                bool isActive = TestingStorageProvider.IsTestingStorageActive(env, config);
                string snapshotsDir = TestingStorageProvider.GetMeetingLogSnapshotsDirectory(contentRoot, env, config);
                string relativePrefix = TestingStorageProvider.GetMeetingLogRelativePrefix(env, config);
                string submissionsDir = TestingStorageProvider.GetSubmissionsDirectory(contentRoot, env, config);
                string submissionsUrl = TestingStorageProvider.GetSubmissionsUrlPrefix(env, config);

                Assert.True(isActive);
                Assert.EndsWith(Path.Combine("App_Data", "MeetingLogSnapshots", "Testing"), snapshotsDir);
                Assert.Equal("App_Data/MeetingLogSnapshots/Testing/", relativePrefix);
                Assert.EndsWith(Path.Combine("wwwroot", "uploads", "testing", "submissions"), submissionsDir);
                Assert.Equal("/uploads/testing/submissions/", submissionsUrl);
            }
            finally
            {
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
            }
        }

        [Fact]
        public void TestingEnvironment_WithWrongDatabase_StrictlyThrowsAndDeniesFileOperations()
        {
            // Critical Fail-Closed Rule:
            // Testing + FYPTracker_Development must NOT fall back to development paths.
            // It MUST throw InvalidOperationException and completely deny access.
            var env = CreateEnv("Testing");
            var config = CreateConfig("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Development;Trusted_Connection=True;");
            string contentRoot = Path.Combine(Path.GetTempPath(), "TestRootFailClosed_" + Guid.NewGuid());

            try
            {
                // AssertFileWriteAllowed must throw
                Assert.Throws<InvalidOperationException>(() => TestingStorageProvider.AssertFileWriteAllowed(env, config));

                // Resolving write directories must fail closed by throwing InvalidOperationException
                Assert.Throws<InvalidOperationException>(() => TestingStorageProvider.GetMeetingLogSnapshotsDirectory(contentRoot, env, config));
                Assert.Throws<InvalidOperationException>(() => TestingStorageProvider.GetMeetingLogRelativePrefix(env, config));
                Assert.Throws<InvalidOperationException>(() => TestingStorageProvider.GetSubmissionsDirectory(contentRoot, env, config));
                Assert.Throws<InvalidOperationException>(() => TestingStorageProvider.GetSubmissionsUrlPrefix(env, config));
            }
            finally
            {
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
            }
        }

        [Fact]
        public void TestingEnvironment_PhysicalWrite_DoesNotTouchOrOverwriteDevelopmentFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "PhysicalTestRoot_" + Guid.NewGuid());
            try
            {
                // 1. Simulate existing Development file in development folder
                string devSubmissionsDir = Path.Combine(root, "wwwroot", "uploads", "submissions");
                Directory.CreateDirectory(devSubmissionsDir);
                string devFile = Path.Combine(devSubmissionsDir, "important_dev_file.pdf");
                File.WriteAllText(devFile, "ORIGINAL DEVELOPMENT DATA");

                // 2. Perform write operation in Testing environment
                var env = CreateEnv("Testing");
                var config = CreateConfig("Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test;Trusted_Connection=True;");

                string testingSubmissionsDir = TestingStorageProvider.GetSubmissionsDirectory(root, env, config);
                string testFile = Path.Combine(testingSubmissionsDir, "important_dev_file.pdf"); // Same filename!
                File.WriteAllText(testFile, "TESTING ISOLATED DATA");

                // 3. Verify Isolation: Development file must remain completely untouched!
                Assert.NotEqual(devSubmissionsDir, testingSubmissionsDir);
                Assert.Equal("ORIGINAL DEVELOPMENT DATA", File.ReadAllText(devFile));
                Assert.Equal("TESTING ISOLATED DATA", File.ReadAllText(testFile));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
