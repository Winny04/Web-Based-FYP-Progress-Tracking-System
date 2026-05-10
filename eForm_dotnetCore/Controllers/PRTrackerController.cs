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

namespace eForm_dotnetCore.Controllers
{
    public class PRTrackerController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IAuditTrailService _auditTrailService;

        public PRTrackerController(EformSophicDemoContext context, IWebHostEnvironment _environment, IAuditTrailService auditTrailService)
        {
            _context = context;
            Environment = _environment;
            _auditTrailService = auditTrailService;
        }

        // GET: BOMs
        public async Task<IActionResult> Index()
        {
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.PRTracker);
            link.Add("/PRTracker/Index/");
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;


            var list = new List<PRTrackerViewModel>();
            try
            {
                var boms = _context.Boms.Include(x => x.Project)
                    .Include(x => x.PR)
                    .Include(x => x.Project)
                    .Where(x => x.PRId.HasValue && (x.Status == "PR Sent" || x.Status == "PR Reject" || x.Status == "PR Approved")).ToList();
                var users = _context.EmployeeInfos.ToList();
                var prIds = new List<int>();
                if (boms.Any())
                {
                    foreach (var bom in boms)
                    {
                        var bomId = bom.Id;

                        if (!prIds.Any(x => x == bom.PRId))
                        {
                            if (bom.PR.DataResponse != null)
                            {
                                var viewModel = new PRTrackerViewModel();
                                prIds.Add(bom.PRId.Value);
                                PRRoot root = JsonConvert.DeserializeObject<PRRoot>(bom.PR.DataResponse);
                                string oldRequestor = root.data.header.Select(x => x.requester).FirstOrDefault();
                                string newRequestor = Common.Departments.ConvertRequestorWithDept(oldRequestor) ?? "";
                                var dept = oldRequestor.Split('(', ')')[1];
                                if (dept == "FSE" || dept == "FIN")
                                {
                                    var commonDept = new Common.Departments();
                                    commonDept.UpdatePRRequestorDept(bom.PRId, newRequestor); //Update requestor in PR
                                }

                                viewModel.Id = bom.PR.Id;
                                viewModel.Status = bom.Status;
                                viewModel.PrName = CommonHelpers.renamePR(bom.PR.Prname, bom.PR.RevisionNo);
                                viewModel.Requestor = newRequestor;
                                viewModel.ProjectName = root.data.header.Select(x => x.projectName).FirstOrDefault();
                                viewModel.ProjectCategory = root.data.header.Select(x => x.projectCategory).FirstOrDefault();
                                viewModel.SupplierName = root.data.supplierContainer.FirstOrDefault()?.supplierKey.name;
                                viewModel.ProjectId = bom.ProjectId;
                                viewModel.BomId = bom.Id;
                                viewModel.BomModule = bom.Module;
                                viewModel.ProjectCode = bom.Project.Code;
                                viewModel.CustomerPO = bom.Project.PoNumber;
                                viewModel.Customer = bom.Project.Customer;
                                viewModel.PRTotalPrice = root.data.bomGrid.Select(x => x.totalPrice).ToList().Sum() ?? 0;
                                viewModel.Remark = bom.PR.Remark;

                                list.Add(viewModel);
                            }
                            
                        }
                       
                    }
                }
                return View(list.OrderBy(x => x.LastUpdatedDate));

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<IActionResult> DeletePR(int? id)
        {
            if (!id.HasValue && !_context.Prs.Any(x => x.Id == id))
                throw new NullReferenceException(nameof(id));

            var targetPR = _context.Prs.Where(x => x.Id == id).FirstOrDefault();

            _context.Prs.Remove(targetPR);
            await _context.SaveChangesAsync();

            var pics = new List<int> { targetPR.RequestorId };

            var auditTrail = await _auditTrailService.InsertLogAsync(
                       Common.LogLevel.Information, $@"{targetPR.Prname} has been removed",
                       pics:pics,
                       fullMessage: "Delete PR with name "+ CommonHelpers.renamePR(targetPR.Prname, targetPR.RevisionNo) + " from PR Tracker",
                       tableChange: nameof(Pr),
                       empId: int.Parse(HttpContext.GetUserId()),
                       ProjectId: targetPR.Project.Id
                       );

            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Submit(Bom form)
        {
            if (form.Id != null)
            {
                var newForm = _context.Boms.FirstOrDefault(x => x.Id == form.Id);
                //newForm.LastUpdatedDate = DateTime.Now;
                newForm.AddedBy = "sys";

                newForm.Status = "Pending RFQ Approved";
                _context.Boms.Update(newForm);

                await _context.SaveChangesAsync();

                return RedirectToAction("Index", new { id = newForm.ProjectId });
            }

            return RedirectToAction("Index", "Project");
        }

        //public void UpdatePRRequestorDept(int? prId, string newRequestor)
        //{
        //    try
        //    {
        //        if (!prId.HasValue || prId == 0)
        //        {
        //            return;
        //        }

        //        var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
        //        if (pr != null)
        //        {
        //            PRRoot root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
        //            //var oldrequestor = root.data.header.Select(x => x.requester).FirstOrDefault();
        //            //oldrequestor = newRequestor;

        //            root.data.header.FirstOrDefault().requester = newRequestor;
                   

        //            pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(root);
        //            _context.Prs.Update(pr);
        //            _context.SaveChanges();
        //        }
        //    }
        //    catch (Exception ex)
        //    {

        //    }
        //}
    }
}
