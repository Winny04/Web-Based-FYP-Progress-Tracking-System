namespace eForm_dotnetCore.Models
{
    public class BrandSupplier
    {
        public Guid Id { get; set; }
        public Guid BrandId { get; set; }
        public Brand Brand { get; set; }
        public Guid SupplierId { get; set; }
        public SupplierInfo Supplier { get; set; }
        public bool IsPrimary { get; set; }
    }
}
