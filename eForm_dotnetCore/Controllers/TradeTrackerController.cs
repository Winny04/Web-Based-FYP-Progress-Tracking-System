using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Office2010.Excel;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NPOI.POIFS.Properties;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace eForm_dotnetCore.Controllers
{
    public class TradeTrackerController: Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public TradeTrackerController(EformSophicDemoContext context,
            IEmailService emailService,
            IAuditTrailService auditTrailService,
            IWebHostEnvironment environment
            )
        {
            _context = context;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
            _env = environment;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var lists = _context.TradeTracker.ToList();

            return View(lists);
        }

        [HttpGet]
        public IActionResult Edit(Guid? id)
        {

            if (id.HasValue)
            {
                var item = _context.TradeTracker.FirstOrDefault(x => x.Id == id);

                if (item == null)
                {
                    RedirectToAction("Error404", "Home", new { msg = "Cant found the select item" });
                }

                return View(item);
            }
            else
            {
                var newItem = new TradeTracker();
                newItem.StartedDate= DateTime.Now;
                return View(newItem);
            }
        }

        [HttpPost]
        public IActionResult Edit(TradeTracker tracker)
        {
            if (tracker.Id.Equals(Guid.Empty))
            {
                _context.TradeTracker.Add(tracker);
            }
            else
            {
                _context.TradeTracker.Update(tracker);
            }

            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public JsonResult DeleteRecord(Guid id)
        {
            var record = _context.TradeTracker.FirstOrDefault(x => x.Id == id);

            if(record != null)
            {
                _context.TradeTracker.Remove(record);
                _context.SaveChanges();
            }

            return Json(null);
        }

        [HttpPost]
        public async Task<ActionResult> ImportData(IFormFile file)
        {
            if (file == null) return BadRequest();

            var filePath = string.Empty;
            var bomList = new List<TradeTracker>();

            try
            {
                filePath = System.IO.Path.Combine(_env.WebRootPath, Guid.NewGuid().ToString() + file.FileName);

                using (FileStream fstream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fstream);
                }

                using (FileStream ostream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(ostream);
                    ISheet sheet = workbook.GetSheet("Sheet1");
                    for (int row = 1; row <= sheet.LastRowNum; row++)
                    {
                        if (row == sheet.LastRowNum)
                        {
                            var getname = sheet.GetRow(row).GetCell(1).ToString();
                        }

                        if (sheet.GetRow(row).GetCell(0) == null && string.IsNullOrEmpty(sheet.GetRow(row).GetCell(0).ToString())) continue;

                        var newTrade = new TradeTracker();

                        newTrade.StartedDate = sheet.GetRow(row).GetCell(0) == null ? null
                            : sheet.GetRow(row).GetCell(0).DateCellValue;
                        newTrade.SalesPIC = sheet.GetRow(row).GetCell(1) == null ? null : sheet.GetRow(row).GetCell(1).ToString();
                        newTrade.Description = sheet.GetRow(row).GetCell(2) == null ? null : sheet.GetRow(row).GetCell(2).ToString();
                        newTrade.Brand = sheet.GetRow(row).GetCell(3) == null ? null : sheet.GetRow(row).GetCell(3).ToString();
                        newTrade.PartNo = sheet.GetRow(row).GetCell(4) == null ? null : sheet.GetRow(row).GetCell(4).ToString();
                        newTrade.Quantity = sheet.GetRow(row).GetCell(5) == null ? 0
                        : (int)sheet.GetRow(row).GetCell(5).NumericCellValue;
                        newTrade.ChancesPO = sheet.GetRow(row).GetCell(6) == null ? null : sheet.GetRow(row).GetCell(6).ToString();
                        newTrade.StatusSource = sheet.GetRow(row).GetCell(7) == null ? null : sheet.GetRow(row).GetCell(7).ToString();
                        newTrade.Supplier = sheet.GetRow(row).GetCell(8) == null ? null : sheet.GetRow(row).GetCell(8).ToString();
                        newTrade.TargetPrice = sheet.GetRow(row).GetCell(9) == null ? 0 : (decimal)sheet.GetRow(row).GetCell(9).NumericCellValue;
                        newTrade.FinalPrice = sheet.GetRow(row).GetCell(10) == null ? 0 : (decimal)sheet.GetRow(row).GetCell(10).NumericCellValue;
                        newTrade.TargetLeadTime = sheet.GetRow(row).GetCell(11) == null ? null : sheet.GetRow(row).GetCell(11).DateCellValue;
                        newTrade.LeadTime = sheet.GetRow(row).GetCell(12) == null ? null : sheet.GetRow(row).GetCell(12).DateCellValue;
                        newTrade.Warranty = sheet.GetRow(row).GetCell(13) == null ? 0 :
                        (int)sheet.GetRow(row).GetCell(13).NumericCellValue;
                        newTrade.Remark = sheet.GetRow(row).GetCell(14) == null ? null : sheet.GetRow(row).GetCell(14).ToString();
                        newTrade.ClosedDate = sheet.GetRow(row).GetCell(15) == null ? null : sheet.GetRow(row).GetCell(15).DateCellValue;
                        newTrade.StatusSales = sheet.GetRow(row).GetCell(16) == null ? null : sheet.GetRow(row).GetCell(16).ToString();
                        newTrade.CustomerPO = sheet.GetRow(row).GetCell(17) == null ? null : sheet.GetRow(row).GetCell(17).ToString();
                        newTrade.ReasonPending = sheet.GetRow(row).GetCell(18) == null ? null : sheet.GetRow(row).GetCell(18).ToString();

                        bomList.Add(newTrade);
                    }
                }

                _context.TradeTracker.AddRange(bomList);
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

            return RedirectToAction(nameof(Index));
        }
    }
}
