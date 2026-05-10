using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class PreviewSourcingVM
    {
        public string Data { get; set; }
        public string DataResponse { get; set; }
        public string EmailContent { get; set; }
        public string Brand { get; set; }
        public int RFQId { get; set; }
        public Brand? BrandRecommend { get; set; }
        public Dictionary<string, (string Email, string VendorName)> EmailList { get; set; }
        public List<VendorInfo> AllVendors { get; set; } = new List<VendorInfo>();
    }

    public class VendorInfo
    {
        public Guid Id { get; set; }
        public string VendorName { get; set; }
        public string Email { get; set; }
    }

}
