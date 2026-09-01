using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("FYPSetting")]
    public class FYPSetting
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Key { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Value { get; set; }

        [StringLength(200)]
        public string? Label { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime UpdatedDate { get; set; } = DateTime.Now;
    }
}
