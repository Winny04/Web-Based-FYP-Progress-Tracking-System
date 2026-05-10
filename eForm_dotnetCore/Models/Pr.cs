using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace eForm_dotnetCore.Models
{
    [Table("PR")]
    public partial class Pr
    {
        public Pr()
        {
            Pos = new HashSet<Po>();
            PRApprovers = new HashSet<PRApprover>();
        }
        [Key]
        public int Id { get; set; }
        public string? Data { get; set; }
        public string? DataResponse { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? CreatedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(40)]
        public string? AddedBy { get; set; }
        [Column("RFQId")]
        public string? Rfqid { get; set; }
        [StringLength(50)]
        public string? FormName { get; set; }
        public string? PORemark { get; set; }
        [Column("PRName")]
        [StringLength(50)]
        public string? Prname { get; set; }
        [StringLength(20)]
        public string? Status { get; set; }
        public string? Remark { get; set; }
        public bool isMerge { get; set; }
        public int? totalApprover { get; set; }
        public int? ProjectId { get; set; }
        public int RequestorId { get; set; }
        [ForeignKey(nameof(RequestorId))]
        public EmployeeInfo Requestor { get; set; }
        public int DepartmentId { get; set; }
        public decimal? PriceBeforeRevise { get; set; }
        public int RevisionNo { get; set; } = 0;
        public DateTime? PRApproveDate { get; set; }
        public virtual Project? Project { get; set; }
        public virtual ICollection<Po> Pos { get; set; }
        public virtual ICollection<PRApprover> PRApprovers { get; set; }
    }


    public class PRGridHeader
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
        public string typeOfPurchase { get; set; }
    }

    public class PRDataFixProperties 
    {
        public string auth { get; set; }
        public string ddURL { get; set; }
        public string rfqUrl { get; set; }
        public string quotations { get; set; }
        //public string? selectedSupplier { get; set; }
        public int? isRevised { get; set; }
        public int? allRm { get; set; }
        public string? departmentList { get; set; }
        public DateTime? createDate { get; set; }
        public DateTime? updateDate { get; set; }
        public List<PRGridHeader> header { get; set; }
        public List<PRBOMGrid> bomGrid { get; set; }
        public List<Currencies> currencyGrid { get; set; }
        public int? discount { get; set; }
        public int isRequestor { get; set; }
        public int ableViewDrawing { get; set; }
        public double? tax { get; set; }
        public double? shippingCost { get; set; }
        public string? userList { get; set; }
        public string rfqLink { get; set; }
        public int? isFabrication { get; set; }
        public double? netPrice { get; set; }
        public double? netPriceUsd { get; set; }
        public string? rejectCommentBox { get; set; }
        public double? discountTotal { get; set; }
        public string? taxSelect { get; set; }
        public double? shippingFee { get; set; }
        public double? bankCharges { get; set; }
        public double? convertedDiscount { get; set; }
        public double? convertedTax { get; set; }
        public double? convertedShippingFee { get; set; }
        public double? convertedBankCharges { get; set; }
        public List<PendingList> pendingItems { get; set; } = new List<PendingList>();
    }

    public class PendingList
    {
        public string module { get; set; }
        public string description { get; set; }
        public string brand { get; set; }
        public string partNo { get; set; }
        public List<Files>? fabPartNo { get; set; }
        public int totalQuantity { get; set; }
        [DisplayName("Unit Price")]
        public double? unitPrice { get; set; }
        public double? discount { get; set; }
        public double? tax { get; set; }
        public double? shippingCost { get; set; }
        public double? bankFee { get; set; }
        public string? quotNoLink { get; set; }
        public string? quotationLineNo { get; set; }
        public string? currency { get; set; }
        public DateTime? requireDate { get; set; }
        public DateTime? eta { get; set; }
    }

    //old
    //public class PRData:PRDataFixProperties
    //{
    //    public List<SupplierContainer> supplierContainer { get; set; }
    //}

    public class Currencies
    {
        public string? cr_currency { get; set; }
        public DateTime? conversionDate { get; set; }
        public double? rate { get; set; }
    }

    //public class SupplierContainer
    //{
    //    public string? supplierName { get; set; }
    //    public string? quotNoLink { get; set; }
    //    public string? remark { get; set; }
    //    public string? quoUrl { get; set; }
    //    public string? quotName { get; set; }
    //}
    public class SupplierContainer
    {
        public SupplierObject? supplierKey { get; set; }
        public string? quotNoLink { get; set; }
        public string? remark { get; set; }
        public string? r_remark { get; set; }
        public string? vendor_remark { get; set; }
        public string? quoUrl { get; set; }
        public string? quotName { get; set; }
    }

    public class PRData:PRDataFixProperties
    {
        public string? pendingRemoveItems { get; set; }
        public string? newQuotationNoLink { get; set; }
        public List<Files> newQuo { get; set; }
        public List<SupplierContainer> supplierContainer { get; set; }
    }

    public class PRBOMGrid
    {
        [DisplayName("Item No")]
        public int itemLineNo { get; set; }
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
        [RegularExpression(@"^\d+\.\d{0,4}$")]
        public double? totalPrice { get; set; }
        [RegularExpression(@"^\d+\.\d{0,4}$")]
        public double? totalPriceUSD { get; set; }
        public int? leadTime { get; set; }
        [DisplayName("Estimate Delivery Date")]
        public DateTime? eta { get; set; }
        public bool? isVoid { get; set; }
    }

    //old
    //public class PRRoot
    //{
    //    public PRData data { get; set; }
    //}

    public class PRRoot
    {
        public PRData data { get; set; }
    }

}
