#nullable disable
using ClosedXML.Excel;
using DocumentFormat.OpenXml.VariantTypes;
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
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using OfficeOpenXml.Drawing.Controls;
using Org.BouncyCastle.Ocsp;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using static eForm_dotnetCore.ViewModels.RFQExportModel;

namespace eForm_dotnetCore.Controllers
{
    public class RFQController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IAuthorizationService authorizationService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public RFQController(EformSophicDemoContext context,
            IEmailService emailService,
            IAuthorizationService authorizationService,
            IAuditTrailService auditTrailService,
            IWebHostEnvironment environment
            )
        {
            _context = context;
            _emailService = emailService;
            this.authorizationService = authorizationService;
            _auditTrailService = auditTrailService;
            _env = environment;
        }

        public async Task<IActionResult> deleteForm(int? id,string reason = null)
        {
            if (!id.HasValue && !_context.Rfqs.Any(x => x.Id == id))
                throw new NullReferenceException(nameof(id));

            var targetRFQ = _context.Rfqs.Where(x => x.Id == id).FirstOrDefault();
            var pics = new List<int> { targetRFQ.RequestorId.Value };

            await _auditTrailService.InsertLogAsync(
                       Common.LogLevel.Information, targetRFQ.Rfqname +" has been removed.",
                       pics:pics,
                       fullMessage: $@"{targetRFQ} has been remove by {HttpContext.GetShortName()}" + (!string.IsNullOrEmpty(reason) ? $@" because of {reason}." : "."),
                       tableChange: nameof(Rfq),
                       empId: int.Parse(HttpContext.GetUserId()),
                       ProjectId: targetRFQ.ProjectId
                       );

            _context.Rfqs.Remove(targetRFQ);

            _context.SaveChanges();



            return NoContent();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Edit(int? id,int? bomId, string error = "")
        {
            if (id == null)
            {
                return NotFound();
            }

            int rfqId = (int)id;
            var fileDic = "BOMTemplate";
            Bom BOM = null;
            string rootPath = _env.WebRootPath; //wwwroot folder
            string templateDestination = Path.Combine(_env.WebRootPath,fileDic,"ImportRFQTemplate.xlsx");
            //ViewBag.RFQTemplateUrl = $@"/{fileDic}/ImportRFQTemplate.xlsx";
            ViewBag.RFQTemplateUrl = $@"/{fileDic}/ImportRFQTemplate.xlsx?v={DateTime.Now.Ticks}";

            var BOMQuery = _context.Boms
                .Include(x => x.RFQ)
                .Include(x => x.BomSupportFiles)
                .ThenInclude(x => x.SupportFile)
                .Where(x => x.RFQId == id.Value).AsQueryable();

            if (bomId.HasValue)
            {
                BOM = BOMQuery.FirstOrDefault(x => x.Id == bomId);
            }
            else
            {
                BOM = BOMQuery.FirstOrDefault();
            }

            var rfqTemp = await _context.Forms.FirstOrDefaultAsync(x => x.Name == "RFQ4_new");
            if (BOM == null)
            {
                return NotFound();
            }

            BOM.RFQ.Data = rfqTemp.Data;

            var project = _context.Projects.Include(x => x.CategoryInfo).Where(d => d.Id == BOM.ProjectId).FirstOrDefault();

            ViewBag.ProjectId = project.Id;
            ViewBag.Error = error;
            ViewBag.isProjectCompleted = project.Status == "Completed";
            ViewBag.isFabrication = BOM.ItemCategory == "Fabrication";
            ViewBag.CategoryList = await _context.Boms
                .Select(b => b.ItemCategory)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();


            if (!string.IsNullOrEmpty(BOM.RFQ.DataResponse))
            {
                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(BOM.RFQ.DataResponse);
                root.data.userType = string.IsNullOrEmpty(HttpContext.GetUserType()) ? "" : HttpContext.GetUserType();
                root.data.isBuyer = (HttpContext.GetUserType().Equals("Buyer", StringComparison.OrdinalIgnoreCase) || 
                     HttpContext.GetUserType().Equals("SuperAdminAssistant", StringComparison.OrdinalIgnoreCase) ||
                     HttpContext.GetUserType().Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)) && !BOM.Status.Contains("New") ? 1 : 0;
                root.data.rfqId = id.Value;//RFQ.Bomid.Value;
                root.data.allowEdit = HttpContext.GetUserType() == "Buyer" || HttpContext.GetUserType() == "SuperAdminAssistant" || HttpContext.GetUserType() == "SuperAdmin" ? 1 : 0;
                root.data.editAll = CommonHelpers.isPics(project) || HttpContext.GetUserType().Equals("SuperAdminAssistant", StringComparison.OrdinalIgnoreCase)|| HttpContext.GetUserType().Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                root.data.isFabrication = BOM.ItemCategory == "Fabrication" ? 1 : 0;
                var checkEnableAddItem = (root.data.editAll == 1 && (BOM.Status.Contains("Reject") || BOM.Status.Contains("Cancelled"))) ||
                    (BOM.Status.Equals("RFQ Sent") && root.data.allowEdit == 1);
                root.data.enableAddItem = root.data.isFabrication != 1 && checkEnableAddItem
                    ? 1 : 0;

                //var superAdmins = CommonExtensions.SuperAdminList();
                var requestorList = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).ToList();
                //var suppliers = _context.Supplier.Include(x => x.SupplierAttachments).ThenInclude(x => x.SupportFile).ToList();
                var supplierAttachments = _context.SupplierAttachment
                   .Include(x => x.SupportFile)
                   .Include(x => x.SupplierInfo)
                   .Where(x => x.RFQId == rfqId).ToList();
                root.data.userList = string.IsNullOrEmpty(project.PICS) ? string.Empty : getPicsWithDept(project.PICS);
                root.data.isRequestor = CommonHelpers.isPics(project) ? 1 : 0;
                root.data.ableViewDrawing = CommonHelpers.isPics(project) || HttpContext.GetUserType().Equals("Buyer",StringComparison.OrdinalIgnoreCase)
                    || HttpContext.GetUserType().Equals("PM", StringComparison.OrdinalIgnoreCase)
                    || HttpContext.GetUserType().Equals("HOD", StringComparison.OrdinalIgnoreCase) ?1 : 0;
                var departmenList = new List<object>();
                //var supplierList = new List<object>();

                foreach (var requestor in requestorList)
                {
                    departmenList.Add(new
                    {
                        username = requestor.ShortName + "(" + requestor.EmpDepartment.Name + ")",
                        department = requestor.EmpDepartment.Name
                    });

                }

                #region rewritesupplierlist-ignore first
                var rewriteSupplierList = new List<SupplierList>();
                if (root.data.supplierList.Count() > 0)
                {
                    root.data.supplierList.ForEach(x =>
                    {
                        if(x.supplierKey != null && x.supplierKey.id == null)
                        {
                            x.supplierKey = null;
                        }
                    });

                    var notQuotationSupplierList = root.data.supplierList.Where(x => x.supplierKey == null
                    || (x.supplierKey != null && (x.quotation == null || x.quotation.Count == 0))).ToList();

                    rewriteSupplierList.AddRange(notQuotationSupplierList);

                    root.data.supplierList = root.data.supplierList.Except(rewriteSupplierList).ToList();
                }

                if (supplierAttachments.Count() > 0)
                {
                    foreach (var attach in supplierAttachments)
                    {

                        rewriteSupplierList.Add(new SupplierList
                        {                  
                            supplierKey = new SupplierObject
                            {
                                id = attach.SupplierInfoId.ToString(),
                                name = attach.SupplierInfo.VendorName,
                            },
                            vendor_remark = attach.V_Remark,
                            remark = attach.Remark,
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
                    }
                }

                root.data.supplierList = rewriteSupplierList;

                #endregion
                //root.data.suppliers = JsonConvert.SerializeObject(supplierList);

                root.data.departmentList = JsonConvert.SerializeObject(departmenList);
                root.data.approverCommentBox = BOM.RFQ.Remark;
                root.data.LatestSupplierRemark = BOM.RFQ.LatestSupplierRemark;

                root.data.header.FirstOrDefault().code = project.Code;
                root.data.header.FirstOrDefault().projectName = project.Name;
                root.data.header.FirstOrDefault().poNumber = project.PoNumber;
                root.data.header.FirstOrDefault().projectName = project.Name;
                root.data.header.FirstOrDefault().projectCategory = project.CategoryInfo.CategoryShortName;
                root.data.header.FirstOrDefault().customer = project.Customer;
                root.data.header.FirstOrDefault().poQuantity = project.PoQuantity ?? 0;
                root.data.header.FirstOrDefault().budgetMaterialCost = project.BudgetMaterialCost ?? 0;
                root.data.header.FirstOrDefault().budgetMaterialCost = project.BudgetMaterialCost ?? 0;

                if (BOM.RFQ.RequestorId.HasValue)
                {
                    var requestorUpdated = _context.EmployeeInfos
                        .Include(x => x.EmpDepartment)
                        .Where(x => x.Id == BOM.RFQ.RequestorId)
                        .FirstOrDefault();
                    root.data.header.FirstOrDefault().requester = requestorUpdated.ShortName + "(" + requestorUpdated.EmpDepartment.Name + ")";
                }

                if (root.data.bomGrid.Count() > 0)
                {
                    var ddUrl = new List<object>();
                    for (var i = 0; i < root.data.bomGrid.Count(); i++)
                    {
                        var rootBom = await _context.Boms
                            .Include(x => x.BomSupportFiles)
                            .ThenInclude(x => x.SupportFile)
                            .Where(x => x.Id == root.data.bomGrid[i].itemLineNo).FirstOrDefaultAsync();
                        if (rootBom != null)
                        {
                            root.data.bomGrid[i].itemLineNo = rootBom.Id;
                            root.data.bomGrid[i].bomNo = rootBom.BomNo;
                            root.data.bomGrid[i].itemCategory = rootBom.ItemCategory + " Part";
                            root.data.bomGrid[i].module = rootBom.Module;
                            root.data.bomGrid[i].partNo = rootBom.PartNumber;
                            root.data.bomGrid[i].brand = rootBom.Brand;
                            root.data.bomGrid[i].description = rootBom.PartDesc;
                            root.data.bomGrid[i].totalQuantity = rootBom.RequireQty ?? 0;

                            if (rootBom.BomSupportFiles != null && rootBom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                            {
                                foreach(var bsf in rootBom.BomSupportFiles.Where(x => x.Type == "IC").ToList())
                                {
                                    ddUrl.Add(new
                                    {
                                        lineNo = rootBom.Id,
                                        downloadLink = generateViewURL(bsf.SupportFile),
                                        fileName = bsf.SupportFile.FileName
                                    });
                                }
                            }
                        }
                    }
                    root.data.ddURL = JsonConvert.SerializeObject(ddUrl);

                    if (root.data.bomGrid.Any(x => x.itemLineNo != null))
                    {
                        root.data.bomGrid = root.data.bomGrid
                            .OrderBy(x =>
                            {
                                if (string.IsNullOrWhiteSpace(x.quotationLineNo))
                                    return double.MaxValue;

                                var cleaned = new string(x.quotationLineNo.Where(c => char.IsDigit(c) || c == '.').ToArray());

                                return double.TryParse(cleaned, out double result) ? result : double.MaxValue;
                            })
                            .ToList();
                    }


                }

                    ViewBag.isDepartmentEmpty = string.IsNullOrEmpty(root.data.header.FirstOrDefault().department);
                var isRequestor = BOM.RFQ.RequestorId.HasValue ? BOM.RFQ.RequestorId.Equals(int.Parse(HttpContext.GetUserId()))
                    && !HttpContext.GetUserType().Equals("Buyer", StringComparison.InvariantCultureIgnoreCase) : false;
                ViewBag.allowSubmit = false;
                var bomStatus = BOM.Status;
                ViewBag.BomStatus = bomStatus;

                if (bomStatus == "New" || bomStatus == "PR Reject" || bomStatus == "PO Reject")
                {
                    ViewBag.allowSubmit = isRequestor || HttpContext.GetUserType() == "SuperAdminAssistant" || HttpContext.GetUserType() == "SuperAdmin" || CommonHelpers.isPics(project);
                }
                else if (bomStatus == "RFQ Sent" || bomStatus == "RFQ Reject")
                {
                    ViewBag.allowSubmit = HttpContext.GetUserType() == "Buyer" || HttpContext.GetUserType() == "SuperAdminAssistant" || HttpContext.GetUserType() == "SuperAdmin";
                }
                else if (bomStatus == "RFQ Updated")
                {
                    if (HttpContext.GetUserType() == "SuperAdmin" || HttpContext.GetUserType() == "SuperAdminAssistant")
                    {
                        ViewBag.allowSubmit = true;
                    }
                    else
                    {
                        ViewBag.allowSubmit = isRequestor || CommonHelpers.isPics(project);
                    }
                }

                var saveRFQ = _context.Rfqs.Where(x => x.Id == BOM.RFQId).FirstOrDefault();

                root.data.bomGrid.ForEach(x =>
                {
                    if (x.totalPrice.HasValue && Double.IsNaN(x.totalPrice.Value))
                    {
                        x.totalPrice = 0;
                    }
                });

                saveRFQ.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                // Also update related PR's RequestorId
                var pr = _context.Prs.FirstOrDefault(x => x.Rfqid == saveRFQ.Id.ToString());
                if (pr != null && BOM.RFQ.RequestorId.HasValue)
                {
                    pr.RequestorId = BOM.RFQ.RequestorId.Value;
                    _context.Prs.Update(pr);
                }
                // Only update DataResponse — using Entry().Property() so EF marks just
                // this one column as Modified. A full Update(saveRFQ) would overwrite every
                // column, wiping LatestSupplierRemark with null on every page load.
                _context.Entry(saveRFQ).Property(x => x.DataResponse).IsModified = true;
                _context.SaveChanges();
            }

            //var project = _context.Projects.Where(d => d.Id == RFQ.ProjectId).FirstOrDefault();
            ViewBag.ProjectId = project.Id;
            ViewBag.projectCode = project.Code;
            ViewBag.poNo = project.PoNumber;
            ViewBag.projectName = project.Name;
            ViewBag.category = project.Category;
            ViewBag.poQuantity = "";
            ViewBag.customer = project.Requestor;

            var checkRFQ = _context.Sourcing.Where(x => x.RFQId == rfqId).FirstOrDefault();

            var srs = new List<RFQSharing>();
            string currency = "MYR";
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == rfqId);
            if (rfq == null)
            {
                return NotFound();
            }

            if (checkRFQ?.Id != null)
            {
              
                var bom = _context.Boms.FirstOrDefault(b => b.Id == bomId);

                if (bom != null)
                {
                   
                    var sourceIds = _context.Sourcing
                        .Where(s => s.RFQId == bom.RFQId && s.Brand == bom.Brand)
                        .Select(s => s.Id)
                        .ToList();

                    
                    srs = _context.RFQSharing
                        .Include(x => x.Sourcing)
                        .ThenInclude(x => x.RFQ)
                        .Include(x => x.Supplier)
                        .Where(x => sourceIds.Contains(x.SourceId))
                        .ToList();
                }
                else
                {
                    
                    srs = _context.RFQSharing
                        .Include(x => x.Sourcing)
                        .ThenInclude(x => x.RFQ)
                        .Include(x => x.Supplier)
                        .Where(x => x.SourceId == checkRFQ.Id)
                        .ToList();
                }

                
                foreach (var s in srs)
                {
                    if (string.IsNullOrEmpty(s.DataResponse))
                        continue;

                    try
                    {
                        var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(s.DataResponse);
                        var bomGrid = jsonObj["data"]?["bomGrid"] as Newtonsoft.Json.Linq.JArray;

                        
                        if (bomGrid != null && bomGrid.Count > 0)
                        {
                            var currencyElement = bomGrid.First()?["currency"];
                            if (currencyElement != null && !string.IsNullOrEmpty(currencyElement.ToString()))
                            {
                                
                                s.GetType().GetProperty("Currency")?.SetValue(s, currencyElement.ToString());
                            }

                            // Extract ALL unique lead times from bomGrid items
                            var uniqueLeadTimes = new List<int>();
                            foreach (var item in bomGrid)
                            {
                                var leadTimeElement = item["leadTime"];
                                if (leadTimeElement != null && int.TryParse(leadTimeElement.ToString(), out int leadTimeDays))
                                {
                                    if (!uniqueLeadTimes.Contains(leadTimeDays))
                                    {
                                        uniqueLeadTimes.Add(leadTimeDays);
                                    }
                                }
                            }

                            // Calculate all unique delivery dates
                            if (uniqueLeadTimes.Any())
                            {
                                var baseDate = s.SubmittedDate ?? s.CreatedDate ?? DateTime.Now;
                                var deliveryDates = uniqueLeadTimes
                                    .Select(days => baseDate.AddDays(days))
                                    .OrderBy(date => date)
                                    .ToList();

                                // Store unique delivery dates
                                s.UniqueDeliveryDates = deliveryDates;

                                // Keep the first/earliest date for backward compatibility
                                s.LeadTime = deliveryDates.First();
                                s.LeadTimeDays = uniqueLeadTimes.Min();
                            }
                        }


                        var attachments = jsonObj["data"]?["attachments"] as JArray;
                        if (attachments != null && attachments.Count > 0)
                        {
                            var fileList = new List<Files>();

                            foreach (var attachGroup in attachments)
                            {
                                // check if it contains 'attachment' array or direct file object
                                var files = attachGroup["attachment"] as JArray;

                                if (files != null)
                                {
                                    foreach (var file in files)
                                    {
                                        var f = file.ToObject<Files>();
                                        if (f != null)
                                            fileList.Add(f);
                                    }
                                }
                                else
                                {
                                    // direct single-level file
                                    var f = attachGroup.ToObject<Files>();
                                    if (f != null)
                                        fileList.Add(f);
                                }
                            }

                            s.GetType().GetProperty("Attachments")?.SetValue(s, fileList);
                        }



                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        continue;
                    }
                }

                //var firstRFQ = srs.FirstOrDefault();
                //if (firstRFQ != null && !string.IsNullOrEmpty(firstRFQ.DataResponse))
                //{
                //    try
                //    {
                //        var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(firstRFQ.DataResponse);

                //        //if (jsonObj.TryGetValue("currency", out var currencyElement))
                //        //{

                //        //    currency = currencyElement.ToString();
                //        //}

                //        var currencyElement = jsonObj["data"]?["bomGrid"]?.First()?["currency"];

                //        if (currencyElement != null)
                //        {
                //            currency = currencyElement.ToString();
                //            Console.WriteLine("Currency: " + currency);
                //        }
                //        else
                //        {
                //            Console.WriteLine("Currency not found");
                //        }
                //    }
                //    catch (Newtonsoft.Json.JsonException)
                //    {
                //        // Handle error
                //    }
                //}
            }

            // Calculate all unique delivery dates across all suppliers
            var allUniqueDeliveryDates = new List<DateTime>();
            foreach (var s in srs)
            {
                if (s.UniqueDeliveryDates != null)
                {
                    allUniqueDeliveryDates.AddRange(s.UniqueDeliveryDates);
                }
            }

            // Pass data to the view
            ViewBag.srs = srs;
            ViewBag.Currency = currency;
            ViewBag.RFQId = rfq.Id;
            ViewBag.AllUniqueDeliveryDates = allUniqueDeliveryDates
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var sourceId = _context.Sourcing.Where(s => s.RFQId == rfqId).Select(s => s.Id).FirstOrDefault();
            ViewBag.SourceId = sourceId;

            return View(BOM);


            /*var srs = new List<RFQSharing>();

            string currency = "MYR"; // Default currency

            if (checkRFQ?.Id != null)
            {
                srs = _context.RFQSharing
                    .Include(x => x.Sourcing)
                    .ThenInclude(x => x.RFQ)
                    .Include(x => x.Supplier)
                    .Where(x => x.SourceId == checkRFQ.Id)
                    .ToList();

                // Extract currency from the first matching RFQSharing record
                var firstRFQ = srs.FirstOrDefault();
                if (firstRFQ != null && !string.IsNullOrEmpty(firstRFQ.DataResponse))
                {
                    try
                    {
                        var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(firstRFQ.DataResponse);
                        if (jsonObj.TryGetValue("currency", out var currencyElement))
                        {
                            currency = currencyElement.ToString();
                        }
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {

                    }

                }
            }

            // Pass data to the view
            ViewBag.srs = srs;
            ViewBag.Currency = currency;

            return View(BOM);*/

        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> AnonymousEdit(int? id, int? bomId, string error = "")
        {
            if (id == null)
            {
                return NotFound();
            }

            int rfqId = (int)id;
            var fileDic = "BOMTemplate";
            Bom BOM = null;
            string rootPath = _env.WebRootPath; //wwwroot folder
            string templateDestination = Path.Combine(_env.WebRootPath, fileDic, "ImportRFQTemplate.xlsx");
            //ViewBag.RFQTemplateUrl = $@"/{fileDic}/ImportRFQTemplate.xlsx";
            ViewBag.RFQTemplateUrl = $@"/{fileDic}/ImportRFQTemplate.xlsx?v={DateTime.Now.Ticks}";

            var BOMQuery = _context.Boms
                .Include(x => x.RFQ)
                .Include(x => x.BomSupportFiles)
                .ThenInclude(x => x.SupportFile)
                .Where(x => x.RFQId == id.Value).AsQueryable();

            if (bomId.HasValue)
            {
                BOM = BOMQuery.FirstOrDefault(x => x.Id == bomId);
            }
            else
            {
                BOM = BOMQuery.FirstOrDefault();
            }

            var rfqTemp = await _context.Forms.FirstOrDefaultAsync(x => x.Name == "RFQ4_new");
            if (BOM == null)
            {
                return NotFound();
            }

            BOM.RFQ.Data = rfqTemp.Data;

            var project = _context.Projects.Include(x => x.CategoryInfo).Where(d => d.Id == BOM.ProjectId).FirstOrDefault();

            ViewBag.ProjectId = project.Id;
            ViewBag.Error = error;
            ViewBag.isProjectCompleted = project.Status == "Completed";
            ViewBag.isFabrication = BOM.ItemCategory == "Fabrication";
            ViewBag.CategoryList = await _context.Boms
                .Select(b => b.ItemCategory)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            int? picId = null;

            if (!string.IsNullOrEmpty(project.PICS))
            {
                var firstPic = project.PICS.Split(',').FirstOrDefault();
                if (int.TryParse(firstPic, out int parsed))
                {
                    picId = parsed;
                }
            }

            string userType = "";

            var user = BOM.RFQ.RequestorId.HasValue
         ? _context.EmployeeInfos.FirstOrDefault(x => x.Id == BOM.RFQ.RequestorId)
         : (picId.HasValue
             ? _context.EmployeeInfos.FirstOrDefault(x => x.Id == picId.Value)
             : null);

            if (user != null)
            {
                userType = user.UserType;
            }


            if (!string.IsNullOrEmpty(BOM.RFQ.DataResponse))
            {
                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(BOM.RFQ.DataResponse);
                root.data.userType = userType;
                root.data.isBuyer = userType.Contains("Buyer") || userType.Contains("SuperAdminAssistant") || userType.Contains("SuperAdmin") ? 1 : 0;
                root.data.rfqId = id.Value;//RFQ.Bomid.Value;
                root.data.allowEdit = userType.Contains("Buyer") || userType.Contains("SuperAdminAssistant")  || userType.Contains("SuperAdmin") ? 1 : 0;
                //root.data.editAll = CommonHelpers.isPics(project) || userType.Contains("SuperAdmin") ? 1 : 0;
                root.data.isFabrication = BOM.ItemCategory == "Fabrication" ? 1 : 0;
                var checkEnableAddItem = (root.data.editAll == 1 && (BOM.Status.Contains("Reject") || BOM.Status.Contains("Cancelled"))) ||
                    (BOM.Status.Equals("RFQ Sent") && root.data.allowEdit == 1);
                root.data.enableAddItem = root.data.isFabrication != 1 && checkEnableAddItem
                    ? 1 : 0;

                //var superAdmins = CommonExtensions.SuperAdminList();
                var requestorList = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).ToList();
                //var suppliers = _context.Supplier.Include(x => x.SupplierAttachments).ThenInclude(x => x.SupportFile).ToList();
                var supplierAttachments = _context.SupplierAttachment
                   .Include(x => x.SupportFile)
                   .Include(x => x.SupplierInfo)
                   .Where(x => x.RFQId == rfqId).ToList();
                root.data.userList = string.IsNullOrEmpty(project.PICS) ? string.Empty : getPicsWithDept(project.PICS);
               // root.data.isRequestor = CommonHelpers.isPics(project) ? 1 : 0;
      //          root.data.ableViewDrawing = CommonHelpers.isPics(project) || userType.Contains("Buyer")
      //|| userType.Contains("PM")
      //|| userType.Contains("HOD") ? 1 : 0;
                var departmenList = new List<object>();
                //var supplierList = new List<object>();

                foreach (var requestor in requestorList)
                {
                    departmenList.Add(new
                    {
                        username = requestor.ShortName + "(" + requestor.EmpDepartment.Name + ")",
                        department = requestor.EmpDepartment.Name
                    });

                }

                #region rewritesupplierlist-ignore first
                var rewriteSupplierList = new List<SupplierList>();
                if (root.data.supplierList.Count() > 0)
                {
                    root.data.supplierList.ForEach(x =>
                    {
                        if (x.supplierKey != null && x.supplierKey.id == null)
                        {
                            x.supplierKey = null;
                        }
                    });

                    var notQuotationSupplierList = root.data.supplierList.Where(x => x.supplierKey == null
                    || (x.supplierKey != null && (x.quotation == null || x.quotation.Count == 0))).ToList();

                    rewriteSupplierList.AddRange(notQuotationSupplierList);

                    root.data.supplierList = root.data.supplierList.Except(rewriteSupplierList).ToList();
                }

                if (supplierAttachments.Count() > 0)
                {
                    foreach (var attach in supplierAttachments)
                    {
                        rewriteSupplierList.Add(new SupplierList
                        {
                            supplierKey = new SupplierObject
                            {
                                id = attach.SupplierInfoId.ToString(),
                                name = attach.SupplierInfo.VendorName,
                            },
                            remark = attach.Remark,
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
                    }
                }

                root.data.supplierList = rewriteSupplierList;
                #endregion
                //root.data.suppliers = JsonConvert.SerializeObject(supplierList);

                root.data.departmentList = JsonConvert.SerializeObject(departmenList);
                root.data.approverCommentBox = BOM.RFQ.Remark;
                root.data.LatestSupplierRemark = BOM.RFQ.LatestSupplierRemark;

                root.data.header.FirstOrDefault().code = project.Code;
                root.data.header.FirstOrDefault().projectName = project.Name;
                root.data.header.FirstOrDefault().poNumber = project.PoNumber;
                root.data.header.FirstOrDefault().projectName = project.Name;
                root.data.header.FirstOrDefault().projectCategory = project.CategoryInfo.CategoryShortName;
                root.data.header.FirstOrDefault().customer = project.Customer;
                root.data.header.FirstOrDefault().poQuantity = project.PoQuantity ?? 0;
                root.data.header.FirstOrDefault().budgetMaterialCost = project.BudgetMaterialCost ?? 0;
                root.data.header.FirstOrDefault().budgetMaterialCost = project.BudgetMaterialCost ?? 0;

                if (BOM.RFQ.RequestorId.HasValue)
                {
                    var requestorUpdated = _context.EmployeeInfos
                        .Include(x => x.EmpDepartment)
                        .Where(x => x.Id == BOM.RFQ.RequestorId)
                        .FirstOrDefault();
                    root.data.header.FirstOrDefault().requester = requestorUpdated.ShortName + "(" + requestorUpdated.EmpDepartment.Name + ")";
                }

                if (root.data.bomGrid.Count() > 0)
                {
                    var ddUrl = new List<object>();
                    for (var i = 0; i < root.data.bomGrid.Count(); i++)
                    {
                        var rootBom = await _context.Boms
                            .Include(x => x.BomSupportFiles)
                            .ThenInclude(x => x.SupportFile)
                            .Where(x => x.Id == root.data.bomGrid[i].itemLineNo).FirstOrDefaultAsync();
                        if (rootBom != null)
                        {
                            root.data.bomGrid[i].itemLineNo = rootBom.Id;
                            root.data.bomGrid[i].bomNo = rootBom.BomNo;
                            root.data.bomGrid[i].itemCategory = rootBom.ItemCategory + " Part";
                            root.data.bomGrid[i].module = rootBom.Module;
                            root.data.bomGrid[i].partNo = rootBom.PartNumber;
                            root.data.bomGrid[i].brand = rootBom.Brand;
                            root.data.bomGrid[i].description = rootBom.PartDesc;
                            root.data.bomGrid[i].totalQuantity = rootBom.RequireQty ?? 0;

                            if (rootBom.BomSupportFiles != null && rootBom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                            {
                                foreach (var bsf in rootBom.BomSupportFiles.Where(x => x.Type == "IC").ToList())
                                {
                                    ddUrl.Add(new
                                    {
                                        lineNo = rootBom.Id,
                                        downloadLink = generateViewURL(bsf.SupportFile),
                                        fileName = bsf.SupportFile.FileName
                                    });
                                }
                            }
                        }
                    }
                    root.data.ddURL = JsonConvert.SerializeObject(ddUrl);

                    if (root.data.bomGrid.Any(x => x.itemLineNo != null))
                    {
                        root.data.bomGrid = root.data.bomGrid
                            .OrderBy(x =>
                            {
                                if (string.IsNullOrWhiteSpace(x.quotationLineNo))
                                    return double.MaxValue;

                                var cleaned = new string(x.quotationLineNo.Where(c => char.IsDigit(c) || c == '.').ToArray());

                                return double.TryParse(cleaned, out double result) ? result : double.MaxValue;
                            })
                            .ToList();
                    }


                }

                ViewBag.isDepartmentEmpty = string.IsNullOrEmpty(root.data.header.FirstOrDefault().department);
                //var isRequestor = BOM.RFQ.RequestorId.HasValue ? BOM.RFQ.RequestorId.Equals(int.Parse(HttpContext.GetUserId()))
                //    && !HttpContext.GetUserType().Equals("Buyer", StringComparison.InvariantCultureIgnoreCase) : false;
                ViewBag.allowSubmit = true;
                var bomStatus = BOM.Status;
                ViewBag.BomStatus = bomStatus;               

                var saveRFQ = _context.Rfqs.Where(x => x.Id == BOM.RFQId).FirstOrDefault();

                root.data.bomGrid.ForEach(x =>
                {
                    if (x.totalPrice.HasValue && Double.IsNaN(x.totalPrice.Value))
                    {
                        x.totalPrice = 0;
                    }
                });

                saveRFQ.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                
                var pr = _context.Prs.FirstOrDefault(x => x.Rfqid == saveRFQ.Id.ToString());
                if (pr != null && BOM.RFQ.RequestorId.HasValue)
                {
                    pr.RequestorId = BOM.RFQ.RequestorId.Value;
                    _context.Prs.Update(pr);
                }
                _context.Rfqs.Update(saveRFQ);
                _context.SaveChanges();
            }

            //var project = _context.Projects.Where(d => d.Id == RFQ.ProjectId).FirstOrDefault();
            ViewBag.ProjectId = project.Id;
            ViewBag.projectCode = project.Code;
            ViewBag.poNo = project.PoNumber;
            ViewBag.projectName = project.Name;
            ViewBag.category = project.Category;
            ViewBag.poQuantity = "";
            ViewBag.customer = project.Requestor;

            var checkRFQ = _context.Sourcing.Where(x => x.RFQId == rfqId).FirstOrDefault();

            var srs = new List<RFQSharing>();
            string currency = "MYR";
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == rfqId);
            if (rfq == null)
            {
                return NotFound();
            }

            if (checkRFQ?.Id != null)
            {
                
                var bom = _context.Boms.FirstOrDefault(b => b.Id == bomId);

                if (bom != null)
                {
                    
                    var sourceIds = _context.Sourcing
                        .Where(s => s.RFQId == bom.RFQId && s.Brand == bom.Brand)
                        .Select(s => s.Id)
                        .ToList();

                    
                    srs = _context.RFQSharing
                        .Include(x => x.Sourcing)
                        .ThenInclude(x => x.RFQ)
                        .Include(x => x.Supplier)
                        .Where(x => sourceIds.Contains(x.SourceId))
                        .ToList();
                }
                else
                {
                    
                    srs = _context.RFQSharing
                        .Include(x => x.Sourcing)
                        .ThenInclude(x => x.RFQ)
                        .Include(x => x.Supplier)
                        .Where(x => x.SourceId == checkRFQ.Id)
                        .ToList();
                }


                
                // Process each RFQSharing record to extract currency, lead times, and attachments
                foreach (var s in srs)
                {
                    if (string.IsNullOrEmpty(s.DataResponse))
                        continue;

                    try
                    {
                        var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(s.DataResponse);
                        var bomGrid = jsonObj["data"]?["bomGrid"] as Newtonsoft.Json.Linq.JArray;

                        if (bomGrid != null && bomGrid.Count > 0)
                        {
                            // Extract currency
                            var currencyElement = bomGrid.First()?["currency"];
                            if (currencyElement != null && !string.IsNullOrEmpty(currencyElement.ToString()))
                            {
                                s.Currency = currencyElement.ToString();
                            }

                            // Extract ALL unique lead times from bomGrid items
                            var uniqueLeadTimes = new List<int>();
                            foreach (var item in bomGrid)
                            {
                                var leadTimeElement = item["leadTime"];
                                if (leadTimeElement != null && int.TryParse(leadTimeElement.ToString(), out int leadTimeDays))
                                {
                                    if (!uniqueLeadTimes.Contains(leadTimeDays))
                                    {
                                        uniqueLeadTimes.Add(leadTimeDays);
                                    }
                                }
                            }

                            // Calculate all unique delivery dates
                            if (uniqueLeadTimes.Any())
                            {
                                var baseDate = s.SubmittedDate ?? s.CreatedDate ?? DateTime.Now;
                                var deliveryDates = uniqueLeadTimes
                                    .Select(days => baseDate.AddDays(days))
                                    .OrderBy(date => date)
                                    .ToList();

                                // Store unique delivery dates
                                s.UniqueDeliveryDates = deliveryDates;

                                // Keep the first/earliest date for backward compatibility
                                s.LeadTime = deliveryDates.First();
                                s.LeadTimeDays = uniqueLeadTimes.Min();
                            }
                        }

                        // Extract attachments
                        var attachments = jsonObj["data"]?["attachments"] as JArray;
                        if (attachments != null && attachments.Count > 0)
                        {
                            var fileList = new List<Files>();

                            foreach (var attachGroup in attachments)
                            {
                                var files = attachGroup["attachment"] as JArray;

                                if (files != null)
                                {
                                    foreach (var file in files)
                                    {
                                        var f = file.ToObject<Files>();
                                        if (f != null)
                                            fileList.Add(f);
                                    }
                                }
                                else
                                {
                                    var f = attachGroup.ToObject<Files>();
                                    if (f != null)
                                        fileList.Add(f);
                                }
                            }

                            s.Attachments = fileList;
                        }
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        continue;
                    }
                }
            }

            // Calculate all unique delivery dates across all suppliers
            var allUniqueDeliveryDates = new List<DateTime>();
            foreach (var s in srs)
            {
                if (s.UniqueDeliveryDates != null)
                {
                    allUniqueDeliveryDates.AddRange(s.UniqueDeliveryDates);
                }
            }
            
            ViewBag.srs = srs;
            ViewBag.Currency = currency;
            ViewBag.RFQId = rfq.Id;
            ViewBag.AutoTrigger = true;
            ViewBag.AllUniqueDeliveryDates = allUniqueDeliveryDates
                .Distinct()
                .OrderBy(d => d)
                .ToList();

           // return RedirectToAction("AnonymousEdit", "RFQ", new {id = rfqId, bomId = bomId, autoTrigger=true}); 
           //return RedirectToAction("Success", "RFQSharing");

           return View(BOM);

        }

        [HttpGet]
        public IActionResult DownloadRFQDrawing(int id)
        {
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == id);
            var boms = _context.Boms.Where(x => x.RFQId.HasValue && x.RFQId == id).Select(x => x.Id).ToList();
            var supportFiles = new List<SupportFile>();
            foreach (var bomId in boms)
            {
                var checkFiles = _context.BomSupportFile.Include(x => x.SupportFile).Where(x => x.BomId == bomId).ToList();
                if (checkFiles.Count > 0)
                {
                    var loadSupportFile = checkFiles.Select(x => x.SupportFile).ToList();
                    supportFiles.AddRange(loadSupportFile);
                }
            }

            var zipName = $"{rfq.Rfqname}-Drawing-{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}.zip";
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach(var sp in supportFiles)
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

        /*public bool isBudgetExceed(int projectId)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            if (project == null) return false;

            return project.BudgetMaterialCost < project.ActualMaterialCost;
        }*/

        public IActionResult isBudgetExceed(int projectId)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            if (project == null)
                return Json(false);

            var isExceed = project.BudgetMaterialCost < project.ActualMaterialCost;
            return Json(isExceed);
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

        [HttpPost]
        [RequestFormLimits(ValueCountLimit = int.MaxValue)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SubmitRFQModel form)
        {
            try
            {
                var newForm = _context.Rfqs.FirstOrDefault(x => x.Id == form.Id);
                var boms = _context.Boms.Where(x => x.RFQId == form.Id).ToList();
                boms.ForEach(x => x.LastUpdatedDate = DateTime.Now);

                var project = _context.Projects.Where(d => d.Id == newForm.ProjectId).FirstOrDefault();

                if (isProjectCompleted(newForm.ProjectId))
                {
                    var msg = "The changes is disallow as the project already complete!";
                    return RedirectToAction("Edit", new { id = newForm.Id,bomId =form.BomId, error = msg });
                }

                if (!string.IsNullOrEmpty(form.DataResponse))
                {
                    RFQRoot rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(form.DataResponse);

                    var remark = string.Join("<br/>", rfqRoot.data.supplierList
                                        .Where(x => x.supplierKey != null && !string.IsNullOrEmpty(x.supplierKey.name) && !string.IsNullOrEmpty(x.remark))
                                        .Select(x => "<b>"+x.supplierKey.name + "</b>:" + x.remark).ToArray());

                    newForm.Remark = remark;
                }

                newForm.DataResponse = form.DataResponse == null ? newForm.DataResponse : form.DataResponse;

                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(newForm.DataResponse);
                var currentRequestor = HttpContext.GetShortName() + "(" + HttpContext.GetUserDept() + ")";

                var newAddItems = root.data.bomGrid.Where(x => !x.itemLineNo.HasValue && !string.IsNullOrEmpty(x.description)).ToList();
                root.data.bomGrid.RemoveAll(x => !x.itemLineNo.HasValue);


                //update bom data
                foreach(var existBom in root.data.bomGrid)
                {
                    if(boms.Any(x => x.Id == existBom.itemLineNo))
                    {
                        var upBom = boms.FirstOrDefault(x => x.Id == existBom.itemLineNo);
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).Brand = existBom.brand;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).PartDesc = existBom.description;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).PartNumber = existBom.partNo;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).Module = existBom.module;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).RequireQty = existBom.totalQuantity;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).RequireDate = existBom.requireDate;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).LeadTime = existBom.leadTime;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).ETA = existBom.eta;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).UnitPrice = existBom.unitPrice;
                    }
                }

                //add item item to db from rfq
                for (int i = 0; i < newAddItems.Count(); i++)
                {
                    var newBomItem = new Bom
                    {
                        BomNo = project.Code + "-" + CommonHelpers.generateUID(),
                        Module = newAddItems[i].module,
                        Brand = newAddItems[i].brand,
                        PartDesc = newAddItems[i].description,
                        PartNumber = newAddItems[i].partNo,
                        ItemCategory = boms.FirstOrDefault().ItemCategory,
                        CreatedDate = DateTime.Now,
                        RequireQty = newAddItems[i].totalQuantity,
                        LastUpdatedDate = DateTime.Now,
                        Status = boms.FirstOrDefault().Status,
                        ProjectId = boms.FirstOrDefault().ProjectId,
                        RFQId = form.Id,
                        UnitPrice = newAddItems[i].unitPrice
                    };
                    _context.Boms.Add(newBomItem);
                    _context.SaveChanges();
                    newAddItems[i].itemLineNo = newBomItem.Id;
                    root.data.bomGrid.Add(newAddItems[i]);
                    boms.Add(newBomItem);
                }

                var unmergeList = new List<Bom>();
                foreach(var bomItem in boms)
                {
                    if(!root.data.bomGrid.Any(x => x.itemLineNo.HasValue && x.itemLineNo == bomItem.Id))
                    {
                        unmergeList.Add(bomItem);
                    }
                }

                foreach(var unmergeItem in unmergeList)
                {
                    boms.Remove(unmergeItem);
                    unmergeItem.RFQId = null;
                    unmergeItem.Status = "New";
                    _context.Boms.Update(unmergeItem);
                }
                _context.SaveChanges();

                if(boms.Count <= 0)
                {
                    _context.Rfqs.Remove(newForm);
                    await _context.SaveChangesAsync();
                    return Redirect("~/BOM/Index/" + form.ProjectId);
                }

                root.data.updateDate = DateTime.Now;

                if (!string.IsNullOrEmpty(root.data.header.FirstOrDefault().requester) && 
                    !string.Equals(root.data.header.FirstOrDefault().requester, currentRequestor))
                {
                    var requestorWithDepartment = root.data.header.FirstOrDefault().requester.Split('(');
                    var requestorUpdated = _context.EmployeeInfos
                        .Include(x => x.EmpDepartment)
                        .Where(x => x.ShortName == requestorWithDepartment[0] && x.EmpDepartment.Name == requestorWithDepartment[1].TrimEnd(')'))
                        .FirstOrDefault();
                    if (requestorUpdated != null)
                        newForm.RequestorId = requestorUpdated.Id;
                }

                root.data.bomGrid.ForEach(x =>
                {
                    if (x.totalPrice.HasValue && Double.IsNaN(x.totalPrice.Value))
                    {
                        x.totalPrice = 0;
                    }
                });

                var rfqSuppliers = root.data.supplierList.Where(x => !string.IsNullOrEmpty(x.quotNoLink) && x.quotation != null
                && x.quotation.Count > 0).ToList();

                foreach(var sp in rfqSuppliers)
                {
                    var sa = _context.SupplierAttachment.Where(x => x.RFQId == form.Id && x.QuotNo == sp.quotNoLink).FirstOrDefault();

                    if(sa != null)
                    {
                        sa.Remark = sp.remark;
                        sa.V_Remark = sp.vendor_remark;
                        sa.R_Remark = sp.r_remark;
                        sa.IsRecommended = sp.recommended.HasValue ? sp.recommended.Value : sa.IsRecommended;

                        _context.SupplierAttachment.Update(sa);
                        _context.SaveChanges();
                    }
                }

                newForm.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);

                _context.Rfqs.Update(newForm);
                //_context.Boms.UpdateRange(boms);

                await _context.SaveChangesAsync();

                return Redirect("~/BOM/Index/" + form.ProjectId);

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpGet]
        public IActionResult CheckLinkedPR(int id)
        {
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == id);
            if (rfq == null) return Json(false);

            
            var pr = _context.Prs.FirstOrDefault(x => x.Rfqid == id.ToString());

            if (pr != null && (
                rfq.Status.Equals("RFQ Approved", StringComparison.OrdinalIgnoreCase) ||
                pr.Status.Equals("PR Approved", StringComparison.OrdinalIgnoreCase) ||
                pr.Status.Equals("PR Sent", StringComparison.OrdinalIgnoreCase) ||
                pr.Status.Equals("PO Approved", StringComparison.OrdinalIgnoreCase)
            ))
            {
                return Json(true); 
            }

            return Json(false);
        }


        [HttpPost]
        public async Task<ActionResult> reverseStatus(int id,string status,FormType formType, bool forceDeletePR = false)
        {
            Expression<Func<Bom, bool>> formSelector = formType switch
            {
                FormType.RFQ => bom => bom.RFQId == id,
                FormType.PR => bom => bom.PRId == id,
                _ => throw new ArgumentException()
            };

            var boms = _context.Boms.Include(x => x.Project).Where(formSelector).ToList();

            var formName = string.Empty;

            if(formType == FormType.RFQ)
            {
                var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == id);
                formName = rfq.Rfqname;

                if (forceDeletePR)
                {
                    var linkedPRs = _context.Prs
                        .Where(x => x.Rfqid == id.ToString())
                        .ToList();

                    if (linkedPRs.Any())
                    {
                        _context.Prs.RemoveRange(linkedPRs);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            else
            {
                var pr = _context.Prs.FirstOrDefault(x => x.Id == id);
                formName = pr.Prname;
            }

            if(boms.Count() > 0)
            {
                boms.ForEach(x =>
                {
                    x.LastUpdatedDate = DateTime.Now;
                    x.Status = status;
                });
                _context.UpdateRange(boms);
                _context.SaveChanges();
                return Ok();
            }

            await _auditTrailService.InsertLogAsync(
                      Common.LogLevel.Information, formName + " has been reverse to status "+status,
                      tableChange: nameof(Bom),
                      empId: int.Parse(HttpContext.GetUserId()),
                      ProjectId: boms[0].ProjectId
                      );

            return Ok();
        }

        [HttpPost]
        public JsonResult addNewForm(Bom model, int projectId)
        {
            try
            {
                if (model == null)
                    throw new NullReferenceException(nameof(model));

                var BOM = new Bom
                {
                    Status = model.Status,
                    CreatedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    AddedBy = "sys",
                    ProjectId = projectId
                };

                _context.Boms.Add(BOM);

                if (_context.SaveChanges() > 0)
                {
                    //sendEmail(formName);

                }

                return Json(new { existed = false, msg = "", id = BOM.Id });
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public bool RFQNameExists(string RFQName, int projectId)
        {
            return _context.Rfqs.Where(x => x.Rfqname == RFQName && x.ProjectId == projectId).Any();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> Submit(SubmitRFQModel form)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[PERF] Submit started at {DateTime.Now:HH:mm:ss.fff}");
            
            if (form.Id > 0)
            {
                Console.WriteLine($"[PERF] Loading RFQ data... {sw.ElapsedMilliseconds}ms");
                var newForm = _context.Rfqs.FirstOrDefault(x => x.Id == form.Id);
                var boms = _context.Boms.Include(x => x.BomSupportFiles).Where(x => x.RFQId == newForm.Id).ToList();
                var bom = boms.Where(x => x.Id == form.BomId).FirstOrDefault();
                var project = _context.Projects.Where(d => d.Id == newForm.ProjectId).FirstOrDefault();
                Console.WriteLine($"[PERF] Data loaded in {sw.ElapsedMilliseconds}ms");
                //get latest remark
                if (!string.IsNullOrEmpty(form.DataResponse))
                {
                    RFQRoot rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(form.DataResponse);

                    var remark = string.Join("<br/>", rfqRoot.data.supplierList
                                        .Where(x => x.supplierKey != null && !string.IsNullOrEmpty(x.supplierKey.name) && !string.IsNullOrEmpty(x.remark))
                                        .Select(x => "<b>" + x.supplierKey.name + "</b>:" + x.remark).ToArray());

                    newForm.Remark = remark;
                }

                //newForm.LastUpdatedDate = DateTime.Now;
                if (!string.IsNullOrEmpty(form.DataResponse)) newForm.DataResponse = form.DataResponse;

                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(newForm.DataResponse);

                var newAddItems = root.data.bomGrid.Where(x => !x.itemLineNo.HasValue && !string.IsNullOrEmpty(x.description)).ToList();
                root.data.bomGrid.RemoveAll(x => !x.itemLineNo.HasValue);

                //update bom data
                var checkFabricationDrawing = false;
                var checkIsFabrication = false;
                foreach (var existBom in root.data.bomGrid)
                {
                    if (boms.Any(x => x.Id == existBom.itemLineNo))
                    {
                        var upBom = boms.FirstOrDefault(x => x.Id == existBom.itemLineNo);


                        if (upBom.ItemCategory == "Fabrication")
                        {
                            checkIsFabrication = true;
                            if (!checkFabricationDrawing && upBom.BomSupportFiles.Any(x => x.Type == "IC"))
                            {
                                checkFabricationDrawing = true;
                            }
                        }

                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).Brand = existBom.brand;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).PartDesc = existBom.description;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).PartNumber = existBom.partNo;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).Module = existBom.module;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).RequireQty = existBom.totalQuantity;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).RequireDate = existBom.requireDate;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).LeadTime = existBom.leadTime;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).ETA = existBom.eta;
                        boms.FirstOrDefault(x => x.Id == existBom.itemLineNo).UnitPrice = existBom.unitPrice;

                        //if (existBom.unitPrice <= 0)
                        //{
                        //    return Json(new { result = false, msg = "Unit price must no less or equal to 0!" });
                        //}
                    }
                }

                for (int i = 0; i < newAddItems.Count(); i++)
                {
                    var newBomItem = new Bom
                    {
                        BomNo = project.Code + "-" + CommonHelpers.generateUID(),
                        Module = newAddItems[i].module,
                        PartDesc = newAddItems[i].description,
                        Brand = newAddItems[i].brand,
                        PartNumber = newAddItems[i].partNo,
                        ItemCategory = boms.FirstOrDefault().ItemCategory,
                        CreatedDate = DateTime.Now,
                        RequireQty = newAddItems[i].totalQuantity,
                        LastUpdatedDate = DateTime.Now,
                        Status = boms.FirstOrDefault().Status,
                        ProjectId = boms.FirstOrDefault().ProjectId,
                        RFQId = form.Id,
                        UnitPrice = newAddItems[i].unitPrice
                    };
                    _context.Boms.Add(newBomItem);
                    _context.SaveChanges();
                    newAddItems[i].itemLineNo = newBomItem.Id;
                    root.data.bomGrid.Add(newAddItems[i]);
                    boms.Add(newBomItem);
                }

                var unmergeList = new List<Bom>();
                foreach (var bomItem in boms)
                {
                    if (!root.data.bomGrid.Any(x => x.itemLineNo.HasValue && x.itemLineNo == bomItem.Id))
                    {
                        unmergeList.Add(bomItem);
                    }
                }

                foreach (var unmergeItem in unmergeList)
                {
                    boms.Remove(unmergeItem);
                    unmergeItem.RFQId = null;
                    unmergeItem.Status = "New";
                    _context.Boms.Update(unmergeItem);
                }
                _context.SaveChanges();

                root.data.updateDate = DateTime.Now;
                newForm.LastUpdatedDate = DateTime.Now;

                var currentRequestor = "";
                int? picId = null;

                if (HttpContext != null && !string.IsNullOrEmpty(HttpContext.GetShortName()))
                {
                    currentRequestor = HttpContext.GetShortName() + "(" + HttpContext.GetUserDept() + ")";
                }
                else
                {     
                    if (!string.IsNullOrEmpty(project.PICS))
                    {
                        var firstPic = project.PICS.Split(',').FirstOrDefault();
                        if (int.TryParse(firstPic, out int parsed))
                        {
                            picId = parsed;
                        }
                    }

                    if (picId.HasValue)
                    {
                        var user = _context.EmployeeInfos
                            .Include(x => x.EmpDepartment)
                            .FirstOrDefault(x => x.Id == picId.Value);

                        if (user != null)
                        {
                            currentRequestor = user.ShortName + " (" + user.EmpDepartment.Name + ")";
                        }
                    }
                }


                if (!string.IsNullOrEmpty(root.data.header.FirstOrDefault().requester) &&
                    !string.Equals(root.data.header.FirstOrDefault().requester, currentRequestor))
                {
                    var requestorWithDepartment = root.data.header.FirstOrDefault().requester.Split('(');
                    var requestorUpdated = _context.EmployeeInfos
                        .Include(x => x.EmpDepartment)
                        .Where(x => x.ShortName == requestorWithDepartment[0] && x.EmpDepartment.Name == requestorWithDepartment[1].TrimEnd(')'))
                        .FirstOrDefault();
                    if (requestorUpdated != null)
                        newForm.RequestorId = requestorUpdated.Id;
                }

                var rfqSuppliers = root.data.supplierList.Where(x => !string.IsNullOrEmpty(x.quotNoLink) && x.quotation != null
                && x.quotation.Count > 0).ToList();

                foreach (var sp in rfqSuppliers)
                {
                    var sa = _context.SupplierAttachment.Where(x => x.RFQId == form.Id && x.QuotNo == sp.quotNoLink).FirstOrDefault();

                    if (sa != null)
                    {
                        sa.Remark = sp.remark;
                        sa.R_Remark = sp.r_remark;
                        sa.IsRecommended = sp.recommended.HasValue ? sp.recommended.Value : sa.IsRecommended;

                        _context.SupplierAttachment.Update(sa);
                        _context.SaveChanges();
                    }
                }

                var supplierAttachs = _context.SupplierAttachment.Where(x => x.RFQId == form.Id && x.IsRecommended).ToList();

                var bomStatus = bom.Status;

                if (CommonHelpers.availableBaseStatus("RFQ Sent", bomStatus) && !supplierAttachs.Any())
                {
                    return Json(new { result = false, msg = "Detect no any quotation was upload!" });
                }

                if (bomStatus == "New" || bomStatus == "PR Reject" || bomStatus == "PO Reject" || bomStatus.Contains("Cancelled"))
                {
                    // if (!checkFabricationDrawing && checkIsFabrication)
                    // {
                    //     return Json(new { result = false, msg = "Detect particular fabrication part item missing drawing. Please upload drawing and refresh the page" });
                    // }
                    // else
                    // {
                        var emailList = _context.EmployeeInfos.Where(x => x.UserType == "Buyer" && !string.IsNullOrEmpty(x.Email) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                        var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Sent, Url.Action("Edit", new { id = newForm.Id }));
                        newForm.Status = "RFQ Sent";
                        boms.ForEach(x => x.Status = "RFQ Sent");
                        
                        // Send email asynchronously without blocking the response
                        _ = Task.Run(async () => 
                        {
                            try
                            {
                                await _emailService.Send(emailList, "New RFQ with name has been raise by requestor!", html);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Email send error: {ex.Message}");
                            }
                        });

                        double totalPrice = root.data.bomGrid
                           .Where(x => x.totalPrice.HasValue && !double.IsNaN(x.totalPrice.Value))
                           .Sum(x => x.totalPrice.Value);

                        Console.WriteLine($"[PERF] Before AutoApprove: {sw.ElapsedMilliseconds}ms");
                        await AutoApprove(form.Id, totalPrice, form.DataResponse, rfqSuppliers.Count);
                        Console.WriteLine($"[PERF] After AutoApprove: {sw.ElapsedMilliseconds}ms");
                    // }

                }
                else if (bomStatus == "RFQ Sent" || bomStatus == "RFQ Reject")
                {
                    if (!root.data.supplierList.Any(x => x.recommended.HasValue && x.recommended == true))
                    {
                        return Json(new { result = false, msg = "Please recommended at least one supplier" });
                    }

                    var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();

                    var emailList = pics.Count() == 0
                        ? null 
                        : _context.EmployeeInfos
                        .Where(x => !string.IsNullOrEmpty(x.Email) && pics.Contains(x.Id) && x.IsNotificationOn)
                        .Select(x => x.Email)
                        .Distinct()
                        .ToList();
                    var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Updated, Url.Action("Edit", new { id = newForm.Id }));
                    
                    // Send email asynchronously without blocking the response
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await _emailService.Send(emailList, "New RFQ with name has been update by buyer!", html);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Email send error: {ex.Message}");
                        }
                    });

                    int buyerId;
                    if (HttpContext != null && !string.IsNullOrEmpty(HttpContext.GetUserId()))
                    {
                        buyerId = int.Parse(HttpContext.GetUserId());
                    }
                    else if (picId != null)
                    {
                        buyerId = picId ?? 0;
                    }
                    else
                    {
                        buyerId = 0;
                    }

                    newForm.BuyerId = buyerId;
                    newForm.Status = "RFQ Updated";
                    newForm.BuyerLastSubmittedDate = DateTime.Now;
                    boms.ForEach(x => x.Status = "RFQ Updated");

                    // Remark depending on submission type
                    if (form.IsAutoSubmit)
                    {
                        var vendor = string.IsNullOrEmpty(form.VendorName) ? "Vendor" : form.VendorName;
                        newForm.LatestSupplierRemark = $"<{vendor}, system submitted the quotation>";
                    }
                    else
                    {
                        // Remark: PIC submitted manually
                        var submitterName = HttpContext != null && !string.IsNullOrEmpty(HttpContext.GetShortName())
                            ? HttpContext.GetShortName()
                            : currentRequestor;
                        newForm.LatestSupplierRemark = $"{submitterName} submitted the Quotation";
                    }

                    double totalPrice = root.data.bomGrid
                        .Where(x => x.totalPrice.HasValue && !double.IsNaN(x.totalPrice.Value))
                        .Sum(x => x.totalPrice.Value);

                    await AutoApprove(form.Id, totalPrice, form.DataResponse, rfqSuppliers.Count);
                }
                else if (bomStatus == "RFQ Updated")
                {
                    newForm.Status = "RFQ Approved";
                    boms.ForEach(x => x.Status = "RFQ Approved");
                }

                boms.ForEach(x => x.LastUpdatedDate = DateTime.Now);

                root.data.bomGrid.ForEach(x =>
                {
                    if (x.totalPrice.HasValue && Double.IsNaN(x.totalPrice.Value))
                    {
                        x.totalPrice = 0;
                    }
                });

                newForm.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
                _context.Rfqs.Update(newForm);
                _context.Boms.UpdateRange(boms);

                if (await _context.SaveChangesAsync() > 0)
                {
                    var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
                    var emailList = pics.Count() == 0
                        ? null
                        : _context.EmployeeInfos
                        .Where(x => !string.IsNullOrEmpty(x.Email) && pics.Contains(x.Id) && x.IsNotificationOn)
                        .Select(x => x.Email)
                        .Distinct()
                        .ToList();
                    var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Updated, Url.Action("Edit", new { id = form.Id }));
                    var title = newForm.Rfqname + " Status Updated";
                    
                    // Send email asynchronously without blocking the response
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await _emailService.Send(emailList, title, html);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Email send error: {ex.Message}");
                        }
                    });

                    var pics2 = newForm.RequestorId.HasValue 
                        ? new List<int> { newForm.RequestorId.Value } 
                        : new List<int>();

                    int empId;

                    if (HttpContext != null && !string.IsNullOrEmpty(HttpContext.GetUserId()))
                    {
                        empId = int.Parse(HttpContext.GetUserId());
                    }
                    else if (picId != null)
                    {
                        empId = picId ?? 0;
                    }
                    else
                    {
                        empId = 0; 
                    }

                    Console.WriteLine($"[PERF] Before audit trail: {sw.ElapsedMilliseconds}ms");
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, title,
                        pics: pics2,
                        fullMessage: msg,
                        tableChange: nameof(Rfq),
                        empId: empId,
                        ProjectId: newForm.Project.Id
                        );
                    Console.WriteLine($"[PERF] After audit trail: {sw.ElapsedMilliseconds}ms");

                    Console.WriteLine($"[PERF] Returning response: {sw.ElapsedMilliseconds}ms");
                    return Json(new { result = true, id = project.Id });
                };
            }

            Console.WriteLine($"[PERF] Final return: {sw.ElapsedMilliseconds}ms");
            return Json(new { result = true, id = 0 });
        }

        public async Task AutoApprove(int rfqId, double price, string form, int supplierCount)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[PERF-AUTO] AutoApprove started for RFQ {rfqId}");
            
            try
            {
                Console.WriteLine($"[PERF-AUTO] Checking IsCopied... {sw.ElapsedMilliseconds}ms");
                var hasCopiedItems = await _context.Boms
                    .Where(x => x.RFQId == rfqId && x.IsCopied == true)
                    .AnyAsync();
                Console.WriteLine($"[PERF-AUTO] IsCopied check done: {hasCopiedItems}, {sw.ElapsedMilliseconds}ms");

                if (hasCopiedItems)
                {
                    Console.WriteLine($"Skipping auto-approval for RFQ {rfqId} - contains copied BOM items");
                    return;
                }

                Console.WriteLine($"[PERF-AUTO] Loading sourcing list... {sw.ElapsedMilliseconds}ms");
                var sourcingList = await (
                    from s in _context.Sourcing
                    join rq in _context.RFQSharing on s.Id equals rq.SourceId
                    join v in _context.SupplierInfo on rq.SupplierId equals v.Id
                    join r in _context.Rfqs on s.RFQId equals r.Id
                    where s.RFQId == rfqId
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
                Console.WriteLine($"[PERF-AUTO] Sourcing list loaded: {sourcingList.Count} items, {sw.ElapsedMilliseconds}ms");

                var additionalSupplierNames = await _context.AdditionalSuppliers
                    .Select(x => x.VendorName)
                    .ToListAsync();

                bool hasAdditionalSupplier = sourcingList.Any(x =>
                additionalSupplierNames.Any(n => n.Trim().ToLower() == x.VendorName.Trim().ToLower())
                );

                if (sourcingList == null)
                    return;

                if (
                    (sourcingList.Count == 1 && !hasAdditionalSupplier && price <= 5000 && price > 0) ||
                    (sourcingList.Count == 1 && hasAdditionalSupplier && price <= 1000 && price > 0) ||
                    (sourcingList.Count == 0 && supplierCount == 1 && price <= 500 && price > 0)       
                    )
                {
                    if (rfqId > 0)
                    {
                        Console.WriteLine($"[PERF-AUTO] Calling approveRFQ... {sw.ElapsedMilliseconds}ms");
                        var newForm = _context.Rfqs.FirstOrDefault(x => x.Id == rfqId);
                        await approveRFQ(form, rfqId);
                        Console.WriteLine($"[PERF-AUTO] approveRFQ completed: {sw.ElapsedMilliseconds}ms");
                    }
                }

                Console.WriteLine($"[PERF-AUTO] AutoApprove completed: {sw.ElapsedMilliseconds}ms");
                return;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error auto approve: {ex.Message}");
            }

        }

        [HttpPost]
        public async Task<IActionResult> approveRFQ(string form, int id)
        {
            if (id > 0)
            {
                var newForm = _context.Rfqs.FirstOrDefault(x => x.Id == id);

                if (isProjectCompleted(newForm.ProjectId))
                    return Forbid();

                var boms = _context.Boms
                    .Include(x => x.BomSupportFiles)
                    .ThenInclude(x => x.SupportFile)
                    .Where(x => x.RFQId == id)
                    .ToList();

                boms.ForEach(x => {
                    x.Status = "RFQ Approved";
                    x.LastUpdatedDate = DateTime.Now;
                });
                
                _context.Boms.UpdateRange(boms);

                newForm.LastUpdatedDate = DateTime.Now;
                newForm.AddedBy = "sys";
                newForm.Status = "RFQ Approved";

                if (!string.IsNullOrEmpty(form))
                {
                    await CreateSourcingRecordsForApprovedRFQ(id, form);
                }

                if (form != null) newForm.DataResponse = form;

                _context.Rfqs.Update(newForm);

                if (boms.FirstOrDefault().PRId.HasValue)
                {
                    var checkPO = _context.Pos.FirstOrDefault(x => x.Prid == boms.FirstOrDefault().PRId);
                    if(checkPO != null)
                    {
                        _context.Pos.Remove(checkPO);
                        _context.SaveChanges();
                    }

                    var pr = _context.Prs.FirstOrDefault(x => x.Id == boms.FirstOrDefault().PRId);
                    _context.Prs.Remove(pr);
                }

                var project = _context.Projects.Where(x => x.Id == newForm.ProjectId).FirstOrDefault();
                if (String.IsNullOrEmpty(project.PoNumber)) //not allow create PR if no PO. Temporary check with Project PO, will check by category later.
                {
                    _context.SaveChanges();
                    return RedirectToAction("Index", "BOM", new { id = newForm.ProjectId });
                }

                await _context.SaveChangesAsync();

                if (await _context.SaveChangesAsync() > 0)
                {
                    var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').ToList() : null;
                    var emailList = pics == null
                        ? pics
                        : _context.EmployeeInfos
                        .Where(x => !string.IsNullOrEmpty(x.Email) && pics.Contains(x.ShortName) && x.IsNotificationOn)
                        .Select(x => x.Email)
                        .ToList();
                    var title = newForm.Rfqname + " has been approved";
                    var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Approved, Url.Action("Edit", new { id = id }));
                    await _emailService.Send(emailList, title, html);
                    var pics2 = new List<int> { newForm.RequestorId.Value };

                    int empId;

                    int? picId = null;

                        if (!string.IsNullOrEmpty(project.PICS))
                        {
                            var firstPic = project.PICS.Split(',').FirstOrDefault();
                            if (int.TryParse(firstPic, out int parsed))
                            {
                                picId = parsed;
                            }
                        }
                    
                    if (HttpContext != null && !string.IsNullOrEmpty(HttpContext.GetUserId()))
                    {
                        empId = int.Parse(HttpContext.GetUserId());
                    }
                    else if (picId != null)
                    {
                        empId = picId ?? 0;
                    }
                    else
                    {
                        empId = 0;
                    }

                    //record
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, title,
                        pics:pics2,
                        fullMessage: msg,
                        tableChange: nameof(Rfq),
                        empId: empId,
                        ProjectId: newForm.Project.Id
                        );
                }

                return RedirectToAction("Index", "BOM", new { id = newForm.ProjectId });
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> rejectRFQ(int id, string remark)
        {
            if (id > 0)
            {
                var newForm = _context.Rfqs.FirstOrDefault(x => x.Id == id);

                if (isProjectCompleted(newForm.ProjectId))
                    return Forbid();

                var boms = _context.Boms.Where(x => x.RFQId == newForm.Id).ToList();
                var project = _context.Projects.FirstOrDefault(x => x.Id == newForm.ProjectId);
                boms.ForEach(x => x.Status = "RFQ Reject");
                //bom.LastUpdatedDate = DateTime.Now;
                _context.Boms.UpdateRange(boms);

                //newForm.LastUpdatedDate = DateTime.Now;
                newForm.AddedBy = "sys";
                newForm.Status = "Pending Submit RFQ";
                newForm.Remark = !string.IsNullOrEmpty(remark) ? remark : string.Empty;
                //newForm.LatestSupplierRemark = !string.IsNullOrEmpty(remark) ? remark : string.Empty;

                _context.Rfqs.Update(newForm);

                if (await _context.SaveChangesAsync() > 0)
                {
                    var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
                    var emailList = pics.Count() == 0
                        ? null
                        : _context.EmployeeInfos
                        .Where(x => !string.IsNullOrEmpty(x.Email) && pics.Contains(x.Id) && x.IsNotificationOn)
                        .Select(x => x.Email)
                        .Distinct()
                        .ToList();
                    var (msg, html) = CommonHelpers.MailTemplateGenerator("RFQ", BOMStatus.Rejected, Url.Action("Edit", new { id = newForm.Id }));
                    var title = newForm.Rfqname + " has been rejected by PIC";
                    await _emailService.Send(emailList, title, html);
                    var pics2 = _context.EmployeeInfos.Where(x => x.UserType == "Buyer" && !string.IsNullOrEmpty(x.Email) && x.IsNotificationOn).Select(x => x.Id)
                        .ToList();
                    //record
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, title,
                        pics:pics2,
                        fullMessage: msg,
                        tableChange: nameof(Rfq),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: newForm.Project.Id
                        );
                };

                return RedirectToAction("Index", "BOM", new { id = newForm.ProjectId });
            }

            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public async Task<IActionResult> ExportToPDF(int id)
        {
            var url = $@"{HttpContext.Request.Scheme}:/{HttpContext.Request.Host}{Url.Action("View", new { id = id })}";

            var rfq = await _context.Rfqs.FirstOrDefaultAsync(x => x.Id == id);

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true }))
            {
                var page = await browser.NewPageAsync();
                var response = await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);
                return File(await page.PdfDataAsync(new PdfOptions
                {
                    Format = new PaperFormat(17m, 15.5m),
                    Scale = (decimal)0.70,
                    Landscape = true
                }), "application/pdf", rfq.FormName + ".pdf");
            }
        }

        [AllowAnonymous]
        public IActionResult View(int id)
        {
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == id);

            if (rfq != null && !string.IsNullOrEmpty(rfq.DataResponse))
            {
                var jsonObj = JObject.Parse(rfq.DataResponse);
                if (jsonObj["data"] != null)
                {
                    jsonObj["data"]["LatestSupplierRemark"] = rfq.LatestSupplierRemark;
                    rfq.DataResponse = jsonObj.ToString();
                }
            }

            return View(rfq);
        }

        protected bool isProjectCompleted(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);

            return project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult ExportToExcel(int id)
        {
            var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == id);
            var supplierQuery = _context.SupplierInfo.AsQueryable();

            if (rfq.DataResponse != null)
            {
                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                var currentRow = 1;

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("RFQ");

                    //rfq info
                    worksheet.Cell(currentRow, 1).Value = "RFQ Name";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    worksheet.Cell(currentRow, 2).Value = rfq.Rfqname;

                    currentRow += 2;

                    //basic info
                    var info = typeof(RFQBasicInfo).GetProperties();
                    var infoData = root.data;
                    for (int i = 1; i <= info.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = CommonHelpers.ConvertColumnName(info[i - 1].Name.FirstCharToUpper().ConvertEnum());
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (infoData.GetType().GetProperty(info[i -1].Name) == null)
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
                    var header = typeof(RFQHeader).GetProperties();
                    var headerData = root.data.header.FirstOrDefault();
                    for (int i = 1; i <= header.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = CommonHelpers.ConvertColumnName(header[i - 1].Name.FirstCharToUpper().ConvertEnum());
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                        if (headerData != null)
                            worksheet.Cell(currentRow + 1, i).Value = headerData.GetType().GetProperty(header[i - 1].Name).GetValue(headerData, null);
                    }

                    //boms
                    currentRow += 3;
                    var bomTitle = typeof(BOMGridFixProperties).GetProperties();
                    var bomItems = root.data.bomGrid.ToList();
                    for (int i = 1; i <= bomTitle.Count(); i++)
                    {
                        if (bomTitle[i - 1].Name == "totalPrice")
                        {
                            var currency = bomItems.Select(x => x.currency).FirstOrDefault();
                            worksheet.Cell(currentRow, i).Value = bomTitle[i - 1].Name.FirstCharToUpper().ConvertEnum() + "("+ currency + ")";
                            worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                            worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                        }
                        else if (bomTitle[i - 1] == typeof(BOMGrid).GetProperty("selectedSupplier"))
                        {
                            continue;
                           
                        }
                        else
                        {
                            worksheet.Cell(currentRow, i).Value = bomTitle[i - 1].Name.FirstCharToUpper().ConvertEnum();
                            worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                            worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                        };
                    }

                    
                    foreach (var b in bomItems)
                    {
                        var currency = b.currency;
                        currentRow += 1;
                        int countIC = 0;

                        for (int i = 1; i <= bomTitle.Count(); i++)
                        {

                            if (bomTitle[i - 1] == typeof(BOMGridFixProperties).GetProperty("partNo"))
                            {
                                var bomId = (int)b.GetType().GetProperty("itemLineNo").GetValue(b);
                                var bom = _context.Boms
                                                .Include(x => x.BomSupportFiles)
                                                .Where(x => x.Id == bomId)
                                                .FirstOrDefault();
                                if (bom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                                {
                                    foreach(var bomAttach in bom.BomSupportFiles.Where(x => x.Type == "IC").ToList())
                                    {
                                        worksheet.Cell(currentRow, i).Value = Url.Action("downloadFile", "BOM", new { id = bomAttach.SupportFileId }, HttpContext.Request.Scheme);
                                        currentRow++;
                                        countIC++;
                                        continue;
                                    }
                                   
                                }
                                else
                                {
                                    worksheet.Cell(currentRow, i).Value = b.GetType().GetProperty(bomTitle[i - 1].Name).GetValue(b);
                                }
                            }
                            else
                            {
                                worksheet.Cell(currentRow - countIC, i).Value = b.GetType().GetProperty(bomTitle[i - 1].Name).GetValue(b);
                            }

                           
                            //else if (bomTitle[i - 1] == typeof(BOMGrid).GetProperty("totalPrice"))
                            //{
                            //    worksheet.Cell(currentRow, i).Value = currency + " " + b.totalPrice;
                            //}
                            //else
                            //{
                            //    worksheet.Cell(currentRow, i).Value = b.GetType().GetProperty(bomTitle[i - 1].Name).GetValue(b);
                            //}
                        }
                    }

                    //suppliers

                    currentRow += 2;
                    var suppliersProperties = typeof(SupplierListExportModel).GetProperties();
                    var supplierList = new List<SupplierListExportModel>();
                    if (root.data.supplierList.Count() > 0)
                    {
                        foreach (var supplier in root.data.supplierList)
                        {
                            if(supplier != null)
                            {
                                supplierList.Add(new SupplierListExportModel
                                {
                                    supplierName = supplier.supplierKey != null ? supplier.supplierKey.name : string.Empty,
                                    Quotation = supplier.quotation != null && supplier.quotation.Count() > 0
                               ? supplier.quotation.FirstOrDefault().url
                               : "",
                                    Remark = supplier.remark
                                });
                            }
                           
                        }
                    }
                    for (int i = 1; i <= suppliersProperties.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = suppliersProperties[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    }
                    foreach (var s in supplierList)
                    {
                        currentRow += 1;
                        for (int i = 1; i <= suppliersProperties.Count(); i++)
                        {
                            worksheet.Cell(currentRow, i).Value = s.GetType().GetProperty(suppliersProperties[i - 1].Name).GetValue(s, null);
                        }
                    }
                    //var rfqData = root.data.gridQuotation.FirstOrDefault();
                    //for (int i = 1; i <= rfqProperties.Count(); i++)
                    //{
                    //    worksheet.Cell(currentRow, i).Value = rfqProperties[i - 1].Name.FirstCharToUpper().ConvertEnum();
                    //    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    //    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                    //    if (rfqData != null)
                    //        worksheet.Cell(currentRow + 1, i).Value = rfqData.GetType().GetProperty(rfqProperties[i - 1].Name).GetValue(rfqData, null);
                    //}

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();

                        return File(
                            content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            rfq.Rfqname+".xlsx");
                    }
                }
            }

            return NoContent();
        }

        private string generateQuotationLinks(ICollection<SupplierAttachment> supplierAttachments)
        {
            var links = string.Empty;
            foreach (var supplierAttachment in supplierAttachments)
            {
                links += Url.Action("downloadFile", "BOM", new { id = supplierAttachment.SupportFileId }, HttpContext.Request.Scheme) + "\n";
            }

            return links;
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

                var url = Url.Action("downloadFile","BOM",new {id = supportFile.Id});

                return url;
            }
            else
            {
                return Url.Action("ShowPDF", "BOM", new { supportFileId = supportFile.Id }, HttpContext.Request.Scheme);
            }
        }

        [HttpPost]
        public async Task<JsonResult> ImportRFQ()
        {
            var postedFile = Request.Form.Files["FileUpload"];
            var rfqId = Request.Form["Id"];
            var rfq = await _context.Rfqs.FirstOrDefaultAsync(x => x.Id == int.Parse(rfqId));
            var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);

            try
            {
                if (postedFile != null)
                {
                    var FileDic = "UploadedRFQExcel";
                    string FilePath = Path.Combine(_env.WebRootPath + "/" + FileDic);

                    
                    if (!Directory.Exists(FilePath))
                        Directory.CreateDirectory(FilePath);
                    var filename = Guid.NewGuid().ToString() + postedFile.FileName;
                    var filePath = Path.Combine(FilePath, filename);

                    using (FileStream stream = new FileStream(filePath, FileMode.Create))
                    {
                        postedFile.CopyTo(stream);
                    }

                    XSSFWorkbook workbook;
                    using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        workbook = new XSSFWorkbook(file);
                    }

                    ISheet sheet = workbook.GetSheet("RFQ");
                    var allowedLeadTimes = new HashSet<int> { 1, 3, 5, 7, 10, 14, 21, 30, 45, 60, 90 };
                    var validationErrors = new List<string>();
                    var rowCount = 0;

                    for (int row = 1; row <= sheet.LastRowNum; row++)
                    {
                        var currentRow = sheet.GetRow(row);
                        if (currentRow == null || currentRow.GetCell(0) == null || string.IsNullOrWhiteSpace(currentRow.GetCell(0).ToString()))
                            continue;

                        var bomNo = currentRow.GetCell(0)?.ToString();
                        if (string.IsNullOrWhiteSpace(bomNo)) continue;

                        var bom = rfqRoot.data.bomGrid.FirstOrDefault(x => x.bomNo == bomNo);
                        if (bom != null)
                        {
                            rowCount++;
                            
                            bom.description = currentRow.GetCell(1)?.ToString();      
                            bom.brand = currentRow.GetCell(2)?.ToString();            
                            bom.partNo = currentRow.GetCell(3)?.ToString();          
                            bom.currency = currentRow.GetCell(5)?.ToString();         
                            bom.unitPrice = ParseDoubleWithDefault(currentRow.GetCell(6)?.ToString(), 0);   
                            bom.discount = ParseDoubleWithDefault(currentRow.GetCell(7)?.ToString(), 0);    
                            bom.tax = ParseDoubleWithDefault(currentRow.GetCell(8)?.ToString(), 0);         
                            bom.shippingCost = ParseDoubleWithDefault(currentRow.GetCell(9)?.ToString(), 0); 
                            bom.bankFee = ParseDoubleWithDefault(currentRow.GetCell(10)?.ToString(), 0);    


                         
                            var leadTimeCell = currentRow.GetCell(11);               

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
                                        else
                                        {
                                            validationErrors.Add($"Row {row + 1}: Lead Time '{leadTimeDays}' is not allowed. Please use: 1,3,5,7,10,14,21,30,45,60,90 days.");
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

                                           
                                            if (!validationErrors.Any(e => e.Contains("old template format")))
                                            {
                                                validationErrors.Add("Detected old template format with dates. Please use new template with Lead Time (days) dropdown.");
                                            }
                                        }
                                        else
                                        {
                                            validationErrors.Add($"Row {row + 1}: Invalid Lead Time value '{leadTimeStr}'. Please enter a number (1,3,5,7,10,14,21,30,45,60,90).");
                                        }
                                    }
                                }
                                else
                                {
                                    bom.leadTime = null;
                                    bom.eta = null;
                                }
                            }
                            



                            bom.quotationLineNo = currentRow.GetCell(12)?.ToString();
                        }
                    }

                    if (validationErrors.Any())
                    {
                        return Json(new { result = false, msg = string.Join("<br/>", validationErrors) });
                    }

                    if (rowCount == 0)
                    {
                        return Json(new { result = false, msg = "No valid data found in the uploaded file." });
                    }

                    rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
                    _context.Update(rfq);

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception e)
                    {
                        return Json(new { result = false, msg = "Database error: " + e.Message });
                    }
                    finally
                    {
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }

                    return Json(new { result = true, msg = $"Import completed successfully! Processed {rowCount} items." });
                }
                return Json(new { result = false, msg = "No file selected." });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = "An error occurred during import. Please try again. Error: " + ex.Message });
            }
        }

        
        private double ParseDoubleWithDefault(string value, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;

           
            value = value.Replace("RM", "").Replace("$", "").Replace(",", "").Trim();

            if (double.TryParse(value, out double result))
                return result;

            return defaultValue;
        }

        [HttpGet]
        public async Task<IActionResult> DownloadRFQTemplate(int id)
        {
            var rfq = await _context.Rfqs.FirstOrDefaultAsync(x => x.Id == id);
            if (rfq == null) return NotFound();

            var rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
            var bomList = rfqRoot.data.bomGrid;

            string templatePath = Path.Combine(_env.WebRootPath, "BOMTemplate", "ImportRFQTemplate.xlsx");

            using (var fs = new FileStream(templatePath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);
                workbook.RemoveSheetAt(0);
                ISheet sheet = workbook.CreateSheet("RFQ");
                workbook.SetSheetOrder("RFQ", 0);
                sheet.SetZoom(80, 100);

                // Get or create header row
                IRow headerRow = sheet.GetRow(0) ?? sheet.CreateRow(0);

                // Define headers
                string[] headers = {
            "Bom No", "Description", "Brand", "Part No",
            "Quantity",
            "Currency", "Unit Price", "Discount", "Tax",
            "Shipping Cost", "Bank Fee", "Lead Time (days)", "Item Line No"
        };

                
                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = headerRow.GetCell(col) ?? headerRow.CreateCell(col);
                    cell.SetCellValue(headers[col]);


                    var headerStyle = workbook.CreateCellStyle();
                    headerStyle.FillPattern = FillPattern.SolidForeground;
                    headerStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
                    headerStyle.BorderBottom = BorderStyle.Thin;
                    headerStyle.BorderTop = BorderStyle.Thin;
                    headerStyle.BorderLeft = BorderStyle.Thin;
                    headerStyle.BorderRight = BorderStyle.Thin;
                    headerStyle.Alignment = HorizontalAlignment.Center;
                    headerStyle.VerticalAlignment = VerticalAlignment.Center;

                    var font = workbook.CreateFont();
                    font.IsBold = true;
                    headerStyle.SetFont(font);

                    cell.CellStyle = headerStyle;
                }

              
                sheet.SetColumnWidth(0, 20 * 256);  
                sheet.SetColumnWidth(1, 40 * 256);  
                sheet.SetColumnWidth(2, 20 * 256);  
                sheet.SetColumnWidth(3, 20 * 256);  
                sheet.SetColumnWidth(4, 15 * 256);  
                sheet.SetColumnWidth(5, 12 * 256);  
                sheet.SetColumnWidth(6, 15 * 256);  
                sheet.SetColumnWidth(7, 12 * 256);  
                sheet.SetColumnWidth(8, 12 * 256);  
                sheet.SetColumnWidth(9, 18 * 256);  
                sheet.SetColumnWidth(10, 12 * 256); 
                sheet.SetColumnWidth(11, 18 * 256); 
                sheet.SetColumnWidth(12, 25 * 256); 

                
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

                
                ICellStyle numberStyle = workbook.CreateCellStyle();
                var numberFormat = workbook.CreateDataFormat();
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

                
                var rowCount = bomList.Count > 20 ? bomList.Count : 20;

                for (int i = 0; i < rowCount; i++)
                {
                    var row = sheet.GetRow(i + 1) ?? sheet.CreateRow(i + 1);
                    if (i < 20)
                    {
                        row.HeightInPoints = 20; 
                    }

                    
                    ICellStyle baseStyle = (i % 2 == 0) ? evenRowStyle : oddRowStyle;

                    
                    if (i < bomList.Count)
                    {
                        var bom = bomList[i];

                        
                        for (int col = 0; col < headers.Length; col++)
                        {
                            var cell = row.GetCell(col) ?? row.CreateCell(col);

                            
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

                        
                        (row.GetCell(0) ?? row.CreateCell(0)).SetCellValue(bom.bomNo ?? "");
                        (row.GetCell(1) ?? row.CreateCell(1)).SetCellValue(bom.description ?? "");
                        (row.GetCell(2) ?? row.CreateCell(2)).SetCellValue(bom.brand ?? "");
                        (row.GetCell(3) ?? row.CreateCell(3)).SetCellValue(bom.partNo ?? "");
                        (row.GetCell(4) ?? row.CreateCell(4)).SetCellValue(bom.totalQuantity);
                        (row.GetCell(5) ?? row.CreateCell(5)).SetCellValue(bom.currency ?? "");
                        (row.GetCell(6) ?? row.CreateCell(6)).SetCellValue(bom.unitPrice ?? 0);
                        (row.GetCell(7) ?? row.CreateCell(7)).SetCellValue(bom.discount ?? 0);
                        (row.GetCell(8) ?? row.CreateCell(8)).SetCellValue(bom.tax ?? 0);
                        (row.GetCell(9) ?? row.CreateCell(9)).SetCellValue(bom.shippingCost ?? 0);
                        (row.GetCell(10) ?? row.CreateCell(10)).SetCellValue(bom.bankFee ?? 0);

                        
                        var leadTimeCell = row.GetCell(11) ?? row.CreateCell(11);
                        if (bom.leadTime.HasValue && bom.leadTime.Value > 0)
                        {
                            leadTimeCell.SetCellValue(bom.leadTime.Value);
                        }
                        else
                        {
                            leadTimeCell.SetCellValue("");
                        }

                        
                        (row.GetCell(12) ?? row.CreateCell(12)).SetCellValue(bom.quotationLineNo ?? "");
                    }
                    else
                    {
                        
                        for (int col = 0; col < headers.Length; col++)
                        {
                            var cell = row.GetCell(col) ?? row.CreateCell(col);

                            
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

                            cell.SetCellValue("");
                        }
                    }
                }

                
                IDataValidationHelper dvHelper = sheet.GetDataValidationHelper();
                CellRangeAddressList addressList = new CellRangeAddressList(1, rowCount, 11, 11); // Column L

                IDataValidationConstraint dvConstraint = dvHelper.CreateExplicitListConstraint(
                    new string[] { "1", "3", "5", "7", "10", "14", "21", "30", "45", "60", "90" });

                IDataValidation validation = dvHelper.CreateValidation(dvConstraint, addressList);
                validation.ShowPromptBox = true;
                validation.CreatePromptBox("Lead Time", "Select lead time in days from the dropdown list");
                validation.CreateErrorBox("Invalid Value", "Please select a valid lead time from the dropdown: 1,3,5,7,10,14,21,30,45,60,90 days");

                sheet.AddValidationData(validation);

                CellRangeAddressList currencyAddressList = new CellRangeAddressList(1, rowCount, 5, 5);
                IDataValidationConstraint currencyConstraint = dvHelper.CreateExplicitListConstraint(
                    new string[] { "MYR", "USD", "SGD", "EURO", "RMB", "YEN" });
                IDataValidation currencyValidation = dvHelper.CreateValidation(currencyConstraint, currencyAddressList);
                currencyValidation.ShowErrorBox = true;
                sheet.AddValidationData(currencyValidation);

                using (var exportData = new MemoryStream())
                {
                    workbook.Write(exportData);
                    string filename = $"RFQ_Template_{rfq.Id}.xlsx";

                    return File(exportData.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        filename);
                }
            }
        }

        private async Task CreateSourcingRecordsForApprovedRFQ(int rfqId, string dataResponse)
        {
            try
            {
                
                if (_context.Sourcing.Any(x => x.RFQId == rfqId))
                    return;

                var rfq = await _context.Rfqs.FindAsync(rfqId);
                if (rfq == null || string.IsNullOrEmpty(dataResponse))
                    return;

                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(dataResponse);

                if (root?.data?.supplierList == null || !root.data.supplierList.Any())
                    return;

                
                var mainBrand = root.data.bomGrid.FirstOrDefault()?.brand ?? "General";

                
                var sourcing = new Sourcing
                {
                    Id = Guid.NewGuid(),
                    Brand = mainBrand,
                    RFQId = rfqId,
                    SupplierStandard = 1,
                    ConfirmAt = DateTime.Now
                };

                
                bool hasRecommendedSuppliers = false;

                
                foreach (var supplier in root.data.supplierList.Where(x => x.recommended == true))
                {
                    if (supplier.supplierKey != null && Guid.TryParse(supplier.supplierKey.id, out Guid supplierId))
                    {
                        hasRecommendedSuppliers = true;

                        
                        if (sourcing.SupplierId == null)
                        {
                            sourcing.SupplierId = supplierId;
                            
                            sourcing.FinalPrice = (decimal?)root.data.bomGrid
                                .Where(x => x.totalPrice.HasValue)
                                .Sum(x => x.totalPrice.Value);
                        }

                        var rfqSharing = new RFQSharing
                        {
                            SourceId = sourcing.Id,
                            SupplierId = supplierId,
                            DataResponse = dataResponse,
                            CreatedDate = DateTime.Now,
                            SubmittedDate = DateTime.Now
                        };

                        _context.RFQSharing.Add(rfqSharing);
                    }
                }

                
                if (hasRecommendedSuppliers)
                {
                    _context.Sourcing.Add(sourcing);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"Error creating sourcing records: {ex.Message}");
            }
        }


    }
}
