using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class BrandRecommendVM
    {
        public Brand BRItem { get; set; }
        public ICollection<SupplierInfo> SupplierList { get; set; }
    }
}
