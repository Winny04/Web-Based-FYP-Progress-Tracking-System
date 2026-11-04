using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Models
{
    // One weekly schedule per student, academic session and stage.
    [Index(
        nameof(StudentId),
        nameof(AcademicSessionId),
        nameof(Stage),
        IsUnique = true)]
    public class WeeklyMeetingSchedule
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        public int SupervisorId { get; set; }

        public int? FYPProjectId { get; set; }

        public int AcademicSessionId { get; set; }

        public int Stage { get; set; }

        // The first meeting approved by the supervisor
        public int FirstMeetingBookingId { get; set; }

        [Column(TypeName = "date")]
        public DateTime FirstMeetingDate { get; set; }

        // Locked recurring weekday
        public DayOfWeek LockedDayOfWeek { get; set; }

        // Locked recurring time, e.g. 10:00 AM
        [Column(TypeName = "time")]
        public TimeSpan LockedTime { get; set; }

        // Next occurrence the system needs to process
        [Column(TypeName = "date")]
        public DateTime NextMeetingDate { get; set; }

        // Turn off when stage ends or schedule is cancelled
        public bool IsActive { get; set; } = true;

        // Track conflicts without confirming invalid meetings
        [Column(TypeName = "date")]
        public DateTime? LastConflictDate { get; set; }

        [StringLength(500)]
        public string? LastConflictReason { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}