namespace FYPTracker.ViewModels
{
    public class StudentFeedbackItemVM
    {
        public string SourceType { get; set; } = string.Empty; // e.g. "Supervisor Submission Review", "Supervisor Evaluation", "Examiner Evaluation", "Stage 2 Submission Feedback"
        public string SourceBadgeClass { get; set; } = "bg-primary";
        public string IconClass { get; set; } = "fas fa-user-tie";
        public string MilestoneTitle { get; set; } = string.Empty;
        public int? MilestoneStage { get; set; }
        public string FeedbackContent { get; set; } = string.Empty;
        public DateTime? FeedbackDate { get; set; }
        public string TargetUrl { get; set; } = "/Student/Forms";
        public string AttachmentFileName { get; set; } = string.Empty;
        public int? AttachmentFileId { get; set; }
    }
}
