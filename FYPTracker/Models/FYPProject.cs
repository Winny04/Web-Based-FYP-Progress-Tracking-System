using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPProject")]
    public class FYPProject
    {
        public FYPProject()
        {
            Milestones = new HashSet<FYPMilestone>();
        }

        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ProblemStatement { get; set; }
        public string? Objective1 { get; set; }
        public string? Objective2 { get; set; }
        public string? Objective3 { get; set; }
        public string? Scope { get; set; }
        public string? ExpectedOutcome { get; set; }
        public string? References { get; set; }

        public int StudentId { get; set; }

        public int? SupervisorId { get; set; }

        public int? ExaminerId { get; set; }

        /// <summary>Values: Proposed, Approved, InProgress, Completed, Rejected</summary>
        [StringLength(50)]
        public string Status { get; set; } = "Proposed";

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime? VivaDate { get; set; }

        public string? AcademicSession { get; set; }

        [StringLength(50)]
        public string? FinalGrade { get; set; }

        [StringLength(255)]
        public string? ExaminerMarksheetPath { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? UpdatedDate { get; set; }
        
        public bool IsGradingLocked { get; set; } = false;

        // Navigation
        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }

        [ForeignKey(nameof(SupervisorId))]
        public virtual ApplicationUser? Supervisor { get; set; }

        [ForeignKey(nameof(ExaminerId))]
        public virtual ApplicationUser? Examiner { get; set; }

        public virtual ICollection<FYPMilestone> Milestones { get; set; }
    }
}


