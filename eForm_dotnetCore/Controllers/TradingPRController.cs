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
    public class TradingPRController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IEmailService _emailService;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public TradingPRController(EformSophicDemoContext context
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
            var userId = int.Parse(HttpContext.GetUserId());

            // Get BOMs that belong to PRs requested by the current user
            var query = _context.Boms
                .Include(x => x.PR)
                .Where(x => x.PR != null && x.PR.RequestorId == userId)
                .AsQueryable();

            if (parameters.Length < 0)
                parameters.Length = query.Count();

            var rfqQuery = _context.Rfqs.AsQueryable();
            var prQuery = _context.Prs.AsQueryable();

            var statusValue = parameters.Status;
            var catValue = parameters.Category;
            var searchValue = parameters.Search.value;

            // === Filters ===
            if (!string.IsNullOrEmpty(statusValue))
                query = query.Where(x => x.Status == statusValue);

            if (!string.IsNullOrEmpty(catValue))
                query = query.Where(x => x.ItemCategory == catValue);

            if (!string.IsNullOrEmpty(searchValue))
            {
                if (searchValue.StartsWith("RFQ"))
                {
                    var findRFQ = await rfqQuery.FirstOrDefaultAsync(x => x.Rfqname == searchValue);
                    if (findRFQ != null)
                        query = query.Where(x => x.RFQId == findRFQ.Id);
                }
                else if (searchValue.StartsWith("PR"))
                {
                    var findPR = await prQuery.FirstOrDefaultAsync(x => x.Prname == searchValue);
                    if (findPR != null)
                        query = query.Where(x => x.PRId == findPR.Id);
                }
                else if (searchValue.StartsWith("SAPO"))
                {
                    query = query.Where(x => x.BomSupportFiles
                        .Any(bsf => bsf.SupportFile.FileName.Contains(searchValue)));
                }
                else
                {
                    query = query.Where(x =>
                        x.PartDesc.Contains(searchValue) ||
                        x.Brand.Contains(searchValue) ||
                        x.Module.Contains(searchValue) ||
                        x.BomNo.Contains(searchValue) ||
                        x.ItemCategory.Contains(searchValue) ||
                        x.PartNumber.Contains(searchValue));
                }
            }

            var total = await query.CountAsync();

            // === Main query ===
            var bomItems = await query
                .Include(x => x.RFQ)
                .Include(x => x.Project)
                .Include(x => x.PR)
                .Include(x => x.BomSupportFiles)
                .ThenInclude(x => x.SupportFile)
                .OrderByDescending(x => x.LastUpdatedDate)
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToListAsync();

            // === Data transformation ===
            var BomList = new List<BOMViewModel>();

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
                    var currentItem = rfqRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == item.Id);

                    if (currentItem != null)
                    {
                        quotationLineNo = currentItem.quotationLineNo;
                        quotationNoLink = currentItem.quotNoLink;
                        eta = currentItem.eta;
                        quantity = currentItem.totalQuantity;

                        var recommendSupplier = rfqRoot.data.supplierList
                            .FirstOrDefault(x => x.recommended == true &&
                                                 x.supplierKey?.id == currentItem.selectedSupplier &&
                                                 x.quotNoLink == quotationNoLink);

                        supplierName = recommendSupplier?.supplierKey?.name ?? string.Empty;
                        quotName = recommendSupplier?.quotation?.FirstOrDefault()?.originalName ?? string.Empty;
                        quotUrl = recommendSupplier?.quotation?.FirstOrDefault()?.url ?? string.Empty;
                    }
                }

                var model = new BOMViewModel
                {
                    Id = item.Id,
                    BomNo = item.BomNo,
                    CanViewDrawing = CommonHelpers.isPics(item.Project),
                    ProjectId = item.ProjectId,
                    ProjectName = item.Project.Name,
                    ProjectStatus = item.Project.Status,
                    Rfq = item.RFQ,
                    Pr = item.PR,
                    Module = item.Module,
                    PartNumber = item.PartNumber,
                    Brand = item.Brand,
                    PartDesc = item.PartDesc,
                    ItemCategory = item.ItemCategory,
                    BomSupportFiles = item.BomSupportFiles,
                    UnitQty = item.UnitQty,
                    RequireQty = item.RequireQty,
                    CreatedDate = item.CreatedDate,
                    LastUpdatedDate = item.LastUpdatedDate,
                    Status = item.Status,
                    SupplierName = CommonHelpers.availableBaseStatus("RFQ Approved", item.Status) ? supplierName : "",
                    QuotName = !string.IsNullOrEmpty(supplierName) ? quotName : "",
                    QuotUrl = quotUrl,
                    RfqId = item.RFQ?.Id,
                    RfqName = item.RFQ?.Rfqname,
                    PrId = item.PR?.Id,
                    PRApprovers = _context.PRApprover.Where(d => d.PRId == item.PR.Id).ToList() ?? null,
                    totalApprover = _context.Prs.Where(d => d.Id == item.PR.Id)
                        .Select(d => d.totalApprover)
                        .FirstOrDefault() ?? 0,
                    PRName = item.PR != null ? CommonHelpers.renamePR(item.PR.Prname, item.PR.RevisionNo) : null,
                    prQuantity = CommonHelpers.availableBaseStatus("PO Approved", item.Status) ? quantity : -1,
                    prUnitPrice = CommonHelpers.availableBaseStatus("PO Approved", item.Status)
                        ? Math.Round(item.UnitPrice ?? 0, 4)
                        : -1,
                    prTotalPrice = CommonHelpers.availableBaseStatus("PO Approved", item.Status)
                        ? Math.Round(item.UnitPrice ?? 0, 4) * quantity
                        : -1,
                    ETA = item.ETA,
                    ReceivedDate = item.ReceivedDate,
                    ReceivedQuantity = item.ReceivedQuantity,
                    QuotationLine = quotationLineNo,
                    QuotationNoLink = quotationNoLink,
                    IssuedPIC = getPicsWithDept(item.IssuedPIC),
                    IssuedQuantity = item.IssuedQuantity,
                    IssuedOutDate = item.IssuedOutDate,
                    BalanceToIssueOutQty = item.BalanceToIssueOutQty,
                    BalanceToReceiveQty = item.BalanceToReceiveQty
                };

                BomList.Add(model);
            }

            var data = BomList
                .OrderByDescending(x => x.LastUpdatedDate)
                .ThenBy(x => x.RfqName)
                .ToList();

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = data
            });
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            List<string> navigation = new List<string> { "Trading PR" };
            List<string> link = new List<string> { "/TradingPR/Index/" };
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;

            var userId = int.Parse(HttpContext.GetUserId());

            var projectIds = await _context.Boms
                .Include(x => x.PR)
                .Where(x => x.PR != null && x.PR.RequestorId == userId)
                .Select(x => x.ProjectId)
                .Distinct()
                .ToListAsync();

            var bomOverView = new BOMOverviewViewModel
            {
                Employees = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue)
                    .ToList(),
                ProjectId = 0 
            };

            return View(bomOverView);
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

        protected bool isProjectCompleted(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);

            return project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
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

                return RedirectToAction("Index");
            }

            return NoContent();
        }

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
            return RedirectToAction("Index");
        }

    }
}
