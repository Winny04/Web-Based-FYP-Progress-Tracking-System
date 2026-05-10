using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class ReceivingTrackerViewModel
    {
        public List<OverEtaItemModel> OverETAItemList { get; set; }
        public List<ReceivingModel> ReceivingList { get; set; }
    }

    public class ReceivingModel
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public int PrId { get; set; }
        public string PrName { get; set; }
        public int PrLineNo { get; set; }
        public string Requestor { get; set; }
        public string? ItemCategory { get; set; }
        public string Customer { get; set; }
        public string Department { get; set; }
        public string CustomerPO { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectCode { get; set; }
        public string ProjectCategory { get; set; }
        public int PoId { get; set; }
        public string POName { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double TotalPrice { get; set; }
        public string ETA { get; set; }
        public int rowIndex { get; set; }
        public bool vendorIsSelected { get; set; }
        public DateTime? EstDeliveryDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public int? ReceivedQuantity { get; set; }
        public int? ReceivedQuantity2 { get; set; }
        public int BalanceToReceivedQty { get; set; }
        public BomSupportFile PO { get; set; }
        public List<BomSupportFile> POLine { get; set; }
        public List<BomSupportFile> DO { get; set; }
        public List<BomSupportFile> Invoice { get; set; }
        public List<BomSupportFile> GRN { get; set; }
        public List<Pr> PR { get; set; }
        public List<BomSupportFile> QUO { get; set; }
        public string IssuedPIC { get; set; }
        public string IssuedPICId { get; set; }
        public string VendorName { get; set; }
        public string VendorQuotation { get; set; }
        public int? IssuedQty { get; set; }
        public int? IssuedQty2 { get; set; }
        public int BalanceToIssueOutQty { get; set; }
        public DateTime? IssueOutDate { get; set; }
        public string Status { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
        public string? Description { get; set; }
        public string? brand { get; set; }
        public string? PartNo { get; set; }
        public string? remark { get; set; }
        public string? POLineNo { get; set; }
        public string? IssueStatus { get; set; }
        public bool isPICs { get; set; }
        public bool hasIssueOut { get; set; }
        public List<EmployeeInfo> EmployeeList { get; set; }
        public List<string> DepartmentList { get; set; }
    }

    //Export PDF model
    public class ExportIssueOutItemsForm
    {
        public List<IssueOutItemList> ItemList { get; set; }
    }

    //OverEta Model
    public class OverEtaItemModel
    {
        public int ItemNo { get; set; }
        public string Status { get; set; }
        public string Desc { get; set; }
        public string Brand { get; set; }
        public string ProjectCode { get; set; }
        public string ProjectName { get; set; }
        public DateTime ETA { get; set; }
        public string PRNo { get; set; }
        public int PRId { get; set; }
        public double DaysOverEta { get; set; }
        //public List<BomSupportFile> PO { get; set; }
        public int? PoId { get; set; }
        public string? POName { get; set; }
    }

    public class IssueOutItemList
    {
        public int ItemId { get; set; }
        public string PartDescription { get; set; }
        public int IssueQuantity { get; set; }
        public string IssueLocation { get; set; }
        public string PONumber { get; set; }
        public int BalanceToIssueQuantity { get; set; }
        public int ReceivedQuantity { get; set; }
    }
}
