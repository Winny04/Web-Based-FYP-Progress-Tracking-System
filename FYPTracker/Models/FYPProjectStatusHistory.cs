using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    public class FYPProjectStatusHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ProjectId { get; set; }
        [ForeignKey("ProjectId")]
        public FYPProject Project { get; set; }

        [StringLength(50)]
        public string OldStatus { get; set; }

        [StringLength(50)]
        public string NewStatus { get; set; }

        public int? ChangedById { get; set; }
        [ForeignKey("ChangedById")]
        public ApplicationUser ChangedBy { get; set; }

        public string Remarks { get; set; }

        public DateTime ChangedDate { get; set; } = DateTime.Now;
    }
}
