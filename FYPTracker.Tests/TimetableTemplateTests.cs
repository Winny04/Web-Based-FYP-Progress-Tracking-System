using Xunit;
using Microsoft.AspNetCore.Mvc;
using FYPTracker.Controllers;
using System.Text;

namespace FYPTracker.Tests
{
    public class TimetableTemplateTests
    {
        [Fact]
        public void DownloadTemplate_ReturnsRealCsvFile_WithExactFormatAndEditableExampleRows()
        {
            // Arrange
            var controller = new TimetableController(null!, null!);

            // Act
            var result = controller.DownloadTemplate();

            // Assert
            var fileResult = Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", fileResult.ContentType);
            Assert.Equal("TeachingTimetable_Template.csv", fileResult.FileDownloadName);

            var content = Encoding.UTF8.GetString(fileResult.FileContents);
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);

            Assert.True(lines.Length >= 6, "CSV template should contain at least a header and 5 weekday example rows.");
            Assert.Equal("DayOfWeek,StartTime,EndTime,Details", lines[0]);
            Assert.Equal("Monday", lines[1].Split(',')[0].Trim());
            Assert.Equal("Tuesday", lines[2].Split(',')[0].Trim());
            Assert.Equal("Wednesday", lines[3].Split(',')[0].Trim());
            Assert.Equal("Thursday", lines[4].Split(',')[0].Trim());
            Assert.Equal("Friday", lines[5].Split(',')[0].Trim());
        }
    }
}
