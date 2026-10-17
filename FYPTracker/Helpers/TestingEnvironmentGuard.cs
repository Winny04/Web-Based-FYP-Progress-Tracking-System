using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FYPTracker.Helpers
{
    public static class TestingEnvironmentGuard
    {
        public const string RequiredTestingDatabase = "FYPTracker_Test";

        /// <summary>
        /// Strictly validates whether flexible meeting booking dates (including historical dates)
        /// are permitted.
        /// Requirements:
        /// 1. Environment must be exactly "Testing".
        /// 2. Connection string "Entities" must parse via SqlConnectionStringBuilder and its InitialCatalog must be exactly "FYPTracker_Test".
        /// Never permitted in Development or Production.
        /// </summary>
        public static bool IsFlexibleTestingBookingAllowed(IWebHostEnvironment? env, IConfiguration? config)
        {
            if (env == null || config == null)
            {
                return false;
            }

            if (!env.IsEnvironment("Testing"))
            {
                return false;
            }

            var connStr = config.GetConnectionString("Entities");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return false;
            }

            try
            {
                var builder = new SqlConnectionStringBuilder(connStr);
                return string.Equals(builder.InitialCatalog, RequiredTestingDatabase, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
