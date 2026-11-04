using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    public class ChatGroupMember
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int GroupId { get; set; }

        [Required]
        public int StudentId { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.Now;

        [ForeignKey("GroupId")]
        public virtual ChatGroup Group { get; set; } = null!;

        [ForeignKey("StudentId")]
        public virtual ApplicationUser Student { get; set; } = null!;
    }
}
