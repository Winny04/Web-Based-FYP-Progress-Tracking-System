using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class SupportFileVM:SupplierSupportFile
    {
        public string FileUrl { get; set; }
    }

    public class SupplierSupportFileVM
    {
        public string SupplierId { get; set; }
        public string SupplierName { get; set; }
        public List<SupportFileVM> SupportFileVM { get; set; } = new List<SupportFileVM>();
    }
}
