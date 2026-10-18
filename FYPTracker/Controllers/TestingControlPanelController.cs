using System;
using System.Threading.Tasks;
using FYPTracker.Helpers;
using FYPTracker.Services.Testing;
using FYPTracker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FYPTracker.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TestingControlPanelController : Controller
    {
        private readonly ITestingModePolicy _testingModePolicy;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public TestingControlPanelController(
            ITestingModePolicy testingModePolicy,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _testingModePolicy = testingModePolicy;
            _env = env;
            _config = config;
        }

        private bool IsGuardedTestingEnvironment()
        {
            return TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _config);
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!IsGuardedTestingEnvironment())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var settings = await _testingModePolicy.GetSettingsAsync();
            return View("~/Views/Admin/TestingControlPanel.cshtml", settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSettings(TestingRelaxedModeSettingsVM model)
        {
            if (!IsGuardedTestingEnvironment())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            try
            {
                await _testingModePolicy.SaveSettingsAsync(model);
                TempData["SuccessMessage"] = model.IsRelaxedModeEnabled
                    ? "Relaxed Manual Testing Mode settings successfully updated and activated."
                    : "Relaxed Manual Testing Mode is now DISABLED. Standard validation restored.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to update testing settings: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
