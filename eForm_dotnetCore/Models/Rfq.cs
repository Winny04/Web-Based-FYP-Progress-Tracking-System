using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    [Table("RFQ")]
    public partial class Rfq
    {
        [Key]
        public int Id { get; set; }
        public string? Data { get; set; }
        public string? DataResponse { get; set; }
        public DateTime? BuyerLastSubmittedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? CreatedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(40)]
        public string? AddedBy { get; set; }
        public int ProjectId { get; set; }
        [StringLength(50)]
        public string? FormName { get; set; }
        [Column("RFQName")]
        [StringLength(50)]
        public string? Rfqname { get; set; }
        [StringLength(20)]
        public string? Status { get; set; }
        public int? BuyerId { get; set; }
        public int? RequestorId { get; set; }
        public string? Remark { get; set; }
        public string? LatestSupplierRemark { get; set; }
        [ForeignKey(nameof(BuyerId))]
        public virtual EmployeeInfo? Buyer { get; set; }
        [ForeignKey(nameof(RequestorId))]
        public virtual EmployeeInfo? Requestor { get; set; }

        [ForeignKey(nameof(ProjectId))]
        public virtual Project Project { get; set; } = null!;
    }


    public class RFQHeader
    {
        [DisplayName("Project Code")]
        public string code { get; set; }

        [DisplayName("Project PO")]
        public string poNumber { get; set; }

        [DisplayName("Customer")]
        public string customer { get; set; }

        [DisplayName("Project Name")]
        public string projectName { get; set; }

        [DisplayName("Project Category")]
        public string projectCategory { get; set; }

        [DisplayName("PO Quantity")]
        public int poQuantity { get; set; }

        [DisplayName("Department")]
        public string department { get; set; }

        [DisplayName("Request By")]
        public string requester { get; set; }
        public int? budgetMaterialCost { get; set; }
        public double? actualMaterialCost { get; set; }


    }

    public class BOMGridFixProperties
    {
        [DisplayName("Item No")]
        public int? itemLineNo { get; set; }
        [DisplayName("Bom No")]
        public string? bomNo { get; set; }
        public string? itemCategory { get; set; }
        public string module { get; set; }
        public string description { get; set; }
        public string brand { get; set; }
        public string partNo { get; set; }
        public DateTime? requireDate { get; set; }
        public string? quotNoLink { get; set; }
        public string? quotationLineNo { get; set; }
        public string? currency { get; set; }
        public int totalQuantity { get; set; }
        [DisplayName("Unit Price")]
        public double? unitPrice { get; set; }
        public double? discount { get; set; }
        public double? tax { get; set; }
        public double? shippingCost { get; set; }
        public double? bankFee { get; set; }
        [DisplayName("Total Price")]
        [RegularExpression(@"^\d+\.\d{0,2}$")]
        public double? totalPrice { get; set; }
        public int? leadTime { get; set; }
        [DisplayName("Estimate Delivery Date")]
        public DateTime? eta { get; set; }

    }

    //public class BOMGrid:BOMGridFixProperties
    //{
    //    public string supplier { get; set; }
    //    public string? quotNoLink { get; set; }
    //}

    //public class NewBOMGrid: BOMGridFixProperties
    //{

    //}

    public class BOMGrid : BOMGridFixProperties
    {
        public string? selectedSupplier { get; set; }
    }

    public class CompleteBomGrid : BOMGrid
    {

        [DisplayName("Project Code")]
        public string code { get; set; }
        public string projectName { get; set; }
        [DisplayName("RFQ Name")]
        public string RFQNo { get; set; }
    }

    public class SelectBoxes
    {
        public bool @true { get; set; }
        public SelectBoxes()
        {
            @true = false;
        }
    }

    //public class GridQuotation
    //{
    //    public string supplierName { get; set; }
    //    public string companyStatus { get; set; }
    //    //public string quotation { get; set; }
    //    public string respondTime { get; set; }
    //    public int? totalQuantity { get; set; }
    //    public int? unitPrice { get; set; }
    //    public int? tax { get; set; }
    //    public int? shippingCost { get; set; }
    //    public int? discount { get; set; }
    //    public int? totalPrice { get; set; }
    //    public string paymentTerms { get; set; }
    //    public int? selectSupplier { get; set; }
    //    public DateTime? eta { get; set; }
    //    public string vendorType { get; set; }
    //    public string? avlVendor { get; set; }
    //    public int? rowIndex { get; set; }
    //    public SelectBoxes selectBoxes { get; set; }
    //    public string remark { get; set; }
    //    public List<Files> quotation { get; set; }
    //}

    public class Files
    {
        public string? storage { get; set; }
        public string? name { get; set; }
        public string? url { get; set; }
        public int? size { get; set; }
        public string? type { get; set; }
        public string? originalName { get; set; }
    }

    public class SupplierListFixProperties
    {
        public string? r_remark { get; set; }
        public string? vendor_remark { get; set; }
        public string? remark { get; set; }
        public string? quotNoLink { get; set; }
        public int? supportFileId { get; set; }
        public List<Files>? quotation { get; set; }
    }

    public class SupplierList : SupplierListFixProperties
    {
        public SupplierObject? supplierKey { get; set; }
        public bool? recommended { get; set; }
    }

    public class SupplierListExportModel
    {
        public string supplierName { get; set; }
        public string? Quotation { get; set; }
        public string? Remark { get; set; }
    }

    public class RFQData
    {
        public RFQData()
        {
            header = new List<RFQHeader>();
            bomGrid = new List<BOMGrid>();
            supplierList = new List<SupplierList>();
        }

        public List<RFQHeader> header { get; set; }
        public List<BOMGrid> bomGrid { get; set; }
        public List<SupplierList>? supplierList { get; set; } = new List<SupplierList>();
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

    //public class NewRFQData
    //{
    //    public NewRFQData()
    //    {
    //        header = new List<RFQHeader>();
    //        bomGrid = new List<NewBOMGrid>();
    //        supplierList = new List<NewSupplierList>();
    //    }

    //    public List<RFQHeader> header { get; set; }
    //    public List<NewBOMGrid> bomGrid { get; set; }
    //    public List<NewSupplierList>? supplierList { get; set; } = new List<NewSupplierList>();
    //    public int? rfqId { get; set; }
    //    public string? userType { get; set; }
    //    public int allowEdit { get; set; }
    //    public int editAll { get; set; }
    //    public int isRequestor { get; set; }
    //    public int enableAddItem { get; set; }
    //    public int ableViewDrawing { get; set; }
    //    public string? userList { get; set; }
    //    public int? isFabrication { get; set; }
    //    public string? departmentList { get; set; }
    //    public string projectCode { get; set; }
    //    public string suppliers { get; set; }
    //    public string ddURL { get; set; }
    //    public int? isBuyer { get; set; }
    //    public string? approverCommentBox { get; set; }
    //    public DateTime? createDate { get; set; }
    //    public DateTime? updateDate { get; set; }
    //}

    public class RFQRoot
    {
        public RFQRoot()
        {
            data = new RFQData();
        }
        public RFQData data { get; set; }
    }

    public class SupplierObject
    {
        public string id { get; set; }
        public string name { get; set; }
    }

    //public class NewRFQRoot
    //{
    //    public NewRFQRoot()
    //    {
    //        data = new NewRFQData();
    //    }
    //    public NewRFQData data { get; set; }
    //}

}
