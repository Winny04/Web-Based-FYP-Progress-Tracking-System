using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    /// <summary>
    /// Represents an administrative request sent to an independent examiner (or supervisor)
    /// to propose an alternative date/time for an IAF Defense when no mutual slots exist.
    /// Suggestions in this table are proposals only and NEVER constitute a confirmed booking.
    /// </summary>
    [Table("IAFAlternativeTimeRequest")]
    public class IAFAlternativeTimeRequest
    {
        public const string StatusPending = "Pending";
        public const string StatusSuggested = "Suggested";
        public const string StatusAccepted = "Accepted";
        public const string StatusRejected = "Rejected";
        public const string StatusCancelled = "Cancelled";

        [Key]
        public int Id { get; set; }

        [Required]
        public int MilestoneId { get; set; }

        [Required]
        public int AttemptNumber { get; set; } = 1;

        [Required]
        public int AcademicSessionId { get; set; }

        /// <summary>
        /// The lecturer from whom an alternative time suggestion is requested (typically Examiner).
        /// </summary>
        [Required]
        public int RequestedLecturerId { get; set; }

        [Required]
        [StringLength(50)]
        public string RequestedLecturerRole { get; set; } = "Examiner"; // "Examiner" or "Supervisor"

        [Required]
        public int RequestedByAdminId { get; set; }

        [StringLength(1000)]
        public string? AdminNotes { get; set; }

        // --- Lecturer Suggestion Payload (populated upon Lecturer response) ---
        [Column(TypeName = "date")]
        public DateTime? SuggestedDate { get; set; }

        public TimeSpan? SuggestedStartTime { get; set; }

        public TimeSpan? SuggestedEndTime { get; set; }

        public int? SuggestedDurationMinutes { get; set; }

        [StringLength(1000)]
        public string? LecturerNotes { get; set; }

        /// <summary>
        /// Lifecycle state: Pending, Suggested, Accepted, Rejected, Cancelled
        /// </summary>
        [Required]
        [StringLength(30)]
        public string Status { get; set; } = StatusPending;

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? RespondedAt { get; set; }


        [Column(TypeName = "datetime")]
        public DateTime? FinalizedAt { get; set; }

        public int? FinalizedByAdminId { get; set; }

        [StringLength(1000)]
        public string? DecisionReason { get; set; }


        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // Navigation properties
        [ForeignKey(nameof(MilestoneId))]
        public virtual FYPMilestone? Milestone { get; set; }

        [ForeignKey(nameof(AcademicSessionId))]
        public virtual AcademicSession? AcademicSession { get; set; }

        [ForeignKey(nameof(RequestedLecturerId))]
        public virtual ApplicationUser? RequestedLecturer { get; set; }

        [ForeignKey(nameof(RequestedByAdminId))]
        public virtual ApplicationUser? RequestedByAdmin { get; set; }

        [ForeignKey(nameof(FinalizedByAdminId))]
        public virtual ApplicationUser? FinalizedByAdmin { get; set; }
    }
}
