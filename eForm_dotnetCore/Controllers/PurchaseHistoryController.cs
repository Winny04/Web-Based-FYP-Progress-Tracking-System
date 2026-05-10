using eForm_dotnetCore.Mapper;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace eForm_dotnetCore.Controllers
{
    public class PurchaseHistoryController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly IEmailService _emailService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public PurchaseHistoryController(EformSophicDemoContext context, IAuthorizationService authorizationService
            , IEmailService emailService, IAuditTrailService auditTrailService, IWebHostEnvironment env)
        {
            _context = context;
            _authorizationService = authorizationService;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
            _env = env;
        }

        public IActionResult Index()
        {
            var vm = new List<PurchaseHistoryVM>();

            var list = _context.PurchaseHistory
                .Include(x => x.PO)
                .ThenInclude(x => x.Requestor)
                .ToList();

            foreach(var item in list)
            {
                var poRoot = JsonConvert.DeserializeObject<PORoot>(item.PO.DataResponse);

                var model = item.ToModel<PurchaseHistoryVM>();
                model.SupplierId = poRoot.data.supplierContainer.FirstOrDefault().supplierKey.id;
                model.SupplierName = poRoot.data.supplierContainer.FirstOrDefault().supplierKey.name;
                poRoot.data.bomGrid.ForEach(x =>
                {
                    model.NetPrice += x.totalPrice.Value;
                });
                vm.Add(model);

            }

            return View(vm);
        }
    }
}
