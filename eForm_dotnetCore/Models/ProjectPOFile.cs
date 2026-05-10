using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eForm_dotnetCore.Models
{
    [Table("ProjectPOFile")]
    public class ProjectPOFile
    {
        [Key]
        public int Id { get; set; }

        public string? FileName { get; set; }

        public string? FileData { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? LastUpdatedDate { get; set; }


        [ForeignKey("Project")]
        public int ProjectId { get; set; }

        public int RequestorId { get; set; }

        // Navigation property
        public virtual Project Project { get; set; } = null!;
    }
}
