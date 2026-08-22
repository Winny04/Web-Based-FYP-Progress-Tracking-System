using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("Specialization")]
    public class Specialization
    {
        public int Id { get; set; }
        
        [Column("SpecializationName")]
        public string Name { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
