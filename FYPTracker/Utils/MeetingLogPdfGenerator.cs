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
        body {{ font-family: 'Helvetica', 'Arial', sans-serif; font-size: 9.5pt; line-height: 1.35; margin: 0; padding: 10mm 12mm; color: #333; }}
        h2.template-heading {{ text-align: center; margin-bottom: 12px; font-size: 14pt; font-weight: bold; text-transform: uppercase; }}
        .meeting-field-label {{ font-size: 8.5pt; font-weight: bold; margin-bottom: 2px; color: #363e4e; margin-top: 6px; }}
        .meeting-single-line {{ min-height: 20px; border-bottom: 1px solid #a0aab9; padding-bottom: 2px; font-size: 9.5pt; }}
        .meeting-writing-area {{ border: 1px solid #a0aab9; padding: 6px 8px; font-size: 9.5pt; white-space: pre-wrap; min-height: 16mm; }}
        .meeting-signature-space {{ border-bottom: 1px solid #a0aab9; min-height: 14mm; text-align: center; padding-top: 4px; font-size: 9.5pt; }}
        table.grid {{ width: 100%; border-collapse: collapse; margin-bottom: 6px; }}
        table.grid td {{ vertical-align: top; padding: 3px 5px; }}
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
                bool isSigField = field.Key == "SupervisorSignature" || field.Key == "SupervisorSignatureDate";
                bool isHalf = isSigField || (field.Columns == 6 || field.Columns == 4 || field.Columns == 3);
                
                string contentHtml = "";
                switch(field.Key)
                {
                    case "Supervisor": contentHtml = WebUtility.HtmlEncode(supervisor?.FullName ?? "Unknown"); break;
                    case "Student": contentHtml = $"{WebUtility.HtmlEncode(student?.FullName ?? "Unknown")} ({WebUtility.HtmlEncode(student?.CoventryId ?? student?.EmployeeId ?? "")})"; break;
                    case "MeetingDate": contentHtml = meeting.MeetingDate.ToString("dd MMM yyyy"); break;
                    case "Week":
                    case "WeekNumber": contentHtml = meeting.WeekNumber.HasValue && meeting.WeekNumber > 0 ? $"Week {meeting.WeekNumber.Value}" : "TBD"; break;
                    case "TopicsDiscussed": contentHtml = WebUtility.HtmlEncode(log.KeyTopicsDiscussed ?? ""); break;
                    case "ActionPoints": contentHtml = WebUtility.HtmlEncode(log.ActionPointsForNextMeeting ?? ""); break;
                    case "ActionsCompleted": contentHtml = WebUtility.HtmlEncode(log.CompletedActionsAndNotes ?? ""); break;
                    case "NextMeetingDate": contentHtml = log.NextMeetingDate?.ToString("dd MMM yyyy") ?? "TBD"; break;
                    case "SupervisorSignature":
                        if (!isSigned) contentHtml = "<em style='color:#777;'>Pending Signature</em>";
                        else {
                            string sig = log.SupervisorSignature;
                            if (string.IsNullOrEmpty(sig)) contentHtml = "<em style='color:#777;'>Signed</em>";
                            else if (sig.StartsWith("data:image/")) contentHtml = $"<img src='{WebUtility.HtmlEncode(sig)}' style='max-height: 45px;' />";
                            else contentHtml = $"<span style='font-family: \"Times New Roman\", serif; font-size: 14pt; font-style: italic; color: #00008B;'>{WebUtility.HtmlEncode(sig)}</span>";
                        }
                        break;
                    case "SupervisorSignatureDate": contentHtml = isSigned ? (log.SignatureDate?.ToString("dd MMM yyyy") ?? "TBD") : "TBD"; break;
                    case "SupervisorComments": contentHtml = isSigned ? WebUtility.HtmlEncode(log.DisplayComments ?? "") : "<em style='color:#777;'>Pending Review</em>"; break;
                    default: contentHtml = ""; break;
                }

                // Render inner field HTML
                string fieldHtml = $@"<div class=""meeting-field-label"">{WebUtility.HtmlEncode(field.Label)}</div>";
                if (field.Type == "TextArea")
                    fieldHtml += $@"<div class=""meeting-writing-area"" style=""min-height: 16mm"">{contentHtml.Replace("\n", "<br/>")}</div>";
                else if (field.Type == "Signature")
                    fieldHtml += $@"<div class=""meeting-signature-space"" style=""min-height: 14mm"">{contentHtml}</div>";
                else
                    fieldHtml += $@"<div class=""meeting-single-line"">{contentHtml}</div>";

                if (isHalf)
                {
                    if (!inRow) { html.Append("<tr>"); inRow = true; }
                    html.Append($@"<td class=""w-50"">{fieldHtml}</td>");
                    // If next field is not half, or this is the last field, close the row
                    bool nextIsHalf = i + 1 < sortedFields.Count && (
                        sortedFields[i+1].Key == "SupervisorSignature" ||
                        sortedFields[i+1].Key == "SupervisorSignatureDate" ||
                        sortedFields[i+1].Columns == 6 || sortedFields[i+1].Columns == 4 || sortedFields[i+1].Columns == 3
                    );
                    if (i == sortedFields.Count - 1 || !nextIsHalf)
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

            // Compact, professional margins for A4 (10 mm vertical, 12 mm horizontal).
            float marginH = MmToPoints(12f);
            float marginV = MmToPoints(10f);
            using var output = new MemoryStream();
            using (var document = new Document(paper, marginH, marginH, marginV, marginV))
            {
                PdfWriter.GetInstance(document, output).CloseStream = false;
                document.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13f, BaseColor.BLACK);
                var title = new Paragraph(templateSnapshot.Title ?? "Record of Supervisor Meeting", titleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 6f
                };
                document.Add(title);

                // The Bootstrap Admin preview is a twelve-column grid.
                // PdfPTable's 12 equal columns preserve 3/4/6/12 spans on A4.
                var formTable = new PdfPTable(12)
                {
                    WidthPercentage = 100f,
                    SplitRows = true,
                    SplitLate = true, // Must be true so iText does not split a row mid-cell across pages!
                    SpacingBefore = 0f
                };
                formTable.SetWidths(new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f });

                // Keep signature, signature date, and comments together across page breaks.
                int? signatureRow = null;
                int? signatureDateRow = null;
                int? commentsRow = null;
                int usedColumns = 0;

                foreach (var field in templateSnapshot.Fields.OrderBy(f => f.Order))
                {
                    // Always pair Supervisor Signature and Signature Date side-by-side (6 columns each)
                    bool isSig = field.Key == "SupervisorSignature";
                    bool isSigDate = field.Key == "SupervisorSignatureDate";
                    int span = (isSig || isSigDate) ? 6 : (field.Columns >= 3 && field.Columns <= 12 ? field.Columns : 12);

                    if (usedColumns + span > 12)
                    {
                        // Finish the current row so the next field starts below.
                        if (usedColumns > 0) formTable.AddCell(EmptyGridCell(12 - usedColumns));
                        usedColumns = 0;
                    }

                    int currentRowIndex = formTable.Rows.Count;
                    if (isSig) signatureRow = currentRowIndex;
                    if (isSigDate) signatureDateRow = currentRowIndex;
                    if (field.Key == "SupervisorComments") commentsRow = currentRowIndex;

                    var outerCell = new PdfPCell
                    {
                        Colspan = span,
                        Border = Rectangle.NO_BORDER,
                        PaddingLeft = 2f,
                        PaddingRight = 4f,
                        PaddingTop = 2f,
                        PaddingBottom = 2f
                    };

                    var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8.5f, new BaseColor(54, 62, 78));
                    outerCell.AddElement(new Paragraph(field.Label ?? field.Key ?? "", labelFont)
                    {
                        SpacingAfter = 2f
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

                // Keep Supervisor Signature, Signature Date, and Supervisor Comments & Feedback together across page breaks:
                int? startRow = null;
                int? endRow = null;

                if (signatureRow.HasValue)
                {
                    startRow = signatureRow.Value;
                    endRow = signatureRow.Value;
                }
                if (signatureDateRow.HasValue)
                {
                    startRow = startRow.HasValue ? Math.Min(startRow.Value, signatureDateRow.Value) : signatureDateRow.Value;
                    endRow = endRow.HasValue ? Math.Max(endRow.Value, signatureDateRow.Value) : signatureDateRow.Value;
                }
                if (commentsRow.HasValue)
                {
                    startRow = startRow.HasValue ? Math.Min(startRow.Value, commentsRow.Value) : commentsRow.Value;
                    endRow = endRow.HasValue ? Math.Max(endRow.Value, commentsRow.Value) : commentsRow.Value;
                }

                if (startRow.HasValue && endRow.HasValue)
                {
                    formTable.KeepRowsTogether(startRow.Value, endRow.Value + 1);
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
            var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 9.5f, BaseColor.BLACK);
            var mutedFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8.5f, BaseColor.GRAY);
            bool isArea = string.Equals(field.Type, "TextArea", StringComparison.OrdinalIgnoreCase);
            bool isSignature = string.Equals(field.Type, "Signature", StringComparison.OrdinalIgnoreCase);

            // Set compact minimum heights that allow standard content to fit on a single A4 page
            // while expanding naturally without clipping or overlapping when text is longer:
            float minHeightPoints = isArea
                ? MmToPoints(16f)
                : isSignature
                    ? MmToPoints(14f)
                    : MmToPoints(6.5f);

            var cell = new PdfPCell
            {
                Border = isArea ? Rectangle.BOX : Rectangle.BOTTOM_BORDER,
                BorderColor = new BaseColor(160, 170, 185),
                BorderWidth = 0.5f,
                PaddingLeft = isArea ? 6f : 2f,
                PaddingRight = isArea ? 6f : 2f,
                PaddingTop = isArea ? 4f : 2f,
                PaddingBottom = isArea ? 4f : 2f,
                MinimumHeight = minHeightPoints
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
                // PDF point dimensions. ScaleToFit retains natural aspect ratio within max 160pt width and 13mm height
                signature.ScaleToFit(160f, MmToPoints(13f));
                signature.Alignment = Element.ALIGN_CENTER;
                cell.AddElement(signature);
                return cell;
            }

            if (field.Key == "SupervisorSignature" && isSigned &&
                !string.IsNullOrWhiteSpace(log.SupervisorSignature) &&
                !log.SupervisorSignature.StartsWith("data:image/"))
            {
                var sigFont = FontFactory.GetFont("Times-Roman", 13f, Font.ITALIC, new BaseColor(0, 0, 139));
                var sigPara = new Paragraph(log.SupervisorSignature, sigFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    Leading = 13f
                };
                cell.AddElement(sigPara);
                return cell;
            }

            string value = field.Key switch
            {
                "Supervisor" => meeting.Supervisor?.FullName ?? "Unknown",
                "Student" => $"{meeting.Student?.FullName ?? "Unknown"} ({meeting.Student?.CoventryId ?? meeting.Student?.EmployeeId ?? ""})",
                "MeetingDate" => meeting.MeetingDate.ToString("dd MMM yyyy"),
                "Week" => meeting.WeekNumber.HasValue && meeting.WeekNumber > 0 ? $"Week {meeting.WeekNumber.Value}" : "TBD",
                "WeekNumber" => meeting.WeekNumber.HasValue && meeting.WeekNumber > 0 ? $"Week {meeting.WeekNumber.Value}" : "TBD",
                "TopicsDiscussed" => log.KeyTopicsDiscussed ?? "",
                "ActionPoints" => log.ActionPointsForNextMeeting ?? "",
                "ActionsCompleted" => log.CompletedActionsAndNotes ?? "",
                "NextMeetingDate" => log.NextMeetingDate?.ToString("dd MMM yyyy") ?? "TBD",
                "SupervisorSignature" => isSigned ? (log.SupervisorSignature ?? "Signed") : "Pending Signature",
                "SupervisorSignatureDate" => isSigned ? (log.SignatureDate?.ToString("dd MMM yyyy") ?? "TBD") : "TBD",
                "SupervisorComments" => isSigned ? (log.DisplayComments ?? "") : "Pending Review",
                _ => ""
            };
            bool isPending = !isSigned && (field.Key == "SupervisorSignature" || field.Key == "SupervisorComments");
            cell.AddElement(new Paragraph((value ?? "").Replace("\r\n", "\n").Replace("\r", "\n"), isPending ? mutedFont : valueFont)
            {
                Leading = 12f
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
            bool isSigned,
            string? subDirectory = null)
        {
            byte[] pdfBytes = GeneratePdfBytes(meeting, templateSnapshot, isSigned);
            
            string directoryPath = string.IsNullOrWhiteSpace(subDirectory)
                ? Path.Combine(contentRootPath, "App_Data", "MeetingLogSnapshots")
                : Path.Combine(contentRootPath, subDirectory.Replace('/', Path.DirectorySeparatorChar));

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

            string relativePrefix = string.IsNullOrWhiteSpace(subDirectory) ? "App_Data/MeetingLogSnapshots" : subDirectory.TrimEnd('/');
            return $"{relativePrefix}/{uniqueFileName}".Replace("\\", "/");
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
                string dirPart = Path.GetDirectoryName(pdfLogbookPath) ?? "App_Data/MeetingLogSnapshots";
                string fileName = Path.GetFileName(pdfLogbookPath);
                if (fileName.StartsWith("Draft_Log_"))
                {
                    string jsonFileName = fileName.Replace("Draft_Log_", "Draft_Template_").Replace(".pdf", ".json");
                    snapshotJsonPath = Path.Combine(contentRootPath, dirPart.Replace('/', Path.DirectorySeparatorChar), jsonFileName);
                }
            }

            // Fallback 1: check inside the folder specified in pdfLogbookPath if not found above
            if (snapshotJsonPath == null || !File.Exists(snapshotJsonPath))
            {
                if (!string.IsNullOrEmpty(pdfLogbookPath))
                {
                    string dirPart = Path.GetDirectoryName(pdfLogbookPath) ?? "App_Data/MeetingLogSnapshots";
                    var candidate = Path.Combine(contentRootPath, dirPart.Replace('/', Path.DirectorySeparatorChar), $"Draft_Template_{meetingId}.json");
                    if (File.Exists(candidate)) snapshotJsonPath = candidate;
                }
            }

            // Fallback 2: legacy snapshot naming in default App_Data/MeetingLogSnapshots
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
