using eForm_dotnetCore.Models;

namespace eForm_dotnetCore.Services.Others.Interfaces
{
    public interface IFileWriter
    {
        Task<SupportFile> UploadDocument(IFormFile file);
        Task<List<SupportFile>> UploadDocument(List<IFormFile> files);
        void DeleteDocument(string fileName);
        (byte[], string, string) DownloadFiles(string dictionary,string fileName);
        string CheckAndGetFileName(string checkFileName);

    }
}
