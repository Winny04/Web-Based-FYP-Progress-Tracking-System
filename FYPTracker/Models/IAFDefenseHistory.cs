using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("IAFDefenseHistory")]
    public class IAFDefenseHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [Required]
        public int MilestoneId { get; set; }

        [Required]
        public int AttemptNumber { get; set; }

        [Required]
        public int AcademicSessionId { get; set; }

        /// <summary>
        /// Event Action Type: InitialScheduled, Rescheduled, ExaminerChanged, Cancelled, Completed, OutcomeRecorded
        /// </summary>
        [Required]
        [StringLength(50)]
        public string ActionType { get; set; } = string.Empty;

        // Full State Snapshot at Time of Event
        public int ExaminerId { get; set; }
        public int? OriginalSupervisorId { get; set; }
        public bool IsSupervisorRequired { get; set; }

        [Column(TypeName = "date")]
        public DateTime DefenseDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int DurationMinutes { get; set; }

        [Required]
        [StringLength(255)]
        public string Venue { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string BookingStatus { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string AcademicOutcome { get; set; } = "Pending";

        public string? OutcomeComments { get; set; }
        public string? ReasonForChange { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public int? ActionByUserId { get; set; }

        // Navigation Properties (Strictly Restrict / NoAction delete behavior)
        [ForeignKey(nameof(BookingId))]
        public virtual IAFDefenseBooking Booking { get; set; } = null!;

        [ForeignKey(nameof(MilestoneId))]
        public virtual FYPMilestone Milestone { get; set; } = null!;

        [ForeignKey(nameof(AcademicSessionId))]
        public virtual AcademicSession AcademicSession { get; set; } = null!;

        [ForeignKey(nameof(ExaminerId))]
        public virtual ApplicationUser Examiner { get; set; } = null!;

        [ForeignKey(nameof(OriginalSupervisorId))]
        public virtual ApplicationUser? OriginalSupervisor { get; set; }

        [ForeignKey(nameof(ActionByUserId))]
        public virtual ApplicationUser? ActionByUser { get; set; }
    }
}
