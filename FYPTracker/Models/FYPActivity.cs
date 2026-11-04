using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    /// <summary>
    /// Activity log for a student (feeds the "Recent Activities" section on the dashboard).
    /// Type: 'FeedbackReceived' | 'MeetingBooked' | 'MeetingApproved' | 'DocumentUploaded' | 'SupervisorAssigned' | 'MilestoneApproved' | 'General'
    /// </summary>
    [Table("FYPActivity")]
    public class FYPActivity
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        [StringLength(80)]
        public string Type { get; set; } = "General";

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }
    }
}


