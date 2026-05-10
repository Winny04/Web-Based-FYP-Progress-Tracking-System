﻿#nullable disable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Services.Others.Interfaces;
using Newtonsoft.Json;
using eForm_dotnetCore.Mapper;
using System.Data;
using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Services.AuditTrails;
using ClosedXML.Excel;
using static eForm_dotnetCore.ViewModels.POTrackerExcelModel;
using eForm_dotnetCore.Common;
using Microsoft.AspNetCore.Hosting.Server;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Globalization;
using System.Linq.Expressions;
using eForm_dotnetCore.Configuration;
using NPOI.SS.Formula.Functions;
using MoreLinq;

namespace eForm_dotnetCore.Controllers
{
    public class POTrackerController : Controller
    {
        private readonly EformSophicDemoContext _context;
        private IWebHostEnvironment Environment;
        private readonly IFileWriter _fileWriter;
        private readonly IAuthorizationService _authorizeService;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IServer _server;
        private readonly IEmailService _emailService;

        public POTrackerController(EformSophicDemoContext context,
            IWebHostEnvironment _environment,
            IFileWriter fileWriter,
            IAuthorizationService authorizeService,
            IAuditTrailService auditTrailService,
            IServer server,
            IEmailService emailService)
        {
            _context = context;
            Environment = _environment;
            _fileWriter = fileWriter;
            _authorizeService = authorizeService;
            _auditTrailService = auditTrailService;
            _server = server;
            _emailService = emailService;
        }

        [Authorize]
        [ClaimRequirement("Type", "CEO,GFC,HOD,PM,Sales,Buyer,Purchaser,Requestor,Quality,Finance")]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            var list = new List<POTrackerViewModel>();
            try
            {
                list = await LoadPOList(null);

                if (fromDate.HasValue && toDate.HasValue)
                {
                    var end = toDate.Value.Date.AddDays(1);
                    list = list.Where(x => x.PRApproveDate >= fromDate.Value && x.PRApproveDate < end).ToList();
                }
                else if (fromDate.HasValue)
                {
                    list = list.Where(x => x.PRApproveDate >= fromDate.Value).ToList();
                }
                else if (toDate.HasValue)
                {
                    var end = toDate.Value.Date.AddDays(1);
                    list = list.Where(x => x.PRApproveDate < end).ToList();
                }


                return View(list);

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private Po GeneratePO(Pr pr)
        {
            if (pr != null)
            {
                var formName = "PO";
                var poTemp = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();
                if (poTemp == null) throw new Exception("PO Form didn't found!");

                var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);

                // Filter out void items
                if (prRoot?.data?.bomGrid != null)
                {
                    prRoot.data.bomGrid = prRoot.data.bomGrid.Where(x => x.isVoid != true).ToList();
                }

                var poData = new POData();
                var poSupplier = new POSupplierContainer();

                poData = prRoot.data.ToModel<POData>();
                poData.poSupplierContainer = new List<POSupplierContainer>();
                poData.createDate = poData.updateDate = DateTime.Now;
                poData.requester = prRoot.data.header.FirstOrDefault().requester;
                poData.project = prRoot.data.header.FirstOrDefault().projectName;
                poData.department = prRoot.data.header.FirstOrDefault().department;
                poData.subTotal = prRoot.data.bomGrid.Select(x => x.totalPrice).FirstOrDefault();

                poSupplier.quotName = prRoot.data.supplierContainer.Select(x => x.quotName).FirstOrDefault();
                poSupplier.quotNoLink = prRoot.data.supplierContainer.Select(x => x.quotNoLink).FirstOrDefault();
                poSupplier.quoUrl = prRoot.data.supplierContainer.Select(x => x.quoUrl).FirstOrDefault();
                poSupplier.remark = prRoot.data.supplierContainer.Select(x => x.remark).FirstOrDefault();
                poSupplier.supplierName = prRoot.data.supplierContainer.FirstOrDefault()?.supplierKey?.name;
                poSupplier.supplierId = prRoot.data.supplierContainer.FirstOrDefault()?.supplierKey?.id;

                var supplierInfo = _context.SupplierInfo.FirstOrDefault(x => x.Id.ToString() == poSupplier.supplierId);
                if (supplierInfo != null && !string.IsNullOrEmpty(supplierInfo.DataResponse))
                {
                    var supplierData = JsonConvert.DeserializeObject<SupplierRoot>(supplierInfo.DataResponse);
                    poSupplier.supplierCode = supplierData.data?.registrationCode;
                    poSupplier.address = supplierData.data?.address;
                    poSupplier.tel = supplierData.data?.telephoneNumber;
                    poSupplier.fax = supplierData.data?.faxNumber;
                }

                poData.poSupplierContainer.Add(poSupplier);
                var newPORoot = new PORoot(poData);
                var newPO = new Po
                {
                    Data = poTemp.Data,
                    AddedBy = HttpContext.GetShortName(),
                    FormName = formName,
                    DataResponse = System.Text.Json.JsonSerializer.Serialize(newPORoot),
                    CreatedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    Prid = pr.Id,
                    RequestorId = pr.RequestorId
                };

                _context.Pos.Add(newPO);
                _context.SaveChanges();

                return newPO;
            }

            return null;
        }

