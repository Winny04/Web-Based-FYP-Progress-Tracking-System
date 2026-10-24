using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FYPTracker.ViewModels;
using NPOI.XWPF.UserModel;

namespace FYPTracker.Utils
{
    public static class CoverPageDocxHelper
    {
        /// <summary>
        /// Generates an official Thesis Cover Page DOCX document.
        /// If a custom DOCX template is provided, placeholders are populated.
        /// Otherwise, an official A4 document matching Coventry/INTI formatting is generated.
        /// </summary>
        public static byte[] GenerateCoverPageDocx(
            MeetingRecordTemplateVM? template,
            string? thesisTitle,
            string? studentName,
            string? coventryId,
            string? supervisorName,
            string? programmeName,
            string? documentDate,
            string? customDocxTemplatePath = null)
        {
            // If custom uploaded template exists, populate placeholders in it
            if (!string.IsNullOrEmpty(customDocxTemplatePath) && File.Exists(customDocxTemplatePath))
            {
                using var fs = new FileStream(customDocxTemplatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var doc = new XWPFDocument(fs);
                PopulatePlaceholders(doc, thesisTitle, studentName, coventryId, supervisorName, programmeName, documentDate);
                using var outMs = new MemoryStream();
                doc.Write(outMs);
                return outMs.ToArray();
            }

            // Build official A4 document matching Coventry / INTI guidelines
            using var ms = new MemoryStream();
            var docx = new XWPFDocument();

            // 1. Explicitly configure A4 Portrait and standard margins in sectPr
            var body = docx.Document.body;
            var sectPr = body.sectPr ?? (body.sectPr = new NPOI.OpenXmlFormats.Wordprocessing.CT_SectPr());
            var pgSz = sectPr.pgSz ?? (sectPr.pgSz = new NPOI.OpenXmlFormats.Wordprocessing.CT_PageSz());
            pgSz.w = (ulong)11906; // 210mm (A4 width in twips)
            pgSz.h = (ulong)16838; // 297mm (A4 height in twips)
            pgSz.orient = NPOI.OpenXmlFormats.Wordprocessing.ST_PageOrientation.portrait;

            var pgMar = sectPr.pgMar ?? (sectPr.pgMar = new NPOI.OpenXmlFormats.Wordprocessing.CT_PageMar());
            pgMar.top = "1152"; // 0.8 inch (1152 twips)
            pgMar.bottom = "1152"; // 0.8 inch (1152 twips)
            pgMar.left = (ulong)1440; // 1.0 inch
            pgMar.right = (ulong)1440; // 1.0 inch

            XWPFParagraph AddPara(string text, bool bold, int fontSize, bool center, int spaceBeforeTwips = 0, int spaceAfterTwips = 0, string? color = null)
            {
                var p = docx.CreateParagraph();
                if (center) p.Alignment = ParagraphAlignment.CENTER;
                p.SpacingBefore = spaceBeforeTwips;
                p.SpacingAfter = spaceAfterTwips;
                p.SpacingBetween = 1.0;

                var r = p.CreateRun();
                r.SetText(text);
                r.IsBold = bold;
                r.FontSize = fontSize;
                r.FontFamily = "Times New Roman";
                if (!string.IsNullOrEmpty(color)) r.SetColor(color);
                return p;
            }

            // 1. Title Block (Upper portion, centered, bold 20pt)
            string displayTitle = !string.IsNullOrWhiteSpace(thesisTitle)
                ? thesisTitle.Trim().ToUpperInvariant()
                : "[THESIS TITLE]";

            // Adaptive spacing based on title and metadata length to guarantee exactly ONE page
            int titleLength = displayTitle.Length;
            int spaceBeforeTitle = titleLength > 90 ? 600 : (titleLength > 45 ? 800 : 1100);
            int spaceBeforeInfo = titleLength > 90 ? 1200 : (titleLength > 45 ? 1600 : 2100);
            int spaceBeforeBottom = titleLength > 90 ? 1200 : (titleLength > 45 ? 1600 : 2100);
            int itemGap = titleLength > 90 ? 120 : 160;

            AddPara(displayTitle, true, 20, true, spaceBeforeTwips: spaceBeforeTitle, spaceAfterTwips: 0, color: "000000");

            // 2. Student & Project Information Block (Middle portion)
            string nameLabel = "Name";
            string idLabel = "Coventry Student ID";
            string supLabel = "Supervisor Name";
            string progLabel = "Program Name";

            if (template?.Fields != null)
            {
                foreach (var f in template.Fields)
                {
                    string norm = (f.Key ?? "").Replace(" ", "").ToLowerInvariant();
                    if (norm.Contains("name") && !norm.Contains("supervisor") && !norm.Contains("program"))
                        nameLabel = f.Label ?? nameLabel;
                    else if (norm.Contains("coventry") || norm.Contains("id"))
                        idLabel = f.Label ?? idLabel;
                    else if (norm.Contains("supervisor"))
                        supLabel = f.Label ?? supLabel;
                    else if (norm.Contains("program"))
                        progLabel = f.Label ?? progLabel;
                }
            }

            // Info rows (clean paragraph spacing, zero blank paragraphs)
            AddPara($"{nameLabel}: {studentName ?? ""}", false, 14, true, spaceBeforeTwips: spaceBeforeInfo, spaceAfterTwips: itemGap, color: "000000");
            AddPara($"{idLabel}: {coventryId ?? ""}", false, 14, true, spaceBeforeTwips: 0, spaceAfterTwips: itemGap, color: "000000");
            AddPara($"{supLabel}: {supervisorName ?? ""}", false, 14, true, spaceBeforeTwips: 0, spaceAfterTwips: itemGap, color: "000000");
            AddPara($"{progLabel}: {programmeName ?? ""}", false, 14, true, spaceBeforeTwips: 0, spaceAfterTwips: 0, color: "000000");

            // 3. Bottom Block (Lower portion without overflowing A4)
            AddPara("School of Computing", true, 14, true, spaceBeforeTwips: spaceBeforeBottom, spaceAfterTwips: 120, color: "000000");
            AddPara("INTI International College Penang", true, 14, true, spaceBeforeTwips: 0, spaceAfterTwips: 180, color: "000000");

            string displayDate = !string.IsNullOrWhiteSpace(documentDate)
                ? documentDate.Trim()
                : "[DPP DEADLINE NOT CONFIGURED]";
            AddPara(displayDate, false, 14, true, spaceBeforeTwips: 0, spaceAfterTwips: 0, color: "000000");

            docx.Write(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Populates placeholders in an existing official Word document template.
        /// </summary>
        public static void PopulatePlaceholders(
            XWPFDocument doc,
            string? thesisTitle,
            string? studentName,
            string? coventryId,
            string? supervisorName,
            string? programmeName,
            string? documentDate)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["{{ThesisTitle}}"] = thesisTitle ?? "",
                ["[THESIS TITLE]"] = thesisTitle ?? "",
                ["{{StudentName}}"] = studentName ?? "",
                ["[STUDENT NAME]"] = studentName ?? "",
                ["[NAME]"] = studentName ?? "",
                ["{{CoventryStudentId}}"] = coventryId ?? "",
                ["[COVENTRY STUDENT ID]"] = coventryId ?? "",
                ["{{SupervisorName}}"] = supervisorName ?? "",
                ["[SUPERVISOR NAME]"] = supervisorName ?? "",
                ["{{ProgrammeName}}"] = programmeName ?? "",
                ["{{ProgramName}}"] = programmeName ?? "",
                ["[PROGRAM NAME]"] = programmeName ?? "",
                ["[PROGRAMME NAME]"] = programmeName ?? "",
                ["{{DocumentDate}}"] = documentDate ?? "",
                ["[DOCUMENT DATE]"] = documentDate ?? "",
                ["[DPP DEADLINE NOT CONFIGURED]"] = documentDate ?? ""
            };

            void ReplaceInPara(XWPFParagraph p)
            {
                string text = p.ParagraphText;
                bool modified = false;
                foreach (var kvp in replacements)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
                        modified = true;
                    }
                }
                if (modified)
                {
                    for (int i = p.Runs.Count - 1; i >= 0; i--)
                    {
                        p.RemoveRun(i);
                    }
                    var r = p.CreateRun();
                    r.SetText(text);
                    r.FontFamily = "Times New Roman";
                }
            }

            foreach (var para in doc.Paragraphs)
            {
                ReplaceInPara(para);
            }

            foreach (var table in doc.Tables)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.GetTableCells())
                    {
                        foreach (var para in cell.Paragraphs)
                        {
                            ReplaceInPara(para);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates that a byte array or stream represents a valid OpenXML DOCX document.
        /// </summary>
        public static bool IsValidDocx(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return false;
            // Zip magic bytes PK\x03\x04
            if (bytes[0] != 0x50 || bytes[1] != 0x4B || bytes[2] != 0x03 || bytes[3] != 0x04)
                return false;

            try
            {
                using var ms = new MemoryStream(bytes);
                var doc = new XWPFDocument(ms);
                doc.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
