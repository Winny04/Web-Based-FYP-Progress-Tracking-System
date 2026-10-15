
using Xunit;
using Microsoft.AspNetCore.Mvc;
using FYPTracker.Controllers;

public class AdminAlternativeTimeRequestDisabledTests
{


    [Fact]
    public void CancelRequest_WhenFeatureDisabled_Returns503()
    {
        // Direct Controller unit test.
        // No database connection or service invocation.
        var controller = new AdminController(
            context: null!,
            emailService: null!,
            hubContext: null!);

        var result = controller.CancelIAFAlternativeTimeRequest(
            requestId: 1,
            reason: "Unit test cancellation");

        var response = Assert.IsType<ObjectResult>(result);

        Assert.Equal(503, response.StatusCode);
    }


}
