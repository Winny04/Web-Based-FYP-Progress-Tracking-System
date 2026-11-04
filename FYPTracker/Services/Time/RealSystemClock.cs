using System;

namespace FYPTracker.Services.Time
{
    /// <summary>
    /// Default real-time system clock used in Development and Production.
    /// Strictly returns actual Windows system time and never permits simulation.
    /// </summary>
    public class RealSystemClock : ISystemClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime Today => DateTime.Today;
        public bool IsSimulated => false;
    }
}
