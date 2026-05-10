using D4Companion.Entities;
using System;

namespace D4Companion.Interfaces
{
    public interface IEventTrackerService
    {
        EventTrackerData CurrentData { get; }
        bool IsDataStale { get; }

        TimeSpan GetTimeRemaining(EventType eventType);
        string GetDisplayText(EventType eventType);
    }
}
