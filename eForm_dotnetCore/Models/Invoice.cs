using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    [Table("Invoice")] // Ensure it matches the table name in SQL
    public class Invoice
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string InvoiceNumber { get; set; }

        public DateTime UploadDate { get; set; } = DateTime.Now;

        [Required]
        [StringLength(255)]
        public string FileName { get; set; }

        [Required]
        public string FileData { get; set; } // Base64 encoded file

        [Required]
        [StringLength(100)]
        public string? UploadedBy { get; set; }

        public int DeleteStatus { get; set; } = 0; // 0 = Active, 1 = Deleted

        [StringLength(100)]
        public string? DeletedBy { get; set; }

        public DateTime? DeletedDate { get; set; }
    }
}
