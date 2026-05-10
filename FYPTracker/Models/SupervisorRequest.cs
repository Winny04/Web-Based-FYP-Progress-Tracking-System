using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    /// <summary>
    /// Represents a student's request for a supervisor.
    /// Status: 'Pending', 'Approved', 'Rejected'
    /// Auto-rejected after 7 days if still Pending.
    /// </summary>
    [Table("SupervisorRequest")]
    public class SupervisorRequest
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        public int SupervisorId { get; set; }

        /// <summary>Pending | Approved | Rejected</summary>
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public string? RequestMessage { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime RequestedDate { get; set; } = DateTime.Now;

        /// <summary>Auto-reject deadline: RequestedDate + 7 days</summary>
        [Column(TypeName = "datetime")]
        public DateTime ExpiryDate { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? RespondedDate { get; set; }

        public string? ResponseNote { get; set; }

        // Navigation
        [ForeignKey(nameof(StudentId))]
        public virtual EmployeeInfo? Student { get; set; }

        [ForeignKey(nameof(SupervisorId))]
        public virtual EmployeeInfo? Supervisor { get; set; }
    }
}
