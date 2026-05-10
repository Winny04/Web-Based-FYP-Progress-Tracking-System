namespace eForm_dotnetCore.ViewModels
{
    public class ShowPDFModel
    {
        public ShowPDFModel(string base64String, string fileName)
        {
            Base64String = base64String;
            FileName = fileName;
        }
        public string? Description { get; set; }
        public string? Description2 { get; set; }
        public string? Description3 { get; set; }
        public string Base64String { get; set; }
        public string FileName { get; set; }
        public int? SupportFileId { get; set; }
    }
}
