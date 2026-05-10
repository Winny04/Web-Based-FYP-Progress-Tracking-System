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
using iTextSharp.text;
using iTextSharp.text.pdf;
using DocumentFormat.OpenXml.Office2010.ExcelAc;
using eForm_dotnetCore.Domain;
using DocumentFormat.OpenXml.Drawing.Charts;
using NuGet.Packaging.Signing;

namespace eForm_dotnetCore.Controllers
{
    public class ReceivingTrackerController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IEmailService _emailService;

        public ReceivingTrackerController(EformSophicDemoContext context,
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

        public JsonResult GetReceivingData(DataTableParameters parameters)
        {
            var list = new List<ReceivingModel>();
            var statusList = new List<string> { "PO Approved", "Item Received", "Partial Received", "Item Issued Out", "Completed" };
            var query = _context.Boms
                                .Where(x => statusList.Contains(x.Status) && x.PRId.HasValue)
                                .AsQueryable();

            var prQuery = _context.Prs
                .Include(x => x.Project)
                .ThenInclude(x => x.CategoryInfo)
                .Include(x => x.Requestor).AsQueryable();

            var fileQuery = _context.SupportFile.AsQueryable();

            if (!string.IsNullOrEmpty(parameters.Search?.value))
            {
                fileQuery = fileQuery.Where(x => x.FileName.Contains(parameters.Search.value)).AsQueryable();
                var fileList = fileQuery.ToList();

                if (fileList.Any())
                {
                    var fileIds = fileList.Select(x => x.Id).ToList();
                    var bomIdsFromFile = _context.BomSupportFile.Where(x => x.Type == "PO" && fileIds.Contains(x.SupportFileId))
                        .Select(x => x.BomId).ToList();
                    if (bomIdsFromFile.Any())
                    {
                        query = query.Where(x => bomIdsFromFile.Contains(x.Id)).AsQueryable();
                    }
                }

                var checkPrs = prQuery.Where(x => x.Prname.Contains(parameters.Search.value)).AsQueryable();
                if (checkPrs.Count() > 0)
                {
                    query = query.Where(x => x.PRId.HasValue && x.PRId == checkPrs.FirstOrDefault().Id).AsQueryable();
                }
            }

            var total = query.Count();

            var boms = query
                .OrderByDescending(x => x.Status != "PO Approved")
                .ThenByDescending(x => x.LastUpdatedDate)
                .ThenByDescending(x => x.PRId)
                .ThenBy(x => x.Id)
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToList();

            foreach (var bom in boms)
            {
                if (!bom.PRId.HasValue) continue;

                var pr = prQuery.FirstOrDefault(x => x.Id == bom.PRId.Value);
                if (pr == null) continue;

                var po = _context.BomSupportFile
                        .Include(x => x.SupportFile)
                        .Where(x => x.BomId == bom.Id && x.Type == "PO").FirstOrDefault();

                var viewModel = new ReceivingModel();
                viewModel.Status = bom.Status;

                viewModel.Id = bom.Id; 
                viewModel.PrId = bom.PRId.Value;
                viewModel.Requestor = pr.Requestor.FullName;
                viewModel.Customer = pr.Project.Customer;
                viewModel.CustomerPO = pr.Project.PoNumber;
                viewModel.ProjectName = pr.Project.Name;
                viewModel.ProjectCode = pr.Project.Code;
                viewModel.ProjectCategory = pr.Project.CategoryInfo.CategoryShortName;
                viewModel.PrName = pr.Prname;

                if (po != null)
                {
                    viewModel.POName = po.SupportFile.FileName;
                }
                viewModel.PO = po;

                list.Add(viewModel);
            }

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = list
            });
        }

        public JsonResult GetReceivedItemData(DataTableParameters parameters)
        {
            var list = new List<ReceivingModel>();
            var statusList = new List<string> { "Item Received"};
            var query = _context.Boms
                                .Where(x => statusList.Contains(x.Status) && x.PRId.HasValue)
                                .AsQueryable();

            var prQuery = _context.Prs
                .Include(x => x.Project)
                .ThenInclude(x => x.CategoryInfo)
                .Include(x => x.Requestor).AsQueryable();

            var fileQuery = _context.SupportFile.AsQueryable();

            if (!string.IsNullOrEmpty(parameters.Search?.value))
            {
                fileQuery = fileQuery.Where(x => x.FileName.Contains(parameters.Search.value)).AsQueryable();
                var fileList = fileQuery.ToList();

                if (fileList.Any())
                {
                    var fileIds = fileList.Select(x => x.Id).ToList();
                    var bomIdsFromFile = _context.BomSupportFile.Where(x => x.Type == "PO" && fileIds.Contains(x.SupportFileId))
                        .Select(x => x.BomId).ToList();
                    if (bomIdsFromFile.Any())
                    {
                        query = query.Where(x => bomIdsFromFile.Contains(x.Id)).AsQueryable();
                    }
                }

                var checkPrs = prQuery.Where(x => x.Prname.Contains(parameters.Search.value)).AsQueryable();
                if (checkPrs.Count() > 0)
                {
                    query = query.Where(x => x.PRId.HasValue && x.PRId == checkPrs.FirstOrDefault().Id).AsQueryable();
                }
            }

            var total = query.Select(x => x.PRId).Distinct().Count();
            var prItems = query.Select(x => x.PRId).Distinct().OrderByDescending(x => x).Skip(parameters.Start).Take(parameters.Length).ToList();

            foreach (var item in prItems)
            {
                var bom = _context.Boms.FirstOrDefault(x => x.PRId == item);
                if (bom == null)
                    continue;

                var pr = prQuery.FirstOrDefault(x => x.Id == item);
                var status = bom.Status;
                var po = _context.BomSupportFile
                        .Include(x => x.SupportFile)
                        .Where(x => x.BomId == bom.Id && x.Type == "PO").FirstOrDefault();

                var viewModel = new ReceivingModel();
                viewModel.Status = status;

                viewModel.Id = item.Value;
                viewModel.PrId = item.Value;
                viewModel.Requestor = pr.Requestor.FullName;
                viewModel.Customer = pr.Project.Customer;
                viewModel.CustomerPO = pr.Project.PoNumber;
                viewModel.ProjectName = pr.Project.Name;
                viewModel.ProjectCode = pr.Project.Code;
                viewModel.ProjectCategory = pr.Project.CategoryInfo.CategoryShortName;
                viewModel.PrName = pr.Prname;

                if (po != null)
                {
                    viewModel.POName = po.SupportFile.FileName;
                }
                viewModel.PO = po;

                list.Add(viewModel);
            }

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = list
            });
        }

        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Requestor,Quality,Finance")]
        public async Task<IActionResult> View(int PrId)
        {
            //var viewModel = new ReceivingTrackerViewModel();
            var list = new ReceivingTrackerViewModel();
            try
            {
                list = await LoadBOMItem(PrId);
                list.ReceivingList.OrderBy(x => x.Id).ToList();
                ViewBag.Status = list.ReceivingList.Select(x => x.Status).FirstOrDefault();
                ViewBag.PrId = list.ReceivingList.Select(x => x.PrId).FirstOrDefault();
                ViewBag.HasItemIssueOut = list.ReceivingList.Where(x => x.Status == "Item Issued Out").Any();
                return View(list);

            }
            catch (Exception ex)
            {
                throw;
            }
        }


        [HttpPost]
        [ClaimRequirement("Type", "HOD,CDO,GFC,CEO,Receiving")]
        public JsonResult Edit(int id, int prId, DateTime? receivingDate, int? receiveQty, int? balanceRQ, DateTime? issueOutDate, string? issueOutBy, int? issueOutQty, int? balIssueOutQty, string? remark)
        {
            var newForm = _context.Boms.FirstOrDefault(x => x.Id == id);

            if (receivingDate != null)
                newForm.ReceivedDate = receivingDate;

            if (receiveQty.HasValue)
                newForm.ReceivedQuantity = receiveQty.Value;

            if (balanceRQ.HasValue)
                newForm.BalanceToReceiveQty = balanceRQ.Value;

            if (issueOutDate.HasValue)
                newForm.IssuedOutDate = issueOutDate;

            if (!String.IsNullOrEmpty(issueOutBy))
                newForm.IssuedPIC = issueOutBy;

            if (issueOutQty.HasValue)
                newForm.IssuedQuantity = issueOutQty.Value;

            if (balIssueOutQty.HasValue)
                newForm.BalanceToIssueOutQty = balIssueOutQty.Value;

            if (!String.IsNullOrEmpty(remark) && prId > 0)
            {
                var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
                pr.Remark = remark;
                _context.Prs.Update(pr);
            }

            newForm.LastUpdatedDate = DateTime.Now;
            _context.Boms.Update(newForm);

            if (_context.SaveChanges() > 0)
            {
                return Json(new { result = true });
            }
            else
            {
                return Json(new { result = false });
            }
        }

        [ClaimRequirement("Type", "HOD,CDO,GFC,CEO,Receiving")]
        public async Task<IActionResult> DeleteRec(int? id)
        {
            if (!id.HasValue && !_context.Boms.Any(x => x.Id == id))
                throw new NullReferenceException(nameof(id));

            var targetBOM = _context.Boms.Where(x => x.Id == id).FirstOrDefault();
            //var project = _context.Projects.FirstOrDefault(x => x.Id == targetBOM.ProjectId);
            //var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
            //if (!authorizationResult.Succeeded)
            //    return Forbid();

            var targetBOMSupportFile = _context.BomSupportFile.Where(x => x.BomId == id && (x.Type == "DO" || x.Type == "INV" || x.Type == "GRN")).ToList();
            var supFileIds = targetBOMSupportFile.Select(x => x.SupportFileId).ToList();
            var targetSupportFile = _context.SupportFile.Where(x => supFileIds.Contains(x.Id)).ToList();

            foreach (var file in targetSupportFile)
            {
                _fileWriter.DeleteDocument(file.FileName);
            }

            _context.SupportFile.RemoveRange(targetSupportFile);
            //_context.Boms.Remove(targetBOM);
            targetBOM.ReceivedDate = null;
            targetBOM.ReceivedQuantity = 0;
            targetBOM.BalanceToReceiveQty = 0;
            targetBOM.IssuedOutDate = null;
            targetBOM.IssuedQuantity = 0;
            targetBOM.BalanceToIssueOutQty = 0;
            targetBOM.IssuedPIC = null;
            targetBOM.ReceivingRemark = "";
            targetBOM.Status = "PO Approved";
            _context.Boms.Update(targetBOM);
            await _context.SaveChangesAsync();

            var auditTrail = await _auditTrailService.InsertLogAsync(
                       Common.LogLevel.Information, "The Receiving from BOM with ID" + targetBOM.Id + "has been deleted",
                       fullMessage: "Delete Receiving from Receiving Tracker",
                       tableChange: nameof(Bom),
                       dataBefore: JsonConvert.SerializeObject(targetBOM),
                       dataAfter: "",
                       empId: int.Parse(HttpContext.GetUserId()),
                       ProjectId: targetBOM.ProjectId
                       );
            return NoContent();

        }

        // GET: BOMs/Edit/5
        [HttpGet]
        [ClaimRequirement("Type", "HOD,CDO,GFC,CEO,Receiving")]
        public async Task<JsonResult> Edit(int? bomId)
        {
            if (bomId == null)
            {
                return Json(new { msg = "BOM ID not found." });
            }

            var BOMs = await LoadBOMList();
            if (BOMs == null)
            {
                return Json(new { msg = "Data not found.", id = bomId });
            }

            var BOM = BOMs.ReceivingList.FirstOrDefault();
            BOM.PO = null; //to prevent json error
            BOM.DO = null; //to prevent json error
            BOM.Invoice = null; //to prevent json error
            BOM.GRN = null; //to prevent json error

            return Json(new { data = BOM, msg = "", id = BOM.Id });
        }


        public async Task<ReceivingTrackerViewModel> LoadBOMList()
        {
            var model = new ReceivingTrackerViewModel();
            var viewModel = new ReceivingModel();
            var list = new List<ReceivingModel>();
            var tempDatalist = new List<ReceivingModel>();
            var boms = new List<Bom>();
            var statusList = new List<string> { "PO Approved", "Partial Received", "Item Received", "Item Issued Out", "Completed" };
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

            //var issueOutStatuslist = await _context.BomIssueOutStatus.Where(x => boms.Select(x => x.Id).Contains(x.BomId)).ToListAsync();
            var prs = await _context.Prs.Where(x => allPrIds.Contains(x.Id)).Include(x => x.Project).ToListAsync();
            var catlist = await _context.Category.ToListAsync();
            var superAdmins = CommonExtensions.SuperAdminList();
            var projectQuery = _context.Projects.AsQueryable();


            if (boms.Any())
            {
                foreach (var bom in boms)
                {
                    viewModel = new ReceivingModel();

                    var currentPR = prs.Where(x => x.Id == bom.PRId).FirstOrDefault();

                    var bomInPR = await _context.Boms.Where(x => x.PRId == bom.PRId).Select(x => new { x.Id, x.Status }).ToListAsync();
                    var bomStatusInPR = bomInPR.Select(x => x.Status).ToList();
                    if (!bomStatusInPR.Where(x => x != "Completed").Any()) //Check if still have no completed item
                    {
                        continue;
                    }

                    viewModel.Status = bomStatusInPR[0];

                    viewModel.hasIssueOut = bomInPR.Where(x => x.Status == "Item Issued Out").Any();

                    var bomId = bom.Id;
                    viewModel.Id = bomId;
                    viewModel.PrId = bom.PRId ?? 0;
                    viewModel.isPICs = CommonHelpers.isPics(bom.Project);
                    viewModel.Requestor = _context.EmployeeInfos.FirstOrDefault(x => x.Id == currentPR.RequestorId).FullName ?? "";
                    viewModel.Customer = currentPR.Project.Customer;
                    viewModel.CustomerPO = currentPR.Project.PoNumber;
                    viewModel.ProjectName = currentPR.Project.Name;
                    viewModel.ProjectCode = currentPR.Project.Code;
                    var catId = Convert.ToInt16(currentPR.Project.Category);
                    viewModel.ProjectCategory = catlist.Where(x => x.Id == catId).Select(x => x.CategoryShortName).FirstOrDefault();
                    //viewModel.EstDeliveryDate = estDeliveryDate;
                    viewModel.IssuedPICId = bom.IssuedPIC;
                    viewModel.ProjectId = bom.ProjectId;
                    viewModel.PrName = CommonHelpers.renamePR(currentPR.Prname, currentPR.RevisionNo);

                    var poFile = _context.BomSupportFile
                        .Include(x => x.SupportFile)
                        .Where(x => x.BomId == bom.Id && x.Type == "PO").FirstOrDefault();

                    if (poFile != null)
                    {
                        viewModel.POName = poFile.SupportFile.FileName;
                    }
                    viewModel.PO = poFile;

                    list.Add(viewModel);
                }
            }


            model.ReceivingList = list.OrderBy(x => x.POName).ToList();
            return model;
        }

        [HttpGet]
        public IActionResult OverETAItem()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ReceivedItem()
        {
            return View();
        }

        public async Task<ReceivingTrackerViewModel> LoadBOMItem(int? PrId)
        {
            var model = new ReceivingTrackerViewModel();
            var viewModel = new ReceivingModel();
            var list = new List<ReceivingModel>();
            var tempDatalist = new List<ReceivingModel>();
            var boms = new List<Bom>();
            var statusList = new List<string> { "PO Approved", "Partial Received", "Item Received", "Item Issued Out", "Completed" };
            if (PrId != null) //Select by PR
            {
                boms = await _context.Boms
                         .Include(x => x.BomSupportFiles)
                         .Where(x => statusList.Contains(x.Status) && x.PRId == PrId).ToListAsync();
            }
            else //
            {
                return model;
            }//end else


            var pr = await _context.Prs.Where(x => x.Id == PrId).FirstOrDefaultAsync();
            var catlist = await _context.Category.ToListAsync();
            var superAdmins = CommonExtensions.SuperAdminList();
            var employeeList = await _context.EmployeeInfos
                .Include(x => x.EmpDepartment)
                .Where(x => !superAdmins.Contains(x.UserName) && !string.IsNullOrEmpty(x.ShortName) && x.DepartmentId.HasValue).OrderBy(x => x.ShortName).ToListAsync();
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
                    viewModel = new ReceivingModel();

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

                    var supFile = _context.BomSupportFile
                        .Include(x => x.SupportFile)
                        .Where(x => x.BomId == bom.Id)
                        .ToList();
                    
                    var poFile = supFile.Where(x => x.Type == "PO").FirstOrDefault();
                    if (poFile != null)
                    {
                        viewModel.POName = poFile.SupportFile.FileName;
                    }
                    viewModel.PO = poFile != null ? poFile : null;
                    viewModel.DO = supFile;
                    viewModel.Invoice = supFile;
                    viewModel.GRN = supFile;
                    viewModel.QUO = supFile;

                    viewModel.Status = bom.Status;

                    list.Add(viewModel);
                }
            }

            model.ReceivingList = list.OrderBy(x => x.POName).ToList();
            return model;
        }

        [HttpPost]
        public async Task<JsonResult> attachFile(int prId, List<IFormFile> files, List<string> types)
        {
            try
            {
                if (prId == 0)
                {
                    return Json(new { result = false, msg = "BOM ID not found." });
                }

                var boms = _context.Boms.Where(x => x.PRId == prId).ToList();
                var projects = _context.Projects.Where(d => boms.Select(x => x.ProjectId).Contains(d.Id)).ToList();

                var auth = new AuthResource("Type", "Finance", null);
                var authorizationResult = await _authorizeService.AuthorizeAsync(User, auth, new UserTypeRequirement());
                if (!authorizationResult.Succeeded)
                    return Json(new { result = false, msg = "You doesn't have permission to attach file." });

                if (files != null)
                {
                    //foreach (var t in types)
                    //{
                    //    foreach (var bom in boms)
                    //    {
                    //        var exists = _context.BomSupportFile.FirstOrDefault(x => x.BomId == bom.Id && x.Type == t);
                    //        if (exists != null)
                    //        {
                    //            _context.BomSupportFile.Remove(exists);
                    //        }
                    //    }
                    //}

                    //await _context.SaveChangesAsync();

                    foreach (var file in files)
                    {
                        var FileDic = "documents";
                        string rootPath = this.Environment.WebRootPath; //wwwroot folder
                        string FilePath = Path.Combine(rootPath + "/" + FileDic);


                        //Create a Folder.
                        if (!Directory.Exists(FilePath))
                            Directory.CreateDirectory(FilePath);
                        var extension = Path.GetExtension(file.FileName);
                        var newfilename = Guid.NewGuid().ToString() + extension;
                        var filePath = Path.Combine(FilePath, newfilename);


                        using (FileStream stream = new FileStream(filePath, FileMode.Create))
                        {
                            file.CopyTo(stream);
                        }

                        for (int i = 0; i < types.Count(); i++)
                        {
                            var sf = new SupportFile();
                            sf.FileName = file.FileName;
                            sf.ContentType = extension.ToString();
                            sf.Size = new FileInfo(filePath).Length.ToString();
                            sf.CreatedAt = DateTime.Now;
                            sf.FileNameOnDisk = newfilename;


                            foreach (var bom in boms)
                            {
                                bom.BomSupportFiles.Add(new BomSupportFile
                                {
                                    SupportFile = sf,
                                    Type = types[i]
                                });
                            }
                        }



                        //var upFiles = await _fileWriter.UploadDocument(file);

                        //for (int i = 0; i < types.Count(); i++)
                        //{
                        //    upFiles.CreatedAt = DateTime.Now;
                        //    foreach (var bom in boms)
                        //    {
                        //        bom.BomSupportFiles.Add(new BomSupportFile
                        //        {
                        //            SupportFile = upFiles,
                        //            Type = types[i]
                        //        });
                        //    }
                        //}
                    }
                    await _context.SaveChangesAsync();
                }
                return Json(new { result = true });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = "An error occur during save file. " + ex.Message });
            }
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
        public async Task<ActionResult> EditRec(List<ReceivingModel> data, List<IFormFile> DO, List<IFormFile> GRN, List<IFormFile> Invoice, List<string> types)
        {
            try
            {
                if (data.Any())
                {
                    foreach (var item in data)
                    {
                        var bomId = item.BomId;
                        if (bomId == 0)
                        {
                            return NotFound();
                        }
                        if (item.IssuedQty2 != 0) //new added qty for partial issued out
                        {
                            item.IssuedQty += item.IssuedQty2 ?? 0;
                        }

                        if (item.ReceivedQuantity2 != 0) //new added qty for partial received
                        {
                            item.ReceivedQuantity += item.ReceivedQuantity2 ?? 0;
                        }

                        if (!item.ReceivedDate.HasValue)
                        {
                            item.ReceivedQuantity = null;
                        }

                        if (String.IsNullOrEmpty(item.IssuedPIC))
                        {
                            item.IssueOutDate = null;
                        }


                        var bom = _context.Boms.Where(x => x.Id == bomId).FirstOrDefault();
                        bom.ReceivedDate = item.ReceivedDate;
                        bom.ReceivedQuantity = item.ReceivedQuantity;
                        bom.BalanceToReceiveQty = item.BalanceToReceivedQty;
                        bom.IssuedPIC = item.IssuedPIC;
                        bom.IssuedOutDate = item.IssueOutDate;
                        bom.IssuedQuantity = item.IssuedQty;
                        bom.BalanceToIssueOutQty = item.BalanceToIssueOutQty;
                        bom.ReceivingRemark = item.remark;
                        bom.LastUpdatedDate = DateTime.Now;
                        _context.Update(bom);

                    }
                    await _context.SaveChangesAsync();



                    return Json(new { status = true });
                }
                return Json(new { status = true });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "An error occur. " + ex.Message });
            }
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

        [HttpPost]
        public async Task<JsonResult> RejectRec(int bomId, string remark)
        {
            try
            {
                if (bomId <= 0)
                {
                    return Json(new { msg = "Invalid BOM ID.", status = false });
                }

                var bom = await _context.Boms.Where(x => x.Id == bomId).FirstOrDefaultAsync();
                //var pr = await _context.Prs.Where(x => x.Id == prId && x.Bomid == bomId).FirstOrDefaultAsync();

                bom.Status = "Receiving Reject";
                bom.IsPOUpdated = false;
                bom.ReceivingRemark = remark;
                //pr.PRApproveDate = null;


                _context.Boms.Update(bom);
                //_context.Prs.Update(pr);
                await _context.SaveChangesAsync();

                return Json(new { msg = "Receiving Rejected", status = true });
            }
            catch (Exception ex)
            {
                return Json(new { msg = ex.Message, status = false });
            }
        }

        [HttpPost]
        public async Task<ActionResult> editPICs(string picsContainer, int bomId)
        {
            var bom = _context.Boms.FirstOrDefault(x => x.Id == bomId);

            //var authorizationResult = await _authorizeService.AuthorizeAsync(User, bom, new PMOrPICRequirement());
            //if (!authorizationResult.Succeeded)
            //    return Forbid();

            if (!string.IsNullOrEmpty(picsContainer))
            {
                bom.IssuedPIC = picsContainer;
                _context.Boms.Update(bom);
                _context.SaveChanges();
            }
            else
            {
                bom.IssuedPIC = string.Empty;
                _context.Boms.Update(bom);
                _context.SaveChanges();
            }
            return RedirectToAction("Index", new { id = bomId });
        }


        public async Task<ActionResult> RemoveFile(int? id, int projectId)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == projectId);

            var authorizationResult = await _authorizeService.AuthorizeAsync(User, project, new PMOrPICRequirement());
            if (!authorizationResult.Succeeded || isProjectCompleted(projectId))
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


        public JsonResult DeleteUploadedFile(int prId, string fileType, int sfId)
        {
            try
            {
                if (prId == 0)
                {
                    return Json(new { status = false, msg = "PR ID not found." });
                }

                if (sfId == 0)
                {
                    return Json(new { status = false, msg = "Support File not found." });
                }

                var bomIds = _context.Boms.Where(x => x.PRId == prId).Select(x => x.Id).ToList();
                //var supportFileIds = _context.BomSupportFile.Where(x => bomIds.Contains(x.BomId) && x.Type == fileType).Select(x => x.SupportFileId).ToList();
                //var supportFiles = _context.SupportFile.Where(x => supportFileIds.Contains(x.Id)).ToList();
                var supportFiles = _context.SupportFile.Where(x => x.Id == sfId).ToList();
                foreach (var file in supportFiles)
                {
                    _fileWriter.DeleteDocument(file.FileNameOnDisk);
                }
                _context.SupportFile.RemoveRange(supportFiles);
                _context.SaveChanges();

                return Json(new { status = false, msg = "Delete Successful" });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "An error occur in delete file type '" + fileType + "' . " + ex.Message });
            }
        }

        public JsonResult ResetBOM(int bomId)
        {
            if (bomId == 0)
            {
                return Json(new { status = false, msg = "Item ID not found." });
            }

            var bom = _context.Boms.Where(x => x.Id == bomId).FirstOrDefault();
            if (bom == null)
            {
                return Json(new { status = false, msg = "Invalid Item ID. Item not found" });
            }

            if (bom.Status == "Item Issued Out" || bom.Status == "Completed")
            {
                return Json(new { status = false, msg = "Reset is not allowed after item is being issued out." });
            }

            bom.ReceivedDate = null;
            bom.ReceivedQuantity = null;
            bom.BalanceToReceiveQty = null;
            bom.IssuedOutDate = null;
            bom.IssuedPIC = null;
            bom.IssuedQuantity = null;
            bom.BalanceToIssueOutQty = null;
            bom.Status = "PO Approved";
            _context.Boms.Update(bom);
            _context.SaveChanges();

            return Json(new { status = true, msg = "Item reset successful." });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateIssueOutFormPDF(string PrIds = null, string BomIds = null)
        {
            //try
            //{
            //    var itemList = new List<IssueOutItemList>();
            //    if (!String.IsNullOrEmpty(PrIds)) // From Receiving Tracker main page
            //    {
            //        var PrIdList = new List<int>();
            //        var splitPrids = PrIds.Split(",").ToList();
            //        foreach (var pr in splitPrids)
            //        {
            //            PrIdList.Add(Convert.ToInt16(pr));
            //        }


            //        foreach (var prId in PrIdList)
            //        {
            //            var model = new IssueOutItemList();
            //            var itemListResult = await _context.Boms
            //                                 .Include(x => x.BomSupportFiles)
            //                                 .Where(x => x.PRId == prId && x.Status == "Item Received")
            //                                 .Select(x => new IssueOutItemList
            //                                 {
            //                                     ItemId = x.Id,
            //                                     PartDescription = x.PartDesc,
            //                                     IssueQuantity = x.IssuedQuantity ?? 0,
            //                                     BalanceToIssueQuantity = x.BalanceToIssueOutQty ?? 0,
            //                                     ReceivedQuantity = x.ReceivedQuantity ?? 0,
            //                                     IssueLocation = "",
            //                                     PONumber = x.BomSupportFiles.Where(x => x.Type == "PO").Select(x => x.SupportFile.FileName).FirstOrDefault(),
            //                                 }).ToListAsync();

            //            itemList.AddRange(itemListResult);
            //        }
            //    }
            //    else if (!String.IsNullOrEmpty(BomIds)) //From Item List
            //    {
            //        var BomIdList = new List<int>();
            //        var splitBomIds = BomIds.Split(",").ToList().Distinct();
            //        foreach (var bom in splitBomIds)
            //        {
            //            BomIdList.Add(Convert.ToInt16(bom));
            //        }

            //        foreach (var bomId in BomIdList)
            //        {
            //            var model = new IssueOutItemList();
            //            var itemListResult = await _context.Boms
            //                                 .Include(x => x.BomSupportFiles)
            //                                 .Where(x => x.Id == bomId)
            //                                 .Select(x => new IssueOutItemList
            //                                 {
            //                                     ItemId = x.Id,
            //                                     PartDescription = x.PartDesc,
            //                                     IssueQuantity = x.IssuedQuantity ?? 0,
            //                                     BalanceToIssueQuantity = x.BalanceToIssueOutQty ?? 0,
            //                                     ReceivedQuantity = x.ReceivedQuantity ?? 0,
            //                                     IssueLocation = "",
            //                                     PONumber = x.BomSupportFiles.Where(x => x.Type == "PO").Select(x => x.SupportFile.FileName).FirstOrDefault(),
            //                                 }).FirstOrDefaultAsync();


            //            itemList.Add(itemListResult); //remove fully issued out records
            //        }

            //    }


            //    #region Generate PDF Section
            //    //Set font size and style 
            //    FontFactory.RegisterDirectories();
            //    Font fontSmaller = new Font(FontFactory.GetFont("Arial", 8, Font.NORMAL));
            //    Font fontSmall = new Font(FontFactory.GetFont("Arial", 10, Font.NORMAL));
            //    Font fontSmallBold = new Font(FontFactory.GetFont("Arial", 9, Font.BOLD));
            //    Font fontMedium = new Font(FontFactory.GetFont("Arial", 11, Font.NORMAL));
            //    Font fontLargeBold = new Font(FontFactory.GetFont("Arial", 14, Font.BOLD));


            //    Document pdfDoc = new Document(PageSize.A4, 10, 5, 40, 40);
            //    System.IO.MemoryStream stream = new System.IO.MemoryStream();
            //    PdfWriter writer = PdfWriter.GetInstance(pdfDoc, stream);
            //    pdfDoc.Open();

            //    //Form Header 
            //    PdfPTable table = new PdfPTable(1);

            //    PdfPCell adjustNumberCell = new PdfPCell(new Phrase("MIF-0282", fontSmaller));
            //    adjustNumberCell.Colspan = 1;
            //    adjustNumberCell.HorizontalAlignment = Element.ALIGN_RIGHT;
            //    adjustNumberCell.Border = 0;
            //    table.AddCell(adjustNumberCell);

            //    PdfPCell cell = new PdfPCell(new Phrase("SOPHIC AUTOMATION SDN BHD (794994-D)", fontLargeBold));
            //    cell.Colspan = 1;
            //    cell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    cell.PaddingTop = 5;
            //    cell.PaddingBottom = 5;
            //    table.AddCell(cell);

            //    PdfPCell cell2 = new PdfPCell(new Phrase("MATERIAL ISSUE FORM", fontSmall));
            //    cell2.PaddingTop = 5;
            //    cell2.PaddingBottom = 5;
            //    cell2.HorizontalAlignment = Element.ALIGN_CENTER;

            //    table.AddCell(cell2);

            //    pdfDoc.Add(table);
            //    //End Form Header


            //    //Item List Table
            //    PdfPTable itemListTable = new PdfPTable(5);
            //    table.HorizontalAlignment = 0;

            //    itemListTable.SetTotalWidth(new float[]
            //    {
            //        (PageSize.A4.Rotate().Width - 25) / 6,
            //        (PageSize.A4.Rotate().Width - 25) / 2,
            //        (PageSize.A4.Rotate().Width - 25) / 2,
            //        (PageSize.A4.Rotate().Width - 25) / 3,
            //        (PageSize.A4.Rotate().Width - 25) / 3
            //    });

            //    //Item Table Header
            //    PdfPCell requestIssueDateCell = new PdfPCell(new Phrase("Requested Issue Date: ", fontMedium));
            //    requestIssueDateCell.Colspan = 5;
            //    requestIssueDateCell.Border = 0;
            //    requestIssueDateCell.PaddingTop = 20;
            //    requestIssueDateCell.PaddingBottom = 10;
            //    itemListTable.AddCell(requestIssueDateCell);

            //    PdfPCell itemHeaderCell = new PdfPCell(new Phrase("No", fontSmallBold));
            //    itemHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    itemListTable.AddCell(itemHeaderCell);

            //    itemHeaderCell = new PdfPCell(new Phrase("SAPO#", fontSmallBold));
            //    itemHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    itemListTable.AddCell(itemHeaderCell);

            //    itemHeaderCell = new PdfPCell(new Phrase("SAPO# PART DESCRIPTION", fontSmallBold));
            //    itemHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    itemListTable.AddCell(itemHeaderCell);

            //    itemHeaderCell = new PdfPCell(new Phrase("ISSUE QUANTITY", fontSmallBold));
            //    itemHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    itemListTable.AddCell(itemHeaderCell);

            //    itemHeaderCell = new PdfPCell(new Phrase("ISSUE LOCATION", fontSmallBold));
            //    itemHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //    itemListTable.AddCell(itemHeaderCell);

            //    int maxRowInPage = 17;
            //    int itemRow = 0;
            //    foreach (var item in itemList)
            //    {
            //        itemRow++;
            //        PdfPCell itemNoCell = new PdfPCell(new Phrase(item.ItemId.ToString(), fontSmaller));
            //        itemNoCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //        itemNoCell.VerticalAlignment = Element.ALIGN_TOP;
            //        itemNoCell.FixedHeight = 30f;
            //        itemNoCell.PaddingTop = 5;
            //        itemListTable.AddCell(itemNoCell);

            //        PdfPCell PoNumberCell = new PdfPCell(new Phrase(item.PONumber.Split('-')[0], fontSmall));
            //        PoNumberCell.HorizontalAlignment = Element.ALIGN_LEFT;
            //        PoNumberCell.FixedHeight = 30f;
            //        itemListTable.AddCell(PoNumberCell);

            //        PdfPCell partDescCell = new PdfPCell(new Phrase(item.PartDescription, fontSmaller));
            //        partDescCell.HorizontalAlignment = Element.ALIGN_LEFT;
            //        partDescCell.FixedHeight = 30f;
            //        itemListTable.AddCell(partDescCell);

            //        var currentIssueQty = 0;
            //        var previousIssueOutQty = 0;
            //        var LastIssuedOutQty = 0;
            //        if (bomIssueStatus != null)
            //        {
            //            previousIssueOutQty = bomIssueStatus.IssuedOutQty ?? 0;
            //            LastIssuedOutQty = bomIssueStatus.LastIssuedOutQty ?? 0;
            //        }

            //        if (item.IssueQuantity != previousIssueOutQty)
            //        {
            //            currentIssueQty = item.IssueQuantity - previousIssueOutQty;
            //        }
            //        else
            //        {
            //            currentIssueQty = LastIssuedOutQty;
            //        }
            //        PdfPCell issueQtyCell = new PdfPCell(new Phrase(currentIssueQty.ToString(), fontSmall));
            //        issueQtyCell.HorizontalAlignment = Element.ALIGN_CENTER;
            //        issueQtyCell.VerticalAlignment = Element.ALIGN_TOP;
            //        issueQtyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(issueQtyCell);

            //        PdfPCell issueLocationCell = new PdfPCell(new Phrase(item.IssueLocation, fontSmall));
            //        issueLocationCell.FixedHeight = 30f;
            //        itemListTable.AddCell(issueLocationCell);

            //        if (itemRow > maxRowInPage) //If reach max row, continue loop on next page
            //        {
            //            var footer2 = PDFFooter();
            //            foreach (var footerCell in footer2)
            //            {
            //                itemListTable.AddCell(footerCell);
            //            }

            //            pdfDoc.Add(itemListTable);

            //            //New Page start here
            //            itemListTable = new PdfPTable(5);
            //            itemListTable.SetTotalWidth(new float[]
            //            {
            //                     (PageSize.A4.Rotate().Width - 25) / 6,
            //                     (PageSize.A4.Rotate().Width - 25) / 2,
            //                     (PageSize.A4.Rotate().Width - 25) / 2,
            //                     (PageSize.A4.Rotate().Width - 25) / 3,
            //                     (PageSize.A4.Rotate().Width - 25) / 3
            //            });

            //            pdfDoc.NewPage();
            //            itemRow = 0;

            //            //add header
            //            PdfPTable headerTable2 = new PdfPTable(1);

            //            PdfPCell adjustNumberCell2 = new PdfPCell(new Phrase("MIF-0282", fontSmaller));
            //            adjustNumberCell2.Colspan = 1;
            //            adjustNumberCell2.HorizontalAlignment = Element.ALIGN_RIGHT;
            //            adjustNumberCell2.Border = 0;
            //            headerTable2.AddCell(adjustNumberCell2);

            //            PdfPCell sophicCell2 = new PdfPCell(new Phrase("SOPHIC AUTOMATION SDN BHD (794994-D)", fontLargeBold));
            //            sophicCell2.Colspan = 1;
            //            sophicCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            sophicCell2.PaddingTop = 5;
            //            sophicCell2.PaddingBottom = 5;
            //            headerTable2.AddCell(sophicCell2);


            //            PdfPCell formNameCell2 = new PdfPCell(new Phrase("MATERIAL ISSUE FORM", fontSmall));
            //            formNameCell2.PaddingTop = 5;
            //            formNameCell2.PaddingBottom = 5;
            //            formNameCell2.HorizontalAlignment = Element.ALIGN_CENTER;

            //            headerTable2.AddCell(formNameCell2);

            //            pdfDoc.Add(headerTable2);

            //            PdfPCell requestIssueDateCell2 = new PdfPCell(new Phrase("Requested Issue Date: ", fontMedium));
            //            requestIssueDateCell2.Colspan = 5;
            //            requestIssueDateCell2.Border = 0;
            //            requestIssueDateCell2.PaddingTop = 20;
            //            requestIssueDateCell2.PaddingBottom = 10;
            //            itemListTable.AddCell(requestIssueDateCell2);


            //            //new table header
            //            PdfPCell itemHeaderCell2 = new PdfPCell(new Phrase("No", fontSmallBold));
            //            itemHeaderCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            itemListTable.AddCell(itemHeaderCell2);

            //            itemHeaderCell2 = new PdfPCell(new Phrase("SAPO#", fontSmallBold));
            //            itemHeaderCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            itemListTable.AddCell(itemHeaderCell2);

            //            itemHeaderCell2 = new PdfPCell(new Phrase("SAPO# PART DESCRIPTION", fontSmallBold));
            //            itemHeaderCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            itemListTable.AddCell(itemHeaderCell2);

            //            itemHeaderCell2 = new PdfPCell(new Phrase("ISSUE QUANTITY", fontSmallBold));
            //            itemHeaderCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            itemListTable.AddCell(itemHeaderCell2);

            //            itemHeaderCell2 = new PdfPCell(new Phrase("ISSUE LOCATION", fontSmallBold));
            //            itemHeaderCell2.HorizontalAlignment = Element.ALIGN_CENTER;
            //            itemListTable.AddCell(itemHeaderCell2);
            //        }//end next page section

            //    }


            //    for (int i = 1; i <= maxRowInPage - itemRow; i++)
            //    {
            //        PdfPCell emptyCell = new PdfPCell(new Phrase(" "));
            //        emptyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(emptyCell);

            //        emptyCell = new PdfPCell(new Phrase(" "));
            //        emptyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(emptyCell);

            //        emptyCell = new PdfPCell(new Phrase(" "));
            //        emptyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(emptyCell);

            //        emptyCell = new PdfPCell(new Phrase(" "));
            //        emptyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(emptyCell);

            //        emptyCell = new PdfPCell(new Phrase(" "));
            //        emptyCell.FixedHeight = 30f;
            //        itemListTable.AddCell(emptyCell);

            //    }

            //    //Footer
            //    var footer = PDFFooter();
            //    foreach (var footerCell in footer)
            //    {
            //        itemListTable.AddCell(footerCell);
            //    }

            //    pdfDoc.Add(itemListTable);


            //    //Save File to server
            //    var FileDic = "documents";
            //    string rootPath = this.Environment.WebRootPath; //wwwroot folder
            //    string FilePath = Path.Combine(rootPath + "/" + FileDic);


            //    //Create a Folder.
            //    if (!Directory.Exists(FilePath))
            //        Directory.CreateDirectory(FilePath);
            //    var filename = "Material_Issue_Form_" + DateTime.Now.ToString() + ".pdf";
            //    filename = filename.Replace(" ", "_");
            //    filename = filename.Replace(":", "_");
            //    filename = filename.Replace(@"/", "_");
            //    var filePath = Path.Combine(FilePath, filename);

            //    pdfDoc.Close();
            //    byte[] bytes = stream.ToArray();
            //    System.IO.File.WriteAllBytes(filePath, bytes);

            //    stream.Close();

            //    foreach (var bom in itemList)
            //    {
            //        var balanceToIssueQty = bom.BalanceToIssueQuantity;
            //        var issuedOutQty = bom.IssueQuantity;
            //        var receivedQty = bom.ReceivedQuantity;
            //        var model = new BomIssueOutStatus();

            //        var findRecord = _context.BomIssueOutStatus.Where(x => x.BomId == bom.ItemId).FirstOrDefault() ?? null;
            //        if (findRecord != null)
            //        {
            //            var currentIssuedQty = issuedOutQty - findRecord.IssuedOutQty;
            //            if (currentIssuedQty == 0)
            //            {
            //                currentIssuedQty = issuedOutQty; //means nothing issue out from last generate. So take the last updated qty.
            //            }
            //            if (issuedOutQty >= receivedQty)
            //            {
            //                //Fully Issue Out
            //                findRecord.IssuedOutQty = issuedOutQty;
            //                findRecord.TotalQty = receivedQty;
            //                findRecord.Status = "Generated";
            //                findRecord.LastUpdatedDate = DateTime.Now;
            //                findRecord.LastIssuedOutQty = currentIssuedQty;
            //            }
            //            else
            //            {
            //                //Partial Issue Out
            //                findRecord.IssuedOutQty = issuedOutQty;
            //                findRecord.TotalQty = receivedQty;
            //                findRecord.Status = "Partial";
            //                findRecord.LastUpdatedDate = DateTime.Now;
            //                findRecord.LastIssuedOutQty = currentIssuedQty;
            //            }

            //            _context.BomIssueOutStatus.Update(findRecord);
            //        }
            //        else
            //        {
            //            if (issuedOutQty == receivedQty)
            //            {
            //                //Fully Issue Out
            //                model.BomId = bom.ItemId;
            //                model.IssuedOutQty = issuedOutQty;
            //                model.TotalQty = receivedQty;
            //                model.Status = "Generated";
            //                model.CreatedDate = DateTime.Now;
            //                model.CreatedBy = int.Parse(HttpContext.GetUserId());
            //                model.LastIssuedOutQty = issuedOutQty;
            //            }
            //            else if (issuedOutQty != receivedQty)
            //            {
            //                //Partial Issue Out
            //                model.BomId = bom.ItemId;
            //                model.IssuedOutQty = issuedOutQty;
            //                model.TotalQty = receivedQty;
            //                model.Status = "Partial";
            //                model.CreatedDate = DateTime.Now;
            //                model.CreatedBy = int.Parse(HttpContext.GetUserId());
            //                model.LastIssuedOutQty = issuedOutQty;
            //            }

            //            _context.BomIssueOutStatus.Add(model);
            //        }
            //        _context.SaveChanges();
            //    }

            //    return Json(new { status = true, fileName = filename });
            //    #endregion
            //}
            //catch (Exception ex)
            //{
            //    return null;
            //}
            return Json(new { status = true});
        }

        public IActionResult ShowIssueFormPDF(string filename)
        {
            var (bytes, fileType, fileName) = _fileWriter.DownloadFiles("documents", filename);
            var base64String = Convert.ToBase64String(bytes);
            var showPdfModel = new ShowPDFModel($@"data:application/pdf;base64,{base64String}", fileName);
            //return RedirectToAction("ShowPDF", showPdfModel);
            return View(showPdfModel);
        }

        public void RemovePDFFile(string filename)
        {
            _fileWriter.DeleteDocument(filename);
        }


        public List<PdfPCell> PDFFooter()
        {
            FontFactory.RegisterDirectories();
            Font fontSmaller = new Font(FontFactory.GetFont("Arial", 8, Font.NORMAL));
            Font fontSmallBold = new Font(FontFactory.GetFont("Arial", 9, Font.BOLD));

            var cells = new List<PdfPCell>();

            PdfPCell issueByCell = new PdfPCell(new Phrase("Issued By", fontSmallBold));
            issueByCell.HorizontalAlignment = Element.ALIGN_CENTER;
            issueByCell.Colspan = 3;
            cells.Add(issueByCell);

            PdfPCell checkedByCell = new PdfPCell(new Phrase("Checked By", fontSmallBold));
            checkedByCell.HorizontalAlignment = Element.ALIGN_CENTER;
            checkedByCell.Colspan = 2;
            cells.Add(checkedByCell);

            PdfPCell signatureCell1 = new PdfPCell(new Phrase(" ", fontSmallBold));
            signatureCell1.Colspan = 3;
            signatureCell1.PaddingTop = 45;
            cells.Add(signatureCell1);

            PdfPCell signatureCell2 = new PdfPCell(new Phrase(" ", fontSmallBold));
            signatureCell2.Colspan = 2;
            signatureCell2.PaddingTop = 45;
            cells.Add(signatureCell2);

            PdfPCell nameCell1 = new PdfPCell(new Phrase("Name: ", fontSmallBold));
            nameCell1.Colspan = 3;
            nameCell1.PaddingTop = 8;
            nameCell1.PaddingBottom = 8;
            nameCell1.VerticalAlignment = Element.ALIGN_MIDDLE;
            cells.Add(nameCell1);

            PdfPCell nameCell2 = new PdfPCell(new Phrase("Name: ", fontSmallBold));
            nameCell2.Colspan = 2;
            nameCell2.PaddingTop = 8;
            nameCell2.PaddingBottom = 8;
            nameCell2.VerticalAlignment = Element.ALIGN_MIDDLE;
            cells.Add(nameCell2);

            PdfPCell dateCell1 = new PdfPCell(new Phrase("Date: ", fontSmallBold));
            dateCell1.Colspan = 3;
            dateCell1.PaddingTop = 8;
            dateCell1.PaddingBottom = 8;
            dateCell1.VerticalAlignment = Element.ALIGN_MIDDLE;
            cells.Add(dateCell1);

            PdfPCell dateCell2 = new PdfPCell(new Phrase("Date: ", fontSmallBold));
            dateCell2.Colspan = 2;
            dateCell2.PaddingTop = 8;
            dateCell2.PaddingBottom = 8;
            dateCell2.VerticalAlignment = Element.ALIGN_MIDDLE;
            cells.Add(dateCell2);

            PdfPCell extraCell1 = new PdfPCell(new Phrase(" "));
            extraCell1.Colspan = 3;
            cells.Add(extraCell1);

            PdfPCell extraCell2 = new PdfPCell(new Phrase(" "));
            extraCell2.Colspan = 2;
            cells.Add(extraCell2);


            return cells;
        }


        [HttpPost]
        public IActionResult MarkCompleted(string PrIds = null)
        {
            try
            {
                var BomIds = string.Empty;
                if (!string.IsNullOrEmpty(PrIds))
                {
                    string[] ids = PrIds.Split(",");
                    var prIds = ids.Select(int.Parse).Distinct().ToList();
                    var bomList = _context.Boms.Where(x => prIds.Contains((int)x.PRId)).Select(x => x.Id).ToList();
                    if (bomList.Any())
                    {
                        BomIds = String.Join(",", bomList);
                    }
                }

                if (!string.IsNullOrEmpty(BomIds))
                {
                    string[] ids = BomIds.Split(",");

                    if (ids.Length > 0)
                    {
                        var bomIds = ids.Select(int.Parse).Distinct().ToList();

                        var list = _context.Boms.Where(x => bomIds.Contains(x.Id)).ToList();

                        foreach (var bom in list)
                        {
                            bom.Status = "Completed";
                            bom.LastUpdatedDate = DateTime.Now;
                            bom.ReceivedDate = DateTime.Now;

                            _context.Boms.Update(bom);
                        }
                        _context.SaveChanges();
                        return Json(new { status = true });
                    }
                }
                return Json(new { status = false, msg = "No content can be found" });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = "Error. " + ex.Message });
            }

        }



        [HttpPost]
        public IActionResult CheckCanGenIssueOutForm(string BomIds = null, string PrIds = null)
        {
            //try
            //{
            //    if (!string.IsNullOrEmpty(PrIds))
            //    {
            //        string[] ids = PrIds.Split(",");
            //        var prIds = ids.Select(int.Parse).Distinct().ToList();
            //        var bomList = _context.Boms.Where(x => prIds.Contains((int)x.PRId)).Select(x => x.Id).ToList();
            //        if (bomList.Any())
            //        {
            //            BomIds = String.Join(",", bomList);
            //        }
            //    }

            //    if (!string.IsNullOrEmpty(BomIds))
            //    {
            //        string[] ids = BomIds.Split(",");

            //        if (ids.Length > 0)
            //        {
            //            var bomIds = ids.Select(int.Parse).Distinct().ToList();

            //            //var issueOutStatusBomId = issueOutStatuslist.Where(x => x.Status == "Generated").Select(x => x.BomId).ToList();
            //            //var bomWithFullIssueOut = bomInPR.Where(x => !issueOutStatusBomId.Contains(x.Id)).Any();
            //            //var isAllIssuedOut = bomWithFullIssueOut;


            //            var generatedIssueOutForm = _context.BomIssueOutStatus.Where(x => bomIds.Contains(x.BomId) && x.Status == "Generated").Select(x => x.BomId).ToList();
            //            var hasGenerated = bomIds.Where(x => generatedIssueOutForm.Contains(x)).ToList();
            //            if (hasGenerated.Count > 0)
            //            {
            //                string itemNo = string.Join(",", hasGenerated);
            //                return Json(new { status = false, msg = "Item Number " + itemNo + " has been generated Material Issue form. " });
            //            }

            //            return Json(new { status = true });
            //        }
            //    }
            //    return Json(new { status = false });
            //}
            //catch (Exception ex)
            //{
            //    return Json(new { status = false, msg = "Error. " + ex.Message });
            //}

            return Json(new { status = false });
        }

        [HttpPost]
        public JsonResult GetOverEtaItems(DataTableParameters parameters)
        {
            var list = new List<OverEtaItemModel>();
            var boms = _context.Boms
                                .Where(x => x.Status == "PO Approved" && x.EstDeliveryDate.HasValue && x.EstDeliveryDate.Value.Date < DateTime.Now.Date && x.PRId.HasValue)
                                .AsQueryable();

            var total = boms.Count();

            if(total > 0)
            {
                var data = _context.Boms
                                .Include(x => x.Project)
                                .Include(x => x.PR)
                                .Where(x => x.Status == "PO Approved" && x.EstDeliveryDate.HasValue && x.EstDeliveryDate.Value.Date < DateTime.Now.Date && x.PRId.HasValue)
                                .OrderByDescending(x => x.EstDeliveryDate)
                                .Skip(parameters.Start).Take(parameters.Length)
                                .ToList();

                foreach(var d in data)
                {
                    var model = new OverEtaItemModel();

                    var getPO = _context.BomSupportFile.Where(x => x.BomId == d.Id && x.Type == "PO").Include(x => x.SupportFile).FirstOrDefault();

                    model.Brand = d.Brand;
                    model.Desc = d.PartDesc;
                    model.ETA = (DateTime)d.EstDeliveryDate;
                    model.ItemNo = d.Id;
                    model.PoId = getPO == null ? null : getPO.SupportFile.Id;
                    model.POName = getPO == null ? null : getPO.SupportFile.FileName;
                    model.PRNo = CommonHelpers.renamePR(d.PR.Prname, d.PR.RevisionNo);
                    model.PRId = d.PRId.Value;
                    model.ProjectCode = d.Project.Code;
                    model.ProjectName = d.Project.Name;
                    model.Status = d.Status;
                    model.DaysOverEta = (DateTime.Now.Date - (DateTime)d.EstDeliveryDate.Value.Date).TotalDays;
                    list.Add(model);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(model));

                }
            }

            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = list
            });
        }

      
        [HttpGet]
        [ClaimRequirement("Type", "HOD,CDO,GFC,CEO,Receiving")]
        public IActionResult DownloadOverdueETATemplate(int length = 10, int start = 0)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            try
            {
                
                var query = _context.Boms
                    .Where(x => x.Status == "PO Approved" && x.EstDeliveryDate.HasValue &&
                               x.EstDeliveryDate.Value.Date < DateTime.Now.Date && x.PRId.HasValue)
                    .Include(x => x.Project)
                    .Include(x => x.PR)
                    .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                    .AsQueryable();




                var overdueItems = query
                    .Skip(start)          
                    .Take(length)        
                    .ToList();

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("OverdueETAItems");

                    worksheet.Cells[1, 1].Value = "ITEM NO";
                    worksheet.Cells[1, 2].Value = "PR NO";
                    worksheet.Cells[1, 3].Value = "DESCRIPTION";
                    worksheet.Cells[1, 4].Value = "BRAND";
                    worksheet.Cells[1, 5].Value = "PROJECT CODE";
                    worksheet.Cells[1, 6].Value = "PROJECT NAME";
                    worksheet.Cells[1, 7].Value = "ETA";
                    worksheet.Cells[1, 8].Value = "DAYS OVER ETA";
                    worksheet.Cells[1, 9].Value = "PO FILE NAME";
                    worksheet.Cells[1, 10].Value = "MARK RECEIVED (Y/N)";
                    worksheet.Cells[1, 11].Value = "REMARKS";

                    
                    worksheet.Cells[1, 1, 1, 11].Style.Font.Bold = true;
                    worksheet.Cells[1, 1, 1, 11].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1, 1, 11].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);


                    worksheet.Cells[3, 1].Value = "INSTRUCTIONS:";
                    worksheet.Cells[3, 1].Style.Font.Bold = true;
                    worksheet.Cells[4, 1].Value = "1. Fill 'Y' in column J to mark items as 'Item Received'";
                    worksheet.Cells[5, 1].Value = "2. Leave empty or fill 'N' to skip items";
                    worksheet.Cells[6, 1].Value = "3. Do not modify columns A-I (data columns)";
                    worksheet.Cells[7, 1].Value = "4. You can add remarks in column K (optional)";
                    worksheet.Cells[8, 1].Value = "5. Save the file before uploading";
                    worksheet.Cells[9, 1].Value = "6. After successful upload, re-download template for future updates";
                    worksheet.Cells[9, 1].Style.Font.Color.SetColor(System.Drawing.Color.Red);


                    int row = 10;
                    foreach (var item in overdueItems)
                    {
                        var poFile = item.BomSupportFiles?.FirstOrDefault(x => x.Type == "PO");
                        var prName = CommonHelpers.renamePR(item.PR?.Prname, item.PR?.RevisionNo ?? 0);
                        var daysOverEta = item.EstDeliveryDate.HasValue ?
                            (DateTime.Now.Date - item.EstDeliveryDate.Value.Date).TotalDays : 0;

                        worksheet.Cells[row, 1].Value = item.Id;
                        worksheet.Cells[row, 2].Value = prName;
                        worksheet.Cells[row, 3].Value = item.PartDesc;
                        worksheet.Cells[row, 4].Value = item.Brand;
                        worksheet.Cells[row, 5].Value = item.Project?.Code;
                        worksheet.Cells[row, 6].Value = item.Project?.Name;
                        worksheet.Cells[row, 7].Value = item.EstDeliveryDate?.ToString("dd-MMM-yyyy");
                        worksheet.Cells[row, 8].Value = daysOverEta;
                        worksheet.Cells[row, 9].Value = poFile?.SupportFile?.FileName ?? "";
                        worksheet.Cells[row, 10].Value = ""; 
                        worksheet.Cells[row, 11].Value = ""; 

                        row++;
                    }

                  
                    worksheet.Column(1).Width = 15;  
                    worksheet.Column(3).Width = 40;  
                    worksheet.Column(4).Width = 20;  
                    worksheet.Column(5).Width = 20;  
                    worksheet.Column(6).Width = 30;  
                    worksheet.Column(7).Width = 15;  
                    worksheet.Column(8).Width = 15;  
                    worksheet.Column(9).Width = 30;  
                    worksheet.Column(10).Width = 20; 
                    worksheet.Column(11).Width = 30;

                    return File(package.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"OverdueETA_Template_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                }
            }
            catch (Exception ex)
            {
               
                System.Diagnostics.Debug.WriteLine($"Template generation error: {ex.Message}");
                return Content($"Error generating template: {ex.Message}");
            }
        }

        [HttpPost]
        [ClaimRequirement("Type", "HOD,CDO,GFC,CEO,Receiving")]
        public async Task<JsonResult> BulkUpdateReceivedFromExcel(IFormFile excelFile)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            try
            {
                var updatedItems = new List<int>();
                var noChangeItems = new List<int>();
                var errorItems = new Dictionary<int, string>();

                if (excelFile == null || excelFile.Length == 0)
                {
                    return Json(new { success = false, message = "Please select an Excel file to upload." });
                }

                var extension = Path.GetExtension(excelFile.FileName).ToLower();
                if (extension != ".xlsx" && extension != ".xls")
                {
                    return Json(new { success = false, message = "Please upload a valid Excel file (.xlsx or .xls)." });
                }

                using (var stream = new MemoryStream())
                {
                    await excelFile.CopyToAsync(stream);

                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null || worksheet.Dimension == null)
                        {
                            return Json(new { success = false, message = "The Excel file is empty or invalid." });
                        }

                        int rowCount = worksheet.Dimension.Rows;
                        var allTamperedItems = new List<string>();

                        
                        var allValidItemIds = await _context.Boms
                            .Where(x => x.Status == "PO Approved" && x.EstDeliveryDate.HasValue &&
                                       x.EstDeliveryDate.Value.Date < DateTime.Now.Date && x.PRId.HasValue)
                            .Select(x => x.Id)
                            .ToListAsync();

                        
                        var allOriginalData = await _context.Boms
                            .Where(x => x.Status == "PO Approved" && x.EstDeliveryDate.HasValue &&
                                       x.EstDeliveryDate.Value.Date < DateTime.Now.Date && x.PRId.HasValue)
                            .Include(x => x.PR)
                            .Include(x => x.Project)
                            .Include(x => x.BomSupportFiles)
                                .ThenInclude(x => x.SupportFile)
                            .Select(x => new
                            {
                                x.Id,
                                x.PartDesc,
                                x.Brand,
                                ProjectCode = x.Project.Code,
                                ProjectName = x.Project.Name,
                                PRName = x.PR.Prname,
                                RevisionNo = x.PR.RevisionNo,
                                x.EstDeliveryDate,
                                POFilename = x.BomSupportFiles
                                    .Where(f => f.Type == "PO")
                                    .Select(f => f.SupportFile.FileName)
                                    .FirstOrDefault()
                            })
                            .ToDictionaryAsync(x => x.Id);

                        
                        bool hasAnyTampering = false;

                        for (int row = 10; row <= rowCount; row++)
                        {
                            var itemNoText = worksheet.Cells[row, 1].Text?.Trim();

                            
                            if (string.IsNullOrEmpty(itemNoText))
                            {
                                
                                bool dataExistsBelow = false;
                                for (int nextRow = row + 1; nextRow <= Math.Min(row + 3, rowCount); nextRow++)
                                {
                                    if (!string.IsNullOrEmpty(worksheet.Cells[nextRow, 1].Text?.Trim()))
                                    {
                                        dataExistsBelow = true;
                                        break;
                                    }
                                }

                                
                                if (dataExistsBelow)
                                {
                                    hasAnyTampering = true;
                                    allTamperedItems.Add($"Row {row}: ITEM NO deleted or modified");
                                }
                                continue; 
                            }

                            if (!int.TryParse(itemNoText, out int itemId))
                            {
                                hasAnyTampering = true;
                                allTamperedItems.Add($"Row {row}: Invalid ITEM NO format - '{itemNoText}'");
                                continue;
                            }

                            if (!allValidItemIds.Contains(itemId))
                            {
                                hasAnyTampering = true;
                                allTamperedItems.Add($"Row {row}: Item {itemId} does not exist in system");
                                continue;
                            }

                            if (allOriginalData.TryGetValue(itemId, out var originalItem))
                            {
                                var prNoText = worksheet.Cells[row, 2].Text?.Trim();
                                var descriptionText = worksheet.Cells[row, 3].Text?.Trim();
                                var brandText = worksheet.Cells[row, 4].Text?.Trim();
                                var projectCodeText = worksheet.Cells[row, 5].Text?.Trim();
                                var projectNameText = worksheet.Cells[row, 6].Text?.Trim();
                                var etaText = worksheet.Cells[row, 7].Text?.Trim();
                                var daysOverEtaText = worksheet.Cells[row, 8].Text?.Trim();
                                var poFileNameText = worksheet.Cells[row, 9].Text?.Trim();

                                List<string> temperedColumns = new List<string>();

                                
                                var originalPRName = CommonHelpers.renamePR(originalItem.PRName, originalItem.RevisionNo);
                                if (prNoText != originalPRName)
                                    temperedColumns.Add("PR NO");

                                                                if (!string.Equals(
                                        (descriptionText ?? "").Replace("\r\n", "\n").Trim(),
                                        (originalItem.PartDesc ?? "").Replace("\r\n", "\n").Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    temperedColumns.Add("DESCRIPTION");
                                }

                                
                                if (!string.Equals(
                                        (brandText ?? "").Trim(),
                                        (originalItem.Brand ?? "").Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    temperedColumns.Add("BRAND");
                                }

                                
                                if (!string.Equals(
                                        (projectCodeText ?? "").Trim(),
                                        (originalItem.ProjectCode ?? "").Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    temperedColumns.Add("PROJECT CODE");
                                }

                                
                                if (!string.Equals(
                                        (projectNameText ?? "").Trim(),
                                        (originalItem.ProjectName ?? "").Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    temperedColumns.Add("PROJECT NAME");
                                }

                                
                                DateTime? excelEta = null;
                                if (DateTime.TryParse(etaText, out DateTime parsedEta))
                                    excelEta = parsedEta.Date;

                                DateTime? originalEta = originalItem.EstDeliveryDate?.Date;

                                if ((excelEta == null && originalEta != null) ||
                                    (excelEta != null && originalEta == null) ||
                                    (excelEta != null && originalEta != null && excelEta.Value != originalEta.Value))
                                {
                                    temperedColumns.Add("ETA");
                                }

                                
                                if (int.TryParse(daysOverEtaText, out int excelDays))
                                {
                                    var originalDays = originalItem.EstDeliveryDate.HasValue ?
                                        (int)(DateTime.Now.Date - originalItem.EstDeliveryDate.Value.Date).TotalDays : 0;

                                    if (Math.Abs(excelDays - originalDays) > 1)
                                    {
                                        temperedColumns.Add("DAYS OVER ETA");
                                    }
                                }
                                else if (!string.IsNullOrEmpty(daysOverEtaText))
                                {
                                    temperedColumns.Add("DAYS OVER ETA (Invalid format)");
                                }

                                
                                if (!string.Equals(
                                        (poFileNameText ?? "").Trim(),
                                        (originalItem.POFilename ?? "").Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    temperedColumns.Add("PO FILE NAME");
                                }

                                if (temperedColumns.Any())
                                {
                                    hasAnyTampering = true;
                                    allTamperedItems.Add($"Item {itemId} (Row {row}): {string.Join(", ", temperedColumns)}");
                                }
                            }
                            else
                            {
                                hasAnyTampering = true;
                                allTamperedItems.Add($"Row {row}: Item {itemId} not found in original data");
                            }
                        }

                        
                        if (hasAnyTampering)
                        {
                            await transaction.RollbackAsync();

                            var errorMessage = new System.Text.StringBuilder();
                            errorMessage.AppendLine("❌ UPLOAD FAILED!");
                            errorMessage.AppendLine("Cannot modify protected data columns (A-I).");
                            errorMessage.AppendLine("Please only change the MARK RECEIVED (Y/N) and REMARKS columns.");
                            errorMessage.AppendLine();
                            errorMessage.AppendLine("Modified protected columns found:");

                            foreach (var item in allTamperedItems)
                                errorMessage.AppendLine($"• {item}");

                            errorMessage.AppendLine();
                            errorMessage.AppendLine("⚠️ You can ONLY modify:");
                            errorMessage.AppendLine("   - Column 10: MARK RECEIVED (Y/N)");
                            errorMessage.AppendLine("   - Column 11: REMARKS");

                            return Json(new
                            {
                                success = false,
                                message = errorMessage.ToString(),
                                updatedCount = 0,
                                skippedCount = 0,
                                errorCount = allTamperedItems.Count
                            });
                        }

                        
                        for (int row = 10; row <= rowCount; row++)
                        {
                            try
                            {
                                var itemNoText = worksheet.Cells[row, 1].Text?.Trim();
                                var markReceived = worksheet.Cells[row, 10].Text?.Trim();

                                if (string.IsNullOrEmpty(itemNoText))
                                    continue;

                                if (!int.TryParse(itemNoText, out int itemId))
                                    continue;

                                if (markReceived?.Equals("Y", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    var bom = await _context.Boms
                                        .Include(x => x.PR)
                                        .Include(x => x.Project)
                                        .FirstOrDefaultAsync(x => x.Id == itemId);

                                    if (bom != null && bom.Status?.Trim() == "PO Approved")
                                    {
                                        bom.Status = "Item Received";
                                        bom.ReceivedDate = DateTime.Now;
                                        bom.LastUpdatedDate = DateTime.Now;

                                        var prResponse = bom.PR?.DataResponse;
                                        if (!string.IsNullOrEmpty(prResponse))
                                        {
                                            try
                                            {
                                                var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                                                if (prRoot?.data?.bomGrid != null)
                                                {
                                                    var bomItem = prRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == bom.Id);
                                                    if (bomItem != null)
                                                        bom.ReceivedQuantity = bomItem?.totalQuantity ?? 0;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Error parsing PR data: {ex.Message}");
                                            }
                                        }

                                        _context.Boms.Update(bom);
                                        updatedItems.Add(itemId);
                                    }
                                }
                                else if (markReceived?.Equals("N", StringComparison.OrdinalIgnoreCase) == true ||
                                         string.IsNullOrWhiteSpace(markReceived))
                                {
                                    noChangeItems.Add(itemId);
                                }
                                else
                                {
                                    errorItems.Add(itemId, $"Invalid value '{markReceived}'. Use 'Y' or 'N' only.");
                                }
                            }
                            catch (Exception ex)
                            {
                                errorItems.Add(row, $"Error processing - {ex.Message}");
                            }
                        }

                        if (updatedItems.Any())
                        {
                            await _context.SaveChangesAsync();
                            await transaction.CommitAsync();
                        }
                        else
                        {
                            await transaction.RollbackAsync();
                        }

                        var message = "";
                        if (updatedItems.Any())
                            message += $"✅ Successfully updated {updatedItems.Count} item(s) to 'Item Received': {string.Join(", ", updatedItems)}.\n";
                        if (noChangeItems.Any())
                            message += $"ℹ️ No changes made for {noChangeItems.Count} item(s): {string.Join(", ", noChangeItems)}.\n";
                        if (errorItems.Any())
                            message += $"❌ Invalid entries in 'Mark Received' column ({errorItems.Count} item(s)): {string.Join(", ", errorItems.Keys)}.\n";

                        return Json(new
                        {
                            success = true,
                            message = message,
                            updatedCount = updatedItems.Count,
                            skippedCount = noChangeItems.Count + errorItems.Count,
                            errorCount = errorItems.Count
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction?.RollbackAsync();
                }
                catch { }

                var fullError = $"Error: {ex.Message}";
                if (ex.InnerException != null)
                    fullError += $"\nInner: {ex.InnerException.Message}";
                if (ex.InnerException?.InnerException != null)
                    fullError += $"\nInner2: {ex.InnerException.InnerException.Message}";
                if (ex is Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
                    fullError += $"\nDB Error: {dbEx.InnerException?.Message}";

                System.Diagnostics.Debug.WriteLine($"FULL ERROR: {fullError}");

                return Json(new
                {
                    success = false,
                    message = fullError
                });
            }
        }

        [HttpGet]
        public IActionResult DownloadPOUpdateTemplate()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            try
            {
                using (var package = new OfficeOpenXml.ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("PO Update Template");
                    
                    // headers
                    worksheet.Cells[1, 1].Value = "Item Line No";
                    worksheet.Cells[1, 2].Value = "Received";
                    worksheet.Cells[1, 3].Value = "Date";

                    var dateComment = worksheet.Cells[1, 3].AddComment("Please enter date in dd-MM-yyyy format (e.g., 31-12-2024)", "System");
                    dateComment.AutoFit = true;

                    // Get or create header row
                    var headerRow = worksheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    headerRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    headerRow.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    headerRow.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    headerRow.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    headerRow.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    headerRow.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    headerRow.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                    
                    worksheet.Column(1).Width = 25;
                    worksheet.Column(2).Width = 20;
                    worksheet.Column(3).Width = 20;

                    for (int i = 0; i < 100; i++)
                    {
                        int rowNum = i + 2;
                        worksheet.Row(rowNum).Height = 15;

                        System.Drawing.Color rowColor = (i % 2 == 0) ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(220, 230, 241);

                        for (int col = 1; col <= 3; col++)
                        {
                            var cell = worksheet.Cells[rowNum, col];
                            cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(rowColor);
                            
                            cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                            cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                            cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                            cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                            
                            cell.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;

                            if (col == 1 || col == 2)
                            {
                                cell.Style.Numberformat.Format = "0";
                            }
                            else if (col == 3)
                            {
                                cell.Style.Numberformat.Format = "@";
                            }
                        }
                    }

                    return File(package.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"POUpdate_Template_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                }
            }
            catch (Exception ex)
            {
                return Content($"Error generating template: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<JsonResult> ImportPOUpdate(IFormFile FileUpload)
        {
            if (FileUpload == null || FileUpload.Length == 0)
            {
                return Json(new { success = false, msg = "Please select an Excel file to upload." });
            }

            try
            {
                int updatedCount = 0;
                var errorItems = new Dictionary<int, List<string>>();

                using (var stream = new MemoryStream())
                {
                    await FileUpload.CopyToAsync(stream);
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null) return Json(new { success = false, msg = "Excel file is empty or invalid." });

                        int rowCount = worksheet.Dimension?.Rows ?? 0;
                        if (rowCount <= 1) return Json(new { success = false, msg = "No data found in the Excel file." });

                        // Begin DB transaction
                        using (var transaction = await _context.Database.BeginTransactionAsync())
                        {
                            for (int row = 2; row <= rowCount; row++) // Skip header
                            {
                                try
                                {
                                    var itemLineNoStr = worksheet.Cells[row, 1].Text?.Trim();
                                    var receivedStr = worksheet.Cells[row, 2].Text?.Trim();
                                    var dateStr = worksheet.Cells[row, 3].Text?.Trim();

                                    if (string.IsNullOrEmpty(itemLineNoStr)) continue; // skip empty rows

                                    var rowErrors = new List<string>();

                                    if (!int.TryParse(itemLineNoStr, out int itemId))
                                    {
                                        rowErrors.Add($"Invalid Item Line No: {itemLineNoStr}");
                                    }

                                    if (!int.TryParse(receivedStr, out int receivedQty) || receivedQty <= 0)
                                    {
                                        rowErrors.Add($"Invalid Received Quantity: '{receivedStr}' (must be > 0)");
                                    }

                                    DateTime receivedDate = DateTime.Now;
                                    if (string.IsNullOrEmpty(dateStr))
                                    {
                                        rowErrors.Add("Date cannot be empty");
                                    }
                                    else if (!DateTime.TryParseExact(dateStr, new[] { "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "dd-MM-yy", "d-M-yy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out receivedDate))
                                    {
                                        if (double.TryParse(dateStr, out double dateSerial))
                                        {
                                            receivedDate = DateTime.FromOADate(dateSerial);
                                        }
                                        else
                                        {
                                            rowErrors.Add($"Invalid Date format (only dd-MM-yyyy is allowed)");
                                        }
                                    }

                                    if (rowErrors.Any())
                                    {
                                        errorItems.Add(row, rowErrors);
                                        continue;
                                    }

                                    var bom = await _context.Boms
                                        .Include(b => b.PR)
                                        .FirstOrDefaultAsync(b => b.Id == itemId);

                                    if (bom == null)
                                    {
                                        errorItems.Add(row, new List<string> { $"Item Line No {itemId} not found in database" });
                                        continue;
                                    }

                                    int totalBomQty = 0;
                                    var prResponse = bom.PR?.DataResponse;
                                    if (!string.IsNullOrEmpty(prResponse))
                                    {
                                        try
                                        {
                                            var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                                            if (prRoot?.data?.bomGrid != null)
                                            {
                                                var bomItem = prRoot.data.bomGrid.FirstOrDefault(x => x.itemLineNo == bom.Id);
                                                if (bomItem != null)
                                                {
                                                    totalBomQty = bomItem.totalQuantity;
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"Error parsing PR data for item {bom.Id}: {ex.Message}");
                                        }
                                    }

                                    // Apply Logic
                                    if (totalBomQty == 0 && receivedQty > 0)
                                    {
                                        bom.Status = "Item Received";
                                    }
                                    else if (receivedQty >= totalBomQty)
                                    {
                                        bom.Status = "Item Received";
                                    }
                                    else if (receivedQty > 0 && receivedQty < totalBomQty)
                                    {
                                        bom.Status = "Partial Received";
                                    }
                                    
                                    bom.ReceivedQuantity = receivedQty;
                                    bom.ReceivedDate = receivedDate;
                                    bom.LastUpdatedDate = DateTime.Now;

                                    _context.Boms.Update(bom);
                                    updatedCount++;
                                }
                                catch (Exception ex)
                                {
                                    errorItems.Add(row, new List<string> { $"Error processing row - {ex.Message}" });
                                }
                            }

                            if (updatedCount > 0)
                            {
                                await _context.SaveChangesAsync();
                                await transaction.CommitAsync();
                            }
                        }
                    }
                }

                if (errorItems.Any())
                {
                    string errorMsg = updatedCount > 0 
                        ? $"Updated {updatedCount} items successfully. However, encountered errors on {errorItems.Count} rows:\n"
                        : $"Encountered errors on {errorItems.Count} rows:\n";

                    foreach (var err in errorItems.Take(5)) // Show first 5 errors to avoid massive message
                    {
                        errorMsg += $"Error in Row {err.Key} - {string.Join(", ", err.Value)}\n";
                    }
                    if (errorItems.Count > 5) errorMsg += "...and more.";

                    return Json(new { success = true, hasErrors = true, msg = errorMsg }); // Allow partial success
                }

                return Json(new { success = true, hasErrors = false, msg = $"Successfully updated {updatedCount} items." });
            }
            catch(Exception ex)
            {
                return Json(new { success = false, hasErrors = true, msg = "An error occurred during import: " + ex.Message });
            }
        }
    }
}
