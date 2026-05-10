namespace eForm_dotnetCore.Models
{
    public class SupportFile
    {
        public int Id { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public string? Size { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? FileNameOnDisk { get; set; }
    }
}
