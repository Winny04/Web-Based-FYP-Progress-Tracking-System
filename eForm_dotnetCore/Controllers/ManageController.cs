using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.RegularExpressions;

namespace eForm_dotnetCore.Controllers
{
    public class ManageController:Controller
    {
        private readonly EformSophicDemoContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly IEmailService _emailService;
        private readonly IAuditTrailService _auditTrailService;
        private IWebHostEnvironment _env;

        public ManageController(EformSophicDemoContext context, IAuthorizationService authorizationService
            , IEmailService emailService, IAuditTrailService auditTrailService, IWebHostEnvironment env)
        {
            _context = context;
            _authorizationService = authorizationService;
            _emailService = emailService;
            _auditTrailService = auditTrailService;
            _env = env;
        }

        public ActionResult Category()
        {
            var category = _context.Category.ToList();
            ViewBag.CategoryList = category;

            return View();
        }

        [HttpPost]
        public ActionResult EditCategory(int categoryId, string categoryName, string categoryShortName)
        {
            if (categoryId != 0 && !String.IsNullOrEmpty(categoryName) && !String.IsNullOrEmpty(categoryShortName))
            {
                var check = _context.Category.Where(x => x.Id == categoryId).FirstOrDefault();
                if (check != null)
                {
                    check.CategoryName = categoryName;
                    check.CategoryShortName = categoryShortName;
                    _context.SaveChanges();
                }
            }
            return RedirectToAction("Category");
        }

        public ActionResult RemoveCategory(int id)
        {
            var category = _context.Category.Where(d => d.Id == id).FirstOrDefault();
            if (category != null)
            {
                _context.Category.Remove(category);
                _context.SaveChanges();
            }
            return RedirectToAction("Category");
        }

        public ActionResult EmailContent(string type)
        {
            var emailConfig = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == type);
            string replaced = Regex.Replace(emailConfig.ConfigValue, "(?<!\\\\)\"", "&quot;");
            emailConfig.ConfigValue = replaced;
            return View(emailConfig);
        }

        [HttpPost]
        public JsonResult UpdateEmailContent(string type,string content)
        {
            var emailConfig = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == type);
            var getTemplateInfo = EmailTemplateConfiguration.GetEmailTemplateByType(type);
            var requireSyntax = string.Join(",", getTemplateInfo.Value.Item2);
            var errorMsg = "The content should contain " + requireSyntax;
            bool checkNoContainSyntax = false;

            if (string.IsNullOrEmpty(content))
            {
                return Json(new { result = false, msg = errorMsg });
            }

            content = Regex.Replace(content, "(?<!\\\\)\"", "&quot;");
            content = content.Replace("<p><br></p>", "<br/>");

            foreach (var param in getTemplateInfo.Value.Item2)
            {
                if (!content.Contains(param))
                {
                    checkNoContainSyntax = true;
                    break;
                }
            }

            if (checkNoContainSyntax)
            {
                return Json(new { result = false, msg = errorMsg });
            }

            if (emailConfig != null)
            {
                emailConfig.ConfigValue = content;
                _context.SaveChanges();
            }

