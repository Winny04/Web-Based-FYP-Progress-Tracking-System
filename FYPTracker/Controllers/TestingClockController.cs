using System;
using FYPTracker.Helpers;
using FYPTracker.Services.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FYPTracker.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class TestingClockController : Controller
    {
        private readonly ISystemClock _clock;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public TestingClockController(ISystemClock clock, IWebHostEnvironment env, IConfiguration config)
        {
            _clock = clock;
            _env = env;
            _config = config;
        }

        private bool IsPermitted => TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _config);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetSimulatedTime(DateTime targetDate, string? targetTime, string? returnUrl)
        {
            if (!IsPermitted)
            {
                return Forbid();
            }

            TimeSpan time = TimeSpan.Zero;
            if (!string.IsNullOrWhiteSpace(targetTime) && TimeSpan.TryParse(targetTime, out var parsedTime))
            {
                time = parsedTime;
            }

            var newNow = targetDate.Date.Add(time);

            if (_clock is TestingSystemClock testingClock)
            {
                testingClock.SetTime(newNow);
                TempData["Success"] = $"System Clock set to simulated time: {newNow:dd MMM yyyy, h:mm tt}.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdvanceDays(int days, string? returnUrl)
        {
            if (!IsPermitted)
            {
                return Forbid();
            }

            if (_clock is TestingSystemClock testingClock)
            {
                testingClock.Advance(TimeSpan.FromDays(days));
                TempData["Success"] = $"Simulated Clock advanced by {days} day(s). Current time: {_clock.Now:dd MMM yyyy, h:mm tt}.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetToRealTime(string? returnUrl)
        {
            if (!IsPermitted)
            {
                return Forbid();
            }

            if (_clock is TestingSystemClock testingClock)
            {
                testingClock.Reset();
                TempData["Success"] = "Simulated clock has been reset to actual Windows system time.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Admin");
        }

        [HttpGet]
        public IActionResult GetStatus()
        {
            if (!IsPermitted)
            {
                return Forbid();
            }

            return Json(new
            {
                isSimulated = _clock.IsSimulated,
                currentNow = _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                displayNow = _clock.Now.ToString("dd MMM yyyy, h:mm tt"),
                today = _clock.Today.ToString("yyyy-MM-dd")
            });
        }
    }
}
