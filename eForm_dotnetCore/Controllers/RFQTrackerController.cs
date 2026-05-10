#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Services.Others.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using eForm_dotnetCore.ViewModel;
using eForm_dotnetCore.Mapper;
using System.Data;
using OfficeOpenXml;
using eForm_dotnetCore.ViewModels;
using eForm_dotnetCore.Services.AuditTrails;
using Microsoft.AspNetCore.Authorization;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Extensions;
using ClosedXML.Excel;
using static eForm_dotnetCore.ViewModels.RFQExportModel;
using MoreLinq;
using Microsoft.CodeAnalysis;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text.Json.Serialization;
using System.Text.Json;
using Hangfire.Storage;
using eForm_dotnetCore.Domain;
//using System.Web.Helpers;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;

namespace eForm_dotnetCore.Controllers
{
    public class RFQTrackerController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IAuthorizationService _authorizationService;
        private readonly IFileWriter _fileWriter;

        public RFQTrackerController(EformSophicDemoContext context,
            IWebHostEnvironment _environment,
            IAuditTrailService auditTrailService,
            IAuthorizationService authorizationService,
            IFileWriter fileWriter
            )
        {
            _context = context;
            Environment = _environment;
            _auditTrailService = auditTrailService;
            _authorizationService = authorizationService;
            _fileWriter = fileWriter;
        }

        public string generateViewURL(SupportFile supportFile)
        {
            if (CommonHelpers.isImage(supportFile.ContentType.ToLower()))
            {
                return Url.Action("PreviewImage", "BOM", new { id = supportFile.Id }, HttpContext.Request.Scheme);
            }
            else if(supportFile.ContentType.ToLower() == ".pdf")
            {
                return Url.Action("ShowPDF", "BOM", new { supportFileId = supportFile.Id }, HttpContext.Request.Scheme);
            }
            else
            {
                var url = Url.Action("downloadFile", "BOM", new { id = supportFile.Id });

                return url;
            }
        }

        public IActionResult History()
        {
            return View();
        }

