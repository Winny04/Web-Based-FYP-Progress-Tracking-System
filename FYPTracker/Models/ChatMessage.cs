using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SenderId { get; set; }

        public int? ReceiverId { get; set; }

        public int? GroupId { get; set; }

        public string? MessageText { get; set; }

        public string? AttachmentUrl { get; set; }

        public string? AttachmentName { get; set; }

        public DateTime SentAt { get; set; } = DateTime.Now;

        public bool IsRead { get; set; } = false;

        public bool IsPinned { get; set; } = false;

        public bool IsEdited { get; set; } = false;

        public bool IsDeleted { get; set; } = false;

        public int? ReplyToId { get; set; }

        [ForeignKey("ReplyToId")]
        public virtual ChatMessage? ReplyToMessage { get; set; }

        // Navigation properties
        [ForeignKey("SenderId")]
        public virtual ApplicationUser Sender { get; set; } = null!;

        [ForeignKey("ReceiverId")]
        public virtual ApplicationUser? Receiver { get; set; }

        [ForeignKey("GroupId")]
        public virtual ChatGroup? Group { get; set; }
    }
}
