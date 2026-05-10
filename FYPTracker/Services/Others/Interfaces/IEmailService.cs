namespace FYPTracker.Services.Others.Interfaces
{
    public interface IEmailService
    {
        void Send(string to, string subject, string html,string? filePath =null, string from = null, string? cc = null);
        Task Send(List<string> to, string subject, string html, string? filePath =null, string from = null);
    }
}
