using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class SupplierAttachment
    {
        public int Id { get; set; }
        public int SupportFileId { get; set; }
        public string? QuotNo { get; set; }
        public bool IsRecommended { get; set; }
        public string? Remark { get; set; }
        public string? R_Remark { get; set; }
        public string? V_Remark { get; set; }
        public SupportFile SupportFile { get; set; }
        public Guid SupplierInfoId { get; set; }
        public SupplierInfo SupplierInfo { get; set; }
        public int RFQId { get; set; }
    }
}
