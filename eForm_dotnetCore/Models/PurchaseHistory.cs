using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class PurchaseHistory
    {
        public Guid Id { get; set; }
        public int POId { get; set; }
        public int PRId { get; set; }
        [ForeignKey(nameof(POId))]
        public Po PO { get; set; }
        [ForeignKey(nameof(PRId))]
        public Pr Pr { get; set; }
    }

    public class PurchaseHistoryVM:PurchaseHistory
    {
        public string SupplierId { get; set; }
        public string SupplierName { get; set; }
        public double NetPrice { get; set; }
    }
}
