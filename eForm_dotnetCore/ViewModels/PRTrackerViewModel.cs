namespace eForm_dotnetCore.ViewModels
{
    public class PRTrackerViewModel
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public string? BomModule { get; set; }
        public string? Remark { get; set; }
        public int RfqId { get; set; }
        public string PrName { get; set; }
        public string Requestor { get; set; }
        public string Department { get; set; }
        public string Customer { get; set; }
        public string CustomerPO { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectCode { get; set; }
        public double PRTotalPrice { get; set; }
        public string ProjectCategory { get; set; }
        public string RFQName { get; set; }
        public string SupplierName { get; set; }
        public string Quotation { get; set; }
        public string Status { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }

    }
}
