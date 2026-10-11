using System;
using System.Collections.Generic;

namespace FYPTracker.ViewModels
{
    public class IafDocumentViewModel
    {
        public MeetingRecordTemplateVM Template { get; set; } = new();
        public Dictionary<string, string> FormData { get; set; } = new();
        public string RenderMode { get; set; } = "ReadOnly"; // "StudentEdit", "SupervisorReview", "ReadOnly", "PrintPdf"
        
        public bool IsLocked { get; set; }
        
        public int? AttemptNumber { get; set; }
        public int? MilestoneId { get; set; }
        public string Status { get; set; } = "Draft";
        
        // Specific immutable fields usually injected from context or FormData
        public string StudentName { get; set; } = "";
        public string CoventryId { get; set; } = "";
        public string Program { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string SupervisorName { get; set; } = "";
        
        // Signatures and Feedback
        public string SupervisorSignature { get; set; } = "";
        public DateTime? SignatureDate { get; set; }
        public string SupervisorFeedback { get; set; } = "";
        public string PanelComments { get; set; } = "";
    }
}
