using FYPTracker.Models;
using FYPTracker.Utils;
using Xunit;
using System.Collections.Generic;

namespace FYPTracker.Tests
{
    public class Stage1GradingPrerequisiteTests
    {
        [Fact]
        public void GetStage1FinalMilestone_ShouldMatchStage1FinalCode_WhenPresent()
        {
            var milestones = new List<FYPMilestone>
            {
                new FYPMilestone { Id = 1, Title = "IAF", MilestoneCode = "IAF", Status = "AdminApproved" },
                new FYPMilestone { Id = 2, Title = "Ethics Form", MilestoneCode = "ETHICS", Status = "Pending" },
                new FYPMilestone { Id = 3, Title = "Turnitin Checking - DPP", MilestoneCode = "TURNITIN_DPP", Status = "Pending" },
                new FYPMilestone { Id = 4, Title = "FYP 1: Stage 1 Final Submission (DPP)", MilestoneCode = "STAGE1_FINAL", Status = "Approved" }
            };

            var target = GradingHelper.GetStage1FinalMilestone(milestones);

            Assert.NotNull(target);
            Assert.Equal(4, target!.Id);
            Assert.Equal("STAGE1_FINAL", target.MilestoneCode);
        }

        [Fact]
        public void GetStage1FinalMilestone_ShouldNotMatchTurnitinChecking_EvenWhenDppInTitle()
        {
            // Case where MilestoneCode might be missing/null (legacy data)
            var milestones = new List<FYPMilestone>
            {
                new FYPMilestone { Id = 1, Title = "IAF", Status = "Completed" },
                new FYPMilestone { Id = 3, Title = "Turnitin Checking - DPP", Status = "Pending" },
                new FYPMilestone { Id = 4, Title = "FYP 1: Stage 1 Final Submission (DPP)", Status = "Approved" }
            };

            var target = GradingHelper.GetStage1FinalMilestone(milestones);

            Assert.NotNull(target);
            Assert.Equal(4, target!.Id);
            Assert.NotEqual(3, target.Id);
        }

        [Fact]
        public void Stage1Grading_DppPending_ShouldBeBlocked()
        {
            var milestone = new FYPMilestone
            {
                Id = 4,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Pending"
            };

            bool allowed = GradingHelper.IsStage1DppApproved(milestone);
            Assert.False(allowed);
        }

        [Fact]
        public void Stage1Grading_DppRejected_ShouldBeBlocked()
        {
            var milestone = new FYPMilestone
            {
                Id = 4,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Rejected"
            };

            bool allowed = GradingHelper.IsStage1DppApproved(milestone);
            Assert.False(allowed);
        }

        [Fact]
        public void Stage1Grading_DppApproved_ShouldBeAllowed()
        {
            var milestone = new FYPMilestone
            {
                Id = 4,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved"
            };

            bool allowed = GradingHelper.IsStage1DppApproved(milestone);
            Assert.True(allowed);
        }

        [Fact]
        public void Stage1Grading_DppCompleted_ShouldBeAllowed()
        {
            var milestone = new FYPMilestone
            {
                Id = 4,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Completed"
            };

            bool allowed = GradingHelper.IsStage1DppApproved(milestone);
            Assert.True(allowed);
        }

