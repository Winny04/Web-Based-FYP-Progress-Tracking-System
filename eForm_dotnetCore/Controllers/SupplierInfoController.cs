using DocumentFormat.OpenXml.Drawing;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace eForm_dotnetCore.Controllers
{
    public class SupplierInfoController: Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IEmailService _emailService;

        public SupplierInfoController(EformSophicDemoContext context,
            IWebHostEnvironment _environment,
            IFileWriter fileWriter,
            IAuthorizationService authorizeService,
            IEmailService emailService,
            IAuditTrailService auditTrailService)
        {
            _context = context;
            Environment = _environment;
            _fileWriter = fileWriter;
            _authorizeService = authorizeService;
            _auditTrailService = auditTrailService;
            _emailService = emailService;
        }

        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,SuperAdmin,SuperAdminAssistant,Purchaser")]
        public ActionResult Index()
        {
            var supplierList = _context.SupplierInfo.ToList();

            var supplierListVM = new List<SupplierVM>();

            foreach(var supplier in supplierList)
            {
                var model = new SupplierVM();
                if (!string.IsNullOrEmpty(supplier.DataResponse))
                {
                    var root = JsonConvert.DeserializeObject<SupplierRoot>(supplier.DataResponse);
                    model = root.data.ToModel<SupplierVM>();
                }

                model.Id = supplier.Id;
                model.Completed = supplier.Completed ? "Y" : "N";
                model.companyName = supplier.VendorName;

                supplierListVM.Add(model);
            }

            return View(supplierListVM);
        }
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,SuperAdmin,SuperAdminAssistant")]
        public ActionResult SupportFiles(Guid id)
        {
            var vm = new SupplierSupportFileVM();

            var supplierInfo = _context.SupplierInfo.FirstOrDefault(x => x.Id == id);

            vm.SupplierId = supplierInfo.Id.ToString();
            vm.SupplierName = supplierInfo.VendorName;

            var supplierFiles = _context.SupplierSupportFile
                .Include(x => x.SupportFile)
                .Where(x => x.SupplierInfoId == id);

            foreach(var sf in supplierFiles)
            {
                var convert = sf.ToModel<SupportFileVM>();
                convert.FileUrl = generateViewURL(convert.SupportFile);
                vm.SupportFileVM.Add(convert);
            }

            return View(vm);
        }
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,SuperAdmin,SuperAdminAssistant")]
        public ActionResult removeFile(int id)
        {
            var supplierFile = _context.SupplierSupportFile
               .Where(x => x.Id == id).FirstOrDefault();

            var supplierId = new Guid();

            if(supplierFile != null)
            {
                supplierId = supplierFile.SupplierInfoId;
                _context.SupplierSupportFile.Remove(supplierFile);
                _context.SaveChanges();
            }

            return RedirectToAction("SupportFiles", new { id = supplierId.ToString() });
        }

        public string generateViewURL(SupportFile supportFile)
        {
            if (CommonHelpers.isImage(supportFile.ContentType))
            {
                return Url.Action("PreviewImage", "BOM", new { id = supportFile.Id }, HttpContext.Request.Scheme);
            }
            else if (supportFile.ContentType.StartsWith(".xl"))
            {
                var documentPath = System.IO.Path.Combine(Environment.WebRootPath, "documents", supportFile.FileName);

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

        public async Task<ActionResult> ImportData(IFormFile file)
        {
            if (file == null) return BadRequest();

            var filePath = string.Empty;
            var supplierList = new List<SupplierInfo>();

            try
            {
                filePath = System.IO.Path.Combine(Environment.WebRootPath, Guid.NewGuid().ToString() + file.FileName);

                using (FileStream fstream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fstream);
                }

                using (FileStream ostream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(ostream);
                    ISheet sheet = workbook.GetSheet("Sheet2");
                    for (int row = 0; row <= sheet.LastRowNum; row++)
                    {
                        if(row == sheet.LastRowNum)
                        {
                            var getname = sheet.GetRow(row).GetCell(1).ToString();
                        }

                        if (sheet.GetRow(row).GetCell(1) == null && string.IsNullOrEmpty(sheet.GetRow(row).GetCell(1).ToString())) continue;

                        var newSupplier = new SupplierInfo { Completed = false,VendorName = sheet.GetRow(row).GetCell(1).ToString()};
                        var supplierData = new SupplierData();

                        supplierData.registrationCode = sheet.GetRow(row).GetCell(0).ToString().Trim();
                        supplierData.companyName =  sheet.GetRow(row).GetCell(1).ToString().Trim();
                        supplierData.address = sheet.GetRow(row).GetCell(2).ToString().Trim();
                        supplierData.telephoneNumber = sheet.GetRow(row).GetCell(3).ToString().Trim();
                        supplierData.faxNumber = sheet.GetRow(row).GetCell(5).ToString().Trim();
                        supplierData.emailAddress = sheet.GetRow(row).GetCell(6).ToString().Trim();
                        supplierData.websiteAddress = sheet.GetRow(row).GetCell(7).ToString().Trim();
                        supplierData.creditTerms = sheet.GetRow(row).GetCell(9).NumericCellValue;
                        supplierData.accountCurrency = sheet.GetRow(row).GetCell(10).ToString().Trim();

                        var supplierRoot = new SupplierRoot();
                        supplierRoot.data = supplierData;

                        newSupplier.DataResponse = System.Text.Json.JsonSerializer.Serialize(supplierRoot);

                        supplierList.Add(newSupplier);
                    }
                }

                _context.SupplierInfo.AddRange(supplierList);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { throw new Exception(ex.Message, ex); }
            finally
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            return Ok();
        }

        [HttpGet]
        public JsonResult GetSupplierList()
        {
            var supplierList = _context.SupplierInfo.Select(x => new { id = x.Id,name = x.VendorName}).ToList();

            return Json(supplierList);
        }
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,SuperAdmin,SuperAdminAssistant")]
        public JsonResult AddSupplier(string supplierName)
        {
            var msg = "Please provide a valid vendor name";
            var result = false;
            Guid vendorId = Guid.Empty;

            if (!string.IsNullOrEmpty(supplierName))
            {
                if(_context.SupplierInfo.Any(x => x.VendorName == supplierName))
                {
                    msg = "The given vendor name already exists.";
                }
                else
                {
                    var newSupplier = new SupplierInfo();
                    var newSupplierRoot = new SupplierRoot();
                    var newSupplierData = new SupplierData { companyName = supplierName };
                    newSupplierRoot.data = newSupplierData;
                    newSupplier.Completed = false;
                    newSupplier.VendorName = supplierName;
                    newSupplier.DataResponse = System.Text.Json.JsonSerializer.Serialize(newSupplierRoot);
                    _context.SupplierInfo.Add(newSupplier);
                    _context.SaveChanges();
                    vendorId = newSupplier.Id;

                    result = true;
                }
            }

            return Json(new { result, msg, vendorId });
        }
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,SuperAdmin,SuperAdminAssistant")]
        public ActionResult DeleteSupplier(Guid id)
        {
            var supplier = _context.SupplierInfo.FirstOrDefault(x => x.Id == id);

            if(supplier != null)
            {
                _context.SupplierInfo.Remove(supplier);
                _context.SaveChanges();
            }

            return Ok();
        }

        [HttpGet]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Buyer,Purchaser,SuperAdmin,SuperAdminAssistant")]
        public ActionResult Edit(Guid id)
        {
            var supplier = _context.SupplierInfo.FirstOrDefault(x => x.Id == id);
            var components = _context.Forms.Where(x => x.Name == "Vendor_Form").FirstOrDefault();

            if (supplier != null && components != null)
            {
                var vm = supplier.ToModel<SupplierInfoEditModel>();

                if (string.IsNullOrEmpty(supplier.DataResponse))
                {
                    var initSupplierData = new SupplierRoot();
                    initSupplierData.data.companyName = supplier.VendorName;
                    vm.DataResponse = JsonConvert.SerializeObject(initSupplierData);
                }

                vm.Data = components.Data;

                return View(vm);
            }

            return NotFound();
        }

        [HttpPost]
        public ActionResult Edit(Guid id,string DataResponse)
        {
            var supplier = _context.SupplierInfo.FirstOrDefault(x => x.Id == id);

            if (supplier != null && !string.IsNullOrEmpty(DataResponse))
            {
                supplier.DataResponse = DataResponse;
                var root = JsonConvert.DeserializeObject<SupplierRoot>(supplier.DataResponse);
                if (root.data.companyName != null)
                {
                    supplier.VendorName = root.data.companyName;
                }
                _context.SupplierInfo.Update(supplier);
                _context.SaveChanges();
                return Ok();
            }

            return NotFound();
        }

        [HttpPost]
        public async Task<ActionResult> attachFile(string supplierId, List<IFormFile> files,string title)
        {
            var supplier = _context.SupplierInfo.FirstOrDefault(x => x.Id == Guid.Parse(supplierId));

            if (files != null)
            {
                var upFile = await _fileWriter.UploadDocument(files[0]);

                upFile.CreatedAt = DateTime.Now;
                supplier.SupplierSupportFiles.Add(new SupplierSupportFile
                {
                    SupportFile = upFile,
                    Title = title
                });

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(SupportFiles), new { id = supplierId });

        }
    }
}
