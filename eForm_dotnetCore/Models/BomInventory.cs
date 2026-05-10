namespace eForm_dotnetCore.Models
{
    public class BomInventory
    {
        public Guid Id { get; set; }
        public string? Description { get; set; }
        public string? Supplier { get; set; }
        public string? Currency { get; set; }
        public string? Brand { get; set; }
        public string? Module { get; set; }
        public decimal UnitPrice { get; set; } = 0;
    }
}
