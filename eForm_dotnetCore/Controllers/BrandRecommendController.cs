using ClosedXML.Excel;
using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoreLinq;
using static eForm_dotnetCore.Controllers.RFQSharingController;

namespace eForm_dotnetCore.Controllers
{
    public class BrandRecommendController:Controller
    {

        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IEmailService _emailService;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;

        public BrandRecommendController(EformSophicDemoContext context
            , IWebHostEnvironment _environment
            , IFileWriter fileWriter
            , IAuthorizationService authorizeService
            , IEmailService emailService
            , IAuditTrailService auditTrailService)
        {
            _context = context;
            Environment = _environment;
            _fileWriter = fileWriter;
            _authorizeService = authorizeService;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
        }

        public IActionResult Index()
        {
            var brands = _context.Brand
                .Include(x => x.BrandSuppliers)
                .ThenInclude(x => x.Supplier)
                .ToList();

            brands.ForEach(x =>
            {
                x.BrandSuppliers = x.BrandSuppliers.Where(x => x.IsPrimary).ToList();
            });

            return View(brands);
        }

        [HttpGet]
        public IActionResult Edit(Guid? id,string? brand)
        {
            if (id.HasValue)
            {
                var brands = _context.Brand
                    .Include(x => x.BrandSuppliers)
                    .ThenInclude(x => x.Supplier)
                    .FirstOrDefault(x => x.Id == id);

                return View(brands);
            }
            else
            {
                var newItem = new Brand
                {
                    Name = brand,
                    BrandSuppliers = new List<BrandSupplier>()
                };
                _context.Brand.Add(newItem);
                _context.SaveChanges();
                return View(newItem);
            }
        }

        [HttpPost]
        public JsonResult Edit(Guid id,List<string>? suppliers)
        {
            var brandSuppliers = _context.BrandSupplier.Where(x => x.BrandId == id).ToList();

            brandSuppliers.ForEach(x =>
            {
                if (suppliers.Contains(x.SupplierId.ToString()))
                {
                    x.IsPrimary = true;
                }
                else
                {
                    x.IsPrimary = false;
                }
            });

            _context.BrandSupplier.UpdateRange(brandSuppliers);
            _context.SaveChanges();

            return Json(new { result = true });
        }

        [HttpGet]
        public JsonResult validateBrand(string brand)
        {
            if (string.IsNullOrEmpty(brand))
            {
                return Json(new { result = false, msg = "Please provide a valid brand" });
            }

            var checkBrand = _context.Brand.Any(x => x.Name.ToLower() == brand.ToLower());
            if (checkBrand)
            {
                return Json(new { result = false, msg = "The brand already exists" });
            }

            return Json(new { result = true });
        }

        [HttpPost]
        public JsonResult Delete(Guid id)
        {
            var record = _context.Brand.FirstOrDefault(x => x.Id == id);

            if (record != null)
            {
                _context.Brand.Remove(record);
                _context.SaveChanges();
            }

            return Json(null);
        }

        public IActionResult AddSupplier(Guid id)
        {
            var bs = _context.BrandSupplier.Where(x => x.Id == id).Select(x => x.SupplierId)
                .ToList();
            var supplierList = _context.SupplierInfo
                .Where(x => !bs.Contains(x.Id))
                .OrderBy(x => x.VendorName)
                .ToList();
            var vm = new AddBrandSupplierModel
            {
                BrandId = id,
                Suppliers = supplierList
            };

            return PartialView("_AddSupplierForBrand", vm);
        }

        [HttpPost]
        public IActionResult AddSupplier(Guid id, Guid supplierId)
        {
            var check = _context.BrandSupplier.FirstOrDefault(x => x.BrandId == id && x.SupplierId == supplierId);

            if (check == null)
            {
                var newBS = new BrandSupplier
                {
                    BrandId = id,
                    SupplierId = supplierId,
                    IsPrimary = false,
                };
                _context.BrandSupplier.Add(newBS);

                _context.SaveChanges();
            }

            return RedirectToAction("Edit", new { id });
        }

        [HttpGet]
        public IActionResult ExportToExcel()
        {
            var brands = _context.Brand
                .Include(x => x.BrandSuppliers)
                .ThenInclude(x => x.Supplier)
                .ToList();

            brands.ForEach(x =>
            {
                x.BrandSuppliers = x.BrandSuppliers.Where(x => x.IsPrimary).ToList();
            });

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Brand Recommendation");
                var currentRow = 1;
                worksheet.Cell(currentRow, 1).Value = "Brand";
                worksheet.Cell(currentRow, 2).Value = "Supplier 1";
                worksheet.Cell(currentRow, 3).Value = "Supplier 2";
                worksheet.Cell(currentRow, 4).Value = "Supplier 3";

                for(var i = 1; i <= 4; i++)
                {
                    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                }

                currentRow += 1;

                foreach (var brand in brands)
                {
                    worksheet.Cell(currentRow, 1).Value = brand.Name;
                    var colNum = 2;
                    foreach(var supplier in brand.BrandSuppliers)
                    {
                        worksheet.Cell(currentRow, colNum).Value = supplier.Supplier.VendorName;
                        colNum += 1;
                    }
                    currentRow += 1;
                }

                worksheet.Columns().AdjustToContents();
                worksheet.Columns().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "BrandRecommendation.xlsx");
                }
            }
        }

        public class AddBrandSupplierModel
        {
            public Guid BrandId { get; set; }
            public List<SupplierInfo> Suppliers { get; set; } = new List<SupplierInfo>();
        }
    }
}
