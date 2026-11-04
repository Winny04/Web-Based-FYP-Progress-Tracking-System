using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("DefaultMilestoneTemplates")]
    public class DefaultMilestoneTemplate
    {
        public int Id { get; set; }

        public int AcademicSessionId { get; set; }
        public virtual AcademicSession Session { get; set; }

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [StringLength(50)]
        public string MilestoneCode { get; set; } = "GENERIC";

        public int Stage { get; set; } = 1;

        public bool IsRequired { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }

        public DateTime? DueDate { get; set; }
    }
}
