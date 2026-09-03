using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FYPTracker.ViewModels
{
    public class Stage2ViewModel
    {
        public int MilestoneId { get; set; }

        public bool IsStage2Unlocked { get; set; }

        [Required(ErrorMessage = "Turnitin report is required")]
        public IFormFile TurnitinFile { get; set; }

        [Required(ErrorMessage = "Final report is required")]
        public IFormFile ReportFile { get; set; }

        [Required(ErrorMessage = "Marksheet is required")]
        public IFormFile MarksheetFile { get; set; }

        [Required(ErrorMessage = "Meeting logs are required")]
        public IFormFile MeetingLogsFile { get; set; }

        [Required(ErrorMessage = "Presentation slides are required")]
        public IFormFile SlideDeckFile { get; set; }

        [Required(ErrorMessage = "You must acknowledge the rules")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the rules")]
        public bool RulesAccepted { get; set; }
    }
}
