using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FYPTracker.Helpers
{
    /// <summary>
    /// Provides dedicated storage paths and isolation for Testing-generated uploads,
    /// logbook snapshots, and PDFs to ensure they never mix with or overwrite Development files.
    /// 
    /// Strict Fail-Closed Isolation Rules:
    /// 1. Development + FYPTracker_Development: uses original development paths.
    /// 2. Testing + FYPTracker_Test: uses isolated Testing storage paths.
    /// 3. Testing + FYPTracker_Development or any unexpected database: THROW / DENY file-writing operations.
    ///    Never falls back to shared Development paths when environment is Testing.
    /// </summary>
    public static class TestingStorageProvider
    {
        public const string TestingUploadsSubdir = "uploads/testing";
        public const string TestingLogSnapshotsSubdir = "App_Data/MeetingLogSnapshots/Testing";

        /// <summary>
        /// Validates file-write permissions for the current environment and database connection.
        /// Throws InvalidOperationException if running in Testing without the FYPTracker_Test database.
        /// </summary>
        public static void AssertFileWriteAllowed(IWebHostEnvironment? env, IConfiguration? config, DbContext? dbContext = null)
        {
            if (env == null || config == null)
            {
                throw new InvalidOperationException("[STORAGE GUARD] Missing hosting environment or configuration.");
            }

            if (env.IsEnvironment("Testing"))
            {
                if (!TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config))
                {
                    throw new InvalidOperationException(
                        "[STORAGE GUARD BLOCKED] Application is running in 'Testing' environment but the configured database " +
                        $"is not '{TestingEnvironmentGuard.RequiredTestingDatabase}'. File writes and modifications are strictly DENIED to protect non-testing data.");
                }

                // If a DbContext is provided, also verify the actual connected database name
                if (dbContext != null)
                {
                    try
                    {
                        var actualDbName = dbContext.Database.GetDbConnection().Database;
                        if (!string.IsNullOrWhiteSpace(actualDbName) &&
                            !string.Equals(actualDbName, TestingEnvironmentGuard.RequiredTestingDatabase, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"[STORAGE GUARD BLOCKED] Actual connected database '{actualDbName}' does not match required testing database '{TestingEnvironmentGuard.RequiredTestingDatabase}'. File writes DENIED.");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("[STORAGE GUARD BLOCKED] Failed to verify live database identity.", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Returns true only if the running environment is Testing and connected to FYPTracker_Test.
        /// </summary>
        public static bool IsTestingStorageActive(IWebHostEnvironment? env, IConfiguration? config)
        {
            return TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(env, config);
        }

        /// <summary>
        /// Resolves the physical base directory for meeting log snapshots (PDFs and JSON templates).
        /// Fails closed with an exception if running in Testing with an invalid target database.
        /// In Development: {contentRoot}/App_Data/MeetingLogSnapshots
        /// In Testing:     {contentRoot}/App_Data/MeetingLogSnapshots/Testing
        /// </summary>
        public static string GetMeetingLogSnapshotsDirectory(string contentRootPath, IWebHostEnvironment? env, IConfiguration? config)
        {
            AssertFileWriteAllowed(env, config);

            string relative = IsTestingStorageActive(env, config)
                ? TestingLogSnapshotsSubdir
                : "App_Data/MeetingLogSnapshots";

            string full = Path.Combine(contentRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
            }
            return full;
        }

        /// <summary>
        /// Resolves the relative database storage path prefix for a meeting log snapshot.
        /// </summary>
        public static string GetMeetingLogRelativePrefix(IWebHostEnvironment? env, IConfiguration? config)
        {
            AssertFileWriteAllowed(env, config);

            return IsTestingStorageActive(env, config)
                ? "App_Data/MeetingLogSnapshots/Testing/"
                : "App_Data/MeetingLogSnapshots/";
        }

        /// <summary>
        /// Resolves the physical path for student and supervisor submission uploads.
        /// Fails closed with an exception if running in Testing with an invalid target database.
        /// In Development: {contentRoot}/wwwroot/uploads/submissions
        /// In Testing:     {contentRoot}/wwwroot/uploads/testing/submissions
        /// </summary>
        public static string GetSubmissionsDirectory(string contentRootPath, IWebHostEnvironment? env, IConfiguration? config)
        {
            AssertFileWriteAllowed(env, config);

            string relative = IsTestingStorageActive(env, config)
                ? Path.Combine("wwwroot", "uploads", "testing", "submissions")
                : Path.Combine("wwwroot", "uploads", "submissions");

            string full = Path.Combine(contentRootPath, relative);
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
            }
            return full;
        }

        /// <summary>
        /// Resolves the relative URL prefix for saved submission files.
        /// </summary>
        public static string GetSubmissionsUrlPrefix(IWebHostEnvironment? env, IConfiguration? config)
        {
            AssertFileWriteAllowed(env, config);

            return IsTestingStorageActive(env, config)
                ? "/uploads/testing/submissions/"
                : "/uploads/submissions/";
        }
    }
}
