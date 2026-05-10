using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Office2010.Excel;

//using DocumentFormat.OpenXml.Drawing;
//using DocumentFormat.OpenXml.InkML;
using eForm_dotnetCore.Common;
using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using iTextSharp.xmp.impl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.GeometriesGraph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.HPSF;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using System.IO.Compression;
using static eForm_dotnetCore.ViewModels.RFQExportModel;

namespace eForm_dotnetCore.Controllers
{
    public class RFQSharingController:Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IFileWriter _fileWriter;
        private IWebHostEnvironment _env;
        private readonly IAuditTrailService _auditTrailService;
        public RFQSharingController(EformSophicDemoContext context
            ,IEmailService emailService,
            IWebHostEnvironment env,
            IFileWriter fileWriter,
            IAuditTrailService auditTrailService)
        {
            _context = context;
            _emailService = emailService;
            _env = env;
            _fileWriter = fileWriter;
            _auditTrailService = auditTrailService;
        }

        [AllowAnonymous]
        public ActionResult Index(Guid id, int bomId, bool fromImport = false)
        {
            var checkSR = _context.RFQSharing.Include(x => x.Sourcing).FirstOrDefault(x => x.Id == id);
            if (checkSR == null) 
            {
                return RedirectToAction("Error404", "Home", new { msg = "The sharing RFQ have been remove,please ask for new RFQ if need." });
            }

            var rfqTemp = _context.Forms.FirstOrDefault(x => x.Name == "RFQ4_outsider");
            var checkRFQ = _context.Rfqs.Where(x => x.Id == checkSR.Sourcing.RFQId).FirstOrDefault();
            var checkStatus = _context.Boms.Where(x => x.RFQId == checkSR.Sourcing.RFQId).FirstOrDefault();

            if (!HttpContext.User.Identity.IsAuthenticated  && checkStatus.Status != "New" && checkStatus.Status != "RFQ Sent")
            {
                return RedirectToAction("Error404", "Home", new { msg = "The form has been block for edit as it was already confirm by buyer" });
            }

            ViewBag.isFabrication = checkStatus.ItemCategory == "Fabrication";
            ViewBag.bomId = bomId;

            var rfqId = _context.Boms
                .Where(x => x.Id == bomId)
                .Select(x => x.RFQId)
                .FirstOrDefault();
            ViewBag.rfqId = rfqId;

            var rsModel = new RSModel();
            rsModel = checkSR.ToModel<RSModel>();
            rsModel.Data = rfqTemp.Data;
            rsModel.RFQName = checkRFQ.Rfqname;

            if (string.IsNullOrEmpty(rsModel.DataResponse))
            {
                rsModel.DataResponse = checkRFQ.DataResponse;      
            }

            if (!string.IsNullOrEmpty(rsModel.DataResponse))
            {
                var jsonObj = JObject.Parse(rsModel.DataResponse);
                if (jsonObj["data"] != null)
                {
                    jsonObj["data"]["LatestSupplierRemark"] = checkRFQ.LatestSupplierRemark;
                    rsModel.DataResponse = jsonObj.ToString();
                }
            }

            if (!fromImport && (!rsModel.SubmittedDate.HasValue && !rsModel.Rejected))
            {
                var jsonObj = JObject.Parse(rsModel.DataResponse);

                var bomGrid = jsonObj["data"]?["bomGrid"] as JArray;
                if (bomGrid != null)
                {
                    foreach (var item in bomGrid)
                    {
                        item["unitPrice"] = null;
                        item["totalPrice"] = null;
                        item["leadTime"] = null;
                        item["eta"] = null;
                    }
                }

                var dataToken = jsonObj["data"];
                if (dataToken != null)
                {
                    dataToken["discountTotal"] = null;
                    dataToken["taxSelect"] = null;
                    dataToken["shippingFee"] = null;
                    dataToken["bankCharges"] = null;
                    dataToken["netPrice"] = null;
                }

                rsModel.DataResponse = jsonObj.ToString();
            }

            // if(!rsModel.SubmittedDate.HasValue && !rsModel.Rejected)
            //if(!fromImport)
            //{
            //    var jsonObj = JObject.Parse(rsModel.DataResponse);

            //    var bomGrid = jsonObj["data"]?["bomGrid"] as JArray;
            //    if (bomGrid != null)
            //    {
            //        foreach (var item in bomGrid)
            //        {
            //            item["unitPrice"] = null;
            //            item["totalPrice"] = null;
            //        }
            //    }
            //    rsModel.DataResponse = jsonObj.ToString();
            //}

            return View(rsModel);
        }

