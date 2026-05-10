using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace eForm_dotnetCore.Models
{
    [Table("PO")]
    public partial class Po
    {
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
        public int RequestorId { get; set; }
        [ForeignKey(nameof(RequestorId))]
        public EmployeeInfo Requestor { get; set; }
        [Column("PRId")]
        public int Prid { get; set; }
        [StringLength(50)]
        public string? FormName { get; set; }
        [Column("POName")]
        [StringLength(50)]
        public string? Poname { get; set; }
        [StringLength(20)]
        public string? Status { get; set; }

        [ForeignKey(nameof(Prid))]
        [InverseProperty("Pos")]
        public virtual Pr Pr { get; set; } = null!;
    }

    public class POBOMGrid
    {
        [DisplayName("Item No")]
        public int itemLineNo { get; set; }
        [DisplayName("Bom No")]
        public string? bomNo { get; set; }
        public string? itemCategory { get; set; }
        public string module { get; set; }
        public string description { get; set; }
        public string brand { get; set; }
        public string? selectedSupplier { get; set; }
        public string partNo { get; set; }
        public DateTime? requireDate { get; set; }
        public string supplier { get; set; }
        public string? quotNoLink { get; set; }
        public string? quotationLineNo { get; set; }
        public string? currency { get; set; }
        public int? totalQuantity { get; set; }
        [DisplayName("Unit Price")]
        public double? unitPrice { get; set; }
        public double? discount { get; set; }
        public double? tax { get; set; }
        public double? shippingCost { get; set; }
        public double? bankFee { get; set; }
        [DisplayName("Total Price")]
        [RegularExpression(@"^\d+\.\d{0,4}$")]
        public double? totalPrice { get; set; }
        [DisplayName("Estimate Delivery Date")]
        public DateTime? eta { get; set; }
        public bool? isVoid { get; set; }
    }

    public class POData
    {
        public string ddURL { get; set; }
        public string rfqLink { get; set; }
        public object quotations { get; set; }
        public List<SupplierContainer> supplierContainer { get; set; }
        public List<POSupplierContainer> poSupplierContainer { get; set; }
        //public List<PORemarkList> remarkList { get; set; }
        public string attention { get; set; }
        //public PORemarkList remarkList { get; set; }
        public string? remark { get; set; }
        public string? delCompany { get; set; }
        public string? delAttn { get; set; }
        public string? delAddr3 { get; set; }
        public string poNo { get; set; }
        public DateTime createDate { get; set; }
        public string quotationNo { get; set; }
        public string customerPo { get; set; }
        public string requester { get; set; }
        public string project { get; set; }
        public string department { get; set; }
        public DateTime? deliveryDate { get; set; }
        public string acc_description { get; set; }
        public string batchGroup { get; set; }
        public string acc_status { get; set; }
        public List<POBOMGrid> bomGrid { get; set; }
        public string? supplierRemark { get; set; }
        public double? subTotal { get; set; }
        public double? tax { get; set; }
        public int? isFabrication { get; set; }
        public int? allRm { get; set; }
        public int? ableViewDrawing { get; set; }
        public DateTime updateDate { get; set; }
        public int? creditTerms { get; set; }
        public object agent { get; set; }
        public double? excludeTax { get; set; }
        public double? sumTax { get; set; }
        public double? grandTotal { get; set; }
        public double? netTotal { get; set; }
    }

    public class POSupplierContainer
    {
        public string? address { get; set; }
        public string? supplierName { get; set; }
        public string? supplierId { get; set; }
        public string? supplierCode { get; set; }
        public string? quotNoLink { get; set; }
        public string? remark { get; set; }
        public string? quoUrl { get; set; }
        public string? quotName { get; set; }
        public string? tel { get; set; }
        public string? fax { get; set; }
    }

    //public class PORemarkList
    //{
    //    public string? remark { get; set; }
    //    public string? delCompany { get; set; }
    //    public string? delAttn { get; set; }
    //    public string? delAddr3 { get; set; }
    //}

    public class PORoot
    {
        public PORoot(POData data)
        {
            this.data = data;
            //this.data.poNo = poName ?? "";
        }
        public POData data { get; set; }
    }


    //PO Form Model
    public class POFormData
    {
        public string poNumber { get; set; }
        public DateTime poDate { get; set; }
        public string refNo { get; set; }
        public int? paymentTerms { get; set; }
        public string agent { get; set; }
        public string page { get; set; }
        public string Customer { get; set; }
        public string customerAddr { get; set; }
        public string CustomerName { get; set; }
        public string customerTel { get; set; }
        public string customerFax { get; set; }
        public string deliveryTo { get; set; }
        public List<POFormPoItemList> poItemList { get; set; }
        public DateTime? eta { get; set; }
        public string totalPriceText { get; set; }
        public double? subtotal { get; set; }
        public double? discount { get; set; }
        public double? nett { get; set; }
        public double? tax { get; set; }
        public double? grandTotal { get; set; }
        public string? remark { get; set; }
        public string? projectCode { get; set; }
        public string? projectName { get; set; }
        public string? category { get; set; }
        public string? customer { get; set; }
        public string? acc_description { get; set; }
        public string? batchGroup { get; set; }
        public string? acc_status { get; set; }
    }

    public class POFormPoItemList
    {
        public int? itemNo { get; set; }
        public string description { get; set; }
        public int? qty { get; set; }
        public double? unitPrice { get; set; }
        public double? discount { get; set; }
        public double? tax { get; set; }
        public double? amount { get; set; }
    }

    public class POFormRoot
    {
        public POFormData data { get; set; }
    }




}

