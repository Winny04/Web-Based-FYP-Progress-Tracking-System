using eForm_dotnetCore.Models;
using eForm_dotnetCore.ViewModel;

namespace eForm_dotnetCore.ViewModels
{
    public class BOMExcelModel
    {
        public class BOMDateZone
        {
            public string DocumentNo { get; set; }
            public string RevisionNo { get; set; }
            public string EffectiveDate { get; set; }
            public string CreatedDate { get; set; }
            public string LastUpdatedDate { get; set; }
        }

        public class ProjectZone
        {
            public string ProjectCode { get; set; }
            public string Customer { get; set; }
            public string ProjectPoNo { get; set; }
            public string ProjectCategory { get; set; }
            public int ProjectQuantity { get; set; }
            public int BudgetMaterialCost { get; set; }
            public int ActualMaterialCost { get; set; }
            public string BuildBy { get; set; }
        }

        public class BOMZone2
        {
            public DateTime? UpdatedDate { get; set; }
            public string? Status { get; set; }
            public DateTime? ReceivedDate { get; set; }
            public string? Module { get; set; }
            public string? Description { get; set; }
            public string? Brand { get; set; }
            public string? Category { get; set; }
            public string? PartNo { get; set; } = "[empty]";
            public int? UnitQuantity { get; set; }
            public int? TotalQuantity { get; set; }
            public DateTime? BuyerLastSubmittedDate { get; set; }
            public DateTime? RfqUpdatedDate { get; set; }
            public string RfqNo { get; set; }
            public DateTime? PrUpdatedDate { get; set; }
            public string PrNo { get; set; }
            public double? UnitPrice { get; set; }
            public double? TotalPrice { get; set; }
            public string VendorName { get; set; }
            public string? VendorQuotation { get; set; }
            public string? PoNo { get; set; }
            public DateTime? PoDate { get; set; }
            public int? PoQuantity { get; set; }
            public string? EstimateDeliveryDate { get; set; }
        }

        public class BOMZone
        {
            public DateTime? UpdatedDate { get; set; }
            public int BomLineNo { get; set; }
            public string? Status { get; set; }
            public string? Module { get; set; }
            public string? Description { get; set; }
            public string? Brand { get; set; }
            public string? Category { get; set; }
            public string? PartNo { get; set; }
            public int? UnitQuantity { get; set; }
            public int? TotalQuantity { get; set; }
            public DateTime? BuyerLastSubmittedDate { get; set; }
            public DateTime? RfqUpdatedDate { get; set; }
            public string RfqNo { get; set; }
            public DateTime? PrUpdatedDate { get; set; }
            public string PrNo { get; set; }
            public double? UnitPrice { get; set; }
            public double? TotalPrice { get; set; }
            public string VendorName { get; set; }
            public string? VendorQuotation { get; set; }
            public string? QuotationLineNo { get; set; }
            public string? PoNo { get; set; }
            public int? PoQuantity { get; set; }
            public string? EstimateDeliveryDate { get; set; }
            public string? DoNo { get; set; }
            public string InvoiceNo { get; set; }
            public DateTime? ReceivedDate { get; set; }
            public int? ReceivedQuantity { get; set; }
            public int? BalanceToIssueOutQuantity { get; set; }
            public string? GrnNo { get; set; }
        }
    }
}