        [HttpPost]
        public async Task<IActionResult> RevertPO(int? prId)
        {

            try
            {
                var boms = _context.Boms.Where(x => x.PRId == prId).ToList();

                foreach (var bom in boms)
                {
                    bom.IsPOUpdated = false;
                    _context.Boms.Update(bom);

                    var bomSupportFile = _context.BomSupportFile.Where(x => x.BomId == bom.Id && (x.Type == "PO")).ToList();
                    foreach (var file in bomSupportFile)
                    {
                        var suppFile = _context.SupportFile.FirstOrDefault(x => x.Id == file.SupportFileId);
                        _fileWriter.DeleteDocument(suppFile.FileName);
                    }

                    if (bomSupportFile != null)
                    {
                        _context.BomSupportFile.RemoveRange(bomSupportFile);
                        bom.Status = "PR Approved";
                    }

                    await _context.SaveChangesAsync();

                    var auditTrail = await _auditTrailService.InsertLogAsync(
                            Common.LogLevel.Information, "The PO in BOM with ID " + bom.Id + " has been revert to PR Approved from PO Tracker",
                            fullMessage: "Revert PO from PO Tracker",
                            tableChange: nameof(Bom),
                            dataBefore: JsonConvert.SerializeObject(bom),
                            dataAfter: "",
                            empId: int.Parse(HttpContext.GetUserId()),
                            ProjectId: bom.ProjectId
                            );
                }
                return Json(new { result = true });
            }
            catch (Exception ex)
            {
                return Json(new { result = false, msg = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> POWithRemark(int prId,int type, string? remark)
        {
            if(type == 1)
            {
                return await RevisePO(prId, remark);
            }

            return await RejectPO(prId, remark);
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var empid = HttpContext.GetUserEmpId().ToString();
            if (HttpContext.GetUserEmpId() != "EO0006" && (HttpContext.GetUserType() != "SuperAdmin" || HttpContext.GetUserType() != "SuperAdminAssistant"))
            {
                return Forbid();
            }

            var po = _context.Pos
            .FirstOrDefault(x => x.Prid == id);

            var pr = _context.Prs.Where(x => x.Id == id)
            .Include(p => p.Project)
            .FirstOrDefault();

            if (po == null)
            {
               po= GeneratePO(pr);
            }

            var poTemp = _context.Forms.Where(x => x.Name == "PO").FirstOrDefault();
            if (poTemp == null) throw new Exception("PO Form no found!");

            po.Data = poTemp.Data;

            ViewBag.isProjectCompleted = pr.Project.Status == "Completed";
            po.Pr = pr;
            return View(po);
        }

        [HttpPost]
        public IActionResult Edit(Po form)
        {
            var po = _context.Pos.FirstOrDefault(x => x.Id == form.Id);

            if (po == null) return NotFound();

            if (!string.IsNullOrEmpty(form.DataResponse)) po.DataResponse = form.DataResponse;

            _context.SaveChanges();

            return RedirectToAction("Index");
        }
        [HttpGet]
        public async Task<IActionResult> POHistory(int? id)
        {
            var data = await LoadPOList(id,true);
            return View(data);
        }
        public JsonResult GeneratePOFormPDF(Po form)
        {
            try
            {
                var po = _context.Pos.Where(x => x.Id == form.Id).FirstOrDefault();
                var pr = _context.Prs.FirstOrDefault(x => x.Id == po.Prid);
                if (String.IsNullOrEmpty(form.DataResponse))
                {
                    form.DataResponse = po.DataResponse;
                }
                else
                {
                    po.DataResponse = form.DataResponse;
                }
                var poRoot = JsonConvert.DeserializeObject<PORoot>(form.DataResponse ?? "");
                SupplierRoot supplierRoot = null;
                if (poRoot != null)
                {
                    po.Poname = poRoot.data.poNo;
                    _context.Pos.Update(po);
                    _context.SaveChanges();
                    var supplierContainer = poRoot.data.supplierContainer;
                    if(supplierContainer != null && supplierContainer.Count > 0)
                    {
                        var supplier = _context.SupplierInfo.FirstOrDefault(x => x.Id == Guid.Parse(supplierContainer[0].supplierKey.id));
                        supplierRoot = JsonConvert.DeserializeObject<SupplierRoot>(supplier.DataResponse ?? "");
                    }
                }

                if (po != null)
                {
                    var formName = "POForm";
                    var poTemp = _context.Forms.Where(x => x.Name == formName).FirstOrDefault();
                    if (poTemp == null) throw new Exception("PO Form didn't found!");
                    
                    //poData.createDate = poData.updateDate = DateTime.Now;
                    //poData.requester = prRoot.data.header.FirstOrDefault().requester;
                    //poData.project = prRoot.data.header.FirstOrDefault().projectName;
                    //poData.department = prRoot.data.header.FirstOrDefault().department;


                    var poFormRoot = new POFormRoot();
                    var poFormData = new POFormData();
                    // poFormData = poFormRoot.data;
                    poFormData.poItemList = new List<POFormPoItemList>();

                            
                    var bomGrid = new List<POFormPoItemList>();
                    var root = poRoot.data;
                    poFormData.agent = root.requester;
                    poFormData.category = "";
                    poFormData.customer = "";
                    poFormData.Customer = root.attention;
                    poFormData.customerAddr = supplierRoot != null ? supplierRoot.data.address : root.poSupplierContainer.Select(x => x.address).FirstOrDefault();
                    poFormData.customerFax = supplierRoot != null ? supplierRoot.data.faxNumber: root.poSupplierContainer.Select(x => x.fax).FirstOrDefault();  //where
                    poFormData.CustomerName = root.poSupplierContainer.Select(x => x.supplierName).FirstOrDefault();
                    poFormData.customerTel = supplierRoot != null ? supplierRoot.data.telephoneNumber : root.poSupplierContainer.Select(x => x.tel).FirstOrDefault();
                    poFormData.deliveryTo = root.delCompany;
                    poFormData.discount = root.bomGrid.Select(x => x.discount).FirstOrDefault();
                    poFormData.eta = root.deliveryDate;
                    poFormData.grandTotal = root.grandTotal;
                    poFormData.nett = root.netTotal;
                    poFormData.page = "";
                    poFormData.paymentTerms = root.creditTerms;
                    poFormData.poDate = root.createDate;
                    poFormData.poNumber = root.poNo;
                    poFormData.poDate = root.createDate;
                    poFormData.projectCode = "";
                    poFormData.projectName = "";
                    poFormData.refNo = root.quotationNo;
                    poFormData.remark = root.remark;
                    poFormData.subtotal = root.netTotal;
                    poFormData.tax = root.tax;
                    string grandTotal = root.grandTotal.HasValue ? root.grandTotal?.ToString("#.00", CultureInfo.InvariantCulture) : "0.00";
                    poFormData.totalPriceText = DecimalToTextCurrencyHelper.ConvertToWords(grandTotal);
                    poFormData.acc_description = root.acc_description;
                    poFormData.acc_status = root.acc_status;
                    poFormData.batchGroup = root.batchGroup;

                    //Item List
                    foreach (var item in poRoot.data.bomGrid.ToList())
                    {
                        var model = new POFormPoItemList();

                        model.itemNo = item.itemLineNo;
                        model.description = item.description;
                        model.qty = item.totalQuantity ?? 0;
                        model.unitPrice = item.unitPrice ?? 0.00;
                        model.discount = item.discount ?? 0.00;
                        model.tax = item.tax ?? 0.00;
                        model.amount = item.totalPrice ?? 0.00;

                        poFormData.poItemList.Add(model);
                    }

                    poFormRoot.data = poFormData;


                    #region Generate PDF Section
                    //Set font size and style 
                    FontFactory.RegisterDirectories();
                    BaseFont TimesNRoman = BaseFont.CreateFont(BaseFont.TIMES_ROMAN, BaseFont.CP1252, false);

                    //Font fontSmaller = new Font(FontFactory.GetFont("Arial", 9, Font.NORMAL));
                    Font fontDamnSmall = new Font(TimesNRoman, 8, Font.NORMAL);
                    Font fontSmaller = new Font(TimesNRoman, 10, Font.NORMAL);
                    Font fontSmall = new Font(TimesNRoman, 11, Font.NORMAL);
                    Font fontSmallBold = new Font(TimesNRoman, 11, Font.BOLD);
                    Font fontMedium = new Font(TimesNRoman, 12, Font.NORMAL);
                    Font fontMediumBold = new Font(TimesNRoman, 12, Font.BOLD);
                    Font fontLargeBold = new Font(TimesNRoman, 15, Font.BOLD);
                    var headerFont = new Font(Font.FontFamily.HELVETICA, 12, Font.BOLD);

                    Document pdfDoc = new Document(PageSize.A4, 10f, 7f, 5f, 7f);
                    System.IO.MemoryStream stream = new System.IO.MemoryStream();
                    PdfWriter writer = PdfWriter.GetInstance(pdfDoc, stream);
                    pdfDoc.Open();
                    int PageNo = 1;

                    //Header 
                    var headerTables = PDFHeader(poFormData, PageNo, writer, pdfDoc);
                    foreach (var table in headerTables)
                    {
                        pdfDoc.Add(table);
                    }

                    //Item
                    PdfPTable itemTable = new PdfPTable(6);
                    itemTable.WidthPercentage = 95;//table %100 width
                    itemTable.HorizontalAlignment = Element.ALIGN_LEFT;
                    itemTable.DefaultCell.Border = Rectangle.NO_BORDER;
                    float[] widths3 = new float[] { 8, 48, 8, 12, 8, 15 };
                    itemTable.SetTotalWidth(widths3);


                    int maxRowInPage = 17;
                    int itemRow = 0;
                    int itemCount = 0;
                    foreach (var item in poFormData.poItemList)
                    {
                        itemRow++;
                        itemCount++;

                        PdfPCell itemCellNo = new PdfPCell(new Phrase(itemCount.ToString(), fontSmaller));
                        itemCellNo.HorizontalAlignment = Element.ALIGN_LEFT;
                        itemCellNo.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellNo.Border = 0;
                        itemCellNo.FixedHeight = 18f;
                        itemCellNo.PaddingTop = 5;
                        itemTable.AddCell(itemCellNo);


                        PdfPCell itemCellDesc = new PdfPCell(new Phrase(item.description, fontSmaller));
                        itemCellDesc.HorizontalAlignment = Element.ALIGN_LEFT;
                        itemCellDesc.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellDesc.Border = 0;
                        itemCellDesc.FixedHeight = 18f;
                        itemCellDesc.PaddingTop = 5;
                        itemTable.AddCell(itemCellDesc);

                        PdfPCell itemCellQty = new PdfPCell(new Phrase(item.qty.ToString(), fontSmaller));
                        itemCellQty.HorizontalAlignment = Element.ALIGN_LEFT;
                        itemCellQty.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellQty.Border = 0;
                        itemCellQty.FixedHeight = 18f;
                        itemCellQty.PaddingTop = 5;
                        itemTable.AddCell(itemCellQty);

                        //var itemUnitPrice = String.IsNullOrEmpty(CurrencyToString(item.unitPrice);
                        PdfPCell itemCellUnitPrice = new PdfPCell(new Phrase(CurrencyToString(item.unitPrice), fontSmaller));
                        itemCellUnitPrice.HorizontalAlignment = Element.ALIGN_CENTER;
                        itemCellUnitPrice.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellUnitPrice.Border = 0;
                        itemCellUnitPrice.FixedHeight = 18f;
                        itemCellUnitPrice.PaddingTop = 5;
                        itemTable.AddCell(itemCellUnitPrice);

                        //var itemDiscount = String.IsNullOrEmpty(item.discount) ? "0.00" : Convert.ToDouble(item.discount).ToString("#.00", CultureInfo.InvariantCulture);
                        PdfPCell itemCellUnitDiscount = new PdfPCell(new Phrase(CurrencyToString(item.discount), fontSmaller));
                        itemCellUnitDiscount.HorizontalAlignment = Element.ALIGN_CENTER;
                        itemCellUnitDiscount.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellUnitDiscount.Border = 0;
                        itemCellUnitDiscount.FixedHeight = 18f;
                        itemCellUnitDiscount.PaddingTop = 5;
                        itemTable.AddCell(itemCellUnitDiscount);

                        //var itemAmount = String.IsNullOrEmpty(item.amount) ? "0.00" : Convert.ToDouble(item.amount).ToString("#.00", CultureInfo.InvariantCulture);
                        PdfPCell itemCellUnitAmount = new PdfPCell(new Phrase(CurrencyToString(item.amount), fontSmaller));
                        itemCellUnitAmount.HorizontalAlignment = Element.ALIGN_CENTER;
                        itemCellUnitAmount.VerticalAlignment = Element.ALIGN_TOP;
                        itemCellUnitAmount.Border = 0;
                        itemCellUnitAmount.FixedHeight = 18f;
                        itemCellUnitAmount.PaddingTop = 5;
                        itemTable.AddCell(itemCellUnitAmount);



                        if (itemRow > maxRowInPage)  //If reach max row, continue loop on next page
                        {
                            pdfDoc.Add(itemTable);
                            itemTable = new PdfPTable(7);
                            itemTable.WidthPercentage = 95;//table %100 width
                            itemTable.HorizontalAlignment = Element.ALIGN_LEFT;
                            itemTable.DefaultCell.Border = Rectangle.NO_BORDER;
                            itemTable.SetTotalWidth(widths3);


                            PageNo++;


                            var header = PDFHeader(poFormData, PageNo,writer,pdfDoc);
                            var footer = PDFFooter(poFormData);

                            //Add footer for current page
                            foreach (var footerTbl in footer)
                            {
                                pdfDoc.Add(footerTbl);
                            }

                            pdfDoc.NewPage();
                            itemRow = 0;

                            //Add new header for next page
                            foreach (var headerTbl in header)
                            {
                                pdfDoc.Add(headerTbl);
                            }
                        }


                    }
                    for (int i = 1; i <= maxRowInPage - itemRow; i++)
                    {
                        PdfPCell emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                        emptyCell = new PdfPCell(new Phrase(" "));
                        emptyCell.FixedHeight = 18f;
                        emptyCell.Border = 0;
                        itemTable.AddCell(emptyCell);

                    }

                    pdfDoc.Add(itemTable);


                    //Footer
                    var footerTables = PDFFooter(poFormData);
                    foreach (var table in footerTables)
                    {
                        pdfDoc.Add(table);
                    }

                    //Save File to server
                    var FileDic = "temp";
                    string rootPath = this.Environment.WebRootPath; //wwwroot folder
                    string FilePath = Path.Combine(rootPath + "/" + FileDic);


                    //Create a Folder.
                    if (!Directory.Exists(FilePath))
                        Directory.CreateDirectory(FilePath);
                    var filename = "PO_Form_" + DateTime.Now.ToString() + ".pdf";
                    filename = filename.Replace(" ", "_");
                    filename = filename.Replace(":", "_");
                    filename = filename.Replace(@"/", "_");
                    var filePath = Path.Combine(FilePath, filename);

                    //add appendix
                    var appendixPath = Path.Combine(this.Environment.WebRootPath, "assets", "poappendix.pdf");

                    var reader = new PdfReader(appendixPath);
                    for(int i = 1;i <= reader.NumberOfPages; i++)
                    {
                        pdfDoc.NewPage();
                        var importPage = writer.GetImportedPage(reader, i);
                        writer.DirectContent.AddTemplate(importPage, 0, 0);
                    }

                    pdfDoc.Close();
                    byte[] bytes = stream.ToArray();
                    System.IO.File.WriteAllBytes(filePath, bytes);

                    stream.Close();
                    #endregion

                    return Json(new { status = true, fileName = filename,poSubject = poFormData.poNumber+$@"[{pr.Prname}]", prId=pr.Id });
                }
                return Json(new { status = false, msg = "PO not found" });
            }
            catch (Exception ex)
            {
                return Json(new { status = false, msg = ex.Message });
            }
        }
        public List<PdfPTable> PDFHeader(POFormData poFormData, int pageNo, PdfWriter writer, Document document)
        {
            var tables = new List<PdfPTable>();

            //Font fontSmaller = new Font(FontFactory.GetFont("Arial", 9, Font.NORMAL));
            Font fontDamnSmall = new Font(Font.FontFamily.TIMES_ROMAN, 8, Font.NORMAL);
            Font fontSmaller = new Font(Font.FontFamily.TIMES_ROMAN, 9, Font.NORMAL);
            Font fontXSmall = new Font(Font.FontFamily.TIMES_ROMAN, 10, Font.NORMAL);
            Font fontSmall = new Font(Font.FontFamily.TIMES_ROMAN, 12, Font.NORMAL);
            Font fontXSmallBold = new Font(Font.FontFamily.TIMES_ROMAN, 10, Font.BOLD);
            Font fontSmallBold = new Font(Font.FontFamily.TIMES_ROMAN, 12, Font.BOLD);
            Font fontMedium = new Font(Font.FontFamily.TIMES_ROMAN, 13, Font.NORMAL);
            Font fontMediumBold = new Font(Font.FontFamily.TIMES_ROMAN, 14, Font.BOLD);
            Font fontLargeBold = new Font(Font.FontFamily.TIMES_ROMAN, 20, Font.BOLD);

            #region logo header
            var fileDic = "assets";
			string rootPath = this.Environment.WebRootPath; //wwwroot folder
			string logoURL = Path.Combine(rootPath, fileDic, "poHeader.png");

			iTextSharp.text.Image logo = iTextSharp.text.Image.GetInstance(logoURL);
			logo.ScaleAbsoluteWidth(100);

			PdfPTable logoTable = new PdfPTable(1);
			logoTable.WidthPercentage = 100;//table %100 width
											//logoTable.HorizontalAlignment = Element.ALIGN_CENTER;
			logoTable.SetTotalWidth(new float[] { 100 });
			logoTable.DefaultCell.Border = Rectangle.NO_BORDER;
			logoTable.AddCell(logo);

			tables.Add(logoTable);
			#endregion

			#region PO Info Section
			PdfPTable POTable = new PdfPTable(2);
			POTable.WidthPercentage = 95;//table %100 width
			POTable.HorizontalAlignment = Element.ALIGN_LEFT;
			POTable.DefaultCell.Border = Rectangle.NO_BORDER;
			float[] widths2 = new float[] { 70, 30 };
			POTable.SetTotalWidth(widths2);

			#region fist column PO
			PdfPTable emptyColumn = new PdfPTable(1);
			emptyColumn.WidthPercentage = 45;//table %100 width
			emptyColumn.HorizontalAlignment = Element.ALIGN_LEFT;
			emptyColumn.DefaultCell.Border = Rectangle.NO_BORDER;

			PdfPCell emptyString = new PdfPCell(new Phrase("", fontMediumBold));
			emptyString.Border = 0;
			emptyString.HorizontalAlignment = Element.ALIGN_LEFT;
			emptyString.VerticalAlignment = Element.ALIGN_TOP;
			emptyColumn.AddCell(emptyString);

			POTable.AddCell(emptyColumn);
			#endregion

			#region second column po
			PdfPTable POInfoTable = new PdfPTable(2);
			POInfoTable.WidthPercentage = 40;//table %100 width
			POInfoTable.HorizontalAlignment = Element.ALIGN_LEFT;
			float[] widths1 = new float[] { 20, 40 };
			POInfoTable.SetTotalWidth(widths1);

			PdfPCell poNO = new PdfPCell(new Phrase("No.: ", fontSmallBold));
			poNO.Border = PdfPCell.LEFT_BORDER | PdfPCell.TOP_BORDER;
            poNO.PaddingLeft = 10;
			poNO.PaddingTop = 5;
			poNO.BackgroundColor = new BaseColor(192, 192, 192);
			POInfoTable.AddCell(poNO);

			PdfPCell poNOVal = new PdfPCell(new Phrase(poFormData.poNumber, fontSmall));
			poNOVal.Border = PdfPCell.RIGHT_BORDER | PdfPCell.TOP_BORDER;
			poNOVal.BackgroundColor = new BaseColor(192, 192, 192);
			poNOVal.PaddingRight = 10;
			poNOVal.PaddingTop = 5;
            poNOVal.HorizontalAlignment = Element.ALIGN_LEFT;
            POInfoTable.AddCell(poNOVal);

			PdfPCell date = new PdfPCell(new Phrase("Date: ", fontSmallBold));
			date.Border = PdfPCell.LEFT_BORDER | PdfPCell.BOTTOM_BORDER;
			date.BackgroundColor = new BaseColor(192, 192, 192);
			date.PaddingLeft = 10;
			date.PaddingBottom = 10;
			POInfoTable.AddCell(date);

			PdfPCell dateVal = new PdfPCell(new Phrase(poFormData.poDate.ToString("dd/MM/yyyy"), fontSmall));
			dateVal.Border = PdfPCell.RIGHT_BORDER | PdfPCell.BOTTOM_BORDER;
			dateVal.BackgroundColor = new BaseColor(192, 192, 192);
            dateVal.HorizontalAlignment = Element.ALIGN_LEFT;
            dateVal.PaddingRight = 10;
            dateVal.PaddingBottom = 10;
			POInfoTable.AddCell(dateVal);

			POTable.AddCell(POInfoTable);

			tables.Add(POTable);
            #endregion

            #endregion

            #region po title
            PdfPTable poTitle = new PdfPTable(3);
            poTitle.WidthPercentage = 95;//table %100 width
            poTitle.HorizontalAlignment = Element.ALIGN_LEFT;
            poTitle.DefaultCell.Border = Rectangle.NO_BORDER;
            poTitle.SetTotalWidth(new float[] { 30, 40, 30 });

            PdfPCell lline = new PdfPCell(new Phrase("============", fontLargeBold));
            lline.Border = PdfPCell.NO_BORDER;
            lline.PaddingBottom = 3;
            lline.HorizontalAlignment = Element.ALIGN_LEFT;
            PdfPCell rline = new PdfPCell(new Phrase("============", fontLargeBold));
            rline.Border = PdfPCell.NO_BORDER;
            rline.PaddingBottom = 3;
            rline.HorizontalAlignment = Element.ALIGN_RIGHT;
            poTitle.AddCell(lline);
            PdfPCell title = new PdfPCell(new Phrase("PURCHASE ORDER", fontLargeBold));
            title.HorizontalAlignment = Element.ALIGN_CENTER;
            title.Border = PdfPCell.NO_BORDER;
            title.PaddingBottom = 3;
            poTitle.AddCell(title);
            poTitle.AddCell(rline);

            tables.Add(poTitle);

            #endregion

            #region supplier section
            //Supplier Address
            PdfPTable supplierAddrTableWrapper = new PdfPTable(2);
            supplierAddrTableWrapper.WidthPercentage = 95;//table %100 width
            supplierAddrTableWrapper.HorizontalAlignment = Element.ALIGN_LEFT;
            supplierAddrTableWrapper.DefaultCell.Border = Rectangle.NO_BORDER;
            supplierAddrTableWrapper.SetTotalWidth(new float[] { 50, 50 });

            //Supplier Table
            PdfPTable supplierAddrTable = new PdfPTable(1);
            supplierAddrTable.WidthPercentage = 45;//table %100 width
            supplierAddrTable.HorizontalAlignment = Element.ALIGN_LEFT;
            supplierAddrTable.DefaultCell.Border = Rectangle.NO_BORDER;

            //Supplier Table Cells
            PdfPCell SupplierName = new PdfPCell(new Phrase(poFormData.CustomerName, fontXSmallBold));
            SupplierName.Border = PdfPCell.TOP_BORDER | PdfPCell.RIGHT_BORDER | PdfPCell.LEFT_BORDER;
            SupplierName.HorizontalAlignment = Element.ALIGN_LEFT;
            SupplierName.VerticalAlignment = Element.ALIGN_TOP;
            supplierAddrTable.AddCell(SupplierName);

            PdfPCell SupplierAddr = new PdfPCell(new Phrase(poFormData.customerAddr, fontXSmall));
            SupplierAddr.Border = PdfPCell.RIGHT_BORDER | PdfPCell.LEFT_BORDER;
            SupplierAddr.FixedHeight = 60f;
            SupplierAddr.VerticalAlignment = Element.ALIGN_TOP;
            SupplierAddr.HorizontalAlignment = Element.ALIGN_LEFT;
            supplierAddrTable.AddCell(SupplierAddr);

            PdfPCell SupplierTelFax = new PdfPCell(new Phrase("Tel: " + poFormData.customerTel + "  Fax: " + poFormData.customerFax, fontXSmall));
            SupplierTelFax.Border = PdfPCell.RIGHT_BORDER | PdfPCell.LEFT_BORDER | PdfPCell.BOTTOM_BORDER;
            SupplierTelFax.PaddingBottom = 2;
            SupplierTelFax.HorizontalAlignment = Element.ALIGN_LEFT;
            supplierAddrTable.AddCell(SupplierTelFax);

            //Delivery Table
            PdfPTable deliveryAddrTable = new PdfPTable(1);
            deliveryAddrTable.WidthPercentage = 45;
            deliveryAddrTable.DefaultCell.Border = Rectangle.NO_BORDER;
            deliveryAddrTable.HorizontalAlignment = Element.ALIGN_LEFT;

            //Delivery Table Cells
            PdfPCell deliveryTo = new PdfPCell(new Phrase("Delivery To: ", fontSmallBold));
            deliveryTo.Border = PdfPCell.TOP_BORDER | PdfPCell.LEFT_BORDER | PdfPCell.RIGHT_BORDER;
            deliveryTo.HorizontalAlignment = Element.ALIGN_LEFT;
            deliveryAddrTable.AddCell(deliveryTo);

            PdfPCell deliveryToVal = new PdfPCell(new Phrase(poFormData.deliveryTo, fontSmall));
            //deliveryTo.Border = 0;
            deliveryToVal.HorizontalAlignment = Element.ALIGN_LEFT;
            deliveryToVal.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.LEFT_BORDER | PdfPCell.RIGHT_BORDER;
            deliveryAddrTable.AddCell(deliveryToVal);

            //deliveryAddrTable.AddCell(supplierAddrTable);

            supplierAddrTableWrapper.AddCell(supplierAddrTable);
            supplierAddrTableWrapper.AddCell(deliveryAddrTable);

            tables.Add(supplierAddrTableWrapper);
            #endregion

            #region terms
            PdfPTable termSection = new PdfPTable(3);
            termSection.WidthPercentage = 95;//table %100 width
            termSection.HorizontalAlignment = Element.ALIGN_LEFT;
            termSection.DefaultCell.Border = Rectangle.NO_BORDER;
            termSection.SetTotalWidth(new float[] { 33, 33, 33 });

            var refTitle = new Chunk("Ref No.: ",fontSmallBold);
            var refValue = new Chunk(poFormData.refNo, fontSmall);
            var refCombine = new Phrase();
            refCombine.Add(refTitle);
            refCombine.Add(refValue);
            PdfPCell refNo = new PdfPCell(refCombine);
            refNo.Padding = 3;

            termSection.AddCell(refNo);

            var ptTitle = new Chunk("Payment Terms : ", fontSmallBold);
            var ptValue = new Chunk(poFormData.paymentTerms?.ToString(), fontSmall);
            var ptCombine = new Phrase();
            ptCombine.Add(ptTitle);
            ptCombine.Add(ptValue);
            PdfPCell payment = new PdfPCell(ptCombine);
            payment.Padding = 3;
            termSection.AddCell(payment);

            var rcTitle = new Chunk("Goods Recipent : ", fontSmallBold);
            var rcValue = new Chunk(poFormData.agent, fontSmall);
            var rcCombine = new Phrase();
            rcCombine.Add(rcTitle);
            rcCombine.Add(rcValue);
            PdfPCell recipent = new PdfPCell(rcCombine);
            recipent.Padding = 3;
            termSection.AddCell(recipent);

            tables.Add(termSection);

            #endregion

            //Item Table
            PdfPTable itemTable = new PdfPTable(6);
            itemTable.WidthPercentage = 95;//table %100 width
            itemTable.HorizontalAlignment = Element.ALIGN_LEFT;
            itemTable.DefaultCell.Border = Rectangle.NO_BORDER;
            float[] widths3 = new float[] { 8, 48, 8, 12, 8, 15 };
            itemTable.SetTotalWidth(widths3);


            PdfPCell itemHeaderPoNo = new PdfPCell(new Phrase("No", fontXSmallBold));
            itemHeaderPoNo.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderPoNo.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderPoNo);

            PdfPCell itemHeaderDesc = new PdfPCell(new Phrase("Description", fontXSmallBold));
            itemHeaderDesc.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderDesc.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderDesc);

            PdfPCell itemHeaderQty = new PdfPCell(new Phrase("Qty", fontXSmallBold));
            itemHeaderQty.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderQty.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderQty);

            PdfPCell itemHeaderUnitPrice = new PdfPCell(new Phrase("Unit Price", fontXSmallBold));
            itemHeaderUnitPrice.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderUnitPrice.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderUnitPrice);

            PdfPCell itemHeaderDiscount = new PdfPCell(new Phrase("Disc.", fontXSmallBold));
            itemHeaderDiscount.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderDiscount.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderDiscount);

