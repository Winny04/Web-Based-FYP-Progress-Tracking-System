using eForm_dotnetCore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using eForm_dotnetCore.Helpers;

namespace eForm_dotnetCore.Controllers
{
    public class InvoiceController : Controller
    {
        private readonly EformSophicDemoContext _context;

        public InvoiceController(EformSophicDemoContext context)
        {
            _context = context;
        }

        // GET: Invoice
        public async Task<IActionResult> Index(string searchQuery)
        {
            if (TempData["Error"] != null) TempData.Keep("Error");
            if (TempData["Success"] != null) TempData.Keep("Success");

            var invoices = _context.Invoices.Where(i => i.DeleteStatus == 0); // Active invoices
            var deletedInvoices = await _context.Invoices.Where(i => i.DeleteStatus == 1).ToListAsync();

            // ✅ Ensure ViewBag.DeletedInvoices is never null
            ViewBag.DeletedInvoices = deletedInvoices ?? new List<Invoice>();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                invoices = invoices.Where(i =>
                    i.InvoiceNumber.Contains(searchQuery) ||
                    i.FileName.Contains(searchQuery) ||
                    i.UploadDate.ToString().Contains(searchQuery) ||
                    i.UploadedBy.Contains(searchQuery));
            }

            return View("~/Views/Invoicing/Index.cshtml", await invoices.ToListAsync());
        }


        // POST: Invoice/Upload
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string invoiceNumber)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(invoiceNumber))
                errors.Add("Invoice number is required.");

            if (file == null || file.Length == 0)
                errors.Add("Please provide a valid file.");

            if (file != null)
            {
                var allowedExtensions = new[] { ".xls", ".xlsx", ".pdf", ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(fileExtension))
                    errors.Add("Invalid file type. Only XLS, PDF, JPG, and PNG are allowed.");
            }

            bool invoiceExists = await _context.Invoices.AnyAsync(i => i.InvoiceNumber == invoiceNumber);
            if (invoiceExists)
                errors.Add($"Invoice number {invoiceNumber} already exists. Please use a different number.");

            if (errors.Any())
            {
                ViewBag.ErrorMessages = errors;
                return View("~/Views/Invoicing/Index.cshtml", await _context.Invoices.ToListAsync());
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            string base64String = Convert.ToBase64String(memoryStream.ToArray());

            var uploadedBy = User.Identity.Name ?? "Unknown User";

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                FileName = file.FileName,
                FileData = base64String,
                UploadedBy = uploadedBy,
                UploadDate = DateTime.Now
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            ViewBag.SuccessMessage = "Invoice uploaded successfully!";
            return View("~/Views/Invoicing/Index.cshtml", await _context.Invoices.ToListAsync());
        }


        // GET: Invoice/ViewFile/5
        public async Task<IActionResult> ViewFile(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                return NotFound();

            var fileBytes = Convert.FromBase64String(invoice.FileData);
            var contentType = GetContentType(invoice.FileName);

            // If the file is an Excel file, force it to be downloaded with its correct filename
            if (contentType == "application/vnd.ms-excel" || contentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                return File(fileBytes, contentType, invoice.FileName);
            }

            // Otherwise, let the browser handle the file (PDFs and images will open in a new tab)
            return File(fileBytes, contentType);
        }

        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
            {
                TempData["Error"] = "Invoice not found.";
                return RedirectToAction("Index");
            }

            invoice.DeleteStatus = 1; // Mark as deleted
            invoice.DeletedBy = User.Identity.Name ?? "Unknown User"; // Get logged-in user
            invoice.DeletedDate = DateTime.Now;

            _context.Invoices.Update(invoice);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Invoice deleted successfully.";
            return RedirectToAction("Index");
        }


    }
}