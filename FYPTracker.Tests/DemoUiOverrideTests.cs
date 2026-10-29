using System.Collections.Generic;
using FYPTracker.Helpers;
using FYPTracker.Models;
using FYPTracker.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FYPTracker.Tests
{
    public class DemoUiOverrideTests
    {
        private readonly IWebHostEnvironment _devEnv;
        private readonly IWebHostEnvironment _testingEnv;
        private readonly IWebHostEnvironment _prodEnv;

        public DemoUiOverrideTests()
        {
            _devEnv = new TestWebHostEnvironment { EnvironmentName = "Development" };
            _testingEnv = new TestWebHostEnvironment { EnvironmentName = "Testing" };
            _prodEnv = new TestWebHostEnvironment { EnvironmentName = "Production" };
        }

        private IConfiguration CreateConfig(int[] projectIds)
        {
            var inMemorySettings = new Dictionary<string, string?>();
            for (int i = 0; i < projectIds.Length; i++)
            {
                inMemorySettings[$"DemoUiOverrides:HideStage2WhileIneligibleProjectIds:{i}"] = projectIds[i].ToString();
            }

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        [Fact]
        public void Project53_InDevelopment_WhenIneligible_HidesStage2()
        {
            var config = CreateConfig(new[] { 53 });
            bool shouldHide = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 53,
                isStage2Eligible: false,
                env: _devEnv,
                config: config);

            Assert.True(shouldHide);
        }

        [Fact]
        public void Project53_InDevelopment_WhenEligible_DoesNotHideStage2()
        {
            var config = CreateConfig(new[] { 53 });
            bool shouldHide = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 53,
                isStage2Eligible: true,
                env: _devEnv,
                config: config);

            Assert.False(shouldHide);
        }

        [Fact]
        public void OtherProject_InDevelopment_WhenIneligible_DoesNotHideStage2()
        {
            var config = CreateConfig(new[] { 53 });
            bool shouldHide51 = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 51,
                isStage2Eligible: false,
                env: _devEnv,
                config: config);

            bool shouldHide52 = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 52,
                isStage2Eligible: false,
                env: _devEnv,
                config: config);

            Assert.False(shouldHide51);
            Assert.False(shouldHide52);
        }

        [Fact]
        public void Project53_OutsideDevelopment_EvenIfInConfig_DoesNotHideStage2()
        {
            var config = CreateConfig(new[] { 53 });

            bool hideInTesting = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 53,
                isStage2Eligible: false,
                env: _testingEnv,
                config: config);

            bool hideInProd = DemoUiOverrideHelper.ShouldHideStage2ForProject(
                projectId: 53,
                isStage2Eligible: false,
                env: _prodEnv,
                config: config);

            Assert.False(hideInTesting);
            Assert.False(hideInProd);
        }

        [Fact]
        public void BackendStage2AcademicEligibility_RemainsStrictlyEnforced()
        {
            var proj53Ineligible = new FYPProject
            {
                Id = 53,
                Stage1Grade = null,
                Milestones = new List<FYPMilestone>
                {
                    new FYPMilestone { Id = 321, Title = "IAF", Stage = 1, IsRequired = true },
                    new FYPMilestone { Id = 324, Title = "Stage 1 Final Submission (DPP)", Stage = 1, IsRequired = true }
                }
            };

            var eligibility = GradingHelper.CheckStage2AcademicEligibility(proj53Ineligible, proj53Ineligible.Milestones);
            Assert.False(eligibility.IsEligible);
            Assert.True(eligibility.IsPendingGrading);

            // Once Pass is achieved
            proj53Ineligible.Stage1Grade = "Pass";
            var passEligibility = GradingHelper.CheckStage2AcademicEligibility(proj53Ineligible, proj53Ineligible.Milestones);
            Assert.True(passEligibility.IsEligible);
        }
    }
}
