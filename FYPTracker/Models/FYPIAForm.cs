using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    public class FYPIAForm
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }
        [ForeignKey("StudentId")]
        public virtual EmployeeInfo Student { get; set; }

        // --- Industrial Information ---
        [Required]
        public string CompanyName { get; set; }
        [Required]
        public string CompanyAddress { get; set; }
        [Required]
        public string CompanySupervisorName { get; set; }
        [Required]
        public string CompanySupervisorEmail { get; set; }
        [Required]
        public string CompanySupervisorPhone { get; set; }

        // --- Placement Details ---
        [Required]
        public DateTime StartDate { get; set; }
        [Required]
        public DateTime EndDate { get; set; }
        [Required]
        public string ProjectTitle { get; set; }
        public string ProjectScope { get; set; }
        public decimal? MonthlyAllowance { get; set; }

        // --- Status ---
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        public string AdminNote { get; set; }
        public DateTime SubmittedDate { get; set; } = DateTime.Now;
        public DateTime? ProcessedDate { get; set; }
    }
}
