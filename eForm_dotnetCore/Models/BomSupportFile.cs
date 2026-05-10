using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    [Table("BOMSupportFile")]
    public class BomSupportFile
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public int BomId { get; set; }
        public int SupportFileId { get; set; }
        [ForeignKey(nameof(BomId))]
        [JsonIgnore]
        public virtual Bom? Bom { get; set; }
        [ForeignKey(nameof(SupportFileId))]
        public virtual SupportFile? SupportFile { get; set; }
    }
}
