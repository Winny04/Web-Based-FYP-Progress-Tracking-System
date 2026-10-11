using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("IAFDefenseBooking")]
    public class IAFDefenseBooking
    {
        public IAFDefenseBooking()
        {
            HistoryRecords = new HashSet<IAFDefenseHistory>();
        }

        [Key]
        public int Id { get; set; }

        [Required]
        public int MilestoneId { get; set; }

        [Required]
        public int AttemptNumber { get; set; } = 1;

        [Required]
        public int AcademicSessionId { get; set; }

        /// <summary>
        /// Independent Examiner assigned specifically for this IAF Defense attempt.
        /// Distinct from permanent FYPProjectExaminer.
        /// </summary>
        [Required]
        public int ExaminerId { get; set; }

        public int? OriginalSupervisorId { get; set; }

        public bool IsSupervisorRequired { get; set; } = false;

        [Column(TypeName = "date")]
        public DateTime DefenseDate { get; set; }

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }

        public int DurationMinutes { get; set; } = 45;

        [Required]
        [StringLength(255)]
        public string Venue { get; set; } = string.Empty;

        /// <summary>
        /// Operational appointment status: Scheduled, Rescheduled, Cancelled, Completed
        /// </summary>
        [Required]
        [StringLength(50)]
        public string BookingStatus { get; set; } = "Scheduled";

        /// <summary>
        /// Academic evaluation outcome: Pending, AdminApproved, ChangeOfTopicRequired
        /// </summary>
        [Required]
        [StringLength(50)]
        public string AcademicOutcome { get; set; } = "Pending";

        public string? OutcomeComments { get; set; }

        public string? Remarks { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int? CreatedByUserId { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? UpdatedAt { get; set; }

        public int? UpdatedByUserId { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(MilestoneId))]
        public virtual FYPMilestone Milestone { get; set; } = null!;

        [ForeignKey(nameof(AcademicSessionId))]
        public virtual AcademicSession AcademicSession { get; set; } = null!;

        [ForeignKey(nameof(ExaminerId))]
        public virtual ApplicationUser Examiner { get; set; } = null!;

        [ForeignKey(nameof(OriginalSupervisorId))]
        public virtual ApplicationUser? OriginalSupervisor { get; set; }

        [ForeignKey(nameof(CreatedByUserId))]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(UpdatedByUserId))]
        public virtual ApplicationUser? UpdatedByUser { get; set; }

        public virtual ICollection<IAFDefenseHistory> HistoryRecords { get; set; }
    }
}
