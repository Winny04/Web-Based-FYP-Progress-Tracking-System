using System.ComponentModel;

namespace eForm_dotnetCore.ViewModels
{
    public class RFQExportModel
    {
        public class RFQBasicInfo
        {
            [DisplayName("Document No")]
            public string DocumentNo { get; set; } = "IMS-F-40";

            [DisplayName("Effective Date")] 
            public string EffectiveDate { get; set; } = "10/10/2023";

            [DisplayName("Revision No")]
            public int RevisionNo { get; set; } = 3;
            [DisplayName("Create Date")]
            public DateTime? createDate { get; set; }
            [DisplayName("Update Date")]
            public DateTime? updateDate { get; set; }
        }
    }
}
