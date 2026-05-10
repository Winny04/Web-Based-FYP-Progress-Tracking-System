using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.Others.Interfaces;

namespace eForm_dotnetCore.Services.Others
{
    public class FileWriter: IFileWriter
    {
        private readonly EformSophicDemoContext _context;

        public FileWriter(EformSophicDemoContext context)
        {
            _context = context;
        }

        public async Task<SupportFile> UploadDocument(IFormFile file)
        {
            if (file != null)
            {
                //throw exception
            }

            return await WriteFile(file);
        }

        private async Task<SupportFile> WriteFile(IFormFile file)
        {
            var sf = new SupportFile();
            try
            {
                var extension = Path.GetExtension(file.FileName);
                //fileName = Guid.NewGuid().ToString() + extension;
                //var uploads = Path.Combine(_environment.WebRootPath, "upload");  IHostingEnvironment _environment;
                var fileName = Path.GetFileNameWithoutExtension(file.FileName).Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('.', '_');
                var _fileName = fileName;
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName+extension);
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents");

                for (int i =1; ;i++)
                {
                    if (File.Exists(path))
                    {
                        _fileName = fileName + "_" + i;
                        path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", _fileName + extension);
                    }
                    else
                    {
                        break;
                    }
                }

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var bits = new FileStream(path, FileMode.Create))
                {
                    await file.CopyToAsync(bits);
                }

                sf.ContentType = extension;
                sf.FileName = _fileName + extension;
                sf.Size = new FileInfo(path).Length.ToString();
                sf.CreatedAt = DateTime.Now;
            }
            catch (Exception e)
            {
                throw new Exception(@$"Convertion date error {DateTime.Now.ToString()}");
                //throw exception
            }

            await _context.SupportFile.AddRangeAsync(sf);
            await _context.SaveChangesAsync();
            return sf;
        }

        public void DeleteDocument(string fileName)
        {
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public string CheckAndGetFileName(string checkFileName)
        {
            var extension = Path.GetExtension(checkFileName);
            var fileName = Path.GetFileNameWithoutExtension(checkFileName).Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('.', '_');
            var _fileName = fileName;
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName + extension);

            for (int i = 1; ; i++)
            {
                if (File.Exists(path))
                {
                    _fileName = fileName + "_" + i;
                    path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", _fileName + extension);
                }
                else
                {
                    break;
                }
            }

            return _fileName + extension;
        }

        public (byte[], string, string) DownloadFiles(string dictionary, string fileName)
        {
            //var zipName = $"archive-{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}.zip";

            var path = Path.Combine(Directory.GetCurrentDirectory(), @$"wwwroot\\{dictionary}", fileName);

            //var memoryStream = new MemoryStream();

            //using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            //{
            //    var theFile = archive.CreateEntry(path);
            //    using (var streamWriter = new StreamWriter(theFile.Open()))
            //    {
            //        streamWriter.Write(File.ReadAllText(path));
            //    }
            //}

            //return ("application/zip", memoryStream.ToArray(), zipName);
            byte[] bytes = File.ReadAllBytes(path);
            return (bytes, "application/octet-stream", fileName);
        }

        public async Task<List<SupportFile>> UploadDocument(List<IFormFile> files)
        {
            var listFile = new List<SupportFile>();

            foreach (var file in files)
            {
                if(file.Length > 0)
                {
                    listFile.Add(await WriteFile(file));
                }
            }

            return listFile;
        }
    }
}
