using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("ApplicationUser")]
    public partial class ApplicationUser
    {
        [Key]
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? UserName { get; set; }

        public string? PassCode { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; } // Shared field

        [StringLength(255)]
        public string? PushSubscriptionJson { get; set; }

        public ICollection<UserRoleMapping> UserRoles { get; set; }

        [StringLength(50)]
        public string? AddedBy { get; set; }
        
        [Column(TypeName = "datetime")]
        public DateTime? DateCreated { get; set; }
        
        [Column(TypeName = "datetime")]
        public DateTime? DateJoin { get; set; }
        
        public string? Image { get; set; }
        public string? UserType { get; set; } // "Student", "Supervisor", "Admin", etc.
        

        public string? EmployeeId { get; set; } // We keep this name for legacy data, but it acts as StudentId (INTI ID)
        public string? CoventryId { get; set; } // Added Coventry ID
        public int? DepartmentId { get; set; }
        public bool IsNotificationOn { get; set; }
        public bool IsResign { get; set; }
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public bool RequirePasswordChange { get; set; } = true;

        // --- NEW FIELDS ---
        // Student Specific
        public string? Programme { get; set; }
        public string? Specialization { get; set; }
        public string? AcademicSession { get; set; }

        // Supervisor Specific
        public string? Title { get; set; } // e.g., Dr, Prof, Mr, Ms

        public int? MaxStudentSlots { get; set; }

        // --- Invite / Onboarding ---
        public string? TempPassword { get; set; }   // Stored for resend-invite scenarios
        public DateTime? AccountExpiry { get; set; } // Auto-calculated from AcademicSession

        [ForeignKey(nameof(DepartmentId))]
        public Department? EmpDepartment { get; set; }
    }
}

