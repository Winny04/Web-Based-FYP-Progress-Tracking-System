using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class PRApprovalViewModel
    {
        public int Id { get; set; }
        public string? Data { get; set; }
        public string? DataResponse { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
        public string? AddedBy { get; set; }
        public int? Rfqid { get; set; }
        public string? FormName { get; set; }
        public string? Prname { get; set; }
        public string? Status { get; set; }
        public string? Remark { get; set; }
        public bool isMerge { get; set; }
        public int? totalApprover { get; set; }
        public string userType { get; set; }
        public string Department { get; set; }
        public double TotalPrice { get; set; }
        public int? Bomid { get; set; }
        public int? ProjectId { get; set; }
        public string? RequestBy { get; set; }
        public string? TypeOfPurchase { get; set; }
        public string? VendorName { get; set; }
        public string? POCType { get; set; }
        public string Category { get; set; }
        public Guid? POCId { get; set; }
        public virtual Project? Project { get; set; }
        public virtual Bom? Bom { get; set; }
        public virtual Rfq? Rfq { get; set; }
        public virtual ICollection<Po> Pos { get; set; }
        public virtual ICollection<PRApprover> PRApprovers { get; set; }
    }
}
