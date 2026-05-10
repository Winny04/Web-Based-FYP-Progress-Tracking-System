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

namespace eForm_dotnetCore.Controllers
{
    public class PurchaseOrderController : Controller
    {

        private readonly EformSophicDemoContext _context;

        public PurchaseOrderController(EformSophicDemoContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewBag.ProjectId = id;
            var formList = await _context.Pos.Where(x => x.Id == id).FirstOrDefaultAsync();
            if (formList == null)
            {
                return NotFound();
            }

            return View(formList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Po model)
        {
            try
            {
                var newForm = _context.Pos.FirstOrDefault(x => x.Id == model.Id);
                newForm.LastUpdatedDate = DateTime.Now;
                newForm.DataResponse = model.DataResponse;
                _context.Pos.Update(newForm);

                await _context.SaveChangesAsync();

                return RedirectToAction("Index", new { id = newForm.Prid });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", "PurchaseOrder");
            }
        }

        [HttpPost]
        public JsonResult addNewForm(string POName, int prId)
        {
            try
            {
                if (String.IsNullOrEmpty(POName))
                    throw new NullReferenceException(nameof(POName));

                if (PONameExists(POName, prId))
                {
                    return Json(new { exist = true, msg = "*PO Name existed", id = "" });
                }

                var formName = "TEST";
                var form = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();
                var poList = new Po
                {
                    Data = form.Data,
                    AddedBy = "sys",
                    Poname = POName,
                    Prid = prId,
                    CreatedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    Status = "New",
                    FormName = formName
                };

                _context.Pos.Add(poList);
                _context.SaveChanges();

                return Json(new { existed = false, msg = "", id = poList.Id });
            }
            catch (Exception ex)
            {
                throw;
            }

        }

        public bool PONameExists(string POName, int prId)
        {
            return _context.Pos.Where(x => x.Poname == POName && x.Prid == prId).Any();
        }
    }
}


