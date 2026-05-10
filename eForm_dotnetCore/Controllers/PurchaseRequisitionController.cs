using ClosedXML.Excel;
using DocumentFormat.OpenXml.Wordprocessing;
using eForm_dotnetCore.Common;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Internal;
using Newtonsoft.Json;
using NPOI.HPSF;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Ocsp;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using static eForm_dotnetCore.ViewModels.RFQExportModel;

namespace eForm_dotnetCore.Controllers
{
    public class PurchaseRequisitionController : Controller
    {

        private readonly EformSophicDemoContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly IEmailService _emailService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;
        private object virtualPath;
        private readonly IFileWriter _fileWriter;

        public PurchaseRequisitionController(
            EformSophicDemoContext context,
            IAuthorizationService authorizationService,
             IEmailService emailService,
             IAuditTrailService auditTrailService,
            IWebHostEnvironment environment,
            IFileWriter fileWriter
            )
        {
            _context = context;
            _authorizationService = authorizationService;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
            _env = environment;
            _fileWriter = fileWriter;
        }

        [HttpDelete]
        public async Task<IActionResult> DeletePR(int id)
        {
            if (!_context.Prs.Any(x => x.Id == id))
                throw new NullReferenceException(nameof(id));

            var targetPR = _context.Prs.Include(x => x.Project).Where(x => x.Id == id).FirstOrDefault();

            _context.Prs.Remove(targetPR);
            _context.SaveChanges();

            await _auditTrailService.InsertLogAsync(
                       Common.LogLevel.Information, "Delete " + CommonHelpers.renamePR(targetPR.Prname, targetPR.RevisionNo),
                       fullMessage: "Delete PR with name " + CommonHelpers.renamePR(targetPR.Prname, targetPR.RevisionNo) + " from Project "+targetPR.Project.Name,
                       tableChange: nameof(Pr),
                       empId: int.Parse(HttpContext.GetUserId()),
                       ProjectId: (int)targetPR.ProjectId
                       );

            return Ok();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }
            int prId = (int)id;

            var PR = await _context.Prs
                .Include(x => x.PRApprovers)
                .ThenInclude(x => x.Approver)
                .Include(x => x.Requestor)
                .ThenInclude(x => x.EmpDepartment)
                .FirstOrDefaultAsync(x => x.Id == prId);

            var prStatus = _context.Boms
                .FirstOrDefault(x => x.PRId == prId);

            var rfqIds = PR.Rfqid.Split(";").Select(int.Parse).ToList();
            var supplierAttachments = _context.SupplierAttachment
                .Include(x => x.SupportFile)
                .Include(x => x.SupplierInfo)
                .Where(x => rfqIds.Contains(x.RFQId) && x.IsRecommended).ToList();
            //var RFQ = await _context.Rfqs.Where(x => rfqIds.Contains(x.Id)).FirstOrDefaultAsync();

            var prTemp = await _context.Forms.Where(x => x.Name == "PR5").FirstOrDefaultAsync();
            PR.Data = prTemp.Data;

            if (PR == null)
            {
                return NotFound();
            }
            var project = _context.Projects.Include(x => x.CategoryInfo).Where(d => d.Id == PR.ProjectId).FirstOrDefault();
            ViewBag.ProjectId = project.Id;
            ViewBag.isProjectCompleted = project.Status == "Completed";

            var root = JsonConvert.DeserializeObject<PRRoot>(PR.DataResponse);
            var rfq = await _context.Rfqs
                .Include(x => x.Requestor)
               .FirstOrDefaultAsync(x => x.Id == int.Parse(rfqIds[0].ToString()));

            var requesterName = rfq?.Requestor?.ShortName;

            var requester = root.data.header.Select(x => x.requester).FirstOrDefault();
            var department = PR.Requestor.EmpDepartment.Name;
            ViewBag.HODDept = department;

            if (root.data.newQuo == null) root.data.newQuo = new List<Files>();

            if (HttpContext.GetUserType() == "SuperAdmin" || HttpContext.GetUserType() == "SuperAdminAssistant")
            {
                ViewBag.IsDepartmentSame = true;
            }
            else
            {
                var checkTeoh = int.Parse(HttpContext.GetUserId()) == 443;

                if (checkTeoh)
                {
                    if(department == "OE" || department == "FAS")
                    {
                        ViewBag.IsDepartmentSame = true;
                    }
                }
                else
                {
                    ViewBag.IsDepartmentSame = HttpContext.GetUserDept() == department;
                }
            }

            root.data.auth = CommonHelpers.isPics(project) ? "1" : "";
            root.data.isRevised = prStatus.Status == "PO Revise" ? 1 : 0;
            var superAdmins = CommonExtensions.SuperAdminList();
            var requestorList = _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).ToList();
            root.data.userList = string.IsNullOrEmpty(project.PICS) ? string.Empty : getPicsWithDept(project.PICS);
            root.data.isRequestor = CommonHelpers.isPics(project) ? 1 : 0;
            root.data.ableViewDrawing = CommonHelpers.isPics(project) || HttpContext.GetUserType().Equals("Buyer", StringComparison.OrdinalIgnoreCase)
               || HttpContext.GetUserType().Equals("PM", StringComparison.OrdinalIgnoreCase)
               || HttpContext.GetUserType().Equals("HOD", StringComparison.OrdinalIgnoreCase)
               || HttpContext.GetUserType().Equals("SuperAdminAssistant", StringComparison.OrdinalIgnoreCase)
               || HttpContext.GetUserType().Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            var isRequestor =PR.RequestorId.Equals(int.Parse(HttpContext.GetUserId()))
              && !HttpContext.GetUserType().Equals("Buyer", StringComparison.InvariantCultureIgnoreCase);

            var allowSubmit = false;
            if (HttpContext.GetUserType() == "SuperAdmin" || HttpContext.GetUserType() == "SuperAdminAssistant")
            {
                allowSubmit = true;
            }
            else
            {
                allowSubmit = isRequestor || CommonHelpers.isPics(project);
            }

            if(PR.Status == "PO Revise" && (HttpContext.GetUserType() == "Buyer") || (HttpContext.GetUserType() == "Purchaser"))
            {
                allowSubmit = true;
            }

            ViewBag.allowSubmit = allowSubmit;
            var departmenList = new List<object>();
            foreach (var requestor in requestorList)
            {
                departmenList.Add(new
                {
                    username = requestor.ShortName + "(" + requestor.EmpDepartment.Name + ")",
                    department = requestor.EmpDepartment.Name
                });

            }

            root.data.departmentList = JsonConvert.SerializeObject(departmenList);

            root.data.header.FirstOrDefault().code = project.Code;
            root.data.header.FirstOrDefault().projectName = project.Name;
            root.data.header.FirstOrDefault().department = department;
            root.data.header.FirstOrDefault().poNumber = project.PoNumber;
            root.data.header.FirstOrDefault().projectName = project.Name;
            root.data.header.FirstOrDefault().projectCategory = project.CategoryInfo.CategoryShortName;
            root.data.header.FirstOrDefault().customer = project.Customer;
            root.data.header.FirstOrDefault().poQuantity = project.PoQuantity ?? 0;
            root.data.header.FirstOrDefault().budgetMaterialCost = project.BudgetMaterialCost ?? 0;
            root.data.header.FirstOrDefault().actualMaterialCost = project.ActualMaterialCost ?? 0;

