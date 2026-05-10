using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Office2010.Excel;
using eForm_dotnetCore.Common;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OfficeOpenXml.FormulaParsing.ExpressionGraph;
using Org.BouncyCastle.Asn1.Ocsp;
using PuppeteerSharp;

namespace eForm_dotnetCore.Controllers
{
    public class POCController : Controller
    {
        //private readonly EformSophicDemoContext _context;
        //private readonly IEmailService _emailService;
        //private readonly IAuthorizationService authorizationService;
        //private readonly IAuditTrailService _auditTrailService;
        //private IWebHostEnvironment _env;

        //public POCController(EformSophicDemoContext context,
        //    IEmailService emailService,
        //    IAuthorizationService authorizationService,
        //    IAuditTrailService auditTrailService,
        //    IWebHostEnvironment environment
        //    )
        //{
        //    _context = context;
        //    _emailService = emailService;
        //    this.authorizationService = authorizationService;
        //    _auditTrailService = auditTrailService;
        //    _env = environment;
        //}

        //public async Task<IActionResult> Index()
        //{
        //    var pocList = await _context.POC
        //        .Include(x => x.Pr)
        //        .ThenInclude(y => y.Project)
        //        .Where(x => x.Type == FormTypeInString.PO).ToListAsync();

        //    var categoryList = _context.Category.Select(x => new { x.Id, x.CategoryShortName }).ToList();
        //    var list = new List<PRApprovalViewModel>();
        //    foreach(var poc in pocList)
        //    {
        //        var modal = new PRApprovalViewModel();
        //        if (!string.IsNullOrEmpty(poc.DataResponse))
        //        {
        //            var prRoot = JsonConvert.DeserializeObject<PRRoot>(poc.DataResponse);
        //            modal.RequestBy = prRoot.data.header.Select(x => x.requester).FirstOrDefault();
        //            modal.TotalPrice = prRoot.data.bomGrid.Select(x => x.totalPrice).Sum() ?? 0;
        //            modal.Department = prRoot.data.header.Select(x => x.department).FirstOrDefault();
        //        }
        //        modal.POCId = poc.Id;
        //        modal.Id = poc.PRId;
        //        modal.Prname = CommonHelpers.renamePR(poc.Pr.Prname, poc.Pr.RevisionNo);
        //        modal.Project = poc.Pr.Project;
        //        modal.ProjectId = poc.Pr.Project.Id;

        //        var categoryId = poc.Pr.Project.Category != null ? Convert.ToInt16(poc.Pr.Project.Category) : 0;
        //        modal.Category = categoryList.Where(x => x.Id == categoryId).Select(x => x.CategoryShortName).FirstOrDefault();

        //        list.Add(modal);
        //    }

        //    return View(list);
        //}

        //public async Task<ActionResult> Transfer(Guid id)
        //{
        //    await TransferPO(id);
        //    return RedirectToAction("Index");
        //}

        //public async Task<ActionResult> Transfers(List<Guid> ids)
        //{
        //    foreach(var id in ids)
        //    {
        //        await TransferPO(id);
        //    }

        //    return Ok();
        //}

        //protected async Task TransferPO(Guid id)
        //{
        //    var poc = _context.POC.FirstOrDefault(x => x.Id == id);

        //    if (poc == null) throw new Exception("POC not found!");

        //    var pr = _context.Prs.FirstOrDefault(x => x.Id == poc.PRId);

        //    var prRoot = JsonConvert.DeserializeObject<PRRoot>(poc.DataResponse);

        //    Random _random = new Random();
        //    var code = _random.Next(0, 9999).ToString("D4");

        //    var boms = await _context.Boms
        //        .Include(x => x.BomSupportFiles)
        //        .Where(x => prRoot.data.bomGrid.Select(x => x.itemLineNo).Contains(x.Id))
        //        .ToListAsync();

        //    var addedBoms = new List<Bom>();
        //    foreach (var bom in boms)
        //    {
        //        var newBom = new Bom
        //        {
        //            Status = "PO Approved",
        //            ProjectId = bom.ProjectId,
        //            AddedBy = bom.AddedBy,
        //            BomNo = "poc_" + code + bom.BomNo,
        //            Brand = bom.Brand,
        //            Module = bom.Module,
        //            CreatedDate = DateTime.Now,
        //            LastUpdatedDate = DateTime.Now,
        //            ItemCategory = bom.ItemCategory,
        //            PartDesc = bom.PartDesc,
        //            UnitQty = bom.UnitQty,
        //            RequireQty = bom.RequireQty,
        //            RFQId = bom.RFQId,
        //        };

        //        if (bom.BomSupportFiles.Any(x => x.Type == "PO"))
        //        {
        //            newBom.BomSupportFiles.Add(new BomSupportFile
        //            {
        //                SupportFileId = bom.BomSupportFiles.FirstOrDefault(x => x.Type == "PO").SupportFileId,
        //                Type = bom.BomSupportFiles.FirstOrDefault(x => x.Type == "PO").Type,
        //            });
        //        }

        //        addedBoms.Add(newBom);
        //    }

        //    _context.Boms.AddRange(addedBoms);
        //    await _context.SaveChangesAsync();

        //    for (var i = 0; i < addedBoms.Count(); i++)
        //    {
        //        var bomRow = prRoot.data.bomGrid;
        //        bomRow[i].itemLineNo = addedBoms[i].Id;
        //    }

        //    var newPR = new Pr
        //    {
        //        Data = pr.Data,
        //        DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot),
        //        CreatedDate = DateTime.Now,
        //        Status = pr.Status,
        //        FormName = pr.FormName,
        //        isMerge = pr.isMerge,
        //        Prname = CommonHelpers.renamePR(pr.Prname, pr.RevisionNo) + "-POC_" + code,
        //        ProjectId = pr.ProjectId,
        //        Remark = pr.Remark,
        //        RequestorId = pr.RequestorId,
        //        Pos = pr.Pos,
        //        LastUpdatedDate = DateTime.Now,
        //        totalApprover = pr.totalApprover,
        //        Rfqid = pr.Rfqid,
        //        AddedBy = pr.AddedBy,
        //        PRApproveDate = pr.PRApproveDate,
        //    };

        //    _context.Prs.Add(newPR);
        //    _context.SaveChanges();

        //    addedBoms.ForEach(x => x.PRId = newPR.Id);
        //    _context.Boms.UpdateRange(addedBoms);

        //    _context.POC.Remove(poc);

        //    await _context.SaveChangesAsync();

        //    await _auditTrailService.InsertLogAsync(
        //            Models.LogLevel.Information, "POC transfering to PO",
        //            fullMessage: "PO has been transfer from POC with PR No" + CommonHelpers.renamePR(pr.Prname, pr.RevisionNo),
        //            tableChange: nameof(POC),
        //            empId: int.Parse(HttpContext.GetUserId()),
        //            ProjectId: pr.ProjectId
        //            );
        //}
    }
}
