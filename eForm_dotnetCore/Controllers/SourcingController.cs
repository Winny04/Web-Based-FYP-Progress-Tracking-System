using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModel;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace eForm_dotnetCore.Controllers
{
    public class SourcingController:Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private IWebHostEnvironment _env;
        private IFileWriter _fileWriter;
        private readonly IAuditTrailService _auditTrailService;

        public SourcingController(EformSophicDemoContext context
            , IEmailService emailService,
            IWebHostEnvironment env,
            IFileWriter fileWriter,
            IAuditTrailService auditTrailService
            )
        {
            _context = context;
            _emailService = emailService;
            _env = env;
            _fileWriter = fileWriter;
            _auditTrailService = auditTrailService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            //var sources = _context.Sourcing
            //    .Include(x => x.RFQ)
            //    .Include(x => x.RFQSharing)
            //    .Include(x => x.SelectedSupplier).AsNoTracking().ToList();
            ////var soucelist = sources.Count();
            var vms = new List<SourcingVM>();

            //foreach (var source in sources)
            //{
            //    var checkStatus = _context.Boms.FirstOrDefault(x => x.RFQId == source.RFQId);
            //    if (checkStatus == null)
            //    {
            //        continue;
            //    }

            //    if (CommonHelpers.availableBaseStatus("RFQ Sent", checkStatus.Status))
            //    {
            //        var vm = new SourcingVM
            //        {
            //            Id = source.Id,
            //            RFQId = source.RFQId,
            //            RFQName = source.RFQ.Rfqname,
            //            TotalPriceSave = source.TotalPriceSave,
            //            Brand = source.Brand,
            //            TotalSend = source.RFQSharing.Count,
            //            TotalResponse = source.RFQSharing.Where(x => x.SubmittedDate.HasValue).Count(),
            //            FinalPrice = source.FinalPrice,
            //            FinalLeadTime = source.FinalLeadTime,
            //            Supplier = source.SelectedSupplier?.VendorName,
            //            Status = checkStatus.Status,
            //        };

            //        vms.Add(vm);
            //    }
            //}
            //var vmsdata = vms.ToList();


            var sources = (from source in _context.Sourcing
                           join rfqsharing in _context.RFQSharing on source.Id equals rfqsharing.SourceId
                           join supplier in _context.SupplierInfo on rfqsharing.SupplierId equals supplier.Id
                           group new { source, rfqsharing } by new
                           {
                               source.Id,
                               source.RFQId,
                               source.RFQ.Rfqname,
                               source.TotalPriceSave,
                               source.Brand,
                               source.FinalPrice,
                               source.FinalLeadTime,
                               source.SelectedSupplier.VendorName
                           } into grouped
                           select new SourcingVM
                           {
                               Id = grouped.Key.Id,
                               RFQId = grouped.Key.RFQId,
                               RFQName = grouped.Key.Rfqname,
                               TotalPriceSave = grouped.Key.TotalPriceSave,
                               Brand = grouped.Key.Brand,
                               TotalSend = grouped.Count(),
                               TotalResponse = grouped.Count(x => x.rfqsharing.SubmittedDate.HasValue),
                               FinalPrice = grouped.Key.FinalPrice,
                               FinalLeadTime = grouped.Key.FinalLeadTime,
                               Supplier = grouped.Key.VendorName
                           })
           .ToList();

            var count = sources.ToList();

            //var query = from a in sources
            //            join bom in _context.Boms
            //                on a.RFQId equals bom.RFQId into bomGroup
            //            from bom in bomGroup
            //                .Where(b => b.Status == "RFQ Sent")
            //                .DefaultIfEmpty() // Left join equivalent
            //            where bom != null // Filtering only non-null statuses
            //            orderby a.RFQId
            //            select new
            //            {
            //                a.Id,
            //                a.RFQId,
            //                a.Brand,
            //                a.Supplier,
            //                Status = bom.Status
            //            };

            //var result = query.ToList();




            foreach (var source in sources)
            {
                var checkStatus = _context.Boms.FirstOrDefault(x => x.RFQId == source.RFQId && x.Brand == source.Brand);
                if (checkStatus == null)
                {
                    continue;
                }

                if (CommonHelpers.availableBaseStatus("RFQ Sent", checkStatus.Status))
                {
                    var vm = new SourcingVM
                    {
                        Id = source.Id,
                        RFQId = source.RFQId,
                        RFQName = source.RFQName,
                        TotalPriceSave = source.TotalPriceSave,
                        Brand = source.Brand,
                        TotalSend = source.TotalSend,
                        TotalResponse = source.TotalResponse,
                        FinalPrice = source.FinalPrice,
                        FinalLeadTime = source.FinalLeadTime,
                        Supplier = source.Supplier,
                        Status = checkStatus.Status,
                        BOMId = checkStatus.Id,
                    };

                    vms.Add(vm);
                }
            }

            //var vmsdata = vms.ToList();

            return View(vms);
        }

        public IActionResult ExportSourcing(DateTime startFrom,DateTime endAt)
        {
            var sources = _context.Sourcing
                .Include(x => x.RFQ)
                .Include(x => x.RFQSharing)
                .ThenInclude(x => x.Supplier)
                .Include(x => x.SelectedSupplier)
                .Where(x => x.SupplierId.HasValue &&
                x.ConfirmAt.HasValue &&
                x.ConfirmAt >= startFrom && x.ConfirmAt <= endAt)
                .ToList();

            var vms = new List<ExportObject>();

            foreach (var source in sources)
            {
                var checkStatus = _context.Boms.FirstOrDefault(x => x.RFQId == source.RFQId);
                if (checkStatus == null)
                {
                    continue;
                }

                if (CommonHelpers.availableBaseStatus("RFQ Approved", checkStatus.Status))
                {
                    var vm = new ExportObject
                    {
                        RFQName = source.RFQ.Rfqname,
                        TotalPriceSave = source.TotalPriceSave,
                        Brand = source.Brand,
                        FinalPrice = source.FinalPrice.Value,
                        FinalLeadTime = source.FinalLeadTime.Value,
                        SelectedSupplier = source.SelectedSupplier?.VendorName,
                        Suppliers = source.RFQSharing.Where(x => x.SubmittedDate.HasValue).ToList()
                    };

                    vms.Add(vm);
                }
            }

            var currentRow = 1;

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Sourcing");

                var columnList = new List<string> { "RFQ", "Brand/Supplier", "Final Price", "Final Lead", "Chosen Supplier", "Cost Saving" };

                foreach(var columnName in columnList.Select((item, index) => new { Item = item, Index = index }))
                {
                    var index = columnName.Index + 1;
                    worksheet.Cell(currentRow, index).Value = columnName.Item;
                    worksheet.Cell(currentRow, index).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, index).Style.Fill.BackgroundColor = XLColor.LightCyan;
                }

                currentRow++;

                foreach(var source in vms)
                {
                    //get rfq source
                    worksheet.Cell(currentRow, 1).Value = source.RFQName;
                    worksheet.Cell(currentRow, 2).Value = source.Brand;
                    worksheet.Cell(currentRow, 3).Value = source.FinalPrice;
                    worksheet.Cell(currentRow, 4).Value = source.FinalLeadTime.ToString("dd/MM/yyyy");
                    worksheet.Cell(currentRow, 5).Value = source.SelectedSupplier;
                    worksheet.Cell(currentRow, 6).Value = source.TotalPriceSave.HasValue ? source.TotalPriceSave.Value : "-";
                    if(source.TotalPriceSave.HasValue && source.TotalPriceSave.Value < 0)
                    {
                        worksheet.Cell(currentRow, 6).Style.Fill.BackgroundColor = XLColor.RadicalRed;
                    }
                    currentRow++;
                    if(source.Suppliers.Count > 0)
                    {
                        foreach(var supplier in source.Suppliers)
                        {
                            var supplierData = GetRFQSharingData(supplier.DataResponse);
                            worksheet.Cell(currentRow, 2).Value = supplier.Supplier.VendorName;
                            //worksheet.Cell(currentRow, 2).Style.Border.OutsideBorderColor = XLColor.Orange;
                            worksheet.Cell(currentRow, 3).Value = supplierData == null ? "-":supplierData.FinalPrice;
                            worksheet.Cell(currentRow, 4).Value = supplierData == null ? "-" : supplierData.FinalLeadTime.ToString("dd/MM/yyyy");
                            currentRow++;
                        }
                    }
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
                        "Sourcing_" + DateTime.Now.ToString("dd_MM_yyyy") + ".xlsx");
                }
            }
        }

        public class ExportObject
        {
            public string RFQName { get; set; }
            public decimal? TotalPriceSave { get; set; }
            public string Brand { get; set; }
            public decimal FinalPrice { get; set; }
            public DateTime FinalLeadTime { get; set; }
            public string SelectedSupplier { get; set; }
            public List<RFQSharing> Suppliers { get; set; }
        }

        [HttpGet]
        public JsonResult IsBrandAndRFQExist(int rfqId,string brand)
        {
            var checkExists = _context.Sourcing.Any(x => x.Brand.ToLower() == brand.ToLower() && x.RFQId == rfqId);

            return Json(new { result = checkExists });
        }

        private async Task<SourceEditVM> GetSourceRFQs(Guid id)
        {
            var rfqs =await _context.RFQSharing.Where(x => x.SourceId == id)
                .ToListAsync();
            var supplierIds =rfqs.Select(x => x.SupplierId).ToList();
            var suppliers = _context.SupplierInfo
                .Where(x => supplierIds.Contains(x.Id)).ToList();

            var vm = new SourceEditVM
            {
                Suppliers = new List<SupplierInfoWithRemark>()
            };

            foreach (var rfq in rfqs)
            {
                var sr = new SupplierInfoWithRemark();
                sr.Supplier = suppliers.First(x => x.Id == rfq.SupplierId);
                sr.Price = (double?)rfq.TotalPrice;
                sr.IsSubmitted = rfq.SubmittedDate.HasValue;
                sr.LeadTime = rfq.LeadTime;
                vm.Suppliers.Add(sr);
            }

            return vm;
        }

        public async Task<IActionResult> Edit(Guid id)
        {

            var source = _context.Sourcing
                .Include(x => x.RFQ)
                .FirstOrDefault(x => x.Id == id);

            var sourceRFQs = await GetSourceRFQs(id);
            if(source != null)
            {
                sourceRFQs.Source = source;
            }

            if (sourceRFQs.Suppliers.Where(x => x.IsSubmitted).ToList().Count > 1)
            {
                var bestPriceSupplier = sourceRFQs.Suppliers.Where(x => x.Price.HasValue).OrderBy(x => x.Price).ToList();
                var bestLeadSupplier = sourceRFQs.Suppliers.Where(x => x.LeadTime.HasValue).OrderBy(x => x.LeadTime).ToList();

                var priceRemark = string.Empty;
                var leadRemark = string.Empty;

                if (bestPriceSupplier.Count > 1)
                {
                    priceRemark = @$"Save RM {(bestPriceSupplier[1].Price - bestPriceSupplier[0].Price)} when compare to {bestPriceSupplier[1].Supplier.VendorName}.";
                }

                if (bestLeadSupplier.Count > 1)
                {
                    var leadDiff = bestLeadSupplier[1].LeadTime - bestLeadSupplier[0].LeadTime;
                    leadRemark = $@"Faster lead time with {leadDiff.Value.TotalDays} days when compare to second option.";
                }
                sourceRFQs.RemarkForBestPrice = priceRemark;
                sourceRFQs.RemarkForBestLead = leadRemark;
            }
            

            return View(sourceRFQs);
        }

        [HttpPost]
        public IActionResult Edit(Guid id,Guid supplierId,int standard)
        {
            var source = _context.Sourcing.FirstOrDefault(x => x.Id == id);

            source.SupplierId = supplierId;
            source.SupplierStandard = standard;

            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<ActionResult> Delete(Guid id)
        {
            var checkItem = _context.Sourcing
                .Include(x => x.RFQ)
                .FirstOrDefault(x => x.Id == id);

            if (checkItem != null)
            {
                _context.Sourcing.Remove(checkItem);
                _context.SaveChanges();

                await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Information, $@"Sourcing id {checkItem.Id} with brand {checkItem.Brand} and with rfq id {checkItem.RFQId} has been removed",
                    tableChange: nameof(Sourcing),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: checkItem.RFQ.ProjectId
                    );
            }

            return Ok();
        }

        [HttpPost]
        public async Task<JsonResult> ConfirmSupplier(Guid id, Guid? supplierId, int standard)
        {
            var source = _context.Sourcing.FirstOrDefault(x => x.Id == id);
            source.ConfirmAt = DateTime.Now;

            var oriRfq = _context.Rfqs.FirstOrDefault(x => x.Id == source.RFQId);

            if (supplierId.HasValue)
            {
                source.SupplierId = supplierId;
            }

            source.SupplierStandard = standard;

            var sourceRFQs =await GetSourceRFQs(id);
            decimal? totalSave = 0;
            if (sourceRFQs.Suppliers.Where(x => x.IsSubmitted).ToList().Count > 1)
            {
                var bestPriceSupplier = sourceRFQs.Suppliers.Where(x => x.Price.HasValue).OrderBy(x => x.Price).ToList();
                var bestLeadSupplier = sourceRFQs.Suppliers.Where(x => x.LeadTime.HasValue).OrderBy(x => x.LeadTime).ToList();

                var selectSupplier = sourceRFQs.Suppliers.First(x => x.Supplier.Id == source.SupplierId);
                source.FinalPrice = (decimal)selectSupplier.Price;
                source.FinalLeadTime = selectSupplier.LeadTime;

                if (source.SupplierId == bestPriceSupplier[0].Supplier.Id)
                {
                    totalSave = (decimal?)bestPriceSupplier[1].Price - (decimal?)bestPriceSupplier[0].Price;
                }
                else
                {
                    totalSave = (decimal?)bestPriceSupplier[0].Price - (decimal?)selectSupplier.Price;
                }

                source.TotalPriceSave = totalSave;
            }

            _context.SaveChanges();

            if (supplierId.HasValue)
            {
                var sr = _context.RFQSharing.FirstOrDefault(x => x.SourceId == id && x.SupplierId == supplierId);

                if (sr == null) return Json(new { result = true, msg = "Something go wrong..." });

                if (!string.IsNullOrEmpty(sr.DataResponse))
                {
                    var rfqRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(sr.DataResponse);

                    var quotations = rfqRoot.data.quotations;

                    var supplierWithQuos = quotations.Where(x => x.quotation.Any()).ToList();

                    if (supplierWithQuos.Count > 0)
                    {
                        foreach (var sq in supplierWithQuos)
                        {
                            var fileName = _fileWriter.CheckAndGetFileName(sq.quotation[0].originalName);
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
                                SupplierInfoId = supplierId.Value,
                                QuotNo = sq.quotNoLink,
                                RFQId = source.RFQId,
                                IsRecommended = true
                            };

                            _context.SupplierAttachment.Add(supplierAttachment);
                            _context.SaveChanges();
                        }
                    }

                    var oriRoot = JsonConvert.DeserializeObject<RFQRoot>(oriRfq.DataResponse);

                    var replaceItems = rfqRoot.data.bomGrid.Select(x => x.itemLineNo);
                    oriRoot.data.bomGrid = oriRoot.data.bomGrid.Where(x => !replaceItems.Contains(x.itemLineNo)).ToList();
                    oriRoot.data.bomGrid.AddRange(rfqRoot.data.bomGrid);

                    oriRfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(oriRoot);
                    _context.SaveChanges();
                }

                await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Information, $@"Sourcing id {source.Id} has been confirm with supplier id {supplierId} for rfq id {source.RFQId} under brand {source.Brand}",
                    tableChange: nameof(Sourcing),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: oriRfq.ProjectId
                    );
            }

            return Json(new { result = true});
        }

        [HttpGet]
        public IActionResult PreviewSourcing(string list, string brand, int rfqId, string dataResponse, string? error)
        {


             var items = list
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var val) ? val : (int?)null)
                .Where(val => val.HasValue)
                .Select(val => val.Value)
                .ToList();

            ViewBag.list = list;
            ViewBag.ErrorMessage = error;

            var vm = new PreviewSourcingVM();
            vm.RFQId = rfqId;
            vm.Brand = brand;
            vm.BrandRecommend = _context.
                Brand
                .Include(x => x.BrandSuppliers)
                .ThenInclude(x => x.Supplier)
                .FirstOrDefault(x => x.Name.ToLower() == brand.ToLower());
            Console.WriteLine($"Received List: {list}");
            if (vm.BrandRecommend == null)
            {
                var newBrand = new Brand
                {
                    Name = brand,
                    BrandSuppliers = new List<BrandSupplier>()
                };

                _context.Brand.Add(newBrand);
                _context.SaveChanges();
                vm.BrandRecommend = newBrand;
            }

            var emailConfig = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == EmailTemplateConfiguration.SREmailContent);
            vm.EmailContent = emailConfig.ConfigValue;
            var rfqTemp = _context.Forms.FirstOrDefault(x => x.Name == "RFQ4_outsider");
            vm.Data = rfqTemp.Data;
            var checkRFQ = _context.Rfqs.FirstOrDefault(x => x.Id == rfqId);
            var bomQuery = _context.Boms.AsQueryable();

            if (!string.IsNullOrEmpty(dataResponse))
            {
                RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(dataResponse);
                if (root.data.bomGrid.Count() > 0)
                {
                    var ddUrl = new List<object>();
                    for (var i = 0; i < root.data.bomGrid.Count(); i++)
                    {
                        var rootBom = bomQuery
                            .Where(x => x.Id == root.data.bomGrid[i].itemLineNo).FirstOrDefault();
                        if (rootBom != null)
                        {
                            rootBom.Brand = root.data.bomGrid[i].brand;
                            _context.SaveChanges();
                        }
                    }
                }
            }

            var emailList = new Dictionary<string, (string Email, string VendorName)>();


            if (vm.BrandRecommend.BrandSuppliers != null)
            {
                foreach (var bs in vm.BrandRecommend.BrandSuppliers)
                {
                    var supplier = bs.Supplier;
                    var supplierEmail = getSupplierEmail(supplier.DataResponse);
                    if (!string.IsNullOrEmpty(supplierEmail))
                    {
                        emailList[supplier.Id.ToString()] = (supplierEmail, "");
                    }
                }
            }

            vm.EmailList = emailList;

            if (checkRFQ != null)
            {
                var rfqRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(checkRFQ.DataResponse);
                rfqRoot.data.bomGrid = rfqRoot.data.bomGrid.Where(x => items.Contains(x.itemLineNo.Value)).ToList();
                vm.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqRoot);
            }

            // Fetch suppliers from AdditionalSuppliers table
            var allVendorList = _context.AdditionalSuppliers
                .Where(v => !string.IsNullOrEmpty(v.Email))
                .Select(v => new VendorInfo
                {
                    Id = v.Id,
                    VendorName = v.VendorName,
                    Email = v.Email
                })
                .OrderBy(v => v.VendorName)
                .ToList();

            vm.AllVendors = allVendorList;

            return View(vm);
        }


        private string getSupplierEmail(string? data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                var supplierRoot = JsonConvert.DeserializeObject<SupplierRoot>(data);
                return supplierRoot?.data?.emailAddress ?? "N/A";
            }
            return "N/A";
        }


        [HttpPost]
        public async Task<IActionResult> CreateSourcing(string list, string brand, int rfqId, string dataResponse, List<string> selectSuppliers, List<string> selectedVendors, string remarks)
        {
            var supplierBrands = _context.Brand
                .Include(x => x.BrandSuppliers)
                .ThenInclude(x => x.Supplier)
                .FirstOrDefault(x => x.Name.ToLower() == brand.ToLower());

            var emailContent = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == EmailTemplateConfiguration.SREmailContent);
            var rfqName = _context.Rfqs.FirstOrDefault(x => x.Id == rfqId);

            if (rfqName == null)
            {
                throw new Exception("Detected original RFQ does not exist during sourcing creation.");
            }

            var emailList = new Dictionary<string, (string, string)>();

            // Process Brand Recommended Suppliers
            if (selectSuppliers != null && selectSuppliers.Any())
            {
                foreach (var supplierId in selectSuppliers)
                {
                    var supplier = supplierBrands?.BrandSuppliers
                        .FirstOrDefault(x => x.SupplierId.ToString() == supplierId)?.Supplier;

                    if (supplier != null)
                    {
                        var email = getSupplierEmail(supplier.DataResponse);
                        if (!string.IsNullOrEmpty(email))
                        {
                            emailList[supplierId] = (email, supplier.VendorName);
                        }
                    }
                }
            }

            // Process Additional Vendors
            if (selectedVendors != null && selectedVendors.Any())
            {
                foreach (var vendorId in selectedVendors)
                {
                    var additionalSupplier = _context.AdditionalSuppliers.FirstOrDefault(x => x.Id.ToString() == vendorId);

                    if (additionalSupplier != null)
                    {
                        var existingSupplier = _context.SupplierInfo
                            .FirstOrDefault(x => x.VendorName.ToLower() == additionalSupplier.VendorName.ToLower());

                        if (existingSupplier != null)
                        {
                            var email = getSupplierEmail(existingSupplier.DataResponse);
                            if (!string.IsNullOrEmpty(email))
                            {
                                emailList[existingSupplier.Id.ToString()] = (email, existingSupplier.VendorName);
                            }
                        }
                        else if (!string.IsNullOrEmpty(additionalSupplier.Email))
                        {
                            
                            var newSupplier = new SupplierInfo
                            {
                                Id = Guid.NewGuid(),
                                VendorName = additionalSupplier.VendorName,
                                DataResponse = $@"{{
                                ""data"": {{
                                   ""emailAddress"": ""{additionalSupplier.Email}"",
                                   ""companyName"": ""{additionalSupplier.VendorName}"",
                                   ""registerType"": ""Additional"",
                                   ""supplierType"": ""External""
                                   }}
                                }}",
                                Completed = false
                            };

                            _context.SupplierInfo.Add(newSupplier);
                            await _context.SaveChangesAsync(); 

                            emailList[newSupplier.Id.ToString()] = (additionalSupplier.Email, additionalSupplier.VendorName);
                        }
                    }
                }
            }

            // Ensure at least one supplier is selected
            if (!emailList.Any())
            {
                string errorMessage = "No valid suppliers selected for email sending.";
                return RedirectToAction("PreviewSourcing", "Sourcing", new
                {
                    list = list,
                    brand = brand,
                    rfqId = rfqId,
                    error = errorMessage
                });
            }

            // Create sourcing record
            var sourcing = new Sourcing
            {
                SupplierStandard = 1,
                Brand = brand,
                RFQId = rfqId
            };

            _context.Sourcing.Add(sourcing);
            _context.SaveChanges();

            if (!string.IsNullOrEmpty(dataResponse))
            {
                var rfqRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(dataResponse);
                if (rfqRoot != null && rfqRoot.data != null && rfqRoot.data.bomGrid != null)
                {
                    foreach (var bom in rfqRoot.data.bomGrid)
                    {
                        bom.unitPrice = null;
                        bom.discount = null;
                        bom.tax = null;
                        bom.shippingCost = null;
                        bom.bankFee = null;
                        bom.totalPrice = null;
                        bom.leadTime = null;
                        bom.eta = null;
                    }
                    dataResponse = JsonConvert.SerializeObject(rfqRoot);
                }
            }

            // Send emails
            foreach (var email in emailList)
            {
                
                if (!Guid.TryParse(email.Key, out Guid supplierGuid) ||
                    !_context.SupplierInfo.Any(s => s.Id == supplierGuid))
                {
                    Console.WriteLine($"Warning: Invalid supplier ID {email.Key}. Skipping RFQSharing creation.");
                    continue; 
                }

                var rfqSharing = new RFQSharing
                {
                    SourceId = sourcing.Id,
                    DataResponse = dataResponse,
                    SupplierId = supplierGuid, 
                    CreatedDate = DateTime.Now
                };

                _context.RFQSharing.Add(rfqSharing);
                _context.SaveChanges();

                var rsLink = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/RFQSharing/Index?id={rfqSharing.Id}";
               
                var currentUserId = HttpContext.GetUserId();
                string name = "System";
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    name = _context.EmployeeInfos
                                   .Where(e => e.Id == int.Parse(currentUserId))   
                                   .Select(e => e.FullName)                      
                                   .FirstOrDefault() ?? "System";          
                }

                var formattedString = emailContent.ConfigValue
                                            .Replace("{vendor}", email.Value.Item2)
                                            .Replace("{remarks}", remarks)
                                            .Replace("{user}", name)
                                            .Replace("{url}", $"<a href='{rsLink}' target='_blank'>this link</a> or paste URL in browser: {rsLink}");

                try
                {
                    _emailService.Send(email.Value.Item1,
                       $"[QUOTATION REQUEST] {rfqName.Rfqname} from Sophic", formattedString,
                       cc: "internal.system@sophicautomation.com");


                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information,
                        $"Sourcing ID {sourcing.Id} has been created for RFQ ID {rfqName.Rfqname} under brand {brand}",
                        tableChange: nameof(Sourcing),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: rfqName.ProjectId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send email to {email.Value.Item2} ({email.Value.Item1}): {ex.Message}");
                }
            }
            var firstValue = list.Split(',')[0];
            return RedirectToAction("Edit", "RFQ", new { id = rfqId, bomId = firstValue });
        }


        private string? GetSupplierEmail(string? data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;

            var supplierRoot = JsonConvert.DeserializeObject<SupplierRoot>(data);

            if(supplierRoot != null)
            {
                return supplierRoot.data.emailAddress;
            }

            return string.Empty;
        }


        private ExportObject? GetRFQSharingData(string? data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                var rfqRoot = JsonConvert.DeserializeObject<RFQSharingRoot>(data);
                if (rfqRoot != null)
                {
                    return new ExportObject
                    {
                        FinalPrice = (decimal)rfqRoot.data.bomGrid.Sum(x => x.totalPrice),
                        FinalLeadTime = rfqRoot.data.bomGrid[0].eta.Value
                    };
                }
            }

            return null;
        }
    }
}