            var requestorUpdated = _context.EmployeeInfos
                     .Include(x => x.EmpDepartment)
                     .Where(x => x.Id == PR.RequestorId)
                     .FirstOrDefault();
            root.data.header.FirstOrDefault().requester = PR.Requestor.ShortName + "(" + requestorUpdated.EmpDepartment.Name + ")";

            if (root.data.bomGrid.Count() > 0)
            {
                var bomGridList = root.data.bomGrid;
                //root.data.allRm = bomGridList.Where(x => x.currency.Equals("MYR", StringComparison.InvariantCultureIgnoreCase)).Count() == bomGridList.Count() ? 1 : 0;
                try
                {
                    root.data.allRm = bomGridList.Where(x => x.currency.Equals("MYR", StringComparison.InvariantCultureIgnoreCase)).Count() == bomGridList.Count() ? 1 : 0;
                }
                catch
                {
                    root.data.allRm = 0;
                }
                var ddUrl = new List<object>();
                var rfqLink = new List<object>();
                var rewriteSupplierList = new List<SupplierContainer>();
                var quoIdsValidate = new List<int>();

                for (var i = 0; i < root.data.bomGrid.Count(); i++)
                {
                    var bom = _context.Boms
                        .Include(x => x.RFQ)
                        .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                        .Where(x => x.Id == root.data.bomGrid[i].itemLineNo).FirstOrDefault();
                    if (bom != null)
                    {
                        bomGridList[i].itemLineNo = bom.Id;
                        bomGridList[i].bomNo = bom.BomNo;
                        bomGridList[i].module = bom.Module;
                        bomGridList[i].partNo = bom.PartNumber;
                        bomGridList[i].itemCategory = bom.ItemCategory + " Part";
                        bomGridList[i].brand = bom.Brand;
                        bomGridList[i].description = bom.PartDesc;
                        bomGridList[i].totalQuantity = bom.RequireQty ?? 0;

                        bomGridList[i].tax = bomGridList[i].tax == null ? 0 : bomGridList[i].tax;
                        bomGridList[i].shippingCost = bomGridList[i].shippingCost == null ? 0 : bomGridList[i].shippingCost;

                        if (bom.BomSupportFiles != null && bom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                        {
                            foreach (var bsf in bom.BomSupportFiles.Where(x => x.Type == "IC").ToList())
                            {
                                ddUrl.Add(new
                                {
                                    lineNo = bom.Id,
                                    downloadLink = generateViewURL(bsf.SupportFile),
                                    fileName = bsf.SupportFile.FileName
                                });
                            }
                        }

                        rfqLink.Add(new
                        {
                            lineNo = bom.Id,
                            rfqName = bom.RFQ.Rfqname,
                            link = Url.Action("Edit", "RFQ", new { id = bom.RFQId, bomId = bom.Id }, HttpContext.Request.Scheme)
                        });

                        var getSupplierQuos = supplierAttachments
                            .Where(x => x.QuotNo == bomGridList[i].quotNoLink).ToList();

                        getSupplierQuos.ForEach(x =>
                        {
                            if (!quoIdsValidate.Any(q => q == x.Id))
                            {
                                quoIdsValidate.Add(x.Id);
                                rewriteSupplierList.Add(new SupplierContainer
                                {
                                    supplierKey = new SupplierObject
                                    {
                                        id = x.SupplierInfo.Id.ToString(),
                                        name = x.SupplierInfo.VendorName
                                    },
                                    remark = x.Remark,
                                    vendor_remark = x.V_Remark,
                                    r_remark = x.R_Remark,
                                    quotName = x.SupportFile.FileName,
                                    quotNoLink = x.QuotNo,
                                    quoUrl = generateViewURL(x.SupportFile),
                                });
                            }
                        });
                    }
                }

                root.data.supplierContainer = rewriteSupplierList;
                root.data.ddURL = JsonConvert.SerializeObject(ddUrl);
                root.data.rfqLink = JsonConvert.SerializeObject(rfqLink);
            }

            if (root.data.pendingItems == null) root.data.pendingItems = new List<PendingList>();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            root.data.bomGrid = root.data.bomGrid
                .OrderBy(x => x.quotNoLink ?? string.Empty)
                .ThenBy(x => x.quotationLineNo)
                .ToList();

            PR.DataResponse = System.Text.Json.JsonSerializer.Serialize(root, options);
            //PR.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
            _context.Prs.Update(PR);
            _context.SaveChanges();

            ViewBag.projectCode = project.Code;
            ViewBag.poNo = project.PoNumber;
            ViewBag.projectName = project.Name;
            ViewBag.category = project.Category;
            ViewBag.poQuantity = "";
            ViewBag.customer = project.Customer;
            ViewBag.requester = requester;
            ViewBag.department = department;
            //ViewBag.buyer = buyer;
            ViewBag.BomStatus = _context.Boms.FirstOrDefault(x => x.PRId == id.Value).Status;

            var relatedBom = _context.Boms.FirstOrDefault(x => x.PRId == PR.Id);

            if (relatedBom != null)
            {
                ViewBag.isFabrication = relatedBom.ItemCategory == "Fabrication";
            }

            return View(PR);
        }

        [HttpGet]
        public IActionResult DownloadPRDrawing(int id)
        {
            var pr = _context.Prs.FirstOrDefault(x => x.Id == id);
            if (pr == null || string.IsNullOrEmpty(pr.Rfqid))
            {
                return NotFound();
            }

            // Parse multiple RFQ IDs from string like "1;2;3"
            var rfqIds = pr.Rfqid.Split(';')
                                 .Where(x => int.TryParse(x, out _))
                                 .Select(int.Parse)
                                 .ToList();

            // Get all related BOMs
            var boms = _context.Boms
                .Where(x => x.RFQId.HasValue && rfqIds.Contains(x.RFQId.Value))
                .Select(x => x.Id)
                .ToList();

            var supportFiles = new List<SupportFile>();

            foreach (var bomId in boms)
            {
                var checkFiles = _context.BomSupportFile
                    .Include(x => x.SupportFile)
                    .Where(x => x.BomId == bomId)
                    .ToList();

                if (checkFiles.Any())
                {
                    var loadSupportFile = checkFiles.Select(x => x.SupportFile).ToList();
                    supportFiles.AddRange(loadSupportFile);
                }
            }

            // Combine all into one zip
            var zipName = $"{pr.Prname}-Drawings-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.zip";
            using (var ms = new MemoryStream())
            {
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
        }


        private string getPicsWithDept(string pICS)
        {
            var pics = string.Empty;
            foreach (var pic in pICS.Split(',').Select(int.Parse))
            {
                var emp = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => x.Id == pic && x.DepartmentId.HasValue).FirstOrDefault();
                if (emp != null)
                    pics += "," + emp.ShortName + "(" + emp.EmpDepartment.Name + ")";
            }
            return pics.TrimStart(',');
        }

