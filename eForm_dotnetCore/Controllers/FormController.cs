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
using Microsoft.AspNetCore.Authorization;
using eForm_dotnetCore.Domain;
using System.Text.Json;
using eForm_dotnetCore.Mapper;

namespace eForm_dotnetCore.Controllers
{
    public class FormController : Controller
    {
        private readonly EformSophicDemoContext _context;

        public FormController(EformSophicDemoContext context)
        {
            _context = context;
        }

        // GET: Form
        [Authorize]
        public IActionResult Index()
        {
            var formList = (from form in _context.Forms
                            select new TableData
                            {
                                Id = form.Id,
                                Name = form.Name,
                                Revision = form.Revision,
                                CreatedAt = (Convert.ToDateTime(form.DateCreated).ToString("dd MMM yyyy HHmm")),
                                Status = form.Status,
                                LastUpdated = form.LastUpdated,
                                DocumentName = form.DocumentName,
                                Remark = form.Remark,
                                ModifiedBy = form.ModifiedBy
                            })
                          .Where(x => x.Status != "Deleted" || x.Status != "Archived")
                          .OrderByDescending(x => x.LastUpdated).ToList();

            return View(formList);
        }

        [HttpPost]
        public async Task<IActionResult> addNewRequest(string request_title, string assignDate, string description)
        {
            var newForm = new Form
            {
                DateCreated = DateTime.Now,
                LastUpdated = DateTime.Now,
                AddedBy = Int32.Parse(HttpContext.GetUserId()),
                Name = request_title,
                DocumentName = request_title,
                Remark = description,
                Status = "Unpublish",
                Revision = "Rev 1.0"
            };

            await _context.Forms.AddAsync(newForm);
            await _context.SaveChangesAsync();

            //var newData = new TableData
            //{
            //    Requestor = HttpContext.User.Identity.Name,
            //    Description = "new form",
            //    CreatedAt = (Convert.ToDateTime(DateTime.Now).ToString("dd MMM yyyy HHmm")),
            //    Status = "Unpublish",
            //    Image = "",
            //    LastUpdated = DateTime.Now,
            //    Role = "Manager"
            //};

            //return Json(newData);
            return RedirectToAction("Edit", new { id = newForm.Id });
        }

        [HttpDelete]
        public async Task<IActionResult> deleteRequest(int id)
        {
            if (!_context.Forms.Any(x => x.Id == id))
                throw new NullReferenceException(nameof(id));

            var delete = await _context.Forms.FirstOrDefaultAsync(x => x.Id == id);
            _context.Forms.Remove(delete);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public ActionResult View(int? id)
        {
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.EBOM);
            navigation.Add(PageTitle.Form);
            navigation.Add(PageTitle.ViewForm);
            link.Add("/Home/Index");
            link.Add("/Form/Index");
            link.Add("/Form/View/" + id);
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;
            HttpContext.Session.SetInt32("id", (int)id);
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var form = _context.Forms.Where(x => x.Id == id).FirstOrDefault();

            return View(form);
        }

        public ActionResult Edit(int? id)
        {
            var id1 = HttpContext.Session.GetInt32("id");
            List<string> navigation = new List<string>();
            List<string> link = new List<string>();
            navigation.Add(MenuTitle.EBOM);
            navigation.Add(PageTitle.Form);
            navigation.Add(PageTitle.ViewForm);
            navigation.Add(PageTitle.EditForm);
            link.Add("/Home/Index");
            link.Add("/Form/Index");
            link.Add("/Form/View/" + id1);
            link.Add("/Form/Edit/" + id);
            ViewBag.Navigation = navigation;
            ViewBag.Link = link;
            HttpContext.Session.SetInt32("id1", (int)id);
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var form = _context.Forms.Where(x => x.Id == id).FirstOrDefault();
            ViewBag.FormId = id;

            //var Department = _context.Departments.ToList();
            //ViewBag.Department = new SelectList(Department, "Id", "DepartmentName");

            if (!string.IsNullOrEmpty(form.Revision))
            {
                var revFloat = float.Parse(form.Revision.Remove(0, 4));
                ViewBag.NewRevision = (revFloat).ToString("f1");
            }

            //TempForm temp_Form = _context.TempForms.Where(a => a.FormId == id).OrderByDescending(a => a.LastUpdated).FirstOrDefault();
            //if (temp_Form != null)
            //{
            //    if (temp_Form.LastUpdated > form.LastUpdated)
            //    {
            //        form.Data = temp_Form.Data;
            //        ViewBag.Recovery = "Recovered form. Click save to save this recovered form or click cancel to remove this recovered form.";
            //    }
            //}

            return View(form);
        }

        [HttpPost]
        public ActionResult Edit(FormVM form)
        {
            if (form == null)
                throw new ArgumentNullException(nameof(form));

            form.LastUpdated = DateTime.Now;
            form.DateCreated = DateTime.Now;
            form.ModifiedBy = HttpContext.User.Identity.Name;
            form.Remark = "Edit by " + HttpContext.User.Identity.Name;

            _context.Forms.Update(form.ToModel<Form>());
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost]
        public IActionResult Publish(Form form)
        {
            if (form == null)
                throw new ArgumentNullException(nameof(form));


            form.Status = "Publish";
            form.LastUpdated = DateTime.Now;
            form.DateCreated = DateTime.Now;    //data error for datecreated : 12/2/0001 <-- years error in sqlserver
            form.Remark = "Publish by " + HttpContext.User.Identity.Name;
            form.ModifiedBy = HttpContext.User.Identity.Name;
            _context.Forms.Update(form);

            _context.SaveChanges();

            return RedirectToAction("Index", "Form");
        }

        private bool FormExists(int id)
        {
            return _context.Forms.Any(e => e.Id == id);
        }
    }
}
