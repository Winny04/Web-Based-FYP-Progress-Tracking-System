using ClosedXML.Excel;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using eForm_dotnetCore.Common;
using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Domain;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Authentication;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Ocsp;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using static eForm_dotnetCore.Controllers.SourcingController;
using static eForm_dotnetCore.ViewModels.BOMExcelModel;

namespace eForm_dotnetCore.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public HomeController(ILogger<HomeController> logger,
            EformSophicDemoContext context,
            IEmailService emailService,
            IAuthenticationService authenticationService,
            IAuditTrailService auditTrailService,
            IWebHostEnvironment environment
            )
        {
            _logger = logger;
            _context = context;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
            _authenticationService = authenticationService;
            _env = environment;
        }

        public async Task<IActionResult> ExportBOMList()
        {
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
                }

                //get bom list
                var bom = _context.Boms
                        .Include(f => f.Project)
                        .Include(x => x.RFQ)
                        .Include(x => x.PR)
                        .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                        .OrderBy(x => x.ProjectId)
                        .ToList();
                var check = false;
                var userList = _context.EmployeeInfos.ToList();
                var user = userList.Where(x => x.UserName == HttpContext.User.Identity.Name).FirstOrDefault();
                if (user.UserType == "CEO" || user.UserType == "GFC" || user.UserType == "HOD" || user.UserType == "PM" || user.UserType == "SuperAdminAssistant" || user.UserType == "SuperAdmin" || user.UserType == "Buyer")
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
                        "all_project_bom_" + DateTime.Now.ToString() + ".xlsx");
                }

            }
        }

        private async Task<SourceEditVM> GetSourceRFQs(Guid id)
        {
            var rfqs = await _context.RFQSharing.Where(x => x.SourceId == id)
                .ToListAsync();
            var supplierIds = rfqs.Select(x => x.SupplierId).ToList();
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

        public async Task UpdateUnitPrice()
        {
            var items = _context.Boms.Where(x => x.RFQId.HasValue || x.PRId.HasValue).ToList();

            foreach(var item in items)
            {
                var bom = _context.Boms.Where(x => x.Id == item.Id).FirstOrDefault();
                double unitPrice = 0;

                if (item.PRId.HasValue)
                {
                    var pr = _context.Prs.FirstOrDefault(x => x.Id == item.PRId);
                    if(pr != null && !string.IsNullOrEmpty(pr.DataResponse))
                    {
                        var root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                        var prItem = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).FirstOrDefault();
                        if(prItem != null)
                        {
                            unitPrice = prItem.unitPrice.HasValue ? prItem.unitPrice.Value : 0;
                        }
                    }
                }
                else
                {
                    var rfq = _context.Rfqs.FirstOrDefault(x => x.Id == item.RFQId);
                    if(rfq != null)
                    {
                        var root = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                        var rfqItem = root.data.bomGrid.Where(x => x.itemLineNo == item.Id).FirstOrDefault();
                        if (rfqItem != null)
                        {
                            unitPrice = rfqItem.unitPrice.HasValue ? rfqItem.unitPrice.Value : 0;
                        }
                    }
                }

                bom.UnitPrice = unitPrice;
                _context.Boms.Update(bom);
                await _context.SaveChangesAsync();
            }
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var dashboardViews = new ProjectOverviewViewModel();

            //await UpdateUnitPrice();

            try
            {
                var currentUsername = HttpContext.User.Identities.Select(x => x.Name).FirstOrDefault();
                var getUser = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => x.UserName == currentUsername).FirstOrDefault();
                if (getUser == null)
                {
                    await _authenticationService.SignOutAsync();
                    return RedirectToRoute("Login");
                }

                ViewBag.CurrentUser = getUser;

                dashboardViews.Employees = _context.EmployeeInfos.ToList();
                var projects = _context.Projects.Include(x => x.CategoryInfo).ToList();

                foreach (var project in projects)
                {
                    var check = project.Status;
                    if (String.IsNullOrEmpty(project.PoDueDate))
                    {
                        project.Status = "New";
                    }
                    else
                    {
                        var allDueDate = project.PoDueDate.Split(",");
                        var lastestDueDate = allDueDate.Last();
                        var projectBoms = _context.Boms.Count(x => x.ProjectId == project.Id);

                        if (DateTime.Now > Convert.ToDateTime(lastestDueDate) && project.Status != "Completed") project.Status = "Overdue";

                        if (project.Status == "New" && ((DateTime.Now - project.CreatedDate).TotalDays >= 5 || projectBoms > 0)) project.Status = "Active";

                    }
                    _context.Projects.Update(project);
                    _context.SaveChanges();
                    dashboardViews.Projects.Add(project);

                }

                // Custom sort by status
                var statusOrder = new List<string> { "New", "Active", "Overdue", "Completed" };
                dashboardViews.Projects = dashboardViews.Projects
                    .OrderBy(p => statusOrder.IndexOf(p.Status ?? ""))
                    .ToList();

                foreach (var item in dashboardViews.Projects)
                {
                    item.PICS = string.IsNullOrEmpty(item.PICS) ? "" : getPicsWithDept(item.PICS);
                }

                var userlist = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .Where(x => !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).ToList();

                dashboardViews.Employees = userlist;

                return View(dashboardViews);
            }
            catch (Exception ex)
            {
                throw;
                Console.WriteLine(ex.Message);
            }
        }

        private string getPicsWithDept(string pICS)
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

        public JsonResult GetSourceDataByYear(int year = 2023)
        {
            var dict = new Dictionary<string, decimal>();
            var sources = _context.Sourcing.Where(x => x.ConfirmAt.HasValue && x.ConfirmAt.Value.Year == year).ToList();
            decimal lostPrice = 0;
            int sourceCount = 0;
            for (var i = 1; i <= 12; i++)
            {
                decimal savePrice = 0;
                var currentMonthSources = sources.Where(x => x.ConfirmAt.Value.Month == i).ToList();
                foreach (var source in currentMonthSources)
                {
                    var checkStatus = _context.Boms.FirstOrDefault(x => x.RFQId == source.RFQId);
                    if (checkStatus == null)
                    {
                        continue;
                    }

                    if (CommonHelpers.availableBaseStatus("RFQ Approved", checkStatus.Status))
                    {
                        if (source.TotalPriceSave.HasValue)
                        {
                            sourceCount++;
                            if (source.TotalPriceSave > 0)
                            {
                                savePrice += source.TotalPriceSave.Value;
                            }
                            else
                            {
                                lostPrice += source.TotalPriceSave.Value;
                            }
                        }
                    }
                }

                string monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i);
                dict.Add(monthName, savePrice);
            }

            return Json(new { data = dict });
        }

        public IActionResult SummaryChart()
        {
            var projects = _context.Projects.ToList();
            var dashboardViews = new ProjectOverviewViewModel();

            foreach (var project in projects)
            {
                dashboardViews.Projects.Add(project);
            }

            return View(dashboardViews);
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public IActionResult SearchDocument(string docType, string query)
        {
            try
            {
                if (string.IsNullOrEmpty(query))
                {
                    return Json(new { success = false, msg = "Search query is empty." });
                }

                query = query.Trim();

                if (docType == "RFQ")
                {
                    var rfq = _context.Rfqs.FirstOrDefault(r => r.Rfqname == query);
                    if (rfq != null)
                    {
                        return Json(new { success = true, url = Url.Action("View", "RFQ", new { id = rfq.Id }) });
                    }
                }
                else if (docType == "PR")
                {
                    var pr = _context.Prs.FirstOrDefault(p => p.Prname == query);
                    if (pr != null)
                    {
                        return Json(new { success = true, url = Url.Action("View", "PurchaseRequisition", new { id = pr.Id }) });
                    }
                }
                else if (docType == "Project")
                {
                    var project = _context.Projects.FirstOrDefault(p => p.Code == query);
                    if (project != null)
                    {
                        return Json(new { success = true, url = Url.Action("Index", "BOM", new { id = project.Id }) });
                    }
                }
                return Json(new { success = false, msg = "Document cannot be found." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, msg = "Error occurred during search." });
            }
        }


        public IActionResult ExportToPDF(int id)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Document pdfDoc = new Document(PageSize.A2.Rotate(), 0, 0, 100, 20);
                PdfWriter writer = PdfWriter.GetInstance(pdfDoc, stream);

                pdfDoc.Open();
                var ProjectList = new List<ExportProjectListViewModel>();
                var userList = _context.EmployeeInfos.ToList();
                ProjectList = _context.Projects
                    .Include(x => x.CategoryInfo)
                    .Select(x => new ExportProjectListViewModel
                {
                    ProjectCode = x.Code,
                    Customer = x.Customer ?? "",
                    Quotation = x.QuotationNo ?? "",
                    ProjectPO = x.PoNumber ?? "",
                    ProjectName = x.Name ?? "",
                    ProjectCategory = x.CategoryInfo.CategoryName ?? "",
                    PoQty = (x.PoQuantity ?? 0).ToString(),
                    PoReceivedDate = x.PoStartDate.HasValue ? x.PoStartDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                    ProjectDeliveryDate = x.PoDueDate ?? "",
                    TermsAndCondition = x.PoTerms ?? "",
                    BudgetMaterialCost = (x.BudgetMaterialCost ?? 0).ToString(),
                    Status = x.Status,
                    //SalesPIC = (x.SalesPic ?? 0).ToString(),
                    //MechanicalPIC = (x.MechanicalPic ?? 0).ToString(),
                    //FSEPIC = (x.Fsepic ?? 0).ToString(),
                    //SoftwarePIC = (x.SoftwarePic ?? 0).ToString(),
                }).ToList();

                //foreach (var item in ProjectList)
                //{
                //    if (!String.IsNullOrEmpty(item.SalesPIC))
                //    {
                //        int salesPICId = Convert.ToInt32(item.SalesPIC);
                //        item.SalesPIC = userList.Where(z => z.Id == salesPICId).Select(z => z.FullName).FirstOrDefault() ?? "";
                //    }
                //    if (!String.IsNullOrEmpty(item.MechanicalPIC))
                //    {
                //        int mechanicalPICId = Convert.ToInt32(item.MechanicalPIC);
                //        item.MechanicalPIC = userList.Where(z => z.Id == mechanicalPICId).Select(z => z.FullName).FirstOrDefault() ?? "";
                //    }
                //    if (!String.IsNullOrEmpty(item.FSEPIC))
                //    {
                //        int fsePICId = Convert.ToInt32(item.FSEPIC);
                //        item.FSEPIC = userList.Where(z => z.Id == fsePICId).Select(z => z.FullName).FirstOrDefault() ?? "";
                //    }
                //    if (!String.IsNullOrEmpty(item.SoftwarePIC))
                //    {
                //        int softwarePICId = Convert.ToInt32(item.SoftwarePIC);
                //        item.SoftwarePIC = userList.Where(z => z.Id == softwarePICId).Select(z => z.FullName).FirstOrDefault() ?? "";
                //    }
                //}

                //bom zone
                var bomZoneTable = new PdfPTable(typeof(ExportProjectListViewModel).GetProperties().Count());
                var bomZone = typeof(ExportProjectListViewModel).GetProperties();
                //set bom title
                for (int i = 1; i <= bomZone.Count(); i++)
                {
                    bomZoneTable.AddCell(new Phrase(bomZone[i - 1].Name.FirstCharToUpper().ConvertEnum(), FontFactory.GetFont(FontFactory.HELVETICA, 11, iTextSharp.text.Font.BOLD)));
                }
                //set bom row
                foreach (var item in ProjectList)
                {
                    for (int i = 1; i <= bomZone.Count(); i++)
                    {
                        var pValue = item.GetType().GetProperty(bomZone[i - 1].Name).GetValue(item, null);
                        var value = pValue != null ? pValue.ToString() : string.Empty;
                        bomZoneTable.AddCell(new Phrase(value));
                    }
                }
                pdfDoc.Add(bomZoneTable);

                pdfDoc.Close();
                //stream.Position = 0;
                //writer.CloseStream = false;

                return File(stream.ToArray(), "application/pdf", "Project_List_" + DateTime.Now.ToString() + ".pdf");
            }
        }

        public IActionResult ExportToExcel()
        {
            //Prepare Data  (Project List)
            var ProjectList = new List<ExportProjectListViewModel>();
            var userList = _context.EmployeeInfos.ToList();
            var categoryList = _context.Category.ToList();
            ProjectList = _context.Projects
                .Include(x => x.CategoryInfo)
                .Select(x => new ExportProjectListViewModel
            {
                ProjectCode = x.Code,
                Customer = x.Customer ?? "",
                Quotation = x.QuotationNo ?? "",
                ProjectPO = x.PoNumber ?? "",
                ProjectName = x.Name ?? "",
                ProjectCategory = x.CategoryInfo.CategoryName ?? "",
                PoQty = (x.PoQuantity ?? 0).ToString(),
                PoReceivedDate = x.PoStartDate.HasValue ? x.PoStartDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                ProjectDeliveryDate = x.PoDueDate ?? "",
                TermsAndCondition = x.PoTerms ?? "",
                BudgetMaterialCost = (x.BudgetMaterialCost ?? 0).ToString(),
                Status = x.Status,
                ActualMaterialCost = x.ActualMaterialCost.ToString()
            }).ToList();


            var currentRow = 1;
            using (var workBook = new XLWorkbook())
            {
                var worksheet = workBook.Worksheets.Add("Project");

                //currentRow += 3;
                var projectZone = typeof(ExportProjectListViewModel).GetProperties();

                for (int i = 1; i <= projectZone.Count(); i++)
                {
                    if (projectZone[i - 1].Name != "ActualMaterialCost")
                    {
                        worksheet.Cell(currentRow, i).Value = projectZone[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightCyan;
                    }
                }

                ProjectList = ProjectList.OrderBy(x => int.Parse(x.ProjectCode.Replace("P", ""))).ToList();

                //set project row
                foreach (var item in ProjectList)
                {
                    var bomStatus = "";
                    double actualSpent = 0;
                    currentRow++;
                    for (int i = 1; i <= projectZone.Count(); i++)
                    {
                        var colName = projectZone[i - 1].Name;
                        var cellValue = item.GetType().GetProperty(projectZone[i - 1].Name).GetValue(item, null);

                        if (colName == "Status")
                        {
                            bomStatus = cellValue.ToString();
                            if(bomStatus == "Overdue")
                            {
                                worksheet.Cell(currentRow, i).Style.Font.FontColor = XLColor.Red;
                            }
                            if(bomStatus == "Active") //Active now change to In Progress in UI
                            {
                                cellValue = "In Progress";
                            }
                        }

                        if (colName == "POReceivedDate")
                        {
                            worksheet.Cell(currentRow, i).Value = "'" + cellValue;
                        }
                        else if (colName == "ProjectDeliveryDate")
                        {
                            if (bomStatus == "Overdue")
                            {
                                worksheet.Cell(currentRow, i).Value = "'" + cellValue;
                                worksheet.Cell(currentRow, i).Style.Font.FontColor = XLColor.Red;
                            }
                            else
                            {
                                worksheet.Cell(currentRow, i).Value = "'" + cellValue;
                            }
                        }
                        else if (colName == "ActualMaterialCost")
                        {
                            worksheet.Cell(currentRow, i).Value = "'";
                        }
                        else if(colName == "BudgetMaterialCost")
                        {
                            double budget = cellValue != null ? Convert.ToDouble(cellValue) : 0.00;
                            actualSpent = !String.IsNullOrEmpty(item.ActualMaterialCost) ? Convert.ToDouble(item.ActualMaterialCost) : 0.00;
                            if (actualSpent > budget && budget > 0)
                            {
                                worksheet.Cell(currentRow, i).Value =  cellValue;
                                worksheet.Cell(currentRow, i).Style.Font.FontColor = XLColor.Red;
                            }
                            else
                            {
                                worksheet.Cell(currentRow, i).Value = cellValue;
                            }
                        }
                        else if (colName == "Quotation")
                        {
                            var currUserDept = HttpContext.GetUserDept();
                            if (currUserDept != "BD" && (currUserDept != "SuperAdmin" || currUserDept != "SuperAdminAssistant"))
                            {
                                worksheet.Cell(currentRow, i).Value = "'" + cellValue;
                            }
                        }
                        else
                        {
                            worksheet.Cell(currentRow, i).Value = cellValue;
                        }
                    }
                }

                using (var stream = new MemoryStream())
                {
                    workBook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "Project_List_" + DateTime.Now.ToString() + ".xlsx");
                }
            }

        }

        [HttpPost]
        public async Task<JsonResult> EditPICs(int projectId, string picsList, int? leader)
        {
            try
            {
                if (projectId == 0)
                {
                    return Json(new { status = false, msg = "An error occur. Project ID is not found." });
                }

                var project = _context.Projects.Where(x => x.Id == projectId).FirstOrDefault();
                if (project == null)
                {
                    return Json(new { status = false, msg = "An error occur. Invalid porject ID. A project cannot be found." });
                }

                var oldPIC = project.PICS;
                var oldPICList = new List<string>();
                if (!String.IsNullOrEmpty(oldPIC))
                {
                    oldPICList = oldPIC.Split(',').ToList();
                }

                var newPICs = "";
                if (!string.IsNullOrEmpty(picsList))
                {
                    newPICs = picsList.TrimEnd(',');
                }
                project.PICS = newPICs;

                var newPICList = newPICs.Split(',').ToList();

                var user = _context.EmployeeInfos.Where(x => x.UserName == HttpContext.User.Identity.Name).FirstOrDefault();
                var checkLeader = (user.Id == project.PICLeader);


                if (leader.HasValue)
                {
                    var check = false;
                    foreach (var item in newPICList)
                    {
                        if (!check)
                        {
                            var picID = Convert.ToInt16(item);
                            if (picID == leader)
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
                //var removedPICs = newPICList.Where(x => !oldPICList.Contains(x)).ToList();
                var removedPICs = oldPICList.Except(newPICList);
                var addedPICs = newPICList.Except(oldPICList);

                var addedPICsIds = new List<int>();
                var removedPICsIds = new List<int>();

                if (addedPICs.Any())
                {
                    foreach (var item in addedPICs)
                    {
                        addedPICsIds.Add(Convert.ToInt16(item));
                    }
                }

                if (removedPICs.Any())
                {
                    foreach (var item in removedPICs)
                    {
                        removedPICsIds.Add(Convert.ToInt16(item));
                    }
                }

                var removedPICsEmailList = _context.EmployeeInfos.Where(x => removedPICsIds.Contains(x.Id) && x.IsNotificationOn).Select(x => x.Email).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                var addedPICsEmailList = _context.EmployeeInfos.Where(x => addedPICsIds.Contains(x.Id) && x.IsNotificationOn).Select(x => x.Email).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

                var titleNewPIC = "Added to Project as PIC";
                var titleRemovedPIC = "Removed from Project";

                var (msgAddedPIC, htmlAddedPIC) = CommonHelpers.AddOrDeletePorjectPICTemplate(titleNewPIC, "ADDED", project.Name, Url.Action("Index", "BOM", new { id = project.Id }));
                var (msgRemovedPIC, htmlRemovedPIC) = CommonHelpers.AddOrDeletePorjectPICTemplate(titleRemovedPIC, "REMOVED", project.Name, Url.Action("Index", "BOM", new { id = project.Id }));

                await _emailService.Send(addedPICsEmailList, titleNewPIC, htmlAddedPIC);
                await _emailService.Send(removedPICsEmailList, titleRemovedPIC, htmlRemovedPIC);
                if (checkLeader)
                {
                    var titleInformManagement = "The PIC Leader modify the Project PIC";
                    var HODPMEmailList = _context.EmployeeInfos.Where(x => (x.UserType == "HOD" || x.UserType == "PM") && x.IsNotificationOn).Select(x => x.Email).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                    var (msgInform, htmlInform) = CommonHelpers.PICLeaderAddOrDeletePIC(titleInformManagement, user.UserName, project.Name, Url.Action("Index", "BOM", new { id = project.Id }));
                    await _emailService.Send(HODPMEmailList, titleInformManagement, htmlInform);
                }



                //await _auditTrailService.InsertLogAsync(
                //    Models.LogLevel.Information, title,
                //    fullMessage: msg,
                //    tableChange: nameof(Project),
                //    empId: int.Parse(HttpContext.GetUserId())
                //    );


                await _context.SaveChangesAsync();

                return Json(new { status = true, msg = "Saved successful" }); ;

            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "An error occur." + ex.Message });
            }
        }

        [AllowAnonymous]
        public IActionResult Error404(string? msg = "")
        {
            var error = new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, Message = msg };
            return View(error);
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

        public static string PadNumbers(string input)
        {
            return Regex.Replace(input, "[0-9]+", match => match.Value.PadLeft(10, '0'));
        }

    }
}