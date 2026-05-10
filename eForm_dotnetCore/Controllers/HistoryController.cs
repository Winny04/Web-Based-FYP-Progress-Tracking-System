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
using Microsoft.AspNetCore.Authorization;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Common;

namespace eForm_dotnetCore.Controllers
{
    public class HistoryController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IEmailService _emailService;

        public HistoryController(EformSophicDemoContext context,
            IWebHostEnvironment _environment,
            IFileWriter fileWriter,
            IAuthorizationService authorizeService,
            IEmailService emailService,
            IAuditTrailService auditTrailService)
        {
            _context = context;
            Environment = _environment;
            _fileWriter = fileWriter;
            _authorizeService = authorizeService;
            _auditTrailService = auditTrailService;
            _emailService = emailService;
        }

        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        public async Task<IActionResult> Index()
        {
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.ReceivingTracker);
            link.Add("/ReceivingTracker/Index/");
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;

            var list = new List<HistoryModel>();
            try
            {
                list = await LoadBOMList();

                return View(list);

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        public async Task<IActionResult> View(int PrId)
        {
            var list = new List<HistoryModel>();
            try
            {
                list = await LoadBOMItem(PrId);
                list = list.OrderBy(x => x.Id).ToList();
                ViewBag.Status = list.Select(x => x.Status).FirstOrDefault();
                ViewBag.PrId = list.Select(x => x.PrId).FirstOrDefault();

                return View(list);

            }
            catch (Exception ex)
            {
                throw;
            }
        }




        public async Task<List<HistoryModel>> LoadBOMList()
        {
            var model = new List<HistoryModel>();
            var viewModel = new HistoryModel();
            var list = new List<HistoryModel>();
            var tempDatalist = new List<HistoryModel>();
            var boms = new List<Bom>();
            var statusList = new List<string> { "Completed" };
            var allPrIds = await _context.Boms
                                .Where(x => statusList.Contains(x.Status))
                                .Select(x => x.PRId).Distinct().ToListAsync();


            var bomIds = new List<int>();
            //just wanna select only 1 bom from every PR
            var firstBomInPR = await _context.Boms.Where(x => allPrIds.Contains(x.PRId) && statusList.Contains(x.Status))
                                 .Select(x => new { x.Id, x.PRId }).ToListAsync();

            foreach (var prId in allPrIds)
            {
                bomIds.Add(firstBomInPR.Where(x => x.PRId == prId)
                                 .Select(x => x.Id).FirstOrDefault());
            }

            boms = await _context.Boms.Where(x => bomIds.Contains((int)x.Id))
                        .Select(x => new Bom
                        {
                            Id = x.Id,
                            Status = x.Status,
                            PRId = x.PRId,
                            ProjectId = x.ProjectId
                        }).ToListAsync();

            var prIds = boms.Select(x => x.PRId).Distinct().ToList();
            var prs = await _context.Prs.Where(x => prIds.Contains(x.Id)).Select(x => new { x.Id, x.DataResponse, x.Prname,x.RevisionNo }).ToListAsync();
            var catlist = await _context.Category.ToListAsync();
            var superAdmins = CommonExtensions.SuperAdminList();
            var employeeList = await _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => !superAdmins.Contains(x.UserName) && !string.IsNullOrEmpty(x.ShortName) && !string.IsNullOrEmpty(x.EmpDepartment.Name))
                .OrderBy(x => x.ShortName).ToListAsync();
            var allProjectIds = boms.Select(x => x.ProjectId).ToList();
            var projectList = await _context.Projects.Where(x => allProjectIds.Contains(x.Id)).ToListAsync();

            ViewBag.EmployeeList = employeeList;
            ViewBag.DepartmentList = employeeList.Select(x => x.EmpDepartment.Name).Distinct().ToList();


            if (boms.Any())
            {
                var Bom_totalPrice = 0;
                foreach (var bom in boms)
                {
                    viewModel = new HistoryModel();
                    bom.Project = projectList.Where(x => x.Id == bom.ProjectId).FirstOrDefault();
                    int totalPrice = 0;
                    string vendorName = "";
                    string vendorQuotation = "";
                    var requestor = "";
                    var department = "";
                    bool vendorIsSelected = false;
                    int rowIndex = 0;

                    //read pr
                    int quantity = 0;
                    double unitPrice = 0;
                    double prTotalPrice = 0;
                    DateTime? estDeliveryDate = null;
                    var currentPR = prs.Where(x => x.Id == bom.PRId).FirstOrDefault();

                    if (currentPR.DataResponse != null)
                    {
                        var prResponse = currentPR.DataResponse;
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                        if (prRoot.data.bomGrid.Count() > 0)
                        {
                            quantity = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalQuantity).FirstOrDefault();
                            unitPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.unitPrice).FirstOrDefault() ?? 0;
                            prTotalPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalPrice).FirstOrDefault() ?? 0;
                            estDeliveryDate = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.eta).FirstOrDefault();
                           
                            var oldRequestor = prRoot.data.header.Select(x => x.requester).FirstOrDefault();
                            requestor = Common.Departments.ConvertRequestorWithDept(oldRequestor) ?? "";
                            var dept = oldRequestor.Split('(', ')')[1];
                            if (dept == "FSE" || dept == "FIN")
                            {
                                var commonDept = new Common.Departments();
                                commonDept.UpdatePRRequestorDept(bom.PRId, requestor); //Update requestor in PR
                            }
                        }
                    }

                    var bomInPR = await _context.Boms.Where(x => x.PRId == bom.PRId).Select(x => new { x.Id, x.Status }).ToListAsync();
                    var bomStatusInPR = bomInPR.Select(x => x.Status).ToList();


                    var bomId = bom.Id;
                    viewModel.Id = bomId;
                    viewModel.PrId = bom.PRId ?? 0;
                    viewModel.isPICs = CommonHelpers.isPics(bom.Project);
                    viewModel.Requestor = requestor;
                    viewModel.Customer = bom.Project.Customer;
                    viewModel.Department = department;
                    viewModel.CustomerPO = bom.Project.PoNumber;
                    viewModel.ProjectName = bom.Project.Name;
                    viewModel.ProjectCode = bom.Project.Code;
                    var catId = Convert.ToInt16(bom.Project.Category);
                    viewModel.ProjectCategory = catlist.Where(x => x.Id == catId).Select(x => x.CategoryShortName).FirstOrDefault();
                    viewModel.EstDeliveryDate = estDeliveryDate;
                    viewModel.IssuedPICId = bom.IssuedPIC;
                    viewModel.ProjectId = bom.ProjectId;
                    viewModel.rowIndex = rowIndex;
                    viewModel.remark = bom.ReceivingRemark;
                    viewModel.PrName = CommonHelpers.renamePR(currentPR.Prname, currentPR.RevisionNo);
                    viewModel.Status = "Completed";

                    var supFile = await _context.BomSupportFile.Where(x => x.BomId == bom.Id && x.Type == "PO").ToListAsync();
                    foreach (var file in supFile)
                    {
                        var sFileId = file.SupportFileId;
                        file.SupportFile = _context.SupportFile.Where(x => x.Id == sFileId).FirstOrDefault();
                    }
                    var poFile = supFile.Where(x => x.Type == "PO").FirstOrDefault();
                    if (poFile != null)
                    {
                        viewModel.POName = poFile.SupportFile.FileName;
                    }
                    viewModel.PO = supFile;

                    list.Add(viewModel);
                }
            }

            model = list.OrderBy(x => x.POName).ToList();
            return model;
        }


        public async Task<List<HistoryModel>> LoadBOMItem(int? PrId)
        {
            var model = new HistoryModel();
            var viewModel = new HistoryModel();
            var list = new List<HistoryModel>();
            var tempDatalist = new List<HistoryModel>();
            var boms = new List<Bom>();
            var statusList = new List<string> { "Completed" };
            if (PrId != null) //Select by PR
            {
                boms = await _context.Boms
                         //.Include(x => x.RFQ)
                         //.Include(x => x.PR)
                         //.Include(x => x.Project)
                         .Include(x => x.BomSupportFiles)
                         //.Include(x => x.BomissueOutStatuses)
                         .Where(x => statusList.Contains(x.Status) && x.PRId == PrId).ToListAsync();
            }
            else //
            {
                return list;
            }//end else

            var pr = await _context.Prs.Where(x => x.Id == PrId).FirstOrDefaultAsync();
            var catlist = await _context.Category.ToListAsync();
            var superAdmins = CommonExtensions.SuperAdminList();
            var employeeList = await _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => !superAdmins.Contains(x.UserName) && !string.IsNullOrEmpty(x.ShortName) && !string.IsNullOrEmpty(x.EmpDepartment.Name)).OrderBy(x => x.ShortName).ToListAsync();
            var project = await _context.Projects.Where(x => x.Id == boms.FirstOrDefault().ProjectId).FirstOrDefaultAsync();
            ViewBag.EmployeeList = employeeList;
            ViewBag.DepartmentList = employeeList.Select(x => x.EmpDepartment.Name).Distinct().ToList();

            if (boms.Any())
            {
                var Bom_totalPrice = 0;
                foreach (var bom in boms)
                {
                    if (bom.Status == "PO Approved" && PrId != null)
                    {
                        if (bom.ReceivedDate.HasValue
                            && (bom.ReceivedQuantity.HasValue && bom.ReceivedQuantity > 0)
                            && bom.BomSupportFiles.Any(x => x.Type == "DO")
                            && bom.BomSupportFiles.Any(x => x.Type == "INV")
                            //&& bom.BomSupportFiles.Any(x => x.Type == "GRN")
                            )
                        {
                            bom.Status = "Item Received";
                            _context.Boms.Update(bom);
                            await _context.SaveChangesAsync();
                        }
                        else if (bom.ReceivedDate.HasValue
                           && (bom.ReceivedQuantity.HasValue && bom.ReceivedQuantity > 0)
                           && !bom.BomSupportFiles.Any(x => x.Type == "DO")
                           && !bom.BomSupportFiles.Any(x => x.Type == "INV")
                           && !String.IsNullOrEmpty(bom.ReceivingRemark)
                           )
                        {
                            bom.Status = "Item Received";
                            _context.Boms.Update(bom);
                            await _context.SaveChangesAsync();
                        }
                    }


                    bom.Project = project;
                    int totalPrice = 0;
                    string vendorName = "";
                    string vendorQuotation = "";
                    var requestor = "";
                    var department = "";
                    bool vendorIsSelected = false;
                    int rowIndex = 0;
                    int quantity = 0;
                    double unitPrice = 0;
                    double prTotalPrice = 0;
                    DateTime? estDeliveryDate = null;

                    if (pr != null && pr.DataResponse != null)
                    {
                        var prResponse = pr.DataResponse;
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                        if (prRoot.data.bomGrid.Count() > 0)
                        {
                            quantity = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalQuantity).FirstOrDefault();
                            unitPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.unitPrice).FirstOrDefault() ?? 0;
                            prTotalPrice = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalPrice).FirstOrDefault() ?? 0;
                            estDeliveryDate = prRoot.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.eta).FirstOrDefault();
                            vendorName = prRoot.data.supplierContainer.FirstOrDefault()?.supplierKey?.name;
                            var oldRequestor = prRoot.data.header.Select(x => x.requester).FirstOrDefault();
                            requestor = Common.Departments.ConvertRequestorWithDept(oldRequestor) ?? "";
                            var dept = oldRequestor.Split('(', ')')[1];
                            if (dept == "FSE" || dept == "FIN")
                            {
                                var commonDept = new Common.Departments();
                                commonDept.UpdatePRRequestorDept(bom.PRId, requestor); //Update requestor in PR
                            }

                        }
                    }

                    if (bom.Status == "Item Received" && PrId != null)
                    {
                        if (bom.BalanceToIssueOutQty.HasValue
                            && bom.IssuedOutDate.HasValue
                            && (bom.IssuedQuantity.HasValue && bom.IssuedQuantity > 0)
                            && !String.IsNullOrEmpty(bom.IssuedPIC)
                            //&& !String.IsNullOrEmpty(bom.remark)
                            )
                        {
                            bom.Status = "Item Issued Out";
                            _context.Boms.Update(bom);
                            await _context.SaveChangesAsync();

                            var requestorShortName = requestor.Substring(0, requestor.IndexOf("("));
                            var issuePICName = employeeList.Where(x => x.Id == Convert.ToInt16(bom.IssuedPIC)).Select(x => x.ShortName).FirstOrDefault();
                            bool isRequestor = issuePICName == requestor.Substring(0, requestor.IndexOf("("));
                            if (!isRequestor)
                            {
                                var bomRequestor = employeeList.Where(x => x.ShortName == requestorShortName && x.IsNotificationOn).FirstOrDefault();
                                var issuedOutPIC = employeeList.Where(x => x.Id == Convert.ToInt16(bom.IssuedPIC) && x.IsNotificationOn).FirstOrDefault();

                                var emailList = new List<string>();
                                emailList.Add(bomRequestor.Email);
                                emailList.Add(issuedOutPIC.Email);
                                emailList = emailList.Where(x => !String.IsNullOrEmpty(x)).ToList();
                                var (msg, html) = CommonHelpers.ItemIssuedOutEmailTemplate(bom.Id.ToString(), "issued out", bom.IssuedPIC, Url.Action("Index", "ReceivingTracker"));

                                var title = "Item No " + bom.Id + " has been issued out by <b>" + issuedOutPIC + "</b>";
                                await _emailService.Send(emailList, title, html);

                            }
                        }
                    }


                    var bomId = bom.Id;
                    //var users = await _context.EmployeeInfos.Select(x => new { x.Id, x.ShortName}).ToListAsync();
                    viewModel = new HistoryModel();

                    viewModel.Id = bomId;
                    //viewModel.PR = pr;
                    viewModel.PrId = bom.PRId ?? 0;
                    viewModel.ItemCategory = bom.ItemCategory;
                    viewModel.isPICs = CommonHelpers.isPics(bom.Project);
                    //viewModel.CreatedDate = bom.CreatedDate;
                    //viewModel.LastUpdatedDate = bom.LastUpdatedDate;
                    viewModel.Requestor = requestor;
                    viewModel.Customer = bom.Project.Customer;
                    // viewModel.Department = department;
                    viewModel.CustomerPO = bom.Project.PoNumber;
                    viewModel.ProjectName = bom.Project.Name;
                    viewModel.ProjectCode = bom.Project.Code;
                    var catId = Convert.ToInt16(bom.Project.Category);
                    viewModel.ProjectCategory = catlist.Where(x => x.Id == catId).Select(x => x.CategoryShortName).FirstOrDefault();
                    viewModel.Quantity = quantity;
                    viewModel.UnitPrice = Math.Round(unitPrice, 4);
                    viewModel.TotalPrice = Math.Round(prTotalPrice, 4);
                    viewModel.EstDeliveryDate = estDeliveryDate;
                    viewModel.ReceivedDate = bom.ReceivedDate;
                    viewModel.ReceivedQuantity = bom.ReceivedQuantity;
                    viewModel.BalanceToReceivedQty = bom.BalanceToReceiveQty ?? 0;
                    viewModel.BalanceToIssueOutQty = bom.BalanceToIssueOutQty ?? 0;
                    viewModel.IssuedPIC = string.IsNullOrEmpty(bom.IssuedPIC) ? "" : GetShortName(bom.IssuedPIC);
                    viewModel.IssuedPICId = bom.IssuedPIC;
                    viewModel.IssuedQty = bom.IssuedQuantity;
                    viewModel.IssueOutDate = bom.IssuedOutDate;
                    viewModel.VendorName = vendorName;
                    viewModel.VendorQuotation = vendorQuotation;
                    viewModel.ProjectId = bom.ProjectId;
                    viewModel.vendorIsSelected = vendorIsSelected;
                    viewModel.rowIndex = rowIndex;
                    viewModel.Description = bom.PartDesc;
                    viewModel.brand = bom.Brand;
                    viewModel.PartNo = bom.PartNumber;
                    viewModel.remark = bom.ReceivingRemark;
                    viewModel.PrName = CommonHelpers.renamePR(bom.PR.Prname, bom.PR.RevisionNo);

                    var supFile = _context.BomSupportFile.Where(x => x.BomId == bom.Id).ToList();
                    foreach (var file in supFile)
                    {
                        var sFileId = file.SupportFileId;
                        file.SupportFile = _context.SupportFile.Where(x => x.Id == sFileId).FirstOrDefault();
                    }
                    var poFile = supFile.Where(x => x.Type == "PO").FirstOrDefault();
                    if (poFile != null)
                    {
                        viewModel.POName = poFile.SupportFile.FileName;
                    }
                    viewModel.PO = supFile;
                    viewModel.DO = supFile;
                    viewModel.Invoice = supFile;
                    viewModel.GRN = supFile;
                    viewModel.QUO = supFile;

                    viewModel.Status = bom.Status;

                    list.Add(viewModel);
                }
            }

            list.OrderBy(x => x.POName).ToList();
            return list;
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


      

        [HttpPost]
        public JsonResult GetReceiving(int bomId)
        {
            try
            {
                if (bomId == 0)
                {
                    return Json(new { msg = "Invalid BOM ID", status = false });
                }

                var getReceiving = _context.Boms.Where(x => x.Id == bomId).FirstOrDefault();
                var poQty = _context.Projects.Where(x => x.Id == getReceiving.ProjectId).Select(x => x.PoQuantity).FirstOrDefault();
                var files = _context.BomSupportFile.Include(x => x.SupportFile).Where(x => x.BomId == bomId).ToList();
                var fileDO = files.Where(x => x.Type == "DO").Select(x => x.SupportFile.FileName).FirstOrDefault();
                var fileINV = files.Where(x => x.Type == "INV").Select(x => x.SupportFile.FileName).FirstOrDefault();

                return Json(new { msg = "OK", status = true, result = getReceiving, poQty = poQty, fileDO = fileDO, fileINV = fileINV });
            }
            catch (Exception ex)
            {
                return Json(new { msg = ex.Message, status = false });
            }
        }


        protected bool isProjectCompleted(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);

            return project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
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
        public string GetPICId(int bomId)
        {
            try
            {
                if (bomId != 0)
                {
                    var PICs = _context.Boms.Where(x => x.Id == bomId).Select(x => x.IssuedPIC).FirstOrDefault();


                    return PICs;

                }
                return "";
            }
            catch (Exception e)
            {
                return "";
            }
        }

       
    }
}
