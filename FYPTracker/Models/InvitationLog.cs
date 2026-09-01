using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("InvitationLog")]
    public class InvitationLog
    {
        [Key]
        public int Id { get; set; }

        public int? StudentId { get; set; }

        [StringLength(100)]
        public string? StudentEmail { get; set; }

        public int AdminId { get; set; }

        /// <summary>Values: Invited, Resend</summary>
        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty;

        /// <summary>Values: Sent, Failed</summary>
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = string.Empty;

        [StringLength(45)]
        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }

        [ForeignKey(nameof(AdminId))]
        public virtual ApplicationUser Admin { get; set; } = null!;
    }
}