        public async Task<ActionResult> Approve(Guid id)
        {
            var sr = _context.RFQSharing.
                Include(x => x.Sourcing).FirstOrDefault(x => x.Id == id);

            if (sr == null) return NotFound();

            var source = _context.Sourcing
                .Include(x => x.RFQ)
                .FirstOrDefault(x => x.Id == sr.SourceId);

            if (!string.IsNullOrEmpty(sr.DataResponse))
            {
                var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(sr.DataResponse);

                var sr_suppliers = rfqRoot.data.supplierList;

                var supplierWithQuos = sr_suppliers.Where(x => x.supplierKey != null && x.quotation.Any()).ToList();
                var supplierWithoutQuos = sr_suppliers.Where(x => !x.quotation.Any()).ToList();

                if (supplierWithQuos.Count > 0)
                {
                    foreach(var sq in supplierWithQuos)
                    {
                        var fileName = _fileWriter.CheckAndGetFileName(sq.quotation[0].originalName); //chnge
                        var filePath = Path.Combine(_env.WebRootPath, "documents", fileName);
                        var base64StringIndex = sq.quotation[0].url.IndexOf(",");
                        var base64String = sq.quotation[0].url.Substring(base64StringIndex + 1);
                        var fileByte = Convert.FromBase64String(base64String);

                        await System.IO.File.WriteAllBytesAsync(filePath, fileByte);

                        var supportFile = new SupportFile
                        {
                            Size = new FileInfo(filePath).Length.ToString(),
                            ContentType = Path.GetExtension(fileName),
                            CreatedAt = DateTime.Now,
                            FileName = fileName,
                        };

                        _context.SupportFile.Add(supportFile);
                        _context.SaveChanges();

                        var supplierAttachment = new SupplierAttachment
                        {
                            SupportFileId = supportFile.Id,
                            SupplierInfoId = Guid.Parse(sq.supplierKey.id),
                            QuotNo= sq.quotNoLink,
                            RFQId = sr.Sourcing.RFQId,
                            IsRecommended = sq.recommended ?? false,
                            Remark = sq.remark,
                            R_Remark = sq.r_remark,
                        };

                        _context.SupplierAttachment.Add(supplierAttachment);
                        _context.SaveChanges();
                    }

                    rfqRoot.data.supplierList = supplierWithoutQuos;
                }

                //sr.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
                var oriRfq = _context.Rfqs.FirstOrDefault(x => x.Id == sr.Sourcing.RFQId);

                if(oriRfq != null)
                {
                    oriRfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
                    _context.SaveChanges();
                }
            }

            _context.RFQSharing.Remove(sr);
            _context.SaveChanges();

            await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, $@"Rfq sharing with id {sr.Id} has been approve for supplier id {sr.SupplierId}",
                        tableChange: nameof(RFQSharing),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: source.RFQ.ProjectId
                        );

