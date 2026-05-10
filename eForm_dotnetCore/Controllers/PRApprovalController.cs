using eForm_dotnetCore.Common;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.SS.Formula.Functions;

namespace eForm_dotnetCore.Controllers
{
    public class PRApprovalController : Controller
    {
        private readonly EformSophicDemoContext _context;

        public PRApprovalController(EformSophicDemoContext context)
        {
            _context = context;
        }


        // GET: PRApprovalController
        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Requestor,Finance,Buyer,Quality")]
        public ActionResult Index(int? deptId,string userType = "")
        {
            var ctxPrApprover = _context.PRApprover.ToList();
            var HODApprovalList = ctxPrApprover.Where(x => x.Type == "HOD").ToList();
            var prList = new List<Pr>();
            if (deptId.HasValue)
            {
                HODApprovalList = HODApprovalList
                    .Where(x => x.DepartmentId.HasValue && 
                    x.DepartmentId == deptId && 
                    x.Type == "HOD" && x.Approved == false)
                    .ToList();

                var prIds = HODApprovalList.Select(x => x.PRId).Distinct().ToList();

                prList = _context.Prs.Where(x => prIds.Contains(x.Id) && !string.IsNullOrEmpty(x.Rfqid)).ToList();
            }
            else if (!String.IsNullOrEmpty(userType))
            {
                var GFCApprovalList = ctxPrApprover.Where(x => x.Type == "GFC").ToList();
                var CEOApprovalList = ctxPrApprover.Where(x => x.Type == "CEO").ToList();

                var prIds = new List<int>();

                if(userType == "GFC")
                {
                    prIds = (from x in GFCApprovalList
                             from y in HODApprovalList
                             where x.PRId == y.PRId
                             && x.Approved == false
                             && y.Approved == true
                             select x.PRId).Distinct().ToList();
                }
                else
                {
                    var query = (from x in HODApprovalList
                                 from y in GFCApprovalList
                                 where x.PRId == y.PRId
                                 && x.Approved == true
                                 && y.Approved == true
                                 select y).ToList();

                    prIds = (from x in query
                                        from z in CEOApprovalList
                                        where x.PRId == z.PRId
                                        && z.Approved == false
                                        select z.PRId).Distinct().ToList();
                }

                prList = _context.Prs.Where(x => prIds.Contains(x.Id) && !string.IsNullOrEmpty(x.Rfqid)).ToList();
            }

            var categoryList = _context.Category.Select(x => new { x.Id, x.CategoryShortName }).ToList();
            var list = new List<PRApprovalViewModel>();
            var deptQuery = _context.Department.AsQueryable();
            var bomQuery = _context.Boms.AsQueryable();
            foreach (var pr in prList)
            {
                var modal = new PRApprovalViewModel();
                var bom = bomQuery.Include(x => x.Project).Where(x => x.PRId == pr.Id).FirstOrDefault();
                if(bom != null && bom.Status == "PR Sent")
                {
                    if (!string.IsNullOrEmpty(pr.DataResponse))
                    {
                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                        modal.RequestBy = prRoot.data.header.Select(x => x.requester).FirstOrDefault();
                        modal.VendorName = prRoot.data.supplierContainer.FirstOrDefault()?.supplierKey?.name;
                        modal.TypeOfPurchase = prRoot.data.header.Select(x => x.typeOfPurchase).FirstOrDefault();
                        modal.TotalPrice = prRoot.data.bomGrid.Select(x => x.totalPrice).Sum() ?? 0;
                    }

                    modal.AddedBy = bom.AddedBy;
                    modal.Bom = bom;
                    modal.Bomid = bom.Id;
                    modal.CreatedDate = bom.CreatedDate;
                    modal.Data = bom.PR.Data;
                    modal.DataResponse = bom.PR.DataResponse;
                    modal.FormName = bom.PR.FormName;
                    modal.Id = bom.PRId ?? 0;
                    modal.isMerge = bom.PR.isMerge;
                    modal.LastUpdatedDate = bom.LastUpdatedDate;
                    modal.Pos = bom.PR.Pos;
                    modal.PRApprovers = bom.PR.PRApprovers;
                    modal.Prname = CommonHelpers.renamePR(bom.PR.Prname, bom.PR.RevisionNo);
                    modal.Project = bom.Project;
                    modal.ProjectId = bom.ProjectId;
                    modal.Rfq = bom.RFQ;
                    modal.Rfqid = bom.RFQId;
                    modal.Status = bom.Status;
                    modal.totalApprover = bom.PR.totalApprover;
                    modal.userType = userType;
                    modal.Department = deptQuery.FirstOrDefault(x => x.Id == pr.DepartmentId)?.Name ?? "";
                    var categoryId = bom.Project.Category != null ? Convert.ToInt16(bom.Project.Category) : 0;
                    modal.Category = categoryList.Where(x => x.Id == categoryId).Select(x => x.CategoryShortName).FirstOrDefault();

                    list.Add(modal);
                }
               
            }
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
    }
}
