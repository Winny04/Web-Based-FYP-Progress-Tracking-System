namespace FYPTracker.Domain
{
    public partial class TableData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Revision { get; set; }
        public DateTime? LastUpdated { get; set; }
        public string CreatedAt { get; set; }
        public string Status { get; set; }
        public string Image { get; set; }
        public string Remark { get; set; }
        public string DocumentName { get; set; }
        public string ModifiedBy { get; set; }
    }
}
