namespace FYPTracker.Models
{
    public class EmailTemplateConfig
    {
        public int Id { get; set; }
        public string ConfigKey { get; set; }
        public string? ConfigValue { get; set; }
        public string? RequireParams { get; set; }
    }
}
