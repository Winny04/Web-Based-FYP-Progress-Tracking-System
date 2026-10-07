using System.Text.Json;

namespace FYPTracker.Utils
{
    public static class GradingHelper
    {
        public static double CalculateStage1Score(string jsonString, string role)
        {
            if (string.IsNullOrEmpty(jsonString)) return 0;
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                
                double tech = GetWeight(root, "Tech");
                double lit = GetWeight(root, "Lit");
                double meth = GetWeight(root, "Meth");
                double pres = GetWeight(root, "Pres");
                
                if (role == "Supervisor")
                {
                    double att = GetWeight(root, "Attitude");
                    return ((tech / 4.0) * 20.0) + ((lit / 4.0) * 20.0) + ((meth / 4.0) * 20.0) + ((pres / 4.0) * 20.0) + ((att / 4.0) * 20.0);
                }
                else
                {
                    // Examiner
                    return ((tech / 4.0) * 25.0) + ((lit / 4.0) * 25.0) + ((meth / 4.0) * 25.0) + ((pres / 4.0) * 25.0);
                }
            }
            catch
            {
                return 0;
            }
        }

        public static double CalculateStage2Score(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString)) return 0;
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                
                double crit1 = GetWeight(root, "Crit1"); // 10
                double crit2 = GetWeight(root, "Crit2"); // 20
                double crit3 = GetWeight(root, "Crit3"); // 10
                double crit4 = GetWeight(root, "Crit4"); // 20
                double crit5 = GetWeight(root, "Crit5"); // 5
                double crit6 = GetWeight(root, "Crit6"); // 5
                double crit7 = GetWeight(root, "Crit7"); // 30
                
                return ((crit1 / 4.0) * 10.0) +
                       ((crit2 / 4.0) * 20.0) +
                       ((crit3 / 4.0) * 10.0) +
                       ((crit4 / 4.0) * 20.0) +
                       ((crit5 / 4.0) * 5.0) +
                       ((crit6 / 4.0) * 5.0) +
                       ((crit7 / 4.0) * 30.0);
            }
            catch
            {
                return 0;
            }
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
