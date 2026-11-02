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
        [StringLength(100)]
        public string? FullName { get; set; }
        [StringLength(50)]
        public string? UserName { get; set; }

        [StringLength(255)]
        public string? PassCode { get; set; }
        [StringLength(100)]
        public string? Email { get; set; }
        [StringLength(20)]
        public string? Phone { get; set; } // Shared field

        [StringLength(255)]
        public string? PushSubscriptionJson { get; set; }

        public ICollection<UserRoleMapping> UserRoles { get; set; } = new List<UserRoleMapping>();

        [StringLength(50)]
        public string? AddedBy { get; set; }
        
        [Column(TypeName = "datetime")]
        public DateTime? DateCreated { get; set; }
        
        [Column(TypeName = "datetime")]
        public DateTime? DateJoin { get; set; }
        
        public string? Image { get; set; }

        [StringLength(50)]
        public string? EmployeeId { get; set; } // We keep this name for legacy data, but it acts as StudentId (INTI ID)
        [StringLength(50)]
        public string? CoventryId { get; set; } // Added Coventry ID
        public int? DepartmentId { get; set; }
        public bool IsNotificationOn { get; set; }
        public bool IsResign { get; set; }
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public bool RequirePasswordChange { get; set; } = true;

        // --- NEW FIELDS ---
        // Student Specific
        [StringLength(100)]
        public string? Programme { get; set; }
        [StringLength(100)]
        public string? Specialization { get; set; }
        [StringLength(50)]
        public string? AcademicSession { get; set; }

        // Supervisor Specific
        [StringLength(20)]
        public string? Title { get; set; } // e.g., Dr, Prof, Mr, Ms

        public int? MaxStudentSlots { get; set; }

        // --- Invite / Onboarding ---
        [StringLength(255)]
        public string? TempPassword { get; set; }   // Stored for resend-invite scenarios
        public DateTime? AccountExpiry { get; set; } // Auto-calculated from AcademicSession

        // Microsoft Teams OAuth fields
        public string? MsAccessToken { get; set; }
        public string? MsRefreshToken { get; set; }
        public DateTime? MsTokenExpiry { get; set; }

        // MFA fields
        public bool IsMfaEnabled { get; set; } = false;
        public string? MfaSecretKey { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public Department? EmpDepartment { get; set; }
    }
}

