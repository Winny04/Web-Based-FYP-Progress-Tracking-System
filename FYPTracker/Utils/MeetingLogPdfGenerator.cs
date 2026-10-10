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

        public static byte[] GeneratePdfBytes(MeetingBooking meeting, MeetingRecordTemplateVM templateSnapshot, bool isSigned)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            string html = GenerateHtmlContent(meeting, templateSnapshot, isSigned);

            using (MemoryStream ms = new MemoryStream())
            {
                Document document = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter writer = PdfWriter.GetInstance(document, ms);
                document.Open();

                // Custom XMLWorker pipeline to support data:image/ base64 images
                var cssFiles = new CssFilesImpl();
                cssFiles.Add(XMLWorkerHelper.GetInstance().GetDefaultCSS());
                var cssResolver = new StyleAttrCSSResolver(cssFiles);
                var htmlContext = new HtmlPipelineContext(new CssAppliersImpl(new XMLWorkerFontProvider()));

                // FIX: Register HTML tag processors for XMLWorker
                htmlContext.SetTagFactory(
                    Tags.GetHtmlTagProcessorFactory());

                // Keep Base64 signature image support
                htmlContext.SetImageProvider(
                    new Base64ImageProvider());

                var pipeline = new CssResolverPipeline(
                    cssResolver,
                    new HtmlPipeline(
                        htmlContext,
                        new PdfWriterPipeline(document, writer)));
                var worker = new XMLWorker(pipeline, true);
                var parser = new XMLParser(worker);

                using (var stringReader = new StringReader(html))
                {
                    try
                    {
                        parser.Parse(stringReader);
                    }
                    catch (Exception ex)
                    {
                        string debugFile = Path.Combine(
                            Path.GetTempPath(),
                            "FYPTracker-MeetingLog-Debug.html"
                        );

                        File.WriteAllText(debugFile, html);

                        throw new InvalidOperationException(
                            $"Failed to generate Meeting Log PDF. " +
                            $"Debug HTML saved at: {debugFile}",
                            ex
                        );
                    }
                }

                document.Close();
                return ms.ToArray();
            }
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
