using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class RFQSharingRoot
    {
        public RFQSharingRoot()
        {
            data = new RFQSharingData();
        }
        public RFQSharingData data { get; set; }
    }

    public class QuotationList
    {
        public string? quotNoLink { get; set; }
        public List<Files>? quotation { get; set; }
    }

    public class RFQSharingData
    {
        public RFQSharingData()
        {
            header = new List<RFQHeader>();
            bomGrid = new List<BOMGrid>();
            quotations = new List<QuotationList>();
            attachments = new List<Files>();
        }

        public List<RFQHeader> header { get; set; }
        public List<BOMGrid> bomGrid { get; set; }
        public List<QuotationList>? quotations { get; set; } = new List<QuotationList>();
        public List<Files>? attachments { get; set; } = new List<Files>();
        public int? rfqId { get; set; }
        public string? userType { get; set; }
        public int allowEdit { get; set; }
        public int editAll { get; set; }
        public int isRequestor { get; set; }
        public int enableAddItem { get; set; }
        public int ableViewDrawing { get; set; }
        public string? userList { get; set; }
        public int? isFabrication { get; set; }
        public string? departmentList { get; set; }
        public string projectCode { get; set; }
        //public string suppliers { get; set; }
        public string? remark { get; set; }
        public string ddURL { get; set; }
        public int? isBuyer { get; set; }
        public string? approverCommentBox { get; set; }
        public string? LatestSupplierRemark { get; set; }
        public DateTime? createDate { get; set; }
        public DateTime? updateDate { get; set; }
        public double? discountTotal { get; set; }
        public string? taxSelect { get; set; }
        public double? shippingFee { get; set; }
        public double? bankCharges { get; set; }
        public double? netPrice { get; set; }
    }
}
