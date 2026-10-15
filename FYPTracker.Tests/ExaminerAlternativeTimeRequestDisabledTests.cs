
using Xunit;
using Microsoft.AspNetCore.Mvc;
using FYPTracker.Controllers;

public class ExaminerAlternativeTimeRequestDisabledTests
{

    [Fact]
    public void SubmitSuggestion_WhenFeatureDisabled_Returns503()
    {
        // Direct Controller unit test. No database connection.
        var controller = new ExaminerController(
            null!,
            null!,
            null!,
            null!);

        var result = controller.SubmitIAFAlternativeTimeSuggestion(
            requestId: 1,
            suggestedDate: new DateTime(2026, 11, 10),
            suggestedStartTime: new TimeSpan(10, 0, 0),
            lecturerNotes: "Unit test only");

        var response = Assert.IsType<ObjectResult>(result);

        Assert.Equal(503, response.StatusCode);
    }

}