            return Json(new { result = true });
        }

        [HttpPost]
        public ActionResult AddCategory(Category category)
        {
            if (ModelState.IsValid)
            {
                var check = _context.Category.Where(x => x.CategoryName == category.CategoryName).Count();

                if (check == 0)
                {
                    _context.Category.Add(category);
                    _context.SaveChanges();

                    return RedirectToAction("Category");
                }
            }
            return View(category);
        }

        public class AddCategoryDto 
        {
            public string CategoryName { get; set; }
            public string CategoryShortName { get; set; }
        }


        public IActionResult Department()
        {
            var list = _context.Department
                .Include(x => x.EmployeeInfo)
                .ToList();

            return View(list);
        }

        public IActionResult Location()
        {
            var list = _context.DeliveryLocation
                .ToList();

            return View(list);
        }

        [HttpPost]
        public IActionResult EditDepartment(Department department)
        {
            var model = _context.Department
                .FirstOrDefault(x => x.Id == department.Id);

            model.Name = department.Name;

            _context.SaveChanges();

            return RedirectToAction("Department");
        }

        [HttpPost]
        public IActionResult EditLocation(DeliveryLocation location)
        {
            var model = _context.DeliveryLocation
                .FirstOrDefault(x => x.Id == location.Id);

            model.FullAddress = location.FullAddress;
            model.Name = location.Name;

            _context.SaveChanges();

            return RedirectToAction("Location");
        }

        [HttpPost]
        public IActionResult DeleteLocation(int id)
        {
            var model = _context.DeliveryLocation
                .FirstOrDefault(x => x.Id == id);

            if(model != null)
            {
                _context.DeliveryLocation.Remove(model);
                _context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        public IActionResult DeleteDepartment(int id)
        {
            var model = _context.Department
                .FirstOrDefault(x => x.Id == id);

            if (model != null)
            {
                _context.Department.Remove(model);
                _context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        public IActionResult AddDepartment(Department department)
        {
            if (ModelState.IsValid)
            {
                var checkExist = _context.Department.Any(x => x.Name == department.Name.Trim());

                if (!checkExist)
                {
                    _context.Department.Add(department);
                    _context.SaveChanges();
                }
            }

            return RedirectToAction("Department");
        }

        [HttpPost]
        public IActionResult AddLocation(DeliveryLocation location)
        {
            if (ModelState.IsValid)
            {
                var checkExist = _context.DeliveryLocation.Any(x => x.Name == location.Name.Trim());

                if (!checkExist)
                {
                    _context.DeliveryLocation.Add(location);
                    _context.SaveChanges();
                }
            }

            return RedirectToAction("Location");
        }

        [HttpGet]
        public JsonResult GetLocations()
        {
            var locationList = new List<object>();
            locationList.Add(new { name ="Other(Manually Type)",address=""});

            var loadLocations = _context.DeliveryLocation.Select(x => new { name = x.Name,address = x.FullAddress }).ToList();
            locationList.AddRange(loadLocations);

            return Json(locationList);
        }

        [HttpGet]
        public IActionResult SupplierEmail()
        {
            // Materialize data to avoid open DataReader issues
            var supplierList = _context.SupplierInfo.ToList();
            var brandSupplierList = _context.BrandSupplier
                .Include(bs => bs.Brand)
                .ToList();
            var addedSuppliers = _context.AdditionalSuppliers
                .Where(s => !string.IsNullOrEmpty(s.Email))
                .ToList();

            var addedSupplierEmails = addedSuppliers
                .Select(s => s.Email.Trim().ToLower())
                .ToHashSet();

            var model = new SupplierEmailViewModel
            {
                Suppliers = supplierList
                    .Select(s => new SupplierDTO
                    {
                        Id = s.Id,
                        VendorName = s.VendorName,
                        Email = GetSupplierEmail(s.DataResponse),
                        BrandNames = string.Join(", ",
                            brandSupplierList
                                .Where(bs => bs.SupplierId == s.Id)
                                .Select(bs => bs.Brand.Name))
                    })
                    .Where(s => !string.IsNullOrEmpty(s.Email) &&
                                !addedSupplierEmails.Contains(s.Email.Trim().ToLower()))
                    .ToList(),

                AddedSuppliers = addedSuppliers
                    .Select(s => new SupplierDTO
                    {
                        Id = s.Id,
                        VendorName = s.VendorName,
                        Email = s.Email,
                        BrandNames = string.Join(", ",
                            brandSupplierList
                                .Where(bs => supplierList.Any(si => si.VendorName == s.VendorName && si.Id == bs.SupplierId))
                                .Select(bs => bs.Brand.Name))
                    })
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult AddAdditionalSupplier([FromBody] List<SupplierDTO> suppliers)
        {
            if (suppliers == null || !suppliers.Any())
            {
                Console.WriteLine("❌ No suppliers received.");
                return Json(new { success = false, message = "No suppliers received." });
            }

            Console.WriteLine($"✅ Received {suppliers.Count} suppliers: {string.Join(", ", suppliers.Select(s => s.VendorName))}");

            var validSuppliers = suppliers
                .Where(s => !string.IsNullOrWhiteSpace(s.VendorName) && !string.IsNullOrWhiteSpace(s.Email))
                .Select(s => new AdditionalSupplier
                {
                    Id = Guid.NewGuid(),
                    VendorName = s.VendorName.Trim(),
                    Email = s.Email.Trim()
                })
                .ToList();

            if (!validSuppliers.Any())
            {
                Console.WriteLine("❌ Invalid supplier data.");
                return Json(new { success = false, message = "Invalid supplier data." });
            }

            _context.AdditionalSuppliers.AddRange(validSuppliers);
            _context.SaveChanges();

            Console.WriteLine("✅ Suppliers added successfully!");
            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult DeleteAdditionalSupplier(List<Guid> ids)
        {
            var suppliersToRemove = _context.AdditionalSuppliers.Where(s => ids.Contains(s.Id)).ToList();
            if (suppliersToRemove.Any())
            {
                _context.AdditionalSuppliers.RemoveRange(suppliersToRemove);
                _context.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        public IActionResult ManageAdditionalSupplierEmail()
        {
            var suppliers = _context.SupplierInfo
                .AsEnumerable()
                .Select(s => new SupplierDTO
                {
                    Id = s.Id,
                    VendorName = s.VendorName,
                    Email = GetSupplierEmail(s.DataResponse),
                    BrandNames = string.Join(", ", _context.BrandSupplier
                        .Where(bs => bs.SupplierId == s.Id)
                        .Select(bs => bs.Brand.Name)
                        .ToList())
                })
                .Where(s => !string.IsNullOrEmpty(s.Email))
                .ToList();

            var addedSuppliers = _context.AdditionalSuppliers
                .Select(s => new SupplierDTO
                {
                    Id = s.Id,
                    VendorName = s.VendorName,
                    Email = s.Email,
                    BrandNames = string.Join(", ", _context.BrandSupplier
                        .Where(bs => _context.SupplierInfo
                            .Any(si => si.VendorName == s.VendorName && si.Id == bs.SupplierId))
                        .Select(bs => bs.Brand.Name)
                        .ToList())
                })
                .ToList();

            var viewModel = new SupplierEmailViewModel
            {
                Suppliers = suppliers,
                AddedSuppliers = addedSuppliers
            };

            return View(viewModel);
        }
        private string GetSupplierEmail(string? dataResponse)
        {
            if (!string.IsNullOrEmpty(dataResponse))
            {
                try
                {
                    var json = JObject.Parse(dataResponse);
                    var email = json["data"]?["emailAddress"]?.ToString()?.Trim();

                    return !string.IsNullOrEmpty(email) ? email : null; // Exclude empty emails
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON: {ex.Message}");
                    return null;
                }
            }
            return null;
        }


    }
}
