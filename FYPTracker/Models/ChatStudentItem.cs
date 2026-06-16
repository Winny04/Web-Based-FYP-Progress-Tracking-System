namespace FYPTracker.Models
{
    public class ChatStudentItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Programme { get; set; }
        public int UnreadCount { get; set; }
        public DateTime? LatestMessageAt { get; set; }
    }
}
