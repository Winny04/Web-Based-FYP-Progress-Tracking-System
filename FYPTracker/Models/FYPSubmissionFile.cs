using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPSubmissionFile")]
    public class FYPSubmissionFile
    {
        [Key]
        public int Id { get; set; }

        public int MilestoneId { get; set; }

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        public long? FileSize { get; set; }

        public int? UploadedBy { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime UploadedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(MilestoneId))]
        public virtual FYPMilestone? Milestone { get; set; }

        [ForeignKey(nameof(UploadedBy))]
        public virtual EmployeeInfo? Uploader { get; set; }
    }
}