        [Fact]
        public void Stage1Rubric_ScoringCriteriaAndPassThreshold_Unchanged()
        {
            // Supervisor full score 4 across 6 criteria (10+20+30+20+10+10 = 100)
            string fullRubric = "{\"Crit1\":4,\"Crit2\":4,\"Crit3\":4,\"Crit4\":4,\"Crit5\":4,\"Crit6\":4}";
            double supScore = GradingHelper.CalculateStage1Score(fullRubric, "Supervisor");
            Assert.Equal(100.0, supScore);

            // Examiner full score
            double exScore = GradingHelper.CalculateStage1Score(fullRubric, "Examiner");
            Assert.Equal(100.0, exScore);

            // Joint percentage
            double? joint = GradingHelper.CalculateJointFinalPercentage(supScore, exScore);
            Assert.Equal(100.0, joint);
            Assert.True(joint.Value >= 40.0);

            // Borderline 40% threshold test
            // 40% on all criteria -> (1.6/4)*100 = 40.0
            string passRubric = "{\"Crit1\":1.6,\"Crit2\":1.6,\"Crit3\":1.6,\"Crit4\":1.6,\"Crit5\":1.6,\"Crit6\":1.6}";
            double passSupScore = GradingHelper.CalculateStage1Score(passRubric, "Supervisor");
            double? jointPass = GradingHelper.CalculateJointFinalPercentage(passSupScore, passSupScore);
            Assert.NotNull(jointPass);
            Assert.True(jointPass.Value >= 40.0);
            Assert.Equal("Average", GradingHelper.DetermineFinalGradeBand(jointPass.Value));

            // Below 40% -> Fail
            string failRubric = "{\"Crit1\":1.0,\"Crit2\":1.0,\"Crit3\":1.0,\"Crit4\":1.0,\"Crit5\":1.0,\"Crit6\":1.0}";
            double failSupScore = GradingHelper.CalculateStage1Score(failRubric, "Supervisor");
            double? jointFail = GradingHelper.CalculateJointFinalPercentage(failSupScore, failSupScore);
            Assert.NotNull(jointFail);
            Assert.True(jointFail.Value < 40.0);
            Assert.Equal("Fail", GradingHelper.DetermineFinalGradeBand(jointFail.Value));
        }

        [Theory]
        [InlineData("Pass", "Completed", true)]
        [InlineData("Pass", "Approved", true)]
        [InlineData("Pass", "Draft", true)]
        [InlineData(null, "Completed", true)]
        [InlineData(null, "Approved", true)]
        [InlineData(null, "Submitted", true)]
        [InlineData(null, "UnderReview", true)]
        [InlineData(null, "Draft", false)]
        [InlineData(null, "Pending", false)]
        [InlineData(null, "Rejected", false)]
        [InlineData("Fail", "Rejected", false)]
        public void IsStage1ProposalLocked_ShouldEnforceCorrectLockBehavior(string? stage1Grade, string milestoneStatus, bool expectedLocked)
        {
            var milestone = new FYPMilestone
            {
                Id = 4,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = milestoneStatus
            };

            bool isLocked = GradingHelper.IsStage1ProposalLocked(milestone, stage1Grade);
            Assert.Equal(expectedLocked, isLocked);
        }

        [Fact]
        public void GetAuthorizedStudentFeedback_ShouldRetrieveSupervisorAndEvaluationFeedback_WhenAvailable()
        {
            var project = new FYPProject
            {
                Id = 1,
                StudentId = 3,
                Title = "Test project",
                Stage1Grade = "Pass",
                Milestones = new List<FYPMilestone>
                {
                    new FYPMilestone
                    {
                        Id = 1,
                        Title = "IAF",
                        Stage = 1,
                        SupervisorFeedback = "Initial form approved."
                    },
                    new FYPMilestone
                    {
                        Id = 4,
                        Title = "FYP 1: Stage 1 Final Submission (DPP)",
                        MilestoneCode = "STAGE1_FINAL",
                        Stage = 1,
                        SupervisorFeedback = "Proposal reviewed and approved.",
                        SupervisorMarksheetData = "{\"Strengths\":\"Good literature review\",\"Improvements\":\"Refine methodology\",\"Comments\":\"Keep it up\",\"IsDraft\":false}"
                    }
                }
            };

            var feedbacks = GradingHelper.GetAuthorizedStudentFeedback(project);

            Assert.NotNull(feedbacks);
            Assert.Equal(3, feedbacks.Count);
            Assert.Contains(feedbacks, f => f.SourceType == "Supervisor Submission Review" && f.FeedbackContent == "Proposal reviewed and approved.");
            Assert.Contains(feedbacks, f => f.SourceType == "Supervisor Evaluation" && f.FeedbackContent.Contains("Good literature review"));
            Assert.Contains(feedbacks, f => f.SourceType == "Supervisor Submission Review" && f.FeedbackContent == "Initial form approved.");
        }

        [Fact]
        public void GetAuthorizedStudentFeedback_ShouldReturnEmpty_WhenNoFeedbackExists()
        {
            var project = new FYPProject
            {
                Id = 2,
                StudentId = 4,
                Milestones = new List<FYPMilestone>
                {
                    new FYPMilestone { Id = 10, Title = "Step 1", Stage = 2 }
                }
            };

            var feedbacks = GradingHelper.GetAuthorizedStudentFeedback(project);

            Assert.NotNull(feedbacks);
            Assert.Empty(feedbacks);
        }
    }
}
