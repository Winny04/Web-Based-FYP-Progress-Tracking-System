using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class PRApprover
    {
        public int Id { get; set; }
        public int PRId { get; set; }
        public int? ApproverId { get; set; }
        [ForeignKey(nameof(PRId))]
        public virtual Pr? Pr { get; set; }
        [ForeignKey(nameof(ApproverId))]
        public virtual EmployeeInfo? Approver { get; set; }
        public string? Type { get; set; }
        public int? DepartmentId { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool Approved { get; set; }
    }
}
