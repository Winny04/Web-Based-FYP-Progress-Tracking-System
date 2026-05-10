using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.Others.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace eForm_dotnetCore.Controllers
{
  
    public class SupplierController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IFileWriter _fileWriter;
        private IWebHostEnvironment _env;
        public SupplierController(
            EformSophicDemoContext context,
            IFileWriter fileWriter,
            IWebHostEnvironment environment
            )
        {
            _context = context;
            _fileWriter = fileWriter;
            _env = environment;
        }

        public async Task<ActionResult> removeFile(int? id)
        {
            if (id.HasValue)
            {
                var file = _context.SupplierAttachment
                    .Include(x => x.SupportFile)
                    .FirstOrDefault(x => x.SupportFileId == id);

                _fileWriter.DeleteDocument(file.SupportFile.FileName);
                _context.SupplierAttachment.Remove(file);
                _context.SaveChanges();

                return RedirectToAction("Index");
            }
            
            return NoContent();
        }

        [HttpPost]
        public async Task<ActionResult> AddSupplierWithAttach(string supplierId,int rfqId,int? supportFileId, string quotNo,List<IFormFile> files)
        {
            if (ModelState.IsValid)
            {
                if(supportFileId.HasValue && supportFileId.Value > 0)
                {
                    var existsFile = _context.SupplierAttachment
                        .Include(x => x.SupportFile)
                        .FirstOrDefault(x =>
                    x.SupplierInfoId.ToString() == supplierId && x.RFQId == rfqId && x.SupportFileId == supportFileId.Value);

                    if(existsFile != null)
                    {
                        var filePath = Path.Combine(_env.WebRootPath, "documents", existsFile.SupportFile.FileName);

                        if(System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }

                        _context.SupplierAttachment.Remove(existsFile);
                        _context.SaveChanges();
                    }
                }

                var upFiles = await _fileWriter.UploadDocument(files.FirstOrDefault());
                var newAttachFile = new SupplierAttachment
                {
                    RFQId = rfqId,
                    SupportFile = upFiles,
                    SupplierInfoId = Guid.Parse(supplierId),
                    QuotNo = quotNo
                };

                _context.SupplierAttachment.Add(newAttachFile);
                _context.SaveChanges();

                return RedirectToAction("Edit", "RFQ", new { id = rfqId });
            }

            return NoContent();
        }

        public async Task<ActionResult> CopySupplierWithAttach(string supplierId, int rfqId, int? supportFileId, string quotNo, List<IFormFile> files)
        {
             if (supportFileId.HasValue && supportFileId.Value > 0)
                {
                    var existsFile = _context.SupplierAttachment
                        .Include(x => x.SupportFile)
                        .FirstOrDefault(x =>
                    x.SupplierInfoId.ToString() == supplierId && x.RFQId == rfqId && x.SupportFileId == supportFileId.Value);

                    if (existsFile != null)
                    {
                        var filePath = Path.Combine(_env.WebRootPath, "documents", existsFile.SupportFile.FileName);

                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }

                        _context.SupplierAttachment.Remove(existsFile);
                        _context.SaveChanges();
                    }
                }

                var upFiles = await _fileWriter.UploadDocument(files.FirstOrDefault());
                var newAttachFile = new SupplierAttachment
                {
                    RFQId = rfqId,
                    SupportFile = upFiles,
                    SupplierInfoId = Guid.Parse(supplierId),
                    QuotNo = quotNo
                };

                _context.SupplierAttachment.Add(newAttachFile);
                _context.SaveChanges();

            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> deleteAttachFile(string supplierId,int rfqId,int? supportFileId)
        {
            if (supportFileId.HasValue && supportFileId > 0)
            {
                var supplierAttachment = _context.SupplierAttachment
                .Where(x => x.SupplierInfoId.ToString() == supplierId && x.RFQId == rfqId && x.Id == supportFileId)
                .FirstOrDefault();

                if (supplierAttachment != null)
                {
                    var sf = _context.SupportFile.FirstOrDefault(x => x.Id == supplierAttachment.SupportFileId);

                    if (sf != null)
                    {
                        _fileWriter.DeleteDocument(sf.FileName);
                        _context.SupportFile.Remove(sf);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            
            return NoContent();
        }

        public string generateViewURL(SupportFile supportFile)
        {
            if (CommonHelpers.isImage(supportFile.ContentType))
            {
                return Url.Action("PreviewImage", "BOM", new { id = supportFile.Id }, HttpContext.Request.Scheme);
            }
            else if (supportFile.ContentType.StartsWith(".xl"))
            {
                var documentPath = Path.Combine(_env.WebRootPath, "documents", supportFile.FileName);

                if (!System.IO.File.Exists(documentPath))
                {
                    throw new Exception("File No Found!");
                }

                var url = Url.Action("downloadFile", "BOM", new { id = supportFile.Id });

                return url;
            }
            else
            {
                return Url.Action("ShowPDF", "BOM", new { supportFileId = supportFile.Id }, HttpContext.Request.Scheme);
            }
        }


        [HttpPost("UploadBase64")]  // Change from HttpGet to HttpPost
        public async Task<IActionResult> UploadBase64([FromForm] string supplierId, [FromForm] int rfqId, [FromForm] string quotNo, [FromForm] int supportFileId , string base64String,[FromForm] string fileName,[FromForm] string contentType = null)
                      
        {
            try
            {
                // Convert base64 to IFormFile
               var formFile = ConvertBase64ToFormFile(base64String, fileName, contentType);

                if (formFile == null)
                {
                    return BadRequest("Invalid file data");
                }
              await CopySupplierWithAttach(supplierId, rfqId, supportFileId, quotNo,formFile);

                return Ok(new
                {
                    message = "File uploaded successfully",
                    //fileName = formFile.FileName,
                    //size = formFile.Length,
                    //contentType = formFile.ContentType,
                    //path = filePath
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error uploading file: {ex.Message}");
            }
        }

        public static List<IFormFile> ConvertBase64ToFormFile(string base64String, string fileName, string contentType = null)
        {
            if (string.IsNullOrEmpty(base64String))
                return null;

            var base64Data = base64String.Split(',')[1];
            var bytes = Convert.FromBase64String(base64Data);
            var stream = new MemoryStream(bytes);

            // Create a list with a single IFormFile
            var formFile = new FormFile(
                baseStream: stream,
                baseStreamOffset: 0,
                length: bytes.Length,
                name: "file",
                fileName: fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType ?? GetMimeType(fileName)
            };

            return new List<IFormFile> { formFile };
        }

        private static string GetMimeType(string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return contentType;
        }
    }
}
