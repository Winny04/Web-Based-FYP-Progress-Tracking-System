using System;
using FYPTracker.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FYPTracker.Services.Time
{
    /// <summary>
    /// Thread-safe Testing-only System Clock.
    /// Strictly enabled ONLY when Environment is Testing AND Database is FYPTracker_Test.
    /// All users (Admin, Student, Supervisor, Examiner) in Testing observe the exact same simulated time.
    /// In Development or Production, or when reset, it falls back strictly to real system time.
    /// </summary>
    public class TestingSystemClock : ISystemClock
    {
        private static readonly object _lock = new();
        private static DateTime? _simulatedNow = null;

        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public TestingSystemClock(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        private bool IsTestingAllowed => TestingEnvironmentGuard.IsFlexibleTestingBookingAllowed(_env, _config);

        public DateTime Now
        {
            get
            {
                if (!IsTestingAllowed) return DateTime.Now;

                lock (_lock)
                {
                    return _simulatedNow ?? DateTime.Now;
                }
            }
        }

        public DateTime UtcNow => Now.ToUniversalTime();

        public DateTime Today => Now.Date;

        public bool IsSimulated
        {
            get
            {
                if (!IsTestingAllowed) return false;

                lock (_lock)
                {
                    return _simulatedNow.HasValue;
                }
            }
        }

        /// <summary>
        /// Sets a specific simulated date and time.
        /// Throws InvalidOperationException if called outside Testing on FYPTracker_Test.
        /// </summary>
        public void SetTime(DateTime newNow)
        {
            if (!IsTestingAllowed)
            {
                throw new InvalidOperationException("SAFETY STOP: Simulated clock is strictly permitted only in Testing environment with FYPTracker_Test database.");
            }

            lock (_lock)
            {
                _simulatedNow = newNow;
            }
        }

        /// <summary>
        /// Advances the simulated time by the given duration.
        /// </summary>
        public void Advance(TimeSpan delta)
        {
            if (!IsTestingAllowed)
            {
                throw new InvalidOperationException("SAFETY STOP: Simulated clock is strictly permitted only in Testing environment with FYPTracker_Test database.");
            }

            lock (_lock)
            {
                var baseTime = _simulatedNow ?? DateTime.Now;
                _simulatedNow = baseTime.Add(delta);
            }
        }

        /// <summary>
        /// Resets the clock to real Windows system time.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _simulatedNow = null;
            }
        }
    }
}
