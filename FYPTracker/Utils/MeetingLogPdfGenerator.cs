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
        body {{ font-family: 'Helvetica', 'Arial', sans-serif; font-size: 12pt; line-height: 1.5; }}
        h2 {{ text-align: center; border-bottom: 2px solid #000; padding-bottom: 10px; margin-bottom: 20px; }}
        .header-info {{ text-align: right; font-size: 10pt; color: #555; margin-bottom: 20px; }}
        .field-container {{ margin-bottom: 20px; width: 100%; page-break-inside: avoid; }}
        .field-label {{ font-weight: bold; margin-bottom: 5px; background-color: #eee; padding: 5px; border-left: 4px solid #0056b3; }}
        .field-value {{ padding: 10px; border: 1px solid #ccc; min-height: 40px; white-space: pre-wrap; }}
        .signature-box {{ border: 1px dashed #aaa; padding: 10px; min-height: 80px; text-align: center; }}
    </style>
</head>
<body>
    <div class=""header-info"">Stage {meeting.Stage} - Week {meeting.WeekNumber}</div>
    <h2>{WebUtility.HtmlEncode(templateSnapshot.Title)}</h2>
");

            // Map all fields dynamically in exact order
            var sortedFields = templateSnapshot.Fields.OrderBy(f => f.Order).ToList();
            
            foreach(var field in sortedFields)
            {
                html.Append($@"<div class=""field-container"">");
                html.Append($@"<div class=""field-label"">{WebUtility.HtmlEncode(field.Label)}</div>");
                
                string contentHtml = "";

                switch(field.Key)
                {
                    case "Supervisor":
                        contentHtml = WebUtility.HtmlEncode(supervisor?.FullName ?? "Unknown");
                        break;
                    case "Student":
                        contentHtml = $"{WebUtility.HtmlEncode(student?.FullName ?? "Unknown")} ({WebUtility.HtmlEncode(student?.EmployeeId ?? "")})";
                        break;
                    case "MeetingDate":
                        contentHtml = meeting.MeetingDate.ToString("dd MMM yyyy");
                        break;
                    case "TopicsDiscussed": 
                        contentHtml = WebUtility.HtmlEncode(log.KeyTopicsDiscussed ?? ""); 
                        break;
                    case "ActionPoints": 
                        contentHtml = WebUtility.HtmlEncode(log.ActionPointsForNextMeeting ?? ""); 
                        break;
                    case "ActionsCompleted": 
                        contentHtml = WebUtility.HtmlEncode(log.CompletedActionsAndNotes ?? ""); 
                        break;
                    case "NextMeetingDate": 
                        contentHtml = log.NextMeetingDate?.ToString("dd MMM yyyy") ?? "TBD"; 
                        break;
                    case "SupervisorSignature":
                        if (!isSigned)
                        {
                            contentHtml = @"<div class=""signature-box"" style=""color: #777; font-style: italic;"">Pending Signature</div>";
                        }
                        else
                        {
                            string sig = log.SupervisorSignature;
                            if (string.IsNullOrEmpty(sig))
                            {
                                contentHtml = @"<div class=""signature-box"" style=""color: #777; font-style: italic;"">Signed (No Signature Image Provided)</div>";
                            }
                            else if (sig.StartsWith("data:image/"))
                            {
                                contentHtml = $@"<div class=""signature-box""><img src=""{WebUtility.HtmlEncode(sig)}"" style=""max-height: 80px; max-width: 250px;"" /></div>";
                            }
                            else
                            {
                                contentHtml = $@"<div class=""signature-box""><span style=""font-family: 'Times New Roman', Times, serif; font-size: 24px; font-style: italic; color: #00008B;"">{WebUtility.HtmlEncode(sig)}</span></div>";
                            }
                        }
                        break;
                    case "SupervisorSignatureDate":
                        contentHtml = isSigned ? (log.SignatureDate?.ToString("dd MMM yyyy") ?? "TBD") : "TBD";
                        break;
                    case "SupervisorComments":
                        contentHtml = isSigned ? WebUtility.HtmlEncode(log.DisplayComments ?? "") : "<em>Pending Review</em>";
                        break;
                    default:
                        contentHtml = "<em>Unknown Field</em>";
                        break;
                }

                if (field.Key != "SupervisorSignature")
                {
                    html.Append($@"<div class=""field-value"">{contentHtml}</div>");
                }
                else
                {
                    html.Append(contentHtml); // Signature box has its own styling
                }
                
                html.Append("</div>");
            }

            html.Append(@"
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
