using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace eForm_dotnetCore.Models
{
    public class vBOM
    {

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
        public int? UnitQty { get; set; }
        public string? QuotationLine { get; set; }
        public int? RequireQty { get; set; }
        [DataType(DataType.Date)]
        public string? Status { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? CreatedDate { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(40)]
        public string? AddedBy { get; set; }
        public int ProjectId { get; set; }
        public string? ETA { get; set; }
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
        public int RevisionNo { get; set; }
        public string? RFQDataResponse { get; set; }
        public string? RFQName { get; set; }
        public string? PRDataResponse { get; set; }
        public string? PRName { get; set; }
        public string ProjectName { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryShortName { get; set; }
        public int? Category { get; set; }
    }
}
