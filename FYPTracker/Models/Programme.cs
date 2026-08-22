using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("Programme")]
    public class Programme
    {
        public int Id { get; set; }
        
        [Column("ProgrammeName")]
        public string Name { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
