using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FYPTracker.Models;
using FYPTracker.Utils;
using FYPTracker.ViewModels;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Xunit;

namespace FYPTracker.Tests
{
    public class MeetingLogPdfLayoutTests
    {
        private MeetingRecordTemplateVM CreateDefaultStage1Template()
        {
            return new MeetingRecordTemplateVM
            {
                TemplateKey = "record-of-supervisor-meeting",
                Version = "1.0",
                Title = "6000CEM/6002CMD Record of Supervisor Meeting",
                Page = new TemplatePage { Size = "A4", Orientation = "Portrait" },
                Fields = new List<TemplateField>
                {
                    new TemplateField { Key = "Supervisor", Label = "Supervisor", Order = 1, Columns = 6, Type = "Text", MinHeightMm = 0 },
                    new TemplateField { Key = "Student", Label = "Student", Order = 2, Columns = 6, Type = "Text", MinHeightMm = 0 },
                    new TemplateField { Key = "MeetingDate", Label = "Date of meeting", Order = 3, Columns = 12, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "TopicsDiscussed", Label = "Key topics Discussed", Order = 4, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "ActionPoints", Label = "Individual action points for next meeting (no more than 3)", Order = 5, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "ActionsCompleted", Label = "Record of individual actions completed + notes", Order = 6, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "NextMeetingDate", Label = "Date of next meeting", Order = 7, Columns = 12, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "SupervisorSignature", Label = "Supervisor Signature", Order = 8, Columns = 6, Type = "Signature", MinHeightMm = 20 },
                    new TemplateField { Key = "SupervisorSignatureDate", Label = "Supervisor Signature Date", Order = 9, Columns = 6, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "SupervisorComments", Label = "Supervisor Comments & Feedback", Order = 10, Columns = 12, Type = "TextArea", MinHeightMm = 40 }
                }
            };
        }

        private MeetingRecordTemplateVM CreateDefaultStage2Template()
        {
            return new MeetingRecordTemplateVM
            {
                TemplateKey = "record-of-supervisor-meeting-6001cem-303com",
                Version = "1.0",
                Title = "6001CEM/303COM Record of Supervisor Meeting",
                Page = new TemplatePage { Size = "A4", Orientation = "Portrait" },
                Fields = new List<TemplateField>
                {
                    new TemplateField { Key = "Supervisor", Label = "Supervisor", Order = 1, Columns = 6, Type = "Text", MinHeightMm = 0 },
                    new TemplateField { Key = "Student", Label = "Student", Order = 2, Columns = 6, Type = "Text", MinHeightMm = 0 },
                    new TemplateField { Key = "MeetingDate", Label = "Date of meeting", Order = 3, Columns = 12, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "TopicsDiscussed", Label = "Key Topics Discussed & Tasks Completed", Order = 4, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "ActionPoints", Label = "Individual action points for next meeting (no more than 3)", Order = 5, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "ActionsCompleted", Label = "Record of individual actions completed + notes", Order = 6, Columns = 12, Type = "TextArea", MinHeightMm = 40 },
                    new TemplateField { Key = "NextMeetingDate", Label = "Date of next meeting", Order = 7, Columns = 12, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "SupervisorSignature", Label = "Supervisor Signature", Order = 8, Columns = 6, Type = "Signature", MinHeightMm = 20 },
                    new TemplateField { Key = "SupervisorSignatureDate", Label = "Supervisor Signature Date", Order = 9, Columns = 6, Type = "Date", MinHeightMm = 0 },
                    new TemplateField { Key = "SupervisorComments", Label = "Supervisor Comments & Feedback", Order = 10, Columns = 12, Type = "TextArea", MinHeightMm = 40 }
                }
            };
        }

        private MeetingBooking CreateSampleMeetingBooking(
            string topics = "Reviewed chapter 1 draft and discussed system architecture.",
            string actionPoints = "1. Finalize database schema.\n2. Create wireframes for review.",
            string actionsCompleted = "Completed background research and problem formulation.",
            string comments = "Good progress shown. Keep up the consistent pace.",
            string signature = "Dr. John Supervisor",
            DateTime? signatureDate = null)
        {
            var student = new ApplicationUser
            {
                FullName = "Alice Tan",
                CoventryId = "1234567",
                EmployeeId = "1234567"
            };
            var supervisor = new ApplicationUser
            {
                FullName = "Dr. John Supervisor",
                EmployeeId = "SUP-001"
            };
            var log = new MeetingLog
            {
                Id = 1,
                KeyTopicsDiscussed = topics,
                ActionPointsForNextMeeting = actionPoints,
                CompletedActionsAndNotes = actionsCompleted,
                SupervisorComments = comments + "|||SIG|||" + signature,
                SignatureDate = signatureDate ?? new DateTime(2026, 10, 23),
                LogStatus = "Signed"
            };
            return new MeetingBooking
            {
                Id = 1,
                Student = student,
                Supervisor = supervisor,
                MeetingDate = new DateTime(2026, 10, 20),
                MeetingTime = "10:00 AM",
                WeekNumber = 3,
                MeetingLog = log
            };
        }

