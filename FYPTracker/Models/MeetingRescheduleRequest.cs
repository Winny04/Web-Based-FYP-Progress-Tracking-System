using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("MeetingRescheduleRequest")]
    public class MeetingRescheduleRequest
    {
        [Key]
        public int Id { get; set; }

        public int MeetingBookingId { get; set; }

        public int StudentId { get; set; }

        public int SupervisorId { get; set; }

        [Column(TypeName = "date")]
        public DateTime OriginalDate { get; set; }

        [StringLength(10)]
        public string? OriginalTime { get; set; }

        [Column(TypeName = "date")]
        public DateTime ProposedDate { get; set; }

        [StringLength(10)]
        public string? ProposedTime { get; set; }

        [Required]
        [StringLength(1000)]
        public string Reason { get; set; } = string.Empty;

        public string? SupervisorResponse { get; set; }

        /// <summary>Pending | Approved | Rejected | CounterProposed</summary>
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? RespondedDate { get; set; }

        // Navigation
        [ForeignKey(nameof(MeetingBookingId))]
        public virtual MeetingBooking? MeetingBooking { get; set; }

        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }

        [ForeignKey(nameof(SupervisorId))]
        public virtual ApplicationUser? Supervisor { get; set; }
    }
}
