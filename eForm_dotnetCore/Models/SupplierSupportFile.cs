using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class SupplierSupportFile
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public Guid SupplierInfoId { get; set; }
        public int SupportFileId { get; set; }
        [ForeignKey(nameof(SupplierInfoId))]
        public virtual SupplierInfo SupplierInfo { get; set; }
        [ForeignKey(nameof(SupportFileId))]
        public virtual SupportFile? SupportFile { get; set; }
    }
}
