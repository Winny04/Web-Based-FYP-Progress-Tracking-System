using FYPTracker.Helpers;
using FYPTracker.Models;
using FYPTracker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FYPTracker.Controllers
{
    public class AuditTrailsController : Controller
    {
        private readonly FYPTrackerContext _context;

        public AuditTrailsController(FYPTrackerContext context)
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
