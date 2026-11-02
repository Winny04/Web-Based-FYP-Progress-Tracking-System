using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("Specialization")]
    public class Specialization
    {
        public int Id { get; set; }
        
        [StringLength(100)]
        [Column("SpecializationName")]
        public string Name { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
