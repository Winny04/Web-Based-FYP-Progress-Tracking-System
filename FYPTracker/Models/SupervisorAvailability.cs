using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    public class SupervisorAvailability
    {
        [Key]
        public int Id { get; set; }

        public int SupervisorId { get; set; }

        // "WeeklyDefault" or "DateOverride"
        [Required]
        [StringLength(50)]
        public string ScheduleType { get; set; } = "WeeklyDefault";

        // e.g., "Monday", "Tuesday". Used if ScheduleType is WeeklyDefault.
        [StringLength(20)]
        public string? DayOfWeek { get; set; }

        // Used if ScheduleType is DateOverride.
        public DateTime? SpecificDate { get; set; }

        [Required]
        public TimeSpan StartTime { get; set; }

        [Required]
        public TimeSpan EndTime { get; set; }

        // True if available, false if they want to explicitly block out this time (useful for DateOverride)
        public bool IsAvailable { get; set; } = true;

        [ForeignKey("SupervisorId")]
        public virtual ApplicationUser? Supervisor { get; set; }
    }
}
