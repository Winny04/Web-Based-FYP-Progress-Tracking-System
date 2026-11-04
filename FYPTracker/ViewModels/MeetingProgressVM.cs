using System;

namespace FYPTracker.ViewModels
{
    public class MeetingProgressVM
    {
        public int? Required { get; set; }
        public int Completed { get; set; }
        public string SessionName { get; set; } = string.Empty;
        public int Stage { get; set; }
        
        public int Remaining => Required.HasValue ? Math.Max(0, Required.Value - Completed) : 0;
        public bool IsSatisfied => !Required.HasValue || Required.Value == 0 || Completed >= Required.Value;
        
        public int ProgressPercentage 
        {
            get
            {
                if (!Required.HasValue || Required.Value == 0) return 0;
                if (Completed >= Required.Value) return 100;
                return (int)Math.Round((double)Completed / Required.Value * 100);
            }
        }
    }
}
