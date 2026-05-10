using System.ComponentModel.DataAnnotations.Schema;
using eForm_dotnetCore.Common;
using LogLevel = eForm_dotnetCore.Common.LogLevel;

namespace eForm_dotnetCore.Models
{
    public class AuditTrail
    {
        public int Id { get; set; }
        public string ShortMessage { get; set; }
        public string? IpAddress { get; set; }
        public int? EmpId { get; set; }
        [ForeignKey(nameof(EmpId))]
        public virtual EmployeeInfo? Employee { get; set; }
        public int LogLevelId { get; set; }
        public string? FullMessage { get; set; }
        public string? ReferrerUrl { get; set; }
        public string? PageUrl { get; set; }
        public string? TableChange { get; set; }
        public string? DataBefore { get; set; }
        public string? DataAfter { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ProjectId { get; set; }
        [ForeignKey(nameof(ProjectId))]
        public virtual Project? Project { get; set; }
        //public int TypeId { get; set; }

        [NotMapped]
        public LogLevel LogLevel
        {
            get => (LogLevel)LogLevelId;
            set => LogLevelId = (int)value;
        }
    }
}
