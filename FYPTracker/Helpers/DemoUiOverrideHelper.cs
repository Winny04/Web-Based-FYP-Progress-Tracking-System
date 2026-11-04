using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FYPTracker.Helpers
{
    public static class DemoUiOverrideHelper
    {
        public const string ConfigurationSectionName = "DemoUiOverrides";
        public const string HideStage2ConfigKey = "HideStage2WhileIneligibleProjectIds";

        /// <summary>
        /// Determines whether Stage 2 UI (accordion, deadlines, progress breakdown) should be hidden
        /// for a specific project while it is academically ineligible for Stage 2.
        /// Strict safety requirements:
        /// 1. Must run strictly in Development environment ("Development"). In Testing, Staging, or Production, always returns false.
        /// 2. Must only apply if the project's Stage 2 eligibility is false (i.e. Stage 1 not passed).
        /// 3. The project Id must be explicitly configured in the non-secret DemoUiOverrides:HideStage2WhileIneligibleProjectIds list in appsettings.Development.json.
        /// 4. If any parameter is null or the project ID is not in the list, returns false (original behavior preserved).
        /// </summary>
        public static bool ShouldHideStage2ForProject(
            int projectId,
            bool isStage2Eligible,
            IWebHostEnvironment? env,
            IConfiguration? config)
        {
            // If already academically eligible for Stage 2, never hide Stage 2.
            if (isStage2Eligible)
            {
                return false;
            }

            // Only active in Development environment.
            if (env == null || !env.IsEnvironment("Development"))
            {
                return false;
            }

            if (config == null)
            {
                return false;
            }

            var section = config.GetSection($"{ConfigurationSectionName}:{HideStage2ConfigKey}");
            var configuredIds = section.Get<List<int>>();
            if (configuredIds == null || configuredIds.Count == 0)
            {
                return false;
            }

            return configuredIds.Contains(projectId);
        }

        public const string EnforceIafReferencesConfigKey = "EnforceIafReferencesValidationProjectIds";

        /// <summary>
        /// Determines whether IAF References Listing validation should be enforced for a project.
        /// Strict safety requirements:
        /// 1. Must run strictly in Development or Testing environment with explicit configuration.
        /// 2. Must only apply to projects explicitly listed in DemoUiOverrides:EnforceIafReferencesValidationProjectIds.
        /// 3. Returns false for all other projects, ensuring standard behavior is strictly preserved.
        /// </summary>
        public static bool ShouldEnforceIafReferencesValidationForProject(
            int projectId,
            IWebHostEnvironment? env,
            IConfiguration? config)
        {
            if (env == null || (!env.IsEnvironment("Development") && !env.IsEnvironment("Testing")))
            {
                return false;
            }

            if (config == null)
            {
                return false;
            }

            var section = config.GetSection($"{ConfigurationSectionName}:{EnforceIafReferencesConfigKey}");
            var configuredIds = section.Get<List<int>>();
            if (configuredIds == null || configuredIds.Count == 0)
            {
                return false;
            }

            return configuredIds.Contains(projectId);
        }

        /// <summary>
        /// Validates references listing for the 2021-2026 academic requirement.
        /// Returns an error message if invalid, or string.Empty if valid.
        /// Requirements:
        /// 1. At least one reference.
        /// 2. Each reference contains author/organization, year, and title.
        /// 3. Years must be between 2021 and 2026.
        /// </summary>
        public static string ValidateIafReferences(string? rawReferences)
        {
            if (string.IsNullOrWhiteSpace(rawReferences))
            {
                return "At least one academic reference is required.";
            }

            // Split into individual reference entries by newlines or numbered items
            var lines = rawReferences
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
            {
                return "At least one academic reference is required.";
            }

            // Year regex: checks for a 4-digit year like (2024), 2024, etc.
            var yearRegex = new System.Text.RegularExpressions.Regex(@"(?:^|\D)(20\d{2})(?:\D|$)", System.Text.RegularExpressions.RegexOptions.Compiled);

            int validReferenceCount = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                // If line is just a number or header like "References:", skip it
                if (line.Equals("References", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("References:", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("Reference:", StringComparison.OrdinalIgnoreCase) ||
                    line.Length < 10)
                {
                    continue;
                }

                // Check for year
                var match = yearRegex.Match(line);
                if (!match.Success)
                {
                    return $"Reference \"{(line.Length > 40 ? line.Substring(0, 37) + "..." : line)}\" is missing a publication year.";
                }

                if (!int.TryParse(match.Groups[1].Value, out int year))
                {
                    return $"Reference \"{(line.Length > 40 ? line.Substring(0, 37) + "..." : line)}\" has an unparseable publication year.";
                }

                if (year < 2021 || year > 2026)
                {
                    return $"Publication year {year} is outside the allowed 2021–2026 range. Academic references must be published between 2021 and 2026.";
                }

                // Remove year and check for identifiable author/title (at least two distinct segments or minimum length)
                string stripped = yearRegex.Replace(line, " ").Trim();
                var words = stripped.Split(new[] { ' ', '\t', ',', '.', ';', ':', '(', ')', '[', ']', '"', '“', '”' }, StringSplitOptions.RemoveEmptyEntries);

                if (words.Length < 3 || stripped.Length < 15)
                {
                    return "Each reference must include identifiable author/organization and title details.";
                }

                validReferenceCount++;
            }

            if (validReferenceCount == 0)
            {
                return "At least one complete academic reference with author, title, and publication year (2021–2026) is required.";
            }

            return string.Empty;
        }
    }
}
