using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace eForm_dotnetCore.Models
{
    [Table("Form")]
    public partial class Form
    {
        [Key]
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Data { get; set; }
        public int? AddedBy { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime DateCreated { get; set; }
        public string? ModifiedBy { get; set; }
        public string? Revision { get; set; }
        public string? Status { get; set; }
        [Column(TypeName = "datetime")]
        public DateTime? LastUpdated { get; set; }
        public string? Remark { get; set; }
        public string? DocumentName { get; set; }
    }

    public class FormVM : Form
    {
        public string? DataResponse { get; set; }
    }
}