        [HttpPost]
        public JsonResult LoadData(DataTableParameters parameters)
        {
            var query = _context.Boms
               .WhereRFQAvailableStatus2("RFQ Updated")
               .AsQueryable();

            var rfqQuery = _context.Rfqs.AsQueryable();

            if(!string.IsNullOrEmpty(parameters.Search.value))
            {
                var tempQ = query.Where(x => x.PartDesc.Contains(parameters.Search.value)).AsQueryable();

                if(tempQ.Count() > 0)
                {
                    query = query.Where(x => x.PartDesc.Contains(parameters.Search.value)).AsQueryable();
                }
                else
                {
                    rfqQuery = rfqQuery.Where(x => x.Rfqname.Contains(parameters.Search.value)).AsQueryable();
                    if(rfqQuery.Count() > 0)
                    {
                        query = query.Where(x => x.RFQId.HasValue && x.RFQId == rfqQuery.FirstOrDefault().Id).AsQueryable();
                    }
                }
            }

            var total = query.Count();

            var data = query
                 .Include(x => x.Project)
               .Include(x => x.BomSupportFiles)
               .ThenInclude(x => x.SupportFile)
               .Include(x => x.RFQ)
               .ThenInclude(x => x.Requestor)
                .Skip(parameters.Start).Take(parameters.Length).ToList();

            data.ForEach(x =>
            {
                if (x.ItemCategory == "Fabrication" && x.BomSupportFiles.Any(x => x.Type == "IC"))
                {
                    x.BomSupportFiles.ForEach(b => b.SupportFile.ContentType = generateViewURL(b.SupportFile));
                }
            });

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = data
            });
        }

        //[Authorize]
        //[ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        //public async Task<IActionResult> Index()
        //{
        //    var data = await _context.Boms
        //       .Include(x => x.Project)
        //       .Include(x => x.BomSupportFiles)
        //       .ThenInclude(x => x.SupportFile)
        //       .Include(x => x.RFQ)
        //       .ThenInclude(x => x.Requestor)
        //       .Where(x => x.Status == "RFQ Sent" && x.RFQId.HasValue && x.Project.Status != "Completed")
        //       .OrderByDescending(x => x.RFQ.LastUpdatedDate)
        //       .ToListAsync();

        //    var sourceQuery = _context.Sourcing.AsQueryable();
        //    data.ForEach(x =>
        //    {
        //        if (x.ItemCategory == "Fabrication" && x.BomSupportFiles.Any(x => x.Type == "IC"))
        //        {
        //            x.BomSupportFiles.ForEach(b => b.SupportFile.ContentType = generateViewURL(b.SupportFile));
        //        }

        //        if (sourceQuery.Any(s => s.RFQId == x.RFQId && s.Brand.ToLower() == x.Brand.ToLower()))
        //        {
        //            x.IsPOUpdated = true;
        //        }
        //        else
        //        {
        //            x.IsPOUpdated = false;
        //        }
        //    });

        //    return View(data);
        //}



        //[Authorize]
        //[ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        //public async Task<IActionResult> Index(string status)
        //{
        //    ViewBag.status = status;

        //    var sourcingQuery = _context.Sourcing.AsNoTracking().ToList();
        //    var rfqSharingQuery = _context.RFQSharing.AsNoTracking().ToList();

        //    var query = _context.Boms
        //    .Include(x => x.Project)
        //    .Include(x => x.BomSupportFiles)
        //        .ThenInclude(x => x.SupportFile)
        //    .Include(x => x.RFQ)
        //        .ThenInclude(x => x.Requestor)
        //    .Where(x => x.RFQId.HasValue && x.Project.Status != "Completed");

        //    if (status == "RFQ Sent" || status == null)
        //    {
        //        query = query.Where(x => x.Status == "RFQ Sent");
        //    }
        //    else
        //    {
        //        query = query.Where(x => x.Status == status);
        //    }

        //    var data = await query.OrderByDescending(x => x.RFQ.LastUpdatedDate).ToListAsync();

        //    data.ForEach(bom =>
        //    {
        //        if (bom.ItemCategory == "Fabrication" && bom.BomSupportFiles.Any(x => x.Type == "IC"))
        //        {
        //            bom.BomSupportFiles.ForEach(b => b.SupportFile.ContentType = generateViewURL(b.SupportFile));
        //        }

        //        var sourcing = sourcingQuery.FirstOrDefault(s => s.RFQId == bom.RFQId && s.Brand.ToLower() == bom.Brand.ToLower());
        //        bom.IsPOUpdated = sourcing != null;

        //        if (sourcing != null)
        //        {
        //            // Find all related RFQSharing records
        //            var relatedRfqSharing = rfqSharingQuery.Where(r => r.SourceId == sourcing.Id).ToList();

        //            if (relatedRfqSharing.Any())
        //            {
        //                // Store all the statuses in a list
        //                var statuses = relatedRfqSharing.Select(rfq =>
        //                    rfq.SubmittedDate.HasValue ? "Submitted" : "Processing"
        //                ).ToList();

        //                // Join the statuses into a single string for display
        //                bom.RFQSharingStatus = string.Join(", ", statuses);
        //            }
        //            else
        //            {
        //                bom.RFQSharingStatus = "Processing";
        //            }
        //        }
        //        else
        //        {
        //            bom.RFQSharingStatus = "N/A";
        //        }
        //    });

        //    return View(data);
        //}

        //public async Task<IActionResult> GetDataByStatus(string status)
        //{
        //    var data = await _context.Boms
        //       .Include(x => x.Project)
        //       .Include(x => x.BomSupportFiles)
        //       .ThenInclude(x => x.SupportFile)
        //       .Include(x => x.RFQ)
        //       .ThenInclude(x => x.Requestor)
        //       .Where(x => x.Status == status && x.RFQId.HasValue)
        //       .OrderByDescending(x => x.RFQ.LastUpdatedDate)
        //       .ToListAsync();

        //    return Json(data);
        //}


        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        public async Task<IActionResult> Index(string status, DateTime? fromDate, DateTime? toDate)
        {
            ViewBag.status = status;

            // Start with the main query
            var query = _context.Boms
                .AsNoTracking()
                .Include(x => x.Project)
                .Include(x => x.BomSupportFiles)
                    .ThenInclude(x => x.SupportFile)
                .Include(x => x.RFQ)
                    .ThenInclude(x => x.Requestor)
                .Where(x => x.RFQId.HasValue && x.Project.Status != "Completed");

            // Apply status filter (case-insensitive)
            if (status == "RFQ Sent" || status == null || status.Trim() == "")
            {
                query = query.Where(x => EF.Functions.Like(x.Status, "RFQ Sent"));
            }
            else
            {
                query = query.Where(x => EF.Functions.Like(x.Status, status));
            }
         
            if (fromDate.HasValue && toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1);
                query = query.Where(x => x.RFQ.LastUpdatedDate >= fromDate.Value && x.RFQ.LastUpdatedDate < end);
            } else if (fromDate.HasValue)
            {
                query = query.Where(x => x.RFQ.LastUpdatedDate >= fromDate.Value);
            } else if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1);
                query = query.Where(x => x.RFQ.LastUpdatedDate < end);
            }


            // Execute the query with ordering
            var data = await query
        .Where(x => x.RFQ != null && x.Project != null) // Add these filters
        .OrderByDescending(x => x.RFQ.LastUpdatedDate)
        .ToListAsync();

            // Get all RFQ IDs in one go
            var rfqIds = data.Select(b => b.RFQId.Value).Distinct().ToList();

            // Load sourcing data in one query per brand
            var sourcingData = await _context.Sourcing
                .AsNoTracking()
                .Where(s => rfqIds.Contains(s.RFQId))
                .ToListAsync();

            // Load RFQ sharing data in one query
            var sourcingIds = sourcingData.Select(s => s.Id).ToList();
            var rfqSharingData = await _context.RFQSharing
                .AsNoTracking()
                .Where(r => sourcingIds.Contains(r.SourceId))
                .ToListAsync();

            // Group the sourcing data by RFQId and Brand for faster lookup
            var sourcingLookup = sourcingData
                .GroupBy(s => new { s.RFQId, Brand = s.Brand.ToLower() })
                .ToDictionary(g => g.Key, g => g.First());

            // Group RFQ sharing by SourceId
            var rfqSharingLookup = rfqSharingData
                .GroupBy(r => r.SourceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Process the data
            foreach (var bom in data)
            {
                // Process support files
                if (bom.ItemCategory == "Fabrication")
                {
                    foreach (var b in bom.BomSupportFiles.Where(x => x.Type == "IC"))
                    {
                        b.SupportFile.ContentType = generateViewURL(b.SupportFile);
                    }
                }

                if (bom.RFQId.HasValue)
                {
                    var rfqId = bom.RFQId.Value;

                    var supplierAttachments = _context.SupplierAttachment
                        .Include(x => x.SupportFile)
                        .Include(x => x.SupplierInfo)
                        .Where(x => x.RFQId == rfqId)
                        .ToList();

                    if (supplierAttachments.Count > 0)
                    {
                        // This matches your earlier rewriteSupplierList logic
                        var supplierQuotationList = new List<SupplierQuotationView>();

                        foreach (var attach in supplierAttachments)
                        {
                            supplierQuotationList.Add(new SupplierQuotationView
                            {
                                SupplierName = attach.SupplierInfo?.VendorName?.Trim(),
                                QuotNo = attach.QuotNo,
                                Remark = attach.Remark,
                                RRemark = attach.R_Remark,
                                Recommended = attach.IsRecommended,
                                FileName = attach.SupportFile?.FileName,
                                FileUrl = generateViewURL(attach.SupportFile)
                            });
                        }

                        // Attach to BOM object (see earlier step: add [NotMapped] property in model)
                        bom.SupplierQuotations = supplierQuotationList;

                    }
                }

                // Check sourcing
                var key = new { RFQId = bom.RFQId.Value, Brand = bom.Brand.ToLower() };
                if (sourcingLookup.TryGetValue(key, out var sourcing))
                {
                    bom.IsPOUpdated = true;

                    // Check RFQ sharing
                    if (rfqSharingLookup.TryGetValue(sourcing.Id, out var relatedRfqSharing))
                    {
                        var statuses = relatedRfqSharing.Select(rfq =>
                            rfq.Rejected == true ? "Rejected" :
                            rfq.SubmittedDate.HasValue ? "Submitted" : "Processing"
                        );
                        bom.RFQSharingStatus = string.Join(", ", statuses);
                    }
                    else
                    {
                        bom.RFQSharingStatus = "Processing";
                    }
                }
                else
                {
                    bom.IsPOUpdated = false;
                    bom.RFQSharingStatus = "N/A";
                }

            }

            return View(data);
        }


        [HttpGet]
        public async Task<IActionResult> GetDataByStatus(string status)
        {
            try
            {
                var data = await _context.Boms
                    .Include(x => x.Project)
                    .Include(x => x.BomSupportFiles)
                    .ThenInclude(x => x.SupportFile)
                    .Include(x => x.RFQ)
                    .ThenInclude(x => x.Requestor)
                    .Where(x => EF.Functions.Like(x.Status, status) && x.RFQId.HasValue && x.Project.Status != "Completed")
                    .OrderByDescending(x => x.RFQ.LastUpdatedDate)
                    .ToListAsync();

                //var result = data.Select(x => new
                //{
                //    x.Id,
                //    x.Status,
                //    ProjectName = x.Project.Name,
                //    RFQLastUpdated = x.RFQ.LastUpdatedDate,
                //    Files = x.BomSupportFiles.Select(b => new
                //    {
                //        b.Id,
                //        b.Type,
                //        URL = GenerateViewURL(b.SupportFile)
                //    }),
                //    IsPOUpdated = _context.Sourcing.Any(s => s.RFQId == x.RFQId && s.Brand.ToLower() == x.Brand.ToLower())
                //});

                return Json(new { success = true, data = data });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }



        public async Task<IActionResult> ExportToExcel()
        {
            var bomsWithCompleteRFQ = await _context.Boms.WhereRFQAvailableStatus2("RFQ Approved").ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Complete RFQ");

                var bomTitle = typeof(CompleteBomGrid).GetProperties();
                var currentRow = 1;
                for (int i = 1; i <= bomTitle.Count(); i++)
                {
                    if(bomTitle[i - 1] == typeof(CompleteBomGrid).GetProperty("RFQNo"))
                    {
                        worksheet.Cell(currentRow, i).Value = bomTitle[i - 1].Name;
                    }
                    else
                    {
                        worksheet.Cell(currentRow, i).Value = bomTitle[i - 1].Name.FirstCharToUpper().ConvertEnum();
                    }

                    worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;

                }

                foreach (var selectedBOM in bomsWithCompleteRFQ)
                {
                    var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == selectedBOM.RFQId);

                    if (rfq.DataResponse != null)
                    {
                        RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);

                        var headerData = root.data.header.FirstOrDefault();
                        
                        var bomItem = root.data.bomGrid.FirstOrDefault(x => x.itemLineNo == selectedBOM.Id);

                        currentRow += 1;
                        worksheet.Cell(currentRow, 1).Value = headerData.code;
                        worksheet.Cell(currentRow, 2).Value = headerData.projectName;
                        worksheet.Cell(currentRow, 3).Value = rfq.Rfqname;

                        for (int i = 1; i <= bomTitle.Count() - 3; i++)
                        {
                            if (bomTitle[i + 2] == typeof(CompleteBomGrid).GetProperty("partNo"))
                            {
                                var bomId = (int)bomItem.GetType().GetProperty("itemLineNo").GetValue(bomItem);
                                var bom = _context.Boms
                                                .Include(x => x.BomSupportFiles)
                                                .Where(x => x.Id == bomId)
                                                .FirstOrDefault();
                                if (bom != null && bom.BomSupportFiles.Where(x => x.Type == "IC").Any())
                                {
                                    worksheet.Cell(currentRow, i + 3).Value = Url.Action("downloadFile", "BOM", new { id = bom.BomSupportFiles.Where(x => x.Type == "IC").FirstOrDefault().SupportFileId }, HttpContext.Request.Scheme);
                                    continue;
                                }
                            }
                            worksheet.Cell(currentRow, i + 3).Value = bomItem.GetType().GetProperty(bomTitle[i + 2].Name).GetValue(bomItem);
                        }
                    }
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "CompletedRFQs.xlsx");
                }
            }
        }

        public async Task<IActionResult> DeleteRFQ(int rfqId, string reason = null, bool fromTracker = false)
        {
            if (!_context.Rfqs.Any(x => x.Id == rfqId))
                throw new NullReferenceException(nameof(rfqId));

            var targetRFQ = _context.Rfqs.Where(x => x.Id == rfqId).FirstOrDefault();
            var targetBoms = _context.Boms.Include(x => x.RFQ).Where(x => x.RFQ.Id == rfqId).ToList();

            var project = _context.Projects.Where(d => d.Id == targetBoms.First().ProjectId).FirstOrDefault();


            var authorizationResult = CommonHelpers.isPics(project);
            if (!authorizationResult && HttpContext.GetUserType() != "Buyer")
                return Forbid();


            var pics = new List<int> { targetRFQ.RequestorId.Value };

            await _auditTrailService.InsertLogAsync(
                     Common.LogLevel.Information, $@"The rfq {targetRFQ.Rfqname} has been removed by buyer",
                     pics: pics,
                     fullMessage: @$"RFQ  {targetRFQ.Rfqname} by {HttpContext.GetShortName()}" + (!string.IsNullOrEmpty(reason) ? $@" because of {reason}." : "."),
                     tableChange: nameof(Rfq),
                     empId: int.Parse(HttpContext.GetUserId()),
                     ProjectId: project.Id
                     );


            if (fromTracker)
            {
                targetRFQ.AddedBy = "sys";
                targetRFQ.Status = "Pending Submit RFQ";
                if (!string.IsNullOrEmpty(reason)) 
                {
                    targetRFQ.Remark = reason;
                }

                _context.Rfqs.Update(targetRFQ);

                foreach(var b in targetBoms)
                {
                    b.Status = "RFQ Reject";
                }
            }
            else
            {
                var targetSupplierAttch = _context.SupplierAttachment.Include(x => x.SupportFile).Where(x => x.RFQId == rfqId).ToList();
                var targetSupplierSupportFile = targetSupplierAttch.Select(x => x.SupportFile).ToList();
                foreach (var supplierfile in targetSupplierSupportFile)
                {
                    _fileWriter.DeleteDocument(supplierfile.FileName);
                }

                if (targetSupplierAttch.Count() > 0)
                {
                    _context.SupplierAttachment.RemoveRange(targetSupplierAttch);
                    await _context.SaveChangesAsync();
                }

                _context.Rfqs.Remove(targetRFQ);

                foreach(var b in targetBoms)
                {
                    b.Status = "New";
                }
            }
            
            _context.Boms.UpdateRange(targetBoms);

            await _context.SaveChangesAsync();
            if (fromTracker)
            {
                var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
                if (isAjax)
                {
                    return Ok();
                }
                else
                {
                    return RedirectToAction("Index","BOM",new {id = project.Id});
                }
            }
            return RedirectToAction("Index","BOM",new {id = project.Id});
        }

        [HttpPost]
        public JsonResult addNewBOM(Bom model, int? projectId)
        {
            try
            {
                if (projectId == null)
                    throw new NullReferenceException(nameof(projectId));

                var formName = "BOM 1";
                var form = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();

                var BOM = new Bom
                {
                    Module = model.Module,
                    PartNumber = model.PartNumber,
                    Brand = model.Brand,
                    PartDesc = model.PartDesc,
                    ItemCategory = model.ItemCategory,
                    UnitQty = model.UnitQty,
                    RequireQty = model.RequireQty,
                    Status = "New",
                    CreatedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    AddedBy = "sys",
                    ProjectId = (int)projectId,
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

        [HttpPost]
        public JsonResult attachFile(int? bomId, IFormFile file = null)
        {
            var bom = _context.Boms.FirstOrDefault(x => x.Id == bomId);
            return Json(new { id = bom.ProjectId });
        }

        private bool IsMoreThanThreeWorkingDay(DateTime? lastUpdateDate)
        {
            if (!lastUpdateDate.HasValue) return false;

            int workDays = 0;
            DateTime start = lastUpdateDate.Value;
            DateTime end = DateTime.Now;

            while (start.Date != end.Date)
            {
                if (start.DayOfWeek != DayOfWeek.Saturday && start.DayOfWeek != DayOfWeek.Sunday)
                {
                    workDays++;
                }

                if (workDays > 3)
                    return true;

                start = start.AddDays(1);
            }
            return false;
        }
    }
}
