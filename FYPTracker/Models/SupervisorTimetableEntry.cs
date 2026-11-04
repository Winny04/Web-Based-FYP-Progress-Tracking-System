using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("SupervisorTimetableEntry")]
    public class SupervisorTimetableEntry
    {
        [Key]
        public int Id { get; set; }

        public int SupervisorId { get; set; }
        
        public int AcademicSessionId { get; set; }

        [Required]
        [StringLength(20)]
        public string DayOfWeek { get; set; } // e.g., "Monday"

        public TimeSpan StartTime { get; set; }
        
        public TimeSpan EndTime { get; set; }

        [StringLength(200)]
        public string? Details { get; set; } // Class name, etc. (hidden from students)

        [ForeignKey("SupervisorId")]
        public virtual ApplicationUser Supervisor { get; set; }

        [ForeignKey("AcademicSessionId")]
        public virtual AcademicSession AcademicSession { get; set; }
    }
}
