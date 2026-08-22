using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("Department")]
    public class Department
    {
        public int Id { get; set; }
        
        [Column("DepartmentName")]
        public string Name { get; set; }
        
        [Column("DepartmentCode")]
        public string? DepartmentCode { get; set; }

        public bool IsActive { get; set; } = true;

        public List<ApplicationUser> ApplicationUsers { get; set; } = new List<ApplicationUser>();
    }
}