            PdfPCell itemHeaderAmount = new PdfPCell(new Phrase("Amount(RM)", fontXSmallBold));
            itemHeaderAmount.HorizontalAlignment = Element.ALIGN_LEFT;
            itemHeaderAmount.Border = PdfPCell.BOTTOM_BORDER | PdfPCell.TOP_BORDER;
            itemTable.AddCell(itemHeaderAmount);

            tables.Add(itemTable);

            return tables;
        }
        public List<PdfPTable> PDFFooter(POFormData poFormData)
        {
            var pdfTables = new List<PdfPTable>();

            var TimesNRoman = Font.FontFamily.TIMES_ROMAN;
            //Font fontSmaller = new Font(FontFactory.GetFont("Arial", 9, Font.NORMAL));
            Font fontDamnSmall = new Font(TimesNRoman, 8, Font.NORMAL);
            Font fontSmaller = new Font(TimesNRoman, 10, Font.NORMAL);
            Font fontSmallerBold = new Font(TimesNRoman, 10, Font.BOLD);
            Font fontSmall = new Font(TimesNRoman, 11, Font.NORMAL);
            Font fontSmallBold = new Font(TimesNRoman, 11, Font.BOLD);
            Font fontMedium = new Font(TimesNRoman, 12, Font.NORMAL);
            Font fontMediumBold = new Font(TimesNRoman, 12, Font.BOLD);
            Font fontLargeBold = new Font(TimesNRoman, 15, Font.BOLD);

            //Date of Required Table
            PdfPTable DoRTable = new PdfPTable(2);
            DoRTable.WidthPercentage = 40;//table %100 width
            DoRTable.HorizontalAlignment = Element.ALIGN_LEFT;
            DoRTable.DefaultCell.Border = Rectangle.NO_BORDER;

            //Date of Required Cell
            PdfPCell dateOfReqCell = new PdfPCell(new Phrase("Date of Required: ", fontMediumBold));
            dateOfReqCell.HorizontalAlignment = Element.ALIGN_LEFT;
            dateOfReqCell.VerticalAlignment = Element.ALIGN_TOP;
            dateOfReqCell.FixedHeight = 35f;
            dateOfReqCell.Border = 0;
            DoRTable.AddCell(dateOfReqCell);

            var eta = poFormData.eta.HasValue ? poFormData.eta.Value.ToString("dd/MM/yyyy") : "";
            PdfPCell dateOfReqVal = new PdfPCell(new Phrase(eta, fontMedium));
            dateOfReqVal.HorizontalAlignment = Element.ALIGN_LEFT;
            dateOfReqVal.VerticalAlignment = Element.ALIGN_TOP;
            dateOfReqVal.FixedHeight = 35f;
            dateOfReqVal.Border = 0;
            DoRTable.AddCell(dateOfReqVal);

            pdfTables.Add(DoRTable);


            //Amount Text Table
            PdfPTable amountTable = new PdfPTable(3);
            amountTable.WidthPercentage = 95;
            amountTable.HorizontalAlignment = Element.ALIGN_LEFT;
            amountTable.DefaultCell.Border = Rectangle.TOP_BORDER;
            float[] widths4 = new float[] { 20, 50, 30 };
            amountTable.SetTotalWidth(widths4);


            PdfPCell RMCell = new PdfPCell(new Phrase("RINGGIT M'SIA", fontSmallerBold));
            RMCell.HorizontalAlignment = Element.ALIGN_LEFT;
            RMCell.VerticalAlignment = Element.ALIGN_TOP;
            RMCell.Border = PdfPCell.TOP_BORDER;
            //RMCell.Border = 0;
            amountTable.AddCell(RMCell);

            PdfPCell RMCellVal = new PdfPCell(new Phrase(poFormData.totalPriceText.ToUpper(), fontSmaller));
            RMCellVal.HorizontalAlignment = Element.ALIGN_LEFT;
            RMCellVal.VerticalAlignment = Element.ALIGN_TOP;
            RMCellVal.Border = PdfPCell.TOP_BORDER;
            //RMCellVal.Border = 0;
            amountTable.AddCell(RMCellVal);


            //Grand Total Table
            PdfPTable grandTotalTable = new PdfPTable(2);
            grandTotalTable.WidthPercentage = 90;
            grandTotalTable.HorizontalAlignment = Element.ALIGN_RIGHT;

            PdfPCell subtotal = new PdfPCell(new Phrase("Subtotal", fontSmallerBold));
            subtotal.HorizontalAlignment = Element.ALIGN_LEFT;
            subtotal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(subtotal);

            PdfPCell subtotalVal = new PdfPCell(new Phrase(CurrencyToString(poFormData.subtotal), fontSmaller));
            subtotalVal.HorizontalAlignment = Element.ALIGN_RIGHT;
            subtotalVal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(subtotalVal);

            PdfPCell discount = new PdfPCell(new Phrase("Discount", fontSmallerBold));
            discount.HorizontalAlignment = Element.ALIGN_LEFT;
            discount.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(discount);

            PdfPCell discountlVal = new PdfPCell(new Phrase(CurrencyToString(poFormData.discount), fontSmaller));
            discountlVal.HorizontalAlignment = Element.ALIGN_RIGHT;
            discountlVal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(discountlVal);


            PdfPCell nett = new PdfPCell(new Phrase("Nett", fontSmallerBold));
            nett.HorizontalAlignment = Element.ALIGN_LEFT;
            nett.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(nett);

            PdfPCell nettVal = new PdfPCell(new Phrase(CurrencyToString(poFormData.nett), fontSmaller));
            nettVal.HorizontalAlignment = Element.ALIGN_RIGHT;
            nettVal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(nettVal);

            PdfPCell tax = new PdfPCell(new Phrase("Tax", fontSmallerBold));
            tax.HorizontalAlignment = Element.ALIGN_LEFT;
            tax.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(tax);

            PdfPCell taxVal = new PdfPCell(new Phrase(CurrencyToString(poFormData.tax), fontSmaller));
            taxVal.HorizontalAlignment = Element.ALIGN_RIGHT;
            taxVal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(taxVal);

            PdfPCell grandTotalCell = new PdfPCell(new Phrase("Grand Total", fontSmallerBold));
            grandTotalCell.HorizontalAlignment = Element.ALIGN_LEFT;
            grandTotalCell.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(grandTotalCell);

            PdfPCell grandTotalVal = new PdfPCell(new Phrase(CurrencyToString(poFormData.grandTotal), fontSmaller));
            grandTotalVal.HorizontalAlignment = Element.ALIGN_RIGHT;
            grandTotalVal.VerticalAlignment = Element.ALIGN_TOP;
            grandTotalTable.AddCell(grandTotalVal);


            amountTable.AddCell(grandTotalTable);
            pdfTables.Add(amountTable);




            //Remark Table
            PdfPTable remarkTable1 = new PdfPTable(1);
            remarkTable1.WidthPercentage = 100;
            float[] widths5 = new float[] { 100 };
            remarkTable1.SetTotalWidth(widths5);
            remarkTable1.HorizontalAlignment = Element.ALIGN_CENTER;
            remarkTable1.DefaultCell.Border = Rectangle.NO_BORDER;

            string remarkText1 = "THIS PURCHASE ORDER IS ELECTRONICALLY APPROVED AND GENERATED. NO SIGNATURE NEEDED.";

            PdfPCell remark1 = new PdfPCell(new Phrase(remarkText1, fontDamnSmall));
            remark1.HorizontalAlignment = Element.ALIGN_CENTER;
            remark1.Border = 0;
            remark1.PaddingBottom = 5;
            remarkTable1.AddCell(remark1);

            pdfTables.Add(remarkTable1);

            PdfPTable remarkTable2 = new PdfPTable(1);
            remarkTable2.WidthPercentage = 100;
            remarkTable2.SetTotalWidth(widths5);
            remarkTable2.HorizontalAlignment = Element.ALIGN_CENTER;
            remarkTable2.DefaultCell.Border = Rectangle.NO_BORDER;

            string remarkText2 = "THIS PURCHASE ORDER ALONG WITH PURCHASE ORDER STANDARD TERMS AND CONDITIONS SET OUT IN APPENDIX A SHALL \r\nCONSTITUTE THE PURCHASE CONTRACT BETWEEN THE BUYER AND SUPPLIER.";

            PdfPCell remark2 = new PdfPCell(new Phrase(remarkText2, fontDamnSmall));
            remark2.HorizontalAlignment = Element.ALIGN_CENTER;
            remark2.Border = 0;
            remarkTable1.AddCell(remark2);

            pdfTables.Add(remarkTable2);

            return pdfTables;
        }
       
