using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eForm_dotnetCore.Controllers
{
    public class AuditTrailsController : Controller
    {
        private readonly EformSophicDemoContext _context;

        public AuditTrailsController(EformSophicDemoContext context)
        {
            _context = context;
        }



        // GET: AuditTrailsController
        [Authorize]
        public ActionResult Index()
        {
            var viewData = new AuditTrailsViewModel<AuditTrail>();

            var query = _context.AuditTrail
                .Include(x => x.Employee)
                .Include(x => x.Project)
                .AsQueryable();

            //today
            viewData.Today.AddRange(query.Where(x => x.CreatedOn.Day.Equals(DateTime.Today.Day)).ToList());

            //week
            var startDate = DateTime.Now.StartOfWeek(DayOfWeek.Monday);
            var endDate = startDate.AddDays(6);
            viewData.Week.AddRange(query.Where(x => x.CreatedOn >= startDate && x.CreatedOn <= endDate).ToList());

            //month
            viewData.Month.AddRange(query.Where(x => x.CreatedOn.Year == DateTime.Now.Year && x.CreatedOn.Month == DateTime.Now.Month).ToList());

            //all
            viewData.All.AddRange(query.ToList());

            return View(viewData);
        }
    }
}
