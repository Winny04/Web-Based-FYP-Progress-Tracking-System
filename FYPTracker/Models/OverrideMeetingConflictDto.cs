using System;

namespace FYPTracker.Models
{
    public class OverrideMeetingConflictDto
    {
        public int MeetingId { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int SupervisorId { get; set; }
        public DateTime MeetingDate { get; set; }
        public string MeetingTime { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? OverrideId { get; set; }
        public string ConflictReason { get; set; } = string.Empty;
        public bool IsLegacyDurationUnverified { get; set; }
    }
}
