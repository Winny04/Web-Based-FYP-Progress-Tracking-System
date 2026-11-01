using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using iTextSharp.text;
using iTextSharp.text.pdf;
using FYPTracker.Models;
using FYPTracker.ViewModels;
using System.Text.Json;

namespace FYPTracker.Utils
{
    public static class IafPdfGenerator
    {
        public static byte[] GenerateIafPdf(FYPMilestone milestone, IafAttempt attempt, ApplicationUser student, string webRootPath, string adminName = "Admin")
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Document document = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter writer = PdfWriter.GetInstance(document, ms);
                document.Open();

                var fd = attempt.FormData ?? new Dictionary<string, string>();
                string Fv(string k) => fd.TryGetValue(k, out var v) ? (v ?? "") : "";

                MeetingRecordTemplateVM viewTemplate = null;
                if (fd.TryGetValue("__iafTemplateSnapshot", out var snapshotJson) && !string.IsNullOrWhiteSpace(snapshotJson))
                {
                    try
                    {
                        viewTemplate = JsonSerializer.Deserialize<MeetingRecordTemplateVM>(
                            snapshotJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (viewTemplate?.Fields == null || viewTemplate.Fields.Count == 0)
                            viewTemplate = null;
                    }
                    catch { viewTemplate = null; }
                }

                bool isIaf = true; 
                if (viewTemplate != null && !string.IsNullOrEmpty(viewTemplate.TemplateKey))
                {
                    isIaf = viewTemplate.TemplateKey.ToLowerInvariant().Contains("initial-project") || viewTemplate.TemplateKey.ToLowerInvariant() == "iaf";
                }

                // Fallback to the immutable layout if no snapshot
                if (viewTemplate == null)
                {
                    viewTemplate = new MeetingRecordTemplateVM
                    {
                        Title = "Initial Project Allocation Form",
                        Fields = new List<TemplateField>
                        {
                            new TemplateField { Key = "StudentName", Label = "Student Name", Order = 1 },
                            new TemplateField { Key = "CoventryId", Label = "Coventry Student ID", Order = 2 },
                            new TemplateField { Key = "Program", Label = "Programme", Order = 3 },
                            new TemplateField { Key = "ProjectTitle", Label = "Project Title", Order = 4 },
                            new TemplateField { Key = "ScopeOfProject", Label = "Scope of Project", Order = 5, MinHeightMm = 30 },
                            new TemplateField { Key = "ProblemStatement", Label = "Problem Statement", Order = 6, MinHeightMm = 30 },
                            new TemplateField { Key = "References", Label = "References Listing", Order = 7, MinHeightMm = 30 },
                            new TemplateField { Key = "Objectives", Label = "Objectives", Order = 8, MinHeightMm = 30 },
                            new TemplateField { Key = "SupervisorName", Label = "Agreed Supervisor Name", Order = 9 },
                            new TemplateField { Key = "SupervisorSignature", Label = "Supervisor Signature", Order = 10, Type = "Signature" },
                            new TemplateField { Key = "CommentFeedback", Label = "Comment / Feedback", Order = 11, MinHeightMm = 30 }
                        }
                    };
                }

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
                
                string documentTitle = viewTemplate.Title ?? milestone.Title;
                
                var titleParagraph = new Paragraph(documentTitle.ToUpper(), titleFont);
                titleParagraph.Alignment = Element.ALIGN_CENTER;
                titleParagraph.SpacingAfter = 20;
                document.Add(titleParagraph);

                string currentSection = "";
                bool isSigned = !string.IsNullOrEmpty(attempt.SupervisorSignature);
                string displayedSupervisorName = !string.IsNullOrWhiteSpace(Fv("__iafSignedByName")) ? Fv("__iafSignedByName") : (milestone.Project?.Supervisor?.FullName ?? "");

                foreach (var field in viewTemplate.Fields.OrderBy(f => f.Order))
                {
                    string sectionTitle = "Form Details";
                    
                    if (isIaf)
                    {
                        sectionTitle = field.Key switch
                        {
                            "StudentName" or "CoventryId" or "Program" or "ProjectTitle" => "Student and Project Information",
                            "ScopeOfProject" => "Scope of Project",
                            "ProblemStatement" => "Problem Statement",
                            "References" => "References Listing",
                            "Objectives" => "Objectives",
                            "SupervisorName" or "SupervisorSignature" => "Supervisor Details (Optional - Prior Agreement Only)",
                            "CommentFeedback" => "Comment / Feedback",
                            _ => "Other Information"
                        };
                    }

                    if (sectionTitle != currentSection && !string.IsNullOrEmpty(sectionTitle))
                    {
                        currentSection = sectionTitle;
                        var sectionPara = new Paragraph(sectionTitle.ToUpper(), sectionFont);
                        sectionPara.SpacingBefore = 10;
                        sectionPara.SpacingAfter = 5;
                        document.Add(sectionPara);
                        
                        var line = new iTextSharp.text.pdf.draw.LineSeparator(1f, 100f, BaseColor.BLACK, Element.ALIGN_LEFT, -1);
                        document.Add(new Chunk(line));
                    }

                    string value = "";
                    string signatureVal = "";

                    if (isIaf)
                    {
                        value = field.Key switch
                        {
                            "StudentName" => !string.IsNullOrWhiteSpace(Fv("studentName")) ? Fv("studentName") : student?.FullName ?? "",
                            "CoventryId" => !string.IsNullOrWhiteSpace(Fv("coventryId")) ? Fv("coventryId") : student?.CoventryId ?? "",
                            "Program" => !string.IsNullOrWhiteSpace(Fv("program")) ? Fv("program") : student?.Programme ?? "",
                            "ProjectTitle" => !string.IsNullOrWhiteSpace(Fv("title")) ? Fv("title") : milestone.Project?.Title ?? "",
                            "ScopeOfProject" => !string.IsNullOrWhiteSpace(Fv("scope")) ? Fv("scope") : milestone.Project?.Scope ?? "",
                            "ProblemStatement" => !string.IsNullOrWhiteSpace(Fv("problem")) ? Fv("problem") : milestone.Project?.ProblemStatement ?? "",
                            "References" => !string.IsNullOrWhiteSpace(Fv("references")) ? Fv("references") : Fv("References"),
                            "SupervisorName" => isSigned ? displayedSupervisorName : (!string.IsNullOrWhiteSpace(Fv("agreedSupervisor")) ? Fv("agreedSupervisor") : (!string.IsNullOrWhiteSpace(Fv("Agreed Supervisor Name")) ? Fv("Agreed Supervisor Name") : "")),
                            "CommentFeedback" => attempt.SupervisorFeedback ?? "",
                            "Objectives" => string.Join("\n", new[] {
                                (!string.IsNullOrWhiteSpace(Fv("obj1")) ? Fv("obj1") : milestone.Project?.Objective1),
                                (!string.IsNullOrWhiteSpace(Fv("obj2")) ? Fv("obj2") : milestone.Project?.Objective2),
                                (!string.IsNullOrWhiteSpace(Fv("obj3")) ? Fv("obj3") : milestone.Project?.Objective3)
                            }.Select((rawObj, idx) => {
                                if (string.IsNullOrWhiteSpace(rawObj)) return "";
                                int num = idx + 1;
                                string cleaned = System.Text.RegularExpressions.Regex.Replace(
                                    rawObj,
                                    $@"^\s*({num}\.\s*)?(To\s+)+",
                                    "",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                                return string.IsNullOrWhiteSpace(cleaned) ? "" : $"{num}. To {cleaned}";
                            }).Where(s => !string.IsNullOrEmpty(s))),
                            _ => Fv(field.Key)
                        };

                        if (field.Key == "SupervisorSignature")
                        {
                            signatureVal = attempt?.SupervisorSignature ?? "";
                        }
                    }
                    else
                    {
                        value = Fv(field.Key);
                        if (field.Type == "Signature")
                        {
                            signatureVal = value;
                        }
                    }

                    var labelPara = new Paragraph(field.Label, labelFont);
                    labelPara.SpacingBefore = 8;
                    labelPara.SpacingAfter = 2;
                    document.Add(labelPara);

                    PdfPTable table = new PdfPTable(1);
                    table.WidthPercentage = 100;
                    
                    PdfPCell cell = new PdfPCell();
                    cell.BorderColor = BaseColor.GRAY;
                    cell.Padding = 5;
                    float minHeight = Math.Max(20f, (field.MinHeightMm * 2.83465f));
                    cell.MinimumHeight = minHeight;
                    
                    if (field.Type == "Signature" || (isIaf && field.Key == "SupervisorSignature"))
                    {
                        if (string.IsNullOrWhiteSpace(signatureVal))
                        {
                            cell.AddElement(new Paragraph("Not signed", FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10, BaseColor.GRAY)));
                        }
                        else if (signatureVal.StartsWith("data:image"))
                        {
                            try
                            {
                                string base64 = signatureVal.Substring(signatureVal.IndexOf(",") + 1);
                                byte[] imageBytes = Convert.FromBase64String(base64);
                                Image img = Image.GetInstance(imageBytes);
                                img.ScaleToFit(200f, 60f);
                                cell.AddElement(img);
                            }
                            catch
                            {
                                cell.AddElement(new Paragraph("Invalid signature image", valueFont));
                            }
                        }
                        else
                        {
                            cell.AddElement(new Paragraph(signatureVal, FontFactory.GetFont("Times-Roman", 14, Font.ITALIC, BaseColor.BLUE)));
                        }
                        
                        if (isIaf && field.Key == "SupervisorSignature" && attempt.SupervisorReviewedAt.HasValue)
                        {
                            cell.AddElement(new Paragraph("Date: " + attempt.SupervisorReviewedAt.Value.ToString("dd/MM/yyyy"), labelFont));
                        }
                    }
                    else
                    {
                        cell.AddElement(new Paragraph(value, valueFont));
                    }
                    
                    table.AddCell(cell);
                    document.Add(table);
                }

                if (!string.IsNullOrWhiteSpace(attempt.PanelComments))
                {
                    var sectionPara = new Paragraph("PANEL COMMENTS", sectionFont);
                    sectionPara.SpacingBefore = 10;
                    sectionPara.SpacingAfter = 5;
                    document.Add(sectionPara);
                    
                    var line = new iTextSharp.text.pdf.draw.LineSeparator(1f, 100f, BaseColor.BLACK, Element.ALIGN_LEFT, -1);
                    document.Add(new Chunk(line));
                    
                    PdfPTable table = new PdfPTable(1);
                    table.WidthPercentage = 100;
                    
                    PdfPCell cell = new PdfPCell(new Paragraph(attempt.PanelComments, valueFont));
                    cell.BorderColor = BaseColor.GRAY;
                    cell.Padding = 5;
                    cell.MinimumHeight = 85f;
                    
                    table.AddCell(cell);
                    document.Add(table);
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
                    var existingFile = milestone.SubmissionFiles?.FirstOrDefault(f => f.AttemptNumber == attempt.AttemptNumber && f.FileName.StartsWith("Signed IAF"));
                    bool hasPhysicalFile = existingFile != null && !string.IsNullOrEmpty(existingFile.FilePath) && File.Exists(Path.Combine(webRootPath, existingFile.FilePath.TrimStart('/', '\\')));
                    
                    if (!hasPhysicalFile)
                    {
                        try
                        {
                            var pdfBytes = GenerateIafPdf(milestone, attempt, milestone.Project?.Student, webRootPath, adminName);
                            string uniqueFileName = $"IAF_Snapshot_Attempt_{attempt.AttemptNumber}_{Guid.NewGuid().ToString().Substring(0, 8)}.pdf";
                            string folderPath = Path.Combine(webRootPath, "uploads", "submissions");
                            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                            string savedPhysicalPath = Path.Combine(folderPath, uniqueFileName);
                            File.WriteAllBytes(savedPhysicalPath, pdfBytes);
                            
                            if (existingFile != null)
                            {
                                existingFile.FilePath = $"/uploads/submissions/{uniqueFileName}";
                                existingFile.FileSize = pdfBytes.Length;
                                existingFile.UploadedDate = attempt.SupervisorReviewedAt ?? DateTime.Now;
                            }
                            else
                            {
                                var subFile = new FYPSubmissionFile
                                {
                                    MilestoneId = milestone.Id,
                                    FileName = $"Signed IAF (Attempt {attempt.AttemptNumber}).pdf",
                                    FilePath = $"/uploads/submissions/{uniqueFileName}",
                                    FileSize = pdfBytes.Length,
                                    UploadedBy = milestone.Project?.SupervisorId,
                                    UploadedDate = attempt.SupervisorReviewedAt ?? DateTime.Now,
                                    AttemptNumber = attempt.AttemptNumber,
                                    Remarks = "System Generated Snapshot (IAF Rendering Engine)"
                                };
                                context.FYPSubmissionFiles.Add(subFile);
                                if (milestone.SubmissionFiles == null) milestone.SubmissionFiles = new List<FYPSubmissionFile>();
                                milestone.SubmissionFiles.Add(subFile);
                            }
                        }
                        catch {}
                    }
                }
            }
            context.SaveChanges();
        }
    }
}