        [Fact]
        public void StandardMeetingLog_FitsOnSingleA4Page_WithoutExcessiveWhitespace()
        {
            // Arrange: typical meeting log with standard length feedback
            var template = CreateDefaultStage1Template();
            var meeting = CreateSampleMeetingBooking();

            // Act: Generate PDF
            byte[] pdfBytes = MeetingLogPdfGenerator.GeneratePdfBytes(meeting, template, isSigned: true);

            // Assert: Must be a valid PDF that fits on exactly ONE A4 page
            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var reader = new PdfReader(pdfBytes);
            Assert.Equal(1, reader.NumberOfPages);

            string page1Text = PdfTextExtractor.GetTextFromPage(reader, 1);
            Assert.Contains("6000CEM/6002CMD Record of Supervisor Meeting", page1Text);
            Assert.Contains("Dr. John Supervisor", page1Text);
            Assert.Contains("Alice Tan", page1Text);
            Assert.Contains("Supervisor Signature", page1Text);
            Assert.Contains("Supervisor Signature Date", page1Text);
            Assert.Contains("23 Oct 2026", page1Text);
            Assert.Contains("Supervisor Comments & Feedback", page1Text);
            Assert.Contains("Good progress shown.", page1Text);
        }

        [Fact]
        public void Stage2MeetingLog_FitsOnSingleA4Page_WhenContentFitsNaturally()
        {
            // Arrange: Stage 2 template where fields had columns = 12 originally
            var template = CreateDefaultStage2Template();
            var meeting = CreateSampleMeetingBooking();

            // Act
            byte[] pdfBytes = MeetingLogPdfGenerator.GeneratePdfBytes(meeting, template, isSigned: true);

            // Assert
            using var reader = new PdfReader(pdfBytes);
            Assert.Equal(1, reader.NumberOfPages);

            string pageText = PdfTextExtractor.GetTextFromPage(reader, 1);
            Assert.Contains("6001CEM/303COM Record of Supervisor Meeting", pageText);
            Assert.Contains("Supervisor Signature", pageText);
            Assert.Contains("Supervisor Signature Date", pageText);
            Assert.Contains("Supervisor Comments & Feedback", pageText);
        }

        [Fact]
        public void SignatureAndSignatureDate_AreAlwaysTogetherOnTheSamePage()
        {
            // Test with both moderate content and long content to ensure that
            // in all circumstances, the signature and signature date are NEVER split across pages.

            var template = CreateDefaultStage1Template();

            // Case A: Standard content (1 page)
            var meeting1 = CreateSampleMeetingBooking();
            byte[] pdf1 = MeetingLogPdfGenerator.GeneratePdfBytes(meeting1, template, isSigned: true);
            using (var reader1 = new PdfReader(pdf1))
            {
                int sigPage = 0;
                int datePage = 0;
                for (int p = 1; p <= reader1.NumberOfPages; p++)
                {
                    string text = PdfTextExtractor.GetTextFromPage(reader1, p);
                    if (text.Contains("Supervisor Signature Date")) datePage = p;
                    if (text.Contains("Dr. John Supervisor")) sigPage = p;
                }
                Assert.True(sigPage > 0, "Signature should be found in PDF");
                Assert.True(datePage > 0, "Signature date should be found in PDF");
                Assert.Equal(sigPage, datePage);
            }

            // Case B: Very long meeting discussion that pushes content to page 2
            string longDiscussion = string.Join("\n\n", Enumerable.Range(1, 15).Select(i =>
                $"Discussion item {i}: Evaluated the multi-tier microservices architecture, Docker containerization, security policies, and performance test results. Detailed discussions on latency metrics and database indexing strategy."));
            string longComments = "Overall satisfactory work. Make sure all action points are strictly implemented before the next milestone.";

            var meeting2 = CreateSampleMeetingBooking(topics: longDiscussion, comments: longComments);
            byte[] pdf2 = MeetingLogPdfGenerator.GeneratePdfBytes(meeting2, template, isSigned: true);
            using (var reader2 = new PdfReader(pdf2))
            {
                Assert.True(reader2.NumberOfPages >= 2, "Expected long content to paginate across multiple pages.");

                int sigPage = 0;
                int datePage = 0;
                for (int p = 1; p <= reader2.NumberOfPages; p++)
                {
                    string text = PdfTextExtractor.GetTextFromPage(reader2, p);
                    if (text.Contains("Supervisor Signature Date")) datePage = p;
                    if (text.Contains("Dr. John Supervisor")) sigPage = p;
                }
                Assert.True(sigPage > 0, "Signature should be found in PDF");
                Assert.True(datePage > 0, "Signature date should be found in PDF");
                Assert.Equal(sigPage, datePage);
            }
        }

