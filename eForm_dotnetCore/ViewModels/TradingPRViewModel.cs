using eForm_dotnetCore.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.ViewModel
{
    public class TradingPRViewModel
    {
        public int Id { get; set; }
        [StringLength(50)]
        public string? Module { get; set; }
        public string? BomNo { get; set; }
        public string? ProjectName { get; set; }
        [StringLength(50)]
        public string? PartNumber { get; set; }
        [StringLength(30)]
        public string? Brand { get; set; }
        [StringLength(100)]
        public string? PartDesc { get; set; }
        [StringLength(100)]
        public string? ItemCategory { get; set; }
        public int? UnitQty { get; set; }
        public int? RequireQty { get; set; }
        public double? TotalPrice { get; set; }
        public string? Status { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? CreatedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(40)]
        public string? AddedBy { get; set; }
        public int ProjectId { get; set; }
        public int BomID { get; set; }
        public string SupplierName { get; set; }
        public string? QuotationLine { get; set; }
        public string? QuotationNoLink { get; set; }
        public int? RfqId { get; set; }
        public string RfqName { get; set; }
        public int? PrId { get; set; }
        public string PRName { get; set; }
        public string? poLineNo { get; set; }
        public int prQuantity { get; set; }
        public double prUnitPrice { get; set; }
        public double prTotalPrice { get; set; }
        [ForeignKey(nameof(ProjectId))]
        [InverseProperty("Boms")]
        public DateTime? ETA { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public int? ReceivedQuantity { get; set; }
        public string? POLine { get; set; }
        public string? IssuedPIC { get; set; }
        public int? IssuedQuantity { get; set; }
        public int? BalanceToIssueOutQty { get; set; }
        public int? BalanceToReceiveQty { get; set; }
        public DateTime? IssuedOutDate { get; set; }
        public string? QuotName { get; set; }
        public string? QuotUrl { get; set; }
        public virtual ICollection<BomSupportFile> BomSupportFiles { get; set; }
        public virtual ICollection<PRApprover> PRApprovers { get; set; }
        public int? totalApprover { get; set; }

        public virtual Pr Pr { get; set; }
        public virtual Rfq Rfq { get; set; }
    }

}
