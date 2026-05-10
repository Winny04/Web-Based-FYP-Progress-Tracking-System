using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class Brand
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<BrandSupplier> BrandSuppliers { get; set; } = new List<BrandSupplier>();
    }
}
