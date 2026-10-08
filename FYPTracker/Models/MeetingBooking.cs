using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    /// <summary>
    /// Represents a meeting booking request from a student to their supervisor.
    /// Status: 'Pending', 'Approved', 'Rejected', 'Postponed'
    /// </summary>
    [Table("MeetingBooking")]
    public class MeetingBooking
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        public int SupervisorId { get; set; }

        public int? FYPProjectId { get; set; }
        public int? Stage { get; set; }
        public int? WeekNumber { get; set; }
        public int? AcademicSessionId { get; set; }

        [Column(TypeName = "date")]
        public DateTime MeetingDate { get; set; }

        [StringLength(20)]
        public string? MeetingDay { get; set; }

        [StringLength(10)]
        public string? MeetingTime { get; set; }

        public string? Reason { get; set; }

        /// <summary>Pending | Approved | Rejected | Postponed</summary>
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public string? SupervisorNote { get; set; }

        [StringLength(255)]
        public string? Location { get; set; }

        /// <summary>If Postponed, suggested new date by supervisor</summary>
        [Column(TypeName = "date")]
        public DateTime? ProposedNewDate { get; set; }

        public string? MeetingLink { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? RespondedDate { get; set; }

        public bool ReminderSent { get; set; } = false;

        // Navigation
        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }

        [ForeignKey(nameof(SupervisorId))]
        public virtual ApplicationUser? Supervisor { get; set; }

        public virtual MeetingLog? MeetingLog { get; set; }
    }
}


