using System.Text.Json;

namespace FYPTracker.Utils
{
    public static class GradingHelper
    {
        public static double CalculateStage1Score(string jsonString, string role = "Supervisor")
        {
            if (string.IsNullOrEmpty(jsonString)) return 0;
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                
                // 1. Official 6-Criterion Stage 1 Rubric (Version >= 2 or Crit1..6 defined)
                // Weights: Crit1=10, Crit2=20, Crit3=30, Crit4=20, Crit5=10, Crit6=10 (Total: 100)
                if (root.TryGetProperty("Crit1", out _) || root.TryGetProperty("Crit3", out _) || root.TryGetProperty("Crit6", out _))
                {
                    double crit1 = Math.Clamp(GetWeight(root, "Crit1"), 0, 4); // Report Format (10)
                    double crit2 = Math.Clamp(GetWeight(root, "Crit2"), 0, 4); // Introduction (20)
                    double crit3 = Math.Clamp(GetWeight(root, "Crit3"), 0, 4); // Literature Review (30)
                    double crit4 = Math.Clamp(GetWeight(root, "Crit4"), 0, 4); // Methodology (20)
                    double crit5 = Math.Clamp(GetWeight(root, "Crit5"), 0, 4); // References & Citation (10)
                    double crit6 = Math.Clamp(GetWeight(root, "Crit6"), 0, 4); // Ethics & Lifelong Learning & Sustainability (10)

                    double score = ((crit1 / 4.0) * 10.0) +
                                   ((crit2 / 4.0) * 20.0) +
                                   ((crit3 / 4.0) * 30.0) +
                                   ((crit4 / 4.0) * 20.0) +
                                   ((crit5 / 4.0) * 10.0) +
                                   ((crit6 / 4.0) * 10.0);

                    return Math.Clamp(score, 0, 100.0);
                }

                // 2. Direct extracted Excel score: { "Score": "75", ... } or { "Total": "75", ... }
                if (root.TryGetProperty("Score", out var sProp))
                {
                    if (sProp.ValueKind == JsonValueKind.Number && sProp.TryGetDouble(out double numScore)) return Math.Clamp(numScore, 0, 100.0);
                    if (sProp.ValueKind == JsonValueKind.String && double.TryParse(sProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedScore)) return Math.Clamp(parsedScore, 0, 100.0);
                }
                if (root.TryGetProperty("Total", out var tProp))
                {
                    if (tProp.ValueKind == JsonValueKind.Number && tProp.TryGetDouble(out double numTotal)) return Math.Clamp(numTotal, 0, 100.0);
                    if (tProp.ValueKind == JsonValueKind.String && double.TryParse(tProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedTotal)) return Math.Clamp(parsedTotal, 0, 100.0);
                }

                // 3. Fallback for legacy 4-5 criteria prototype if any
                double tech = Math.Clamp(GetWeight(root, "Tech"), 0, 4);
                double lit = Math.Clamp(GetWeight(root, "Lit"), 0, 4);
                double meth = Math.Clamp(GetWeight(root, "Meth"), 0, 4);
                double pres = Math.Clamp(GetWeight(root, "Pres"), 0, 4);
                
                if (role == "Supervisor")
                {
                    double att = Math.Clamp(GetWeight(root, "Attitude"), 0, 4);
                    double score = ((tech / 4.0) * 20.0) + ((lit / 4.0) * 20.0) + ((meth / 4.0) * 20.0) + ((pres / 4.0) * 20.0) + ((att / 4.0) * 20.0);
                    return Math.Clamp(score, 0, 100.0);
                }
                else
                {
                    double score = ((tech / 4.0) * 25.0) + ((lit / 4.0) * 25.0) + ((meth / 4.0) * 25.0) + ((pres / 4.0) * 25.0);
                    return Math.Clamp(score, 0, 100.0);
                }
            }
            catch
            {
                return 0;
            }
        }

