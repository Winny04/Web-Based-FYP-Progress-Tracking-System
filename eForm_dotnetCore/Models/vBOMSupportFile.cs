namespace eForm_dotnetCore.Models
{
    public class vBOMSupportFile
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public int BomId { get; set; }
        public int SupportFileId { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
