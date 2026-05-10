namespace eForm_dotnetCore.Models
{
    public class TradeTracker
    {
        public Guid Id { get; set; }
        public DateTime? StartedDate { get; set; }
        public string? SalesPIC { get; set; }
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? PartNo { get; set; }
        public int? Quantity { get; set; }
        public string? SupportDoc { get; set; }
        public string? ChancesPO { get; set; }
        public string? StatusSource { get; set; }
        public string? Supplier { get; set; }
        public decimal? TargetPrice { get; set; }
        public decimal? FinalPrice { get; set; }
        public DateTime? TargetLeadTime { get; set; }
        public DateTime? LeadTime { get; set; }
        public string? CompetitorName { get; set; }
        public int? Warranty { get; set; }
        public string? Remark { get; set; }
        public DateTime? ClosedDate { get; set; }
        public string? StatusSales { get; set; }
        public string? CustomerPO { get; set; }
        public string? ReasonPending { get; set; }

    }
}
