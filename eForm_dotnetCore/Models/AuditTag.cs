using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class AuditTag
    {
        public Guid Id { get; set; }
        public int AuditId { get; set; }
        [ForeignKey(nameof(AuditId))]
        public AuditTrail AuditTrail { get; set; }
        public int EmployeeId { get; set; }
        [ForeignKey(nameof(EmployeeId))]
        public EmployeeInfo EmployeeInfo { get; set; }
        public DateTime CreateAt { get; set; }
        public bool IsRead { get; set; }
    }
}
