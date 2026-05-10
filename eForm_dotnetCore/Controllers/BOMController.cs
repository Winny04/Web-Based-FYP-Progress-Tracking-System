using ClosedXML.Excel;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using eForm_dotnetCore.Common;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Domain;
using eForm_dotnetCore.Extensions;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModel;
using eForm_dotnetCore.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MoreLinq;
using MoreLinq.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq;
using NPOI.HPSF;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using OfficeOpenXml;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Ocsp;
using PuppeteerSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static eForm_dotnetCore.ViewModels.BOMExcelModel;

namespace eForm_dotnetCore.Controllers
{
    public class BOMController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IEmailService _emailService;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public BOMController(EformSophicDemoContext context
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
            _env = _environment;
        }


        [HttpPost]
        public async Task<JsonResult> LoadData(BomDataTableParams parameters)
        {

            var query = _context.Boms
                .Where(x => x.ProjectId == parameters.ProjectId)
               .AsQueryable();

            if (parameters.Length < 0)
                parameters.Length = query.Count();

            var rfqQuery = _context.Rfqs.AsQueryable();
            var prQuery = _context.Prs.AsQueryable();

            var statusValue = parameters.Status;
            var catValue = parameters.Category;
            var searchValue = parameters.Search.value;

            if (!string.IsNullOrEmpty(statusValue))
            {
                query = query.Where(x => x.Status == statusValue).AsQueryable();
            }
            if (!string.IsNullOrEmpty(catValue))
            {
                query = query.Where(x => x.ItemCategory == catValue).AsQueryable();
            }
            if (!string.IsNullOrEmpty(searchValue))
            {
                if (searchValue.StartsWith("RFQ"))
                {
                    var findRFQ = rfqQuery.Where(x => x.Rfqname == searchValue).FirstOrDefault();
                    if (findRFQ != null)
                    {
                        query = query.Where(x => x.RFQId == findRFQ.Id).AsQueryable();
                    }
                }
                else if (searchValue.StartsWith("PR"))
                {
                    var findPR = prQuery.Where(x => x.Prname == searchValue).FirstOrDefault();
                    if (findPR != null)
                    {
                        query = query.Where(x => x.PRId == findPR.Id).AsQueryable();
                    }
                }
                else if (searchValue.StartsWith("SAPO"))
                {
                    query = query.Where(x => x.BomSupportFiles.Any(bsf => bsf.SupportFile.FileName.Contains(searchValue))).AsQueryable();
                }
                else
                {
                    query = query
                         .Where(x => x.PartDesc.Contains(searchValue) ||
                                 x.Brand.Contains(searchValue) ||
                                 x.Module.Contains(searchValue) ||
                                 x.BomNo.Contains(searchValue) ||
                                 x.ItemCategory.Contains(searchValue) ||
                                 x.PartNumber.Contains(searchValue)
                                 )
                         .AsQueryable();
                }
            }

            var total = query.Count();

            var bomItems = await query
                        .Include(x => x.RFQ)
                        .Include(x => x.PR)
                        .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                        .OrderByDescending(x => x.LastUpdatedDate)
                        .Skip(parameters.Start).Take(parameters.Length)
                        .ToListAsync();

            var BomList = new List<BOMViewModel>();

            var prIds = new List<int>();
            foreach (var item in bomItems)
            {
                var quantity = 0;
                var quotationNoLink = string.Empty;
                var quotationLineNo = string.Empty;
                var supplierName = string.Empty;
                var quotName = string.Empty;
                var quotUrl = string.Empty;
                DateTime? eta = null;

                if (item.RFQ != null && CommonHelpers.availableBaseStatus("RFQ Approved", item.Status))
                {
                    var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(item.RFQ.DataResponse);
                    var currentItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).FirstOrDefault();

                    if (currentItem != null)
                    {
                        quotationLineNo = currentItem.quotationLineNo;
                        quotationNoLink = currentItem.quotNoLink;
                        eta = currentItem.eta;
                        quantity = currentItem.totalQuantity;

                        var recommendSupplier = rfqRoot.data.supplierList.Where(x => x.recommended.HasValue && x.recommended == true
                        && x.supplierKey != null && x.supplierKey.id != null &&
                        x.supplierKey.id == currentItem.selectedSupplier && x.quotNoLink == quotationNoLink)
                            .FirstOrDefault();

                        supplierName = recommendSupplier != null ? recommendSupplier.supplierKey.name : string.Empty;
                        quotName = recommendSupplier != null && recommendSupplier.quotation != null && recommendSupplier.quotation.Count > 0
                            ? recommendSupplier.quotation[0].originalName : string.Empty;
                        quotUrl = recommendSupplier != null && recommendSupplier.quotation != null && recommendSupplier.quotation.Count > 0
                            ? recommendSupplier.quotation[0].url : string.Empty;
                    }

                }

                var model = new BOMViewModel();
                model.Id = item.Id;
                model.BomNo = item.BomNo;
                model.Rfq = item.RFQ;
                model.Pr = item.PR;
                model.Module = item.Module;
                model.PartNumber = item.PartNumber;
                model.Brand = item.Brand;
                model.PartDesc = item.PartDesc;
                model.ItemCategory = item.ItemCategory;
                model.BomSupportFiles = item.BomSupportFiles;
                model.UnitQty = item.UnitQty;
                model.RequireQty = item.RequireQty;
                model.CreatedDate = item.CreatedDate;
                model.LastUpdatedDate = item.LastUpdatedDate;
                model.Status = item.Status;
                model.SupplierName = CommonHelpers.availableBaseStatus("RFQ Approved", item.Status) ? supplierName : "";
                model.QuotName = !string.IsNullOrEmpty(model.SupplierName) ? quotName : "";
                model.QuotUrl = quotUrl;
                model.RfqId = item.RFQ != null ? item.RFQ.Id : null;
                model.RfqName = item.RFQ != null ? item.RFQ.Rfqname : null;
                model.PrId = item.PR != null ? item.PR.Id : null;
                model.PRApprovers = _context.PRApprover.Where(d => d.PRId == model.PrId).ToList() ?? null;
                model.totalApprover = _context.Prs.Where(d => d.Id == model.PrId).Select(d => d.totalApprover).FirstOrDefault() ?? 0;
                model.PRName = item.PR != null ? CommonHelpers.renamePR(item.PR.Prname, item.PR.RevisionNo) : null;
                model.prQuantity = CommonHelpers.availableBaseStatus("PO Approved", item.Status) ? quantity : -1;
                //model.prUnitPrice = CommonHelpers.availableBaseStatus("PO Approved", item.Status) ? Math.Round(item.UnitPrice ?? 0, 4) : -1;
                //model.prTotalPrice = CommonHelpers.availableBaseStatus("PO Approved", item.Status) ? (Math.Round(item.UnitPrice ?? 0, 4)) * quantity : -1;

                if (item.PR != null)
                {
                    try
                    {
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(item.PR.DataResponse);
                        var currentItem = prRoot?.data?.bomGrid?.Where(x => x.itemLineNo == item.Id).FirstOrDefault();

                        if (currentItem != null)
                        {
                            var itemCurrency = currentItem.currency ?? "RM";

                            model.prUnitPrice = -1;
                            model.prTotalPrice = -1;

                            if (CommonHelpers.availableBaseStatus("PO Approved", item.Status))
                            {
                                
                                double unitPrice = Math.Round(currentItem.unitPrice ?? 0, 4);
                                
                                
                                if (unitPrice == 0 && item.UnitPrice.HasValue)
                                {
                                    unitPrice = Math.Round(item.UnitPrice.Value, 4);
                                }
                                
                                int qty = model.prQuantity > 0 ? model.prQuantity : (item.RequireQty ?? 0);

                                // base prices (before conversion)
                                double totalPrice = unitPrice * qty;

                                // get conversion rate if currency is not RM
                                double rate = 1;
                                if (!itemCurrency.Equals("RM", StringComparison.OrdinalIgnoreCase) &&
                                    !itemCurrency.Equals("MYR", StringComparison.OrdinalIgnoreCase))
                                {
                                    var conversion = prRoot.data.currencyGrid?.FirstOrDefault(c =>
                                        c.cr_currency != null && c.cr_currency.Equals(itemCurrency, StringComparison.OrdinalIgnoreCase));

                                    if (conversion != null && conversion.rate.HasValue && conversion.rate > 0)
                                        rate = conversion.rate.Value;
                                }

                                model.prUnitPrice = unitPrice * rate;
                                model.prTotalPrice = totalPrice * rate;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: BOM item {item.Id} not found in PR {item.PR.Id} bomGrid. PR DataResponse might be missing this item.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing PR data for BOM {item.Id}: {ex.Message}");
                    }

                }

                // model.ETA = eta;
                model.ETA = item.ETA;
                model.ReceivedDate = item.ReceivedDate;
                model.ReceivedQuantity = item.ReceivedQuantity;
                model.QuotationLine = quotationLineNo;
                model.QuotationNoLink = quotationNoLink;
                model.IssuedPIC = getPicsWithDept(item.IssuedPIC);
                model.IssuedQuantity = item.IssuedQuantity;
                model.IssuedOutDate = item.IssuedOutDate;
                model.BalanceToIssueOutQty = item.BalanceToIssueOutQty;
                model.BalanceToReceiveQty = item.BalanceToReceiveQty;

                BomList.Add(model);
            }

            var data = BomList.OrderByDescending(x => x.LastUpdatedDate).ThenBy(x => x.RfqName).ToList();

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = data
            });
        }

        [Authorize]
        public async Task<IActionResult> Index(int id)
        {
            var fileDic = "BOMTemplate";
            var templateDestination = $@"/{fileDic}/Import BOM Template.xlsx";
            ViewBag.ProjectId = id;
            var project = _context.Projects.Include(x => x.CategoryInfo).Where(d => d.Id == id).FirstOrDefault();


            ViewBag.ProjectCategory = project.CategoryInfo.CategoryShortName;
            ViewBag.BOMTemplateUrl = templateDestination;
            ViewBag.PICs = string.IsNullOrEmpty(project.PICS) ? "" : getPicsWithDept(project.PICS);
            ViewBag.PICIds = string.IsNullOrEmpty(project.PICS) ? "" : project.PICS;

            var PICLeaderInfo = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => x.Id == project.PICLeader).FirstOrDefault();
            ViewBag.PICLeaderId = PICLeaderInfo != null ? PICLeaderInfo.Id.ToString() : "";
            ViewBag.PICLeader = PICLeaderInfo != null ? PICLeaderInfo.ShortName + "(" + PICLeaderInfo.EmpDepartment.Name + ")" : "";

            var bomQuery = _context.Boms.AsQueryable();

            var prIds = await bomQuery
                            .Where(x => x.ProjectId == id && x.PRId.HasValue)
                            .WhereRFQAvailableStatus2("RFQ Approved")
                            .Select(x => x.PRId)
                            .Distinct()
                            .ToListAsync();

            var ProjectQuery = await bomQuery
                            .Where(x => x.ProjectId == id)
                            .ToListAsync();


            var PRApprovedQuery = await bomQuery
                            .Where(x => x.ProjectId == id && x.PRId.HasValue)
                            .ToListAsync();

            int num = 0;
            var PrIdArr = new List<int?>();
            foreach (var pr in PRApprovedQuery)
            {
                var prid = pr.PRId;


                if (!PrIdArr.Contains(prid))
                {
                    PrIdArr.Add(prid);
                    var po = _context.Pos.Where(x => x.Prid == prid).Count();
                    num += 1;
                }


            }

            var prApprovedRate = Math.Round((decimal)0);
            var poApprovedRate = Math.Round((decimal)0);

            if (ProjectQuery.Count > 0)
            {
                prApprovedRate = Math.Round((decimal)PRApprovedQuery.Count() / ProjectQuery.Count() * 100, 2);
                poApprovedRate = Math.Round((decimal)num / ProjectQuery.Count() * 100, 2);
            }

            var prs = await _context.Prs
                .Where(x => prIds.Contains(x.Id) && !string.IsNullOrEmpty(x.DataResponse))
                .ToListAsync();

            double Bom_totalPrice = 0;

            foreach (var pr in prs)
            {
                var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);

                var prNetPrice = prRoot.data.netPrice ?? 0;

                Bom_totalPrice += prNetPrice;
            }

            var BomList = new List<BOMViewModel>();
            var itemCatNames = EnumHelpers.ToSelectList<BomItemCategory>().ToList();
            var itemShortNames = EnumHelpers.ToSelectList<BomItemCategoryShortName>();

            ViewBag.ActualMaterialCost = Math.Round(Bom_totalPrice, 2);
            project.ActualMaterialCost = Bom_totalPrice;
            _context.Projects.Update(project);
            _context.SaveChanges();

            var bomOverView = new BOMOverviewViewModel();
            bomOverView.PRApprovedRate = prApprovedRate;
            bomOverView.POApprovedRate = poApprovedRate;
            bomOverView.Employees = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).ToList();
            bomOverView.Project = project;
            bomOverView.ProjectId = project.Id;

            return View(bomOverView);
        }

        public async Task<IActionResult> Inventory()
        {
            var lists = await _context.BomInventory.ToListAsync();

            return View(lists);
        }

        [HttpGet]
        public JsonResult BOMInventoryApi()
        {
            var lists = _context.BomInventory.ToList();

            return Json(lists);
        }

        public IActionResult DeleteBomInv(Guid id)
        {
            var checkItem = _context.BomInventory.FirstOrDefault(x => x.Id == id);

            if (checkItem != null)
            {
                _context.BomInventory.Remove(checkItem);
                _context.SaveChanges();
            }

            return Ok();
        }

        [HttpPost]
        public IActionResult AddOrUpdateBomInv(BomInventory inv)
        {
            var checkExist = _context.BomInventory.FirstOrDefault(x => x.Id == inv.Id);

            inv.Currency = inv.Currency.ToUpper();

            if (checkExist != null)
            {
                checkExist.Description = inv.Description;
                checkExist.Supplier = inv.Supplier;
                checkExist.Currency = inv.Currency;
                checkExist.Module = inv.Module;
                checkExist.Brand = inv.Brand;
                checkExist.UnitPrice = inv.UnitPrice;

                _context.BomInventory.Update(checkExist);
            }
            else
            {
                _context.BomInventory.Add(inv);
            }

            _context.SaveChanges();

            return RedirectToAction(nameof(Inventory));
        }


        public IActionResult EditInvItem(Guid id)
        {
            var checkExist = _context.BomInventory.FirstOrDefault(x => x.Id == id);

            if (checkExist == null) return NotFound();

            return View(checkExist);
        }

        [HttpPost]
        public async Task<ActionResult> ImportBOMInventory(IFormFile file)
        {
            if (file == null) return BadRequest();

            var filePath = string.Empty;
            var bomList = new List<BomInventory>();

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
                    ISheet sheet = workbook.GetSheet("Sheet1");
                    for (int row = 1; row <= sheet.LastRowNum; row++)
                    {
                        if (row == sheet.LastRowNum)
                        {
                            var getname = sheet.GetRow(row).GetCell(1).ToString();
                        }

                        if (sheet.GetRow(row).GetCell(1) == null && string.IsNullOrEmpty(sheet.GetRow(row).GetCell(1).ToString())) continue;

                        var newBom = new BomInventory
                        {
                            Description = sheet.GetRow(row).GetCell(0).ToString().Trim(),
                            Supplier = sheet.GetRow(row).GetCell(1).ToString().Trim(),
                            Currency = sheet.GetRow(row).GetCell(2).ToString().Trim().ToUpper(),
                            Brand = sheet.GetRow(row).GetCell(3).ToString().Trim(),
                            Module = sheet.GetRow(row).GetCell(4).ToString().Trim(),
                            UnitPrice = (decimal)sheet.GetRow(row).GetCell(5).NumericCellValue
                        };

                        bomList.Add(newBom);
                    }
                }

                _context.BomInventory.AddRange(bomList);
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

            return RedirectToAction(nameof(Inventory));
        }

        private string getPicsWithDept(string pICS)
        {
            if (!String.IsNullOrEmpty(pICS))
            {
                var pics = string.Empty;
                var shortname = string.Empty;
                foreach (var pic in pICS.Split(',').Select(int.Parse))
                {
                    var emp = _context.EmployeeInfos
                        .Include(x => x.EmpDepartment)
                        .Where(x => x.Id == pic).FirstOrDefault();
                    if (emp != null)
                        pics += "," + emp.ShortName + "(" + emp.EmpDepartment.Name + ")";
                }
                return pics.TrimStart(',');
            }
            return "";
        }

        [HttpPost]
        public async Task<IActionResult> deleteForm(int? id)
        {
            try
            {
                if (!id.HasValue && !_context.Boms.Any(x => x.Id == id))
                    throw new ArgumentNullException(nameof(id));

                var targetBOM = _context.Boms.Include(x => x.RFQ).Include(x => x.PR).Where(x => x.Id == id).FirstOrDefault();
                var project = _context.Projects.FirstOrDefault(x => x.Id == targetBOM.ProjectId);
                var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
                if (!authorizationResult.Succeeded || isProjectCompleted(project.Id) ||
                    CommonHelpers.availableBaseStatus("PR Approved", targetBOM.Status))
                    return Forbid();

                var targetBOMSupportFile = _context.BomSupportFile.Where(x => x.BomId == id).ToList();
                var supFileIds = targetBOMSupportFile.Select(x => x.SupportFileId).ToList();
                var targetSupportFile = _context.SupportFile.Where(x => supFileIds.Contains(x.Id)).ToList();

                int targetRfqId = 0;
                int targetPrId = 0;
                //Remove item from PR
                if (targetBOM.PRId.HasValue)
                {
                    var targetPR = await _context.Prs.Where(x => x.Id == targetBOM.PRId).FirstOrDefaultAsync();
                    targetPrId = targetPR.Id;
                    var prRoot = JsonConvert.DeserializeObject<PRRoot>(targetPR.DataResponse);
                    var targetDeletePrItem = prRoot.data.bomGrid.Where(x => x.itemLineNo == id).FirstOrDefault();
                    var newPrDataResponse = prRoot.data.bomGrid.Where(x => x.itemLineNo != id).ToList();
                    prRoot.data.bomGrid = newPrDataResponse;
                    targetPR.DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot);
                    _context.Prs.Update(targetPR);
                    _context.SaveChanges();

                    var checkPrEmpty = _context.Boms.Where(x => x.PRId == targetPrId && x.Id != id).Any();
                    if (!checkPrEmpty)
                    {
                        var pr = _context.Prs.Where(x => x.Id == targetPrId).FirstOrDefault();
                        _context.Prs.Remove(pr);
                        _context.SaveChanges();
                    }
                }


                //Remove item inside RFQ
                if (targetBOM.RFQId.HasValue)
                {
                    var targetRFQ = await _context.Rfqs.Where(x => x.Id == targetBOM.RFQId).FirstOrDefaultAsync();
                    targetRfqId = targetRFQ.Id;
                    var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(targetRFQ.DataResponse);
                    var targetDeleteItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo == id).FirstOrDefault();
                    var newRfqDataResponse = rfqRoot.data.bomGrid.Where(x => x.itemLineNo != id).ToList();
                    rfqRoot.data.bomGrid = newRfqDataResponse;
                    targetRFQ.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
                    _context.Rfqs.Update(targetRFQ);
                    _context.SaveChanges();


                    var checkIfAllBOMHaveRemove = _context.Boms.Where(x => x.RFQId == targetRfqId && x.Id != id).Any();
                    if (!checkIfAllBOMHaveRemove)
                    {
                        var rfq = _context.Rfqs.Where(x => x.Id == targetRfqId).FirstOrDefault();

                        if (targetBOM.RFQ != null) //remove supplier file
                        {
                            var targetSupplierAttch = await _context.SupplierAttachment.Include(x => x.SupportFile).Where(x => x.RFQId == targetBOM.RFQId).ToListAsync();
                            var targetSupplierSupportFile = targetSupplierAttch.Select(x => x.SupportFile).ToList();

                            _context.Rfqs.Remove(targetRFQ);

                            foreach (var supplierfile in targetSupplierSupportFile)
                            {
                                _fileWriter.DeleteDocument(supplierfile.FileName);
                            }

                            _context.SupplierAttachment.RemoveRange(targetSupplierAttch);
                        }

                        _context.Rfqs.Remove(rfq);
                        _context.SaveChanges();
                    }
                }

                foreach (var file in targetSupportFile)
                {
                    _fileWriter.DeleteDocument(file.FileName);
                }

                _context.SupportFile.RemoveRange(targetSupportFile);
                _context.Boms.Remove(targetBOM);

                if (_context.SaveChanges() > 0)
                {
                    await _auditTrailService.InsertLogAsync(
                            Common.LogLevel.Information, $@"BOM with id {id.Value} has been remove by {User.Identity.Name}",
                            tableChange: nameof(Bom),
                            empId: int.Parse(HttpContext.GetUserId()),
                            ProjectId: targetBOM.Project.Id
                            );
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                return NoContent();
            }
        }

        [HttpGet]
        public string checkCategoryValid(string select, FormType type)
        {
            if (!string.IsNullOrEmpty(select))
            {
                string[] ids = select.Split(",");

                //if(FormType.RFQ == type && ids.Length > 10)
                //{
                //    return JsonConvert.SerializeObject(new { result = false, msg = "Because of performance issue, one RFQ only can contain 10 items." });
                //}

                if (ids.Length > 1)
                {
                    var mergeProperty = new MergeProperties();
                    var isFirst = true;
                    var canMerge = false;
                    var status = string.Empty;
                    var bomIds = ids.Select(int.Parse).Distinct().ToList();
                    var rfqIds = new List<int>();
                    foreach (var id in bomIds)
                    {
                        var check = _context.Boms
                            .Include(x => x.RFQ)
                            .Where(x => x.Id == id).FirstOrDefault();

                        if (check != null)
                        {
                            if (check.Status == "New" && check.RFQId != null)
                            {
                                return JsonConvert.SerializeObject(new { result = false, msg = "RFQ merge only Item without RFQ can merge." });
                            }
                            else if ((check.Status == "RFQ Approved" && check.RFQId == null) && check.PRId != null)
                            {
                                return JsonConvert.SerializeObject(new { result = false, msg = "Detect specific BOM dont have related RFQ." });
                            }
                            else if (check.Status == "RFQ Approved" && check.RFQId != null && check.PRId != null)
                            {
                                return JsonConvert.SerializeObject(new { result = false, msg = "PR merge only Item without PR can merge." });
                            }

                            var mp = new MergeProperties
                            {
                                ItemCategory = check.ItemCategory,
                                Status = check.Status,
                            };

                            if (check.RFQId.HasValue)
                            {
                                //if (rfqIds.Any(x => x == check.RFQId)) return false;

                                //rfqIds.Add(check.RFQId.Value);
                                var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == check.RFQId);
                                var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                                mp.Supplier = rfqRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == id)?.selectedSupplier;
                            }

                            if (check.Status == "RFQ Approved" && string.IsNullOrEmpty(mp.Supplier))
                            {
                                return JsonConvert.SerializeObject(new { result = false, msg = "Please check again on RFQ as particular item wasn't select correct supplier or quotation" });
                            }

                            if (isFirst)
                            {
                                mergeProperty = mp;
                                isFirst = false;
                            }
                            else
                            {
                                if (check.Status == "New")
                                {
                                    status = check.Status;
                                    bool sameStatus = mergeProperty.Status == status;

                                    if (sameStatus == true)
                                    {
                                        canMerge = true;
                                    }
                                    else
                                    {
                                        canMerge = false;
                                    }
                                    if (!canMerge) return JsonConvert.SerializeObject(new { result = false, msg = "Only status 'New' items can merge." });
                                }
                                else if (check.Status == "RFQ Approved")
                                {
                                    status = check.Status;
                                    bool sameSupplier = mergeProperty.Supplier == mp.Supplier;
                                    // bool sameProperty = (JsonConvert.SerializeObject(mergeProperty) == JsonConvert.SerializeObject(mp));
                                    if (sameSupplier == true)
                                    {
                                        canMerge = true;
                                    }
                                    else
                                    {
                                        canMerge = false;
                                    }
                                    if (!canMerge) return JsonConvert.SerializeObject(new { result = false, msg = "PR merge only allowed for items with the same supplier." });
                                    //  if (!canMerge) return JsonConvert.SerializeObject(new { result = false, msg = "Only status 'RFQ Approved' items with same category/supplier can merge." });
                                }
                            }
                        }
                    }
                    return JsonConvert.SerializeObject(new { result = canMerge, msg = "Only item with status 'New' and 'RFQ Approved' can merge.", status });
                }
            }

            return JsonConvert.SerializeObject(new { result = false, msg = "Something go wrong. Please contact system admin for assist." });
        }

        [HttpPost]
        public async Task<JsonResult> mergeBOM(string select, int projectId, FormType type)
        {
            var validation = checkCategoryValid(select, type);

            if (!(bool)JObject.Parse(validation)["result"])
            {
                return Json(new { result = (bool)JObject.Parse(validation)["result"], msg = JObject.Parse(validation)["msg"].ToString() });
            }

            try
            {
                if (!string.IsNullOrEmpty(select))
                {
                    var ids = select.Split(",").Select(int.Parse).ToList();
                    ids = ids.Where(x => x != null && x != 0).ToList();

                    // Check for Fabrication Category Limit
                    var fabricationItems = _context.Boms.Where(x => ids.Contains(x.Id) && x.ItemCategory == "Fabrication").ToList();
                    //if (fabricationItems.Count > 15)
                    //{
                    //    return Json(new { result = false, msg = "Only up to 15 Fabrication items can be merged at once." });
                    //}

                    if (JObject.Parse(validation)["status"].ToString() == "New" && type == FormType.RFQ)
                    {
                        var updateList = new List<Rfq>();
                        var ddUrl = new List<object>();
                        var root = new RFQRoot();

                        foreach (var id in ids)
                        {
                            var bom = _context.Boms
                                       .Include(x => x.BomSupportFiles)
                                       .ThenInclude(x => x.SupportFile)
                                       .Where(x => x.Id == id).FirstOrDefault();

                            var selectRFQ = JsonConvert.DeserializeObject<RFQRoot>(await initRFQ(projectId, id));
                            root.data.bomGrid.AddRange(selectRFQ.data.bomGrid);
                            if (root.data.header.Count() == 0)
                                root.data.header.AddRange(selectRFQ.data.header);


                            if (bom.BomSupportFiles != null && bom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                            {
                                ddUrl.Add(new
                                {
                                    lineNo = bom.Id,
                                    downloadLink = Url.Action("ShowPDF", "BOM", new { supportFileId = bom.BomSupportFiles.FirstOrDefault(x => x.Type == "IC").SupportFileId }, HttpContext.Request.Scheme),
                                    fileName = bom.BomSupportFiles.FirstOrDefault(x => x.Type == "IC").SupportFile.FileName
                                });
                            }
                        }


                        root.data.createDate = DateTime.Now;
                        root.data.ddURL = JsonConvert.SerializeObject(ddUrl);

                        //add rfq
                        var formName = "RFQ4";
                        var rfqData = new Rfq
                        {
                            AddedBy = "sys",
                            Rfqname = _context.Rfqs.Any() ? formName + _context.Rfqs.Select(x => x.Id).Max() + 2 : formName,
                            ProjectId = (int)projectId,
                            CreatedDate = DateTime.Now,
                            LastUpdatedDate = DateTime.Now,
                            Status = "New",
                            FormName = formName,
                            DataResponse = System.Text.Json.JsonSerializer.Serialize(root),
                            RequestorId = int.Parse(HttpContext.GetUserId())
                        };

                        _context.Rfqs.Add(rfqData);
                        _context.SaveChanges();

                        //update boms
                        foreach (var id in ids)
                        {
                            var bom = _context.Boms.Where(x => x.Id == id).FirstOrDefault();
                            bom.RFQ = rfqData;
                            _context.Boms.Update(bom);
                            _context.SaveChanges();
                        }

                        var title = "New RFQ with name " + rfqData.Rfqname + " Created";
                        var pics = new List<int> { rfqData.RequestorId.Value };

                        await _auditTrailService.InsertLogAsync(
                            Common.LogLevel.Information, title,
                            pics: pics,
                            tableChange: nameof(Rfq),
                            empId: int.Parse(HttpContext.GetUserId()),
                            ProjectId: rfqData.Project.Id
                            );
                            
                        return Json(new { result = true, redirectId = rfqData.Id });
                    }
                    else if (JObject.Parse(validation)["status"].ToString() == "RFQ Approved" && type == FormType.PR)
                    {
                        var prList = new List<Pr>();
                        var updateBoms = await _context.Boms.Include(x => x.RFQ).Where(x => ids.Contains(x.Id)).ToListAsync();
                        var rfqIds = updateBoms.Select(x => x.RFQId).Distinct().ToList();

                        foreach (var id in ids)
                        {
                            prList.Add(await initPR(updateBoms.FirstOrDefault(x => x.Id == id).RFQId.Value, projectId, id));
                        }

                        var dataResponse = new PRRoot();
                        dataResponse.data = new PRData();
                        dataResponse.data.header = new List<PRGridHeader>();
                        dataResponse.data.currencyGrid = new List<Currencies>() { new Currencies { cr_currency = "USD" } };
                        dataResponse.data.bomGrid = new List<PRBOMGrid>();
                        var dataHeader = new PRGridHeader();
                        var dataGrid1 = new PRBOMGrid();
                        dataResponse.data.createDate = DateTime.Now;
                        var rfqLinks = new List<object>();

                        foreach (var pr in prList)
                        {
                            //importing and merging
                            var selectPR = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                            dataResponse.data.bomGrid.AddRange(selectPR.data.bomGrid);
                            if (dataResponse.data.header.Count() == 0)
                                dataResponse.data.header.AddRange(selectPR.data.header);

                            var currencyList = selectPR.data.currencyGrid
                                     .Where(x => !dataResponse.data.currencyGrid.Any(x => x.cr_currency.Equals(x.cr_currency)))
                                     .ToList();
                            dataResponse.data.currencyGrid.AddRange(currencyList);

                            _context.Remove(pr);
                        }

                        var formName = "PR5";
                        var prTemp = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();

                        var prData = new Pr
                        {
                            Data = prTemp.Data,
                            AddedBy = "sys",
                            Prname = _context.Prs.Count() > 0 ? formName + _context.Prs.Select(x => x.Id).Max() + 1 : formName,
                            ProjectId = projectId,
                            CreatedDate = DateTime.Now,
                            LastUpdatedDate = DateTime.Now,
                            Status = "New",
                            FormName = formName,
                            totalApprover = 0,
                            RevisionNo = 0,
                            DataResponse = System.Text.Json.JsonSerializer.Serialize(dataResponse),
                            Rfqid = string.Join(";", prList.Select(x => int.Parse(x.Rfqid)).Distinct().ToList()),
                            // RequestorId = int.Parse(HttpContext.GetUserId()),
                            RequestorId = updateBoms.FirstOrDefault()?.RFQ?.RequestorId ?? int.Parse(HttpContext.GetUserId()),
                            DepartmentId = int.Parse(HttpContext.GetUserDeptId())
                        };


                        _context.Add(prData);
                        await _context.SaveChangesAsync();

                        updateBoms.ForEach(x =>
                        {
                            x.LastUpdatedDate = DateTime.Now;
                            x.PRId = prData.Id;
                        });

                        _context.Boms.UpdateRange(updateBoms);
                        await _context.SaveChangesAsync();

                        await _auditTrailService.InsertLogAsync(
                       Common.LogLevel.Information, prData.Prname + " has been created.",
                       fullMessage: prData.FormName + " has been created.",
                       tableChange: nameof(Pr),
                       empId: int.Parse(HttpContext.GetUserId()),
                       ProjectId: (int)prData.ProjectId
                       );

                        return Json(new { result = true, redirectId = prData.Id });
                    }
                }

                return Json(new { result = true });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = ex.Message, str = select });

            }
        }

        public IActionResult ShowPDF(int supportFileId)
        {
            var file = _context.SupportFile.FirstOrDefault(x => x.Id == supportFileId);
            //string filePath = Path.Combine(Environment.WebRootPath, @"documents\" + file.FileName);

            //var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            //return File(fileStream, "application/pdf",file.FileName);
            if (file == null)
                throw new Exception($@"Error from BOM/ShowPDf where supportFileId {supportFileId} cant found");

            var filenameOnDisk = "";
            if (String.IsNullOrEmpty(file.FileNameOnDisk))
            {
                filenameOnDisk = file.FileName;
            }
            else
            {
                filenameOnDisk = file.FileNameOnDisk;
            }

            var (bytes, fileType, fileName) = _fileWriter.DownloadFiles("documents", filenameOnDisk);
            var base64String = Convert.ToBase64String(bytes);
            var pdfBase64 = $@"data:application/pdf;base64,{base64String}";
            var showPdfModel = new ShowPDFModel(pdfBase64, fileName);
            showPdfModel.SupportFileId = supportFileId;
            return View(showPdfModel);
        }

        public ActionResult PreviewImage(int id)
        {
            var file = _context.SupportFile.FirstOrDefault(x => x.Id == id);

            if (file == null)
                return NoContent();

            return View(file);
        }

        // GET: BOMs/Edit/5
        [HttpGet]
        public JsonResult Edit(int? id, int projectId)
        {

            var bomId = HttpContext.Session.GetInt32("id");
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.EBOM);
            navigation.Add(PageTitle.BOM);
            navigation.Add(PageTitle.EditBOM);
            link.Add("/Home/Index");
            link.Add("/BOM/Index/" + bomId);
            link.Add("/BOM/Edit" + id);
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;
            if (id == null)
            {
                return Json(new { msg = "ID not found." });
            }

            ViewBag.ProjectId = projectId;
            var BOM = _context.Boms
                .Include(x => x.BomSupportFiles)
                .ThenInclude(x => x.SupportFile)
                .Where(x => x.Id == id).FirstOrDefault();

            var isPRApproved = CommonHelpers.availableBaseStatus("PR Approved", BOM.Status);

            if (BOM == null)
            {
                return Json(new { msg = "Data not found.", id = BOM.Id });
            }
            //var project = _context.Projects.Where(d => d.Id == BOM.ProjectId).FirstOrDefault();
            //ViewBag.projectCode = project.Code;
            //ViewBag.poNo = project.PoNumber;
            //ViewBag.projectName = project.Name;
            //ViewBag.category = project.Category;
            //ViewBag.customer = BOM.Requestor;

            return Json(new { data = BOM, msg = "", id = BOM.Id, block = isPRApproved });
        }


        [HttpPost]
        public async Task<JsonResult> Edit(Bom form)
        {
            try
            {
                var newForm = _context.Boms.FirstOrDefault(x => x.Id == form.Id);

                var project = _context.Projects.FirstOrDefault(x => x.Id == newForm.ProjectId);

                if (!CommonHelpers.isPics(project))
                    return Json(new { result = false, msg = "You have not authorized to perform this action, please contact admin for further info.", id = project.Id });

                if (isProjectCompleted(project.Id))
                    return Json(new { result = false, msg = "The changes was disabled as project is in completed status.", id = project.Id });

                if (form.Module == null || form.Brand == null || form.ItemCategory == null || form.PartDesc == null || form.UnitQty == null || form.RequireQty == null)
                    return Json(new { result = false, msg = "You have to fill in all the required fields before perform the submitting.", id = project.Id });

                
                if (!string.IsNullOrEmpty(form.PartDesc))
                {
                    var existingBomWithSameDesc = await _context.Boms
                        .AnyAsync(x => x.ProjectId == newForm.ProjectId &&
                                      x.Id != form.Id && 
                                      x.PartDesc.ToLower().Trim() == form.PartDesc.ToLower().Trim());

                    if (existingBomWithSameDesc)
                    {
                        return Json(new
                        {
                            result = false,
                            msg = "This description already exists in the project. Please use a different description.",
                            id = project.Id
                        });
                    }
                }


                if (Enum.Parse<BomItemCategory>(form.ItemCategory) != BomItemCategory.Fabrication && form.PartNumber == null)
                {
                    return Json(new { result = false, msg = "You have to fill in all the required fields before perform the submitting.", id = project.Id });
                }

                if (BrandDisabledWords.DisableWords.Contains(form.Brand.Trim().ToLower()))
                    return Json(new { result = false, msg = "Please provide the valid brand name.", id = project.Id });

                newForm.Module = form.Module;
                newForm.PartNumber = form.PartNumber;
                newForm.Brand = form.Brand;
                newForm.PartDesc = form.PartDesc;
                newForm.ItemCategory = form.ItemCategory;
                newForm.UnitQty = form.UnitQty;
                newForm.RequireQty = form.RequireQty;
                newForm.LastUpdatedDate = DateTime.Now;
                newForm.ETA = form.ETA;
                newForm.ReceivedQuantity = form.ReceivedQuantity;
                newForm.ReceivedDate = form.ReceivedDate;
                newForm.IssuedPIC = form.IssuedPIC;
                newForm.IssuedQuantity = form.IssuedQuantity;
                newForm.IssuedOutDate = form.IssuedOutDate;
                //newForm.BalanceToIssueOutQty = form.BalanceToIssueOutQty;
                //newForm.BalanceToReceiveQty = form.BalanceToReceiveQty;
                _context.Boms.Update(newForm);

                _context.SaveChanges();
                return Json(new { result = true, id = form.Id });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = ex.Message, id = form.Id });
            }
        }

        //public async Task<IActionResult> View(int? id)
        //{
        //    if (id == null)
        //    {
        //        return NotFound();
        //    }

        //    ViewBag.ProjectId = id;
        //    var BOM = await _context.Rfqs.Where(x => x.ProjectId == id).ToListAsync();
        //    if (BOM == null)
        //    {
        //        return NotFound();
        //    }
        //    return View(BOM);
        //}

        [HttpPost]
        public async Task<JsonResult> addNewBOM(Bom model, int? projectId, bool hasUpload)
        {
            if (projectId == null)
                throw new NullReferenceException(nameof(projectId));

            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
            if (!CommonHelpers.isPics(project))
                return Json(new { result = false, msg = "You have not authorized to perform this action, please contact admin for further info.", id = project.Id });

            if (isProjectCompleted(projectId.Value))
                return Json(new { result = false, msg = "The changes were disabled as the project is in completed status.", id = project.Id });

            if (model.Module == null || model.ItemCategory == null || model.PartDesc == null || model.UnitQty == null || model.RequireQty == null)
                return Json(new { result = false, msg = "You have to fill in all the required fields before submitting.", id = project.Id });
    
            if (!string.IsNullOrEmpty(model.PartDesc))
            {
                var existingBomWithSameDesc = await _context.Boms
                    .AnyAsync(x => x.ProjectId == projectId.Value &&
                                  x.PartDesc.ToLower().Trim() == model.PartDesc.ToLower().Trim());

                if (existingBomWithSameDesc)
                {
                    return Json(new
                    {
                        result = false,
                        msg = "This description already exists in the project. Please use a different description.",
                        id = project.Id
                    });
                }
            }

            if (Enum.Parse<BomItemCategory>(model.ItemCategory) == BomItemCategory.Fabrication && !hasUpload)
            {
                return Json(new { result = false, msg = "Please upload the drawing as the item category is fabrication.", id = project.Id });
            }
            else if (Enum.Parse<BomItemCategory>(model.ItemCategory) != BomItemCategory.Fabrication && model.PartNumber == null)
            {
                return Json(new { result = false, msg = "You have to fill in all the required fields before performing the submission.", id = project.Id });
            }
            /*// Check if part number exists and handle the copy logic
            if (copyExisting && existingBomId.HasValue)
            {
                var existingBom = _context.Boms.FirstOrDefault(x => x.Id == existingBomId.Value);
                if (existingBom != null)
                {
                    model.ItemCategory = existingBom.ItemCategory;
                    model.Module = existingBom.Module;
                    model.PartDesc = existingBom.PartDesc;
                    model.Brand = existingBom.Brand;
                    model.UnitQty = existingBom.UnitQty;
                    model.RequireQty = existingBom.RequireQty;

                    // Clone RFQ logic
                    if (existingBom.RFQId != null)
                    {
                        var existingRFQ = _context.Rfqs.FirstOrDefault(r => r.Id == existingBom.RFQId);
                        if (existingRFQ != null)
                        {
                            // Generate new RFQName from latest RFQ number
                            var lastRFQs = _context.Rfqs
                                .Where(r => r.Rfqname.StartsWith("RFQ"))
                                .Select(r => new { r.Id, r.Rfqname })
                                .ToList();

                            int maxNumber = 0;
                            foreach (var rfq in lastRFQs)
                            {
                                var match = Regex.Match(rfq.Rfqname, @"RFQ(\d+)");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                                {
                                    if (number > maxNumber)
                                        maxNumber = number;
                                }
                            }
                            string newRFQName = $"RFQ{maxNumber + 1}";

                            // Copy DataResponse and supplierList
                            string newDataResponse = existingRFQ.DataResponse;

                            try
                            {
                                var jObj = JObject.Parse(existingRFQ.DataResponse);

                                // Access "data" object
                                var dataObj = jObj["data"] as JObject;
                                if (dataObj != null)
                                {
                                    // ✅ Copy supplierList from the original
                                    var supplierList = dataObj["supplierList"];
                                    dataObj["supplierList"] = supplierList != null ? supplierList.DeepClone() : new JArray();

                                    // ✅ Generate new bomNo for each item in bomGrid
                                    var bomGrid = dataObj["bomGrid"] as JArray;
                                    if (bomGrid != null)
                                    {
                                        foreach (var item in bomGrid)
                                        {
                                            var newBomNo = project.Code + "-" + CommonHelpers.generateUID();
                                            item["bomNo"] = newBomNo;
                                        }
                                    }

                                    // ✅ Save back the updated "data" object
                                    jObj["data"] = dataObj;
                                }

                                newDataResponse = jObj.ToString(Formatting.None);
                            }
                            catch (Exception ex)
                            {
                                // Optional: log the error
                                newDataResponse = existingRFQ.DataResponse;
                            }


                            var newRFQ = new Rfq
                            {
                                Data = existingRFQ.Data,
                                DataResponse = newDataResponse,
                                CreatedDate = DateTime.Now,
                                LastUpdatedDate = DateTime.Now,
                                AddedBy = "sys",
                                ProjectId = projectId.Value,
                                FormName = "RFQ4",
                                Rfqname = newRFQName,
                                Status = "New",
                                BuyerId = existingRFQ.BuyerId,
                                Remark = existingRFQ.Remark,
                                RequestorId = existingRFQ.RequestorId,
                                LatestSupplierRemark = existingRFQ.LatestSupplierRemark,
                                BuyerLastSubmittedDate = existingRFQ.BuyerLastSubmittedDate
                            };

                            _context.Rfqs.Add(newRFQ);
                            _context.SaveChanges();

                            model.RFQId = newRFQ.Id;
                        }
                    }
                }
            }*/

            if (!string.IsNullOrEmpty(model.Brand) && BrandDisabledWords.DisableWords.Contains(model.Brand.ToLower()))
                return Json(new { result = false, msg = "Please provide a valid brand name.", id = project.Id });

            model.Status = "New";
            model.ProjectId = projectId.Value;
            model.AddedBy = HttpContext.User.Identity.Name;
            model.CreatedDate = model.LastUpdatedDate = DateTime.Now;
            model.BomNo = project.Code + "-" + CommonHelpers.generateUID();

            _context.Boms.Add(model);

            if (_context.SaveChanges() > 0)
            {
                return Json(new { result = true, msg = "", id = model.Id });
            }

            return Json(new { result = false, msg = "", id = model.Id });
        }

        /*public async Task<JsonResult> checkExistingPart(string partNumber, int projectId)
        {
            if (string.IsNullOrEmpty(partNumber))
            {
                return Json(new { result = false, msg = "Part Number is required." });
            }

            var existingBOM = await _context.Boms
                .FirstOrDefaultAsync(b => b.PartNumber == partNumber && b.ProjectId == projectId);

            if (existingBOM != null)
            {
                return Json(new
                {
                    exists = true,
                    msg = "The Part Number already exists in the BOM.",
                    existingBomId = existingBOM.Id,
                    existingData = new
                    {
                        existingBOM.PartDesc,
                        existingBOM.Brand,
                        existingBOM.UnitQty,
                        existingBOM.RequireQty,
                        existingBOM.RFQId,
                        //existingBOM.PRId
                    }
                });
            }

            return Json(new { result = true, msg = "The Part Number is available." });
        }*/


        [HttpPost]
        public async Task<ActionResult> attachFile(int? bomId, List<IFormFile> files, List<string> types, bool isFromReceiving = false)
        {
            var bom = _context.Boms.FirstOrDefault(x => x.Id == bomId);
            var project = _context.Projects.FirstOrDefault(x => x.Id == bom.ProjectId);
            if (!CommonHelpers.isPics(project))
                return Forbid();

            if (files != null)
            {
                foreach (var t in types)
                {
                    var exists = _context.BomSupportFile.Where(x => x.BomId == bomId && x.Type == t).ToList();
                    if (exists.Count() > 0)
                    {
                        _context.BomSupportFile.RemoveRange(exists);
                    }
                }

                await _context.SaveChangesAsync();

                var upFiles = await _fileWriter.UploadDocument(files);

                for (int i = 0; i < types.Count(); i++)
                {
                    foreach (var upFile in upFiles)
                    {
                        upFile.CreatedAt = DateTime.Now;
                        bom.BomSupportFiles.Add(new BomSupportFile
                        {
                            SupportFile = upFile,
                            Type = types[i]
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }

            if (isFromReceiving)
            {
                return RedirectToAction("Index", "ReceivingTracker");
            }
            return RedirectToAction("Index", new { id = bom.ProjectId });
        }

        [HttpPost]
        public async Task<ActionResult> MassAttachDrawings(int projectId, List<IFormFile> massFiles)
        {
            var project = await _context.Projects.FirstOrDefaultAsync(x => x.Id == projectId);
            if (project == null || !CommonHelpers.isPics(project) || isProjectCompleted(projectId))
                return Json(new { success = false, msg = "You do not have permission to modify this project." });

            if (massFiles == null || massFiles.Count == 0)
                return Json(new { success = false, msg = "No files were uploaded." });

            int successCount = 0;
            List<string> errorFiles = new List<string>();

            
            var projectBoms = await _context.Boms
                .Where(x => x.ProjectId == projectId && x.PartNumber != null)
                .ToListAsync();

            
            var sortedFiles = massFiles.OrderBy(f => f.FileName).ToList();

            foreach (var file in sortedFiles)
            {
                if (Path.GetExtension(file.FileName).ToLower() != ".pdf")
                {
                    errorFiles.Add($"{file.FileName} (Not a PDF)");
                    continue;
                }

                
                string partNoFromFileName = Path.GetFileNameWithoutExtension(file.FileName);

                
                var matchingBom = projectBoms.FirstOrDefault(x => x.PartNumber.Equals(partNoFromFileName, StringComparison.OrdinalIgnoreCase));

                if (matchingBom == null)
                {
                    errorFiles.Add(file.FileName);
                    continue;
                }

                
                var existingSupportFiles = _context.BomSupportFile.Where(x => x.BomId == matchingBom.Id && x.Type == "IC").ToList();
                if (existingSupportFiles.Any())
                {
                    _context.BomSupportFile.RemoveRange(existingSupportFiles);
                    await _context.SaveChangesAsync();
                }

                
                var upFiles = await _fileWriter.UploadDocument(new List<IFormFile> { file });
                if (upFiles != null && upFiles.Any())
                {
                    var newSupportFile = upFiles.First();
                    newSupportFile.CreatedAt = DateTime.Now;

                    _context.BomSupportFile.Add(new BomSupportFile
                    {
                        BomId = matchingBom.Id,
                        SupportFile = newSupportFile,
                        Type = "IC"
                    });

                    successCount++;
                }
                else
                {
                    errorFiles.Add($"{file.FileName} (Upload failed)");
                }
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, successCount = successCount, errorFiles = errorFiles });
        }


        public ActionResult downloadFile(int? id)
        {
            if (id.HasValue)
            {
                var file = _context.SupportFile.FirstOrDefault(x => x.Id == id);
                var (bytes, fileType, fileName) = _fileWriter.DownloadFiles("documents", file.FileName);

                return File(bytes, fileType, fileName);
            }

            return NoContent();
        }
        public ActionResult removeFile(int? id, int projectId)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            if (!CommonHelpers.isPics(project) || isProjectCompleted(projectId))
                return Forbid();

            if (id.HasValue)
            {
                var file = _context.SupportFile.FirstOrDefault(x => x.Id == id);

                _fileWriter.DeleteDocument(file.FileName);
                _context.SupportFile.Remove(file);
                _context.SaveChanges();

                return RedirectToAction("Index", new { id = projectId });
            }

            return NoContent();
        }

        [HttpPost]
        public async Task<JsonResult> addNewRFQ(int? projectId, int? bomId, string name = "")
        {
            try
            {
                if (projectId == null)
                    throw new NullReferenceException(nameof(projectId));

                if (!bomId.HasValue)
                    throw new NullReferenceException(nameof(bomId));

                //var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);
                //var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
                //if (!authorizationResult.Succeeded)
                //    return Json(new { result = false, msg = "You haven't authorize to perform this action" });

                if (isProjectCompleted(projectId.Value))
                    return Json(new { result = false, msg = "The changes was disabled as project is in completed status.", id = projectId.Value });

                var bom = _context.Boms.Where(x => x.Id == bomId.Value).FirstOrDefault();
                var formName = "RFQ4";
                var form = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();
                var rfqData = new Rfq
                {
                    Data = form.Data,
                    AddedBy = "sys",
                    Rfqname = !string.IsNullOrEmpty(name) ? name : _context.Rfqs.Any() ? formName + _context.Rfqs.Select(x => x.Id).Max() + 1 : formName,
                    ProjectId = (int)projectId,
                    CreatedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    Status = "New",
                    RequestorId = int.Parse(HttpContext.GetUserId()),
                    FormName = formName
                };

                rfqData.DataResponse = await initRFQ(projectId.Value, bomId.Value);
                bom.RFQ = rfqData;
                _context.Boms.Update(bom);

                if (_context.SaveChanges() > 0)
                {
                    var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Sent, Url.Action("Edit", "RFQ", new { id = rfqData.Id, bomId = bom.Id }));
                    var project = await _context.Projects.FirstOrDefaultAsync(x => x.Id == projectId);
                    var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
                    var emailList = _context.EmployeeInfos.Where(x => (pics.Contains(x.Id) || x.Id == project.PICLeader) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                    var title = "New RFQ with name " + rfqData.Rfqname + " Created";
                    await _emailService.Send(emailList, title, html);

                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, title,
                        fullMessage: msg,
                        tableChange: nameof(Rfq),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: rfqData.Project.Id
                        );
                }

                return Json(new { result = true, id = rfqData.Id });
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<string> initRFQ(int projectId, int bomId)
        {
            var project = _context.Projects
                .Include(x => x.CategoryInfo)
                .Where(x => x.Id == projectId).FirstOrDefault();

            // Null check for project
            if (project == null)
                throw new Exception($"Project with ID {projectId} not found");

            var updateBom = _context.Boms
                .Include(x => x.BomSupportFiles)
                .ThenInclude(x => x.SupportFile)
                .FirstOrDefault(x => x.Id == bomId);

            // Null check for BOM
            if (updateBom == null)
                throw new Exception($"BOM with ID {bomId} not found");

            ////set suppliers
            var dataResponse = new RFQRoot();
            dataResponse.data = new RFQData();
            dataResponse.data.header = new List<RFQHeader>();
            dataResponse.data.bomGrid = new List<BOMGrid>();
            dataResponse.data.supplierList = new List<SupplierList>();
            var dataHeader = new RFQHeader();
            var bomGrid = new BOMGrid();

            dataHeader.code = project.Code;
            dataHeader.projectName = project.Name;
            dataHeader.poNumber = project.PoNumber;
            dataHeader.projectName = project.Name;
            dataHeader.projectCategory = project.CategoryInfo?.CategoryShortName ?? string.Empty; // Null check
            dataHeader.customer = project.Customer;
            dataHeader.poQuantity = project.PoQuantity ?? 0;
            dataHeader.budgetMaterialCost = project.BudgetMaterialCost ?? 0;
            dataHeader.actualMaterialCost = project.ActualMaterialCost ?? 0;
            dataHeader.requester = HttpContext.GetShortName() + "(" + HttpContext.GetUserDept() + ")";

            //populate bom grid with null checks
            bomGrid.itemLineNo = bomId;
            bomGrid.module = updateBom.Module ?? string.Empty;
            bomGrid.partNo = updateBom.PartNumber ?? string.Empty;

            
            string itemCategory = !string.IsNullOrEmpty(updateBom.ItemCategory)
                ? updateBom.ItemCategory + " Part"
                : " Part";
            bomGrid.itemCategory = itemCategory;

            bomGrid.brand = updateBom.Brand ?? string.Empty;
            bomGrid.description = updateBom.PartDesc ?? string.Empty;
            bomGrid.totalQuantity = updateBom.RequireQty ?? 0;

            
            dataResponse.data.header.Add(dataHeader);
            dataResponse.data.bomGrid.Add(bomGrid);
            dataResponse.data.createDate = DateTime.Now;
            dataResponse.data.ddURL = "";

            
            bool isFab = false;

            if (!string.IsNullOrEmpty(updateBom.ItemCategory))
            {
                isFab = updateBom.ItemCategory.Equals("Fabrication", StringComparison.OrdinalIgnoreCase)
                        || updateBom.ItemCategory.Equals("Fabrication Part", StringComparison.OrdinalIgnoreCase);
            }
            

            dataResponse.data.isFabrication = isFab ? 1 : 0;  

            
            if (updateBom.BomSupportFiles != null)
            {
                var supportFileItem = updateBom.BomSupportFiles.FirstOrDefault(x => x.Type == "IC");

                if (supportFileItem?.SupportFile != null)
                {
                    var ddUrl = new List<object> {
                new
                {
                    lineNo = updateBom.Id,
                    downloadLink = generateViewURL(supportFileItem.SupportFile),
                    fileName = supportFileItem.SupportFile.FileName
                }
            };
                    dataResponse.data.ddURL = JsonConvert.SerializeObject(ddUrl);
                }
            }

            return System.Text.Json.JsonSerializer.Serialize(dataResponse);
        }

        public async Task<Pr> initPR(int id, int projectId, int? bomId = 0)
        {
            var formName = "PR5";
            var prTemp = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();
            var rfq = await _context.Rfqs.FirstOrDefaultAsync(x => x.Id == id);

            var dataResponse = new PRRoot();
            dataResponse.data = new PRData();
            dataResponse.data.header = new List<PRGridHeader>();
            // dataResponse.data.currencyGrid = new List<Currencies>() { new Currencies { cr_currency = "USD" } };
            dataResponse.data.bomGrid = new List<PRBOMGrid>();
            var dataHeader = new PRGridHeader();
            dataResponse.data.createDate = DateTime.Now;
                                                                                                   
            RFQRoot rfqRoot = new RFQRoot();
            if (rfq != null)
            {                                                       
                if (rfq.DataResponse != null)
                {
                    rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                    dataResponse.data.ddURL = rfqRoot.data.ddURL;
                    dataResponse.data.isFabrication = rfqRoot.data.isFabrication ?? 0;
                    dataResponse.data.discountTotal = rfqRoot.data.discountTotal;
                    dataResponse.data.taxSelect = rfqRoot.data.taxSelect;
                    dataResponse.data.shippingFee = rfqRoot.data.shippingFee;
                    dataResponse.data.bankCharges = rfqRoot.data.bankCharges;
                    dataResponse.data.netPrice = rfqRoot.data.netPrice;
                    if (rfqRoot.data.header != null)
                    {
                        dataHeader = rfqRoot.data.header.FirstOrDefault().ToModel<PRGridHeader>();
                    }

                    if (rfqRoot.data.bomGrid.Count > 0)
                    {
                        var selectedBom = rfqRoot.data.bomGrid
                               .FirstOrDefault(x => x.itemLineNo == bomId.Value);

                        if (selectedBom != null)
                        {
                            var bomItem = selectedBom.ToModel<PRBOMGrid>();

                            if (selectedBom.leadTime != null)
                            {
                                bomItem.leadTime = selectedBom.leadTime;

                                if (int.TryParse(selectedBom.leadTime.ToString(), out int days))
                                {
                                    bomItem.eta = DateTime.Now.Date.AddDays(days);
                                }
                            }
                            else if (selectedBom.eta != null)
                            {
                                bomItem.eta = selectedBom.eta;
                            }
                            else
                            {
                                bomItem.eta = DateTime.Now.Date;
                            }

                            dataResponse.data.currencyGrid = new List<Currencies>() { new Currencies { cr_currency = selectedBom.currency } };

                            dataResponse.data.bomGrid.Add(bomItem);
                            //  dataResponse.data.bomGrid.Add(selectedBom.ToModel<PRBOMGrid>());
                        }
                    }
                }
            }

            dataHeader.typeOfPurchase = "";
            ViewBag.IsDepartmentSame = HttpContext.GetUserDept() == dataHeader.department;

            //Map into JSON model
            dataResponse.data.header.Add(dataHeader);

            var prData = new Pr
            {
                Data = prTemp.Data,
                AddedBy = "sys",
                Prname = _context.Prs.Count() > 0 ? formName + _context.Prs.Select(x => x.Id).Max() + 1 : formName,
                ProjectId = projectId,
                CreatedDate = DateTime.Now,
                LastUpdatedDate = DateTime.Now,
                Status = "New",
                FormName = formName,
                Rfqid = rfq.Id.ToString(),
                totalApprover = 0,
                RevisionNo = 0,
                //RequestorId = int.Parse(HttpContext.GetUserId()),
                RequestorId = rfq.RequestorId ?? int.Parse(HttpContext.GetUserId()),
                DepartmentId = int.Parse(HttpContext.GetUserDeptId())
            };

            prData.DataResponse = System.Text.Json.JsonSerializer.Serialize(dataResponse);

            _context.Prs.Add(prData);

            await _context.SaveChangesAsync();
            return prData;
        }

        public async Task<JsonResult> addNewPR(int? projectId, int? bomId)
        {
            try
            {
                if (projectId == null || bomId == null)
                    throw new NullReferenceException("projectId or BOMId is null");

                var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);
                if (!CommonHelpers.isPics(project) || isProjectCompleted(projectId.Value))
                    return Json(new { result = false, msg = "UnAuthorized" });

                var bom = _context.Boms.FirstOrDefault(x => x.Id == bomId);
                var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == bom.RFQId);

                rfq.LastUpdatedDate = DateTime.Now;
                rfq.AddedBy = "sys";
                rfq.Status = "RFQ Approved";

                _context.Rfqs.Update(rfq);

                if (String.IsNullOrEmpty(project.PoNumber)) //not allow create PR if no PO. Temporary check with Project PO, will check by category later.
                {
                    _context.SaveChanges();
                    return Json(new { result = false, msg = "Need to have PO Number before able create PR" });
                }

                var prData = await initPR(rfq.Id, projectId.Value, bomId);
                bom.PRId = prData.Id;
                _context.Boms.UpdateRange(bom);

                //_context.POC.Add(new POC { PRId = prData.Id, DataResponse = prData.DataResponse, Type = FormTypeInString.PR });
                var pics = new List<int> { prData.RequestorId };

                if (_context.SaveChanges() > 0)
                {
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, prData.Prname + " has been created.",
                        pics: pics,
                        fullMessage: prData.FormName + " has been created.",
                        tableChange: nameof(Pr),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: prData.Project.Id
                        );
                }

                return Json(new { result = true, id = prData.Id });
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<ActionResult> editPICs(string picsContainer, int projectId, int? leader)
        {
            try
            {
                var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);
                var emailList = _context.EmployeeInfos.Where(x => !string.IsNullOrEmpty(x.Email) && !string.IsNullOrEmpty(x.ShortName)).ToList();
                var user = _context.EmployeeInfos.Where(x => x.UserName == HttpContext.User.Identity.Name).FirstOrDefault();
                var checkLeader = (user.Id == project.PICLeader);

                /*            if (HttpContext.GetUserType() != "PM" || isProjectCompleted(projectId) || 
                                HttpContext.GetUserType() != "CEO" || HttpContext.GetUserType() != "GFC" || HttpContext.GetUserType() != "HOD")
                                return Forbid();
                */
                if (!string.IsNullOrEmpty(picsContainer))
                {
                    var pics = picsContainer.Split(',');
                    var picList = String.Join(",", pics.Select(x => x.Split('(')[0]).ToList());

                    var checkPics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').ToList() : new List<string>();

                    var removedPICs = checkPics.Except(picList.Split(',').ToList());
                    var addedPICs = picList.Split(',').ToList().Except(checkPics);

                    var addedPICsIds = new List<int>();
                    var removedPICsIds = new List<int>();

                    if (addedPICs.Any())
                    {
                        foreach (var item in addedPICs)
                        {
                            if (!String.IsNullOrEmpty(item))
                            {
                                addedPICsIds.Add(Convert.ToInt16(item));
                            }
                        }
                    }

                    if (removedPICs.Any())
                    {
                        foreach (var item in removedPICs)
                        {
                            if (!String.IsNullOrEmpty(item))
                            {
                                removedPICsIds.Add(Convert.ToInt16(item));
                            }
                        }
                    }

                    var titleNewPIC = "Added to Project as PIC";
                    var titleRemovedPIC = "Removed from Project";

                    var (msgAddedPIC, htmlAddedPIC) = CommonHelpers.AddOrDeletePorjectPICTemplate(titleNewPIC, "ADDED", project.Name, Url.Action("Index", "BOM", new { id = project.Id }));
                    var (msgRemovedPIC, htmlRemovedPIC) = CommonHelpers.AddOrDeletePorjectPICTemplate(titleRemovedPIC, "REMOVED", project.Name, Url.Action("Index", "BOM", new { id = project.Id }));

                    await _emailService.Send(emailList.Where(x => addedPICsIds.Contains(x.Id) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList(), titleNewPIC, htmlAddedPIC);
                    await _emailService.Send(emailList.Where(x => removedPICsIds.Contains(x.Id) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList(), titleRemovedPIC, htmlRemovedPIC);

                    project.PICS = picList;
                    if (leader != null)
                    {
                        var check = false;
                        foreach (var item in pics)
                        {
                            var pic = item.Split("(");
                            if (!check)
                            {
                                var newPicIDs = new List<int>();
                                foreach (var id in pic)
                                {
                                    newPicIDs.Add(Convert.ToInt16(id));
                                }

                                if (newPicIDs[0] == leader)
                                {
                                    project.PICLeader = leader;
                                    check = true;
                                }
                                else
                                {
                                    project.PICLeader = null;
                                }
                            }
                        }
                    }
                    if (checkLeader)
                    {
                        var titleInformManagement = "The PIC Leader modify the Project PIC";
                        var HODPMEmailList = _context.EmployeeInfos.Where(x => (x.UserType == "HOD" || x.UserType == "PM") && x.IsNotificationOn && !string.IsNullOrEmpty(x.Email)).Select(x => x.Email).Distinct().ToList();
                        var (msgInform, htmlInform) = CommonHelpers.PICLeaderAddOrDeletePIC(titleInformManagement, user.UserName, project.Name, Url.Action("Index", "BOM", new { id = project.Id }));
                        await _emailService.Send(HODPMEmailList, titleInformManagement, htmlInform);
                    }
                    _context.Projects.Update(project);
                    _context.SaveChanges();
                }
                else
                {
                    project.PICS = string.Empty;
                    _context.Projects.Update(project);
                    _context.SaveChanges();
                }
                return Json(new { status = true, msg = "Saved successful" });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "error occur: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Submit(Bom form)
        {
            if (form.Id != null)
            {
                var newForm = _context.Boms.FirstOrDefault(x => x.Id == form.Id);

                var project = _context.Projects.FirstOrDefault(x => x.Id == newForm.ProjectId);

                var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
                if (!authorizationResult.Succeeded || isProjectCompleted(project.Id))
                    return Forbid();

                newForm.LastUpdatedDate = DateTime.Now;
                newForm.AddedBy = "sys";

                newForm.Status = "Pending RFQ Approved";
                _context.Boms.Update(newForm);

                await _context.SaveChangesAsync();

                return RedirectToAction("Index", new { id = newForm.ProjectId });
            }

            return RedirectToAction("Index", "Project");
        }

        [HttpPost]
        public async Task<JsonResult> ImportBOM()
        {
            var postedFile = Request.Form.Files["FileUpload"];
            var projectId = Request.Form["projectId"];
            var project = await _context.Projects.FirstOrDefaultAsync(x => x.Id == int.Parse(projectId));

            if (isProjectCompleted(Convert.ToInt16(projectId)))
                return Json(new { result = false, msg = "The changes was disabled as project is in completed status." });

            //var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
            var authorizationResult = CommonHelpers.isPics(project);
            if (!authorizationResult)
                return Json(new { result = false, msg = "The changes was disabled as your role isn't give the permission to perform this action." });

            try
            {
                if (postedFile != null)
                {
                    var FileDic = "UploadedBOMExcel";
                    string rootPath = this.Environment.WebRootPath; //wwwroot folder
                    string FilePath = Path.Combine(rootPath + "/" + FileDic);


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
                    ISheet sheet = workbook.GetSheet("BOM");
                    var bomList = new List<Bom>();
                    for (int row = 1; row <= sheet.LastRowNum; row++)
                    {
                        try
                        {
                            if (sheet.GetRow(row) == null)
                                continue;

                            if (sheet.GetRow(row).GetCell(1).CellType == NPOI.SS.UserModel.CellType.Blank)
                                continue;

                            if (sheet.GetRow(row).GetCell(2) == null || string.IsNullOrEmpty(sheet.GetRow(row).GetCell(2).ToString()))
                            {
                                return Json(new { result = false, msg = "Import Fail! Please dont leave blank on brand field." });
                            }

                            var bom = new Bom();
                            bom.BomNo = project.Code + "-" + CommonHelpers.generateUID();
                            bom.Module = sheet.GetRow(row).GetCell(0) == null ? "" : sheet.GetRow(row).GetCell(0).ToString();
                            bom.PartDesc = sheet.GetRow(row).GetCell(1) == null ? "" : sheet.GetRow(row).GetCell(1).ToString();
                            bom.Brand = sheet.GetRow(row).GetCell(2) == null ? "" : sheet.GetRow(row).GetCell(2).ToString();
                            bom.PartNumber = sheet.GetRow(row).GetCell(3) == null ? "" : sheet.GetRow(row).GetCell(3).ToString();
                            var itemCategory = sheet.GetRow(row).GetCell(4) == null ? "" : sheet.GetRow(row).GetCell(4).ToString();
                            bom.ItemCategory = itemCategory;
                            var unitQty = sheet.GetRow(row).GetCell(5) == null ? "0" : sheet.GetRow(row).GetCell(5).ToString();
                            var poQty = project.PoQuantity ?? 0;
                            //var totalQty = sheet.GetRow(row).GetCell(6) == null ? "0" : sheet.GetRow(row).GetCell(6).ToString();
                            bom.UnitQty = String.IsNullOrEmpty(unitQty) ? 0 : Convert.ToInt16(unitQty);
                            //bom.RequireQty = String.IsNullOrEmpty(totalQty) ? 0 : Convert.ToInt16(totalQty);
                            bom.RequireQty = poQty * bom.UnitQty;
                            bom.Status = "New";
                            bom.AddedBy = "Excel Import";
                            bom.ProjectId = !String.IsNullOrEmpty(projectId) ? Convert.ToInt16(projectId) : 0;
                            bom.CreatedDate = DateTime.Now;
                            bom.LastUpdatedDate = DateTime.Now;

                            bomList.Add(bom);
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    _context.Boms.AddRange(bomList);

                    if (_context.SaveChanges() > 0)
                    {
                        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
                        {
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                        };
                        await _auditTrailService.InsertLogAsync(
                            Common.LogLevel.Information, "New BOM Data Import in Project " + bomList[0].Project.Code,
                            tableChange: nameof(Bom),
                            dataAfter: JsonConvert.SerializeObject(bomList),
                            empId: int.Parse(HttpContext.GetUserId()),
                            ProjectId: bomList[0].Project.Id
                        );
                    }

                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
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

        [HttpPost]
        public async Task<JsonResult> UpdateETA()
        {
            var postedFile = Request.Form.Files["FileUpload"];
            var projectId = Request.Form["projectId"];

            try
            {
                if (postedFile != null)
                {
                    var FileDic = "UploadedETAExcel";
                    string FilePath = Path.Combine(_env.WebRootPath + "/" + FileDic);

                    if (!Directory.Exists(FilePath))
                        Directory.CreateDirectory(FilePath);

                    var filename = Guid.NewGuid().ToString() + postedFile.FileName;
                    var filePath = Path.Combine(FilePath, filename);

                    using (FileStream stream = new FileStream(filePath, FileMode.Create))
                    {
                        await postedFile.CopyToAsync(stream);
                    }

                    XSSFWorkbook workbook;
                    using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        workbook = new XSSFWorkbook(file);
                    }

                    ISheet sheet = workbook.GetSheetAt(0);
                    for (int row = 7; row <= sheet.LastRowNum; row++)
                    {
                        var currentRow = sheet.GetRow(row);
                        if (currentRow == null) continue;

                        var bomLineNo = currentRow.GetCell(1)?.ToString();
                        if (string.IsNullOrWhiteSpace(bomLineNo)) continue;

                        // Parse BOM ID (Line No)
                        if (!int.TryParse(bomLineNo, out int bomId)) continue;

                        // ETA Date
                        DateTime? newETA = null;
                        var etaCell = currentRow.GetCell(22);
                        if (etaCell != null)
                        {
                            if (etaCell.CellType == NPOI.SS.UserModel.CellType.Numeric && DateUtil.IsCellDateFormatted(etaCell))
                            {
                                newETA = etaCell.DateCellValue;
                                Console.WriteLine($"ETA: {newETA}");
                            }
                            else if (etaCell.CellType == NPOI.SS.UserModel.CellType.String && DateTime.TryParse(etaCell.StringCellValue, out var parsedDate))
                            {
                                newETA = parsedDate;
                            }
                        }

                        // Get BOM record
                        var bom = await _context.Boms.FirstOrDefaultAsync(x => x.Id == bomId);
                        if (bom != null && newETA.HasValue)
                        {

                            // Only update if ETA changed
                            if (bom.ETA == null || bom.ETA.Value.Date != newETA.Value.Date)
                            {
                                bom.ETA = newETA;
                                bom.Status = "ETA Updated";
                                bom.LastUpdatedDate = DateTime.Now;
                                _context.Boms.Update(bom);
                            }

                            //if (bom.RFQId != null)
                            //{
                            //    var rfq = await _context.Rfqs.FirstOrDefaultAsync(r => r.Id == bom.RFQId);
                            //    if (rfq != null)
                            //    {
                            //        var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                            //        var matchingItems = rfqRoot.data.bomGrid
                            //            .Where(x => x.itemLineNo == bom.Id) 
                            //            .ToList();

                            //        foreach (var itemGrid in matchingItems)
                            //        {
                            //            if (itemGrid.eta == null || itemGrid.eta.Value.Date != newETA.Value.Date)
                            //            {
                            //                itemGrid.eta = newETA.Value;
                            //            }
                            //        }

                            //        rfq.DataResponse = JsonConvert.SerializeObject(rfqRoot);
                            //        _context.Rfqs.Update(rfq);
                            //    }

                            //    var pr = await _context.Prs.FirstOrDefaultAsync(p => p.Rfqid == bom.RFQId.ToString());
                            //    if (pr != null)
                            //    {
                            //        var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                            //        var matchingPRItems = prRoot.data.bomGrid
                            //            .Where(x => x.itemLineNo == bom.Id)
                            //            .ToList();

                            //        foreach (var itemGrid in matchingPRItems)
                            //        {
                            //            if (itemGrid.eta == null || itemGrid.eta.Value.Date != newETA.Value.Date)
                            //            {
                            //                itemGrid.eta = newETA.Value;
                            //            }
                            //        }

                            //        pr.DataResponse = JsonConvert.SerializeObject(prRoot);
                            //        _context.Prs.Update(pr);
                            //    }
                            //}

                        }
                    }

                    await _context.SaveChangesAsync();

                    // Delete temp file
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    return Json(new { result = true, msg = "ETA updated successfully!" });
                }

                return Json(new { result = false, msg = "No file selected." });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = $"Error: {ex.Message}" });
            }
        }


        protected bool isProjectCompleted(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);

            return project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult ExportToExcel_ByCategory(int id)
        {
            var entity = _context.Projects
                .Include(x => x.Boms)
                .Include(x => x.CategoryInfo)
                .FirstOrDefault(x => x.Id == id);

            if (entity == null)
                return NoContent();

            using (var workBook = new XLWorkbook())
            {
                //get bom list
                var bom = _context.Boms.Where(x => x.ProjectId == id)
                                                .Include(f => f.Project)
                                                .Include(x => x.RFQ)
                                                .Include(x => x.PR)
                                                .Include(x => x.BomSupportFiles)
                                                .ThenInclude(x => x.SupportFile)
                                                .ToList();

                List<string> worksheetNames = new List<string> { "FAB", "MEC", "ELEC", "PNE", "Others" };

                foreach (string sheetName in worksheetNames)
                {
                    var currentRow = 1;
                    var worksheet = workBook.Worksheets.Add(sheetName);

                    //date zone
                    var dateZ = typeof(BOMDateZone).GetProperties();
                    for (int i = 1; i <= dateZ.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = dateZ[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (dateZ[i - 1].Name == "DocumentNo")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.DocumentNo;
                        }
                        else if (dateZ[i - 1].Name == "RevisionNo")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.RevisionNo;
                        }
                        else if (dateZ[i - 1].Name == "EffectiveDate")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.EffectiveDate;
                        }
                        else
                        {
                            worksheet.Cell(currentRow + 1, i).Value = entity.GetType().GetProperty(dateZ[i - 1].Name).GetValue(entity, null);
                        }
                    }

                    var filteredBom = bom.Where(item =>
                        (sheetName == "FAB" && item.ItemCategory == "Fabrication") ||
                        (sheetName == "MEC" && item.ItemCategory == "Mechanical") ||
                        (sheetName == "ELEC" && item.ItemCategory == "Electrical") ||
                        (sheetName == "PNE" && item.ItemCategory == "Pneumatic") ||
                        (sheetName == "Others" && item.ItemCategory != "Fabrication" && item.ItemCategory != "Mechanical" &&
                            item.ItemCategory != "Electrical" && item.ItemCategory != "Pneumatic")).ToList();

                    var check = false;
                    var userList = _context.EmployeeInfos.ToList();
                    var user = userList.Where(x => x.UserName == HttpContext.User.Identity.Name).FirstOrDefault();
                    if (user.UserType == "CEO" || user.UserType == "GFC" || user.UserType == "HOD" || user.UserType == "PM" || user.UserType == "SuperAdminAssistant" || user.UserType == "SuperAdmin" || user.UserType == "Buyer" || user.UserType == "Requestor")
                    {
                        check = true;
                    }
                    if (filteredBom.Count > 0 && check == false)
                    {
                        var pic = filteredBom[0].Project.PICS;
                        var picIdList = pic.Split(",");
                        var picNameList = userList.Where(x => picIdList.Contains(x.Id.ToString())).Select(x => x.ShortName).ToList();
                        check = picNameList.Contains(user.ShortName);
                    }
                    var BomList = new List<BOMZone2>();
                    double Bom_totalPrice = 0;
                    SupplierObject selectSupplier = null;
                    double unitPrice = 0;
                    string quotationLine = string.Empty;
                    foreach (var item in filteredBom)
                    {
                        double totalPrice = 0;
                        if (item.RFQ != null && item.RFQ.DataResponse != null)
                        {
                            RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(item.RFQ.DataResponse);
                            totalPrice = root.data.bomGrid.FirstOrDefault().totalPrice ?? 0;
                            //unitPrice = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice) ?? 0;
                            var checkUnitPrice = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice).FirstOrDefault();
                            unitPrice = checkUnitPrice.HasValue ? Convert.ToDouble(checkUnitPrice) : 0;
                            quotationLine = root.data.bomGrid.FirstOrDefault().quotationLineNo;
                            selectSupplier = root.data.supplierList.FirstOrDefault(x => x.recommended.HasValue && x.recommended == true)?.supplierKey;
                            //Bom_totalPrice += totalPrice;
                        }
                        var model = new BOMZone2();
                        model.UpdatedDate = item.LastUpdatedDate;
                        model.BuyerLastSubmittedDate = item.RFQ != null ? item.RFQ.BuyerLastSubmittedDate : null;
                        model.RfqUpdatedDate = item.RFQ != null ? item.RFQ.LastUpdatedDate : null;
                        model.PrUpdatedDate = item.PR != null ? item.PR.LastUpdatedDate : null;
                        //model.BomLineNo = item.Id;
                        model.Status = item.Status;
                        model.Module = item.Module;
                        model.PartNo = item.PartNumber;
                        model.Description = item.PartDesc;
                        model.Brand = item.Brand;
                        model.Category = item.ItemCategory;
                        model.UnitQuantity = item.UnitQty;
                        model.TotalQuantity = item.RequireQty;
                        model.UnitPrice = null; //unitPrice;
                        model.VendorName = selectSupplier != null ? selectSupplier.name : string.Empty;
                        //model.QuotationLineNo = quotationLine;
                        model.ReceivedDate = item.ReceivedDate;
                        //model.ReceivedQuantity = item.ReceivedQuantity;
                        //model.BalanceToIssueOutQuantity = item.BalanceToIssueOutQty;

                        var supplierAttachments = selectSupplier != null
                           ? _context.SupplierAttachment
                           .Include(x => x.SupportFile)
                           .Where(x => x.RFQId == item.RFQId && x.SupplierInfoId.ToString() == selectSupplier.id).ToList()
                           : null;

                        if (supplierAttachments != null && supplierAttachments.Count() > 0)
                        {
                            var quotations = string.Empty;
                            foreach (var attach in supplierAttachments)
                            {
                                quotations += Url.Action("downloadFile", "BOM", new { id = attach.SupportFileId }, HttpContext.Request.Scheme) + "\n";
                            }
                            model.VendorQuotation = quotations;
                        }

                        //if (vendorIsSelected && item.BomSupportFiles.Where(x => x.Type == "QUO_"+rowIndex).Any())
                        //    model.VendorQuotation = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "QUO_" + rowIndex).FirstOrDefault().SupportFileId })}";


                        var POQuotations = item.BomSupportFiles.Where(x => x.Type == "PO" && x.BomId == item.Id)
                            .ToList();

                        if (POQuotations != null && POQuotations.Count() > 0)
                        {
                            var PoNo = "";

                            var POQuotation = item.BomSupportFiles.Where(x => x.Type == "PO" && x.BomId == item.Id)
                                .FirstOrDefault()
                                .SupportFileId;


                            PoNo = _context.SupportFile.Where(x => x.Id == POQuotation).FirstOrDefault().FileName;

                            model.PoNo = PoNo.Substring(0, 12);

                            var poDate = _context.Pos.FirstOrDefault(x => x.Prid == item.PRId)?.CreatedDate;
                            model.PoDate = poDate;

                        }


                        //if (item.BomSupportFiles.Where(x => x.Type == "DO").Any())
                        //model.DoNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "DO").FirstOrDefault().SupportFileId })}";

                        //if (item.BomSupportFiles.Where(x => x.Type == "GRN").Any())
                        //model.GrnNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "DO").FirstOrDefault().SupportFileId })}";

                        //if (item.BomSupportFiles.Where(x => x.Type == "INC").Any())
                        //model.InvoiceNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "INC").FirstOrDefault().SupportFileId })}";

                        //check rfq and pr
                        if (item.PR != null)
                            model.PrNo = CommonHelpers.renamePR(item.PR.Prname, item.PR.RevisionNo);

                        if (item.RFQ != null)
                            model.RfqNo = item.RFQ.Rfqname;

                        //read pr
                        var prQty = 0;
                        double prUP = 0;
                        double prTP = 0;
                        if (item.PR != null && !string.IsNullOrEmpty(item.PR.DataResponse))
                        {
                            var prResponse = item.PR.DataResponse;
                            var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                            //prQty = prRoot.data.bomGrid.FirstOrDefault().totalQuantity;
                            prQty = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.totalQuantity).FirstOrDefault();
                            //prUP = prRoot.data.bomGrid.FirstOrDefault().unitPrice ?? 0;
                            var checkUnitPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice).FirstOrDefault();
                            prUP = checkUnitPrice.HasValue ? Convert.ToDouble(checkUnitPrice) : 0;
                            //prTP = prRoot.data.bomGrid.FirstOrDefault().totalPrice ?? 0;
                            var checkTotalPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.totalPrice).FirstOrDefault();
                            prTP = checkTotalPrice.HasValue ? Convert.ToDouble(checkTotalPrice) : 0;

                            model.PoQuantity = prQty;
                            if (check)
                            {
                                model.UnitPrice = prUP;
                                model.TotalPrice = prTP;
                            }

                            Bom_totalPrice += prTP;
                            model.EstimateDeliveryDate = prRoot.data.bomGrid.FirstOrDefault().eta?.ToString() ?? string.Empty;
                        }

                        BomList.Add(model);
                    }
                    //ViewBag.ActualMaterialCost = Bom_totalPrice;

                    //project zone
                    currentRow += 3;
                    var projectZone = typeof(ProjectZone).GetProperties();
                    var picsId = entity.PICS.Split(",").ToList();
                    var picsName = userList.Where(x => picsId.Contains(x.Id.ToString())).Select(x => x.ShortName).ToList();
                    entity.PICS = string.Join(',', picsName);

                    var project = entity.ToModel<ProjectZone>();
                    for (int i = 1; i <= projectZone.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = projectZone[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (projectZone[i - 1] == typeof(ProjectZone).GetProperty("ActualMaterialCost"))
                        {
                            var tempProject = new Project();
                            tempProject = _context.Projects.FirstOrDefault();
                            if (CommonHelpers.isPics(tempProject)
                            || HttpContext.GetUserType().Equals("Buyer", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("CEO", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("GFC", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("HOD", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("PM", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("Quality", StringComparison.OrdinalIgnoreCase)
                            || HttpContext.GetUserType().Equals("Sales", StringComparison.OrdinalIgnoreCase))
                            {
                                worksheet.Cell(currentRow + 1, i).Value = Bom_totalPrice;
                                continue;
                            }
                            else
                            {
                                worksheet.Cell(currentRow + 1, i).Value = "";
                                continue;
                            }
                        }

                        if (projectZone[i - 1] == typeof(ProjectZone).GetProperty("ProjectCategory"))
                        {
                            worksheet.Cell(currentRow + 1, i).Value = project.ProjectCategory;
                            continue;
                        }

                        worksheet.Cell(currentRow + 1, i).Value = project.GetType().GetProperty(projectZone[i - 1].Name).GetValue(project, null);
                    }

                    //bom zone
                    currentRow += 3;
                    var bomZone = typeof(BOMZone2).GetProperties();
                    //set bom title
                    for (int i = 1; i <= bomZone.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = bomZone[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    }
                    //set bom row
                    foreach (var item in BomList)
                    {
                        currentRow++;
                        for (int i = 1; i <= bomZone.Count(); i++)
                        {
                            worksheet.Cell(currentRow, i).Value = item.GetType().GetProperty(bomZone[i - 1].Name).GetValue(item, null);
                        }
                    }

                    worksheet.Columns().AdjustToContents();
                    worksheet.Columns().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }

                using (var stream = new MemoryStream())
                {
                    workBook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "bom_" + DateTime.Now.ToString() + ".xlsx");
                }

            }
        }

        public IActionResult ExportToExcel(int id, string? status = null)
        {
            var entity = _context.Projects
                .Include(x => x.Boms)
                .Include(x => x.CategoryInfo)
                .FirstOrDefault(x => x.Id == id);

            if (entity == null)
                return NoContent();

            var currentRow = 1;
            using (var workBook = new XLWorkbook())
            {
                var worksheet = workBook.Worksheets.Add("BOM");

                //date zone
                var dateZ = typeof(BOMDateZone).GetProperties();
                for (int i = 1; i <= dateZ.Count(); i++)
                {
                    worksheet.Cell(currentRow, i).Value = dateZ[i - 1].Name.FirstCharToUpper().ConvertEnum();
                    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                    if (dateZ[i - 1].Name == "DocumentNo")
                    {
                        worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.DocumentNo;
                    }
                    else if (dateZ[i - 1].Name == "RevisionNo")
                    {
                        worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.RevisionNo;
                    }
                    else if (dateZ[i - 1].Name == "EffectiveDate")
                    {
                        worksheet.Cell(currentRow + 1, i).Value = BomHeaderAttribute.EffectiveDate;
                    }
                    else
                    {
                        worksheet.Cell(currentRow + 1, i).Value = entity.GetType().GetProperty(dateZ[i - 1].Name).GetValue(entity, null);
                    }
                }

                //get bom list
                //var bom = _context.Boms.Where(x => x.ProjectId == id)
                //                                .Include(f => f.Project)
                //                                .Include(x => x.RFQ)
                //                                .Include(x => x.PR)
                //                                .Include(x => x.BomSupportFiles)
                //                                .ThenInclude(x => x.SupportFile)
                //                                .ToList();

                IQueryable<Bom> bomQuery = _context.Boms
                    .Where(x => x.ProjectId == id)
                    .Include(f => f.Project)
                    .Include(x => x.RFQ)
                    .Include(x => x.PR)
                    .Include(x => x.BomSupportFiles)
                    .ThenInclude(x => x.SupportFile);

                if (!string.IsNullOrEmpty(status))
                {
                    var statuses = status.Split(',');
                    bomQuery = bomQuery.Where(x => statuses.Contains(x.Status));
                }

                var bom = bomQuery.ToList();

                var check = false;
                var userList = _context.EmployeeInfos.ToList();
                var user = userList.Where(x => x.UserName == HttpContext.User.Identity.Name).FirstOrDefault();
                if (user.UserType == "CEO" || user.UserType == "GFC" || user.UserType == "HOD" || user.UserType == "PM" || user.UserType == "SuperAdminAssistant" || user.UserType == "SuperAdmin" || user.UserType == "Buyer" || user.UserType == "Requestor")
                {
                    check = true;
                }
                if (bom.Count > 0 && check == false)
                {
                    var pic = bom[0].Project.PICS;
                    var picIdList = pic.Split(",");
                    var picNameList = userList.Where(x => picIdList.Contains(x.Id.ToString())).Select(x => x.ShortName).ToList();
                    check = picNameList.Contains(user.ShortName);
                }
                var BomList = new List<BOMZone>();
                double Bom_totalPrice = 0;
                SupplierObject selectSupplier = null;
                double unitPrice = 0;
                string quotationLine = string.Empty;
                foreach (var item in bom)
                {
                    double totalPrice = 0;
                    if (item.RFQ != null && item.RFQ.DataResponse != null)
                    {
                        RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(item.RFQ.DataResponse);
                        totalPrice = root.data.bomGrid.FirstOrDefault().totalPrice ?? 0;
                        //unitPrice = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice) ?? 0;
                        var checkUnitPrice = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice).FirstOrDefault();
                        unitPrice = checkUnitPrice.HasValue ? Convert.ToDouble(checkUnitPrice) : 0;
                        quotationLine = root.data.bomGrid.FirstOrDefault().quotationLineNo;
                        selectSupplier = root.data.supplierList.FirstOrDefault(x => x.recommended.HasValue && x.recommended == true)?.supplierKey;
                        //Bom_totalPrice += totalPrice;
                    }
                    var model = new BOMZone();
                    model.UpdatedDate = item.LastUpdatedDate;
                    model.BuyerLastSubmittedDate = item.RFQ != null ? item.RFQ.BuyerLastSubmittedDate : null;
                    model.RfqUpdatedDate = item.RFQ != null ? item.RFQ.LastUpdatedDate : null;
                    model.PrUpdatedDate = item.PR != null ? item.PR.LastUpdatedDate : null;
                    model.BomLineNo = item.Id;
                    model.Status = item.Status;
                    model.Module = item.Module;
                    model.PartNo = item.PartNumber;
                    model.Description = item.PartDesc;
                    model.Brand = item.Brand;
                    model.Category = item.ItemCategory;
                    model.UnitQuantity = item.UnitQty;
                    model.TotalQuantity = item.RequireQty;
                    model.UnitPrice = null; //unitPrice;
                    model.VendorName = selectSupplier != null ? selectSupplier.name : string.Empty;
                    model.QuotationLineNo = quotationLine;
                    model.ReceivedDate = item.ReceivedDate;
                    model.ReceivedQuantity = item.ReceivedQuantity;
                    model.BalanceToIssueOutQuantity = item.BalanceToIssueOutQty;

                    var supplierAttachments = selectSupplier != null
                       ? _context.SupplierAttachment
                       .Include(x => x.SupportFile)
                       .Where(x => x.RFQId == item.RFQId && x.SupplierInfoId.ToString() == selectSupplier.id).ToList()
                       : null;

                    if (supplierAttachments != null && supplierAttachments.Count() > 0)
                    {
                        var quotations = string.Empty;
                        foreach (var attach in supplierAttachments)
                        {
                            quotations += Url.Action("downloadFile", "BOM", new { id = attach.SupportFileId }, HttpContext.Request.Scheme) + "\n";
                        }
                        model.VendorQuotation = quotations;
                    }

                    //if (vendorIsSelected && item.BomSupportFiles.Where(x => x.Type == "QUO_"+rowIndex).Any())
                    //    model.VendorQuotation = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "QUO_" + rowIndex).FirstOrDefault().SupportFileId })}";


                    var POQuotations = item.BomSupportFiles.Where(x => x.Type == "PO" && x.BomId == item.Id)
                        .ToList();

                    if (POQuotations != null && POQuotations.Count() > 0)
                    {
                        var PoNo = "";

                        var POQuotation = item.BomSupportFiles.Where(x => x.Type == "PO" && x.BomId == item.Id)
                            .FirstOrDefault()
                            .SupportFileId;


                        PoNo = _context.SupportFile.Where(x => x.Id == POQuotation).FirstOrDefault().FileName;

                        model.PoNo = PoNo.Substring(0, 12);

                    }


                    if (item.BomSupportFiles.Where(x => x.Type == "DO").Any())
                        model.DoNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "DO").FirstOrDefault().SupportFileId })}";

                    if (item.BomSupportFiles.Where(x => x.Type == "GRN").Any())
                        model.GrnNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "DO").FirstOrDefault().SupportFileId })}";

                    if (item.BomSupportFiles.Where(x => x.Type == "INC").Any())
                        model.InvoiceNo = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("downloadFile", new { id = item.BomSupportFiles.Where(x => x.Type == "INC").FirstOrDefault().SupportFileId })}";

                    //check rfq and pr
                    if (item.PR != null)
                        model.PrNo = CommonHelpers.renamePR(item.PR.Prname, item.PR.RevisionNo);

                    if (item.RFQ != null)
                        model.RfqNo = item.RFQ.Rfqname;

                    //read pr
                    var prQty = 0;
                    double prUP = 0;
                    double prTP = 0;
                    if (item.PR != null && !string.IsNullOrEmpty(item.PR.DataResponse))
                    {
                        var prResponse = item.PR.DataResponse;
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                        //prQty = prRoot.data.bomGrid.FirstOrDefault().totalQuantity;
                        prQty = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.totalQuantity).FirstOrDefault();
                        //prUP = prRoot.data.bomGrid.FirstOrDefault().unitPrice ?? 0;
                        var checkUnitPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.unitPrice).FirstOrDefault();
                        prUP = checkUnitPrice.HasValue ? Convert.ToDouble(checkUnitPrice) : 0;
                        //prTP = prRoot.data.bomGrid.FirstOrDefault().totalPrice ?? 0;
                        var checkTotalPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == item.Id).Select(x => x.totalPrice).FirstOrDefault();
                        prTP = checkTotalPrice.HasValue ? Convert.ToDouble(checkTotalPrice) : 0;

                        model.PoQuantity = prQty;
                        if (check)
                        {
                            model.UnitPrice = prUP;
                            model.TotalPrice = prTP;
                        }

                        Bom_totalPrice += prTP;
                        model.EstimateDeliveryDate = prRoot.data.bomGrid.FirstOrDefault().eta?.ToString() ?? string.Empty;
                    }

                    BomList.Add(model);
                }
                //ViewBag.ActualMaterialCost = Bom_totalPrice;

                //project zone
                currentRow += 3;
                var projectZone = typeof(ProjectZone).GetProperties();
                var picsId = entity.PICS.Split(",").ToList();
                var picsName = userList.Where(x => picsId.Contains(x.Id.ToString())).Select(x => x.ShortName).ToList();
                entity.PICS = string.Join(',', picsName);

                var project = entity.ToModel<ProjectZone>();
                for (int i = 1; i <= projectZone.Count(); i++)
                {
                    worksheet.Cell(currentRow, i).Value = projectZone[i - 1].Name.FirstCharToUpper().ConvertEnum();
                    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                    if (projectZone[i - 1] == typeof(ProjectZone).GetProperty("ActualMaterialCost"))
                    {
                        var tempProject = new Project();
                        tempProject = _context.Projects.FirstOrDefault();
                        if (CommonHelpers.isPics(tempProject)
                        || HttpContext.GetUserType().Equals("Buyer", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("CEO", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("GFC", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("HOD", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("PM", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("Quality", StringComparison.OrdinalIgnoreCase)
                        || HttpContext.GetUserType().Equals("Sales", StringComparison.OrdinalIgnoreCase))
                        {
                            worksheet.Cell(currentRow + 1, i).Value = Bom_totalPrice;
                            continue;
                        }
                        else
                        {
                            worksheet.Cell(currentRow + 1, i).Value = "";
                            continue;
                        }
                    }

                    if (projectZone[i - 1] == typeof(ProjectZone).GetProperty("ProjectCategory"))
                    {
                        worksheet.Cell(currentRow + 1, i).Value = project.ProjectCategory;
                        continue;
                    }

                    worksheet.Cell(currentRow + 1, i).Value = project.GetType().GetProperty(projectZone[i - 1].Name).GetValue(project, null);
                }

                //bom zone
                currentRow += 3;
                var bomZone = typeof(BOMZone).GetProperties();
                //set bom title
                for (int i = 1; i <= bomZone.Count(); i++)
                {
                    worksheet.Cell(currentRow, i).Value = bomZone[i - 1].Name.FirstCharToUpper().ConvertEnum();
                    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                }
                //set bom row
                foreach (var item in BomList)
                {
                    currentRow++;
                    for (int i = 1; i <= bomZone.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = item.GetType().GetProperty(bomZone[i - 1].Name).GetValue(item, null);
                    }
                }

                worksheet.Columns().AdjustToContents();
                worksheet.Columns().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                using (var stream = new MemoryStream())
                {
                    workBook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "bom_" + DateTime.Now.ToString() + ".xlsx");
                }

            }
        }

        [HttpPost]
        public async Task<ActionResult> DeleteMultiBOM(string select, int projectId)
        {
            try
            {
                if (!string.IsNullOrEmpty(select))
                {

                    var ids = select.Split(",").Select(int.Parse).ToList();
                    ids = ids.Where(x => x != null && x != 0).ToList();

                    var bomList = _context.Boms.Where(x => ids.Contains(x.Id)).ToList();
                    var statusList = new List<string>() { "PR Approved", "PO Approved", "Partial Received", "Item Received", "Item Issued Out", "Completed" };
                    var isCompleted = bomList.Where(x => statusList.Contains(x.Status)).Count();
                    if (isCompleted > 0)
                    {
                        return Json(new { status = false, msg = "BOM cannot be deleted after PR Approved.", str = select });
                    }
                    var project = await _context.Projects.Where(x => x.Id == projectId).FirstOrDefaultAsync();
                    var isPic = CommonHelpers.isPics(project);
                    if (!isPic)
                    {
                        return Json(new { status = false, msg = "Only Project PIC can perform delete action." });
                    }

                    //_context.Boms.RemoveRange(bomList);
                    //_context.SaveChanges();
                    foreach (var bom in bomList)
                    {
                        await deleteForm(bom.Id);
                    }

                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, $@"Bom items {bomList.Select(x => x.PartDesc).ToArray().ToString()} has been removed",
                        tableChange: nameof(Pr),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: bomList[0].Project.Id
                        );
                }

                return Json(new { status = true, msg = "BOM deleted successfully.", str = select });
            }
            catch (Exception ex)
            {
                return Json(new { msg = ex.Message, str = select });
            }
        }

        [HttpPost]
        public string GetShortName(string userIds)
        {
            try
            {
                if (!String.IsNullOrEmpty(userIds))
                {
                    var splitIds = userIds.Split(',').ToList();
                    var Ids = new List<int>();
                    foreach (var id in splitIds)
                    {
                        if (!String.IsNullOrEmpty(id))
                        {
                            Ids.Add(Convert.ToInt16(id));
                        }
                    }
                    if (!String.IsNullOrEmpty(userIds))
                    {
                        var list = _context.EmployeeInfos
                            .Include(x => x.EmpDepartment)
                            .Where(x => Ids.Contains(x.Id)).Select(x => x.ShortName + "(" + x.EmpDepartment.Name + ")").ToList();
                        string joinIds = string.Join(",", list);
                        return joinIds;
                    }
                }
                return "";
            }
            catch (Exception e)
            {
                return "";
            }
        }

        [HttpPost]
        public ActionResult SaveExtraCost(int id, decimal cost)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);
            if (project == null) return Ok();

            project.ExtraCost = cost;
            _context.SaveChanges();
            return RedirectToAction("Index", new { id });
        }


        [HttpPost]
        public string GetPICId(int projectId)
        {
            try
            {
                if (projectId != 0)
                {
                    var PICs = _context.Projects.Where(x => x.Id == projectId).Select(x => x.PICS).FirstOrDefault();
                    return PICs;

                }
                return "";
            }
            catch (Exception e)
            {
                return "";
            }
        }
        public string generateViewURL(SupportFile supportFile)
        {
            if (CommonHelpers.isImage(supportFile.ContentType))
            {
                return Url.Action("PreviewImage", "BOM", new { id = supportFile.Id }, HttpContext.Request.Scheme);
            }
            else if (supportFile.ContentType.StartsWith(".xl"))
            {
                var documentPath = Path.Combine(Environment.WebRootPath, "documents", supportFile.FileName);

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

        [HttpPost]
        public int GetPICIdByShortName(string shortName)
        {
            try
            {
                if (!String.IsNullOrEmpty(shortName))
                {
                    var PICs = _context.EmployeeInfos.Where(x => x.ShortName == shortName).Select(x => x.Id).FirstOrDefault();
                    return PICs;

                }
                return 0;
            }
            catch (Exception e)
            {
                return 0;
            }
        }

        [HttpPost]
        public async Task<ActionResult> CopyBOMToProject(int sourceProjectId, int targetProjectId, string bomIds)
        {
            try
            {
                var errors = new List<object>();

                // project validation
                var sourceProject = await _context.Projects.FirstOrDefaultAsync(x => x.Id == sourceProjectId);
                var targetProject = await _context.Projects.FirstOrDefaultAsync(x => x.Id == targetProjectId);

                if (sourceProject == null)
                {
                    errors.Add(new { code = "SOURCE_PROJECT_NOT_FOUND", message = "Source project not found", field = "sourceProjectId" });
                    return Json(new { success = false, errors });
                }

                if (targetProject == null)
                {
                    errors.Add(new { code = "TARGET_PROJECT_NOT_FOUND", message = "Target project not found", field = "targetProjectId" });
                    return Json(new { success = false, errors });
                }

                if (targetProject.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new { code = "PROJECT_COMPLETED", message = "Cannot copy to a completed project. Please reopen the project first.", field = "targetProjectId" });
                    return Json(new { success = false, errors });
                }

                if (sourceProjectId == targetProjectId)
                {
                    errors.Add(new { code = "SAME_PROJECT", message = "Cannot copy BOM items to the same project", field = "targetProjectId" });
                    return Json(new { success = false, errors });
                }

                // check the user permission for their target project
                var isPic = CommonHelpers.isPics(targetProject);
                var isHOD = HttpContext.GetUserType().Equals("HOD", StringComparison.OrdinalIgnoreCase);
                var isAuthorized = isPic || isHOD || HttpContext.GetUserType().Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase);

                if (!isAuthorized)
                {
                    errors.Add(new { code = "PERMISSION_DENIED", message = $"You don't have permission to edit project {targetProject.Code}. Only PICs and HODs can add BOM items.", field = "targetProjectId" });
                    return Json(new { success = false, errors });
                }

                // bom item validation
                var selectedBomIds = bomIds.Split(',').Select(int.Parse).ToList();
                var sourceBoms = await _context.Boms
                    .Include(x => x.RFQ)
                    .Include(x => x.PR)
                    .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                    .Where(x => selectedBomIds.Contains(x.Id))
                    .ToListAsync();

                if (sourceBoms.Count != selectedBomIds.Count)
                {
                    errors.Add(new { code = "BOM_NOT_FOUND", message = "One or more BOM items not found", field = "bomIds" });
                    return Json(new { success = false, errors });
                }

                // verify the bom items from source project
                var invalidBoms = sourceBoms.Where(x => x.ProjectId != sourceProjectId).ToList();
                if (invalidBoms.Any())
                {
                    errors.Add(new { code = "INVALID_BOM_PROJECT", message = "Selected BOM items do not belong to the source project", field = "bomIds" });
                    return Json(new { success = false, errors });
                }


                // Strict validation: Only allow specific statuses
                var allowedStatuses = new List<string> { "New", "RFQ Sent", "RFQ Updated" };
                
                var invalidStatusBoms = sourceBoms.Where(x => !allowedStatuses.Contains(x.Status)).ToList();
                if (invalidStatusBoms.Any())
                {
                    errors.Add(new { code = "INVALID_STATUS", message = $"Cannot copy BOM items with status other than New, RFQ Sent, or RFQ Updated. Invalid items: {string.Join(", ", invalidStatusBoms.Select(x => x.Module))}", field = "bomIds" });
                    return Json(new { success = false, errors });
                }


                var targetBoms = await _context.Boms.Where(x => x.ProjectId == targetProjectId).ToListAsync();
                foreach (var sourceBom in sourceBoms)
                {
                    var duplicate = targetBoms.FirstOrDefault(x => 
                        x.PartNumber == sourceBom.PartNumber && 
                        x.Module == sourceBom.Module);
                    
                    if (duplicate != null)
                    {
                        errors.Add(new { 
                            code = "DUPLICATE_BOM", 
                            message = $"BOM item '{sourceBom.Module} - {sourceBom.PartDesc}' already exists in target project", 
                            field = "bomIds",
                            bomId = sourceBom.Id 
                        });
                    }
                }

                if (errors.Any())
                {
                    return Json(new { success = false, errors });
                }

         
                var copiedBomIds = new List<int>();
                var copiedCount = 0;

                foreach (var sourceBom in sourceBoms)
                {
     
                    var newBom = new Bom
                    {
                        BomNo = targetProject.Code + "-" + CommonHelpers.generateUID(),
                        Module = sourceBom.Module,
                        PartNumber = sourceBom.PartNumber,
                        Brand = sourceBom.Brand,
                        PartDesc = sourceBom.PartDesc,
                        ItemCategory = sourceBom.ItemCategory,
                        UnitQty = sourceBom.UnitQty,
                        RequireQty = sourceBom.RequireQty,
                        ProjectId = targetProjectId,
                        Status = sourceBom.Status,  
                        CreatedDate = DateTime.Now,
                        LastUpdatedDate = DateTime.Now,
                        AddedBy = HttpContext.GetUserEmpId(),
                        IsCopied = true  
                    };

                    _context.Boms.Add(newBom);
                    await _context.SaveChangesAsync();

                    if (sourceBom.RFQId.HasValue && sourceBom.RFQ != null)
                    {
                        var sourceRfq = sourceBom.RFQ;
                        string newDataResponse = sourceRfq.DataResponse;

                        if (!string.IsNullOrEmpty(sourceRfq.DataResponse))
                        {
                            try
                            {
                                var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(sourceRfq.DataResponse);
                                if (rfqRoot != null && rfqRoot.data != null)
                                {
                                   
                                    if (rfqRoot.data.header != null && rfqRoot.data.header.Count > 0)
                                    {
                                        rfqRoot.data.header[0].code = targetProject.Code;
                                        rfqRoot.data.header[0].projectName = targetProject.Name;
                                    }


                                    if (rfqRoot.data.bomGrid != null)
                                    {
                                        var matchingItem = rfqRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == sourceBom.Id);
                                        if (matchingItem != null)
                                        {
                                            matchingItem.itemLineNo = newBom.Id;
                                            matchingItem.bomNo = newBom.BomNo;
                                            
                                          
                                            rfqRoot.data.bomGrid = new List<BOMGrid> { matchingItem };
                                        }
                                    }

                                    // Clear Remark(Buyer) from supplierList to prevent "Copied from project" text
                                    if (rfqRoot.data.supplierList != null)
                                    {
                                        foreach (var supplier in rfqRoot.data.supplierList)
                                        {
                                            supplier.remark = null;
                                        }
                                    }

                                    newDataResponse = JsonConvert.SerializeObject(rfqRoot);
                                }
                            }
                            catch (Exception)
                            {
                               
                            }
                        }

                        var rfqFormName = "RFQ4";
                        var nextRfqId = _context.Rfqs.Any() ? _context.Rfqs.Max(x => x.Id) + 1 : 1;
                        
                        var newRfq = new Rfq
                        {
                            Rfqname = rfqFormName + nextRfqId,
                            ProjectId = targetProjectId,
                            DataResponse = newDataResponse,
                            Status = "New",
                            CreatedDate = DateTime.Now,
                            LastUpdatedDate = DateTime.Now,
                            AddedBy = HttpContext.GetUserEmpId()
                        };

                        _context.Rfqs.Add(newRfq);
                        await _context.SaveChangesAsync();

                        newBom.RFQId = newRfq.Id;
                        _context.Boms.Update(newBom);
                        await _context.SaveChangesAsync();

                
                        var supplierAttachments = await _context.SupplierAttachment
                            .Include(x => x.SupportFile)
                            .Where(x => x.RFQId == sourceRfq.Id)
                            .ToListAsync();

                        foreach (var attachment in supplierAttachments)
                        {
                       
                            var sourceFilePath = Path.Combine(_env.WebRootPath, "documents", attachment.SupportFile.FileName);
                            
                            if (System.IO.File.Exists(sourceFilePath))
                            {
                                var newFileName = Path.GetFileNameWithoutExtension(attachment.SupportFile.FileName) + 
                                                "_copy_" + DateTime.Now.Ticks + 
                                                Path.GetExtension(attachment.SupportFile.FileName);
                                var newFilePath = Path.Combine(_env.WebRootPath, "documents", newFileName);

                                System.IO.File.Copy(sourceFilePath, newFilePath);

                           
                                var newSupportFile = new SupportFile
                                {
                                    FileName = newFileName,
                                    FileNameOnDisk = newFileName,
                                    ContentType = attachment.SupportFile.ContentType,
                                    Size = attachment.SupportFile.Size,
                                    CreatedAt = DateTime.Now
                                };

                                _context.SupportFile.Add(newSupportFile);
                                await _context.SaveChangesAsync();

                          
                                var newAttachment = new SupplierAttachment
                                {
                                    QuotNo = attachment.QuotNo,
                                    IsRecommended = attachment.IsRecommended,
                                    Remark = "Copied from project " + sourceProject.Code,
                                    SupplierInfoId = attachment.SupplierInfoId,
                                    RFQId = newRfq.Id,
                                    SupportFileId = newSupportFile.Id
                                };

                                _context.SupplierAttachment.Add(newAttachment);
                            }
                        }
                    }

                    
                    if (sourceBom.PRId.HasValue && sourceBom.PR != null)
                    {
                        var sourcePr = sourceBom.PR;
                        string newPrDataResponse = sourcePr.DataResponse;

                        if (!string.IsNullOrEmpty(sourcePr.DataResponse))
                        {
                            try
                            {
                                var prRoot = JsonConvert.DeserializeObject<PRRoot>(sourcePr.DataResponse);
                                if (prRoot != null && prRoot.data != null)
                                {
                                   
                                    if (prRoot.data.header != null && prRoot.data.header.Count > 0)
                                    {
                                        prRoot.data.header[0].code = targetProject.Code;
                                        prRoot.data.header[0].projectName = targetProject.Name;
                                    }

                                  
                                    if (prRoot.data.bomGrid != null)
                                    {
                                        var matchingItem = prRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == sourceBom.Id);
                                        if (matchingItem != null)
                                        {
                                            matchingItem.itemLineNo = newBom.Id;
                                            matchingItem.bomNo = newBom.BomNo;
                                            
                                          
                                            prRoot.data.bomGrid = new List<PRBOMGrid> { matchingItem };
                                        }
                                    }

                                    newPrDataResponse = JsonConvert.SerializeObject(prRoot);
                                }
                            }
                            catch (Exception)
                            {
                               
                            }
                        }

                        var prFormName = "PR5";
                        var nextPrId = _context.Prs.Any() ? _context.Prs.Max(x => x.Id) + 1 : 1;

                        var newPr = new Pr
                        {
                            Prname = prFormName + nextPrId,
                            ProjectId = targetProjectId,
                            Data = sourcePr.Data,
                            DataResponse = newPrDataResponse,
                            Status = sourcePr.Status,
                            Remark = "Copied from project " + sourceProject.Code,
                            FormName = sourcePr.FormName,
                            PORemark = sourcePr.PORemark,
                            isMerge = sourcePr.isMerge,
                            totalApprover = sourcePr.totalApprover,
                            RequestorId = sourcePr.RequestorId,
                            DepartmentId = sourcePr.DepartmentId,
                            Rfqid = newBom.RFQId.HasValue ? newBom.RFQId.Value.ToString() : null,  
                            RevisionNo = 0,
                            CreatedDate = DateTime.Now,
                            LastUpdatedDate = DateTime.Now,
                            AddedBy = HttpContext.GetUserEmpId()
                        };

                        _context.Prs.Add(newPr);
                        await _context.SaveChangesAsync();

                       
                        newBom.PRId = newPr.Id;
                        _context.Boms.Update(newBom);
                        await _context.SaveChangesAsync();
                    }

             
                    foreach (var bomSupportFile in sourceBom.BomSupportFiles)
                    {
                        var sourceFilePath = Path.Combine(_env.WebRootPath, "documents", bomSupportFile.SupportFile.FileName);
                        
                        if (System.IO.File.Exists(sourceFilePath))
                        {
                            var newFileName = Path.GetFileNameWithoutExtension(bomSupportFile.SupportFile.FileName) + 
                                            "_copy_" + DateTime.Now.Ticks + 
                                            Path.GetExtension(bomSupportFile.SupportFile.FileName);
                            var newFilePath = Path.Combine(_env.WebRootPath, "documents", newFileName);

                            System.IO.File.Copy(sourceFilePath, newFilePath);

            
                            var newSupportFile = new SupportFile
                            {
                                FileName = newFileName,
                                FileNameOnDisk = newFileName,
                                ContentType = bomSupportFile.SupportFile.ContentType,
                                Size = bomSupportFile.SupportFile.Size,
                                CreatedAt = DateTime.Now
                            };

                            _context.SupportFile.Add(newSupportFile);
                            await _context.SaveChangesAsync();

                       
                            var newBomSupportFile = new BomSupportFile
                            {
                                BomId = newBom.Id,
                                SupportFileId = newSupportFile.Id,
                                Type = bomSupportFile.Type
                            };

                            _context.BomSupportFile.Add(newBomSupportFile);
                        }
                    }

                    await _context.SaveChangesAsync();
                    copiedBomIds.Add(newBom.Id);
                    copiedCount++;
                }

              
                await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Information,
                    $"Copied {copiedCount} BOM items from project {sourceProject.Code} to {targetProject.Code}",
                    tableChange: nameof(Bom),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: targetProjectId
                );

                return Json(new { 
                    success = true, 
                    message = $"Successfully copied {copiedCount} BOM items",
                    copiedBomIds 
                });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += " | Inner: " + ex.InnerException.Message;
                }
                
                return Json(new { 
                    success = false, 
                    errors = new[] { new { code = "EXCEPTION", message = errorMessage, field = "" } } 
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProjects()
        {
            try
            {
                var projects = await _context.Projects
                    .Where(x => x.Status != "Completed" && x.Status != "Archived")
                    .OrderByDescending(x => x.CreatedDate)
                    .Select(x => new
                    {
                        id = x.Id,
                        code = x.Code,
                        name = x.Name,
                        status = x.Status
                    })
                    .ToListAsync();

                return Json(projects);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBOMsByProject(int projectId)
        {
            try
            {
                var allowedStatuses = new List<string> { "New", "RFQ Sent", "RFQ Updated" };

        var boms = await _context.Boms
            .Where(x => x.ProjectId == projectId && allowedStatuses.Contains(x.Status))
            .OrderBy(x => x.BomNo)
            .Select(x => new
            {
                id = x.Id,
                bomNo = x.BomNo,
                partDesc = x.PartDesc,
                partNumber = x.PartNumber,
                status = x.Status,
                module = x.Module,
                rfqId = x.RFQId
            })
            .ToListAsync();

                return Json(boms);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
