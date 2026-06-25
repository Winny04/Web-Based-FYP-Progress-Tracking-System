using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FYPTracker.Models
{
    [Table("MeetingLog")]
    public class MeetingLog
    {
        [Key]
        public int Id { get; set; }

        public int MeetingBookingId { get; set; }

        public string? KeyTopicsDiscussed { get; set; }

        public string? ActionPointsForNextMeeting { get; set; }

        public string? CompletedActionsAndNotes { get; set; }

        [StringLength(255)]
        public string? PdfLogbookPath { get; set; }

        [StringLength(255)]
        public string? SignedPdfLogbookPath { get; set; }

        public string? SupervisorComments { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? NextMeetingDate { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? SignatureDate { get; set; }

        /// <summary>Draft | Signed</summary>
        [StringLength(50)]
        public string LogStatus { get; set; } = "Draft";

        [Column(TypeName = "datetime")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(MeetingBookingId))]
        public virtual MeetingBooking? MeetingBooking { get; set; }
    }
}
