using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPMilestone")]
    public class FYPMilestone
    {
        public FYPMilestone()
        {
            Feedbacks = new HashSet<FYPFeedback>();
            SubmissionFiles = new HashSet<FYPSubmissionFile>();
        }

        [Key]
        public int Id { get; set; }

        public int FYPProjectId { get; set; }

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime? DueDate { get; set; }

        public DateTime? SubmittedDate { get; set; }

        /// <summary>Values: Pending, Submitted, UnderReview, Approved, Rejected</summary>
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public string? Remarks { get; set; }

        public string? SupervisorFeedback { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int RejectionCount { get; set; } = 0;

        /// <summary>Values: Default (system-seeded) or Supervisor (added via Add Milestone).</summary>
        [StringLength(20)]
        public string MilestoneSource { get; set; } = "Default";

        /// <summary>FYP stage this milestone belongs to: 1 or 2.</summary>
        public int Stage { get; set; } = 1;

        // --- Joint Marking Fields ---
        public bool IsReadyForJointEvaluation { get; set; } = false;

        public string? SupervisorMarksheetData { get; set; }

        public string? ExaminerMarksheetData { get; set; }
        // ----------------------------

        [Column(TypeName = "date")]
        public DateTime? DefenseDate { get; set; }

        [StringLength(20)]
        public string? DefenseTime { get; set; }

        [StringLength(255)]
        public string? DefenseVenue { get; set; }

        // Navigation
        [ForeignKey(nameof(FYPProjectId))]
        public virtual FYPProject? Project { get; set; }

        public virtual ICollection<FYPFeedback> Feedbacks { get; set; }
        public virtual ICollection<FYPSubmissionFile> SubmissionFiles { get; set; }
    }
}
