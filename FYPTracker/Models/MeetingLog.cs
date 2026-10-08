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

        [NotMapped]
        public string? SupervisorSignature 
        { 
            get 
            {
                if (SupervisorComments != null && SupervisorComments.Contains("|||SIG|||"))
                {
                    var parts = SupervisorComments.Split(new[] { "|||SIG|||" }, StringSplitOptions.None);
                    return parts.Length > 1 ? parts[1] : null;
                }
                return null;
            }
            set { }
        }

        [NotMapped]
        public string DisplayComments
        {
            get
            {
                if (SupervisorComments != null && SupervisorComments.Contains("|||SIG|||"))
                {
                    return SupervisorComments.Split(new[] { "|||SIG|||" }, StringSplitOptions.None)[0];
                }
                return SupervisorComments ?? "";
            }
        }

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
