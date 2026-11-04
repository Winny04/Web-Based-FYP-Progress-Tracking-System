using System.Collections.Generic;

namespace FYPTracker.ViewModels
{
    public class MeetingRecordTemplateVM
    {
        public string TemplateKey { get; set; }
        public string Version { get; set; }
        public string Title { get; set; }
        public TemplatePage Page { get; set; }
        public List<TemplateField> Fields { get; set; }
    }

    public class TemplatePage
    {
        public string Size { get; set; }
        public string Orientation { get; set; }
    }

    public class TemplateField
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int Order { get; set; }
        public int Columns { get; set; }
        public string Type { get; set; }
        public int MinHeightMm { get; set; }
    }
}
