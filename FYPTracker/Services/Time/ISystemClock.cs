using System;

namespace FYPTracker.Services.Time
{
    public interface ISystemClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
        DateTime Today { get; }
        bool IsSimulated { get; }
    }
}
