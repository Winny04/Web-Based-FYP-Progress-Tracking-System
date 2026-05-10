using DocumentFormat.OpenXml.Office2010.Excel;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others;
using eForm_dotnetCore.Services.Others.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace eForm_dotnetCore.Controllers
{
    public class ProjectController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IEmailService _emailService;
        private readonly IFileWriter _fileWriter;

        public ProjectController(
            EformSophicDemoContext context,
            IAuditTrailService auditTrailService,
            IFileWriter fileWriter,
            IEmailService emailService)
        {
            _context = context;
            _auditTrailService = auditTrailService;
            _emailService = emailService;
            _fileWriter = fileWriter;
        }

        [HttpPost]
        [ClaimRequirement("Type", "Sales,PM,HOD,GFC,CEO,CDO,Buyer,SuperAdminAssistant")]
        public async Task<IActionResult> addNewProject(Project model, IFormFile PoFile, IFormFile QfFile)
        {
            if (model != null)
            {
                var count = 1;
                var code = "P" + count;
                var check = _context.Projects.Where(d => d.Code == code).Count();
                while (check > 0)
                {
                    count++;
                    code = "P" + count;
                    check = _context.Projects.Where(d => d.Code == code).Count();
                }
                model.Code = code;
                model.Requestor = HttpContext.User.Identity.Name;
                model.Status = "New";
                model.LastUpdatedDate = model.CreatedDate = DateTime.Now;

                var PICs = new List<int>();
                if (!string.IsNullOrEmpty(model.PICS))
                {
                    model.PICS = model.PICS.TrimEnd(',');
                    PICs = model.PICS.Split(',').Select(int.Parse).ToList();
                }
                await _context.Projects.AddAsync(model);

                if(await _context.SaveChangesAsync() > 1)
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, @$"New Project with name {model.Name} is been created.",
                        tableChange: nameof(Project),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: model.Id
                        );

                // Handle PO File Upload
                if (PoFile != null && PoFile.Length > 0)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await PoFile.CopyToAsync(memoryStream);

                        // Convert to Base64 string
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());

                        var projectPOFile = new ProjectPOFile
                        {
                            ProjectId = model.Id,
                            FileName = Path.GetFileName(PoFile.FileName),
                            FileData = base64String,
                            CreatedDate = DateTime.Now,
                            RequestorId = int.Parse(HttpContext.GetUserId()),
                        };

                        await _context.ProjectPOFiles.AddAsync(projectPOFile);
                        await _context.SaveChangesAsync();
                    }
                }

                // Handle QF File Upload
                if (QfFile != null && QfFile.Length > 0)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await QfFile.CopyToAsync(memoryStream);

                        // Convert to Base64 string
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());

                        var projectQFFile = new ProjectQFFile
                        {                        
                            ProjectId = model.Id,
                            FileName = Path.GetFileName(QfFile.FileName),
                            FileData = base64String,
                            CreatedDate = DateTime.Now,
                            RequestorId = int.Parse(HttpContext.GetUserId()),
                        };

                        await _context.ProjectQFFiles.AddAsync(projectQFFile);
                        await _context.SaveChangesAsync();
                    }
                }


                //Send Email
                var (msg, html) = CommonHelpers.ProjectMailTemplateGenerator("Project", "Created", Url.Action("Index", "BOM", new { id = model.Id }), model.Code);

                //TODO
                var emailList = _context.EmployeeInfos.Where(x => PICs.Contains(x.Id) && !string.IsNullOrEmpty(x.Email) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                var emailList1 = _context.EmployeeInfos.Where(d => (d.UserType == "Project Engineer" || d.UserType == "HOD") && !string.IsNullOrEmpty(d.Email) && d.IsNotificationOn).Select(d => d.Email).ToList();
                emailList.AddRange(emailList1);
                var title = "New Project " + model.Code + " is Created";

                await _emailService.Send(emailList, title, html);

                await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Information, title,
                    pics:PICs,
                    fullMessage: msg,
                    tableChange: nameof(Project),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: model.Id
                    );
            }

            return RedirectToAction("Index", "Home");
        }

        // Show PO PDF
        [HttpGet]
        public IActionResult ViewPOFile(int projectId)
        {
            var poFile = _context.ProjectPOFiles.FirstOrDefault(p => p.ProjectId == projectId);

            if (poFile == null || string.IsNullOrEmpty(poFile.FileData))
            {
                return Json(new { success = false, message = "PO file not found for this project." });
            }

            try
            {
                var fileBytes = Convert.FromBase64String(poFile.FileData);
                return new FileStreamResult(new MemoryStream(fileBytes), "application/pdf");
            }
            catch (FormatException)
            {
                return Json(new { success = false, message = "File data is not a valid Base64 string." });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ViewQFFile(int projectId)
        {
            var qfFile = await _context.ProjectQFFiles
                .Where(q => q.ProjectId == projectId)
                .FirstOrDefaultAsync();

            if (qfFile == null || string.IsNullOrEmpty(qfFile.FileData))
            {
                return Json(new { success = false, message = "QF file not found." });
            }

            byte[] fileBytes = Convert.FromBase64String(qfFile.FileData);
            return File(fileBytes, "application/pdf", qfFile.FileName);
        }

        [HttpPost]
        public IActionResult ArchiveProject(int id)
        {
            var project = _context.Projects.FirstOrDefault(p => p.Id == id);
            if (project != null)
            {
                project.Archive = 1;
                _context.SaveChanges();
                return Ok();
            }
            return NotFound();
        }

        [HttpPost]
        public IActionResult UnarchiveProject(int id)
        {
            var project = _context.Projects.FirstOrDefault(p => p.Id == id);
            if (project != null)
            {
                project.Archive = 0;
                _context.SaveChanges();
                return Ok();
            }
            return NotFound();
        }



        [HttpPost]
        [Authorize(Policy = "PMOnly")]
        public async Task<IActionResult> deleteProject(int id)
        {
            try
            {
                if (!_context.Projects.Any(x => x.Id == id))
                    throw new NullReferenceException(nameof(id));

                var targetProject = await _context.Projects.Where(x => x.Id == id).FirstOrDefaultAsync();
                var targetBOM = await _context.Boms.Include(x => x.RFQ).Where(x => x.ProjectId == id).ToListAsync();
                var bomIdList = targetBOM.Select(x => x.Id).ToList();
                //var targetRFQ = await _context.Rfqs.Where(x => bomIdList.Contains((int)x.Bomid)).ToListAsync();
                var targetRFQ = await _context.Rfqs.Where(x => x.ProjectId == id).ToListAsync();
                var targetPR = await _context.Prs.Where(x => x.ProjectId == id).ToListAsync();

                if(targetRFQ.Any())
                {
                    var targetSupplierAttch = await _context.SupplierAttachment
                        .Include(x => x.SupportFile)
                        .Where(x => targetRFQ.Select(r => r.Id).Contains(x.RFQId)).ToListAsync();

                    foreach(var item in targetSupplierAttch)
                    {
                        _fileWriter.DeleteDocument(item.SupportFile.FileName);
                    }

                    _context.SupplierAttachment.RemoveRange(targetSupplierAttch);

                }
                await _context.SaveChangesAsync();

                if (targetPR.Any())
                {
                    var prList = targetPR.Select(x => x.Id).ToList();
                    var targetPo = await _context.Pos.Where(x => prList.Contains(x.Prid)).ToListAsync();

                    _context.Pos.RemoveRange(targetPo);
                }

                _context.Prs.RemoveRange(targetPR);
                await _context.SaveChangesAsync();

                _context.Rfqs.RemoveRange(targetRFQ);

                await _context.SaveChangesAsync();
                _context.Boms.RemoveRange(targetBOM);

                await _context.SaveChangesAsync();

                _context.Projects.Remove(targetProject);

                if (await _context.SaveChangesAsync() > 1)
                {
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Warning, @$"Project {targetProject.Name} with code {targetProject.Code} is been removed.",
                        pics: !string.IsNullOrEmpty(targetProject.PICS) ? targetProject.PICS.Split(",").Select(int.Parse).ToList() : null,
                        tableChange: nameof(Project),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: targetProject.Id
                        );
                }
                return NoContent();
            }
            catch(Exception ex)
            {
                throw;
            }
        }

        public IActionResult Edit(int id)
        {
            var project = _context.Projects.FirstOrDefault(x => x.Id == id);
            //ViewBag.LastDueDate = project.PoDueDate.Split(",").Last();
            return PartialView("_EditProjectModalPartial", project);
        }

        [HttpPost]
        [ClaimRequirement("Type", "PM,HOD")]
        public async Task<IActionResult> editProject(Project model, List<string> poDueDate, IFormFile PoFile, IFormFile QfFile)
        {
            if (model != null)
            {
                //var newPODueDate = String.Join(",", poDueDate.Where(s => !string.IsNullOrEmpty(s)));

                //var oldPoDueDate = _context.Projects.Where(x => x.Id == model.Id).Select(x => x.PoDueDate).FirstOrDefault();
                //if (!String.IsNullOrEmpty(oldPoDueDate))
                //{
                //    var lastDDCount = oldPoDueDate.Split(",").Count();

                //    if (poDueDate.Count() > lastDDCount)
                //        model.Status = "Overdue";
                //}

                var newPODueDate = String.Join(",", poDueDate.Where(s => !string.IsNullOrEmpty(s)));

                var parsedDueDates = poDueDate
                    .Select(d =>
                    {
                        DateTime parsed;
                        return DateTime.TryParse(d, out parsed) ? (DateTime?)parsed : null;
                    })
                    .Where(d => d.HasValue)
                    .Select(d => d.Value)
                    .ToList();

                if (parsedDueDates.Any())
                {
                    // Find the *latest* due date in the list
                    var latestDueDate = parsedDueDates.Max();

                    // Compare with today's date
                    if (DateTime.Today > latestDueDate)
                        model.Status = "Overdue";
                }


                // Handle PO File Upload
                if (PoFile != null && PoFile.Length > 0)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await PoFile.CopyToAsync(memoryStream);
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());

                        var existingPO = await _context.ProjectPOFiles
                            .FirstOrDefaultAsync(p => p.ProjectId == model.Id);

                        if (existingPO != null)
                        {
                            // Update existing PO file
                            existingPO.FileName = Path.GetFileName(PoFile.FileName);
                            existingPO.FileData = base64String;
                            existingPO.LastUpdatedDate = DateTime.Now;
                        }
                        else
                        {
                            // Add new PO file
                            var newPO = new ProjectPOFile
                            {
                                ProjectId = model.Id,
                                FileName = Path.GetFileName(PoFile.FileName),
                                FileData = base64String,
                                CreatedDate = DateTime.Now,
                            };

                            await _context.ProjectPOFiles.AddAsync(newPO);
                        }

                        await _context.SaveChangesAsync();
                    }
                }


                // Handle QF File Upload 
                if (QfFile != null && QfFile.Length > 0)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await QfFile.CopyToAsync(memoryStream);
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());

                        var existingQF = await _context.ProjectQFFiles
                            .FirstOrDefaultAsync(q => q.ProjectId == model.Id);

                        if (existingQF != null)
                        {
                            existingQF.FileName = Path.GetFileName(QfFile.FileName);
                            existingQF.FileData = base64String;
                            existingQF.LastUpdatedDate = DateTime.Now;
                        }
                        else
                        {
                            var newQF = new ProjectQFFile
                            {
                                ProjectId = model.Id,
                                FileName = Path.GetFileName(QfFile.FileName),
                                FileData = base64String,
                                CreatedDate = DateTime.Now,
                                RequestorId = int.Parse(HttpContext.GetUserId()),
                            };

                            await _context.ProjectQFFiles.AddAsync(newQF);
                        }

                        await _context.SaveChangesAsync();
                    }
                }


                model.LastUpdatedDate = DateTime.Now;
                model.PoDueDate = newPODueDate;

                var existingProject = await _context.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.Id);
                if (existingProject != null)
                {
                    model.Archive = existingProject.Archive;
                }

                _context.Projects.Update(model);


                if (await _context.SaveChangesAsync() > 1)
                    await _auditTrailService.InsertLogAsync(
                        Common.LogLevel.Information, @$"Project with name {model.Name} is been updated.",
                        tableChange: nameof(Project),
                        empId: int.Parse(HttpContext.GetUserId()),
                        ProjectId: model.Id
                        );
            }

            return RedirectToAction("Index", "Home");
        }


        [HttpPost]
        public async Task<JsonResult> MarkProjectCompleted(int projectId)
        {
            try
            {
                var project = _context.Projects.Where(x => x.Id == projectId).FirstOrDefault();
                if(project != null)
                {
                    project.Status = "Completed";
                    project.LastUpdatedDate = DateTime.Now;
                    project.MarkCompletedBy = int.Parse(HttpContext.GetUserId());
                    _context.Update(project);

                    if (_context.SaveChanges() > 0)
                    {
                        await _auditTrailService.InsertLogAsync(
                                Common.LogLevel.Information, $@"Project Code {project.Code} has been mark as Completed by {User.Identity.Name}",
                                pics: !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(",").Select(int.Parse).ToList() : null,
                                tableChange: nameof(project),
                                empId: int.Parse(HttpContext.GetUserId()),
                                ProjectId: project.Id
                                );
                    }

                    return Json(new { status = true, msg = "Project has been mark as Completed." });
                }
                return Json(new { status = false, msg = "Error occur. Project ID not found." });
            }
            catch(Exception ex)
            {
                return Json(new { status = false, msg = "Internal Server Error. " + ex.Message });
            }
        }

    }
}
