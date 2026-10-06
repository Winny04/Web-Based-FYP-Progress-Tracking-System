using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("AcademicSession")]
    public class AcademicSession
    {
        public int Id { get; set; }
        
        [Column("SessionName")]
        public string Name { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool? IsCurrent { get; set; }
    }
}
