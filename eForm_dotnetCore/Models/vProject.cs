using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    public class vProject
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
        public string? Requestor { get; set; }
        public string? PONumber { get; set; }
        public string? Name { get; set; }
        public int? Category { get; set; }
        public int? SalesPic { get; set; }
        public string? Customer { get; set; }
        [Column("PO_Start_Date", TypeName = "datetime")]
        public DateTime? PoStartDate { get; set; }
        public string? PoDueDate { get; set; }
        [Column("PO_Terms")]
        public string? PoTerms { get; set; }
        public string? Status { get; set; }
        [Column("PO_Quantity")]
        public int? PoQuantity { get; set; }
        [Column("Budget_Material_Cost")]
        public int? BudgetMaterialCost { get; set; }
        [Column("Actual_Material_Cost")]
        public double? ActualMaterialCost { get; set; }
        public decimal? ExtraCost { get; set; }
        [Column("MechanicalPIC")]
        public int? MechanicalPic { get; set; }
        [Column("FSEPIC")]
        public int? Fsepic { get; set; }
        [Column("SoftwarePIC")]
        public int? SoftwarePic { get; set; }
        [Column("Quotation_No")]
        public string? QuotationNo { get; set; }
        public string? PICS { get; set; }
        [Column("ProjectPIC")]
        public string? ProjectPIC { get; set; }
        public int? MarkCompletedBy { get; set; }
        public int? PICLeader { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryShortName { get; set; }
    }
}