        public async Task<List<POTrackerViewModel>> LoadPOList(int? id,bool poHistory = false)
        {
            var viewModel = new POTrackerViewModel();
            var list = new List<POTrackerViewModel>();
            var tempDatalist = new List<POTrackerViewModel>();
            Expression<Func<Bom, bool>> bomSelector = null;

            if (poHistory)
            {
                bomSelector = bom => bom.Status == "PO Approved" && bom.PRId.HasValue;
            }
            else
            {
                bomSelector = bom => bom.Status == "PR Approved" && bom.PRId.HasValue;
            }

            var boms = await _context.Boms
                .Where(bomSelector).ToListAsync();

            if (id.HasValue)
            {
                boms = boms.Where(x => x.Id == id).ToList();
            }

            var allPrIds = boms.Select(x => x.PRId.Value).Distinct().ToList();
            var allProjectIds = boms.Select(x => x.ProjectId).ToList();

            if (allPrIds.Any())
            {
                if (!poHistory)
                {
                    foreach (var prId in allPrIds)
                    {
                        CheckAndUpdateETA((int)prId);
                    }
                }

                var users = await _context.EmployeeInfos.ToListAsync();
                var categoryList = await _context.Category.Select(x => new { x.Id, x.CategoryShortName }).ToListAsync();
                var getPr = await _context.Prs.Where(x => allPrIds.Contains(x.Id)).ToListAsync();
                var getProject = await _context.Projects.Include(x => x.CategoryInfo).Where(x => allProjectIds.Contains(x.Id)).ToListAsync();

                foreach (var currentPr in getPr)
                {
                    var project = getProject.Where(x => x.Id == currentPr.ProjectId).FirstOrDefault();
                    var bomItem = _context.Boms
                        .Include(x => x.BomSupportFiles)
                        .ThenInclude(x => x.SupportFile)
                        .FirstOrDefault(x => x.PRId == currentPr.Id);

                    var po = await _context.Pos.Where(x => x.Prid == currentPr.Id).FirstOrDefaultAsync();
                    viewModel = new POTrackerViewModel();

                    viewModel.CreatedDate = currentPr.CreatedDate;
                    viewModel.LastUpdatedDate = currentPr.LastUpdatedDate;
                    var prRoot = JsonConvert.DeserializeObject<PRRoot>(currentPr.DataResponse);
                    viewModel.VendorName = prRoot.data.supplierContainer.FirstOrDefault()?.supplierKey?.name;

                    if (!String.IsNullOrEmpty(currentPr.DataResponse))
                    {
                        PRRoot root = JsonConvert.DeserializeObject<PRRoot>(currentPr.DataResponse);
                        string requestor = root.data.header.Select(x => x.requester).FirstOrDefault();

                        string oldRequestor = root.data.header.Select(x => x.requester).FirstOrDefault();
                        string newRequestor = Common.Departments.ConvertRequestorWithDept(oldRequestor) ?? "";
                        var dept = oldRequestor.Split('(', ')')[1];
                        if (dept == "FSE" || dept == "FIN")
                        {
                            var commonDept = new Common.Departments();
                            commonDept.UpdatePRRequestorDept(currentPr.Id, newRequestor);
                        }

                        var supplierRemarks = prRoot.data.supplierContainer
                                 ?.Where(x => x != null)
                                 .Select(x => new
                                 {
                                     requestorRemark = x.r_remark,
                                     vendorRemark = x.vendor_remark, 
                                     buyerRemark = x.remark
                                 })
                                 .ToList();

                        if (supplierRemarks != null && supplierRemarks.Any())
                        {
                            viewModel.Remark = string.Join("<br>", supplierRemarks
                                .Select(x =>
                                string.Join(", ", new[] { x.vendorRemark, x.requestorRemark, x.buyerRemark }
                                .Where(r => !string.IsNullOrWhiteSpace(r))
                                )
                            ));
                        }
                        else
                        {
                            viewModel.Remark = currentPr.PORemark;
                        }

                        viewModel.Requestor = newRequestor;
                        viewModel.Id = currentPr.Id;
                        viewModel.PrId = currentPr.Id;
                        viewModel.PrName = CommonHelpers.renamePR(currentPr.Prname, currentPr.RevisionNo);
                        viewModel.PoId = po != null ? po.Id : null;
                        viewModel.CustomerPO = project.PoNumber;
                        viewModel.ProjectName = project.Name;
                        viewModel.Status = bomItem?.Status;
                        viewModel.ProjectId = currentPr.ProjectId.Value;
                        viewModel.BomSupportFiles = bomItem.BomSupportFiles.Count() > 0 ? bomItem.BomSupportFiles.Where(x => x.Type == "PO").ToList() : null;
                        viewModel.ProjectCode = project.Code;
                        viewModel.Customer = project.Customer;
                        viewModel.PRApproveDate = currentPr.PRApproveDate;
                       // viewModel.Remark = currentPr.PORemark;
                        viewModel.ProjectCategory = project.CategoryInfo.CategoryShortName;
                        
                        var bomItems = _context.Boms
                            .Include(x => x.BomSupportFiles)
                            .ThenInclude(x => x.SupportFile)
                            .Where(x => x.PRId == currentPr.Id)
                            .ToList();

                        // Skip if no BOMs found
                        if (!bomItems.Any())
                            continue;

                        double totalPrice = 0.0;
                        string currency = "";
                        List<BomSupportFile> allSupportFiles = new();

                        foreach (var bom in bomItems)
                        {
                            var bomJsonItem = prRoot.data.bomGrid?.FirstOrDefault(x => x.itemLineNo == bom.Id);

                            if (bomJsonItem == null) continue;

                            currency = (bomJsonItem.currency ?? "").ToUpper();

                            
                            double itemTotal = bomJsonItem.totalPrice ?? ((bomJsonItem.unitPrice ?? 0.0) * bomJsonItem.totalQuantity);
                            totalPrice += itemTotal;
                        }

                        viewModel.TotalPrice = totalPrice;
                        viewModel.Currency = currency;

                        list.Add(viewModel);
                    }
                }
            } //end bom loop

            return list.OrderByDescending(x => x.PRApproveDate).ToList();
        }