        [Fact]
        public void SupervisorCommentsAndSignature_StayTogetherOnPage2_WhenContentOverflowsPage1()
        {
            var template = CreateDefaultStage1Template();

            // Create notes that take up most of page 1, forcing signature & comments block to page 2
            string moderateLongDiscussion = string.Join("\n\n", Enumerable.Range(1, 14).Select(i =>
                $"Point {i}: Thorough analysis of the machine learning pipeline, dataset cleaning, data augmentation, hyperparameter tuning, and cross-validation accuracy metrics."));
            string comments = "Detailed feedback: Outstanding diligence in following up on previous feedback. Ready for milestone defense.";

            var meeting = CreateSampleMeetingBooking(topics: moderateLongDiscussion, comments: comments);
            byte[] pdfBytes = MeetingLogPdfGenerator.GeneratePdfBytes(meeting, template, isSigned: true);

            using var reader = new PdfReader(pdfBytes);
            Assert.True(reader.NumberOfPages >= 2, "Should paginate to 2 pages.");

            // Both signature and comments must be together on page 2
            string page2Text = PdfTextExtractor.GetTextFromPage(reader, 2);
            Assert.Contains("Supervisor Signature", page2Text);
            Assert.Contains("Supervisor Signature Date", page2Text);
            Assert.Contains("Supervisor Comments & Feedback", page2Text);
            Assert.Contains("Outstanding diligence", page2Text);
        }

        [Fact]
        public void LongSupervisorFeedback_RendersWithoutClippingOrError()
        {
            var template = CreateDefaultStage1Template();

            // Long comments of 20 paragraphs
            string longFeedback = string.Join("\n\n", Enumerable.Range(1, 20).Select(i =>
                $"Paragraph {i}: Supervisor detailed feedback notes regarding implementation quality, adherence to academic standards, robustness of software testing, and literature review thoroughness. All requirements have been reviewed with the student."));

            var meeting = CreateSampleMeetingBooking(comments: longFeedback);

            byte[] pdfBytes = MeetingLogPdfGenerator.GeneratePdfBytes(meeting, template, isSigned: true);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var reader = new PdfReader(pdfBytes);
            Assert.True(reader.NumberOfPages >= 2);

            // Verify the content is present and not lost
            bool foundFirstPara = false;
            bool foundLastPara = false;
            for (int p = 1; p <= reader.NumberOfPages; p++)
            {
                string text = PdfTextExtractor.GetTextFromPage(reader, p);
                if (text.Contains("Paragraph 1:")) foundFirstPara = true;
                if (text.Contains("Paragraph 20:")) foundLastPara = true;
            }

            Assert.True(foundFirstPara, "First paragraph of long feedback should be present in PDF");
            Assert.True(foundLastPara, "Last paragraph of long feedback should be present in PDF");
        }

        [Fact]
        public void Base64ImageSignature_RendersSuccessfullyInPdf()
        {
            var template = CreateDefaultStage1Template();

            // A valid 1x1 transparent PNG data URL
            string pngBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
            var meeting = CreateSampleMeetingBooking(signature: pngBase64);

            byte[] pdfBytes = MeetingLogPdfGenerator.GeneratePdfBytes(meeting, template, isSigned: true);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var reader = new PdfReader(pdfBytes);
            Assert.Equal(1, reader.NumberOfPages);
            string text = PdfTextExtractor.GetTextFromPage(reader, 1);
            Assert.Contains("Supervisor Signature Date", text);
            Assert.Contains("23 Oct 2026", text);
        }
    }
}
