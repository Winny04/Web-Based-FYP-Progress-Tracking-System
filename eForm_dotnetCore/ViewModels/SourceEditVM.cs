using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class SourceEditVM
    {
        public Sourcing Source { get; set; }
        public string? RemarkForBestPrice { get; set; }
        public string? RemarkForBestLead { get; set; }
        public List<SupplierInfoWithRemark> Suppliers { get; set; }
    }

    public class SupplierInfoWithRemark
    {
        public SupplierInfo Supplier { get; set; }
        public double? Price { get; set; }
        public bool IsSubmitted { get; set; }
        public DateTime? LeadTime { get; set; }
    }
}
