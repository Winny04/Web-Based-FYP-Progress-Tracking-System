using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.ViewModels
{
    public class POTrackerExcelModel
    {
        public class POHeaderZone
        {
            public string Status { get; set; }
            public string PoName { get; set; }
            public DateTime PrApproveDate { get; set; }
            public string ProjectCode { get; set; }
            public string ProjectPo { get; set; }
            public string Customer { get; set; }
            public string ProjectName { get; set; }
            public string Category { get; set; }
            public string Rfq { get; set; }
            public string Pr { get; set; }
            public string RequestBy { get; set; }
            public string Remark { get; set; }
        }

        public class POContentZone
        {
            public int ItemLineNo { get; set; }
            public string PoLine { get; set; }
            public int PoQty { get; set; }
            public double UnitPrice { get; set; }
            public double Discount { get; set; }
            public double Tax { get; set; }
            public double ShippingCost { get; set; }
            public double BankFee { get; set; }
            public double TotalPrice { get; set; }
            public DateTime Eta { get; set; }

        }

    }
}
