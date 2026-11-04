using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPProjectExaminer")]
    public class FYPProjectExaminer
    {
        [Key]
        public int Id { get; set; }

        public int FYPProjectId { get; set; }

        public int ExaminerId { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime AssignedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(FYPProjectId))]
        public virtual FYPProject? Project { get; set; }

        [ForeignKey(nameof(ExaminerId))]
        public virtual ApplicationUser? Examiner { get; set; }
    }
}
