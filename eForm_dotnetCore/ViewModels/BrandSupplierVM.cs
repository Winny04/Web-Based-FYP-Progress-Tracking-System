using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class BrandSupplierVM
    {
        public Guid BrandId { get; set; }
        public string Brand { get; set; }
        public List<BrandSupplier> BrandSuppliers { get; set; }
    }
}
