using System;
using System.Collections.Generic;

namespace eForm_dotnetCore.Models
{
    public partial class BomsendEmail
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public DateTime? BomlastUpdatedDate { get; set; }
        public DateTime? LastEmailSentDate { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? BomStatus { get; set; }

        public virtual Bom Bom { get; set; } = null!;
    }
}
