using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPFeedback")]
    public class FYPFeedback
    {
        [Key]
        public int Id { get; set; }

        public int MilestoneId { get; set; }

        public int SupervisorId { get; set; }

        public string? Comment { get; set; }

        /// <summary>e.g. A, B+, Pass, Fail, or a numeric score like 85</summary>
        [StringLength(10)]
        public string? Grade { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(MilestoneId))]
        public virtual FYPMilestone? Milestone { get; set; }

        [ForeignKey(nameof(SupervisorId))]
        public virtual ApplicationUser? Supervisor { get; set; }
    }
}


