﻿using Prism.Events;
using System.Drawing;
using Windows.Win32.Foundation;

namespace D4Companion.Events
{
    public class MouseUpdatedEvent : PubSubEvent<MouseUpdatedEventParams>
    {

    }

    public class MouseUpdatedEventParams
    {
        public int CoordsMouseX { get; set; }
        public int CoordsMouseY { get; set; }
    }

    public class ScreenCaptureReadyEvent : PubSubEvent<ScreenCaptureReadyEventParams>
    {
    }

    public class ScreenCaptureReadyEventParams
    {
        public Bitmap? CurrentScreen { get; set; }
    }

    public class TakeScreenshotRequestedEvent : PubSubEvent
    {

    }

    public class WindowHandleUpdatedEvent : PubSubEvent<WindowHandleUpdatedEventParams>
    {

    }

    public class WindowHandleUpdatedEventParams
    {
        public HWND WindowHandle { get; set; }
    }
}
