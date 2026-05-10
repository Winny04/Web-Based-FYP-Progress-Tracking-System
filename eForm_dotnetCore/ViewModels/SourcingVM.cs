namespace eForm_dotnetCore.ViewModels
{
    public class SourcingVM
    {
        public Guid Id { get; set; }
        public int RFQId { get; set; }
        public decimal? TotalPriceSave { get; set; }
        public string RFQName { get; set; }
        public int TotalSend { get; set; }
        public int TotalResponse { get; set; }
        public string Brand { get; set; }
        public decimal? FinalPrice { get; set; }
        public DateTime? FinalLeadTime { get; set; }
        public string? Supplier { get; set; }
        public string Status { get; set; }
        public int BOMId { get; set; }

    }
}
