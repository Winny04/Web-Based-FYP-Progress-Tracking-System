using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using FYPTracker.Models;

namespace FYPTracker.Services.Defense
{
    /// <summary>
    /// Safety gate restricting activation of IAF Defense Alternative Time Request (Phase B3.5)
    /// to the verified Testing environment and database under an explicit feature flag.
    /// </summary>
    public static class IAFAlternativeTimeFeatureGate
    {
        public const string FeatureFlagKey = "FeatureFlags:IAFAlternativeTimeRequestEnabled";

        public static bool IsFeatureEnabled(
            IConfiguration? configuration,
            IWebHostEnvironment? environment,
            FYPTrackerContext? context = null)
        {
            if (configuration == null)
            {
                return false;
            }

            // 1. Explicit feature flag must be enabled
            if (!configuration.GetValue<bool>(FeatureFlagKey, false))
            {
                return false;
            }

            // 2. Environment must be strictly 'Testing'
            if (environment != null && !environment.IsEnvironment("Testing"))
            {
                return false;
            }

            // 3. Database connection string must point strictly to Winny\SQLEXPRESS and FYPTracker_Test
            var connStr = configuration.GetConnectionString("Entities");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return false;
            }

            try
            {
                var sqlBuilder = new SqlConnectionStringBuilder(connStr);
                string server = sqlBuilder.DataSource;
                string db = sqlBuilder.InitialCatalog;

                if (!string.Equals(server, @"Winny\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(db, "FYPTracker_Test", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            // 4. Testing requires Offline SMTP
            if (!configuration.GetValue<bool>("SmtpSettings:Offline", false))
            {
                return false;
            }

            return true;
        }
    }
}
