using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("Department")]
    public class Department
    {
        public int Id { get; set; }
        
        [StringLength(100)]
        [Column("DepartmentName")]
        public string Name { get; set; }
        
        [StringLength(50)]
        [Column("DepartmentCode")]
        public string? DepartmentCode { get; set; }

        public bool IsActive { get; set; } = true;

        public List<ApplicationUser> ApplicationUsers { get; set; } = new List<ApplicationUser>();
    }
}
