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

namespace eForm_dotnetCore.Controllers
{
    public class CompletedTrackerController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IAuthorizationService _authorizeService;

        public CompletedTrackerController(EformSophicDemoContext context, 
            IWebHostEnvironment _environment, 
            IFileWriter fileWriter,
            IAuthorizationService authorizeService)
        {
            _context = context;
            Environment = _environment;
            _fileWriter = fileWriter;
            _authorizeService = authorizeService;
        }

        public async Task<IActionResult> Index()
        {
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.CompletedTracker);
            link.Add("/CompletedTracker/Index/");
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;


            var viewModel = new ReceivingTrackerViewModel();
            var list = new List<ReceivingTrackerViewModel>();
            try
            {
                list = LoadBOMList();

                return View(list.OrderBy(x =>
                        x.Status == "New" ? 1 :
                        x.Status == "RFQ Sent" ? 2 :
                        x.Status == "RFQ Updated" ? 3 :
                        x.Status == "RFQ Reject" ? 4 :
                        x.Status == "RFQ Approved" ? 5 :
                        x.Status == "PR Sent" ? 6 :
                        x.Status == "PR Reject" ? 7 :
                        x.Status == "PR Approved" ? 8 :
                        x.Status == "PO Reject" ? 9 :
                        x.Status == "PO Approved" ? 10 :
                        x.Status == "Item Received" ? 11 :
                        x.Status == "Item Issued Out" ? 12 :
                        x.Status == "Completed" ? 13 : 14
                    )
                    .ThenByDescending(x => x.LastUpdatedDate));

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public List<ReceivingTrackerViewModel> LoadBOMList(int? id = null)
        {
            var viewModel = new ReceivingTrackerViewModel();
            var list = new List<ReceivingTrackerViewModel>();

            var boms = _context.Boms
                       .Include(x => x.RFQ)
                       .Include(x => x.PR)
                       .Include(x => x.BomSupportFiles)
                       .Where(x => x.Status == "PO Approved" || x.Status == "Item Received" || x.Status == "Item Issued Out" || x.Status == "Receiving Reject").ToList();
            var prIds = boms.Select(x => x.PRId).ToList();
            var prs = _context.Prs.Where(x => prIds.Contains(x.Id)).ToList();
            var catlist = _context.Category.ToList();
            if (id != null)
            {
                boms = boms.Where(x => x.Id == id).ToList();
            }

            if (boms.Any())
            {
                var Bom_totalPrice = 0;
                foreach (var bom in boms)
                {
                    if (bom.Status == "PO Approved")
                    {
                        if (bom.ReceivedDate.HasValue
                            && bom.ReceivedQuantity.HasValue
                            && bom.BomSupportFiles.Any(x => x.Type == "DO")
                            && bom.BomSupportFiles.Any(x => x.Type == "INV")
                            //&& bom.BomSupportFiles.Any(x => x.Type == "GRN")
                            )
                        {
                            bom.Status = "Item Received";
                            _context.Boms.Update(bom);
                            _context.SaveChanges();
                        }
                        else if (bom.ReceivedDate.HasValue
                           && bom.ReceivedQuantity.HasValue
                           && !bom.BomSupportFiles.Any(x => x.Type == "DO")
                           && !bom.BomSupportFiles.Any(x => x.Type == "INV")
                           && !String.IsNullOrEmpty(bom.ReceivingRemark)
                           )
                        {
                            bom.Status = "Item Received";
                            _context.Boms.Update(bom);
                            _context.SaveChanges();
                        }
                    }

                    if (bom.Status == "Item Received")
                    {
                        if (bom.BalanceToIssueOutQty.HasValue
                            && bom.IssuedOutDate.HasValue
                            && bom.IssuedQuantity.HasValue
                            && !String.IsNullOrEmpty(bom.IssuedPIC)
                            //&& !String.IsNullOrEmpty(bom.remark)
                            )
                        {
                            bom.Status = "Item Issued Out";
                            _context.Boms.Update(bom);
                            _context.SaveChanges();
                        }
                    }

                    if (bom.ReceivedDate.HasValue
                            && bom.ReceivedQuantity.HasValue
                            && bom.BomSupportFiles.Any(x => x.Type == "DO")
                            && bom.BomSupportFiles.Any(x => x.Type == "INV")
                            && bom.BomSupportFiles.Any(x => x.Type == "GRN")
                            && bom.BalanceToIssueOutQty.HasValue
                            && bom.IssuedOutDate.HasValue
                            && bom.IssuedQuantity.HasValue
                            && !String.IsNullOrEmpty(bom.IssuedPIC)
                            )
                    {
                        bom.Status = "Completed";
                        _context.Boms.Update(bom);
                        _context.SaveChanges();
                    }

                    bom.Project = _context.Projects.Where(x => x.Id == bom.ProjectId).FirstOrDefault();
                    int totalPrice = 0;
                    string vendorName = "";
                    string vendorQuotation = "";
                    var requestor = "";
                    var department = "";
                    bool vendorIsSelected = false;
                    int rowIndex = 0;
                    if (bom.RFQ.DataResponse != null)
                    {
                        RFQRoot root = JsonConvert.DeserializeObject<RFQRoot>(bom.RFQ.DataResponse);
                        if (root.data.bomGrid.Count() > 0)
                        {
                            vendorName = root.data.bomGrid.FirstOrDefault().supplier ?? "";
                            //var getSelectVendor = root.data.gridQuotation.Where(x => x.selectBoxes.@true == true).FirstOrDefault();
                            //vendorIsSelected = getSelectVendor != null;
                            //rowIndex = vendorIsSelected ? getSelectVendor.rowIndex.Value : 0;
                            //totalPrice = vendorIsSelected && getSelectVendor.totalPrice.HasValue ? getSelectVendor.totalPrice.Value : 0;

                            requestor = root.data.header.FirstOrDefault().requester;
                            department = root.data.header.FirstOrDefault().department;

                            Bom_totalPrice += totalPrice;
                        }
                    }

                    //read pr
                    var quantity = 0;
                    double unitPrice = 0;
                    double prTotalPrice = 0;
                    //var remark = bom.Prs.Select(x => x.Remark).FirstOrDefault();
                    DateTime? estDeliveryDate = null;
                    if (bom.PR != null && bom.PR.DataResponse != null)
                    {
                        var prResponse = bom.PR.DataResponse;
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(prResponse);
                        if (prRoot.data.bomGrid.Count() > 0)
                        {
                            quantity = prRoot.data.bomGrid.FirstOrDefault().totalQuantity;
                            unitPrice = prRoot.data.bomGrid.FirstOrDefault().totalPrice ?? 0;
                            prTotalPrice = prRoot.data.bomGrid.FirstOrDefault().unitPrice ?? 0;
                            estDeliveryDate = prRoot.data.bomGrid.FirstOrDefault().eta;
                            //remark = prRoot.data.bomGrid.FirstOrDefault().remark;
                        }
                    }
                    var bomId = bom.Id;
                    var users = _context.EmployeeInfos.ToList();
                    viewModel = new ReceivingTrackerViewModel();
                    viewModel.Id = bomId;
                    viewModel.PR = prs;
                    viewModel.PrId = bom.PRId ?? 0;
                    viewModel.CreatedDate = bom.CreatedDate;
                    viewModel.LastUpdatedDate = bom.LastUpdatedDate;
                    viewModel.Requestor = requestor;
                    viewModel.Customer = bom.Project.Customer;
                    viewModel.Department = department;
                    viewModel.CustomerPO = bom.Project.PoNumber;
                    viewModel.ProjectName = bom.Project.Name;
                    viewModel.ProjectCode = bom.Project.Code;
                    var catId = Convert.ToInt16(bom.Project.Category);
                    viewModel.ProjectCategory = catlist.Where(x => x.Id == catId).Select(x => x.CategoryShortName).FirstOrDefault();
                    viewModel.Quantity = bom.Project.PoQuantity ?? 0;
                    viewModel.UnitPrice = unitPrice;
                    viewModel.TotalPrice = prTotalPrice;
                    //viewModel.ETA = bom.ETA;
                    viewModel.EstDeliveryDate = estDeliveryDate;
                    viewModel.ReceivedDate = bom.ReceivedDate;
                    viewModel.ReceivedQuantity = bom.ReceivedQuantity ?? 0;
                    viewModel.BalanceToReceivedQty = bom.BalanceToReceiveQty ?? 0;
                    viewModel.BalanceToIssueOutQty = bom.BalanceToIssueOutQty ?? 0;
                    viewModel.IssuedPIC = string.IsNullOrEmpty(bom.IssuedPIC) ? "" : bom.IssuedPIC;
                    viewModel.IssuedQty = bom.IssuedQuantity ?? 0;
                    viewModel.IssueOutDate = bom.IssuedOutDate;
                    viewModel.VendorName = vendorName;
                    viewModel.VendorQuotation = vendorQuotation;
                    viewModel.Status = bom.Status;
                    viewModel.ProjectId = bom.ProjectId;
                    viewModel.vendorIsSelected = vendorIsSelected;
                    viewModel.rowIndex = rowIndex;
                    viewModel.Description = bom.PartDesc;
                    viewModel.brand = bom.Brand;
                    viewModel.PartNo = bom.PartNumber;
                    viewModel.remark = bom.ReceivingRemark;
                    viewModel.POLineNo = bom.POLineNo;
                    var superAdmins = CommonExtensions.SuperAdminList();
                    viewModel.EmployeeList = _context.EmployeeInfos.Where(x => !superAdmins.Contains(x.UserName) && !string.IsNullOrEmpty(x.ShortName) && !string.IsNullOrEmpty(x.Department)).ToList();

                    var supFile = _context.BomSupportFile.Where(x => x.BomId == bom.Id).ToList();
                    foreach (var file in supFile)
                    {
                        var sFileId = file.SupportFileId;
                        file.SupportFile = _context.SupportFile.Where(x => x.Id == sFileId).FirstOrDefault();
                    }
                    viewModel.PO = supFile;
                    //viewModel.POLine = supFile;
                    viewModel.DO = supFile;
                    viewModel.Invoice = supFile;
                    viewModel.GRN = supFile;
                    viewModel.QUO = supFile;

                    list.Add(viewModel);
                }
            }
            return list.OrderBy(x =>
                        x.Status == "New" ? 1 :
                        x.Status == "RFQ Sent" ? 2 :
                        x.Status == "RFQ Updated" ? 3 :
                        x.Status == "RFQ Reject" ? 4 :
                        x.Status == "RFQ Approved" ? 5 :
                        x.Status == "PR Sent" ? 6 :
                        x.Status == "PR Reject" ? 7 :
                        x.Status == "PR Approved" ? 8 :
                        x.Status == "PO Reject" ? 9 :
                        x.Status == "PO Approved" ? 10 :
                        x.Status == "Item Received" ? 11 :
                        x.Status == "Item Issued Out" ? 12 :
                        x.Status == "Completed" ? 13 : 14
                    )
                    .ThenByDescending(x => x.LastUpdatedDate).ToList();
        }


        public ActionResult downloadFile(int? id)
        {
            if (id.HasValue)
            {
                var file = _context.SupportFile.FirstOrDefault(x => x.Id == id);
                var (bytes, fileType, fileName) = _fileWriter.DownloadFiles(file.FileName);

                return File(bytes, fileType, fileName);
            }

            return NoContent();
        }


    }
}
