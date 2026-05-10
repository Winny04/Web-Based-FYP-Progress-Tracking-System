using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class RFQSharing
    {
        public Guid Id { get; set; }
        public string? DataResponse { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public int? LeadTimeDays { get; set; }
        public DateTime? LeadTime { get; set; }
        public decimal? TotalPrice { get; set; }
        public Guid SourceId { get; set; }
        [ForeignKey(nameof(SourceId))]
        public Sourcing Sourcing { get; set; }
        public Guid SupplierId { get; set; }
        [ForeignKey(nameof(SupplierId))]
        public SupplierInfo Supplier { get; set; }
        public bool Rejected { get; set; }
        public DateTime? CreatedDate { get; set; }

        [NotMapped]
        public string? Currency { get; set; }

        [NotMapped]
        public List<Files>? Attachments { get; set; }

        [NotMapped]
        public List<DateTime>? UniqueDeliveryDates { get; set; }

    }

    public class RSDto
    {
        public string Id { get; set; }
        public string DataResponse { get; set; }
    }
}
