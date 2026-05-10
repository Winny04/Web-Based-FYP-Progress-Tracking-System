namespace eForm_dotnetCore.ViewModels
{
    public class SubmitRFQModel
    {
        public string Data { get; set; }
        public string DataResponse { get; set; }
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int BomId { get; set; }
        public bool IsAutoSubmit { get; set; }
        public string VendorName { get; set; }
    }
}
