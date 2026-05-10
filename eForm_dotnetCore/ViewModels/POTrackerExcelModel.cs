using DocumentFormat.OpenXml.Vml.Spreadsheet;
using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class POTrackerViewModel
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public int RfqId { get; set; }
        public int PrId { get; set; }
        public string BomModule { get; set; }
        public string RfqName { get; set; }
        public string PrName { get; set; }
        public string Requestor { get; set; }
        public string Department { get; set; }
        public string CustomerPO { get; set; }
        public string Customer { get; set; }
        public int ProjectId { get; set; }
        public string ProjectCode { get; set; }
        public string ProjectName { get; set; }
        public string ProjectCategory { get; set; }
        public string VendorName { get; set; }
        public string VendorQuotation { get; set; }
        public DateTime? EstDeliveryDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public int ReceivedQty { get; set; }
        public int BalanceToReceiveQty { get; set; }
        public int? PoId { get; set; }
        public string POName { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double? Discount { get; set; }
        public double? Tax { get; set; }
        public double? ShippingCost { get; set; }
        public double? BankFee { get; set; }
        public double? TotalPrice { get; set; }
        public int? LeadTime { get; set; }
        public string ETA { get; set; }
        public string Remark { get; set; }
        public string Status { get; set; }
        public string Currency { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
        public DateTime? PRApproveDate { get; set; }
        public List<BomSupportFile> BomSupportFiles { get; set; }
        public bool IsVoid { get; set; }


    }
}
