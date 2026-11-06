using FYPTracker.Models;
using FYPTracker.Utils;
using FYPTracker.Controllers;
using Xunit;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

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
            Assert.NotNull(joint);
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

        [Fact]
        public void Stage1Rubric_DecimalTotalScore_PreservesPrecision_52_5()
        {
            // Report Format (Crit1) = 3/4 * 10 = 7.5
            // Introduction (Crit2) = 2/4 * 20 = 10.0
            // Literature Review (Crit3) = 2/4 * 30 = 15.0
            // Methodology (Crit4) = 2/4 * 20 = 10.0
            // References (Crit5) = 2/4 * 10 = 5.0
            // Ethics (Crit6) = 2/4 * 10 = 5.0
            // Total = 7.5 + 10.0 + 15.0 + 10.0 + 5.0 + 5.0 = 52.5/100 (NOT rounded to 53)
            string rubricWithStrings = "{\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"2\",\"Crit6\":\"2\"}";
            double supScore = GradingHelper.CalculateStage1Score(rubricWithStrings, "Supervisor");
            Assert.Equal(52.5, supScore);

            double exScore = GradingHelper.CalculateStage1Score(rubricWithStrings, "Examiner");
            Assert.Equal(52.5, exScore);

            // Numeric criteria format
            string rubricWithNumbers = "{\"Crit1\":3,\"Crit2\":2,\"Crit3\":2,\"Crit4\":2,\"Crit5\":2,\"Crit6\":2}";
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(rubricWithNumbers, "Supervisor"));
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(rubricWithNumbers, "Examiner"));

            // Regression test: Even if previously saved JSON contained rounded \"Score\": \"53\", criteria recalculation must return 52.5
            string rubricWithLegacyRoundedScore = "{\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"2\",\"Crit6\":\"2\",\"Score\":\"53\",\"Total\":\"53\"}";
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(rubricWithLegacyRoundedScore, "Supervisor"));
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(rubricWithLegacyRoundedScore, "Examiner"));

            // Validation passes for 0-4 rubric range
            Assert.True(GradingHelper.ValidateStage1RubricMarks(rubricWithStrings, out string? valErr));
            Assert.Null(valErr);

            // Joint score calculation with decimal scores
            double? joint = GradingHelper.CalculateJointFinalPercentage(supScore, exScore);
            Assert.NotNull(joint);
            Assert.Equal(52.5, joint.Value);
            Assert.True(joint.Value >= 40.0);
            Assert.Equal("Average", GradingHelper.DetermineFinalGradeBand(joint.Value));
        }

        [Fact]
        public void Stage1Rubric_DirectScoreExtraction_PreservesDecimalPrecision()
        {
            // Direct Score property (e.g. from Excel or direct payload)
            string jsonScore = "{\"Score\":\"52.5\"}";
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(jsonScore, "Supervisor"));

            string jsonNumScore = "{\"Score\":52.5}";
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(jsonNumScore, "Supervisor"));

            string jsonTotal = "{\"Total\":\"52.5\"}";
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(jsonTotal, "Supervisor"));
        }

        [Fact]
        public void Stage2Rubric_DecimalTotalScore_PreservesPrecision()
        {
            // Crit1 (10): weight 3 -> 7.5
            // Crit2 (20): weight 2 -> 10.0
            // Crit3 (10): weight 2 -> 5.0
            // Crit4 (20): weight 2 -> 10.0
            // Crit5 (5): weight 1 -> 1.25
            // Crit6 (5): weight 3 -> 3.75
            // Crit7 (30): weight 2 -> 15.0
            // Total = 7.5 + 10.0 + 5.0 + 10.0 + 1.25 + 3.75 + 15.0 = 52.5 (exact decimal)
            string stage2Rubric = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"1\",\"Crit6\":\"3\",\"Crit7\":\"2\"}";
            double? supStage2 = GradingHelper.CalculateStage2Score(stage2Rubric, "Supervisor");
            Assert.NotNull(supStage2);
            Assert.Equal(52.5, supStage2.Value);

            double? exStage2 = GradingHelper.CalculateStage2Score(stage2Rubric, "Examiner");
            Assert.NotNull(exStage2);
            Assert.Equal(52.5, exStage2.Value);

            // Fractional score with .25:
            // Crit5 (5): weight 1 -> 1.25, Crit6 (5): weight 2 -> 2.50, others whole
            // Total = 7.5 + 10.0 + 5.0 + 10.0 + 1.25 + 2.50 + 15.0 = 51.25
            string stage2Frac = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"1\",\"Crit6\":\"2\",\"Crit7\":\"2\"}";
            double? fracSup = GradingHelper.CalculateStage2Score(stage2Frac, "Supervisor");
            Assert.NotNull(fracSup);
            Assert.Equal(51.25, fracSup.Value);

            // Joint Stage 2 calculation: (0.8 * 52.5 + 0.7 * 51.25) / 1.5
            // (42.0 + 35.875) / 1.5 = 77.875 / 1.5 = 51.91666... -> 51.92
            double? jointStage2 = GradingHelper.CalculateJointFinalPercentage(supStage2.Value, fracSup.Value);
            Assert.NotNull(jointStage2);
            Assert.Equal(51.92, jointStage2.Value);
            Assert.Equal("Average", GradingHelper.DetermineFinalGradeBand(jointStage2.Value));
        }

        [Fact]
        public void Stage1AndStage2_DraftAndFinalPayloads_PreserveSeparationAndPrecision()
        {
            // Stage 1 draft
            string s1Draft = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"IsDraft\":true,\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"2\",\"Crit6\":\"2\",\"Score\":\"52.5\",\"Total\":\"52.5\"}";
            Assert.True(GradingHelper.IsMarksheetDraft(s1Draft));
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(s1Draft, "Supervisor"));

            // Stage 1 final
            string s1Final = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"IsDraft\":false,\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"2\",\"Crit6\":\"2\",\"Score\":\"52.5\",\"Total\":\"52.5\"}";
            Assert.False(GradingHelper.IsMarksheetDraft(s1Final));
            Assert.Equal(52.5, GradingHelper.CalculateStage1Score(s1Final, "Supervisor"));

            // Stage 2 payload remains separate and not affected by Stage 1 fields
            string s2Payload = "{\"Version\":2,\"Scale\":\"Rubric0-4\",\"Crit1\":\"3\",\"Crit2\":\"2\",\"Crit3\":\"2\",\"Crit4\":\"2\",\"Crit5\":\"1\",\"Crit6\":\"3\",\"Crit7\":\"2\",\"Total\":\"52.5\"}";
            Assert.Equal(52.5, GradingHelper.CalculateStage2Score(s2Payload, "Supervisor"));
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

        [Fact]
        public void CanProgressToStage1Dpp_AdminApproved_WithoutDefense_ShouldReturnTrue()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "AdminApproved" };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, null);
            Assert.True(canProgress);
        }

        [Fact]
        public void CanProgressToStage1Dpp_AdminApproved_WithPassedBooking_ShouldReturnTrue()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "AdminApproved" };
            var booking = new IAFDefenseBooking
            {
                MilestoneId = 1,
                BookingStatus = "Completed",
                AcademicOutcome = "AdminApproved"
            };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, booking);
            Assert.True(canProgress);
        }

        [Fact]
        public void CanProgressToStage1Dpp_DefenseRequired_ShouldReturnFalse()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "DefenseRequired" };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, null);
            Assert.False(canProgress);
        }

        [Fact]
        public void CanProgressToStage1Dpp_DefenseScheduled_PendingOutcome_ShouldReturnFalse()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "DefenseScheduled" };
            var booking = new IAFDefenseBooking
            {
                MilestoneId = 1,
                BookingStatus = "Scheduled",
                AcademicOutcome = "Pending"
            };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, booking);
            Assert.False(canProgress);
        }

        [Fact]
        public void CanProgressToStage1Dpp_DefenseFailed_Attempt1_ShouldReturnFalse()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "ChangeOfTopicRequired" };
            var booking = new IAFDefenseBooking
            {
                MilestoneId = 1,
                AttemptNumber = 1,
                BookingStatus = "Completed",
                AcademicOutcome = "ChangeOfTopicRequired"
            };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, booking);
            Assert.False(canProgress);
        }

        [Fact]
        public void CanProgressToStage1Dpp_DefenseFailed_Attempt2_ShouldReturnFalse()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Status = "ChangeOfTopicRequired" };
            var booking = new IAFDefenseBooking
            {
                MilestoneId = 1,
                AttemptNumber = 2,
                BookingStatus = "Completed",
                AcademicOutcome = "ChangeOfTopicRequired"
            };
            bool canProgress = GradingHelper.CanProgressToStage1Dpp(iaf, booking);
            Assert.False(canProgress);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenIafDefenseScheduled_DirectPostDpp_ShouldBeRejectedServerSide()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 99,
                FullName = "Test Student",
                EmployeeId = "p99000001",
                Email = "p99000001@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 10,
                StudentId = 99,
                Title = "Test FYP Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 10,
                FYPProjectId = 10,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "DefenseScheduled",
                Stage = 1,
                IsRequired = true,
                Remarks = "JSON: {\"CurrentAttempt\":1,\"Attempts\":[{\"AttemptNumber\":1,\"Status\":\"DefenseScheduled\"}]}"
            };
            context.FYPMilestones.Add(iafMilestone);

            var dppMilestone = new FYPMilestone
            {
                Id = 11,
                FYPProjectId = 10,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);

            var booking = new IAFDefenseBooking
            {
                Id = 2,
                MilestoneId = 10,
                AttemptNumber = 1,
                AcademicSessionId = 1,
                ExaminerId = 5,
                DefenseDate = new DateTime(2026, 10, 27),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 15, 0),
                Venue = "FYP Testing Room 1",
                BookingStatus = "Scheduled",
                AcademicOutcome = "Pending"
            };
            context.IAFDefenseBookings.Add(booking);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Attempt direct crafted POST submission for DPP while IAF defense is pending
            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Bypassed Proposal Title",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            // Assert: Server-side progression guard rejects the submission
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.True(controller.TempData.ContainsKey("Error"));
            Assert.Contains("Cannot submit Stage 1 Proposal", controller.TempData["Error"]!.ToString());
            Assert.Contains("IAF Defense session is scheduled", controller.TempData["Error"]!.ToString());

            // Ensure milestone was not marked submitted or modified
            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Pending", dppAfter!.Status);
            Assert.Null(dppAfter.SubmittedDate);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenIafDefenseRequired_DirectPostDpp_ShouldBeRejectedServerSide()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 98,
                FullName = "Test Student 2",
                EmployeeId = "p98000001",
                Email = "p98000001@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 11,
                StudentId = 98,
                Title = "Test FYP Project 2",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 12,
                FYPProjectId = 11,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "DefenseRequired",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var dppMilestone = new FYPMilestone
            {
                Id = 13,
                FYPProjectId = 11,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Bypassed Proposal Title",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.True(controller.TempData.ContainsKey("Error"));
            Assert.Contains("Cannot submit Stage 1 Proposal", controller.TempData["Error"]!.ToString());
            Assert.Contains("Defense is required", controller.TempData["Error"]!.ToString());

            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Pending", dppAfter!.Status);
            Assert.Null(dppAfter.SubmittedDate);
        }

        [Fact]
        public void CanSubmitStage1FinalDpp_NoTasks_ShouldReturnBlockedWithWaitingForSupervisorMessage()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "AdminApproved", MilestoneSource = "Default" };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.False(result.Allowed);
            Assert.False(result.HasTasks);
            Assert.False(result.AllApproved);
            Assert.Equal("DPP Submission Locked — Waiting for your Supervisor to assign Stage 1 DPP progress tasks.", result.Reason);
        }

        [Theory]
        [InlineData("Draft")]
        [InlineData("Pending")]
        public void CanSubmitStage1FinalDpp_TaskIncomplete_ShouldReturnBlocked(string taskStatus)
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "AdminApproved", MilestoneSource = "Default" };
            var task = new FYPMilestone { Id = 10, Title = "Chapter 1 Review", MilestoneSource = "Supervisor", Stage = 1, Status = taskStatus };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, task, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.False(result.Allowed);
            Assert.True(result.HasTasks);
            Assert.False(result.AllApproved);
            Assert.Equal("DPP Submission Locked — Waiting for Supervisor approval of all required Stage 1 progress tasks.", result.Reason);
        }

        [Fact]
        public void CanSubmitStage1FinalDpp_TaskAwaitingSupervisorReview_ShouldReturnBlocked()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "AdminApproved", MilestoneSource = "Default" };
            var task = new FYPMilestone { Id = 10, Title = "Literature Review Draft", MilestoneSource = "Supervisor", Stage = 1, Status = "PendingSupervisorReview" };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, task, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.False(result.Allowed);
            Assert.True(result.HasTasks);
            Assert.False(result.AllApproved);
            Assert.Equal("DPP Submission Locked — Waiting for Supervisor approval of all required Stage 1 progress tasks.", result.Reason);
        }

        [Theory]
        [InlineData("ReturnedForChanges")]
        [InlineData("ReturnedForEdits")]
        [InlineData("Revision")]
        [InlineData("Rejected")]
        public void CanSubmitStage1FinalDpp_TaskReturnedOrRejected_ShouldReturnBlocked(string taskStatus)
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "AdminApproved", MilestoneSource = "Default" };
            var task = new FYPMilestone { Id = 10, Title = "Methodology Flowchart", MilestoneSource = "Supervisor", Stage = 1, Status = taskStatus };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, task, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.False(result.Allowed);
            Assert.True(result.HasTasks);
            Assert.False(result.AllApproved);
            Assert.Equal("DPP Submission Locked — Waiting for Supervisor approval of all required Stage 1 progress tasks.", result.Reason);
        }

        [Fact]
        public void CanSubmitStage1FinalDpp_AllRequiredTasksApproved_ShouldReturnAllowed()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "AdminApproved", MilestoneSource = "Default" };
            var task1 = new FYPMilestone { Id = 10, Title = "Chapter 1", MilestoneSource = "Supervisor", Stage = 1, Status = "Completed" };
            var task2 = new FYPMilestone { Id = 11, Title = "Chapter 2", MilestoneSource = "Supervisor", Stage = 1, Status = "Approved" };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, task1, task2, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.True(result.Allowed);
            Assert.True(result.HasTasks);
            Assert.True(result.AllApproved);
            Assert.Empty(result.Reason);
        }

        [Fact]
        public void CanSubmitStage1FinalDpp_IafNotApproved_ShouldReturnBlockedEvenIfTasksApproved()
        {
            var iaf = new FYPMilestone { Id = 1, MilestoneCode = "IAF", Title = "IAF", Status = "Pending", MilestoneSource = "Default" };
            var task = new FYPMilestone { Id = 10, Title = "Chapter 1", MilestoneSource = "Supervisor", Stage = 1, Status = "Completed" };
            var dpp = new FYPMilestone { Id = 2, MilestoneCode = "STAGE1_FINAL", Title = "Stage 1 DPP", Status = "Pending", MilestoneSource = "Default" };
            var milestones = new List<FYPMilestone> { iaf, task, dpp };

            var result = GradingHelper.CanSubmitStage1FinalDpp(milestones, iaf, null);

            Assert.False(result.Allowed);
            Assert.Contains("Cannot submit Stage 1 Proposal", result.Reason);
            Assert.Contains("IAF is currently in 'Pending' status", result.Reason);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenNoSupervisorTasks_DirectPostFinalSubmit_ShouldBeRejectedServerSide()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 150,
                FullName = "Student No Tasks",
                EmployeeId = "p15000001",
                Email = "p150@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 50,
                StudentId = 150,
                Title = "No Tasks Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 501,
                FYPProjectId = 50,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var dppMilestone = new FYPMilestone
            {
                Id = 502,
                FYPProjectId = 50,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Formal submission attempt (isDraft = false)
            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Draft Proposal",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            // Assert: Server-side guard blocks submission with exact lock message
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.True(controller.TempData.ContainsKey("Error"));
            Assert.Equal("DPP Submission Locked — Waiting for your Supervisor to assign Stage 1 DPP progress tasks.", controller.TempData["Error"]!.ToString());

            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Pending", dppAfter!.Status);
            Assert.Null(dppAfter.SubmittedDate);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenTaskPendingReview_DirectPostFinalSubmit_ShouldBeRejectedServerSide()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 151,
                FullName = "Student Task Pending",
                EmployeeId = "p15100001",
                Email = "p151@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 51,
                StudentId = 151,
                Title = "Task Pending Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 511,
                FYPProjectId = 51,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var supTask = new FYPMilestone
            {
                Id = 512,
                FYPProjectId = 51,
                Title = "Stage 1 Progress Task 1",
                MilestoneSource = "Supervisor",
                Status = "PendingSupervisorReview",
                Stage = 1
            };
            context.FYPMilestones.Add(supTask);

            var dppMilestone = new FYPMilestone
            {
                Id = 513,
                FYPProjectId = 51,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Formal submission attempt (isDraft = false)
            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Draft Proposal",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            // Assert: Server-side guard blocks submission with exact lock message
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.True(controller.TempData.ContainsKey("Error"));
            Assert.Equal("DPP Submission Locked — Waiting for Supervisor approval of all required Stage 1 progress tasks.", controller.TempData["Error"]!.ToString());

            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Pending", dppAfter!.Status);
            Assert.Null(dppAfter.SubmittedDate);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenDraft_ShouldBeAllowedEvenBeforeTasksApproved()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 152,
                FullName = "Student Draft",
                EmployeeId = "p15200001",
                Email = "p152@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 52,
                StudentId = 152,
                Title = "Draft Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 521,
                FYPProjectId = 52,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var dppMilestone = new FYPMilestone
            {
                Id = 522,
                FYPProjectId = 52,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Save Draft (isDraft = true) before supervisor tasks are assigned or approved
            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "My Work In Progress Title",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            // Assert: Draft is successfully saved!
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.True(controller.TempData.ContainsKey("Success"));
            Assert.Equal("Draft saved successfully.", controller.TempData["Success"]!.ToString());

            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Draft", dppAfter!.Status);
            Assert.Null(dppAfter.SubmittedDate);
        }

        [Fact]
        public async Task SubmitDigitalForm_WhenAllTasksApproved_ShouldAllowFinalSubmission()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 153,
                FullName = "Student Approved Tasks",
                EmployeeId = "p15300001",
                Email = "p153@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 53,
                StudentId = 153,
                Title = "Approved Tasks Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 531,
                FYPProjectId = 53,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var supTask = new FYPMilestone
            {
                Id = 532,
                FYPProjectId = 53,
                Title = "Stage 1 Progress Task 1",
                MilestoneSource = "Supervisor",
                Status = "Completed",
                Stage = 1
            };
            context.FYPMilestones.Add(supTask);

            var dppMilestone = new FYPMilestone
            {
                Id = 533,
                FYPProjectId = 53,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Pending",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Formal submission attempt (isDraft = false) when all tasks are approved
            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Finalized Proposal Title",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            // Assert: Formal submission allowed!
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);

            var dppAfter = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            Assert.Equal("Submitted", dppAfter!.Status);
            Assert.NotNull(dppAfter.SubmittedDate);
        }

        [Fact]
        public async Task SubmitDigitalForm_UnauthorizedStudent_ShouldBeRejected()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var ownerStudent = new ApplicationUser { Id = 154, FullName = "Owner Student", EmployeeId = "p15400001", Email = "p154@test.com" };
            var attackerStudent = new ApplicationUser { Id = 155, FullName = "Attacker Student", EmployeeId = "p15500001", Email = "p155@test.com" };
            context.ApplicationUsers.AddRange(ownerStudent, attackerStudent);

            var project = new FYPProject { Id = 54, StudentId = 154, Title = "Owner Project" };
            context.FYPProjects.Add(project);

            var dppMilestone = new FYPMilestone { Id = 541, FYPProjectId = 54, MilestoneCode = "STAGE1_FINAL", Title = "DPP", Status = "Pending", Stage = 1 };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", attackerStudent.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Hacked Submission",
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("Invalid milestone or unauthorized access.", controller.TempData["Error"]!.ToString());
        }

        [Fact]
        public async Task SubmitDigitalForm_ProgressTask_SaveDraft_PersistsIsoDates_WithoutPdf_AndRemainsDraft()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: "ProgressTask_SaveDraft_IsoDates_" + Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 160,
                UserName = "p99000001",
                FullName = "Student One",
                AcademicSession = "2026/2027-1"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 60,
                StudentId = 160,
                Title = "Progress Task Test Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 601,
                FYPProjectId = 60,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var chapter1Task = new FYPMilestone
            {
                Id = 602,
                FYPProjectId = 60,
                Title = "Chapter 1: Introduction",
                MilestoneSource = "Supervisor",
                Status = "Draft",
                Stage = 1
            };
            context.FYPMilestones.Add(chapter1Task);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act: Student saves draft with Start Period = 2026-10-23, End Period = 2026-10-23, without PDF (attachedFile = null)
            var result = await controller.SubmitDigitalForm(
                milestoneId: chapter1Task.Id,
                formType: "DPP Work & Progress",
                title: null,
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: new DateTime(2026, 10, 23),
                endDate: new DateTime(2026, 10, 23),
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("Draft saved successfully.", controller.TempData["Success"]!.ToString());

            var updatedMilestone = await context.FYPMilestones.FindAsync(chapter1Task.Id);
            Assert.NotNull(updatedMilestone);
            Assert.Equal("Draft", updatedMilestone.Status); // Remains Draft, NOT submitted to Supervisor
            Assert.Contains("\"Start Period\":\"2026-10-23\"", updatedMilestone.Remarks);
            Assert.Contains("\"End Period\":\"2026-10-23\"", updatedMilestone.Remarks);

            // Prerequisite check: Final DPP submission is still blocked because task is only a draft
            var prereq = GradingHelper.CanSubmitStage1FinalDpp(new[] { iafMilestone, updatedMilestone }, iafMilestone, null);
            Assert.False(prereq.Allowed);
            Assert.Contains("Waiting for Supervisor approval", prereq.Reason);
        }

        [Fact]
        public async Task SubmitDigitalForm_ProgressTask_Chapter1And2_PreservesSeparateDates()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: "ProgressTask_Chapter1And2_" + Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 161,
                UserName = "p99000001",
                FullName = "Student One",
                AcademicSession = "2026/2027-1"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 61,
                StudentId = 161,
                Title = "Multi-Chapter Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = 610,
                FYPProjectId = 61,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var ch1 = new FYPMilestone
            {
                Id = 611,
                FYPProjectId = 61,
                Title = "Chapter 1: Introduction",
                MilestoneSource = "Supervisor",
                Status = "Draft",
                Stage = 1
            };
            var ch2 = new FYPMilestone
            {
                Id = 612,
                FYPProjectId = 61,
                Title = "Chapter 2: Literature Review",
                MilestoneSource = "Supervisor",
                Status = "Draft",
                Stage = 1
            };
            context.FYPMilestones.AddRange(ch1, ch2);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, null!, null!, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // 1. Save Chapter 1
            await controller.SubmitDigitalForm(
                milestoneId: ch1.Id,
                formType: "DPP Work & Progress",
                title: null,
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: new DateTime(2026, 10, 23),
                endDate: new DateTime(2026, 10, 23),
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            // 2. Save Chapter 2 with different dates
            await controller.SubmitDigitalForm(
                milestoneId: ch2.Id,
                formType: "DPP Work & Progress",
                title: null,
                detailsJson: null,
                attachedFile: null,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: new DateTime(2026, 11, 1),
                endDate: new DateTime(2026, 11, 15),
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            // 3. Verify Chapter 1 is completely preserved and distinct from Chapter 2
            var ch1Reloaded = await context.FYPMilestones.FindAsync(ch1.Id);
            var ch2Reloaded = await context.FYPMilestones.FindAsync(ch2.Id);

            Assert.NotNull(ch1Reloaded);
            Assert.NotNull(ch2Reloaded);

            Assert.Contains("\"Start Period\":\"2026-10-23\"", ch1Reloaded.Remarks);
            Assert.Contains("\"End Period\":\"2026-10-23\"", ch1Reloaded.Remarks);

            Assert.Contains("\"Start Period\":\"2026-11-01\"", ch2Reloaded.Remarks);
            Assert.Contains("\"End Period\":\"2026-11-15\"", ch2Reloaded.Remarks);
        }

        private static IFormFile CreateTestProgressFormFile(string fileName, string contentType = "application/pdf")
        {
            var content = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 test progress report content");
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "attachedFiles", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        private static (StudentController controller, FYPTrackerContext context, string tempDir, ApplicationUser student, FYPMilestone progressMilestone) SetupProgressControllerContext(
            string milestoneStatus = "Draft",
            int studentId = 181,
            int projectId = 81,
            int milestoneId = 812)
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var context = new FYPTrackerContext(options);
            var tempDir = Path.Combine(Path.GetTempPath(), "ProgressTestEnv_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var env = new TestWebHostEnvironment
            {
                EnvironmentName = "Testing",
                ContentRootPath = tempDir,
                WebRootPath = tempDir
            };

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Entities", "Server=(localdb)\\mssqllocaldb;Database=FYPTracker_Test;Trusted_Connection=True;" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var student = new ApplicationUser
            {
                Id = studentId,
                FullName = "Test Progress Student",
                EmployeeId = $"p{studentId}0001",
                Email = $"student{studentId}@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = projectId,
                StudentId = studentId,
                Title = "Progress Test Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var iafMilestone = new FYPMilestone
            {
                Id = projectId * 10 + 1,
                FYPProjectId = projectId,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true
            };
            context.FYPMilestones.Add(iafMilestone);

            var progressTask = new FYPMilestone
            {
                Id = milestoneId,
                FYPProjectId = projectId,
                Title = "Chapter 1: Introduction",
                MilestoneSource = "Supervisor",
                Status = milestoneStatus,
                Stage = 1
            };
            context.FYPMilestones.Add(progressTask);
            context.SaveChanges();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            return (controller, context, tempDir, student, progressTask);
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_SameDayReporting_Allowed()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var pdfFile = CreateTestProgressFormFile("Chapter1_Intro.pdf");
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { pdfFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                Assert.Null(controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("PendingSupervisorReview", updated.Status);
                Assert.NotNull(updated.SubmittedDate);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_FutureDates_Allowed()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var pdfFile = CreateTestProgressFormFile("Chapter1_Future.pdf");
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { pdfFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2027, 2, 1),
                    endDate: new DateTime(2027, 2, 15),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                Assert.Null(controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("PendingSupervisorReview", updated.Status);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_InvalidDateRange_EndDateBeforeStartDate_Rejected()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var pdfFile = CreateTestProgressFormFile("Chapter1.pdf");
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { pdfFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 25),
                    endDate: new DateTime(2026, 10, 20),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                Assert.Equal("End Period must be equal to or after the Start Period.", controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("Draft", updated.Status);
                Assert.Null(updated.SubmittedDate);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_FormalSubmission_WithoutFiles_Rejected()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                Assert.Equal("Please upload a progress document (PDF).", controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("Draft", updated.Status);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_FormalSubmission_NonPdfFile_Rejected()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var content = System.Text.Encoding.UTF8.GetBytes("fake docx content");
                var stream = new MemoryStream(content);
                var docxFile = new FormFile(stream, 0, content.Length, "attachedFiles", "report.docx")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                };

                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { docxFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                Assert.Equal("Only PDF files are allowed for DPP Work & Progress.", controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("Draft", updated.Status);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_ReturnedForChanges_WithoutRevisionNotes_Rejected()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(milestoneStatus: "ReturnedForChanges");
            try
            {
                var pdfFile = CreateTestProgressFormFile("Chapter1_Fix.pdf");
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { pdfFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: "   ", // Empty / whitespace
                    isDraft: false);

                Assert.Equal("Revision details are required when resubmitting a returned milestone.", controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("ReturnedForChanges", updated.Status);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_ReturnedForChanges_WithRevisionNotes_Accepted()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(milestoneStatus: "ReturnedForChanges");
            try
            {
                var pdfFile = CreateTestProgressFormFile("Chapter1_Revised.pdf");
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: new List<IFormFile> { pdfFile },
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: "Addressed feedback in Section 1.3.",
                    isDraft: false);

                Assert.Null(controller.TempData["Error"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("PendingSupervisorReview", updated.Status);
                Assert.NotNull(updated.SubmittedDate);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task SubmitDigitalForm_Progress_Draft_PreservesReturnedStatusAndAllowsSameDay()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(milestoneStatus: "ReturnedForChanges");
            try
            {
                var result = await controller.SubmitDigitalForm(
                    milestoneId: progressTask.Id,
                    formType: "DPP Work & Progress",
                    title: null,
                    detailsJson: null,
                    attachedFile: null,
                    attachedFiles: null,
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: new DateTime(2026, 10, 23),
                    endDate: new DateTime(2026, 10, 23),
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: "Work in progress revision notes",
                    isDraft: true);

                Assert.Null(controller.TempData["Error"]);
                Assert.Equal("Draft saved successfully.", controller.TempData["Success"]);
                var updated = await context.FYPMilestones.FindAsync(progressTask.Id);
                Assert.NotNull(updated);
                Assert.Equal("ReturnedForChanges", updated.Status);
                Assert.Contains("\"Start Period\":\"2026-10-23\"", updated.Remarks);
                Assert.Contains("\"End Period\":\"2026-10-23\"", updated.Remarks);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-99)]
        public async Task GetMilestoneFeedback_InvalidMilestoneId_ReturnsBadRequest(int invalidId)
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                var result = await controller.GetMilestoneFeedback(invalidId);
                var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
                Assert.Equal(400, badRequestResult.StatusCode);

                var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(badRequestResult.Value));
                Assert.False(jsonDoc.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("A valid milestone ID is required.", jsonDoc.RootElement.GetProperty("message").GetString());
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_MilestoneWithNoFeedback_ReturnsEmptyFeedbackAndAttempts()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(
                milestoneStatus: "Completed",
                studentId: 251,
                projectId: 151,
                milestoneId: 1512);
            try
            {
                var ms = await context.FYPMilestones.FindAsync(progressTask.Id);
                ms!.SupervisorFeedback = null;
                ms.SupervisorMarksheetData = null;
                await context.SaveChangesAsync();

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var jsonResult = Assert.IsType<JsonResult>(result);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonResult.Value));
                var root = jsonDoc.RootElement;

                Assert.True(root.GetProperty("success").GetBoolean());
                var data = root.GetProperty("data");
                Assert.Equal("Completed", data.GetProperty("status").GetString());
                Assert.True(data.GetProperty("latestFeedback").ValueKind == System.Text.Json.JsonValueKind.Null);
                Assert.True(data.GetProperty("rubricFeedback").ValueKind == System.Text.Json.JsonValueKind.Null);

                var attempts = data.GetProperty("attempts");
                Assert.Equal(1, attempts.GetArrayLength());
                Assert.True(attempts[0].GetProperty("supervisorFeedback").ValueKind == System.Text.Json.JsonValueKind.Null);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_Unauthenticated_ReturnsUnauthorized()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext();
            try
            {
                // Set unauthenticated user
                controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var unauthResult = Assert.IsType<UnauthorizedObjectResult>(result);
                Assert.Equal(401, unauthResult.StatusCode);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_BelongsToAnotherStudent_ReturnsNotFound()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(studentId: 191, projectId: 91, milestoneId: 912);
            try
            {
                // Another student
                var otherStudent = new ApplicationUser
                {
                    Id = 192,
                    FullName = "Other Student",
                    EmployeeId = "p1920001",
                    Email = "other192@test.com"
                };
                context.ApplicationUsers.Add(otherStudent);
                await context.SaveChangesAsync();

                // Authenticate as other student
                controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("Id", otherStudent.Id.ToString()),
                    new Claim(ClaimTypes.Role, "Student")
                }, "TestAuth"));

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
                Assert.Equal(404, notFoundResult.StatusCode);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_ApprovedMilestone_ReturnsFeedbackAndSupervisorDetails()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(
                milestoneStatus: "Approved",
                studentId: 201,
                projectId: 101,
                milestoneId: 1012);
            try
            {
                var supervisor = new ApplicationUser
                {
                    Id = 202,
                    FullName = "Dr. Test Supervisor",
                    EmployeeId = "t2020001",
                    Email = "supervisor@test.com"
                };
                context.ApplicationUsers.Add(supervisor);

                var proj = await context.FYPProjects.FindAsync(101);
                proj!.SupervisorId = supervisor.Id;

                var ms = await context.FYPMilestones.FindAsync(progressTask.Id);
                ms!.SupervisorFeedback = "Excellent literature synthesis. Ready for chapter 2.";
                ms.SubmittedDate = new DateTime(2026, 10, 23, 10, 0, 0);
                await context.SaveChangesAsync();

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var jsonResult = Assert.IsType<JsonResult>(result);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonResult.Value));
                var root = jsonDoc.RootElement;

                Assert.True(root.GetProperty("success").GetBoolean());
                var data = root.GetProperty("data");
                Assert.Equal("Chapter 1: Introduction", data.GetProperty("milestoneTitle").GetString());
                Assert.Equal("Approved", data.GetProperty("status").GetString());
                Assert.Equal("Dr. Test Supervisor", data.GetProperty("supervisorName").GetString());
                Assert.Equal("Excellent literature synthesis. Ready for chapter 2.", data.GetProperty("latestFeedback").GetString());

                var attempts = data.GetProperty("attempts");
                Assert.Equal(1, attempts.GetArrayLength());
                var firstAttempt = attempts[0];
                Assert.Equal(1, firstAttempt.GetProperty("attemptNumber").GetInt32());
                Assert.Equal("Excellent literature synthesis. Ready for chapter 2.", firstAttempt.GetProperty("supervisorFeedback").GetString());
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_ReturnedForChanges_ReturnsFeedbackAndRevisionNotes()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(
                milestoneStatus: "ReturnedForChanges",
                studentId: 211,
                projectId: 111,
                milestoneId: 1112);
            try
            {
                var ms = await context.FYPMilestones.FindAsync(progressTask.Id);
                ms!.SupervisorFeedback = "Please narrow down the research question in section 1.2.";
                ms.SubmittedDate = new DateTime(2026, 10, 22, 14, 30, 0);

                var subFile = new FYPSubmissionFile
                {
                    Id = 551,
                    MilestoneId = progressTask.Id,
                    FileName = "Ch1_Draft_v1.pdf",
                    FilePath = "uploads/Ch1_Draft_v1.pdf",
                    RevisionNotes = "Initial submission for review",
                    AttemptNumber = 1,
                    UploadedDate = new DateTime(2026, 10, 22, 14, 0, 0)
                };
                context.FYPSubmissionFiles.Add(subFile);
                await context.SaveChangesAsync();

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var jsonResult = Assert.IsType<JsonResult>(result);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonResult.Value));
                var root = jsonDoc.RootElement;

                Assert.True(root.GetProperty("success").GetBoolean());
                var data = root.GetProperty("data");
                Assert.Equal("ReturnedForChanges", data.GetProperty("status").GetString());
                Assert.Equal("Please narrow down the research question in section 1.2.", data.GetProperty("latestFeedback").GetString());

                var attempts = data.GetProperty("attempts");
                Assert.Equal(1, attempts.GetArrayLength());
                var att = attempts[0];
                Assert.Equal(1, att.GetProperty("attemptNumber").GetInt32());
                Assert.Equal("Please narrow down the research question in section 1.2.", att.GetProperty("supervisorFeedback").GetString());
                Assert.Equal("Initial submission for review", att.GetProperty("revisionNotes").GetString());
                Assert.Contains("Ch1_Draft_v1.pdf", att.GetProperty("files")[0].GetString());
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_MultiAttemptHistory_PreservesSeparateAttempts()
        {
            var (controller, context, tempDir, student, progressTask) = SetupProgressControllerContext(
                milestoneStatus: "Approved",
                studentId: 221,
                projectId: 121,
                milestoneId: 1212);
            try
            {
                var ms = await context.FYPMilestones.FindAsync(progressTask.Id);
                ms!.SupervisorFeedback = "Second attempt looks great and is approved.";
                ms.SubmittedDate = new DateTime(2026, 10, 24, 11, 0, 0);

                var fileAttempt1 = new FYPSubmissionFile
                {
                    Id = 561,
                    MilestoneId = progressTask.Id,
                    FileName = "Ch1_Attempt1.pdf",
                    FilePath = "uploads/Ch1_Attempt1.pdf",
                    RevisionNotes = "First draft",
                    AttemptNumber = 1,
                    UploadedDate = new DateTime(2026, 10, 20, 10, 0, 0)
                };
                var fileAttempt2 = new FYPSubmissionFile
                {
                    Id = 562,
                    MilestoneId = progressTask.Id,
                    FileName = "Ch1_Attempt2_Revised.pdf",
                    FilePath = "uploads/Ch1_Attempt2_Revised.pdf",
                    RevisionNotes = "Addressed all supervisor comments",
                    AttemptNumber = 2,
                    UploadedDate = new DateTime(2026, 10, 24, 9, 30, 0)
                };
                context.FYPSubmissionFiles.AddRange(fileAttempt1, fileAttempt2);
                await context.SaveChangesAsync();

                var result = await controller.GetMilestoneFeedback(progressTask.Id);
                var jsonResult = Assert.IsType<JsonResult>(result);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonResult.Value));
                var attempts = jsonDoc.RootElement.GetProperty("data").GetProperty("attempts");

                Assert.Equal(2, attempts.GetArrayLength());

                // Attempt 1 check
                var att1 = attempts[0];
                Assert.Equal(1, att1.GetProperty("attemptNumber").GetInt32());
                Assert.Equal("ReturnedForChanges", att1.GetProperty("status").GetString());
                Assert.Equal("First draft", att1.GetProperty("revisionNotes").GetString());
                Assert.Contains("Ch1_Attempt1.pdf", att1.GetProperty("files")[0].GetString());

                // Attempt 2 check
                var att2 = attempts[1];
                Assert.Equal(2, att2.GetProperty("attemptNumber").GetInt32());
                Assert.Equal("Approved", att2.GetProperty("status").GetString());
                Assert.Equal("Addressed all supervisor comments", att2.GetProperty("revisionNotes").GetString());
                Assert.Equal("Second attempt looks great and is approved.", att2.GetProperty("supervisorFeedback").GetString());
                Assert.Contains("Ch1_Attempt2_Revised.pdf", att2.GetProperty("files")[0].GetString());
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetMilestoneFeedback_IafAttemptHistory_PreservedAccurately()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser
            {
                Id = 231,
                FullName = "IAF Student",
                EmployeeId = "p2310001",
                Email = "iaf231@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 131,
                StudentId = 231,
                Title = "IAF Testing Project",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            var history = new IafAttemptHistory
            {
                CurrentAttempt = 2,
                Attempts = new List<IafAttempt>
                {
                    new IafAttempt
                    {
                        AttemptNumber = 1,
                        Status = "ReturnedForChanges",
                        SubmittedAt = new DateTime(2026, 9, 15, 9, 0, 0),
                        SupervisorReviewedAt = new DateTime(2026, 9, 16, 14, 0, 0),
                        SupervisorFeedback = "Initial IAF scope needs to be more specific regarding cloud architecture.",
                        PanelComments = "Panel agrees with supervisor recommendation."
                    },
                    new IafAttempt
                    {
                        AttemptNumber = 2,
                        Status = "AdminApproved",
                        SubmittedAt = new DateTime(2026, 9, 20, 10, 0, 0),
                        SupervisorReviewedAt = new DateTime(2026, 9, 21, 11, 0, 0),
                        SupervisorFeedback = "Revised scope is clear and achievable.",
                        PanelComments = "Defense completed successfully."
                    }
                }
            };

            var iafMilestone = new FYPMilestone
            {
                Id = 1311,
                FYPProjectId = 131,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1,
                IsRequired = true,
                Remarks = "JSON: " + System.Text.Json.JsonSerializer.Serialize(history)
            };
            context.FYPMilestones.Add(iafMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await controller.GetMilestoneFeedback(iafMilestone.Id);
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(jsonResult.Value));
            var attempts = jsonDoc.RootElement.GetProperty("data").GetProperty("attempts");

            Assert.Equal(2, attempts.GetArrayLength());

            var att1 = attempts[0];
            Assert.Equal(1, att1.GetProperty("attemptNumber").GetInt32());
            Assert.Equal("ReturnedForChanges", att1.GetProperty("status").GetString());
            Assert.Equal("Initial IAF scope needs to be more specific regarding cloud architecture.", att1.GetProperty("supervisorFeedback").GetString());
            Assert.Equal("Panel agrees with supervisor recommendation.", att1.GetProperty("panelFeedback").GetString());

            var att2 = attempts[1];
            Assert.Equal(2, att2.GetProperty("attemptNumber").GetInt32());
            Assert.Equal("AdminApproved", att2.GetProperty("status").GetString());
            Assert.Equal("Revised scope is clear and achievable.", att2.GetProperty("supervisorFeedback").GetString());
            Assert.Equal("Defense completed successfully.", att2.GetProperty("panelFeedback").GetString());
        }

        [Fact]
        public async Task GetMilestoneFeedback_Stage1AndStage2_FeedbackKeptSeparate()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser
            {
                Id = 241,
                FullName = "Stage Student",
                EmployeeId = "p2410001",
                Email = "stage241@test.com"
            };
            context.ApplicationUsers.Add(student);

            var project = new FYPProject
            {
                Id = 141,
                StudentId = 241,
                Title = "Two Stage Project",
                AcademicSession = "2026/2027-1",
                Stage2SupervisorMarksheetData = "{\"Feedback\":\"Outstanding final thesis defense!\"}"
            };
            context.FYPProjects.Add(project);

            var stage1Ms = new FYPMilestone
            {
                Id = 1411,
                FYPProjectId = 141,
                Title = "Chapter 1: Introduction",
                Stage = 1,
                Status = "Approved",
                SupervisorFeedback = "Stage 1 progress task feedback."
            };

            var stage2FinalMs = new FYPMilestone
            {
                Id = 1412,
                FYPProjectId = 141,
                Title = "Final Thesis Submission",
                Stage = 2,
                Status = "Approved",
                SupervisorFeedback = "Stage 2 milestone feedback."
            };

            context.FYPMilestones.AddRange(stage1Ms, stage2FinalMs);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // 1. Stage 1 Milestone: Must NOT have Stage 2 rubric feedback
            var s1Result = await controller.GetMilestoneFeedback(stage1Ms.Id);
            var s1Json = Assert.IsType<JsonResult>(s1Result);
            var s1Doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(s1Json.Value));
            var s1Data = s1Doc.RootElement.GetProperty("data");
            Assert.Equal("Stage 1 progress task feedback.", s1Data.GetProperty("latestFeedback").GetString());
            Assert.True(s1Data.GetProperty("rubricFeedback").ValueKind == System.Text.Json.JsonValueKind.Null);

            // 2. Stage 2 Final Milestone: Must have Stage 2 remarks
            var s2Result = await controller.GetMilestoneFeedback(stage2FinalMs.Id);
            var s2Json = Assert.IsType<JsonResult>(s2Result);
            var s2Doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(s2Json.Value));
            var s2Data = s2Doc.RootElement.GetProperty("data");
            Assert.Equal("Stage 2 milestone feedback.", s2Data.GetProperty("latestFeedback").GetString());
            Assert.Equal("Outstanding final thesis defense!", s2Data.GetProperty("rubricFeedback").GetString());
        }

        [Fact]
        public async Task DownloadThesisCoverPage_AuthorizedStudent_ReturnsValidDocxWithStudentMetadata()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var supervisor = new ApplicationUser
            {
                Id = 150,
                FullName = "Dr. Jane Supervisor",
                Email = "supervisor@test.com"
            };
            var student = new ApplicationUser
            {
                Id = 151,
                FullName = "Alice Tan",
                EmployeeId = "p15100001",
                CoventryId = "C9876543",
                Programme = "BSc (Hons) Computing",
                Email = "alice@test.com"
            };
            context.ApplicationUsers.AddRange(supervisor, student);

            var project = new FYPProject
            {
                Id = 91,
                StudentId = 151,
                SupervisorId = 150,
                Title = "Cloud-Based Health Monitoring System",
                AcademicSession = "2026/2027-1"
            };
            context.FYPProjects.Add(project);

            context.FYPSettings.Add(new FYPSetting
            {
                Id = 1,
                Key = "DPPDeadline",
                Value = "2026-10-25"
            });
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string projectDir = Directory.GetCurrentDirectory();
            if (!Directory.Exists(Path.Combine(projectDir, "FormTemplates")))
            {
                var parent = Directory.GetParent(projectDir);
                while (parent != null && !Directory.Exists(Path.Combine(parent.FullName, "FYPTracker", "FormTemplates")))
                {
                    parent = parent.Parent;
                }
                if (parent != null)
                {
                    projectDir = Path.Combine(parent.FullName, "FYPTracker");
                }
            }

            var env = new TestWebHostEnvironment { ContentRootPath = projectDir, WebRootPath = projectDir };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Act
            var result = await controller.DownloadThesisCoverPage();

            // Assert
            var fileResult = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileResult.ContentType);
            Assert.Equal("Thesis_Cover_Page_C9876543.docx", fileResult.FileDownloadName);
            Assert.True(CoverPageDocxHelper.IsValidDocx(fileResult.FileContents));

            // Verify metadata is populated inside the DOCX
            using var ms = new MemoryStream(fileResult.FileContents);
            var doc = new NPOI.XWPF.UserModel.XWPFDocument(ms);
            var allText = string.Join(" ", doc.Paragraphs.Select(p => p.ParagraphText));
            Assert.Contains("CLOUD-BASED HEALTH MONITORING SYSTEM", allText);
            Assert.Contains("Alice Tan", allText);
            Assert.Contains("C9876543", allText);
            Assert.Contains("Dr. Jane Supervisor", allText);
            Assert.Contains("BSc (Hons) Computing", allText);
            Assert.Contains("25th October 2026", allText);
        }

        [Fact]
        public async Task DownloadThesisCoverPage_Unauthenticated_RedirectsToLogin()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);
            var httpContext = new DefaultHttpContext();

            var env = new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await controller.DownloadThesisCoverPage();
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
            Assert.Equal("User", redirect.ControllerName);
        }

        [Fact]
        public async Task DownloadThesisCoverPage_StudentWithoutProject_RedirectsWithErrorMessage()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 160, FullName = "No Project Student" };
            context.ApplicationUsers.Add(student);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var result = await controller.DownloadThesisCoverPage();
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("No active project found for your student account.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task DownloadThesisCoverPage_MissingPublishedTemplate_RedirectsWithClearExplanation()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 170, FullName = "Missing Template Student" };
            var project = new FYPProject { Id = 92, StudentId = 170, Title = "Test Title" };
            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string emptyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(emptyDir);

            try
            {
                var env = new TestWebHostEnvironment { ContentRootPath = emptyDir };
                var config = new ConfigurationBuilder().Build();
                var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
                controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
                controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

                var result = await controller.DownloadThesisCoverPage();
                var redirect = Assert.IsType<RedirectToActionResult>(result);
                Assert.Equal("Forms", redirect.ActionName);
                Assert.Contains("template has not been published yet", controller.TempData["Error"]?.ToString());
            }
            finally
            {
                if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, true);
            }
        }

        [Fact]
        public async Task DownloadThesisCoverPage_StudentCannotAccessAnotherStudentPersonalizedPage()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new FYPTrackerContext(options);

            var student1 = new ApplicationUser { Id = 181, FullName = "Student One", CoventryId = "C1111111" };
            var project1 = new FYPProject { Id = 93, StudentId = 181, Title = "Student One Project" };

            var student2 = new ApplicationUser { Id = 182, FullName = "Student Two", CoventryId = "C2222222" };
            var project2 = new FYPProject { Id = 94, StudentId = 182, Title = "Student Two Confidential Title" };

            context.ApplicationUsers.AddRange(student1, student2);
            context.FYPProjects.AddRange(project1, project2);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student1.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string projectDir = Directory.GetCurrentDirectory();
            var parent = Directory.GetParent(projectDir);
            while (parent != null && !Directory.Exists(Path.Combine(parent.FullName, "FYPTracker", "FormTemplates")))
            {
                parent = parent.Parent;
            }
            if (parent != null) projectDir = Path.Combine(parent.FullName, "FYPTracker");

            var env = new TestWebHostEnvironment { ContentRootPath = projectDir };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var result = await controller.DownloadThesisCoverPage();
            var fileResult = Assert.IsType<FileContentResult>(result);

            using var ms = new MemoryStream(fileResult.FileContents);
            var doc = new NPOI.XWPF.UserModel.XWPFDocument(ms);
            var allText = string.Join(" ", doc.Paragraphs.Select(p => p.ParagraphText));

            Assert.Contains("STUDENT ONE PROJECT", allText);
            Assert.Contains("Student One", allText);
            Assert.Contains("C1111111", allText);
            Assert.DoesNotContain("Student Two", allText);
            Assert.DoesNotContain("C2222222", allText);
            Assert.DoesNotContain("Confidential Title", allText);
        }

        [Fact]
        public async Task SubmitDigitalForm_Stage1Dpp_DocxFile_IsRejected()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 191, FullName = "Test Student" };
            var project = new FYPProject { Id = 95, StudentId = 191, Title = "Test Project" };
            var iafMilestone = new FYPMilestone
            {
                Id = 950,
                FYPProjectId = 95,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "Approved",
                Stage = 1,
                IsRequired = true
            };
            var supTask = new FYPMilestone
            {
                Id = 952,
                FYPProjectId = 95,
                Title = "Supervisor Task 1",
                MilestoneSource = "Supervisor",
                Status = "Approved",
                Stage = 1
            };
            var dppMilestone = new FYPMilestone
            {
                Id = 951,
                FYPProjectId = 95,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Draft",
                Stage = 1
            };
            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.AddRange(iafMilestone, supTask, dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var content = System.Text.Encoding.UTF8.GetBytes("fake docx content");
            var stream = new MemoryStream(content);
            var docxFile = new FormFile(stream, 0, content.Length, "attachedFile", "Final_Proposal.docx");

            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Test Proposal",
                detailsJson: null,
                attachedFile: docxFile,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Contains("Only PDF files are allowed", controller.TempData["Error"]?.ToString());
        }

        [Fact]
        public async Task SubmitDigitalForm_Stage1Dpp_DoubleExtensionDocxFile_IsRejected()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 192, FullName = "Test Student" };
            var project = new FYPProject { Id = 96, StudentId = 192, Title = "Test Project" };
            var iafMilestone = new FYPMilestone
            {
                Id = 960,
                FYPProjectId = 96,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "Approved",
                Stage = 1,
                IsRequired = true
            };
            var supTask = new FYPMilestone
            {
                Id = 962,
                FYPProjectId = 96,
                Title = "Supervisor Task 1",
                MilestoneSource = "Supervisor",
                Status = "Approved",
                Stage = 1
            };
            var dppMilestone = new FYPMilestone
            {
                Id = 961,
                FYPProjectId = 96,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Draft",
                Stage = 1
            };
            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.AddRange(iafMilestone, supTask, dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var content = System.Text.Encoding.UTF8.GetBytes("fake docx content");
            var stream = new MemoryStream(content);
            var docxFile = new FormFile(stream, 0, content.Length, "attachedFile", "TEST_Stage1_Final_DPP.pdf.docx");

            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Test Proposal",
                detailsJson: null,
                attachedFile: docxFile,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Contains("Only PDF files are allowed", controller.TempData["Error"]?.ToString());
        }

        [Fact]
        public async Task SubmitDigitalForm_Stage1Dpp_DisguisedDocxWithPdfExtension_IsRejected()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 193, FullName = "Test Student" };
            var project = new FYPProject { Id = 97, StudentId = 193, Title = "Test Project" };
            var iafMilestone = new FYPMilestone
            {
                Id = 970,
                FYPProjectId = 97,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "Approved",
                Stage = 1,
                IsRequired = true
            };
            var supTask = new FYPMilestone
            {
                Id = 972,
                FYPProjectId = 97,
                Title = "Supervisor Task 1",
                MilestoneSource = "Supervisor",
                Status = "Approved",
                Stage = 1
            };
            var dppMilestone = new FYPMilestone
            {
                Id = 971,
                FYPProjectId = 97,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Draft",
                Stage = 1
            };
            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.AddRange(iafMilestone, supTask, dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            // Zip archive header bytes PK\x03\x04
            var zipHeaderContent = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };
            var stream = new MemoryStream(zipHeaderContent);
            var disguisedFile = new FormFile(stream, 0, zipHeaderContent.Length, "attachedFile", "TEST_Stage1_Final_DPP.pdf");

            var result = await controller.SubmitDigitalForm(
                milestoneId: dppMilestone.Id,
                formType: "Proposal Form",
                title: "Test Proposal",
                detailsJson: null,
                attachedFile: disguisedFile,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: null,
                endDate: null,
                workDone: null,
                challenges: null,
                nextPeriodPlan: null,
                pctComplete: null,
                coventryId: null,
                revisionNotes: null,
                isDraft: true);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Contains("not a valid PDF document", controller.TempData["Error"]?.ToString());
        }

        [Fact]
        public async Task SubmitDigitalForm_Stage1Dpp_ValidPdf_IsAcceptedWhenPrerequisitesMet()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);
            var student = new ApplicationUser { Id = 194, FullName = "Test Student" };
            var project = new FYPProject { Id = 98, StudentId = 194, Title = "Test Project" };
            var iafMilestone = new FYPMilestone
            {
                Id = 980,
                FYPProjectId = 98,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "Approved",
                Stage = 1,
                IsRequired = true
            };
            var supTask = new FYPMilestone
            {
                Id = 981,
                FYPProjectId = 98,
                Title = "Supervisor Task 1",
                MilestoneSource = "Supervisor",
                Status = "Approved",
                Stage = 1
            };
            var dppMilestone = new FYPMilestone
            {
                Id = 982,
                FYPProjectId = 98,
                MilestoneCode = "STAGE1_FINAL",
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                Status = "Draft",
                Stage = 1,
                IsRequired = true
            };
            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.AddRange(iafMilestone, supTask, dppMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = tempDir, WebRootPath = tempDir };
                var config = new ConfigurationBuilder().Build();
                var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
                controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
                controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

                var pdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 mock valid pdf document stream content");
                var stream = new MemoryStream(pdfBytes);
                var validPdfFile = new FormFile(stream, 0, pdfBytes.Length, "attachedFile", "Final_DPP_Combined.pdf");

                var result = await controller.SubmitDigitalForm(
                    milestoneId: dppMilestone.Id,
                    formType: "Proposal Form",
                    title: "Test Proposal",
                    detailsJson: null,
                    attachedFile: validPdfFile,
                    attachedFiles: null,
                    problemStatement: null,
                    objective1: null,
                    objective2: null,
                    objective3: null,
                    scope: null,
                    outcome: null,
                    references: null,
                    startDate: null,
                    endDate: null,
                    workDone: null,
                    challenges: null,
                    nextPeriodPlan: null,
                    pctComplete: null,
                    coventryId: null,
                    revisionNotes: null,
                    isDraft: false);

                var redirect = Assert.IsType<RedirectToActionResult>(result);
                Assert.Equal("Forms", redirect.ActionName);
                Assert.Equal("Your form has been submitted successfully!", controller.TempData["Success"]);

                var updatedMilestone = await context.FYPMilestones.FindAsync(dppMilestone.Id);
                Assert.Equal("Submitted", updatedMilestone!.Status);

                var savedFile = await context.FYPSubmissionFiles.FirstOrDefaultAsync(f => f.MilestoneId == dppMilestone.Id);
                Assert.NotNull(savedFile);
                Assert.Equal("Final_DPP_Combined.pdf", savedFile!.FileName);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GenerateCoverPageDocx_A4Portrait_SinglePageStructure_HasNoUnintendedBreaks()
        {
            var bytes = CoverPageDocxHelper.GenerateCoverPageDocx(
                null,
                "Cloud-Based Health Monitoring System",
                "Alice Tan",
                "C9876543",
                "Dr. Jane Supervisor",
                "BSc (Hons) Computing",
                "25th October 2026");

            Assert.NotNull(bytes);
            Assert.True(CoverPageDocxHelper.IsValidDocx(bytes));

            using var ms = new MemoryStream(bytes);
            var doc = new NPOI.XWPF.UserModel.XWPFDocument(ms);

            // 1. Verify SectPr, Page Size (A4: 11906 x 16838 twips) and Portrait orientation
            var sectPr = doc.Document.body.sectPr;
            Assert.NotNull(sectPr);
            Assert.NotNull(sectPr.pgSz);
            Assert.Equal((ulong)11906, sectPr.pgSz.w);
            Assert.Equal((ulong)16838, sectPr.pgSz.h);
            Assert.Equal(NPOI.OpenXmlFormats.Wordprocessing.ST_PageOrientation.portrait, sectPr.pgSz.orient);

            // 2. Verify page margins (0.8-inch top/bottom, 1.0-inch left/right)
            Assert.NotNull(sectPr.pgMar);
            Assert.Equal("1152", sectPr.pgMar.top);
            Assert.Equal("1152", sectPr.pgMar.bottom);
            Assert.Equal((ulong)1440, sectPr.pgMar.left);
            Assert.Equal((ulong)1440, sectPr.pgMar.right);

            // 3. Verify exactly 8 substantive paragraphs with NO blank spacer paragraphs
            Assert.Equal(8, doc.Paragraphs.Count);
            foreach (var p in doc.Paragraphs)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.ParagraphText), "Cover page should not contain empty spacer paragraphs.");
                // Verify no page break runs
                foreach (var r in p.Runs)
                {
                    var text = r.GetText(0);
                    Assert.DoesNotContain("\f", text ?? "");
                    Assert.DoesNotContain("\x000c", text ?? "");
                }
            }

            // Save to temp for Word automation verification
            File.WriteAllBytes(@"C:\Users\ASUS\AppData\Local\Temp\coverpage_generated.docx", bytes);
        }

        [Fact]
        public void GenerateCoverPageDocx_LongMetadata_RemainsSinglePageStructure()
        {
            var bytes = CoverPageDocxHelper.GenerateCoverPageDocx(
                null,
                "DEVELOPMENT AND PERFORMANCE BENCHMARKING OF A HYBRID IOT EDGE-CLOUD COMPUTING FRAMEWORK FOR REAL-TIME BIO-SIGNAL ANOMALY DETECTION AND ANALYSIS",
                "Muhammad Haziq Bin Abdul Rahman",
                "C123456789",
                "Associate Professor Ts. Dr. Subramaniam A/L Ramasamy",
                "Bachelor of Science (Honours) in Computer Science and Software Engineering",
                "25th October 2026");

            Assert.NotNull(bytes);
            Assert.True(CoverPageDocxHelper.IsValidDocx(bytes));

            using var ms = new MemoryStream(bytes);
            var doc = new NPOI.XWPF.UserModel.XWPFDocument(ms);

            var sectPr = doc.Document.body.sectPr;
            Assert.NotNull(sectPr);
            Assert.Equal((ulong)11906, sectPr.pgSz.w);
            Assert.Equal((ulong)16838, sectPr.pgSz.h);
            Assert.Equal(NPOI.OpenXmlFormats.Wordprocessing.ST_PageOrientation.portrait, sectPr.pgSz.orient);

            Assert.Equal(8, doc.Paragraphs.Count);
            foreach (var p in doc.Paragraphs)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.ParagraphText));
            }

            File.WriteAllBytes(@"C:\Users\ASUS\AppData\Local\Temp\coverpage_long_metadata.docx", bytes);
        }

        [Fact]
        public void CheckStage2AcademicEligibility_WhenStage1GradeNullOrPending_ShouldBeLockedWithPendingReason()
        {
            var projectNullGrade = new FYPProject
            {
                Id = 1001,
                Title = "Test Project",
                Stage1Grade = null
            };

            var eligibilityNull = GradingHelper.CheckStage2AcademicEligibility(projectNullGrade);
            Assert.False(eligibilityNull.IsEligible);
            Assert.True(eligibilityNull.IsPendingGrading);
            Assert.Equal("Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", eligibilityNull.Reason);

            var projectPendingGrade = new FYPProject
            {
                Id = 1002,
                Title = "Test Project 2",
                Stage1Grade = "Pending"
            };

            var eligibilityPending = GradingHelper.CheckStage2AcademicEligibility(projectPendingGrade);
            Assert.False(eligibilityPending.IsEligible);
            Assert.True(eligibilityPending.IsPendingGrading);
            Assert.Equal("Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", eligibilityPending.Reason);
        }

        [Fact]
        public void CheckStage2AcademicEligibility_WhenStage1Pass_ShouldBeEligibleAndUnlocked()
        {
            var project = new FYPProject
            {
                Id = 1003,
                Title = "Test Project Passed",
                Stage1Grade = "Pass"
            };

            var eligibility = GradingHelper.CheckStage2AcademicEligibility(project);
            Assert.True(eligibility.IsEligible);
            Assert.False(eligibility.IsPendingGrading);
            Assert.Equal(string.Empty, eligibility.Reason);
        }

        [Fact]
        public void CheckStage2AcademicEligibility_WhenStage1FailWithValidIaf_ShouldBeEligibleForConcurrentRemediation()
        {
            var iafMilestone = new FYPMilestone
            {
                Id = 10041,
                FYPProjectId = 1004,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1
            };

            var project = new FYPProject
            {
                Id = 1004,
                Title = "Test Project Failed With Remediation",
                Stage1Grade = "Fail",
                Milestones = new List<FYPMilestone> { iafMilestone }
            };

            var eligibility = GradingHelper.CheckStage2AcademicEligibility(project, project.Milestones);
            Assert.True(eligibility.IsEligible);
            Assert.False(eligibility.IsPendingGrading);
            Assert.Equal(string.Empty, eligibility.Reason);
        }

        [Fact]
        public void CheckStage2AcademicEligibility_WhenStage1FailWithInvalidIaf_ShouldBeBlocked()
        {
            var iafMilestone = new FYPMilestone
            {
                Id = 10051,
                FYPProjectId = 1005,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "DefenseRequired",
                Stage = 1
            };

            var project = new FYPProject
            {
                Id = 1005,
                Title = "Test Project Failed Invalid IAF",
                Stage1Grade = "Fail",
                Milestones = new List<FYPMilestone> { iafMilestone }
            };

            var eligibility = GradingHelper.CheckStage2AcademicEligibility(project, project.Milestones);
            Assert.False(eligibility.IsEligible);
            Assert.False(eligibility.IsPendingGrading);
            Assert.Contains("Initial Project Allocation Form (IAF) is not approved", eligibility.Reason);
        }

        [Fact]
        public void CheckStage2AcademicEligibility_WhenRepeatRequested_ShouldBeBlocked()
        {
            var project = new FYPProject
            {
                Id = 1006,
                Title = "Test Project Repeat Requested",
                Stage1Grade = "Pass",
                Status = "RepeatRequested"
            };

            var eligibility = GradingHelper.CheckStage2AcademicEligibility(project);
            Assert.False(eligibility.IsEligible);
            Assert.Contains("repeat FYP request is currently pending Admin review", eligibility.Reason);
        }

        [Fact]
        public async Task SubmitStage2Milestone_WhenStage1GradingPending_DirectPost_ShouldBlockAndRedirectToForms()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 1007,
                FullName = "Student 1",
                Email = "student1@test.com",
                AcademicSession = "2026/2027-1"
            };

            var project = new FYPProject
            {
                Id = 1007,
                StudentId = student.Id,
                Title = "Student 1 Project",
                Status = "Approved",
                Stage1Grade = null // Stage 1 grading pending
            };

            var stage2Milestone = new FYPMilestone
            {
                Id = 10071,
                FYPProjectId = project.Id,
                Title = "Step 1: Turnitin Checking",
                Status = "Pending",
                Stage = 2,
                IsRequired = true
            };

            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.Add(stage2Milestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var mockFileBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 Mock Stage 2 content");
            var formFile = new FormFile(new MemoryStream(mockFileBytes), 0, mockFileBytes.Length, "file", "Turnitin_Report.pdf");

            var result = await controller.SubmitStage2Milestone("Step 1: Turnitin Checking", formFile);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", controller.TempData["Error"]);
        }

        [Fact]
        public async Task SubmitDigitalForm_Stage2Milestone_WhenStage1GradingPending_DirectPost_ShouldBlockAndRedirectToForms()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 1008,
                FullName = "Student 2",
                Email = "student2@test.com",
                AcademicSession = "2026/2027-1"
            };

            var project = new FYPProject
            {
                Id = 1008,
                StudentId = student.Id,
                Title = "Student 2 Project",
                Status = "Approved",
                Stage1Grade = null // Stage 1 grading pending
            };

            var stage2ProgressMilestone = new FYPMilestone
            {
                Id = 10081,
                FYPProjectId = project.Id,
                Title = "Stage 2 Sprint Progress",
                Status = "Pending",
                Stage = 2,
                MilestoneSource = "Supervisor",
                IsRequired = false
            };

            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.Add(stage2ProgressMilestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var mockPdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 Mock Progress content");
            var formFile = new FormFile(new MemoryStream(mockPdfBytes), 0, mockPdfBytes.Length, "attachedFile", "Stage2_Progress.pdf");

            var result = await controller.SubmitDigitalForm(
                milestoneId: stage2ProgressMilestone.Id,
                formType: "Stage 2 Progress Milestone",
                title: "Stage 2 Progress",
                detailsJson: null,
                attachedFile: formFile,
                attachedFiles: null,
                problemStatement: null,
                objective1: null,
                objective2: null,
                objective3: null,
                scope: null,
                outcome: null,
                references: null,
                startDate: DateTime.Today,
                endDate: DateTime.Today.AddDays(7),
                workDone: "Progress",
                challenges: "None",
                nextPeriodPlan: "Plan",
                pctComplete: "50",
                coventryId: null,
                revisionNotes: null,
                isDraft: false);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", controller.TempData["Error"]);
        }

        [Fact]
        public async Task SubmitStage2Milestone_WhenStage1Pass_ShouldAllowSubmission()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 1009,
                FullName = "Student 3",
                Email = "student3@test.com",
                AcademicSession = "2026/2027-1"
            };

            var project = new FYPProject
            {
                Id = 1009,
                StudentId = student.Id,
                Title = "Student 3 Project Passed",
                Status = "Approved",
                Stage1Grade = "Pass"
            };

            var stage2Milestone = new FYPMilestone
            {
                Id = 10091,
                FYPProjectId = project.Id,
                Title = "Step 1: Turnitin Checking",
                Status = "Pending",
                Stage = 2,
                IsRequired = true
            };

            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.Add(stage2Milestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "uploads", "submissions"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = tempDir, WebRootPath = tempDir };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var mockPdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 Mock Turnitin Report");
            var formFile = new FormFile(new MemoryStream(mockPdfBytes), 0, mockPdfBytes.Length, "file", "Turnitin_Report.pdf");

            var result = await controller.SubmitStage2Milestone("Step 1: Turnitin Checking", formFile);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Stage2", redirect.ActionName);
            Assert.Contains("successfully submitted", controller.TempData["Success"]?.ToString() ?? "");
        }

        [Fact]
        public async Task SubmitStage2Milestone_WhenStage1FailWithValidIaf_ShouldAllowSubmission()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 1010,
                FullName = "Student 4",
                Email = "student4@test.com",
                AcademicSession = "2026/2027-1"
            };

            var project = new FYPProject
            {
                Id = 1010,
                StudentId = student.Id,
                Title = "Student 4 Project Failed Remediation",
                Status = "Approved",
                Stage1Grade = "Fail"
            };

            var iafMilestone = new FYPMilestone
            {
                Id = 10101,
                FYPProjectId = project.Id,
                MilestoneCode = "IAF",
                Title = "Initial Project Allocation Form (IAF)",
                Status = "AdminApproved",
                Stage = 1
            };

            var stage2Milestone = new FYPMilestone
            {
                Id = 10102,
                FYPProjectId = project.Id,
                Title = "Step 1: Turnitin Checking",
                Status = "Pending",
                Stage = 2,
                IsRequired = true
            };

            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            context.FYPMilestones.AddRange(iafMilestone, stage2Milestone);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "uploads", "submissions"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = tempDir, WebRootPath = tempDir };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var mockPdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 Mock Turnitin Report");
            var formFile = new FormFile(new MemoryStream(mockPdfBytes), 0, mockPdfBytes.Length, "file", "Turnitin_Report.pdf");

            var result = await controller.SubmitStage2Milestone("Step 1: Turnitin Checking", formFile);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Stage2", redirect.ActionName);
            Assert.Contains("successfully submitted", controller.TempData["Success"]?.ToString() ?? "");
        }

        [Fact]
        public async Task Stage2_GetAction_WhenStage1GradingPending_ShouldRedirectToForms()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var student = new ApplicationUser
            {
                Id = 1011,
                FullName = "Student 5",
                Email = "student5@test.com",
                AcademicSession = "2026/2027-1"
            };

            var project = new FYPProject
            {
                Id = 1011,
                StudentId = student.Id,
                Title = "Student 5 Project Grading Pending",
                Status = "Approved",
                Stage1Grade = null
            };

            context.ApplicationUsers.Add(student);
            context.FYPProjects.Add(project);
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Id", student.Id.ToString()),
                new Claim(ClaimTypes.Role, "Student")
            }, "TestAuth"));

            var env = new TestWebHostEnvironment { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), WebRootPath = Path.GetTempPath() };
            var config = new ConfigurationBuilder().Build();
            var controller = new StudentController(context, env, config, null!, null!, null!, null!, new FYPTracker.Services.Time.RealSystemClock());
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            var result = await controller.Stage2();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Forms", redirect.ActionName);
            Assert.Equal("Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", controller.TempData["Error"]);
        }

        #region Supervisor Stage 1 Grading Reminder Tests

        [Fact]
        public void IsStage1SupervisorRubricPending_WhenProjectNullOrInactive_ShouldReturnFalse()
        {
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(null));

            var completedProj = new FYPProject { Id = 1, Status = "Completed" };
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(completedProj));

            var archivedProj = new FYPProject { Id = 2, Status = "Archived" };
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(archivedProj));

            var repeatProj = new FYPProject { Id = 3, Status = "RepeatRequested" };
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(repeatProj));

            var failedProj = new FYPProject { Id = 4, Status = "Failed" };
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(failedProj));
        }

        [Theory]
        [InlineData("Pending")]
        [InlineData("Submitted")]
        [InlineData("UnderReview")]
        [InlineData("Rejected")]
        [InlineData("Revision")]
        public void IsStage1SupervisorRubricPending_WhenStage1DppNotApproved_ShouldReturnFalse(string dppStatus)
        {
            var project = new FYPProject { Id = 10, Status = "Approved" };
            var dppMilestone = new FYPMilestone
            {
                Id = 101,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = dppStatus
            };

            bool isPending = GradingHelper.IsStage1SupervisorRubricPending(project, new[] { dppMilestone });
            Assert.False(isPending);
        }

        [Fact]
        public void IsStage1SupervisorRubricPending_WhenStage1DppApprovedAndRubricPending_ShouldReturnTrue()
        {
            var project = new FYPProject { Id = 10, Status = "Approved" };
            var dppMilestone = new FYPMilestone
            {
                Id = 101,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved",
                SupervisorMarksheetData = null
            };

            bool isPending = GradingHelper.IsStage1SupervisorRubricPending(project, new[] { dppMilestone });
            Assert.True(isPending);
        }

        [Fact]
        public void IsStage1SupervisorRubricPending_WhenStage1SupervisorRubricIsDraft_ShouldReturnTrue()
        {
            var project = new FYPProject { Id = 10, Status = "Approved" };
            var dppMilestone = new FYPMilestone
            {
                Id = 101,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved",
                SupervisorMarksheetData = "{\"IsDraft\": true, \"Criteria\": []}"
            };

            bool isPending = GradingHelper.IsStage1SupervisorRubricPending(project, new[] { dppMilestone });
            Assert.True(isPending);
        }

        [Fact]
        public void IsStage1SupervisorRubricPending_WhenStage1SupervisorRubricIsFinalized_ShouldReturnFalse()
        {
            var project = new FYPProject { Id = 10, Status = "Approved" };
            var dppMilestone = new FYPMilestone
            {
                Id = 101,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved",
                SupervisorMarksheetData = "{\"IsDraft\": false, \"Criteria\": [{\"Name\":\"A\",\"Score\":5}]}"
            };

            bool isPending = GradingHelper.IsStage1SupervisorRubricPending(project, new[] { dppMilestone });
            Assert.False(isPending);
        }

        [Fact]
        public async Task SupervisorDashboard_Index_CalculatesRemindersFor0EligibleStudents_DocumentsToReviewCountsSubmissionsOnly()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var supervisor = new ApplicationUser { Id = 501, FullName = "Dr. Test Supervisor", Email = "sup501@test.com" };
            var student = new ApplicationUser { Id = 502, FullName = "Student With Submitted DPP", Email = "stu502@test.com", IsResign = false };
            var role = new AppRole { Id = 15, Name = "Supervisor", Description = "Supervisor" };
            context.ApplicationUsers.AddRange(supervisor, student);
            context.AppRoles.Add(role);
            context.UserRoleMappings.Add(new UserRoleMapping { UserId = supervisor.Id, RoleId = role.Id });

            var project = new FYPProject
            {
                Id = 501,
                StudentId = student.Id,
                SupervisorId = supervisor.Id,
                AcademicSession = "2026/2027-1",
                Title = "Project with Submitted DPP",
                Status = "Approved"
            };
            context.FYPProjects.Add(project);

            // DPP Milestone is only "Submitted" (awaiting supervisor document review, NOT yet approved)
            var dppMilestone = new FYPMilestone
            {
                Id = 5001,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Submitted",
                SubmittedDate = DateTime.Now.AddDays(-2),
                Stage = 1
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var controller = CreateSupervisorController(context, supervisor);

            var result = await controller.Index("2026/2027-1");
            Assert.IsType<ViewResult>(result);

            // Requirement 3: Documents to Review counts document submissions only
            Assert.Equal(1, controller.ViewBag.PendingReviews);

            // Requirement 2 & 8: 0 students awaiting rubric grading because DPP is not approved yet
            Assert.Equal(0, controller.ViewBag.PendingStage1GradingCount);
        }

        [Fact]
        public async Task SupervisorDashboard_Index_CalculatesRemindersFor1EligibleStudent()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var supervisor = new ApplicationUser { Id = 601, FullName = "Dr. One Student Sup", Email = "sup601@test.com" };
            var student = new ApplicationUser { Id = 602, FullName = "Student 1 DPP Approved", Email = "stu602@test.com", IsResign = false };
            var role = new AppRole { Id = 16, Name = "Supervisor", Description = "Supervisor" };
            context.ApplicationUsers.AddRange(supervisor, student);
            context.AppRoles.Add(role);
            context.UserRoleMappings.Add(new UserRoleMapping { UserId = supervisor.Id, RoleId = role.Id });

            var project = new FYPProject
            {
                Id = 601,
                StudentId = student.Id,
                SupervisorId = supervisor.Id,
                AcademicSession = "2026/2027-1",
                Title = "Project 1 DPP Approved",
                Status = "Approved"
            };
            context.FYPProjects.Add(project);

            // DPP Milestone has been approved by supervisor, but rubric is pending
            var dppMilestone = new FYPMilestone
            {
                Id = 6001,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved",
                SubmittedDate = DateTime.Now.AddDays(-5),
                SupervisorMarksheetData = null,
                Stage = 1
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            var controller = CreateSupervisorController(context, supervisor);

            var result = await controller.Index("2026/2027-1");
            Assert.IsType<ViewResult>(result);

            // Documents to review is 0 since document was already approved
            Assert.Equal(0, controller.ViewBag.PendingReviews);

            // Exactly 1 student awaiting Stage 1 rubric grading
            Assert.Equal(1, controller.ViewBag.PendingStage1GradingCount);
        }

        [Fact]
        public async Task SupervisorDashboard_Index_CalculatesRemindersForMultipleEligibleStudents_AndIsolatesSessionsAndSupervisors()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var supervisorA = new ApplicationUser { Id = 701, FullName = "Supervisor A", Email = "supA@test.com" };
            var supervisorB = new ApplicationUser { Id = 702, FullName = "Supervisor B", Email = "supB@test.com" };
            var role = new AppRole { Id = 17, Name = "Supervisor", Description = "Supervisor" };
            context.ApplicationUsers.AddRange(supervisorA, supervisorB);
            context.AppRoles.Add(role);
            context.UserRoleMappings.AddRange(
                new UserRoleMapping { UserId = supervisorA.Id, RoleId = role.Id },
                new UserRoleMapping { UserId = supervisorB.Id, RoleId = role.Id }
            );

            // Helper to seed student, project and dpp milestone
            void SeedStudentProject(int sId, int pId, int supId, string session, string dppStatus, string? rubricData, bool isResign = false)
            {
                var stu = new ApplicationUser { Id = sId, FullName = $"Student {sId}", Email = $"stu{sId}@test.com", IsResign = isResign };
                var proj = new FYPProject { Id = pId, StudentId = sId, SupervisorId = supId, AcademicSession = session, Title = $"Project {pId}", Status = "Approved" };
                var ms = new FYPMilestone { Id = pId * 10, FYPProjectId = pId, Title = "FYP 1: Stage 1 Final Submission (DPP)", MilestoneCode = "STAGE1_FINAL", Status = dppStatus, SupervisorMarksheetData = rubricData, Stage = 1 };
                context.ApplicationUsers.Add(stu);
                context.FYPProjects.Add(proj);
                context.FYPMilestones.Add(ms);
            }

            // Supervisor A students in current session "2026/2027-1":
            // 1. Student 1: Approved DPP, rubric null -> ELIGIBLE
            SeedStudentProject(711, 711, supervisorA.Id, "2026/2027-1", "Approved", null);
            // 2. Student 2: Approved DPP, rubric draft -> ELIGIBLE
            SeedStudentProject(712, 712, supervisorA.Id, "2026/2027-1", "Approved", "{\"IsDraft\": true}");
            // 3. Student 3: Completed DPP, rubric empty -> ELIGIBLE
            SeedStudentProject(713, 713, supervisorA.Id, "2026/2027-1", "Completed", "");
            // 4. Student 4: Approved DPP, rubric finalized -> NOT eligible (already graded)
            SeedStudentProject(714, 714, supervisorA.Id, "2026/2027-1", "Approved", "{\"IsDraft\": false, \"Score\": 85}");
            // 5. Student 5: Submitted DPP, not yet approved -> NOT eligible
            SeedStudentProject(715, 715, supervisorA.Id, "2026/2027-1", "Submitted", null);
            // 6. Student 6: Approved DPP, but different session -> NOT eligible (session isolation)
            SeedStudentProject(716, 716, supervisorA.Id, "2025/2026-2", "Approved", null);
            // 7. Student 7: Approved DPP, but resigned student -> NOT eligible
            SeedStudentProject(717, 717, supervisorA.Id, "2026/2027-1", "Approved", null, isResign: true);

            // Supervisor B students in current session "2026/2027-1":
            // 8. Student 8: Approved DPP, rubric null -> belongs to Supervisor B, NOT Supervisor A
            SeedStudentProject(718, 718, supervisorB.Id, "2026/2027-1", "Approved", null);

            await context.SaveChangesAsync();

            var controllerA = CreateSupervisorController(context, supervisorA);

            var result = await controllerA.Index("2026/2027-1");
            Assert.IsType<ViewResult>(result);

            // Must count exactly the 3 eligible students for Supervisor A in session 2026/2027-1
            Assert.Equal(3, controllerA.ViewBag.PendingStage1GradingCount);
        }

        [Fact]
        public async Task Supervisor_GradeMarksheetStage1_SubmittingFinalizedRubric_RemovesStudentFromPendingReminders_WithoutMarkingPassOrUnlockingStage2()
        {
            var options = new DbContextOptionsBuilder<FYPTrackerContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new FYPTrackerContext(options);

            var supervisor = new ApplicationUser { Id = 801, FullName = "Dr. Finalize Supervisor", Email = "sup801@test.com" };
            var student = new ApplicationUser { Id = 802, FullName = "Student Awaiting Grading", Email = "stu802@test.com", IsResign = false };
            var role = new AppRole { Id = 18, Name = "Supervisor", Description = "Supervisor" };
            context.ApplicationUsers.AddRange(supervisor, student);
            context.AppRoles.Add(role);
            context.UserRoleMappings.Add(new UserRoleMapping { UserId = supervisor.Id, RoleId = role.Id });

            var project = new FYPProject
            {
                Id = 801,
                StudentId = student.Id,
                SupervisorId = supervisor.Id,
                AcademicSession = "2026/2027-1",
                Title = "Project Awaiting Grading",
                Status = "Approved",
                Stage1Grade = null
            };
            context.FYPProjects.Add(project);

            var dppMilestone = new FYPMilestone
            {
                Id = 8001,
                FYPProjectId = project.Id,
                Title = "FYP 1: Stage 1 Final Submission (DPP)",
                MilestoneCode = "STAGE1_FINAL",
                Status = "Approved",
                SupervisorMarksheetData = null,
                Stage = 1
            };
            context.FYPMilestones.Add(dppMilestone);
            await context.SaveChangesAsync();

            // Pre-condition: IsStage1SupervisorRubricPending is TRUE
            Assert.True(GradingHelper.IsStage1SupervisorRubricPending(project, new[] { dppMilestone }));

            var controller = CreateSupervisorController(context, supervisor);

            // Valid Stage 1 Rubric data (6 criteria with valid 0-4 scores)
            string marksheetJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                IsDraft = false,
                Crit1 = 4.0,
                Crit2 = 3.5,
                Crit3 = 4.0,
                Crit4 = 3.0,
                Crit5 = 3.5,
                Crit6 = 4.0
            });

            var result = await controller.GradeMarksheetStage1(project.Id, marksheetJson, isDraft: false);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ProjectDetails", redirect.ActionName);

            // Refresh milestone and project
            var updatedMilestone = await context.FYPMilestones.FindAsync(dppMilestone.Id);
            var updatedProject = await context.FYPProjects.FindAsync(project.Id);

            // 1. Student reminder is now removed (IsStage1SupervisorRubricPending is FALSE)
            Assert.False(GradingHelper.IsStage1SupervisorRubricPending(updatedProject, new[] { updatedMilestone! }));

            // 2. Requirement 12: Stage 1 is NOT automatically marked as Passed upon supervisor grading alone
            // Examiner has not graded yet, so Stage1Grade remains null/unconfirmed
            Assert.Null(updatedProject!.Stage1Grade);

            // 3. Stage 2 remains locked until academic Pass
            var stage2Eligibility = GradingHelper.CheckStage2AcademicEligibility(updatedProject, new[] { updatedMilestone! });
            Assert.False(stage2Eligibility.IsEligible);
            Assert.True(stage2Eligibility.IsPendingGrading);
        }

        #endregion

        private static SupervisorController CreateSupervisorController(FYPTrackerContext context, ApplicationUser supervisor)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, supervisor.Id.ToString()),
                new Claim("Id", supervisor.Id.ToString()),
                new Claim(ClaimTypes.Name, supervisor.FullName ?? "Test Supervisor"),
                new Claim(ClaimTypes.Email, supervisor.Email ?? "sup@test.com"),
                new Claim(ClaimTypes.Role, "Supervisor")
            };

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            };

            var controller = new SupervisorController(
                context,
                new DummyEmailService(),
                new DummyHubContext(),
                null!,
                null!,
                new DummyWeeklyMeetingSchedulingService())
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };

            return controller;
        }

        private class DummyEmailService : FYPTracker.Services.Others.Interfaces.IEmailService
        {
            public void Send(string to, string subject, string html, string? filePath = null, string? from = null, string? cc = null) { }
            public Task Send(List<string> to, string subject, string html, string? filePath = null, string? from = null) => Task.CompletedTask;
        }

        private class DummyHubContext : Microsoft.AspNetCore.SignalR.IHubContext<FYPTracker.Hubs.NotificationHub>
        {
            public Microsoft.AspNetCore.SignalR.IHubClients Clients => new DummyHubClients();
            public Microsoft.AspNetCore.SignalR.IGroupManager Groups => throw new NotImplementedException();
        }

        private class DummyHubClients : Microsoft.AspNetCore.SignalR.IHubClients
        {
            public Microsoft.AspNetCore.SignalR.IClientProxy All => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy Client(string connectionId) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy Clients(IReadOnlyList<string> connectionIds) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy Group(string groupName) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy Groups(IReadOnlyList<string> groupNames) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy User(string userId) => new DummyClientProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy Users(IReadOnlyList<string> userIds) => new DummyClientProxy();
        }

        private class DummyClientProxy : Microsoft.AspNetCore.SignalR.IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class DummyWeeklyMeetingSchedulingService : FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService
        {
            public Task<int?> GetMeetingDurationMinutesAsync() => Task.FromResult<int?>(30);
            public Task<(bool HasConflict, string ErrorMessage)> ValidateMeetingConflictAsync(int supervisorId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime, int academicSessionId, int? ignoreMeetingId = null) => Task.FromResult((false, ""));
            public Task<string?> ValidateMeetingConflictAsync(int supervisorId, DateTime startDateTime, DateTime endDateTime, int? excludeMeetingId = null, int? sessionId = null) => Task.FromResult<string?>(null);
            public Task<(int ScheduledCount, int ConflictCount, string Message)> GenerateWeeklyScheduleAsync(int studentId, int supervisorId, int meetingId) => Task.FromResult((0, 0, ""));
            public Task<List<FYPTracker.Models.OverrideMeetingConflictDto>> DetectOverrideMeetingConflictsAsync(int supervisorId, DateTime overrideDate, TimeSpan overrideStart, TimeSpan overrideEnd, bool isAvailable, int? overrideId = null) => Task.FromResult(new List<FYPTracker.Models.OverrideMeetingConflictDto>());
            public Task<List<FYPTracker.Models.OverrideMeetingConflictDto>> GetSupervisorActiveOverrideMeetingConflictsAsync(int supervisorId) => Task.FromResult(new List<FYPTracker.Models.OverrideMeetingConflictDto>());
            public Task<List<FYPTracker.Models.OverrideMeetingConflictDto>> GetStudentActiveOverrideMeetingConflictsAsync(int studentId) => Task.FromResult(new List<FYPTracker.Models.OverrideMeetingConflictDto>());
        }

        private class TestTempDataProvider : ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }
    }
}
