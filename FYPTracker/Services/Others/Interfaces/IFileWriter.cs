using FYPTracker.Models;

namespace FYPTracker.Services.Others.Interfaces
{
    public interface IFileWriter
    {
        Task<FYPSubmissionFile> UploadDocument(IFormFile file, int milestoneId, int uploadedBy);
        Task<List<FYPSubmissionFile>> UploadDocuments(List<IFormFile> files, int milestoneId, int uploadedBy);
        void DeleteDocument(string fileName);
        (byte[], string, string) DownloadFiles(string directory, string fileName);
        string CheckAndGetFileName(string checkFileName);
    }
}
