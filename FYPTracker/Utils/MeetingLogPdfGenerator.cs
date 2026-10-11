using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using FYPTracker.Models;
using FYPTracker.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
using iTextSharp.tool.xml.html;
using iTextSharp.tool.xml.pipeline.html;
using iTextSharp.tool.xml.pipeline.css;
using iTextSharp.tool.xml.pipeline.end;
using iTextSharp.tool.xml.parser;
using iTextSharp.tool.xml.css;
using System.Text;

namespace FYPTracker.Utils
{
    public class Base64ImageProvider : AbstractImageProvider
    {
        public override iTextSharp.text.Image Retrieve(string src)
        {
            if (src != null && src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(src, @"data:image/(?<type>.+?);base64,(?<data>.+)");
                if (match.Success)
                {
                    var base64Data = match.Groups["data"].Value;
                    var binData = Convert.FromBase64String(base64Data);
                    return iTextSharp.text.Image.GetInstance(binData);
                }
            }
            return null;
        }
        
        public override string GetImageRootPath()
        {
            return "";
        }
    }

    public static class MeetingLogPdfGenerator
    {
        public static string GenerateHtmlContent(
            MeetingBooking meeting, 
            MeetingRecordTemplateVM templateSnapshot, 
            bool isSigned)
        {
            var student = meeting.Student;
            var supervisor = meeting.Supervisor;
            var log = meeting.MeetingLog;

            if (log == null) throw new ArgumentNullException(nameof(log), "MeetingLog cannot be null when generating a snapshot.");

            StringBuilder html = new StringBuilder();
            html.Append($@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: 'Helvetica', 'Arial', sans-serif; font-size: 10pt; line-height: 1.4; margin: 0; padding: 20px; color: #333; }}
        h2.template-heading {{ text-align: center; margin-bottom: 20px; font-size: 16pt; font-weight: bold; text-transform: uppercase; }}
        .meeting-field-label {{ font-size: 10pt; font-weight: bold; margin-bottom: 3px; color: #444; margin-top: 8px; }}
        .meeting-single-line {{ min-height: 25px; border-bottom: 1px solid #777; padding-bottom: 2px; font-size: 10pt; }}
        .meeting-writing-area {{ border: 1px solid #ccc; padding: 8px; font-size: 10pt; white-space: pre-wrap; }}
        .meeting-signature-space {{ border-bottom: 1px solid #333; min-height: 60px; text-align: center; padding-top: 15px; font-size: 10pt; }}
        table.grid {{ width: 100%; border-collapse: collapse; margin-bottom: 10px; }}
        table.grid td {{ vertical-align: top; padding: 5px; }}
        .w-50 {{ width: 50%; }}
        .w-100 {{ width: 100%; }}
    </style>
</head>
<body>
    <h2 class=""template-heading"">{WebUtility.HtmlEncode(templateSnapshot.Title)}</h2>
    <table class=""grid"">
");

            var sortedFields = templateSnapshot.Fields.OrderBy(f => f.Order).ToList();
            bool inRow = false;

            for (int i = 0; i < sortedFields.Count; i++)
            {
                var field = sortedFields[i];
                bool isHalf = (field.Columns == 6 || field.Columns == 4 || field.Columns == 3);
                
                string contentHtml = "";
                switch(field.Key)
                {
                    case "Supervisor": contentHtml = WebUtility.HtmlEncode(supervisor?.FullName ?? "Unknown"); break;
                    case "Student": contentHtml = $"{WebUtility.HtmlEncode(student?.FullName ?? "Unknown")} ({WebUtility.HtmlEncode(student?.EmployeeId ?? "")})"; break;
                    case "MeetingDate": contentHtml = meeting.MeetingDate.ToString("dd MMM yyyy"); break;
                    case "TopicsDiscussed": contentHtml = WebUtility.HtmlEncode(log.KeyTopicsDiscussed ?? ""); break;
                    case "ActionPoints": contentHtml = WebUtility.HtmlEncode(log.ActionPointsForNextMeeting ?? ""); break;
                    case "ActionsCompleted": contentHtml = WebUtility.HtmlEncode(log.CompletedActionsAndNotes ?? ""); break;
                    case "NextMeetingDate": contentHtml = log.NextMeetingDate?.ToString("dd MMM yyyy") ?? "TBD"; break;
                    case "SupervisorSignature":
                        if (!isSigned) contentHtml = "<em style='color:#777;'>Pending Signature</em>";
                        else {
                            string sig = log.SupervisorSignature;
                            if (string.IsNullOrEmpty(sig)) contentHtml = "<em style='color:#777;'>Signed</em>";
                            else if (sig.StartsWith("data:image/")) contentHtml = $"<img src='{WebUtility.HtmlEncode(sig)}' style='max-height: 50px;' />";
                            else contentHtml = $"<span style='font-family: \"Times New Roman\", serif; font-size: 16pt; font-style: italic; color: #00008B;'>{WebUtility.HtmlEncode(sig)}</span>";
                        }
                        break;
                    case "SupervisorSignatureDate": contentHtml = isSigned ? (log.SignatureDate?.ToString("dd MMM yyyy") ?? "TBD") : "TBD"; break;
                    case "SupervisorComments": contentHtml = isSigned ? WebUtility.HtmlEncode(log.DisplayComments ?? "") : "<em style='color:#777;'>Pending Review</em>"; break;
                    default: contentHtml = ""; break;
                }

                // Render inner field HTML
                string fieldHtml = $@"<div class=""meeting-field-label"">{WebUtility.HtmlEncode(field.Label)}</div>";
                if (field.Type == "TextArea")
                    fieldHtml += $@"<div class=""meeting-writing-area"" style=""min-height: {Math.Max(15, field.MinHeightMm > 0 ? field.MinHeightMm : 35)}mm"">{contentHtml.Replace("\n", "<br/>")}</div>";
                else if (field.Type == "Signature")
                    fieldHtml += $@"<div class=""meeting-signature-space"" style=""min-height: {Math.Max(15, field.MinHeightMm > 0 ? field.MinHeightMm : 20)}mm"">{contentHtml}</div>";
                else
                    fieldHtml += $@"<div class=""meeting-single-line"">{contentHtml}</div>";

                if (isHalf)
                {
                    if (!inRow) { html.Append("<tr>"); inRow = true; }
                    html.Append($@"<td class=""w-50"">{fieldHtml}</td>");
                    // If next field is not half, or this is the last field, close the row
                    if (i == sortedFields.Count - 1 || (sortedFields[i+1].Columns != 6 && sortedFields[i+1].Columns != 4 && sortedFields[i+1].Columns != 3))
                    {
                        html.Append("</tr>");
                        inRow = false;
                    }
                }
                else
                {
                    if (inRow) { html.Append("</tr>"); inRow = false; }
                    html.Append($@"<tr><td colspan=""2"" class=""w-100"">{fieldHtml}</td></tr>");
                }
            }

            if (inRow) html.Append("</tr>"); // Fallback safety

            html.Append(@"
    </table>
</body>
</html>");

            return html.ToString();
        }

        // Render directly with iTextSharp tables instead of relying on XMLWorker CSS
        // min-height and column sizing, which are inconsistently supported in PDFs.
        // The template passed here MUST be the snapshot selected by the caller.
        public static byte[] GeneratePdfBytes(
            MeetingBooking meeting,
            MeetingRecordTemplateVM templateSnapshot,
            bool isSigned)
        {
            if (meeting?.MeetingLog == null)
                throw new ArgumentException("Meeting Log data is required.", nameof(meeting));
            if (templateSnapshot?.Fields == null || templateSnapshot.Fields.Count == 0)
                throw new ArgumentException("A valid template snapshot is required.", nameof(templateSnapshot));

            // Admin currently uses A4; honour its orientation when supplied.
            Rectangle paper = PageSize.A4;
            if (string.Equals(templateSnapshot.Page?.Orientation, "Landscape", StringComparison.OrdinalIgnoreCase))
                paper = paper.Rotate();

            // Admin A4 preview uses approximately 20 mm of paper padding.
            float margin = MmToPoints(20);
            using var output = new MemoryStream();
            using (var document = new Document(paper, margin, margin, margin, margin))
            {
                PdfWriter.GetInstance(document, output).CloseStream = false;
                document.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 15f, BaseColor.BLACK);
                var title = new Paragraph(templateSnapshot.Title ?? "Record of Supervisor Meeting", titleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 13f
                };
                document.Add(title);

                // The Bootstrap Admin preview is a twelve-column grid.
                // PdfPTable's 12 equal columns preserve 3/4/6/12 spans on A4.
                var formTable = new PdfPTable(12)
                {
                    WidthPercentage = 100f,
                    SplitRows = true,
                    SplitLate = false,
                    SpacingBefore = 2f
                };
                formTable.SetWidths(new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f });

                // Keep a signed signature and its date together across PDF page breaks.
                // Capture row indices in the 12-column grid (the Stage 1 fields use 6+6).
                int? signatureRow = null;
                int? signatureDateRow = null;
                int usedColumns = 0;
                foreach (var field in templateSnapshot.Fields.OrderBy(f => f.Order))
                {
                    int span = field.Columns >= 3 && field.Columns <= 12 ? field.Columns : 12;
                    if (usedColumns + span > 12)
                    {
                        // Finish the current row so a 12-column field starts below.
                        if (usedColumns > 0) formTable.AddCell(EmptyGridCell(12 - usedColumns));
                        usedColumns = 0;
                    }

                    // Row count is still the current row before its final cell is added.
                    // Record AFTER completing a previous partial row, if necessary.
                    int rowIndex = formTable.Rows.Count;
                    if (field.Key == "SupervisorSignature") signatureRow = rowIndex;
                    if (field.Key == "SupervisorSignatureDate") signatureDateRow = rowIndex;

                    var outerCell = new PdfPCell
                    {
                        Colspan = span,
                        Border = Rectangle.NO_BORDER,
                        PaddingLeft = 3f,
                        PaddingRight = 7f,
                        PaddingTop = 5f,
                        PaddingBottom = 6f
                    };

                    var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9f, new BaseColor(54, 62, 78));
                    outerCell.AddElement(new Paragraph(field.Label ?? field.Key ?? "", labelFont)
                    {
                        SpacingAfter = 5f
                    });

                    var valueTable = new PdfPTable(1) { WidthPercentage = 100f };
                    var valueCell = BuildValueCell(meeting, field, isSigned);
                    valueTable.AddCell(valueCell);
                    outerCell.AddElement(valueTable);

                    formTable.AddCell(outerCell);
                    usedColumns += span;
                    if (usedColumns == 12) usedColumns = 0;
                }

                if (usedColumns > 0)
                    formTable.AddCell(EmptyGridCell(12 - usedColumns));

                // PdfPTable can otherwise split the signature image and its date
                // across two pages (especially with SplitRows=true/SplitLate=false).
                // Only protect the short signature row(s), not long TextArea rows:
                // long meeting notes must remain allowed to paginate normally.
                if (signatureRow.HasValue && signatureDateRow.HasValue &&
                    Math.Abs(signatureRow.Value - signatureDateRow.Value) <= 1)
                {
                    int firstRow = Math.Min(signatureRow.Value, signatureDateRow.Value);
                    int endExclusive = Math.Max(signatureRow.Value, signatureDateRow.Value) + 1;
                    formTable.KeepRowsTogether(firstRow, endExclusive);
                }
                else
                {
                    if (signatureRow.HasValue)
                        formTable.KeepRowsTogether(signatureRow.Value, signatureRow.Value + 1);
                    if (signatureDateRow.HasValue)
                        formTable.KeepRowsTogether(signatureDateRow.Value, signatureDateRow.Value + 1);
                }

                document.Add(formTable);
                document.Close();
            }
            return output.ToArray();
        }

        private static float MmToPoints(float millimetres) => millimetres * 72f / 25.4f;

        private static PdfPCell EmptyGridCell(int span) => new PdfPCell
        {
            Colspan = span,
            Border = Rectangle.NO_BORDER
        };

        private static PdfPCell BuildValueCell(MeetingBooking meeting, TemplateField field, bool isSigned)
        {
            var log = meeting.MeetingLog!;
            var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 10f, BaseColor.BLACK);
            var mutedFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 9f, BaseColor.GRAY);
            bool isArea = string.Equals(field.Type, "TextArea", StringComparison.OrdinalIgnoreCase);
            bool isSignature = string.Equals(field.Type, "Signature", StringComparison.OrdinalIgnoreCase);
            var cell = new PdfPCell
            {
                Border = isArea ? Rectangle.BOX : Rectangle.BOTTOM_BORDER,
                BorderColor = new BaseColor(145, 155, 170),
                BorderWidth = 0.65f,
                PaddingLeft = isArea ? 8f : 2f,
                PaddingRight = isArea ? 8f : 2f,
                PaddingTop = isArea ? 8f : 5f,
                PaddingBottom = isArea ? 8f : 5f,
                MinimumHeight = isArea
                    ? MmToPoints(Math.Max(15, Math.Min(100, field.MinHeightMm > 0 ? field.MinHeightMm : 35)))
                    : isSignature
                        ? MmToPoints(Math.Max(15, Math.Min(100, field.MinHeightMm > 0 ? field.MinHeightMm : 20)))
                        : MmToPoints(9)
            };

            // Preserve the original signature image instead of encoding its data URL as text.
            if (field.Key == "SupervisorSignature" && isSigned &&
                !string.IsNullOrWhiteSpace(log.SupervisorSignature) &&
                log.SupervisorSignature.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                string dataUrl = log.SupervisorSignature;
                int separator = dataUrl.IndexOf(',');
                if (separator < 0) throw new InvalidDataException("Invalid supervisor signature data URL.");
                var signature = iTextSharp.text.Image.GetInstance(
                    Convert.FromBase64String(dataUrl.Substring(separator + 1)));
                // PDF point dimensions. ScaleToFit retains the natural aspect ratio.
                signature.ScaleToFit(180f, MmToPoints(16));
                signature.Alignment = Element.ALIGN_CENTER;
                cell.AddElement(signature);
                return cell;
            }

            string value = field.Key switch
            {
                "Supervisor" => meeting.Supervisor?.FullName ?? "Unknown",
                "Student" => $"{meeting.Student?.FullName ?? "Unknown"} ({meeting.Student?.EmployeeId ?? ""})",
                "MeetingDate" => meeting.MeetingDate.ToString("dd MMM yyyy"),
                "TopicsDiscussed" => log.KeyTopicsDiscussed ?? "",
                "ActionPoints" => log.ActionPointsForNextMeeting ?? "",
                "ActionsCompleted" => log.CompletedActionsAndNotes ?? "",
                "NextMeetingDate" => log.NextMeetingDate?.ToString("dd MMM yyyy") ?? "TBD",
                "SupervisorSignature" => isSigned ? (log.SupervisorSignature ?? "Signed") : "Pending Signature",
                "SupervisorSignatureDate" => isSigned ? log.SignatureDate?.ToString("dd MMM yyyy") ?? "TBD" : "TBD",
                "SupervisorComments" => isSigned ? log.DisplayComments ?? "" : "Pending Review",
                _ => ""
            };
            bool isPending = !isSigned && (field.Key == "SupervisorSignature" || field.Key == "SupervisorComments");
            cell.AddElement(new Paragraph(value, isPending ? mutedFont : valueFont)
            {
                Leading = 14f
            });
            return cell;
        }

        /// <summary>
        /// Saves the PDF snapshot. If isSigned is false, it also saves a JSON representation of the template 
        /// so that the Supervisor can sign against the exact same template later.
        /// </summary>
        public static string SavePdfSnapshot(
            string contentRootPath, 
            MeetingBooking meeting, 
            MeetingRecordTemplateVM templateSnapshot, 
            bool isSigned)
        {
            byte[] pdfBytes = GeneratePdfBytes(meeting, templateSnapshot, isSigned);
            
            string directoryPath = Path.Combine(contentRootPath, "App_Data", "MeetingLogSnapshots");
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string statusPrefix = isSigned ? "Signed" : "Draft";
            string uniqueFileName = $"{statusPrefix}_Log_{meeting.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
            string absoluteFilePath = Path.Combine(directoryPath, uniqueFileName);

            File.WriteAllBytes(absoluteFilePath, pdfBytes);

            if (!isSigned)
            {
                // Save the frozen template snapshot so the supervisor signs against the same template
                string snapshotJsonPath = Path.Combine(directoryPath, $"Draft_Template_{meeting.Id}.json");
                File.WriteAllText(snapshotJsonPath, JsonSerializer.Serialize(templateSnapshot));
            }

            return Path.Combine("App_Data", "MeetingLogSnapshots", uniqueFileName).Replace("\\", "/");
        }

        /// <summary>
        /// Retrieves the frozen template snapshot for a given meeting draft. 
        /// Returns null if not found, forcing a fallback to the active template.
        /// </summary>
        public static MeetingRecordTemplateVM GetFrozenTemplateSnapshot(string contentRootPath, int meetingId, string pdfLogbookPath = null)
        {
            string snapshotJsonPath = null;

            if (!string.IsNullOrEmpty(pdfLogbookPath) && pdfLogbookPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Derived from modern format: App_Data/MeetingLogSnapshots/Draft_Log_123_abc123.pdf -> Draft_Template_123_abc123.json
                string fileName = Path.GetFileName(pdfLogbookPath);
                if (fileName.StartsWith("Draft_Log_"))
                {
                    string jsonFileName = fileName.Replace("Draft_Log_", "Draft_Template_").Replace(".pdf", ".json");
                    snapshotJsonPath = Path.Combine(contentRootPath, "App_Data", "MeetingLogSnapshots", jsonFileName);
                }
            }

            // Fallback to legacy snapshot naming
            if (snapshotJsonPath == null || !File.Exists(snapshotJsonPath))
            {
                snapshotJsonPath = Path.Combine(contentRootPath, "App_Data", "MeetingLogSnapshots", $"Draft_Template_{meetingId}.json");
            }

            if (File.Exists(snapshotJsonPath))
            {
                try
                {
                    string json = File.ReadAllText(snapshotJsonPath);
                    return JsonSerializer.Deserialize<MeetingRecordTemplateVM>(json);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }
}
