using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FYPTracker.Models
{
    public class IafAttempt
    {
        public int AttemptNumber { get; set; }
        public Dictionary<string, string> FormData { get; set; } = new Dictionary<string, string>();
        public DateTime? SubmittedAt { get; set; }
        public string SupervisorFeedback { get; set; }
        public string SupervisorSignature { get; set; }
        public DateTime? SupervisorReviewedAt { get; set; }
        public string Status { get; set; }
        public string PanelComments { get; set; }
    }

    public class IafAttemptHistory
    {
        public int CurrentAttempt { get; set; } = 1;
        public List<IafAttempt> Attempts { get; set; } = new List<IafAttempt>();

        public static IafAttemptHistory ParseOrMigrate(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return new IafAttemptHistory();
            rawJson = rawJson.Trim();
            if (rawJson.EndsWith("(Post-Defence Resubmission)"))
            {
                rawJson = rawJson.Replace("(Post-Defence Resubmission)", "").Trim();
            }

            try
            {
                // Try parsing as the new history format
                var history = JsonSerializer.Deserialize<IafAttemptHistory>(rawJson);
                if (history != null && history.Attempts != null && history.Attempts.Count > 0)
                {
                    return history;
                }
            }
            catch { }

            // If it fails, it might be the old flat format
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(rawJson);
                if (dict != null)
                {
                    var history = new IafAttemptHistory { CurrentAttempt = 1 };
                    
                    // Extract signature and feedback if they somehow existed in the old dict (signature did)
                    string sig = "";
                    if (dict.ContainsKey("Supervisor Signature"))
                    {
                        sig = dict["Supervisor Signature"];
                        dict.Remove("Supervisor Signature");
                    }

                    history.Attempts.Add(new IafAttempt
                    {
                        AttemptNumber = 1,
                        FormData = dict,
                        SupervisorSignature = sig,
                        Status = "Migrated" 
                    });
                    return history;
                }
            }
            catch { }

            return new IafAttemptHistory();
        }
    }
}
