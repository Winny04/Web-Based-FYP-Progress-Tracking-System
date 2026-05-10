using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class RFQTrackerViewModel
    {
        public List<RFQWithBoms> RFQs { get; set; } = new List<RFQWithBoms>();
        //public List<ActiveRFQList> activeRFQList { get; set; } = new List<ActiveRFQList>();
        //public List<AuditTrail> auditTrails { get; set; } = new List<AuditTrail>();

    }

    public class RFQWithBoms
    {
        public Rfq RFQ { get; set; }
        public string Boms { get; set; }
    }

    public class ActiveRFQList
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public string RfqName { get; set; }
        public string ProjectCode { get; set; }
        public string Requestor { get; set; }
        public string Department { get; set; }
        public string CustomerPO { get; set; }
        public string Customer { get; set; }
        public string ProjectName { get; set; }
        public string ProjectCategory { get; set; }
        public string Status { get; set; }
        public string ProjectStatus { get; set; }
        public string Remarks { get; set; }
        public string LatestSupplierRemark { get; set; }
        public bool IsOverThreeDays { get; set; }
        public int? LineNo { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
    }
}