        public static bool ValidateStage1RubricMarks(string jsonString, out string? errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                errorMessage = "Marksheet data cannot be empty.";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                string[] critProps = new[] { "Crit1", "Crit2", "Crit3", "Crit4", "Crit5", "Crit6" };
                foreach (var prop in critProps)
                {
                    if (!root.TryGetProperty(prop, out var cVal))
                    {
                        errorMessage = $"Missing scoring criterion {prop}.";
                        return false;
                    }

                    double scoreVal;
                    if (cVal.ValueKind == JsonValueKind.Number) scoreVal = cVal.GetDouble();
                    else if (cVal.ValueKind == JsonValueKind.String && double.TryParse(cVal.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) scoreVal = parsed;
                    else
                    {
                        errorMessage = $"Invalid score value format for {prop}.";
                        return false;
                    }

                    if (scoreVal < 0 || scoreVal > 4)
                    {
                        errorMessage = $"Score for criterion {prop} ({scoreVal}) is outside the valid 0-4 rubric range.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Marksheet format error: {ex.Message}";
                return false;
            }
        }

        public static bool IsMarksheetDraft(string? jsonString)
        {
            if (string.IsNullOrEmpty(jsonString)) return false;
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                if (doc.RootElement.TryGetProperty("IsDraft", out var dProp))
                {
                    if (dProp.ValueKind == JsonValueKind.True || dProp.ValueKind == JsonValueKind.False) return dProp.GetBoolean();
                    if (dProp.ValueKind == JsonValueKind.String && bool.TryParse(dProp.GetString(), out bool b)) return b;
                }
                return false;
            }
            catch { return false; }
        }

        public static double? CalculateStage2Score(string jsonString, string role = "Supervisor")
        {
            if (string.IsNullOrEmpty(jsonString)) return null;
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                // Check versioning
                int version = 1;
                if (root.TryGetProperty("Version", out var vProp))
                {
                    if (vProp.ValueKind == JsonValueKind.Number && vProp.TryGetInt32(out int vNum)) version = vNum;
                    else if (vProp.ValueKind == JsonValueKind.String && int.TryParse(vProp.GetString(), out int vParsed)) version = vParsed;
                }

                string scale = "";
                if (root.TryGetProperty("Scale", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                {
                    scale = sProp.GetString() ?? "";
                }

                double crit1 = GetWeight(root, "Crit1");
                double crit2 = GetWeight(root, "Crit2");
                double crit3 = GetWeight(root, "Crit3");
                double crit4 = GetWeight(root, "Crit4");
                double crit5 = GetWeight(root, "Crit5");
                double crit6 = GetWeight(root, "Crit6");
                double crit7 = GetWeight(root, "Crit7");

                // Version 2 submissions are explicitly standardized on Rubric0-4
                if (version >= 2 || scale == "Rubric0-4")
                {
                    double score = ((Math.Clamp(crit1, 0, 4) / 4.0) * 10.0) +
                                   ((Math.Clamp(crit2, 0, 4) / 4.0) * 20.0) +
                                   ((Math.Clamp(crit3, 0, 4) / 4.0) * 10.0) +
                                   ((Math.Clamp(crit4, 0, 4) / 4.0) * 20.0) +
                                   ((Math.Clamp(crit5, 0, 4) / 4.0) * 5.0) +
                                   ((Math.Clamp(crit6, 0, 4) / 4.0) * 5.0) +
                                   ((Math.Clamp(crit7, 0, 4) / 4.0) * 30.0);
                    return Math.Clamp(score, 0, 100.0);
                }

                // Historical / Legacy handling by Role:
                if (role == "Supervisor")
                {
                    // Supervisors historically always evaluated on the 0-4 slider rubric
                    double score = ((Math.Clamp(crit1, 0, 4) / 4.0) * 10.0) +
                                   ((Math.Clamp(crit2, 0, 4) / 4.0) * 20.0) +
                                   ((Math.Clamp(crit3, 0, 4) / 4.0) * 10.0) +
                                   ((Math.Clamp(crit4, 0, 4) / 4.0) * 20.0) +
                                   ((Math.Clamp(crit5, 0, 4) / 4.0) * 5.0) +
                                   ((Math.Clamp(crit6, 0, 4) / 4.0) * 5.0) +
                                   ((Math.Clamp(crit7, 0, 4) / 4.0) * 30.0);
                    return Math.Clamp(score, 0, 100.0);
                }
                else
                {
                    // Examiner: Unversioned historical record.
                    // Do NOT guess or use `crit > 4` as the only detector because low-scoring records (all <= 4)
                    // would be miscalculated if treated as 0-4 rubric ratings.
                    // Return null to signal that the historical grading format is ambiguous and requires review.
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool IsLegacyUnversionedExaminerRecord(string? jsonString)
        {
            if (string.IsNullOrEmpty(jsonString)) return false;
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                if (root.TryGetProperty("Version", out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int vNum) && vNum >= 2) return false;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(vGetString(v), out int vParsed) && vParsed >= 2) return false;
                }
                if (root.TryGetProperty("Scale", out var s) && s.GetString() == "Rubric0-4") return false;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static string? vGetString(JsonElement el) => el.GetString();

        public static bool ValidateStage2Version2ExaminerMarks(string jsonString, out string? errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                errorMessage = "Marksheet data cannot be empty.";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                // Validate Version >= 2 and Scale == Rubric0-4
                int version = 0;
                if (root.TryGetProperty("Version", out var vProp))
                {
                    if (vProp.ValueKind == JsonValueKind.Number && vProp.TryGetInt32(out int vNum)) version = vNum;
                    else if (vProp.ValueKind == JsonValueKind.String && int.TryParse(vProp.GetString(), out int vParsed)) version = vParsed;
                }

                string scale = "";
                if (root.TryGetProperty("Scale", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                {
                    scale = sProp.GetString() ?? "";
                }

                if (version < 2 || scale != "Rubric0-4")
                {
                    errorMessage = "Invalid marksheet version or grading scale. Expected Version 2 with 'Rubric0-4'.";
                    return false;
                }

                // Validate each criterion is within 0..4
                string[] critProps = new[] { "Crit1", "Crit2", "Crit3", "Crit4", "Crit5", "Crit6", "Crit7" };
                foreach (var prop in critProps)
                {
                    if (!root.TryGetProperty(prop, out var cVal))
                    {
                        errorMessage = $"Missing scoring criterion {prop}.";
                        return false;
                    }

                    double scoreVal;
                    if (cVal.ValueKind == JsonValueKind.Number)
                    {
                        scoreVal = cVal.GetDouble();
                    }
                    else if (cVal.ValueKind == JsonValueKind.String && double.TryParse(cVal.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        scoreVal = parsed;
                    }
                    else
                    {
                        errorMessage = $"Invalid score value format for {prop}.";
                        return false;
                    }

                    if (scoreVal < 0 || scoreVal > 4)
                    {
                        errorMessage = $"Score for criterion {prop} ({scoreVal}) is outside the valid 0-4 rubric range.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Marksheet format error: {ex.Message}";
                return false;
            }
        }

        public static double? CalculateJointFinalPercentage(double? supervisorNormalizedPercentage, double? examinerNormalizedPercentage)
        {
            if (!supervisorNormalizedPercentage.HasValue || !examinerNormalizedPercentage.HasValue) 
                return null;

            double sup = Math.Clamp(supervisorNormalizedPercentage.Value, 0, 100.0);
            double ex = Math.Clamp(examinerNormalizedPercentage.Value, 0, 100.0);

            // Official formula: (0.8 * SupervisorPercentage + 0.7 * ExaminerPercentage) / 1.5
            double totalRaw = (sup * 0.8) + (ex * 0.7);
            return Math.Round(totalRaw / 1.5, 2);
        }

        public static string DetermineFinalGradeBand(double score)
        {
            if (score >= 80.0) return "Excellent";
            if (score >= 60.0) return "Good";
            if (score >= 40.0) return "Average";
            return "Fail"; // Scores below 40% are Fail (Poor band subsumed into Fail)
        }

        private static double GetWeight(JsonElement element, string propName)
        {
            if (element.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetDouble();
                }
            }
            return 0;
        }

        public static Models.FYPMilestone? GetStage1FinalMilestone(IEnumerable<Models.FYPMilestone>? milestones)
        {
            if (milestones == null) return null;

            // 1. Precise match on canonical MilestoneCode
            var byCode = milestones.FirstOrDefault(m => string.Equals(m.MilestoneCode, "STAGE1_FINAL", StringComparison.OrdinalIgnoreCase));
            if (byCode != null) return byCode;

            // 2. Specific title match for Stage 1 Final Submission (ignoring Turnitin Checking)
            var byTitle = milestones.FirstOrDefault(m => 
                !string.IsNullOrEmpty(m.Title) &&
                m.Title.Contains("Stage 1 Final Submission", StringComparison.OrdinalIgnoreCase));
            if (byTitle != null) return byTitle;

            // 3. Fallback DPP match excluding Turnitin
            return milestones.FirstOrDefault(m =>
                !string.IsNullOrEmpty(m.Title) &&
                m.Title.Contains("DPP", StringComparison.OrdinalIgnoreCase) &&
                !m.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase));
        }

        public static Models.FYPMilestone? GetIafMilestone(IEnumerable<Models.FYPMilestone>? milestones)
        {
            if (milestones == null) return null;

            // 1. Precise match on canonical MilestoneCode
            var byCode = milestones.FirstOrDefault(m => string.Equals(m.MilestoneCode, "IAF", StringComparison.OrdinalIgnoreCase));
            if (byCode != null) return byCode;

            // 2. Specific title match for Initial Project Allocation Form (IAF) excluding Industrial
            return milestones.FirstOrDefault(m =>
                !string.IsNullOrEmpty(m.Title) &&
                (m.Title.Contains("Initial Project Allocation", StringComparison.OrdinalIgnoreCase) ||
                 m.Title.Contains("IAF", StringComparison.OrdinalIgnoreCase)) &&
                !m.Title.Contains("Industrial", StringComparison.OrdinalIgnoreCase));
        }

        public static bool CanProgressToStage1Dpp(Models.FYPMilestone? iafMilestone, Models.IAFDefenseBooking? activeBooking = null)
        {
            if (iafMilestone == null) return false;

            // If an active booking exists, it must have passed (AdminApproved academic outcome).
            // A scheduled, rescheduled, or pending booking must never be interpreted as passed.
            if (activeBooking != null)
            {
                if (!string.Equals(activeBooking.AcademicOutcome, "AdminApproved", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // IAF Milestone itself must be formally AdminApproved, Approved, or Completed
            return string.Equals(iafMilestone.Status, "AdminApproved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(iafMilestone.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(iafMilestone.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates mandatory prerequisites for Stage 1 Final DPP Submission:
        /// 1. IAF must be passed/AdminApproved (and booking passed if defense occurred).
        /// 2. At least one Supervisor-assigned Stage 1 DPP progress task must exist on the project.
        /// 3. All assigned Stage 1 DPP progress tasks must be completed and approved by the assigned Supervisor.
        /// Tasks that are Not Started, In Progress, Submitted/Pending Review, Returned for Revision, or Rejected do NOT satisfy approval.
        /// </summary>
        public static (bool Allowed, string Reason, bool HasTasks, bool AllApproved) CanSubmitStage1FinalDpp(
            IEnumerable<Models.FYPMilestone>? projectMilestones,
            Models.FYPMilestone? iafMilestone = null,
            Models.IAFDefenseBooking? activeBooking = null)
        {
            var milestonesList = projectMilestones?.ToList() ?? new List<Models.FYPMilestone>();
            if (iafMilestone == null)
            {
                iafMilestone = GetIafMilestone(milestonesList);
            }

            // 1. Gate: IAF Approval & Defense Outcome
            if (!CanProgressToStage1Dpp(iafMilestone, activeBooking))
            {
                string reason = iafMilestone == null ? "IAF milestone not found." :
                    (iafMilestone.Status == "DefenseScheduled" ? "Your IAF Defense session is scheduled and awaiting assessment." :
                    (iafMilestone.Status == "DefenseRequired" ? "IAF Defense is required and awaiting scheduling." :
                    (iafMilestone.Status == "ChangeOfTopicRequired" ? "IAF Defense was unsuccessful; change of topic is required." :
                    $"IAF is currently in '{iafMilestone.Status}' status and has not been approved.")));

                return (false, $"Cannot submit Stage 1 Proposal. {reason}", false, false);
            }

            // 2. Gate: Require at least one valid Supervisor-assigned Stage 1 DPP progress task
            var stage1Tasks = milestonesList
                .Where(m => m.MilestoneSource == "Supervisor" && m.Stage == 1)
                .ToList();

            if (!stage1Tasks.Any())
            {
                return (false, "DPP Submission Locked — Waiting for your Supervisor to assign Stage 1 DPP progress tasks.", false, false);
            }

            // 3. Gate: Require all mandatory Stage 1 DPP progress tasks to be completed and approved
            bool isTaskApproved(Models.FYPMilestone m) =>
                string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Status, "Approved", StringComparison.OrdinalIgnoreCase);

            if (!stage1Tasks.All(isTaskApproved))
            {
                return (false, "DPP Submission Locked — Waiting for Supervisor approval of all required Stage 1 progress tasks.", true, false);
            }

            return (true, string.Empty, true, true);
        }


        public static bool IsStage1DppApproved(Models.FYPMilestone? milestone)
        {
            if (milestone == null) return false;
            return string.Equals(milestone.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(milestone.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if an assigned project is eligible for Stage 1 supervisor rubric grading and is currently pending evaluation.
        /// Academic Prerequisites:
        /// 1. Project must be active (not null, not Completed, Archived, RepeatRequested, or Failed).
        /// 2. Stage 1 Final Submission (DPP) milestone must exist and be approved (IsStage1DppApproved).
        /// 3. Supervisor rubric marksheet has NOT been validly submitted (empty, null, or saved as draft).
        /// </summary>
        public static bool IsStage1SupervisorRubricPending(Models.FYPProject? project, IEnumerable<Models.FYPMilestone>? milestones = null)
        {
            if (project == null) return false;
            if (string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, "Archived", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, "RepeatRequested", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var msList = (milestones ?? project.Milestones)?.ToList() ?? new List<Models.FYPMilestone>();
            var s1Final = GetStage1FinalMilestone(msList);
            if (s1Final == null || !IsStage1DppApproved(s1Final))
            {
                return false;
            }

            string? supMarks = s1Final.SupervisorMarksheetData;
            if (string.IsNullOrWhiteSpace(supMarks) || IsMarksheetDraft(supMarks))
            {
                return true;
            }

            return false;
        }

        public static bool IsStage1ProposalLocked(Models.FYPMilestone? milestone, string? stage1Grade)
        {
            if (string.Equals(stage1Grade, "Pass", StringComparison.OrdinalIgnoreCase)) return true;
            if (milestone == null) return false;
            return string.Equals(milestone.Status, "Submitted", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(milestone.Status, "UnderReview", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(milestone.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(milestone.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates academic eligibility to access Stage 2 or submit Stage 2 deliverables:
        /// 1. Project must be active and not archived, completed, or in RepeatRequested status.
        /// 2. If Stage 1 Grade is 'Pass', Stage 2 is unlocked (unless IAF attempt >= 2 is exhausted).
        /// 3. If Stage 1 Grade is 'Fail', academic exception allows proceeding concurrently with Stage 2
        ///    while remediating Stage 1 deliverables, provided IAF was valid/approved and not exhausted.
        /// 4. If Stage 1 Grade is Pending / Not Graded (null, empty, or unassessed), Stage 2 is strictly locked.
        /// </summary>
        public static (bool IsEligible, string Reason, bool IsPendingGrading) CheckStage2AcademicEligibility(
            Models.FYPProject? project,
            IEnumerable<Models.FYPMilestone>? projectMilestones = null,
            Models.IAFDefenseBooking? activeBooking = null)
        {
            if (project == null)
            {
                return (false, "Active project not found.", false);
            }

            if (string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "This project has already been completed and is in read-only status.", false);
            }

            if (string.Equals(project.Status, "Archived", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "This project has been archived.", false);
            }

            if (string.Equals(project.Status, "RepeatRequested", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Your repeat FYP request is currently pending Admin review. Stage 2 access is locked.", false);
            }

            var milestonesList = (projectMilestones ?? project.Milestones)?.ToList() ?? new List<Models.FYPMilestone>();
            var iafMilestone = GetIafMilestone(milestonesList);

            if (iafMilestone != null && string.Equals(iafMilestone.Status, "ChangeOfTopicRequired", StringComparison.OrdinalIgnoreCase))
            {
                int attemptNum = 1;
                if (!string.IsNullOrEmpty(iafMilestone.Remarks) && iafMilestone.Remarks.Contains("JSON: "))
                {
                    try
                    {
                        string rawJson = iafMilestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None)[1].Trim();
                        var h = Models.IafAttemptHistory.ParseOrMigrate(rawJson);
                        attemptNum = h?.CurrentAttempt ?? 1;
                    }
                    catch { }
                }

                if (attemptNum >= 2)
                {
                    return (false, "Maximum 2 IAF attempts reached. Please contact Admin regarding the FYP repeat process.", false);
                }
            }

            string? grade = project.Stage1Grade?.Trim();

            // 1. Stage 1 Pass -> Unlocks Stage 2
            if (string.Equals(grade, "Pass", StringComparison.OrdinalIgnoreCase))
            {
                return (true, string.Empty, false);
            }

            // 2. Stage 1 Fail -> Academic exception: Stage 1 Fail with valid IAF and approved Stage 1 remediation
            // may proceed concurrently with Stage 2 only when the established workflow permits it.
            if (string.Equals(grade, "Fail", StringComparison.OrdinalIgnoreCase))
            {
                if (iafMilestone != null && !CanProgressToStage1Dpp(iafMilestone, activeBooking))
                {
                    return (false, "Cannot proceed to Stage 2: Initial Project Allocation Form (IAF) is not approved.", false);
                }

                return (true, string.Empty, false);
            }

            // 3. Stage 1 Pending / Not Graded -> Strictly locked
            return (false, "Stage 1 Grading Pending — Awaiting Supervisor and Examiner Final Evaluation", true);
        }

        /// <summary>
        /// Identifies whether a milestone is an obsolete or retired Stage 2 student upload requirement.
        /// Obsolete/retired items:
        /// 1. "Marksheet Submission" — student never submits marksheets; grading is performed via online 7-criteria rubrics.
        /// 2. "Record of Supervisor Meeting Form Submission" — student meetings are logged and digitally signed; duplicate PDF upload is redundant.
        /// </summary>
        public static bool IsObsoleteOrRetiredStage2Milestone(Models.FYPMilestone? milestone)
        {
            if (milestone == null || string.IsNullOrWhiteSpace(milestone.Title)) return false;
            string t = milestone.Title.Trim();
            if (t.Contains("Marksheet", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Contains("Record of Supervisor Meeting", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Meeting Form", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(milestone.MilestoneCode, "STAGE2_MARKSHEET", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(milestone.MilestoneCode, "STAGE2_MEETING_FORM", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsStage2TurnitinMilestone(Models.FYPMilestone? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Title)) return false;
            if (IsObsoleteOrRetiredStage2Milestone(m)) return false;

            // Explicitly exclude Stage 1 milestones (e.g. Stage 1 "Turnitin Checking - DPP")
            if (m.Stage == 1) return false;
            if (m.Title.Contains("DPP", StringComparison.OrdinalIgnoreCase) && !m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase)) return false;

            // Canonical milestone code
            if (string.Equals(m.MilestoneCode, "STAGE2_TURNITIN", StringComparison.OrdinalIgnoreCase)) return true;

            // Legacy Step-based titles (e.g. "Step 1: Turnitin Checking" or "Step 1: Turnitin")
            if (m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase) && m.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase)) return true;

            // Explicit Stage 2 classification
            if (m.Stage == 2 && m.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase)) return true;

            // Fallback for non-Stage-1 records
            return m.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase) && m.Stage != 1;
        }

        public static bool IsStage2FinalReportMilestone(Models.FYPMilestone? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Title)) return false;
            if (IsObsoleteOrRetiredStage2Milestone(m)) return false;

            // Explicitly exclude Stage 1 milestones
            if (m.Stage == 1) return false;
            if (m.Title.Contains("Stage 1", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Proposal", StringComparison.OrdinalIgnoreCase)) return false;

            // Canonical milestone code
            if (string.Equals(m.MilestoneCode, "STAGE2_FINAL", StringComparison.OrdinalIgnoreCase)) return true;

            // Legacy Step 2 titles
            if (m.Title.StartsWith("Step 2", StringComparison.OrdinalIgnoreCase) && !m.Title.Contains("Turnitin", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase) && 
                (m.Title.Contains("Final Report", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("DPP report full", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Dissertation", StringComparison.OrdinalIgnoreCase))) return true;

            // Explicit Stage 2 classification
            if (m.Stage == 2 && 
                (m.Title.Contains("Final Report", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("DPP report full", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Thesis", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Dissertation", StringComparison.OrdinalIgnoreCase))) return true;

            return (m.Title.Contains("Final Report", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("DPP report full", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Dissertation", StringComparison.OrdinalIgnoreCase)) && m.Stage != 1;
        }

        public static bool IsStage2PresentationMilestone(Models.FYPMilestone? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Title)) return false;
            if (IsObsoleteOrRetiredStage2Milestone(m)) return false;

            // Explicitly exclude Stage 1 milestones
            if (m.Stage == 1) return false;

            // Canonical milestone code
            if (string.Equals(m.MilestoneCode, "STAGE2_PRESENTATION", StringComparison.OrdinalIgnoreCase)) return true;

            // Legacy Step 5 or Step 3 presentation titles
            if (m.Title.StartsWith("Step 5", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Title.StartsWith("Step 3", StringComparison.OrdinalIgnoreCase) && !m.Title.Contains("Marksheet", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase) && 
                (m.Title.Contains("Presentation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Slide", StringComparison.OrdinalIgnoreCase))) return true;

            // Explicit Stage 2 classification
            if (m.Stage == 2 && 
                (m.Title.Contains("Presentation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Slide", StringComparison.OrdinalIgnoreCase))) return true;

            return (m.Title.Contains("Presentation", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Slide", StringComparison.OrdinalIgnoreCase)) && m.Stage != 1;
        }

        public static bool IsMilestoneApproved(Models.FYPMilestone? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Status)) return false;
            return string.Equals(m.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(m.Status, "AdminApproved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(m.Status, "SupervisorSigned", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the genuine required Stage 2 student upload milestones:
        /// Exactly 3 deliverables: Turnitin Checking, Final Report Submission, and Presentation Slides Submission.
        /// </summary>
        public static List<Models.FYPMilestone> GetRequiredStage2Milestones(IEnumerable<Models.FYPMilestone>? milestones)
        {
            var result = new List<Models.FYPMilestone>();
            if (milestones == null) return result;
            var list = milestones
                .Where(m => m.Stage != 1 && !IsObsoleteOrRetiredStage2Milestone(m))
                .ToList();

            var turnitin = list.Where(IsStage2TurnitinMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
            if (turnitin != null) result.Add(turnitin);

            var finalReport = list.Where(IsStage2FinalReportMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
            if (finalReport != null) result.Add(finalReport);

            var presentation = list.Where(IsStage2PresentationMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
            if (presentation != null) result.Add(presentation);

            return result;
        }

        /// <summary>
        /// Returns all active Stage 2 milestones excluding obsolete or retired student submission requirements.
        /// </summary>
        public static List<Models.FYPMilestone> GetActiveStage2Milestones(IEnumerable<Models.FYPMilestone>? milestones)
        {
            if (milestones == null) return new List<Models.FYPMilestone>();
            return milestones
                .Where(m => (m.Stage == 2 || (m.Stage != 1 && (m.Title.StartsWith("Step", StringComparison.OrdinalIgnoreCase) || m.MilestoneCode?.StartsWith("STAGE2_", StringComparison.OrdinalIgnoreCase) == true))) && !IsObsoleteOrRetiredStage2Milestone(m))
                .ToList();
        }

        /// <summary>
        /// Returns clean, sequential display numbering (Step 1, Step 2, Step 3) for the 3 genuine Stage 2 student upload deliverables
        /// without altering stable database milestone codes, titles, or historical records.
        /// </summary>
        public static string GetStage2DisplayTitle(Models.FYPMilestone? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Title)) return string.Empty;
            if (IsStage2TurnitinMilestone(m)) return "Step 1: Turnitin Similarity Verification";
            if (IsStage2FinalReportMilestone(m)) return "Step 2: Final Report Submission";
            if (IsStage2PresentationMilestone(m)) return "Step 3: Presentation Slides Submission";

            return m.Title;
        }

        /// <summary>
        /// Checks whether all genuine required Stage 2 student uploads are present and have been approved by the supervisor.
        /// </summary>
        public static bool AreStage2DeliverablesApproved(IEnumerable<Models.FYPMilestone>? milestones)
        {
            if (milestones == null) return false;
            var list = milestones
                .Where(m => m.Stage != 1 && !IsObsoleteOrRetiredStage2Milestone(m))
                .ToList();

            var turnitin = list.Where(IsStage2TurnitinMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
            var finalReport = list.Where(IsStage2FinalReportMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
            var presentation = list.Where(IsStage2PresentationMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();

            if (turnitin == null || finalReport == null || presentation == null)
                return false;

            return IsMilestoneApproved(turnitin) && IsMilestoneApproved(finalReport) && IsMilestoneApproved(presentation);
        }

        /// <summary>
        /// Checks whether the Stage 2 signed supervisor meeting requirement is satisfied.
        /// </summary>
        public static bool IsStage2MeetingRequirementSatisfied(int completedMeetings, int? requiredMeetings)
        {
            if (!requiredMeetings.HasValue || requiredMeetings.Value <= 0) return true;
            return completedMeetings >= requiredMeetings.Value;
        }

        /// <summary>
        /// Authoritative validation for unlocking Stage 2 Final Marksheet grading for Supervisor and Examiner:
        /// 1. Project must be active and not in an archived/terminal non-passing state.
        /// 2. Stage 1 must be passed.
        /// 3. All 3 genuine required Stage 2 student deliverables (Turnitin, Final Report, Presentation Slides) must be approved.
        /// 4. Configured required Stage 2 supervisor meeting count must be satisfied via signed digital meeting logs.
        /// </summary>
        public static (bool IsUnlocked, string Reason) CanUnlockStage2Grading(
            Models.FYPProject? project,
            int completedStage2Meetings,
            int? requiredStage2Meetings)
        {
            if (project == null)
                return (false, "Active project not found.");

            if (string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                return (false, "This project has already been completed.");

            if (string.Equals(project.Status, "Archived", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, "RepeatRequested", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "This project is not currently active for Stage 2 grading.");
            }

            if (!string.Equals(project.Stage1Grade, "Pass", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Stage 2 grading is locked: Stage 1 must be Passed first.");
            }

            if (!AreStage2DeliverablesApproved(project.Milestones))
            {
                var list = (project.Milestones ?? Enumerable.Empty<Models.FYPMilestone>())
                    .Where(m => m.Stage != 1 && !IsObsoleteOrRetiredStage2Milestone(m))
                    .ToList();
                var t = list.Where(IsStage2TurnitinMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
                var f = list.Where(IsStage2FinalReportMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();
                var p = list.Where(IsStage2PresentationMilestone).OrderByDescending(IsMilestoneApproved).FirstOrDefault();

                var pendingNames = new List<string>();
                if (!IsMilestoneApproved(t)) pendingNames.Add("Turnitin Similarity Verification");
                if (!IsMilestoneApproved(f)) pendingNames.Add("Final Report Submission");
                if (!IsMilestoneApproved(p)) pendingNames.Add("Presentation Slides Submission");

                return (false, $"Stage 2 grading is locked: All required student submissions must be approved ({string.Join(", ", pendingNames)}).");
            }

            if (!IsStage2MeetingRequirementSatisfied(completedStage2Meetings, requiredStage2Meetings))
            {
                return (false, $"Stage 2 grading is locked: Meeting requirement incomplete ({completedStage2Meetings}/{(requiredStage2Meetings ?? 10)} signed meetings completed).");
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Retrieves the latest uploaded file for the Stage 2 Final Report submission.
        /// </summary>
        public static Models.FYPSubmissionFile? GetStage2FinalReportFile(IEnumerable<Models.FYPMilestone>? milestones)
        {
            if (milestones == null) return null;
            var finalReportMilestone = milestones.FirstOrDefault(IsStage2FinalReportMilestone);
            return finalReportMilestone?.SubmissionFiles?
                .Where(f => !f.IsDeleted)
                .OrderByDescending(f => f.UploadedDate)
                .FirstOrDefault();
        }

        public static List<ViewModels.StudentFeedbackItemVM> GetAuthorizedStudentFeedback(Models.FYPProject? project)
        {
            var result = new List<ViewModels.StudentFeedbackItemVM>();
            if (project == null || project.Milestones == null) return result;

            var milestones = project.Milestones.ToList();

            // 1. Supervisor Milestone Review Remarks (SupervisorFeedback on milestones)
            foreach (var m in milestones)
            {
                if (IsObsoleteOrRetiredStage2Milestone(m)) continue;

                if (!string.IsNullOrWhiteSpace(m.SupervisorFeedback))
                {
                    // Find optional supervisor-uploaded feedback attachment if available
                    var feedbackFile = m.SubmissionFiles?
                        .Where(f => !f.IsDeleted && f.UploadedBy != project.StudentId)
                        .OrderByDescending(f => f.UploadedDate)
                        .FirstOrDefault();

                    string sourceLabel = m.Stage == 2 ? "Stage 2 Submission Feedback" : "Supervisor Submission Review";
                    string badgeColor = m.Stage == 2 ? "bg-info text-white" : "bg-primary text-white";
                    string targetUrl = m.Stage == 2 ? "/Student/Stage2" : "/Student/Forms#stage1HistorySection";
                    string milestoneTitle = m.Stage == 2 ? GetStage2DisplayTitle(m) : m.Title;

                    result.Add(new ViewModels.StudentFeedbackItemVM
                    {
                        SourceType = sourceLabel,
                        SourceBadgeClass = badgeColor,
                        IconClass = "fas fa-user-tie",
                        MilestoneTitle = milestoneTitle,
                        MilestoneStage = m.Stage,
                        FeedbackContent = m.SupervisorFeedback.Trim(),
                        FeedbackDate = m.DueDate ?? m.SubmittedDate,
                        TargetUrl = targetUrl,
                        AttachmentFileName = feedbackFile?.FileName ?? string.Empty,
                        AttachmentFileId = feedbackFile?.Id
                    });
                }
            }

            // 2. Supervisor Stage 1 Online Rubric Evaluation Feedback (when finalized)
            var stage1Milestone = GetStage1FinalMilestone(milestones);
            if (stage1Milestone != null && !string.IsNullOrWhiteSpace(stage1Milestone.SupervisorMarksheetData))
            {
                bool isDraft = IsMarksheetDraft(stage1Milestone.SupervisorMarksheetData);
                if (!isDraft)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(stage1Milestone.SupervisorMarksheetData);
                        var root = doc.RootElement;
                        var parts = new List<string>();

                        if (root.TryGetProperty("Strengths", out var sProp) && !string.IsNullOrWhiteSpace(sProp.GetString()))
                            parts.Add($"Strengths: {sProp.GetString()!.Trim()}");
                        if (root.TryGetProperty("Improvements", out var iProp) && !string.IsNullOrWhiteSpace(iProp.GetString()))
                            parts.Add($"Improvements: {iProp.GetString()!.Trim()}");
                        if (root.TryGetProperty("Comments", out var cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                            parts.Add($"Comments: {cProp.GetString()!.Trim()}");

                        if (parts.Any())
                        {
                            result.Add(new ViewModels.StudentFeedbackItemVM
                            {
                                SourceType = "Supervisor Evaluation",
                                SourceBadgeClass = "bg-primary text-white",
                                IconClass = "fas fa-clipboard-check",
                                MilestoneTitle = "Stage 1 Proposal Assessment",
                                MilestoneStage = 1,
                                FeedbackContent = string.Join(" | ", parts),
                                FeedbackDate = stage1Milestone.SubmittedDate,
                                TargetUrl = "/Student/Forms#stage1HistorySection"
                            });
                        }
                    }
                    catch { }
                }
            }

            // 3. Stage 2 Supervisor Marksheet Feedback (if recorded)
            if (!string.IsNullOrWhiteSpace(project.Stage2SupervisorMarksheetData))
            {
                try
                {
                    using var doc = JsonDocument.Parse(project.Stage2SupervisorMarksheetData);
                    if (doc.RootElement.TryGetProperty("Feedback", out var fbProp) && !string.IsNullOrWhiteSpace(fbProp.GetString()))
                    {
                        result.Add(new ViewModels.StudentFeedbackItemVM
                        {
                            SourceType = "Stage 2 Submission Feedback",
                            SourceBadgeClass = "bg-info text-white",
                            IconClass = "fas fa-clipboard-check",
                            MilestoneTitle = "Stage 2 Supervisor Evaluation",
                            MilestoneStage = 2,
                            FeedbackContent = fbProp.GetString()!.Trim(),
                            FeedbackDate = project.UpdatedDate ?? project.CreatedDate,
                            TargetUrl = "/Student/Stage2"
                        });
                    }
                }
                catch { }
            }

            // 4. Released Examiner Marksheet Feedback
            // Only released when Stage1ExaminerMarksheetPath is set or explicit ExaminerComments released
            if (!string.IsNullOrWhiteSpace(project.ExaminerComments))
            {
                result.Add(new ViewModels.StudentFeedbackItemVM
                {
                    SourceType = "Examiner Evaluation",
                    SourceBadgeClass = "bg-success text-white",
                    IconClass = "fas fa-user-graduate",
                    MilestoneTitle = "Examiner Evaluation",
                    MilestoneStage = 2,
                    FeedbackContent = project.ExaminerComments.Trim(),
                    FeedbackDate = project.UpdatedDate,
                    TargetUrl = "/Student/Stage2"
                });
            }

            // Order by most recent date descending (fallback to newest additions)
            return result
                .OrderByDescending(f => f.FeedbackDate ?? DateTime.MinValue)
                .ToList();
        }
    }
}
