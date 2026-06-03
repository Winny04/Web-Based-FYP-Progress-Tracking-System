using System.ComponentModel.DataAnnotations.Schema;
using FYPTracker.Common;
using LogLevel = FYPTracker.Common.LogLevel;

namespace FYPTracker.Models
{
    public class AuditTrail
    {
        public int Id { get; set; }
        public string ShortMessage { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public int? EmpId { get; set; }
        [ForeignKey(nameof(EmpId))]
        public virtual ApplicationUser? Employee { get; set; }
        public int LogLevelId { get; set; }
        public string? FullMessage { get; set; }
        public string? ReferrerUrl { get; set; }
        public string? PageUrl { get; set; }
        public string? TableChange { get; set; }
        public string? DataBefore { get; set; }
        public string? DataAfter { get; set; }
        public DateTime CreatedOn { get; set; }

        [NotMapped]
        public LogLevel LogLevel
        {
            get => (LogLevel)LogLevelId;
            set => LogLevelId = (int)value;
        }
    }
}


