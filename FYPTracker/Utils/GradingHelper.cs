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
                
                // 1. Check for direct extracted Excel score: { "Score": "75", ... }
                if (root.TryGetProperty("Score", out var sProp))
                {
                    if (sProp.ValueKind == JsonValueKind.Number && sProp.TryGetDouble(out double numScore)) return Math.Clamp(numScore, 0, 100.0);
                    if (sProp.ValueKind == JsonValueKind.String && double.TryParse(sProp.GetString(), out double parsedScore)) return Math.Clamp(parsedScore, 0, 100.0);
                }

                // 2. Official 6-Criterion Stage 1 Rubric (Version >= 2 or Crit1..6 defined)
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
                    else if (cVal.ValueKind == JsonValueKind.String && double.TryParse(cVal.GetString(), out double parsed)) scoreVal = parsed;
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
                    else if (cVal.ValueKind == JsonValueKind.String && double.TryParse(cVal.GetString(), out double parsed))
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
                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out double val))
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
    }
}
