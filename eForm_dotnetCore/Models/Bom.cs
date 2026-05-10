using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace eForm_dotnetCore.Models
{
    [Table("BOM")]
    public partial class Bom
    {
        public Bom()
        {
            BomSupportFiles = new HashSet<BomSupportFile>();
            BomsendEmails = new HashSet<BomsendEmail>();
        }

        public int Id { get; set; }
        [StringLength(50)]
        public string? Module { get; set; }
        [StringLength(int.MaxValue)]
        public string? PartNumber { get; set; }
        [StringLength(30)]
        public string? Brand { get; set; }
        [StringLength(int.MaxValue)]
        public string? BomNo { get; set; }
        public string? PartDesc { get; set; }
        [StringLength(100)]
        public string? ItemCategory { get; set; }
        public double? UnitPrice { get; set; }
        public int? UnitQty { get; set; }
        public string? QuotationLine { get; set; }
        public int? RequireQty { get; set; }
        public DateTime? RequireDate { get; set; }
        public int? LeadTime { get; set; }
        public DateTime? ETA { get; set; }
        [DataType(DataType.Date)]
        public string? Status { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? CreatedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(40)]
        public string? AddedBy { get; set; }
        public int ProjectId { get; set; }
        public DateTime? EstDeliveryDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public int? ReceivedQuantity { get; set; }
        public string? IssuedPIC { get; set; }
        public int? IssuedQuantity { get; set; }
        public DateTime? IssuedOutDate { get; set; }
        public int? BalanceToReceiveQty { get; set; }
        public int? BalanceToIssueOutQty { get; set; }
        public bool IsPOUpdated { get; set; }
        public string? ReceivingRemark { get; set; }
        public int? RFQId { get; set; }
        public int? PRId { get; set; }
        public bool IsCopied { get; set; } = false;

        [ForeignKey(nameof(RFQId))]
        public virtual Rfq? RFQ { get; set; }

        [ForeignKey(nameof(PRId))]
        public virtual Pr? PR { get; set; }

        [ForeignKey(nameof(ProjectId))]
        public virtual Project Project { get; set; } = null!;
        public virtual ICollection<BomsendEmail> BomsendEmails { get; set; }
        public virtual ICollection<BomSupportFile> BomSupportFiles { get; set; }

        [NotMapped]
        public List<SupplierQuotationView>? SupplierQuotations { get; set; }

        [NotMapped]
        public string RFQSharingStatus { get; set; } = "Processing";

    }

    public class SupplierQuotationView
    {
        public string? SupplierName { get; set; }
        public string? QuotNo { get; set; }
        public string? Remark { get; set; }
        public string? RRemark { get; set; }
        public bool Recommended { get; set; }
        public string? FileName { get; set; }
        public string? FileUrl { get; set; }
    }

}