        private async Task<IActionResult> SavePR(Pr ori,Pr model)
        {
            var rfqIds = ori.Rfqid.Split(";").Select(int.Parse).ToList();

            if (isProjectCompleted(ori.ProjectId.Value))
                return RedirectToAction("Error404", "Home", new { msg = "The project of current PR is under the completed status.Please info the PM to extend the due date of project for further action." });

            if (model.DataResponse != null)
                ori.DataResponse = model.DataResponse;

            var prRoot = JsonConvert.DeserializeObject<PRRoot>(ori.DataResponse);

            foreach (var item in prRoot.data.bomGrid)
            {
                var bom = await _context.Boms.FirstOrDefaultAsync(x => x.Id == item.itemLineNo);
                bom.Brand = item.brand;
                bom.PartDesc = item.description;
                bom.RequireQty = item.totalQuantity;
                bom.PartNumber = item.partNo;
                bom.UnitPrice = item.unitPrice;
            }

            await _context.SaveChangesAsync();
            prRoot.data.updateDate = DateTime.Now;

            //remove item from rfq
            if (!string.IsNullOrEmpty(prRoot.data.pendingRemoveItems))
            {
                var removeItemIds = prRoot.data.pendingRemoveItems.Split(",").Select(int.Parse).ToList();
                foreach (var ri in removeItemIds)
                {
                    var bom = await _context.Boms.FirstOrDefaultAsync(x => x.Id == ri);
                    if (bom != null && bom.RFQId.HasValue)
                    {
                        var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == bom.RFQId);
                        var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                        var removeItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo.HasValue && x.itemLineNo == ri).FirstOrDefault();

                        if (removeItem != null)
                            rfqRoot.data.bomGrid.Remove(removeItem);

                        bom.RFQId = null;
                        bom.PRId = null;
                        bom.Status = "New";

                        rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);

                        await _context.SaveChangesAsync();
                    }
                }

                prRoot.data.pendingRemoveItems = string.Empty;
            }

            //update item on rfq side
            foreach (var item in prRoot.data.bomGrid)
            {
                var bom = _context.Boms.FirstOrDefault(x => x.Id == item.itemLineNo);
                if (bom != null && bom.RFQId.HasValue)
                {
                    var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == bom.RFQId);
                    var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                    var updateItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo.HasValue && x.itemLineNo == item.itemLineNo).FirstOrDefault();

                    if (updateItem != null)
                    {
                        rfqRoot.data.bomGrid.Remove(updateItem);
                        var _up = item.ToModel<BOMGrid>();
                        // old code: _up.selectedSupplier = updateItem.selectedSupplier;
                        var newSupplierId = prRoot.data.supplierContainer?.FirstOrDefault(x => x.quotNoLink == item.quotNoLink)?.supplierKey?.id;
                        if (!string.IsNullOrEmpty(newSupplierId))
                        {
                            _up.selectedSupplier = newSupplierId;
                        }
                        else
                        {
                            _up.selectedSupplier = updateItem.selectedSupplier;
                        }
                        updateItem = _up;
                        rfqRoot.data.bomGrid.Add(updateItem);
                    }

                    rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);

                    await _context.SaveChangesAsync();
                }
            }

            ori.DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot);
            _context.Prs.Update(ori);

            await _context.SaveChangesAsync();

            return Redirect("~/BOM/Index/" + ori.ProjectId);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Pr model)
        {
            try
            {
                var newForm = _context.Prs.FirstOrDefault(x => x.Id == model.Id);
                var response =await SavePR(newForm, model);
                return response;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public IActionResult isBudgetExceed(int projectId)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            if (project == null)
                return Json(false);

            var isExceed = project.BudgetMaterialCost < project.ActualMaterialCost;
            return Json(isExceed);
        }

        [HttpPost]
        public async Task<JsonResult> Submit(Pr form)
        {
            try
            {
                if (form.Id > 0)
                {
                    var newForm = _context.Prs.FirstOrDefault(x => x.Id == form.Id);
                    var rfqIds = newForm.Rfqid.Split(";").Select(int.Parse).ToList();
                    var project = _context.Projects.FirstOrDefault(x => x.Id == newForm.ProjectId);

                    //check whether is project pic

                    if (!CommonHelpers.isPics(project) && (newForm.Status == "PO Revise" && !CommonHelpers.checkIsBuyerOrPurchaser()))
                    {
                        return Json(new { result = false });
                    }
                    else if (isProjectCompleted(project.Id))
                    {
                        return Json(new { result = false, isCompleted = true});
                    }

                    //get all bom list
                    var boms = _context.Boms.Where(x => x.PRId == newForm.Id).ToList();

                    if (form.DataResponse != null)
                        newForm.DataResponse = form.DataResponse;

                    var prRoot = JsonConvert.DeserializeObject<PRRoot>(newForm.DataResponse);

                    var typeOfpurchase = prRoot.data.header.Select(x => x.typeOfPurchase).FirstOrDefault();
                    var requestor = prRoot.data.header.Select(x => x.requester).FirstOrDefault();
                    if (!string.IsNullOrEmpty(typeOfpurchase) && !string.IsNullOrEmpty(requestor))
                    {
                        foreach (var item in prRoot.data.bomGrid)
                        {
                            var bom = await _context.Boms.FirstOrDefaultAsync(x => x.Id == item.itemLineNo);
                            bom.Brand = item.brand;
                            bom.PartDesc = item.description;
                            bom.RequireQty = item.totalQuantity;
                            bom.PartNumber = item.partNo;
                            bom.UnitPrice = item.unitPrice;

                            await _context.SaveChangesAsync();
                        }
                    }

                    prRoot.data.updateDate = DateTime.Now;

                    //remove item from rfq
                    if (!string.IsNullOrEmpty(prRoot.data.pendingRemoveItems))
                    {
                        var removeItemIds = prRoot.data.pendingRemoveItems.Split(",").Select(int.Parse).ToList();
                        foreach (var ri in removeItemIds)
                        {
                            var bom = await _context.Boms.FirstOrDefaultAsync(x => x.Id == ri);
                            if (bom != null && bom.RFQId.HasValue)
                            {
                                var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == bom.RFQId);
                                var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                                var removeItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo.HasValue && x.itemLineNo == ri).FirstOrDefault();

                                if (removeItem != null)
                                    rfqRoot.data.bomGrid.Remove(removeItem);

                                bom.RFQId = null;
                                bom.PRId = null;
                                bom.Status = "New";

                                rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);

                                await _context.SaveChangesAsync();
                            }
                        }

                        prRoot.data.pendingRemoveItems = string.Empty;
                    }

                    //update item on rfq side

                    foreach (var item in prRoot.data.bomGrid)
                    {
                        var bom = _context.Boms.FirstOrDefault(x => x.Id == item.itemLineNo);
                        if (bom != null && bom.RFQId.HasValue)
                        {
                            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == bom.RFQId);
                            var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                            var updateItem = rfqRoot.data.bomGrid.Where(x => x.itemLineNo.HasValue && x.itemLineNo == item.itemLineNo).FirstOrDefault();

                            if (updateItem != null)
                            {
                                rfqRoot.data.bomGrid.Remove(updateItem);
                                var _up = item.ToModel<BOMGrid>();
                                // old code: _up.selectedSupplier = updateItem.selectedSupplier;
                                var newSupplierId = prRoot.data.supplierContainer?.FirstOrDefault(x => x.quotNoLink == item.quotNoLink)?.supplierKey?.id;
                                if (!string.IsNullOrEmpty(newSupplierId))
                                {
                                    _up.selectedSupplier = newSupplierId;
                                }
                                else
                                {
                                    _up.selectedSupplier = updateItem.selectedSupplier;
                                }
                                rfqRoot.data.bomGrid.Add(_up);
                            }

                            rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);

                            await _context.SaveChangesAsync();
                        }
                    }

                    //estimate price
                    var approverCount = 1;
                    var approvers = new List<PRApprover>();
                    
                    
                    double totalPriceRM = 0;
                    double extractedNetPrice = prRoot.data.netPrice ?? 0;
                    string quoteCurrency = "MYR";

                    if (prRoot.data.bomGrid != null && prRoot.data.bomGrid.Count > 0)
                    {
                        quoteCurrency = prRoot.data.bomGrid[0].currency ?? "MYR";
                    }

                    if (!quoteCurrency.Equals("RM", StringComparison.OrdinalIgnoreCase) && 
                        !quoteCurrency.Equals("MYR", StringComparison.OrdinalIgnoreCase))
                    {
                        var conversion = prRoot.data.currencyGrid?.FirstOrDefault(c => 
                            c.cr_currency != null && c.cr_currency.Equals(quoteCurrency, StringComparison.OrdinalIgnoreCase));
                        
                        if (conversion != null && conversion.rate.HasValue && conversion.rate > 0)
                        {
                            extractedNetPrice *= conversion.rate.Value;
                        }
                    }

                    totalPriceRM = extractedNetPrice;
                    double totalPrice = totalPriceRM;

                    /*
                    // Commented out old auto-approval logic as per user request to not automatic approve
                    if (newForm.PriceBeforeRevise.HasValue && newForm.Status == "PO Revise")
                    {
                        var oldApprover = CommonHelpers.LevelApprover((double)newForm.PriceBeforeRevise.Value);                        var newApprover = CommonHelpers.LevelApprover(totalPrice);

                        var approverIndex = 0;
                        foreach (var oa in oldApprover)
                        {
                            var approver = _context.EmployeeInfos.FirstOrDefault(x => x.UserType == oa && x.DepartmentId == newForm.DepartmentId);
                            approvers.Add(new PRApprover
                            {
                                Approved = true,
                                ApproverId = approver.Id,
                                Type = oa,
                                LastUpdated = DateTime.Now,
                                DepartmentId = newForm.DepartmentId
                            });
                            approverIndex += 1;
                        }

                        if (oldApprover.Count != newApprover.Count && newApprover.Count > oldApprover.Count)
                        {
                            for(var i = approverIndex; i < newApprover.Count; i++)
                            {
                                approvers.Add(new PRApprover
                                {
                                    Approved = false,
                                    Type = newApprover[i],
                                    LastUpdated = DateTime.Now,
                                    DepartmentId = newForm.DepartmentId
                                });
                            }
                            approverCount = newApprover.Count;
                            boms.ForEach(x => x.Status = "PR Sent");

                        }
                        else
                        {
                            approverCount = newForm.totalApprover.Value;
                            boms.ForEach(x => x.Status = "PR Approved");
                        }
                    }
                    */

                    approverCount = CommonHelpers.LevelApprover(totalPrice).Count;
                    approvers.Add(new PRApprover
                    {
                        Approved = false,
                        Type = "HOD",
                        LastUpdated = DateTime.Now,
                        DepartmentId = int.Parse(HttpContext.GetUserDeptId())
                    });

                    if (approverCount > 1)
                        approvers.Add(new PRApprover
                        {
                            Approved = false,
                            Type = "GFC",
                            LastUpdated = DateTime.Now
                        });

                    if (approverCount > 2)
                        approvers.Add(new PRApprover
                        {
                            Approved = false,
                            Type = "CEO",
                            LastUpdated = DateTime.Now
                        });

                    boms.ForEach(x =>
                    {
                        x.Status = "PR Sent";
                        x.LastUpdatedDate = DateTime.Now;
                    });

                    _context.Boms.UpdateRange(boms);
                    newForm.RevisionNo = newForm.Status == "PO Revise" ? newForm.RevisionNo + 1 : newForm.RevisionNo;
                    newForm.DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot);
                    newForm.AddedBy = "sys";
                    newForm.totalApprover = approverCount;
                    newForm.Status = boms[0].Status;

                    //check & add approvers
                    var existsApprovers = _context.PRApprover.Where(x => x.PRId == newForm.Id).ToList();

                    if(existsApprovers.Count> 0)
                    {
                        _context.PRApprover.RemoveRange(existsApprovers);
                        await _context.SaveChangesAsync();
                    }

                    newForm.PRApprovers = approvers;

                    _context.Prs.Update(newForm);

                    if (await _context.SaveChangesAsync() > 0)
                    {
                        var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
                        var title = "New PR with name " + CommonHelpers.renamePR(newForm.Prname, newForm.RevisionNo) + " is been sent for approval";

                        var deptHod = _context.EmployeeInfos.Where(x => x.Email == "jim.chew@sophicautomation.com"
                        && !string.IsNullOrEmpty(x.Email)).ToList();
                        var hodEmails = deptHod.Select(x => x.Email).ToList();
                        var approvalMailTemp = CommonHelpers.ApprovalRequireMailTemp("HOD", Url.Action("Edit", new { id = newForm.Id }), newForm.Prname);
                        await _emailService.Send(hodEmails, newForm.Prname + " is pending for approval", approvalMailTemp);

                        pics.AddRange(deptHod.Select(x => x.Id).ToList());
                        await _auditTrailService.InsertLogAsync(
                           Common.LogLevel.Information, title,
                           pics:pics,
                           tableChange: nameof(Pr),
                           empId: int.Parse(HttpContext.GetUserId()),
                           ProjectId: (int)newForm.ProjectId
                           );
                    }

                    return Json(new { id = boms.FirstOrDefault().ProjectId });
                }

                return Json(new { result = false });
            }
            catch (Exception ex)
            {
                return Json(new { result = false });
            }
        }

        [HttpPost]
        [Authorize(Policy = "ManagementLevelOnly")]
        public async Task<IActionResult> approveOrReject(PRApprovedViewModel model)
        {
            var pr = _context.Prs.FirstOrDefault(x => x.Id == model.prId);
            var boms = _context.Boms.Where(x => x.PRId == model.prId).ToList();
            var project = _context.Projects.Where(x => x.Id == boms.First().ProjectId).FirstOrDefault();
            var action = model.action == 1 ? "Approve" : "Reject";

            if (isProjectCompleted(boms.FirstOrDefault().ProjectId))
                return Json(new { result = false, msg = "The changes is unsuccessful as project in completed status." });


            if (_context.PRApprover.Any(x => x.PRId == model.prId && x.Type == model.approver) && model.action == 1)
            {
                var approver = _context.PRApprover.FirstOrDefault(x => x.PRId == model.prId && x.Type == model.approver);
                approver.Approved = true;
                approver.ApproverId = int.Parse(HttpContext.GetUserId());
                approver.LastUpdated = DateTime.Now;

                _context.PRApprover.Update(approver);
            }
            else if (model.action == 0)
            {
                if (string.IsNullOrEmpty(model.Remark))
                {
                    return Json(new { result = false, msg = "Remark is required!." });
                }

                var approvers = _context.PRApprover.Where(x => x.PRId == model.prId).ToList();
                //approvers.ForEach(a => )
                approvers.ForEach(a =>
                {
                    a.ApproverId = null;
                    a.Approved = false;
                });
                boms.ForEach(x => x.Status = "PR Reject");
                boms.ForEach(x => x.LastUpdatedDate = DateTime.Now);
                _context.Boms.UpdateRange(boms);
                _context.PRApprover.UpdateRange(approvers);
            }

            var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
            if(!string.IsNullOrEmpty(model.Remark))
                prRoot.data.rejectCommentBox += model.approver + action.ToLower() + " the form and comment at" + DateTime.Now.ToString("g") + " because " + model.Remark + "\n";

            pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot);
            _context.Prs.Update(pr);

            _context.SaveChanges();

            var (msg, html) = CommonHelpers.MailTemplateGenerator("PR", model.action == 0 ? BOMStatus.Rejected : BOMStatus.Approved, Url.Action("Edit", new { id = pr.Id }));
            var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
            var emailList = pics.Count() == 0
                ? null
                : _context.EmployeeInfos
                .Where(x => !string.IsNullOrEmpty(x.Email)
                && (pics.Contains(x.Id)
                || model.action == 1 ? x.UserType == "Purchaser" : x.UserType == "Requestor"))
                .Select(x => x.Email)
                .ToList();
            var title = $@"PR with name {CommonHelpers.renamePR(pr.Prname, pr.RevisionNo)} is been {action} by {model.approver}";

            await _emailService.Send(emailList, title, html);

            var pics2 = new List<int> { pr.RequestorId };

            await _auditTrailService.InsertLogAsync(
                Common.LogLevel.Information, title,
                pics:pics2,
                fullMessage: msg,
                tableChange: nameof(Pr),
                empId: int.Parse(HttpContext.GetUserId()),
                ProjectId: (int)pr.ProjectId
                );

            var totalRequireApprovers = _context.PRApprover.Where(x => x.PRId == model.prId).ToList();

            if (totalRequireApprovers.Count(x => x.Approved) == pr.totalApprover)
            {
                boms.ForEach(x => x.Status = "PR Approved");
                pr.PRApproveDate = DateTime.Now;
                boms.ForEach(x => x.LastUpdatedDate = DateTime.Now);
                _context.Prs.Update(pr);
                _context.Boms.UpdateRange(boms);

                _context.SaveChanges();
            }
            else if(totalRequireApprovers.Count(x => x.Approved) < pr.totalApprover)
            {
                var nextApprovers = totalRequireApprovers.Where(x => !x.Approved).FirstOrDefault();

                if(nextApprovers != null)
                {
                    var approvers = _context.EmployeeInfos.Where(x => x.UserType == nextApprovers.Type
                    && x.IsNotificationOn && !string.IsNullOrEmpty(x.Email)).ToList();
                    var additionalEmails = approvers.Select(x => x.Email).Distinct().ToList();
                    var approvalMailTemp = CommonHelpers.ApprovalRequireMailTemp(nextApprovers.Type, Url.Action("Edit", new { id = pr.Id }), pr.Prname);
                    var pendingTitle = pr.Prname + " is pending for approval";
                    await _emailService.Send(additionalEmails, pendingTitle, approvalMailTemp);

                    await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Information, pendingTitle,
                    pics: approvers.Select(x => x.Id).ToList(),
                    tableChange: nameof(Pr),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: (int)pr.ProjectId
                    );
                }
            }

            return RedirectToAction("Index", "BOM", new { id = pr.ProjectId });
        }

        [HttpPost]
        public async Task<IActionResult> addPendingItems(Pr model)
        {
            var checkPR = _context.Prs.FirstOrDefault(x => x.Id == model.Id);
            var project = _context.Projects.FirstOrDefault(x => x.Id == checkPR.ProjectId);
            await SavePR(checkPR,model);

            if(checkPR != null)
            {
                if (!string.IsNullOrEmpty(model.DataResponse))
                {
                    var firstRfqId = checkPR.Rfqid.Split(';')[0];
                    var deserializeData = JsonConvert.DeserializeObject<PRRoot>(model.DataResponse);
                    var checkCategory = deserializeData.data.bomGrid[0].itemCategory.Split("Part")[0];
                    var checkRFQ = _context.Rfqs.FirstOrDefault(x => x.Id == int.Parse(firstRfqId));
                    var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(checkRFQ.DataResponse);
                    var pendingToAdded = new List<BOMGrid>();

                    //add new quotation
                    if (!string.IsNullOrEmpty(deserializeData.data.newQuotationNoLink) && deserializeData.data.newQuo != null && deserializeData.data.newQuo.Count > 0)
                    {
                        var base64String = deserializeData.data.newQuo[0].url.Split(",");
                        var uploadSAFile = Convert.FromBase64String(base64String[1]);
                        var uploadSAFilePath = Path.Combine(_env.WebRootPath, "documents", deserializeData.data.newQuo[0].originalName);
                        var origFileName = deserializeData.data.newQuo[0].originalName.Split(".");
                        var _fileName = origFileName.Length > 0 ? origFileName[0] : "quote";
                        for (int i = 1; ; i++)
                        {
                            if (System.IO.File.Exists(uploadSAFilePath))
                            {
                                _fileName = origFileName[0] + "_" + i;
                                uploadSAFilePath = Path.Combine(_env.WebRootPath, "documents", _fileName + "." + origFileName[1]);
                            }
                            else
                            {
                                break;
                            }
                        }

                        System.IO.File.WriteAllBytes(uploadSAFilePath, uploadSAFile);

                        var addSupportFile = new SupportFile
                        {
                            ContentType = "pdf",
                            CreatedAt = DateTime.Now,
                            FileName = _fileName + "." + origFileName[1],
                            FileNameOnDisk = _fileName + "." + origFileName[1],
                            Size = new FileInfo(uploadSAFilePath).Length.ToString()
                        };

                        _context.SupportFile.Add(addSupportFile);
                        _context.SaveChanges();

                        var newSA = new SupplierAttachment
                        {
                            IsRecommended = true,
                            Remark = "Added during PO Revise",
                            RFQId = int.Parse(firstRfqId),
                            SupportFileId = addSupportFile.Id,
                            QuotNo = deserializeData.data.newQuotationNoLink,
                            SupplierInfoId = Guid.Parse(deserializeData.data.supplierContainer[0].supplierKey.id)
                        };

                        _context.SupplierAttachment.Add(newSA);
                        _context.SaveChanges();

                        var supplierContainer = deserializeData.data.supplierContainer?.FirstOrDefault();
                        if (supplierContainer != null && supplierContainer.supplierKey != null)
                        {
                            rfqRoot.data.supplierList ??= new List<SupplierList>();
                            rfqRoot.data.supplierList.Add(new SupplierList
                            {
                                supplierKey = new SupplierObject
                                {
                                    id = supplierContainer.supplierKey.id,
                                    name = supplierContainer.supplierKey.name ?? ""
                                },
                                recommended = true,
                                quotNoLink = deserializeData.data.newQuotationNoLink,
                                r_remark = "Added during PO Revise",
                                quotation = new List<Files>
                                {
                                    new Files
                                    {
                                        name = deserializeData.data.newQuo[0].name,
                                        originalName = deserializeData.data.newQuo[0].originalName,
                                        url = deserializeData.data.newQuo[0].url,
                                        size = deserializeData.data.newQuo[0].size,
                                        type = deserializeData.data.newQuo[0].type
                                    }
                                }
                            });
                        }

                        deserializeData.data.newQuotationNoLink = string.Empty;
                        deserializeData.data.newQuo = new List<Files>();
                    }

                    //update rfq
                    if (checkRFQ == null) throw new Exception("PRController: RFQ NOT Found!");

                    //add pending items
                    if (deserializeData.data.pendingItems.Count > 0)
                    {
                        foreach (var pendingItem in deserializeData.data.pendingItems)
                        {
                            var newBom = new Bom
                            {
                                BomNo = project.Code + "-" + CommonHelpers.generateUID(),
                                Module = pendingItem.module,
                                PartDesc = pendingItem.description,
                                PartNumber = pendingItem.partNo,
                                Brand = pendingItem.brand,
                                UnitQty = pendingItem.totalQuantity,
                                CreatedDate = DateTime.Now,
                                LastUpdatedDate = DateTime.Now,
                                AddedBy = HttpContext.GetUserEmpId(),
                                RequireQty = pendingItem.totalQuantity,
                                Status = "PO Revise",
                                RFQId = checkRFQ.Id,
                                PRId = checkPR.Id,
                                ProjectId = project.Id,
                                ItemCategory = checkCategory.Trim()
                            };

                            _context.Boms.Add(newBom);
                            _context.SaveChanges();

                            if (checkCategory.StartsWith("Fab") && pendingItem.fabPartNo.Any())
                            {

                            }

                            //add to pending add list
                            var newRFQItem = pendingItem.ToModel<BOMGrid>();
                            newRFQItem.itemLineNo = newBom.Id;
                            newRFQItem.module = newBom.Module;
                            newRFQItem.partNo = newBom.PartNumber;
                            newRFQItem.description = newBom.PartDesc;
                            newRFQItem.selectedSupplier = deserializeData.data.supplierContainer[0].supplierKey.id;
                            pendingToAdded.Add(newRFQItem);
                        }
                    }

                    rfqRoot.data.bomGrid.AddRange(pendingToAdded);

                    //update pr
                    deserializeData.data.bomGrid.AddRange(pendingToAdded.ToListModel<PRBOMGrid>());
                    deserializeData.data.pendingItems = new List<PendingList>();

                    checkRFQ.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
                    checkPR.DataResponse = System.Text.Json.JsonSerializer.Serialize(deserializeData);
                    _context.SaveChanges();
                }
            }

            return Ok();
        }

        [AllowAnonymous]
        public async Task<IActionResult> ExportToPDF(int id)
        {
            var url = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("View", new { id = id })}";

            var pr = await _context.Prs.FirstOrDefaultAsync(x => x.Id == id);

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true }))
            {
                var page = await browser.NewPageAsync();
                var response = await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);
                return File(await page.PdfDataAsync(new PdfOptions
                {
                    Format = new PaperFormat(17m, 15.5m),
                    Scale = (decimal)0.75,
                    Landscape = true
                }), "application/pdf", pr.FormName + ".pdf");
            }
        }

        [AllowAnonymous]
        public IActionResult View(int id)
        {
            var pr = _context.Prs.FirstOrDefault(x => x.Id == id);
            
            if (pr != null && !string.IsNullOrEmpty(pr.DataResponse))
            {
                var root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                if (root?.data != null)
                {
                    root.data.isFabrication = root.data.isFabrication ?? 0;
                }
                
                if (root.data.bomGrid != null && root.data.bomGrid.Count > 0)
                {
                    Console.WriteLine($"[DEBUG VIEW] Recalculating totalPrice for {root.data.bomGrid.Count} BOM items");
                    foreach (var item in root.data.bomGrid)
                    {
                        item.tax = item.tax ?? 0;
                        item.shippingCost = item.shippingCost ?? 0;
                        item.bankFee = item.bankFee ?? 0;
                        
                        double afterDiscount = item.totalQuantity * (item.unitPrice ?? 0) - (item.discount ?? 0);
                        double oldTotalPrice = item.totalPrice ?? 0;
                        item.totalPrice = afterDiscount + item.tax.Value + item.shippingCost.Value + item.bankFee.Value;
                        
                        Console.WriteLine($"[DEBUG VIEW] Item: Qty={item.totalQuantity}, UnitPrice={item.unitPrice}, Discount={item.discount}, Tax={item.tax}, Shipping={item.shippingCost}, BankFee={item.bankFee}");
                        Console.WriteLine($"[DEBUG VIEW] AfterDiscount={afterDiscount}, OldTotalPrice={oldTotalPrice}, NewTotalPrice={item.totalPrice}");
                    }
                }
                
                pr.DataResponse = JsonConvert.SerializeObject(root);
            }

            return View(pr);
        }

        protected bool isProjectCompleted(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);

            return project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult ExportToExcel(int id)
        {
            var pr = _context.Prs
                .Include(x => x.PRApprovers)
                .ThenInclude(x => x.Approver)
                .FirstOrDefault(x => x.Id == id);

            if (pr.DataResponse != null)
            {
                var root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                var currentRow = 1;

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("PR");

                    //rfq info
                    worksheet.Cell(currentRow, 1).Value = "PR Number";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 2).Value = CommonHelpers.renamePR(pr.Prname, pr.RevisionNo);

                    currentRow += 2;

                    //basic info
                    var info = typeof(RFQBasicInfo).GetProperties();
                    var infoData = root.data;
                    for (int i = 1; i <= info.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = CommonHelpers.ConvertColumnName(info[i - 1].Name.FirstCharToUpper().ConvertEnum());
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (infoData.GetType().GetProperty(info[i - 1].Name) == null)
                        {
                            worksheet.Cell(currentRow + 1, i).Value = info[i - 1].GetValue(new RFQBasicInfo());
                        }
                        else
                        {
                            worksheet.Cell(currentRow + 1, i).Value = infoData.GetType().GetProperty(info[i - 1].Name).GetValue(infoData);
                        }
                    }

                    currentRow += 3;

                    //project
                    var header = typeof(PRGridHeader).GetProperties();
                    var headerData = root.data.header.FirstOrDefault();
                    for (int i = 1; i <= header.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = CommonHelpers.ConvertColumnName(header[i - 1].Name.FirstCharToUpper().ConvertEnum());
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (headerData != null)
                        {
                            worksheet.Cell(currentRow + 1, i).Value = headerData.GetType().GetProperty(header[i - 1].Name).GetValue(headerData, null);
                        }
                    }

                    currentRow += 3;
                    //supplierName
                    var supplier = typeof(SupplierContainer).GetProperties();
                    var supplierData = root.data.supplierContainer;
                    for (int i = 1; i <= supplier.Count(); i++)
                    {
                        var columnName = supplier[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Value = supplier[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    }

                    foreach (var s in supplierData)
                    {
                        currentRow += 1;
                        worksheet.Cell(currentRow, 1).Value = s.supplierKey != null ? s.supplierKey.name : string.Empty;
                        worksheet.Cell(currentRow, 2).Value = s.quotNoLink;
                        worksheet.Cell(currentRow, 3).Value = s.remark;
                        worksheet.Cell(currentRow, 4).Value = s.quoUrl;
                        worksheet.Cell(currentRow, 5).Value = s.quotName;
                    }

                    //Bom 
                    currentRow += 2;
                    var bom = typeof(PRBOMGrid).GetProperties();
                    var bomData = root.data.bomGrid.ToList();
                    int colIndex = 1;
                    for (int i = 1; i <= bom.Count(); i++)
                    {
                        var columnName = bom[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        columnName = columnName == "Total Price" ? "Total Price(MYR)" : columnName;
                        columnName = columnName == "Total Price U S D" ? "Total Price(USD)" : columnName;
                        if (columnName != "Part No" || (root.data.isFabrication ?? 0) == 0)
                        {
                            worksheet.Cell(currentRow, colIndex).Value = columnName;
                            worksheet.Cell(currentRow, colIndex).Style.Font.Bold = true;
                            worksheet.Cell(currentRow, colIndex).Style.Fill.BackgroundColor = XLColor.LightCyan;

                            if (bomData.Count > 0)
                            {
                                foreach (var item in bomData)
                                {
                                    worksheet.Cell(currentRow + 1, colIndex).Value = item.GetType().GetProperty(bom[i - 1].Name).GetValue(item, null);
                                    currentRow++;
                                }
                                currentRow -= bomData.Count();
                            }
                            colIndex++;
                        }
                    }

                    currentRow += bomData.Count() + 1;
                    colIndex -= 4;
                    worksheet.Cell(currentRow, colIndex).Value = "Net Price(MYR):";
                    worksheet.Cell(currentRow, colIndex+1).Value = root.data.netPrice;
                    worksheet.Cell(currentRow, colIndex+1).Style.Font.Bold = true;
                    if (root.data.netPriceUsd.HasValue && root.data.netPriceUsd > 0)
                    {
                        currentRow += 1;
                        worksheet.Cell(currentRow, colIndex).Value = "Net Price(USD):";
                        worksheet.Cell(currentRow, colIndex + 1).Value = root.data.netPriceUsd;
                        worksheet.Cell(currentRow, colIndex + 1).Style.Font.Bold = true;
                    }

                    currentRow += 2;
                    worksheet.Cell(currentRow, 1).Value = "Approver Status";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    currentRow++;
                    foreach (var approver in pr.PRApprovers)
                    {
                        worksheet.Cell(currentRow, 1).Value = approver.Type;
                        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 2).Value = approver.Approved ? "Pass":"Fail";
                        worksheet.Cell(currentRow, 3).Value = approver.Approved ? approver.Approver.FullName : "";
                        worksheet.Cell(currentRow, 4).Value = approver.Approved ? approver.LastUpdated:"";
                        currentRow++;
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
                            CommonHelpers.renamePR(pr.Prname, pr.RevisionNo) + ".xlsx");
                    }
                }
            }

            return NoContent();
        }

        public IActionResult ExportToExcel2026(int id)
        {
            var pr = _context.Prs
                .Include(x => x.PRApprovers)
                .ThenInclude(x => x.Approver)
                .FirstOrDefault(x => x.Id == id);

            if (pr.DataResponse != null)
            {
                var root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                var currentRow = 1;

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("PR");
                    worksheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
                    worksheet.PageSetup.FitToPages(1, 0);

                    worksheet.Range(currentRow, 1, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 1).Value = "SOPHIC AUTOMATION SDN BHD";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Font.Underline = XLFontUnderlineValues.Single;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    
                    currentRow += 1;
                    
                    var infoData = root.data;
                    var rfqBasicInfo = new RFQBasicInfo();
                    
                    worksheet.Range(currentRow, 1, currentRow, 4).Merge();
                    worksheet.Cell(currentRow, 1).Value = "Document No : " + (infoData.GetType().GetProperty("DocumentNo")?.GetValue(infoData) ?? "IMS-F-18");
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    
                    worksheet.Range(currentRow, 5, currentRow, 8).Merge();
                    worksheet.Cell(currentRow, 5).Value = "Effective Date : " + (infoData.GetType().GetProperty("EffectiveDate")?.GetValue(infoData) ?? "7/11/2022");
                    worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                    
                    worksheet.Range(currentRow, 9, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 9).Value = "Revision No : " + (infoData.GetType().GetProperty("RevisionNo")?.GetValue(infoData) ?? "7");
                    worksheet.Cell(currentRow, 9).Style.Font.Bold = true;

                    worksheet.Range(currentRow, 1, currentRow, 12).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    
                    currentRow += 2;

                    var headerData = root.data.header.FirstOrDefault();
                    var supplierData = root.data.supplierContainer?.FirstOrDefault();

                    int prDetailsStartRow = currentRow;

                    worksheet.Cell(currentRow, 1).Value = "PR Number";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 2, currentRow, 4).Merge();
                    worksheet.Cell(currentRow, 2).Value = CommonHelpers.renamePR(pr.Prname, pr.RevisionNo);
                    worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Cell(currentRow, 5).Value = "Request By";
                    worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 5).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 6, currentRow, 8).Merge();
                    worksheet.Cell(currentRow, 6).Value = headerData?.requester ?? "";
                    worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    
                    worksheet.Cell(currentRow, 9).Value = "Create Date";
                    worksheet.Cell(currentRow, 9).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 9).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 10, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 10).Value = root.data.createDate?.ToString("g") ?? "";
                    worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    
                    currentRow += 2;

                    worksheet.Cell(currentRow, 1).Value = "CUSTOMER PO";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 2, currentRow, 4).Merge();
                    worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "@";
                    worksheet.Cell(currentRow, 2).Value = headerData?.poNumber ?? "";
                    worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Cell(currentRow, 5).Value = "Customer";
                    worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 5).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 6, currentRow, 8).Merge();
                    worksheet.Cell(currentRow, 6).Value = headerData?.customer ?? "";
                    worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Cell(currentRow, 9).Value = "Project Code";
                    worksheet.Cell(currentRow, 9).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 9).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 10, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 10).Value = headerData?.code ?? "";
                    worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    currentRow += 1;

                    worksheet.Cell(currentRow, 1).Value = "Project Name";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 2, currentRow, 8).Merge(); // Spans middle section
                    worksheet.Cell(currentRow, 2).Style.Alignment.WrapText = true;
                    worksheet.Cell(currentRow, 2).Value = headerData?.projectName ?? "";
                    worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Cell(currentRow, 9).Value = "Type Of Purchase";
                    worksheet.Cell(currentRow, 9).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 9).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    
                    worksheet.Range(currentRow, 10, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 10).Value = headerData?.typeOfPurchase ?? "";
                    worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    currentRow += 2;
                    
                    worksheet.Cell(currentRow, 1).Value = "Supplier Key";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 2, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 2).Value = supplierData?.supplierKey?.name ?? "";
                    worksheet.Cell(currentRow, 2).Style.Alignment.WrapText = true;
                    worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    
                    currentRow += 1;
                    
                    worksheet.Cell(currentRow, 1).Value = "Quot No Link";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 2, currentRow, 4).Merge();
                    worksheet.Cell(currentRow, 2).Value = supplierData?.quotNoLink ?? "";
                    worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Cell(currentRow, 5).Value = "Quo Url";
                    worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 5).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    worksheet.Range(currentRow, 6, currentRow, 12).Merge();
                    worksheet.Cell(currentRow, 6).Value = supplierData?.quoUrl ?? "";
                    worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    worksheet.Range(prDetailsStartRow, 1, currentRow, 12).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    currentRow += 2;

                    var headers = new string[] { "EBOM ID", "Quot No", "Line No", "Description", "Part number", "Leadtime", "Qty", "Currency", "Unit price", "Tax %", "Discount", "Amount" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var actualCol = i + 1; 
                        worksheet.Cell(currentRow, actualCol).Value = headers[i];
                        worksheet.Cell(currentRow, actualCol).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, actualCol).Style.Fill.BackgroundColor = XLColor.LightCyan;
                        worksheet.Cell(currentRow, actualCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Cell(currentRow, actualCol).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    }

                    currentRow += 1;
                    
                    var bomData = root.data.bomGrid.ToList();
                    
                    bool isFirstRow = true;
                    foreach (var item in bomData)
                    {
                        string displayBomNo = item.bomNo ?? "";
                        if (displayBomNo.Contains("-"))
                        {
                            displayBomNo = displayBomNo.Substring(0, displayBomNo.LastIndexOf("-"));
                        }
                        worksheet.Cell(currentRow, 1).Value = displayBomNo;
                        worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 2).Value = item.quotNoLink; 
                        worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 3).Value = item.quotationLineNo; 
                        worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 4).Value = item.description;
                        worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        
                        worksheet.Cell(currentRow, 5).Value = item.partNo;
                        worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 6).Value = item.leadTime;
                        worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 7).Value = item.totalQuantity;
                        worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 8).Value = item.currency;
                        worksheet.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        
                        worksheet.Cell(currentRow, 9).Value = item.unitPrice;
                        worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0.00";
                        
                        string rowTaxPerc = root.data.taxSelect ?? "0";
                        if (item.tax == null || item.tax == 0) { 
                            worksheet.Cell(currentRow, 10).Value = (rowTaxPerc == "0" ? "" : rowTaxPerc); 
                        } else { 
                            worksheet.Cell(currentRow, 10).Value = item.tax; 
                        }
                        worksheet.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        
                        double rowGlobalDiscount = root.data.discountTotal ?? 0;
                        if (item.discount == null || item.discount == 0) { 
                            worksheet.Cell(currentRow, 11).Value = (isFirstRow && rowGlobalDiscount > 0) ? rowGlobalDiscount : ""; 
                            if (isFirstRow && rowGlobalDiscount > 0) worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "#,##0.00";
                        } else { 
                            worksheet.Cell(currentRow, 11).Value = item.discount; 
                            worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "#,##0.00"; 
                        }
                        worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        
                        worksheet.Cell(currentRow, 12).Value = item.totalPrice;
                        worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                        
                        worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                        worksheet.Range(currentRow, 1, currentRow, 12).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                        
                        currentRow++;
                        isFirstRow = false;
                    }

                    int emptyRows = Math.Max(0, 8 - bomData.Count);
                    for (int i = 0; i < emptyRows; i++)
                    {
                         worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                         worksheet.Range(currentRow, 1, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                         currentRow++;
                    }

                    double totalDiscount = root.data.discountTotal ?? 0;
                    double totalShipping = root.data.shippingFee ?? 0;
                    double totalBankFee = root.data.bankCharges ?? 0;
                    string taxPerc = root.data.taxSelect ?? "0";

                    worksheet.Cell(currentRow, 11).Value = "Discount";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = totalDiscount;
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    currentRow++;
                    
                    worksheet.Cell(currentRow, 11).Value = "Tax";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = taxPerc + (!taxPerc.Contains("%") ? "%" : "");
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    currentRow++;
                    
                    worksheet.Cell(currentRow, 11).Value = "SHIPPING FEE";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = totalShipping;
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    currentRow++;
                    
                    worksheet.Cell(currentRow, 11).Value = "BANK CHARGES";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = totalBankFee;
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    currentRow++;

                    double calculatedNetTotal = root.data.netPrice ?? 0;
                    string currency = bomData.FirstOrDefault()?.currency ?? "MYR";
                    
                    double netTotalMYR = 0;
                    double netTotalForeign = 0;
                    
                    if (currency.Equals("MYR", StringComparison.OrdinalIgnoreCase) || currency.Equals("RM", StringComparison.OrdinalIgnoreCase))
                    {
                        netTotalMYR = calculatedNetTotal;
                    }
                    else
                    {
                        netTotalForeign = calculatedNetTotal;
                        var conversion = root.data.currencyGrid?.FirstOrDefault(c => c.cr_currency != null && c.cr_currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
                        if (conversion != null && conversion.rate.HasValue && conversion.rate > 0)
                        {
                            netTotalMYR = calculatedNetTotal * conversion.rate.Value;
                        }
                    }

                    worksheet.Cell(currentRow, 11).Value = "NET Total:   $";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = netTotalForeign;
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    currentRow++;
                    
                    worksheet.Cell(currentRow, 11).Value = "NET Total (MYR)";
                    worksheet.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Value = netTotalMYR; 
                    worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Range(currentRow, 11, currentRow, 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    
                    currentRow += 2;
                    worksheet.Cell(currentRow, 2).Value = "Approver Status";
                    worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
                    
                    currentRow++;
                    foreach (var approver in pr.PRApprovers)
                    {
                        var approverLabel = approver.Type == "GFC" ? "CFO" : approver.Type;
                        worksheet.Cell(currentRow, 2).Value = approverLabel;
                        worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        worksheet.Cell(currentRow, 4).Value = approver.Approved ? "Pass":"Fail";
                        worksheet.Cell(currentRow, 6).Value = approver.Approver?.FullName ?? "-";
                        if (approver.Approved) worksheet.Cell(currentRow, 8).Value = approver.LastUpdated;
                        currentRow++;
                    }

                    using (var stream = new MemoryStream())
                    {
                        worksheet.Column(1).Width = 15; // EBOM ID
                        worksheet.Column(5).Width = 15; // Part number
                        worksheet.Column(9).Width = 17; // Unit price

                        var stretchCols = new int[] { 2, 3, 6, 7, 8, 10, 11, 12 };
                        foreach (var col in stretchCols)
                        {
                            worksheet.Column(col).AdjustToContents();
                        }
                        
                        worksheet.Column(4).Width = 45;
                        worksheet.Column(4).Style.Alignment.WrapText = true;
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();

                        return File(
                            content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            CommonHelpers.renamePR(pr.Prname, pr.RevisionNo) + ".xlsx");
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


                var url = Url.Action("downloadFile", "BOM", new { id = supportFile.Id });

                return url;
            }
            else
            {
                return Url.Action("ShowPDF", "BOM", new { supportFileId = supportFile.Id }, HttpContext.Request.Scheme);
            }
        }
    
        [AllowAnonymous]
        public async Task<IActionResult> FixPR5Schema()
        {
            var form = await _context.Forms.FirstOrDefaultAsync(x => x.Name == "PR5");
            if (form == null) return Content("PR5 form not found");

            try
            {
                var jObject = Newtonsoft.Json.Linq.JObject.Parse(form.Data);
                
                int fixedCount = 0;
                
                FixClearOnHide(jObject, ref fixedCount);

                form.Data = jObject.ToString(Newtonsoft.Json.Formatting.None);
                _context.Update(form);
                await _context.SaveChangesAsync();

                return Content($"Fixed PR5 Schema: Set clearOnHide=false for {fixedCount} components (tax, shipping, bank).");
            }
            catch (Exception ex)
            {
                return Content($"Error: {ex.Message}");
            }
        }

        private void FixClearOnHide(Newtonsoft.Json.Linq.JToken token, ref int count)
        {
            if (token == null) return;
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                foreach (var item in token) FixClearOnHide(item, ref count);
                return;
            }
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var obj = (Newtonsoft.Json.Linq.JObject)token;
                var key = obj["key"]?.ToString();
                
                if (key == "tax" || key == "shippingCost" || key == "bankFee")
                {
                    bool changed = false;
                    if (obj["clearOnHide"] == null) 
                    {
                        obj["clearOnHide"] = false;
                        changed = true;
                    } 
                    else if (obj["clearOnHide"].Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)obj["clearOnHide"] == true)
                    {
                        obj["clearOnHide"] = false;
                        changed = true;
                    }

                    if (changed) count++;
                }
                
                foreach (var property in obj.Properties()) FixClearOnHide(property.Value, ref count);
            }
        }

        private Newtonsoft.Json.Linq.JObject FindComponentRecursive(Newtonsoft.Json.Linq.JToken token, string key)
        {
             if (token == null) return null;
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                foreach (var item in token)
                {
                    var found = FindComponentRecursive(item, key);
                    if (found != null) return found;
                }
                return null;
            }
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var obj = (Newtonsoft.Json.Linq.JObject)token;
                if (obj["key"]?.ToString() == key)
                {
                    return obj;
                }
                foreach (var property in obj.Properties())
                {
                    var found = FindComponentRecursive(property.Value, key);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}


