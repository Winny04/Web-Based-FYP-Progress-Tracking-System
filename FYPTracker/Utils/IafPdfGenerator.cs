using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using iTextSharp.text;
using iTextSharp.text.pdf;
using FYPTracker.Models;
using iTextSharp.tool.xml;

namespace FYPTracker.Utils
{
    public static class IafPdfGenerator
    {
        public static string GetIafHtml(FYPMilestone milestone, IafAttempt attempt, ApplicationUser student, string templatePath, string adminName = "Admin")
        {
            if (!File.Exists(templatePath)) return "Template not found.";
            string html = File.ReadAllText(templatePath);
            
            var dict = attempt.FormData ?? new Dictionary<string, string>();
            string getVal(string key) => dict.TryGetValue(key, out var val) ? val : "";

            html = html.Replace("{{StudentName}}", System.Net.WebUtility.HtmlEncode(student?.FullName ?? ""));
            html = html.Replace("{{CoventryId}}", System.Net.WebUtility.HtmlEncode(student?.CoventryId ?? ""));
            html = html.Replace("{{IntiId}}", System.Net.WebUtility.HtmlEncode(student?.EmployeeId ?? ""));
            
            // For now, extract Course Code from somewhere, or leave blank if not available
            html = html.Replace("{{CourseCode}}", "6002CMD");
            html = html.Replace("{{ModuleLeaderName}}", System.Net.WebUtility.HtmlEncode(adminName));
            
            html = html.Replace("{{ProjectTitle}}", System.Net.WebUtility.HtmlEncode(getVal("title")));
            html = html.Replace("{{Scope}}", System.Net.WebUtility.HtmlEncode(getVal("scope")));
            html = html.Replace("{{ProblemStatement}}", System.Net.WebUtility.HtmlEncode(getVal("problem")));
            html = html.Replace("{{Objective1}}", System.Net.WebUtility.HtmlEncode(getVal("obj1")));
            html = html.Replace("{{Objective2}}", System.Net.WebUtility.HtmlEncode(getVal("obj2")));
            html = html.Replace("{{Objective3}}", System.Net.WebUtility.HtmlEncode(getVal("obj3")));
            html = html.Replace("{{AgreedSupervisorName}}", System.Net.WebUtility.HtmlEncode(getVal("agreedSupervisor")));
            
            // Supervisor Signature & Feedback
            string sigHtml = "";
            string sig = attempt.SupervisorSignature;
            if (!string.IsNullOrEmpty(sig))
            {
                if (sig.StartsWith("data:image/"))
                    sigHtml = $"<img src=\"{sig}\" style=\"max-height: 60px; max-width: 200px;\" />";
                else
                    sigHtml = $"<span style=\"font-family: 'Times New Roman', Times, serif; font-size: 24px; font-style: italic; color: #00008B;\">{System.Net.WebUtility.HtmlEncode(sig)}</span>";
            }
            
            html = html.Replace("{{SupervisorName}}", System.Net.WebUtility.HtmlEncode(milestone.Project?.Supervisor?.FullName ?? ""));
            html = html.Replace("{{SupervisorSignature}}", sigHtml);
            html = html.Replace("{{SupervisorFeedback}}", System.Net.WebUtility.HtmlEncode(attempt.SupervisorFeedback ?? ""));

            return html;
        }

        public static byte[] GenerateIafPdf(FYPMilestone milestone, IafAttempt attempt, ApplicationUser student, string webRootPath, string adminName = "Admin")
        {
            string templatePath = System.IO.Path.Combine(webRootPath, "templates", "IafTemplate.html");
            string html = GetIafHtml(milestone, attempt, student, templatePath, adminName);

            using (MemoryStream ms = new MemoryStream())
            {
                Document document = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter writer = PdfWriter.GetInstance(document, ms);
                document.Open();

                using (var stringReader = new StringReader(html))
                {
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, document, stringReader);
                }

                document.Close();
                return ms.ToArray();
            }
        }
        public static void EnsurePdfSnapshots(FYPMilestone milestone, string webRootPath, FYPTrackerContext context)
        {
            if (milestone == null || string.IsNullOrEmpty(milestone.Remarks) || milestone.Status == "Draft" || milestone.Status == "Submitted") return;
            if (!milestone.Title.Contains("Initial Project Allocation Form (IAF)") && !milestone.Title.Contains("IAF")) return;
            
            string parsedJson = milestone.Remarks;
            if (milestone.Remarks.Contains("JSON: ")) parsedJson = milestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None)[1];
            
            var history = IafAttemptHistory.ParseOrMigrate(parsedJson);
            var adminUser = context.ApplicationUsers.FirstOrDefault(u => u.UserRoles.Any(r => r.Role.Name == "Admin"));
            string adminName = adminUser?.FullName ?? "Admin";
            
            foreach (var attempt in history.Attempts)
            {
                if (attempt.Status == "Approved" || attempt.Status == "Rejected" || milestone.Status == "Approved" || milestone.Status == "AdminApproved" || milestone.Status == "ChangeOfTopicRequired" || milestone.Status == "DefenseRequired" || milestone.Status == "PanelRevisionRequired")
                {
                    // Check if PDF already exists for this attempt
                    bool hasPdf = milestone.SubmissionFiles?.Any(f => f.AttemptNumber == attempt.AttemptNumber && f.FileName.StartsWith("Signed IAF")) == true;
                    if (!hasPdf)
                    {
                        try
                        {
                            var pdfBytes = GenerateIafPdf(milestone, attempt, milestone.Project?.Student, webRootPath, adminName);
                            string uniqueFileName = $"IAF_Snapshot_Legacy_Attempt_{attempt.AttemptNumber}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
                            string folderPath = Path.Combine(webRootPath, "uploads", "submissions");
                            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                            File.WriteAllBytes(Path.Combine(folderPath, uniqueFileName), pdfBytes);
                            
                            var subFile = new FYPSubmissionFile
                            {
                                MilestoneId = milestone.Id,
                                FileName = $"Signed IAF (Attempt {attempt.AttemptNumber}).pdf",
                                FilePath = $"/uploads/submissions/{uniqueFileName}",
                                FileSize = pdfBytes.Length,
                                UploadedBy = milestone.Project?.SupervisorId,
                                UploadedDate = attempt.SupervisorReviewedAt ?? DateTime.Now,
                                AttemptNumber = attempt.AttemptNumber,
                                Remarks = "System Generated Snapshot (Legacy Fallback)"
                            };
                            context.FYPSubmissionFiles.Add(subFile);
                            if (milestone.SubmissionFiles == null) milestone.SubmissionFiles = new List<FYPSubmissionFile>();
                            milestone.SubmissionFiles.Add(subFile);
                        }
                        catch {}
                    }
                }
            }
            context.SaveChanges();
        }
    }
}