            return RedirectToAction(nameof(Manage));

        }

        public ActionResult Manage(Guid sourceId)
        {
            var srs = _context.RFQSharing
                .Include(x => x.Sourcing)
                .ThenInclude(x => x.RFQ)
                .Include(x => x.Supplier)
                .Where(x => x.SourceId == sourceId)
                .ToList();

            return View(srs);
        }

        public IActionResult AddSupplier(Guid id)
        {
            var source = _context.Sourcing.FirstOrDefault(x => x.Id == id);
            var supplierList = _context.SupplierInfo.OrderBy(x => x.VendorName).ToList();
            var vm = new AddSourceSupplierModel
            {
                SourceId = id,
                Suppliers = supplierList
            };

            return PartialView("_AddSupplierForSource", vm);
        }


        [HttpPost]
        public async Task<IActionResult> AddSupplier(Guid id,Guid supplierId)
        {
            var check = _context.RFQSharing.FirstOrDefault(x => x.SourceId == id && x.SupplierId == supplierId);
            var source = _context.Sourcing.FirstOrDefault(x => x.Id == id);
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == source.RFQId);

            if (check == null)
            {
                var newRS = new RFQSharing
                {
                    SourceId = id,
                    SupplierId = supplierId,
                    CreatedDate = DateTime.Now
                };
                _context.RFQSharing.Add(newRS);

                _context.SaveChanges();

                var supplierDetails = _context.SupplierInfo.FirstOrDefault(x => x.Id == supplierId);
                var email = GetSupplierEmail(supplierDetails.DataResponse);

                if (!string.IsNullOrEmpty(email))
                {
                    var emailContent = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == EmailTemplateConfiguration.SREmailContent);
                    var rsLink = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}/RFQSharing/Index?id={newRS.Id.ToString()}";
                    var rsUrl = $@"<a href='{rsLink}' target='_blank'>this link</a> or paste url in browser with {rsLink}";
                    string formattedString = emailContent.ConfigValue.Replace("{vendor}", supplierDetails.VendorName)
                                          .Replace("{url}", rsUrl);

                    _emailService.Send(email, $@"Subject: [QUOTATION REQUEST] {rfq.Rfqname} from Sophic", formattedString,
                       cc: "internal.system@sophicautomation.com");
                    

                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, $@"Additional rfq sharing was sending to supplier id {supplierId}",
                        tableChange: nameof(RFQSharing),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: source.RFQ.ProjectId
                        );
                }
            }

            return RedirectToAction("Manage", new { sourceId = id });
        }

        private string? GetSupplierEmail(string? data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;

            var supplierRoot = JsonConvert.DeserializeObject<SupplierRoot>(data);

            if (supplierRoot != null)
            {
                return supplierRoot.data.emailAddress;
            }

            return string.Empty;
        }

        public class AddSourceSupplierModel
        {
            public Guid SourceId { get; set; }
            public List<SupplierInfo> Suppliers { get; set; } = new List<SupplierInfo>();
        }


        [HttpPost]
        public async Task<ActionResult> DeleteSR(Guid id)
        {
            var checkItem = _context.RFQSharing.FirstOrDefault(x => x.Id == id);

            if (checkItem != null)
            {
                var source = _context.Sourcing.FirstOrDefault(x => x.Id == id);
                _context.RFQSharing.Remove(checkItem);
                _context.SaveChanges();

                await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, $@"Rfq sharing with supplier id {checkItem.SupplierId} was removed from source id {checkItem.SourceId}",
                        tableChange: nameof(RFQSharing),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: source.RFQ.ProjectId
                        );
            }

            return Ok();
        }

        public class RSModel : RFQSharing
        { 
            public string RFQName { get; set; }
            public string? Data { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> SubmitSR(Guid id, string dataResponse)
        {
            if (!id.Equals(Guid.Empty))
            {
                var checkSR = _context.RFQSharing
                    .Include(x => x.Supplier)
                    .FirstOrDefault(x => x.Id == id);
                if (checkSR == null) return NotFound();

                checkSR.Rejected = false;
                checkSR.SubmittedDate = DateTime.Now;
                var currentRFQ = JsonConvert.DeserializeObject<RFQSharingRoot>(dataResponse);

                currentRFQ.data.bomGrid.ForEach(x => x.selectedSupplier = checkSR.SupplierId.ToString());

                checkSR.DataResponse = System.Text.Json.JsonSerializer.Serialize(currentRFQ);

                checkSR.LeadTimeDays = currentRFQ.data.bomGrid[0].leadTime;
                DateTime today = DateTime.Now.Date;
                var leadTimeValue = currentRFQ.data.bomGrid[0].leadTime;

                int leadDays = 0;

                if (leadTimeValue != null)
                {
                    if (int.TryParse(leadTimeValue.ToString(), out int parsedDays))
                    {
                        leadDays = parsedDays;
                    }
                }

                checkSR.LeadTime = today.AddDays(leadDays);
                //checkSR.LeadTime = currentRFQ.data.bomGrid[0].eta;
                checkSR.TotalPrice = (decimal)currentRFQ.data.bomGrid.Sum(x => x.totalPrice);
                _context.SaveChanges();

                // Remark: vendor submitted via SHARERFQ sharing link
                var parentRfqId = _context.Sourcing
                    .Where(x => x.Id == checkSR.SourceId)
                    .Select(x => x.RFQId)
                    .FirstOrDefault();
                var parentRfq = _context.Rfqs.FirstOrDefault(x => x.Id == parentRfqId);
                if (parentRfq != null)
                {
                    parentRfq.LatestSupplierRemark = $"Vendor, system submitted the quotation";
                    _context.Entry(parentRfq).Property(x => x.LatestSupplierRemark).IsModified = true;
                    _context.SaveChanges();
                }

                bool autoapprove = await CheckAutoApprove(id, checkSR.TotalPrice, dataResponse);
                if (autoapprove)
                    await AutoapprovedRFQ(checkSR.SupplierId.ToString(), dataResponse, checkSR.TotalPrice);

                var otherSR = _context.RFQSharing.Where(x => x.Id != id && x.SourceId == checkSR.SourceId && x.SubmittedDate.HasValue).ToList();
                var updateSource = _context.Sourcing
                    .Include(x => x.RFQ)
                    .FirstOrDefault(x => x.Id == checkSR.SourceId);

                var compareSR = new List<RFQSharing> { checkSR };
                compareSR.AddRange(otherSR);

                // Get all submissions
                var validSR = compareSR
                    .Where(x => x.LeadTime.HasValue && x.TotalPrice.HasValue)
                    .ToList();

                // Get the earliest delivery month/year
                var earliestMonth = validSR
                    .Select(x => new { x.LeadTime.Value.Year, x.LeadTime.Value.Month })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .First();

                // Filter suppliers in the fastest ETA month
                var fastestMonthSuppliers = validSR
                    .Where(x => x.LeadTime.Value.Year == earliestMonth.Year &&
                                x.LeadTime.Value.Month == earliestMonth.Month)
                    .ToList();

                // Pick the one with lowest price
                var bestSupplier = fastestMonthSuppliers
                    .OrderBy(x => x.TotalPrice)
                    .First();

                // Assign values
                updateSource.BestLeadTimeSupplierId = bestSupplier.SupplierId;
                updateSource.BestPriceSupplierId = validSR.OrderBy(x => x.TotalPrice).First().SupplierId;
                updateSource.FinalLeadTime = bestSupplier.LeadTime;
                updateSource.FinalPrice = bestSupplier.TotalPrice;

                // Assign main supplier (fastest ETA month, then lowest price in that month)
                updateSource.SupplierId = bestSupplier.SupplierId;

                var buyers = _context.EmployeeInfos.Where(x => x.IsNotificationOn
                && x.UserType == "Buyer" && !string.IsNullOrEmpty(x.Email)).ToList();
                var emails = buyers.Select(x => x.Email).ToList();
                var buyerIds = buyers.Select(x => x.Id).ToList();

                if (buyers.Count > 0)
                {
                    var details = @$"Supplier {checkSR.Supplier.VendorName} has been submit the rfq sharing for RFQ {updateSource.RFQ.Rfqname}";
                    await _emailService.Send(emails, "Sharing RFQ has been submit", details);

                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, details,
                        pics: buyerIds,
                        tableChange: nameof(RFQSharing),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: updateSource.RFQ.ProjectId
                        );

                }

                return Ok(new { autoapprove });
                //return Ok();
            }
            return NotFound();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult RejectSR(Guid id, string dataResponse)
        {
            if (!id.Equals(Guid.Empty))
            {
                var checkSR = _context.RFQSharing
                    .Include(x => x.Supplier)
                    .FirstOrDefault(x => x.Id == id);
                if (checkSR == null) return NotFound();

                checkSR.Rejected = true;
                _context.SaveChanges();

                var updateSource = _context.Sourcing
                    .Include(x => x.RFQ)
                    .FirstOrDefault(x => x.Id == checkSR.SourceId);

                //return Ok();
                return RedirectToAction("Edit", "RFQ", new { id = updateSource.RFQ.Id, bomId = ViewBag.bomId });
            }

            return NotFound();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadRFQSharingTemplate(Guid id)
        {
            var checkSR = _context.RFQSharing.Include(x => x.Sourcing).FirstOrDefault(x => x.Id == id);
            if (checkSR == null) return NotFound();
            if (checkSR.DataResponse == null) return NotFound();

            var rfqSharingRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(checkSR.DataResponse);
            var bomList = rfqSharingRoot.data.bomGrid;

            string templatePath = Path.Combine(_env.WebRootPath, "BOMTemplate", "ImportRFQSharingTemplate.xlsx");

            IWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("RFQ");
            sheet.SetZoom(80, 100); 
            
            // Create header row
            var headerRow = sheet.CreateRow(0);
            string[] headers = { "Bom No", "Description", "Brand", "Part No", "Quantity", "Currency", 
                                "Unit Price", "Discount", "Tax", "Shipping Cost", "Bank Fee", "Lead Time (days)", "Item Line No" };
            
            // Create header style
            ICellStyle headerStyle = workbook.CreateCellStyle();
            headerStyle.FillPattern = FillPattern.SolidForeground;
            headerStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            headerStyle.BorderBottom = BorderStyle.Thin;
            headerStyle.BorderTop = BorderStyle.Thin;
            headerStyle.BorderLeft = BorderStyle.Thin;
            headerStyle.BorderRight = BorderStyle.Thin;
            headerStyle.Alignment = HorizontalAlignment.Center;
            headerStyle.VerticalAlignment = VerticalAlignment.Center;
            
            IFont headerFont = workbook.CreateFont();
            headerFont.IsBold = true;
            headerStyle.SetFont(headerFont);
            
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = headerRow.CreateCell(i);
                cell.SetCellValue(headers[i]);
                cell.CellStyle = headerStyle;
            }
            
            // Set column widths
            sheet.SetColumnWidth(0, 20 * 256);  // Bom No
            sheet.SetColumnWidth(1, 40 * 256);  // Description
            sheet.SetColumnWidth(2, 20 * 256);  // Brand
            sheet.SetColumnWidth(3, 20 * 256);  // Part No
            sheet.SetColumnWidth(4, 12 * 256);  // Quantity
            sheet.SetColumnWidth(5, 12 * 256);  // Currency
            sheet.SetColumnWidth(6, 15 * 256);  // Unit Price
            sheet.SetColumnWidth(7, 12 * 256);  // Discount
            sheet.SetColumnWidth(8, 12 * 256);  // Tax
            sheet.SetColumnWidth(9, 18 * 256);  // Shipping Cost
            sheet.SetColumnWidth(10, 12 * 256); // Bank Fee
            sheet.SetColumnWidth(11, 18 * 256); // Lead Time
            sheet.SetColumnWidth(12, 25 * 256); // Item Line No

                ICellStyle evenRowStyle = workbook.CreateCellStyle();
                evenRowStyle.FillPattern = FillPattern.SolidForeground;
                evenRowStyle.FillForegroundColor = IndexedColors.White.Index;
                evenRowStyle.BorderBottom = BorderStyle.Thin;
                evenRowStyle.BorderTop = BorderStyle.Thin;
                evenRowStyle.BorderLeft = BorderStyle.Thin;
                evenRowStyle.BorderRight = BorderStyle.Thin;
                evenRowStyle.VerticalAlignment = VerticalAlignment.Center;

                ICellStyle oddRowStyle = workbook.CreateCellStyle();
                oddRowStyle.FillPattern = FillPattern.SolidForeground;
                XSSFColor customGreyBlue = new XSSFColor(new byte[] { 220, 230, 241 });
                ((XSSFCellStyle)oddRowStyle).SetFillForegroundColor(customGreyBlue);
                oddRowStyle.BorderBottom = BorderStyle.Thin;
                oddRowStyle.BorderTop = BorderStyle.Thin;
                oddRowStyle.BorderLeft = BorderStyle.Thin;
                oddRowStyle.BorderRight = BorderStyle.Thin;
                oddRowStyle.VerticalAlignment = VerticalAlignment.Center;

                ICellStyle evenDateStyle = workbook.CreateCellStyle();
                evenDateStyle.CloneStyleFrom(evenRowStyle);
                IDataFormat dateFormat = workbook.CreateDataFormat();
                evenDateStyle.DataFormat = dateFormat.GetFormat("mm/dd/yyyy");

                ICellStyle oddDateStyle = workbook.CreateCellStyle();
                oddDateStyle.CloneStyleFrom(oddRowStyle);
                oddDateStyle.DataFormat = dateFormat.GetFormat("mm/dd/yyyy");

                IDataFormat numberFormat = workbook.CreateDataFormat();
                
                ICellStyle numberStyle = workbook.CreateCellStyle();
                numberStyle.DataFormat = numberFormat.GetFormat("#,##0.00");
                numberStyle.BorderBottom = BorderStyle.Thin;
                numberStyle.BorderTop = BorderStyle.Thin;
                numberStyle.BorderLeft = BorderStyle.Thin;
                numberStyle.BorderRight = BorderStyle.Thin;
                numberStyle.VerticalAlignment = VerticalAlignment.Center;

                ICellStyle wholeNumberStyle = workbook.CreateCellStyle();
                wholeNumberStyle.DataFormat = numberFormat.GetFormat("0");
                wholeNumberStyle.BorderBottom = BorderStyle.Thin;
                wholeNumberStyle.BorderTop = BorderStyle.Thin;
                wholeNumberStyle.BorderLeft = BorderStyle.Thin;
                wholeNumberStyle.BorderRight = BorderStyle.Thin;
                wholeNumberStyle.VerticalAlignment = VerticalAlignment.Center;

                var currencyList = new string[] { "MYR", "USD", "SGD", "EURO", "RMB", "YEN" };
                var validationHelper = new XSSFDataValidationHelper((XSSFSheet)sheet);
                var validationConstraint = (XSSFDataValidationConstraint)validationHelper.CreateExplicitListConstraint(currencyList);
                var cellRangeAddressList = new CellRangeAddressList(1, 20, 5, 5); // Rows 1-20 (2-21 in Excel), Col 5 (F - Currency)
                var validation = (XSSFDataValidation)validationHelper.CreateValidation(validationConstraint, cellRangeAddressList);
                validation.ShowErrorBox = true;
                sheet.AddValidationData(validation);

                for (int i = 0; i < bomList.Count; i++)
                {
                    var row = sheet.CreateRow(i + 1);
                    
                    if (i < 20)
                    {
                        row.HeightInPoints = 20;
                    }
                    
                    
                    ICellStyle baseStyle = (i % 2 == 0) ? evenRowStyle : oddRowStyle;

                    var bom = bomList[i];

                    for (int col = 0; col < 13; col++)
                    {
                        var cell = row.CreateCell(col);
                        
                        if (i < 20)
                        {
                            ICellStyle cellStyle = workbook.CreateCellStyle();
                            cellStyle.CloneStyleFrom(baseStyle);

                            if (col == 4 || col == 11) 
                            {
                                ICellStyle numStyle = workbook.CreateCellStyle();
                                numStyle.CloneStyleFrom(cellStyle);
                                numStyle.DataFormat = wholeNumberStyle.DataFormat;
                                cell.CellStyle = numStyle;
                            }
                            else if (col >= 6 && col <= 10) 
                            {
                                ICellStyle numStyle = workbook.CreateCellStyle();
                                numStyle.CloneStyleFrom(cellStyle);
                                numStyle.DataFormat = numberStyle.DataFormat;
                                cell.CellStyle = numStyle;
                            }
                            else
                            {
                                cell.CellStyle = cellStyle;
                            }
                        }
                    }

                    row.GetCell(0).SetCellValue(bom.bomNo ?? "");
                    row.GetCell(1).SetCellValue(bom.description ?? "");
                    row.GetCell(2).SetCellValue(bom.brand ?? "");
                    row.GetCell(3).SetCellValue(bom.partNo ?? "");
                    
                    row.GetCell(4).SetCellValue(bom.totalQuantity);
                    
                    row.GetCell(5).SetCellValue(bom.currency ?? "");
                    row.GetCell(6).SetCellValue(bom.unitPrice ?? 0);
                    row.GetCell(7).SetCellValue(bom.discount ?? 0);
                    row.GetCell(8).SetCellValue(bom.tax ?? 0);
                    row.GetCell(9).SetCellValue(bom.shippingCost ?? 0);
                    row.GetCell(10).SetCellValue(bom.bankFee ?? 0);

                    var leadTimeCell = row.GetCell(11);
                    if (bom.leadTime.HasValue && bom.leadTime.Value > 0)
                    {
                        leadTimeCell.SetCellValue(bom.leadTime.Value);
                    }
                    else
                    {
                        leadTimeCell.SetCellValue("");
                    }

                    row.GetCell(12).SetCellValue(bom.quotationLineNo ?? "");
                }

                var dvHelper = sheet.GetDataValidationHelper();
                var leadTimeAddressList = new CellRangeAddressList(1, bomList.Count, 11, 11);
                var leadTimeConstraint = dvHelper.CreateExplicitListConstraint(
                    new string[] { "1", "3", "5", "7", "10", "14", "21", "30", "45", "60", "90" });
                var leadTimeValidation = dvHelper.CreateValidation(leadTimeConstraint, leadTimeAddressList);
                leadTimeValidation.ShowErrorBox = true;
                sheet.AddValidationData(leadTimeValidation);


                using (var exportData = new MemoryStream())
                {
                    workbook.Write(exportData);
                    string filename = $"RFQ_List_Template_{checkSR.Id}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

                    return File(exportData.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        filename);
                }
            }


        [AllowAnonymous]
        public async Task<JsonResult> ImportRFQSharing()
        {
            var postedFile = Request.Form.Files["FileUpload"];
            var rfqSharingIdString = Request.Form["Id"];

            if (!Guid.TryParse(rfqSharingIdString, out var rfqSharingId))
            {
                return Json(new { result = false, msg = "Invalid RFQ Sharing Id" });
            }

            var rfqSharing = await _context.RFQSharing
                .FirstOrDefaultAsync(x => x.Id == rfqSharingId);

            if (rfqSharing == null)
            {
                return Json(new { result = false, msg = "RFQ Sharing record not found" });
            }

            var rfqSharingRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(rfqSharing.DataResponse);

            try
            {
                if (postedFile != null)
                {
                    var FileDic = "UploadedRFQSharingExcel";
                    string FilePath = Path.Combine(_env.WebRootPath + "/" + FileDic);

                    //Create a Folder.
                    if (!Directory.Exists(FilePath))
                        Directory.CreateDirectory(FilePath);
                    var filename = Guid.NewGuid().ToString() + postedFile.FileName;
                    var filePath = Path.Combine(FilePath, filename);

                    using (FileStream stream = new FileStream(filePath, FileMode.Create))
                    {
                        postedFile.CopyTo(stream);
                    }

                    //HSSFWorkbook hssfwb;
                    XSSFWorkbook workbook;
                    using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        workbook = new XSSFWorkbook(file);
                    }
                    ISheet sheet = workbook.GetSheet("RFQ");
                    var bomList = new List<Bom>();
                    for (int row = 1; row <= sheet.LastRowNum; row++)
                    {
                        if (sheet.GetRow(row).GetCell(0) != null)
                        {
                            var bomNo = sheet.GetRow(row).GetCell(0)?.ToString();
                            if (string.IsNullOrWhiteSpace(bomNo)) continue;

                            var bom = rfqSharingRoot.data.bomGrid.FirstOrDefault(x => x.bomNo == bomNo);
                            if (bom != null)
                            {
                                bom.currency = sheet.GetRow(row).GetCell(5)?.ToString();
                                bom.unitPrice = double.TryParse(sheet.GetRow(row).GetCell(6)?.ToString(), out var unitPrice) ? unitPrice : 0;
                                bom.discount = double.TryParse(sheet.GetRow(row).GetCell(7)?.ToString(), out var discount) ? discount : 0;
                                bom.tax = double.TryParse(sheet.GetRow(row).GetCell(8)?.ToString(), out var tax) ? tax : 0;
                                bom.shippingCost = double.TryParse(sheet.GetRow(row).GetCell(9)?.ToString(), out var shipping) ? shipping : 0;
                                bom.bankFee = double.TryParse(sheet.GetRow(row).GetCell(10)?.ToString(), out var bankFee) ? bankFee : 0;

                                var allowedLeadTimes = new HashSet<int> { 1, 3, 5, 7, 10, 14, 21, 30, 45, 60, 90 };
                                var leadTimeCell = sheet.GetRow(row).GetCell(11);

                                if (leadTimeCell != null)
                                {
                                    string leadTimeStr = leadTimeCell.ToString().Trim();

                                    if (!string.IsNullOrWhiteSpace(leadTimeStr))
                                    {
                                        if (int.TryParse(leadTimeStr, out int leadTimeDays))
                                        {
                                            if (allowedLeadTimes.Contains(leadTimeDays))
                                            {
                                                bom.leadTime = leadTimeDays;

                                                if (bom.requireDate.HasValue)
                                                {
                                                    bom.eta = bom.requireDate.Value.AddDays(leadTimeDays);
                                                }
                                                else
                                                {
                                                    bom.eta = DateTime.Today.AddDays(leadTimeDays);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (DateTime.TryParse(leadTimeStr, out DateTime dateValue))
                                            {
                                                bom.eta = dateValue;

                                                if (bom.requireDate.HasValue)
                                                {
                                                    bom.leadTime = (int)(dateValue - bom.requireDate.Value).TotalDays;
                                                }
                                                else
                                                {
                                                    bom.leadTime = (int)(dateValue - DateTime.Today).TotalDays;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        bom.leadTime = null;
                                        bom.eta = null;
                                    }
                                }

                                bom.quotationLineNo = sheet.GetRow(row).GetCell(12)?.ToString();
                            }

                        }
                    }

                    rfqSharing.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqSharingRoot);
                    _context.Update(rfqSharing);

                    try
                    {

                        await _context.SaveChangesAsync();
                    }
                    catch (Exception e)
                    {
                        throw new Exception(e.Message);
                    }
                    finally
                    {
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }

                    return Json(new { result = true, msg = "Import completed successfully!" });
                }
                return Json(new { result = false, msg = "No file selected." });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = "An error occur. Please try again. " });
            }
        }


        [HttpGet]
        public IActionResult DownloadRFQDrawing(Guid id)
        {
            var rfq = _context.RFQSharing.FirstOrDefault(x => x.Id == id);

            if(rfq != null && !string.IsNullOrEmpty(rfq.DataResponse))
            {
                var root = JsonConvert.DeserializeObject<RFQSharingRoot>(rfq.DataResponse);
                var bomItems = root.data.bomGrid.Select(x => x.itemLineNo).ToList();
                var supportFiles = new List<SupportFile>();
                foreach (var bomId in bomItems)
                {
                    var checkFiles = _context.BomSupportFile.Include(x => x.SupportFile).Where(x => x.BomId == bomId).ToList();
                    if (checkFiles.Count > 0)
                    {
                        var loadSupportFile = checkFiles.Select(x => x.SupportFile).ToList();
                        supportFiles.AddRange(loadSupportFile);
                    }
                }

                var zipName = $"Drawing-{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}.zip";
                var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var sp in supportFiles)
                    {
                        var filePath = Path.Combine(_env.WebRootPath, "documents", sp.FileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            zip.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                        }
                    }
                }

                return File(ms.ToArray(), "application/zip", zipName);

            }

            return Ok();
        }

        public IActionResult ExportToExcel(Guid id)
        {
            var rfq = _context.RFQSharing.FirstOrDefault(x => x.Id == id);
            var supplierQuery = _context.SupplierInfo.AsQueryable();

            if (rfq.DataResponse != null)
            {
                RFQSharingRoot root = JsonConvert.DeserializeObject<RFQSharingRoot>(rfq.DataResponse);
                var currentRow = 1;

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("RFQ");

                    //boms
                    var bomTitle = typeof(BOMGridFixProperties).GetProperties();
                    var bomItems = root.data.bomGrid.ToList();
                    var holdBackValue = 0;
                    for (int i = 1; i <= bomTitle.Count(); i++)
                    {
                        if (bomTitle[i - 1].Name == "totalPrice")
                        {
                            var currency = bomItems.Select(x => x.currency).FirstOrDefault();
                            worksheet.Cell(currentRow, i - holdBackValue).Value = bomTitle[i - 1].Name.FirstCharToUpper().ConvertEnum() + "(" + currency + ")";
                            worksheet.Cell(currentRow, i - holdBackValue).Style.Font.Bold = true;
                            worksheet.Cell(currentRow, i - holdBackValue).Style.Fill.BackgroundColor = XLColor.LightCyan;
                        }
                        else if (bomTitle[i - 1] == typeof(BOMGrid).GetProperty("selectedSupplier")
                            || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("partNo")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("itemLineNo")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("bomNo")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("itemCategory")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("module")
                                )
                        {
                            holdBackValue += 1;
                            continue;

                        }
                        else
                        {
                            worksheet.Cell(currentRow, i - holdBackValue).Value = bomTitle[i - 1].Name.FirstCharToUpper().ConvertEnum();
                            worksheet.Cell(currentRow, i - holdBackValue).Style.Font.Bold = true;
                            worksheet.Cell(currentRow, i - holdBackValue).Style.Fill.BackgroundColor = XLColor.LightCyan;
                        };
                    }

                    foreach (var b in bomItems)
                    {
                        holdBackValue = 0;
                        currentRow += 1;
                        int countIC = 0;

                        for (int i = 1; i <= bomTitle.Count(); i++)
                        {
                            if (bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("partNo")
                                || bomTitle[i - 1] == typeof(BOMGrid).GetProperty("selectedSupplier")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("itemLineNo")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("bomNo")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("itemCategory")
                                || bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("module")
                                )
                            {
                                holdBackValue += 1;
                                continue;
                            }
                            else
                            {
                                worksheet.Cell(currentRow, i - holdBackValue).Value = b.GetType().GetProperty(bomTitle[i - 1].Name).GetValue(b);
                            }
                        }
                    }

                    worksheet.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();

                        return File(
                            content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            "ExportRFQ_" +DateTime.Now.ToString("yyyy_MM_dd_hh:mm")+ ".xlsx");
                    }
                }
            }

            return NoContent();
        }

        [AllowAnonymous]
        [HttpGet]
        public ActionResult Success()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetRfqSharing(Guid id)
        {
            var rfqSharing = _context.RFQSharing.FirstOrDefault(d => d.Id == id);

            if (rfqSharing == null)
            {
                return Json(new { success = false, message = "RFQ Sharing not found." });
            }

            return Json(new { success = true, data = rfqSharing.DataResponse });
        }

        public async Task<ActionResult> AutoapprovedRFQ(string supplierid, string dataResponse, decimal? price)
        {
            try
            {
                var currentRFQ = JsonConvert.DeserializeObject<RFQSharingRoot>(dataResponse);
                var rfqform = _context.Rfqs.FirstOrDefaultAsync(x => x.Id == currentRFQ.data.rfqId);
                RFQRoot? root = JsonConvert.DeserializeObject<RFQRoot>(rfqform.Result.DataResponse);
                int rfqid = Convert.ToInt32(currentRFQ.data.rfqId);
                //var supplierid = "";
                var quatno = "";

                foreach (var bom in root.data.bomGrid)
                {
                    var quot = currentRFQ.data.bomGrid.FirstOrDefault(x => x.bomNo == bom.bomNo);
                    bom.quotNoLink = quot?.quotNoLink ?? null;
                    bom.itemLineNo = quot?.itemLineNo ?? null;
                    bom.currency = quot?.currency ?? null;
                    bom.unitPrice = quot?.unitPrice ?? null;
                    bom.discount = quot?.discount ?? null;
                    bom.tax = quot?.tax ?? null;
                    bom.shippingCost = quot?.shippingCost ?? null;
                    bom.bankFee = quot?.bankFee ?? null;
                    bom.totalPrice = quot?.totalPrice ?? null;
                    bom.requireDate = quot?.requireDate ?? null;
                    bom.leadTime = quot?.leadTime ?? null;
                    bom.eta = quot?.eta ?? null;
                    bom.quotationLineNo = quot?.quotationLineNo ?? null;
                    bom.quotNoLink = quot?.quotNoLink ?? null;
                    //supplierid = (quot.selectedSupplier=="") ? root.data.supplierList?.FirstOrDefault()?.supplierKey?.id : "";
                    //supplierid = supplierid;
                }




                //update selected supplier and attachment
                var supplierlist = root.data.supplierList?.ToList();
                if (supplierlist?.Count > 0)
                {
                    var quatation = currentRFQ?.data.quotations?.FirstOrDefault();
                    foreach (var supplier in supplierlist)
                    {
                        if (!string.IsNullOrEmpty(supplier.supplierKey?.id))
                        {
                            if (supplier.supplierKey.id == supplierid)
                            {
                                //supplier.recommended = true;
                                //supplier.remark = currentRFQ?.data.remark;
                                //supplier.quotNoLink = quatation?.quotNoLink;
                                ////supplier.quotation = quatation?.quotation;
                                //quatno = quatation?.quotNoLink;

                                var CheckattachmentSupplier = await GetAttachmentSupplier(rfqid);
                                if (CheckattachmentSupplier.Count > 0)
                                {
                                    await deleteAttachFile(supplierid, rfqid, CheckattachmentSupplier?.FirstOrDefault()?.Id);
                                }
                            }

                        }
                        quatno = quatation?.quotNoLink;

                        var attachment = quatation?.quotation?.FirstOrDefault();

                        string url = string.IsNullOrEmpty(attachment?.url) ? "" : attachment.url;
                        string attcmentname = string.IsNullOrEmpty(attachment?.name) ? "" : attachment.name;
                        
                        if (!string.IsNullOrEmpty(url))
                        {
                            await UploadBase64(supplierid, rfqid, quatno, 0, url, attcmentname);
                        }

                        var suppliername = _context.SupplierInfo.FirstOrDefault(x => x.Id == Guid.Parse(supplierid));
                        var supplierkey = new SupplierObject
                        {
                            id = supplierid,
                            name = suppliername.VendorName

                        };
                        var attachmentSupplier = await GetAttachmentSupplier(rfqid);

                        if (attachmentSupplier.FirstOrDefault(x => x.SupplierInfoId == Guid.Parse(supplierid)) != null)
                        {
                            var attach = attachmentSupplier.FirstOrDefault(x => x.SupplierInfoId == Guid.Parse(supplierid));                      

                            attach.IsRecommended = true;
                            attach.V_Remark = currentRFQ?.data.remark;
                            //attach.Remark = currentRFQ?.data.remark;
                            attach.R_Remark = root.data.supplierList.FirstOrDefault()?.r_remark;
                           // _context.SupplierAttachment.Update(attach);

                            var newssupplier = new List<SupplierList>();
                            newssupplier.Add(new SupplierList
                            {
                                supplierKey = new SupplierObject
                                {
                                    id = supplierid,
                                    name = suppliername.VendorName

                                },
                                vendor_remark = currentRFQ?.data.remark,
                                // remark = currentRFQ?.data.remark,
                                r_remark = attach.R_Remark,
                                quotNoLink = attach.QuotNo,
                                quotation = new List<Files>
                                        {
                                            new Files
                                            {
                                                name = attach.SupportFile.FileName,
                                                originalName = attach.SupportFile.FileName,
                                                type = attach.SupportFile.ContentType,
                                                url = generateViewURL(attach.SupportFile),
                                                storage = "url"
                                            }
                                        },
                                recommended = attach.IsRecommended,
                                supportFileId = attach.Id,
                            });
                            root.data.supplierList = newssupplier;
                            rfqform.Result.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                        }
                    }
                }
                else
                {
                    var quatation = currentRFQ?.data.quotations?.FirstOrDefault();
                    var suppliername = _context.SupplierInfo.FirstOrDefault(x => x.Id == Guid.Parse(supplierid));
                    var supplierkey = new SupplierObject
                    {
                        id = supplierid,
                        name = suppliername.VendorName

                    };


                    var attachment = quatation?.quotation?.FirstOrDefault();
                    string url = string.IsNullOrEmpty(attachment?.url) ? "" : attachment.url;
                    string attcmentname = string.IsNullOrEmpty(attachment?.name) ? "" : attachment.name;
                    //await UploadBase64(supplierid, rfqid, quatno, 0, url, attcmentname);

                    var attachmentSupplier = await GetAttachmentSupplier(rfqid);

                    if (attachmentSupplier.FirstOrDefault(x => x.SupplierInfoId == Guid.Parse(supplierid)) != null)
                    {
                        var attach = attachmentSupplier.FirstOrDefault(x => x.SupplierInfoId == Guid.Parse(supplierid));
                        var newsupplierlist = new SupplierList
                        {
                            quotNoLink = quatation?.quotNoLink,
                            recommended = true,
                            vendor_remark = currentRFQ?.data.remark,
                            // remark = currentRFQ?.data.remark,
                            supplierKey = supplierkey,
                            quotation = new List<Files>
                               {
                                   new Files
                                   {
                                      name = attach.SupportFile.FileName,
                                       originalName = attach.SupportFile.FileName,
                                       type = attach.SupportFile.ContentType,
                                       url = generateViewURL(attach.SupportFile),
                                       storage = "url"
                                   }
                               },
                        };
                        root.data?.supplierList?.Add(newsupplierlist);
                    }
                }

                rfqform.Result.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                _context.Rfqs.Update(rfqform.Result);
                await _context.SaveChangesAsync();

                return Ok();

            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error uploading file: {ex.Message}");
            }

        }

        public async Task<bool> CheckAutoApprove(Guid Id, decimal? price, string form)
        {
            var currentRFQ = JsonConvert.DeserializeObject<RFQSharingRoot>(form);
            int rfqid = Convert.ToInt32(currentRFQ.data.rfqId);

            var hasCopiedItems = await _context.Boms
                .Where(x => x.RFQId == rfqid && x.IsCopied == true)
                .AnyAsync();

            if (hasCopiedItems)
            {
                Console.WriteLine($"Skipping auto-approval for RFQ {rfqid} - contains copied BOM items");
                return false;
            }

            var sourcingList = await (
                from s in _context.Sourcing
                join rq in _context.RFQSharing on s.Id equals rq.SourceId
                join v in _context.SupplierInfo on rq.SupplierId equals v.Id
                join r in _context.Rfqs on s.RFQId equals r.Id
                where s.RFQId == rfqid
                group new { s, rq, v, r } by new
                {
                    s.Id,
                    s.RFQId,
                    r.Rfqname,
                    s.TotalPriceSave,
                    s.Brand,
                    s.FinalPrice,
                    s.FinalLeadTime,
                    v.VendorName
                } into g
                select g.Key
                ).ToListAsync();

            var additionalSupplierNames = await _context.AdditionalSuppliers
                  .Select(x => x.VendorName)
                  .ToListAsync();

            bool hasAdditionalSupplier = sourcingList.Any(x =>
            additionalSupplierNames.Any(n => n.Trim().ToLower() == x.VendorName.Trim().ToLower())
            );

            if (sourcingList == null)
                return false;

            if (
                (sourcingList.Count == 1 && !hasAdditionalSupplier && price <= 5000 && price > 0) ||
                (sourcingList.Count == 1 && hasAdditionalSupplier && price <= 1000 && price > 0))
            {
                return true;
            }
            else
            {
                return false;
            }
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
                   await _context.SaveChangesAsync();
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
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public async Task<IActionResult> UploadBase64([FromForm] string supplierId, [FromForm] int rfqId, [FromForm] string quotNo, [FromForm] int supportFileId, string base64String, [FromForm] string fileName, [FromForm] string contentType = null)

        {
            try
            {
                // Convert base64 to IFormFile
                var formFile = ConvertBase64ToFormFile(base64String, fileName, contentType);

                if (formFile == null)
                {
                    return BadRequest("Invalid file data");
                }
                await CopySupplierWithAttach(supplierId, rfqId, supportFileId, quotNo, formFile);

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

        public async Task<List<SupplierAttachment>> GetAttachmentSupplier(int rfqId)
        {
            var supplierAttachments = await _context.SupplierAttachment
                .Include(x => x.SupportFile)
                .Include(x => x.SupplierInfo)
                .Where(x => x.RFQId == rfqId)
                .ToListAsync();

            return supplierAttachments;
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
                    //throw new Exception("File No Found!");
                }

                var url = Url.Action("downloadFile", "BOM", new { id = supportFile.Id });

                return url;
            }
            else
            {
                return Url.Action("ShowPDF", "BOM", new { supportFileId = supportFile.Id }, HttpContext.Request.Scheme);
            }
        }

        public async Task<IActionResult> deleteAttachFile(string supplierId, int rfqId, int? supportFileId)
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

    }
}
