using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;

namespace FYPTracker.Services.Others
{
    public class FileWriter : IFileWriter
    {
        private readonly FYPTrackerContext _context;

        public FileWriter(FYPTrackerContext context)
        {
            _context = context;
        }

        public async Task<FYPSubmissionFile> UploadDocument(IFormFile file, int milestoneId, int uploadedBy)
        {
            return await WriteFile(file, milestoneId, uploadedBy);
        }

        public async Task<List<FYPSubmissionFile>> UploadDocuments(List<IFormFile> files, int milestoneId, int uploadedBy)
        {
            var list = new List<FYPSubmissionFile>();
            foreach (var file in files)
            {
                if (file.Length > 0)
                    list.Add(await WriteFile(file, milestoneId, uploadedBy));
            }
            return list;
        }

        private async Task<FYPSubmissionFile> WriteFile(IFormFile file, int milestoneId, int uploadedBy)
        {
            var extension = Path.GetExtension(file.FileName);
            var baseName = Path.GetFileNameWithoutExtension(file.FileName)
                .Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('.', '_');

            var finalName = baseName;
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
            var path = Path.Combine(dir, finalName + extension);

            for (int i = 1; File.Exists(path); i++)
            {
                finalName = baseName + "_" + i;
                path = Path.Combine(dir, finalName + extension);
            }

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var bits = new FileStream(path, FileMode.Create))
                await file.CopyToAsync(bits);

            var sf = new FYPSubmissionFile
            {
                MilestoneId  = milestoneId,
                FileName     = finalName + extension,
                FilePath     = Path.Combine("documents", finalName + extension),
                FileSize     = new FileInfo(path).Length,
                UploadedBy   = uploadedBy,
                UploadedDate = DateTime.Now
            };

            await _context.FYPSubmissionFiles.AddAsync(sf);
            await _context.SaveChangesAsync();
            return sf;
        }

        public void DeleteDocument(string fileName)
        {
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents", fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* swallow */ }
        }

        public string CheckAndGetFileName(string checkFileName)
        {
            var extension = Path.GetExtension(checkFileName);
            var baseName  = Path.GetFileNameWithoutExtension(checkFileName)
                .Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('.', '_');
            var finalName = baseName;
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents", finalName + extension);

            for (int i = 1; File.Exists(path); i++)
            {
                finalName = baseName + "_" + i;
                path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents", finalName + extension);
            }
            return finalName + extension;
        }

        public (byte[], string, string) DownloadFiles(string directory, string fileName)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", directory, fileName);
            byte[] bytes = File.ReadAllBytes(path);
            return (bytes, "application/octet-stream", fileName);
        }
    }
}