        public async Task<List<POTrackerViewModel>> LoadPOItem(int? id = 0)
        {
            var list = new List<POTrackerViewModel>();
            var tempDatalist = new List<POTrackerViewModel>();
            var boms = await _context.Boms.Where(x => x.PRId == id).ToListAsync();

            if (boms.Any())
            {

                var users = await _context.EmployeeInfos.ToListAsync();
                var categoryList = await _context.Category.Select(x => new { x.Id, x.CategoryShortName }).ToListAsync();
                foreach (var bom in boms)
                {
                    var viewModel = new POTrackerViewModel();
                    var bomId = bom.Id;
                    var rfq = await _context.Rfqs.Where(x => x.Id == bom.RFQId).FirstOrDefaultAsync();
                    var pr = await _context.Prs.Where(x => x.Id == bom.PRId).FirstOrDefaultAsync();
                    var project = await _context.Projects.Where(x => x.Id == bom.ProjectId).FirstOrDefaultAsync();
                    if (pr != null)
                    {
                        var po = await _context.Pos.Where(x => x.Prid == pr.Id).FirstOrDefaultAsync();
                        
                        if (rfq.DataResponse != null)
                        {
                            RFQRoot rfqRoot = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                            viewModel.RfqId = bom.RFQId.Value;
                            viewModel.RfqName = rfq.Rfqname;
                        }

                        viewModel.CreatedDate = pr.CreatedDate;
                        viewModel.LastUpdatedDate = pr.LastUpdatedDate;

                        if (!string.IsNullOrEmpty(pr.DataResponse))
                        {
                            PRRoot root = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                            //string requestor = root.data.header.Select(x => x.requester).FirstOrDefault();
                            string oldRequestor = root.data.header.Select(x => x.requester).FirstOrDefault();
                            string newRequestor = Common.Departments.ConvertRequestorWithDept(oldRequestor) ?? "";
                            var dept = oldRequestor.Split('(', ')')[1];
                            if (dept == "FSE" || dept == "FIN")
                            {
                                var commonDept = new Common.Departments();
                                commonDept.UpdatePRRequestorDept(bom.PRId, newRequestor);
                            }

                            string department = root.data.header.Select(x => x.department).FirstOrDefault();


                            var unitPrice = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.unitPrice).FirstOrDefault() ?? 0;
                            var discount = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.discount).FirstOrDefault() ?? 0;
                            var tax = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.tax).FirstOrDefault() ?? 0;
                            var shippingCost = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.shippingCost).FirstOrDefault() ?? 0;
                            var bankFee = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.bankFee).FirstOrDefault() ?? 0;
                            // var totalPrice = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalPrice).FirstOrDefault() ?? 0;

