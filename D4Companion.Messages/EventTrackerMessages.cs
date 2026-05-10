using CommunityToolkit.Mvvm.Messaging.Messages;
using D4Companion.Entities;

namespace D4Companion.Messages
{
    public class EventTrackerAlertMessage : ValueChangedMessage<EventTrackerAlertMessageParams>
    {
        public EventTrackerAlertMessage(EventTrackerAlertMessageParams value) : base(value) { }
    }

    public class EventTrackerAlertMessageParams
    {
        public EventType EventType { get; set; }
        public string EventName { get; set; } = string.Empty;
        public TimeSpan TimeRemaining { get; set; }
    }
}
