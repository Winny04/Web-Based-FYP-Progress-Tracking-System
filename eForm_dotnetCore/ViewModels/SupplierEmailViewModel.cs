using eForm_dotnetCore.Models;
using System.Collections.Generic;

namespace eForm_dotnetCore.ViewModels
{
    public class SupplierEmailViewModel
    {
        public List<SupplierDTO> Suppliers { get; set; }
        public List<SupplierDTO> AddedSuppliers { get; set; }
    }

    public class SupplierDTO
    {
        public Guid Id { get; set; }
        public string VendorName { get; set; }
        public string Email { get; set; }
        public string BrandNames { get; set; }
    }
}