                            var bomItem = root.data.bomGrid.FirstOrDefault(x => x.itemLineNo == bom.Id);
                            var currency = (bomItem.currency ?? "").ToUpper();
                            var totalPrice = 0.0;

                            if (bomItem != null)
                            {
                               
                                
                                totalPrice = bomItem.totalPrice ?? ((bomItem.unitPrice ?? 0.0) * bomItem.totalQuantity);
                            }
                            else
                            {
                                totalPrice = 0.0;
                            }
                     
                            viewModel.Currency = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.currency).FirstOrDefault().ToUpper();
                            viewModel.UnitPrice = Math.Round(unitPrice, 4);
                            viewModel.Discount = Math.Round(discount, 4);
                            viewModel.Tax = Math.Round(tax, 4);
                            viewModel.ShippingCost = Math.Round(shippingCost, 4);
                            viewModel.BankFee = Math.Round(bankFee, 4);
                            viewModel.TotalPrice = Math.Round(totalPrice, 4);
                            viewModel.Id = pr.Id;
                            viewModel.PrId = pr.Id;
                            viewModel.PrName = CommonHelpers.renamePR(pr.Prname, pr.RevisionNo);
                            viewModel.PoId = po != null ? po.Id : null;
                            viewModel.IsVoid = bomItem?.isVoid ?? false;
                            viewModel.Requestor = newRequestor;
                            viewModel.Department = department;
                            viewModel.CustomerPO = root.data.header.Select(x => x.poNumber).FirstOrDefault();
                            viewModel.ProjectName = root.data.header.Select(x => x.projectName).FirstOrDefault();
                            viewModel.Quantity = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.totalQuantity).FirstOrDefault();
                            viewModel.Status = bom.Status;
                            viewModel.ProjectId = bom.ProjectId;
                            viewModel.BomId = bom.Id;
                            viewModel.ProjectCode = project.Code;
                            viewModel.Customer = project.Customer;
                            viewModel.ReceivedDate = bom.ReceivedDate;
                            viewModel.ReceivedQty = bom.ReceivedQuantity ?? 0;
                            viewModel.BalanceToReceiveQty = bom.BalanceToReceiveQty ?? 0;
                            viewModel.PRApproveDate = pr.PRApproveDate;
                            viewModel.LeadTime = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.leadTime).FirstOrDefault();
                            viewModel.EstDeliveryDate = root.data.bomGrid.Where(x => x.itemLineNo == bom.Id).Select(x => x.eta).FirstOrDefault();

                            var categoryId = root.data.header.Select(x => x.projectCategory).FirstOrDefault();
                            int cId = 0;
                            if (!String.IsNullOrEmpty(categoryId))
                            {
                                bool isNumericCatId = int.TryParse(categoryId, out cId);
                                if (isNumericCatId)
                                {
                                    viewModel.ProjectCategory = categoryList.Where(x => x.Id == cId).Select(x => x.CategoryShortName).FirstOrDefault();
                                }
                                else
                                {
                                    viewModel.ProjectCategory = root.data.header.Select(x => x.projectCategory).FirstOrDefault();
                                }
                            }

                        }

                        if (id.HasValue) //Is From Edit 
                        {
                            list.Add(viewModel);
                        }
                        else
                        {
                            tempDatalist.Add(viewModel);
                        }

                    }
                }
            } //end bom loop

            //var grandTotal = 0.0;
            //if (list.Any())
            //{
            //    grandTotal = list.Sum(x => x.TotalPrice ?? 0);
            //    ViewBag.GrandTotal = Math.Round(grandTotal, 2);
            //    Console.WriteLine("grandtotal is ", grandTotal);
            //} else
            //{
            //    grandTotal = tempDatalist.Sum(x => x.TotalPrice ?? 0);
            //    ViewBag.GrandTotal = Math.Round(grandTotal, 2);
            //    Console.WriteLine("temptgrandtotal is ", tempDatalist);
            //}

            if (!id.HasValue)
            {
                var allPr = tempDatalist.Select(x => x.Id).Distinct().ToList();
                foreach (var pr in allPr)
                {
                    var po = tempDatalist.Where(x => x.Id == pr).FirstOrDefault();
                    var sumPOQty = tempDatalist.Where(x => x.Id == pr).Select(x => x.Quantity).Sum();
                    var sumUnitPrice = tempDatalist.Where(x => x.Id == pr).Select(x => x.UnitPrice).Sum();
                    var sumTotalPrice = tempDatalist.Where(x => x.Id == pr).Select(x => x.TotalPrice).Sum();
                    var sumETA = String.Join(",", tempDatalist.Where(x => x.Id == pr && x.EstDeliveryDate != null).Select(x => x.EstDeliveryDate?.ToString("yyyy-MM-dd")).ToList());

                    po.Quantity = sumPOQty;
                    po.UnitPrice = sumUnitPrice;
                    po.TotalPrice = sumTotalPrice;
                    po.ETA = sumETA;

                    list.Add(po);
                }
            }

            return list.OrderBy(x =>
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
                    .ThenByDescending(x => x.PRApproveDate).ToList();
        }

        [HttpPost]
        public async Task<ActionResult> attachFile(int prId, List<IFormFile> files, List<string> types)
        {
            var boms = _context.Boms.Where(x => x.PRId == prId).ToList();
            var pr = _context.Prs.FirstOrDefault(x => x.Id == prId);
            var checkPO = _context.Pos.FirstOrDefault(x => x.Prid == pr.Id);
            bool isRepeat = false;
            var upFiles = new List<SupportFile>();
            if (files.Count > 0)
            {
                foreach (var bom in boms)
                {
                    foreach (var t in types)
                    {
                        var exists = _context.BomSupportFile.FirstOrDefault(x => x.BomId == bom.Id && x.Type == t);
                        if (exists != null)
                        {
                            _context.BomSupportFile.Remove(exists);
                        }
                    }

                    await _context.SaveChangesAsync();

                    if (!isRepeat) //bom in same PR (Merge PR) PO filename must be same
                    {
                        upFiles = await _fileWriter.UploadDocument(files);
                        isRepeat = true;
                    }

                    for (int i = 0; i < types.Count(); i++)
                    {
                        upFiles[i].CreatedAt = DateTime.Now;
                        bom.BomSupportFiles.Add(new BomSupportFile
                        {
                            SupportFile = upFiles[i],
                            Type = types[i]
                        });
                    }

                    bom.Status = "PO Approved";

                    await _context.SaveChangesAsync();
                }

                _context.Boms.UpdateRange(boms);
                await _context.SaveChangesAsync();

                if(checkPO == null)
                {
                    var createdPO = GeneratePO(pr);
                    if (createdPO != null)
                    {
                        _context.PurchaseHistory.Add(new PurchaseHistory
                        {
                            POId = createdPO.Id,
                            PRId = pr.Id
                        });
                        _context.SaveChanges();
                    }
                }
            }

            return NoContent();
        }

        public async Task<IActionResult> RevisePO(int id,string remark ="")
        {
            var pr = _context.Prs.FirstOrDefault(x => x.Id == id);
            var po = _context.Pos.FirstOrDefault(x => x.Prid == id);

            if(po != null)
            {
                _context.Pos.Remove(po);
                _context.SaveChanges();
            }

            if (pr is null) return NoContent();

            var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
            prRoot.data.rejectCommentBox = "";
            if (!string.IsNullOrEmpty(remark))
            {
                prRoot.data.rejectCommentBox += "Purchaser request to revise the PR at " + DateTime.Now.ToString("g") + " because of " + remark + "\n";
            }
            pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(prRoot); 
            pr.Status = "PO Revise";
            pr.PriceBeforeRevise = (decimal)prRoot.data.netPrice;
            
            var boms = await _context.Boms.Where(x => x.PRId == id).ToListAsync();
            var resetPRApproved = await _context.PRApprover.Where(x => x.PRId == pr.Id).ToListAsync();
            _context.PRApprover.RemoveRange(resetPRApproved);
            boms.ForEach(x => x.Status = "PO Revise");

            await _context.SaveChangesAsync();

            var requestorEmail = _context.EmployeeInfos.FirstOrDefault(x => x.Id == pr.RequestorId && !string.IsNullOrEmpty(x.Email));
            if (requestorEmail != null)
            {
                var (msg, html) = CommonHelpers.MailTemplateGenerator("PR", BOMStatus.Revise, Url.Action("Edit","PurchaseRequisition", new { id = pr.Id }), prRoot.data.rejectCommentBox);
                var title = @$"Your PR {pr.Prname} is request to revise";

                _emailService.Send(requestorEmail.Email, title, msg);
                var pics = new List<int>{ requestorEmail.Id};
                await _auditTrailService.InsertLogAsync(
                  Common.LogLevel.Warning, title,
                  pics:pics,
                  fullMessage: msg,
                  tableChange: nameof(Bom),
                  empId: int.Parse(HttpContext.GetUserId()),
                  ProjectId: pr.ProjectId.Value
                  );
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<JsonResult> GetPO(int prId)
        {
            try
            {
                if (prId == 0)
                {
                    return Json(new { msg = "Invalid PR ID", status = false });
                }

                var getPO = await LoadPOItem(prId);
                foreach (var x in getPO)
                {
                    x.BomSupportFiles = null; //will cause json parser error if no removed
                }

                double grandTotal = getPO.Sum(x => x.TotalPrice ?? 0); // if nullable, use ?? 0
                string currency = getPO.FirstOrDefault()?.Currency ?? "";

                var pr = _context.Prs.FirstOrDefault(x => x.Id == prId);

                string vendorName = "";
                if (!string.IsNullOrEmpty(pr.DataResponse))
                {
                    var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                    vendorName = prRoot?.data?.supplierContainer?.FirstOrDefault()?.supplierKey?.name ?? "";
                }

                return Json(new
                {
                    msg = "OK",
                    status = true,
                    result = getPO,
                    remark = pr.PORemark,
                    prNumber = CommonHelpers.renamePR(pr.Prname, pr.RevisionNo),
                    prId = prId,
                    vendorName = vendorName,
                    grandTotal = grandTotal.ToString("N4"), // formatted output
                    currency = currency
                });
            }
            catch (Exception ex)
            {
                return Json(new { msg = ex.Message, status = false });
            }
        }

        [HttpPost]
        public async Task<ActionResult> EditPO(List<POTrackerViewModel> data,string remark,int prId)
        {
            try
            {
                var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
                if (data.Any())
                {
                    foreach (var item in data)
                    {
                        var bomId = item.BomId;
                        if (bomId == 0)
                        {
                            return NotFound();
                        }
                        var rfq = _context.Boms.Include(x => x.RFQ).Where(x => x.Id == bomId).Select(x => x.RFQ).FirstOrDefault();


                        if (!string.IsNullOrEmpty(pr.DataResponse))
                        {

                            var pritemResponse = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                            var header = pritemResponse.data.header.FirstOrDefault();
                            var bomGrid = pritemResponse.data.bomGrid.Where(x => x.itemLineNo == bomId).FirstOrDefault();
                            bomGrid.totalQuantity = item.Quantity;
                            bomGrid.unitPrice = item.UnitPrice;
                            bomGrid.discount = item.Discount;
                            bomGrid.tax = item.Tax;
                            bomGrid.shippingCost = item.ShippingCost;
                            bomGrid.bankFee = item.BankFee;
                            if (bomGrid != null)
                            {
                                var itemCurrency = bomGrid.currency ?? "RM";

                                    double unitPrice = Math.Round(item.UnitPrice, 4);
                                    int qty = item.Quantity;

                                    double totalPrice = unitPrice * qty;

                                    // get conversion rate if currency is not RM
                                    double rate = 1;
                                    if (!itemCurrency.Equals("RM", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var conversion = pritemResponse.data.currencyGrid.FirstOrDefault(c =>
                                            c.cr_currency.Equals(itemCurrency, StringComparison.OrdinalIgnoreCase));

                                        if (conversion != null && conversion.rate > 0)
                                            rate = conversion.rate ?? 1;
                                    }

                                bomGrid.totalPrice = totalPrice * rate;
                            }

                            //bomGrid.totalPrice = item.TotalPrice;
                            bomGrid.leadTime = item.LeadTime;
                            bomGrid.eta = item.EstDeliveryDate;

                            pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(pritemResponse);
                            _context.Prs.Update(pr);
                            _context.SaveChanges();
                        }

                        if (rfq.DataResponse.Any())
                        {
                            var rfqitemResponse = JsonConvert.DeserializeObject<RFQRoot>(rfq.DataResponse);
                            var header = rfqitemResponse.data.header.FirstOrDefault();
                            var bomGrid = rfqitemResponse.data.bomGrid.Where(x => x.itemLineNo == bomId).FirstOrDefault();

                            bomGrid.totalQuantity = item.Quantity;
                            bomGrid.unitPrice = item.UnitPrice;
                            bomGrid.discount = item.Discount;
                            bomGrid.tax = item.Tax;
                            bomGrid.shippingCost = item.ShippingCost;
                            bomGrid.bankFee = item.BankFee;
                            bomGrid.totalPrice = item.TotalPrice;
                            bomGrid.leadTime = item.LeadTime;
                            bomGrid.eta = item.EstDeliveryDate;

                            rfq.DataResponse = System.Text.Json.JsonSerializer.Serialize(rfqitemResponse);
                            _context.Rfqs.Update(rfq);
                            _context.SaveChanges();
                        }
                        _context.SaveChanges();
                    }
                }

                pr.PORemark = remark;

                _context.Prs.Update(pr);
                _context.SaveChanges();

                return Json(new { msg = "PO Update successful.", status = true });
            }
            catch (Exception ex)
            {
                return NotFound();
            }
        }

        [HttpPost]
        public async Task<ActionResult> VoidBOMItem(int bomId, int prId, bool isVoid)
        {
            try
            {
                var pr = _context.Prs.Where(x => x.Id == prId).FirstOrDefault();
                if (pr == null)
                {
                    return Json(new { msg = "PR not found", status = false });
                }

                if (!string.IsNullOrEmpty(pr.DataResponse))
                {
                    var pritemResponse = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                    var bomGrid = pritemResponse.data.bomGrid.Where(x => x.itemLineNo == bomId).FirstOrDefault();
                    
                    if (bomGrid != null)
                    {
                        bomGrid.isVoid = isVoid;
                        pr.DataResponse = System.Text.Json.JsonSerializer.Serialize(pritemResponse);
                        _context.Prs.Update(pr);
                        await _context.SaveChangesAsync();

                        var auditTrail = await _auditTrailService.InsertLogAsync(
                            Common.LogLevel.Information, 
                            $"BOM item {bomId} has been {(isVoid ? "voided" : "unvoided")} in PR {pr.Prname}",
                            fullMessage: $"{(isVoid ? "Void" : "Unvoid")} BOM item from PO Tracker",
                            tableChange: nameof(Pr),
                            empId: int.Parse(HttpContext.GetUserId()),
                            ProjectId: pr.ProjectId ?? 0
                        );

                        return Json(new { msg = $"Item {(isVoid ? "voided" : "unvoided")} successfully.", status = true });
                    }
                }

                return Json(new { msg = "BOM item not found", status = false });
            }
            catch (Exception ex)
            {
                return Json(new { msg = ex.Message, status = false });
            }
        }

        public async Task<IActionResult> RejectPO(int prId, string remark)
        {
            try
            {
                if (prId <= 0)
                {
                    return RedirectToAction("Error404", "Home", new { msg = "Invalid PR" });
                }

                var boms = await _context.Boms.Where(x => x.PRId == prId)
                    .Include(x => x.BomSupportFiles)
                    .ThenInclude(x => x.SupportFile)
                    .ToListAsync();

                var po = _context.Pos.Where(x => x.Prid == prId).FirstOrDefault();

                if(po != null)
                {
                    _context.Pos.Remove(po);
                    _context.SaveChanges();
                }

                var pr = await _context.Prs.Where(x => x.Id == prId).FirstOrDefaultAsync();
                var prApprover = await _context.PRApprover.Where(x => x.PRId == prId).ToListAsync();

                foreach (var bom in boms)
                {
                    bom.Status = "PO Reject";
                    bom.IsPOUpdated = false;

                    if(bom.BomSupportFiles.Count > 0 && bom.BomSupportFiles.Any(x => x.Type == "PO"))
                    {
                        var poFile = bom.BomSupportFiles.FirstOrDefault(x => x.Type == "PO");

                        if (poFile != null)
                        {
                            var filePath = Path.Combine(Environment.WebRootPath, "documents", poFile.SupportFile.FileName);
                            if (System.IO.File.Exists(filePath))
                            {
                                System.IO.File.Delete(filePath);
                            }

                            bom.BomSupportFiles.Remove(poFile);
                        }                        
                    }

                    _context.Boms.Update(bom);
                }

                _context.RemoveRange(prApprover);
                _context.Remove(pr);

                var project = _context.Projects.FirstOrDefault(x => x.Id == boms.FirstOrDefault().ProjectId);
                var (msg, html) = CommonHelpers.MailTemplateGenerator("PO",  BOMStatus.Rejected , Url.Action("Edit", new { id = pr.Id }),remark);
                var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').Select(int.Parse).ToList() : new List<int>();
                var emailList = pics.Count() == 0
                    ? null
                    : _context.EmployeeInfos
                    .Where(x => !string.IsNullOrEmpty(x.Email)
                    && (pics.Contains(x.Id)
                    || x.UserType == "Requestor") && x.IsNotificationOn)
                    .Select(x => x.Email)
                    .ToList();

                var title = $@"PO with name {CommonHelpers.renamePR(pr.Prname, pr.RevisionNo)} is been reject";

                await _emailService.Send(emailList, title, html);
                var pics2 = new List<int> { pr.RequestorId };
                await _auditTrailService.InsertLogAsync(
                    Common.LogLevel.Fatal, title,
                    pics:pics2,
                    fullMessage: msg,
                    tableChange: nameof(Pr),
                    empId: int.Parse(HttpContext.GetUserId()),
                    ProjectId: pr.Project.Id
                    );

                await _context.SaveChangesAsync();

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                return RedirectToAction("Error404", "Home", new { msg = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportToExcel(DateTime? fromDate, DateTime? toDate,bool isHistory = false)
        {
            var entity = await LoadPOList(null, isHistory);
            var sc = new ServerContext(_server);
            var serverDomain = sc.GetServerAddress();
            if (fromDate.HasValue)
            {
                entity = entity.Where(x => x.PRApproveDate >= fromDate).ToList();
            }
            if (toDate.HasValue)
            {
                entity = entity.Where(x => x.PRApproveDate <= Convert.ToDateTime(toDate).AddHours(23).AddMinutes(59).AddSeconds(59)).ToList();
            }

            if (entity == null)
                return NoContent();

            var currentRow = 1;
            using (var workBook = new XLWorkbook())
            {
                var worksheet = workBook.Worksheets.Add("PO");

                foreach (var item in entity)
                {

                    //header zone
                    var dateZ = typeof(POHeaderZone).GetProperties();
                    for (int i = 1; i <= dateZ.Count(); i++)
                    {
                        worksheet.Cell(currentRow, i).Value = dateZ[i - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, i).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                        worksheet.Cell(currentRow, i).Style.Font.FontColor = XLColor.Black;

                        if (dateZ[i - 1].Name == "Status")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.Status;
                            worksheet.Cell(currentRow, i).Style.Font.Bold = true;
                        }
                        else if (dateZ[i - 1].Name == "PoName")
                        {
                            if (item.BomSupportFiles != null && item.BomSupportFiles.Where(x => x.Type == "PO").Any())
                            {
                                worksheet.Cell(currentRow + 1, i).Value = item.BomSupportFiles.FirstOrDefault(x => x.Type == "PO").SupportFile.FileName;

                                if (item.BomSupportFiles.FirstOrDefault(x => x.Type == "PO").SupportFileId != 0)
                                {
                                    worksheet.Cell(currentRow + 1, i).Hyperlink = new XLHyperlink(serverDomain + "/BOM/ShowPDF?supportFileId=" + item.BomSupportFiles.FirstOrDefault(x => x.Type == "PO").SupportFileId);
                                }
                            }
                        }
                        else if (dateZ[i - 1].Name == "PrApproveDate")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.PRApproveDate;
                        }
                        else if (dateZ[i - 1].Name == "ProjectCode")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.ProjectCode;
                        }
                        else if (dateZ[i - 1].Name == "ProjectPo")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.CustomerPO;
                        }
                        else if (dateZ[i - 1].Name == "Customer")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.Customer;
                        }
                        else if (dateZ[i - 1].Name == "ProjectName")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.ProjectName;
                        }
                        else if (dateZ[i - 1].Name == "Category")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.ProjectCategory;
                        }
                        else if (dateZ[i - 1].Name == "Rfq")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.RfqName;
                            worksheet.Cell(currentRow + 1, i).Hyperlink = new XLHyperlink(serverDomain + "RFQ/Edit/" + item.RfqId);
                        }
                        else if (dateZ[i - 1].Name == "Pr")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = CommonHelpers.renamePR(item.PrName, 0);
                            worksheet.Cell(currentRow + 1, i).Hyperlink = new XLHyperlink(serverDomain + "PurchaseRequisition/Edit/" + item.Id);
                        }
                        else if (dateZ[i - 1].Name == "RequestBy")
                        {
                            worksheet.Cell(currentRow + 1, i).Value = item.Requestor;
                        }
                    }//end header loop

                    currentRow += 2;
                    var bomsInPr = await LoadPOItem(item.Id);

                    var poContentCell = typeof(POContentZone).GetProperties();
                    for (int g = 1; g <= poContentCell.Count(); g++)
                    {
                        worksheet.Cell(currentRow, g).Value = poContentCell[g - 1].Name.FirstCharToUpper().ConvertEnum();
                        worksheet.Cell(currentRow, g).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, g).Style.Fill.BackgroundColor = XLColor.Gainsboro;
                    }


                    foreach (var bom in bomsInPr)
                    {
                        var currency = bom.Currency;
                        for (int g = 1; g <= poContentCell.Count(); g++)
                        {
                            if (poContentCell[g - 1].Name == "ItemLineNo")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = bom.BomId;
                            }
                            else if (poContentCell[g - 1].Name == "PoLine")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = bom.POName;
                            }
                            else if (poContentCell[g - 1].Name == "PoQty")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = bom.Quantity;
                            }
                            else if (poContentCell[g - 1].Name == "UnitPrice")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.UnitPrice;
                            }
                            else if (poContentCell[g - 1].Name == "Discount")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.Discount;
                            }
                            else if (poContentCell[g - 1].Name == "Tax")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.Tax;
                            }
                            else if (poContentCell[g - 1].Name == "ShippingCost")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.ShippingCost;
                            }
                            else if (poContentCell[g - 1].Name == "BankFee")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.BankFee;
                            }
                            else if (poContentCell[g - 1].Name == "TotalPrice")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = currency + " " + bom.TotalPrice;
                            }
                            else if (poContentCell[g - 1].Name == "Eta")
                            {
                                worksheet.Cell(currentRow + 1, g).Value = bom.EstDeliveryDate;
                            }
                        }
                        currentRow++;
                    }
                    currentRow += 3;
                }//end bom loop

                using (var stream = new MemoryStream())
                {
                    workBook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "PO_Export_" + DateTime.Now.ToString() + ".xlsx");
                }
            }
        }


        public void CheckAndUpdateETA(int PrId)
        {
            try
            {
                var PR = _context.Prs.Where(x => x.Id == PrId).FirstOrDefault();
                if (PR != null && !string.IsNullOrEmpty(PR.DataResponse))
                {
                    var root = JsonConvert.DeserializeObject<PRRoot>(PR.DataResponse);
                    var list = _context.Boms.Where(x => x.PRId == PrId && !x.EstDeliveryDate.HasValue).ToList();

                    list.ForEach(x =>
                    {
                        if (root.data.bomGrid.Any(b => b.itemLineNo == x.Id))
                        {
                            x.EstDeliveryDate = root.data.bomGrid.FirstOrDefault(k => k.itemLineNo == x.Id).eta;
                        }
                    });

                    _context.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public string CurrencyToString(double? val)
        {
            string result = "";
            if (!val.HasValue || val == 0)
            {
                result = "0.00";
            }
            else
            {
                result = val?.ToString("#.00", CultureInfo.InvariantCulture);
            }
            return result;
        }

        [HttpPost]
        public async Task<IActionResult> SendPO(string fileName,string subject,string mails,int prId,List<IFormFile> pos)
        {
            var boms = _context.Boms.Where(x => x.PRId == prId).ToList();
            var po = _context.Pos.FirstOrDefault(x => x.Prid == prId);
            var emailContent = _context.EmailTemplateConfig
                .FirstOrDefault(x => x.ConfigKey == EmailTemplateConfiguration.POEmailContent);

            string targetDirectoryFullPath = Path.Combine(Environment.WebRootPath, "documents");
            var newName = $@"{subject}_{Guid.NewGuid().ToString()}.pdf";
            string newFilePath = Path.Combine(targetDirectoryFullPath, newName);

            if (pos.Count > 0)
            {
                newName = _fileWriter.CheckAndGetFileName(pos[0].FileName);
                newFilePath = Path.Combine(targetDirectoryFullPath, newName);
                MergeAppendix(pos, newFilePath);
            }
            else
            {
                string currentFullPath = Path.Combine(Environment.WebRootPath, "temp", fileName);
                System.IO.File.Move(currentFullPath, newFilePath);
            }

            var upFile = new SupportFile
            {
                FileName = newName,
                ContentType = "application/pdf",
                CreatedAt = DateTime.Now,
                FileNameOnDisk = newName,
            };

            _context.SupportFile.Add(upFile);

            _context.SaveChanges();

            foreach (var bom in boms)
            {
                var exists = _context.BomSupportFile.FirstOrDefault(x => x.BomId == bom.Id && x.Type == "PO");
                if (exists != null)
                {
                    _context.BomSupportFile.Remove(exists);
                    await _context.SaveChangesAsync();
                }

                bom.BomSupportFiles.Add(new BomSupportFile
                {
                    SupportFile = upFile,
                    Type = "PO"
                });
                bom.Status = "PO Approved";

                await _context.SaveChangesAsync();
            }
            _context.Boms.UpdateRange(boms);
            await _context.SaveChangesAsync();

            string formattedString = emailContent.ConfigValue.Replace("{poNo}", po.Poname);

            var recipients = mails.Split(",");
            await _emailService.Send(recipients.ToList(), subject, formattedString, newFilePath);

            return RedirectToAction("Index");
        }

        private string MergeAppendix(List<IFormFile> pos,string outputPath)
        {
            if (pos.Count > 0)
            {
                Document pdfDoc = new Document(PageSize.A4, 10f, 7f, 5f, 7f);
                var output = new FileStream(outputPath, FileMode.Create);
                PdfCopy pdfCopyProvider = new PdfCopy(pdfDoc, output);

                pdfDoc.Open();

                using (var uploadedPdfStream = pos[0].OpenReadStream())
                using (var uploadedPdfReader = new PdfReader(uploadedPdfStream))
                {
                    for (int page = 1; page <= uploadedPdfReader.NumberOfPages; page++)
                    {
                        PdfImportedPage importedPage = pdfCopyProvider.GetImportedPage(uploadedPdfReader, page);
                        pdfCopyProvider.AddPage(importedPage);
                    }
                }

                var appendixPath = Path.Combine(Environment.WebRootPath, "assets", "poappendix.pdf");
                using (var existingPdfReader = new PdfReader(appendixPath))
                {
                    for (int page = 1; page <= existingPdfReader.NumberOfPages; page++)
                    {
                        PdfImportedPage importedPage = pdfCopyProvider.GetImportedPage(existingPdfReader, page);
                        pdfCopyProvider.AddPage(importedPage);
                    }
                }

                pdfCopyProvider?.Close();
                pdfDoc?.Close();

                return outputPath;
            }

            return string.Empty;
        }

        public IActionResult ShowPOFormPDF(string filename,string subject,int prId)
        {
            var emailContent = _context.EmailTemplateConfig.FirstOrDefault(x => x.ConfigKey == EmailTemplateConfiguration.POEmailContent);

            var (bytes, fileType, fileName) = _fileWriter.DownloadFiles("temp", filename);
            var base64String = Convert.ToBase64String(bytes);
            var showPdfModel = new ShowPDFModel($@"data:application/pdf;base64,{base64String}", fileName);
            showPdfModel.Description = subject;
            showPdfModel.Description2 = emailContent.ConfigValue;
            showPdfModel.Description3 = prId.ToString();
            return View(showPdfModel);
        }

        public class SendPODto
        {
            public string FileName { get; set; }
            public string Subject { get; set; }
            public int PrId { get; set; }
            public List<string> Mails { get; set; }
            public List<IFormFile>? Pos  { get; set; }
        }

        public class RFQPRGroup
        {
            public int RFQId { get; set; }
            public int PRId { get; set; }
        }

    }
}

