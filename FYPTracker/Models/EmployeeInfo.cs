using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("EmployeeInfo")]
    public partial class EmployeeInfo
    {
        [Key]
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? UserName { get; set; }
        public string? ShortName { get; set; }
        public string? PassCode { get; set; }
        public string? Email { get; set; }
        [StringLength(50)]
        public string? AddedBy { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? DateCreated { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? DateJoin { get; set; }
        public string? Image { get; set; }
        public string? UserType { get; set; }
        
        public string? Department { get; set; }
        public string? EmployeeId { get; set; }
        public int? DepartmentId { get; set; }
        public bool IsNotificationOn { get; set; }
        public bool IsResign { get; set; }
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        [ForeignKey(nameof(DepartmentId))]
        public Department? EmpDepartment { get; set; }
    }
}
