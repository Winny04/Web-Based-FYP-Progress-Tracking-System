using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    [Table("Project")]
    public partial class Project
    {
        public Project()
        {
            Boms = new HashSet<Bom>();
            Prs = new HashSet<Pr>();
            Rfqs = new HashSet<Rfq>();
            ProjectPOFiles = new HashSet<ProjectPOFile>(); // Link to PO file
            ProjectQFFiles = new HashSet<ProjectQFFile>(); // Link to QF file
        }

        [Key]
        public int Id { get; set; }
        [Unicode(false)]
        public string Code { get; set; } = null!;
        [Unicode(false)]
        public string? Requestor { get; set; }
        [Column("PO_Number")]
        [Unicode(false)]
        public string? PoNumber { get; set; }
        [Unicode(false)]
        public string? Name { get; set; }
        [Unicode(false)]
        public int? Category { get; set; }
        public int? SalesPic { get; set; }
        [Column("Customer")]
        [StringLength(50)]
        [Unicode(false)]
        public string? Customer { get; set; }
        [Column("PO_Start_Date", TypeName = "datetime")]
        public DateTime? PoStartDate { get; set; }
        [Column("PO_Due_Date")]
        public string? PoDueDate { get; set; }
        [Column("PO_Terms")]
        [Unicode(false)]
        public string? PoTerms { get; set; }
        [StringLength(50)]
        [Unicode(false)]
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
        [ForeignKey(nameof(Category))]
        public Category? CategoryInfo { get; set; }

        [InverseProperty(nameof(Bom.Project))]
        public virtual ICollection<Bom> Boms { get; set; }
        [InverseProperty(nameof(Pr.Project))]
        public virtual ICollection<Pr> Prs { get; set; }
        [InverseProperty(nameof(Rfq.Project))]
        public virtual ICollection<Rfq> Rfqs { get; set; }

        // Relationship for ProjectPOFile
        [InverseProperty(nameof(ProjectPOFile.Project))]
        public virtual ICollection<ProjectPOFile> ProjectPOFiles { get; set; }

        // Relationship for ProjectQFFile
        [InverseProperty(nameof(ProjectQFFile.Project))]
        public virtual ICollection<ProjectQFFile> ProjectQFFiles { get; set; }
        public double GetTotalActualCost() =>
            Math.Round((ActualMaterialCost ?? 0) +  (double)(ExtraCost ?? 0) ,4);

        [Column("Archive")]
        public int Archive { get; set; } = 0;  // Defaults to 0 (active)
    }
}
