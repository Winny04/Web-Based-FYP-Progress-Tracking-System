using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Office2010.Excel;
using eForm_dotnetCore.ViewModels;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class Sourcing
    {

        public Guid Id { get; set; }
        public string Brand { get; set; }
        public decimal? FinalPrice { get; set; }
        public decimal? TotalPriceSave { get; set; }
        public DateTime? FinalLeadTime { get; set; }
        public int SupplierStandard { get; set; }
        public int RFQId { get; set; }
        [ForeignKey(nameof(RFQId))]
        public Rfq RFQ { get; set; }
        public Guid? SupplierId { get; set; }
        [ForeignKey(nameof(SupplierId))]
        public SupplierInfo? SelectedSupplier { get; set; }
        public Guid? BestPriceSupplierId { get; set; }
        [ForeignKey(nameof(BestPriceSupplierId))]
        public SupplierInfo? BestPriceSupplier { get; set; }
        public Guid? BestLeadTimeSupplierId { get; set; }
        [ForeignKey(nameof(BestLeadTimeSupplierId))]
        public SupplierInfo? BestLeadTimeSupplier { get; set; }
        public List<RFQSharing> RFQSharing { get; set; }
        public DateTime? ConfirmAt { get; set; }
    }
}
