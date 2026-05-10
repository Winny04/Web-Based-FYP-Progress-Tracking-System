using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    /// <summary>
    /// System notification for a student.
    /// Type: 'SupervisorApproval' | 'MeetingUpdate' | 'DeadlineReminder' | 'SubmissionFeedback' | 'RevisionRequest' | 'General'
    /// </summary>
    [Table("FYPNotification")]
    public class FYPNotification
    {
        [Key]
        public int Id { get; set; }

        public int RecipientId { get; set; }

        [StringLength(80)]
        public string Type { get; set; } = "General";

        [Required]
        public string Title { get; set; } = string.Empty;

        public string? Message { get; set; }

        public bool IsRead { get; set; } = false;

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? ReadDate { get; set; }

        // Navigation
        [ForeignKey(nameof(RecipientId))]
        public virtual EmployeeInfo? Recipient { get; set; }
    }
}
