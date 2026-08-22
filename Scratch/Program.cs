using System;
using System.IO;
using NPOI.XWPF.UserModel;

class Program
{
    static void Main()
    {
        var doc = new XWPFDocument();

        Action<string, bool, int, string, bool> addPara = (text, bold, fontSize, color, center) =>
        {
            var para = doc.CreateParagraph();
            if (center) para.Alignment = ParagraphAlignment.CENTER;
            var run = para.CreateRun();
            run.SetText(text);
            run.IsBold = bold;
            run.FontSize = fontSize;
            if (!string.IsNullOrEmpty(color)) run.SetColor(color);
            run.FontFamily = "Times New Roman";
        };

        addPara("FYP PROGRESS REPORT", true, 20, "000000", true);
        addPara("STUDENT NAME: ________________________", true, 14, "000000", true);
        addPara("STUDENT ID: ________________________", true, 14, "000000", true);
        addPara("MILESTONE: ________________________", true, 14, "000000", true);
        
        doc.CreateParagraph().CreateRun().AddBreak(BreakType.PAGE);

        addPara("1. PROGRESS SUMMARY", true, 16, "000000", false);
        addPara("Describe the tasks you have completed during this period. Include any code written, diagrams created, or research conducted...", false, 12, "000000", false);
        
        addPara("2. CHALLENGES FACED", true, 16, "000000", false);
        addPara("Explain any blockers or issues you encountered and how you resolved them...", false, 12, "000000", false);
        
        addPara("3. PLAN FOR NEXT PERIOD", true, 16, "000000", false);
        addPara("List the tasks you intend to complete before your next meeting with your supervisor...", false, 12, "000000", false);

        string outputPath = @"c:\Users\yeapz\source\repos\Web-Based-FYP-Progress-Tracking-System\FYPTracker\wwwroot\uploads\submissions\Sample_Progress_Report_V2.docx";
        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            doc.Write(fs);
        }
        Console.WriteLine("Progress Template generated successfully!");
    }
}
