using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class AdditionalSupplier
    {
        [Key]
        public Guid Id { get; set; }
        public string VendorName { get; set; }
        public string Email { get; set; }

        [NotMapped]
        public string BrandNames { get; set; }
    }
}
