namespace FYPTracker.ViewModels
{
    public class TestingRelaxedModeSettingsVM
    {
        public bool IsRelaxedModeEnabled { get; set; } = false;
        public bool AllowFlexibleDates { get; set; } = false;
        public bool ManualMeetingOnly { get; set; } = false;
        public bool ImmediateLogbookUnlock { get; set; } = false;
        public bool RelaxAcademicDeadlines { get; set; } = false;
        public bool RelaxMeetingFrequency { get; set; } = false;
        public bool RelaxTimetableAvailability { get; set; } = false;

        public bool IsTestingEnvironment { get; set; } = false;
        public string? ActiveDatabaseName { get; set; }
    }
}
