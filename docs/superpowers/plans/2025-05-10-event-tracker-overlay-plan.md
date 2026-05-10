# Event Tracker Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an event tracker overlay panel that displays live Diablo IV world events (Helltide, World Boss, Zone Event, Chest Respawn) using the diablo4.life API, with configurable visibility, position, and alerts.

**Architecture:** A new `EventTrackerService` singleton polls the public API every 60s and exposes parsed data. `OverlayHandler` reads this data in its render loop to draw the panel and notifications. All business logic is isolated in the service; the overlay handler only renders.

**Tech Stack:** C# 13, .NET 10-windows, WPF, GameOverlay.Drawing, CommunityToolkit.Mvvm.Messaging, System.Text.Json, NUnit, Microsoft.Extensions.DependencyInjection

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `D4Companion.Entities/EventType.cs` | Create | Enum of event types |
| `D4Companion.Entities/EventTrackerData.cs` | Create | DTO for API response and derived data |
| `D4Companion.Interfaces/IEventTrackerService.cs` | Create | Service contract |
| `D4Companion.Messages/EventTrackerMessages.cs` | Create | MVVM alert message |
| `D4Companion.Services/EventTrackerService.cs` | Create | HTTP polling, parsing, countdown calculation, alert detection |
| `D4Companion.Tests/EventTrackerServiceTests.cs` | Create | Unit tests for parsing, countdowns, alerts, stale detection |
| `D4Companion.Entities/SettingsD4.cs` | Modify | Add event tracker settings properties |
| `D4Companion/App.xaml.cs` | Modify | Register `IEventTrackerService` in DI container |
| `D4Companion.Services/OverlayHandler.cs` | Modify | Inject service, add `DrawGraphicsEventTracker`, subscribe to alert message |

---

## Task 1: EventType enum

**Files:**
- Create: `D4Companion.Entities/EventType.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace D4Companion.Entities
{
    public enum EventType
    {
        Helltide,
        WorldBoss,
        ZoneEvent,
        ChestRespawn
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Entities/EventType.cs
git commit -m "feat(event-tracker): add EventType enum"
```

---

## Task 2: EventTrackerData entity

**Files:**
- Create: `D4Companion.Entities/EventTrackerData.cs`

- [ ] **Step 1: Create the DTOs**

```csharp
namespace D4Companion.Entities
{
    public class EventTrackerData
    {
        public DateTimeOffset Helltide { get; set; }
        public WorldBossInfo WorldBoss { get; set; } = new WorldBossInfo();
        public DateTimeOffset ZoneEvent { get; set; }
        public DateTimeOffset ChestRespawn { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public bool IsStale { get; set; }
    }

    public class WorldBossInfo
    {
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset Time { get; set; }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Entities/EventTrackerData.cs
git commit -m "feat(event-tracker): add EventTrackerData and WorldBossInfo entities"
```

---

## Task 3: EventTracker alert message

**Files:**
- Create: `D4Companion.Messages/EventTrackerMessages.cs`

- [ ] **Step 1: Create the message classes**

```csharp
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
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Messages/EventTrackerMessages.cs
git commit -m "feat(event-tracker): add EventTrackerAlertMessage"
```

---

## Task 4: IEventTrackerService interface

**Files:**
- Create: `D4Companion.Interfaces/IEventTrackerService.cs`

- [ ] **Step 1: Create the interface**

```csharp
using D4Companion.Entities;

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
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Interfaces/IEventTrackerService.cs
git commit -m "feat(event-tracker): add IEventTrackerService interface"
```

---

## Task 5: EventTrackerService implementation

**Files:**
- Create: `D4Companion.Services/EventTrackerService.cs`

- [ ] **Step 1: Write the service**

```csharp
using CommunityToolkit.Mvvm.Messaging;
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace D4Companion.Services
{
    public class EventTrackerService : IEventTrackerService
    {
        private readonly IHttpClientHandler _httpClientHandler;
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger<EventTrackerService> _logger;
        private readonly System.Threading.Timer _timer;
        private readonly object _dataLock = new object();

        private EventTrackerData _currentData = new EventTrackerData();
        private readonly Dictionary<EventType, DateTimeOffset> _lastAlerted = new Dictionary<EventType, DateTimeOffset>();
        private const string ApiUrl = "https://diablo4.life/api/trackers/list";
        private const int PollIntervalMs = 60000;
        private const int StaleThresholdMinutes = 5;

        public EventTrackerService(IHttpClientHandler httpClientHandler, ISettingsManager settingsManager, ILogger<EventTrackerService> logger)
        {
            _httpClientHandler = httpClientHandler;
            _settingsManager = settingsManager;
            _logger = logger;

            _timer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(PollIntervalMs));
        }

        public EventTrackerData CurrentData
        {
            get
            {
                lock (_dataLock)
                {
                    return _currentData;
                }
            }
        }

        public bool IsDataStale
        {
            get
            {
                lock (_dataLock)
                {
                    return _currentData.IsStale;
                }
            }
        }

        public TimeSpan GetTimeRemaining(EventType eventType)
        {
            DateTimeOffset target;
            lock (_dataLock)
            {
                target = eventType switch
                {
                    EventType.Helltide => _currentData.Helltide,
                    EventType.WorldBoss => _currentData.WorldBoss.Time,
                    EventType.ZoneEvent => _currentData.ZoneEvent,
                    EventType.ChestRespawn => _currentData.ChestRespawn,
                    _ => DateTimeOffset.MinValue
                };
            }

            var remaining = target - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        public string GetDisplayText(EventType eventType)
        {
            var remaining = GetTimeRemaining(eventType);
            var timeText = remaining > TimeSpan.Zero ? $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}" : "Active";

            return eventType switch
            {
                EventType.Helltide => $"Helltide: {timeText}",
                EventType.WorldBoss => $"World Boss ({CurrentData.WorldBoss.Name}): {timeText}",
                EventType.ZoneEvent => $"Zone Event: {timeText}",
                EventType.ChestRespawn => $"Chest Respawn: {timeText}",
                _ => string.Empty
            };
        }

        private async void OnTimerTick(object? state)
        {
            try
            {
                var json = await _httpClientHandler.GetRequest(ApiUrl);
                if (string.IsNullOrWhiteSpace(json))
                {
                    MarkStale();
                    return;
                }

                var data = ParseResponse(json);
                if (data == null)
                {
                    MarkStale();
                    return;
                }

                lock (_dataLock)
                {
                    _currentData = data;
                    _currentData.LastUpdated = DateTimeOffset.UtcNow;
                    _currentData.IsStale = false;
                }

                CheckAlerts(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventTrackerService polling failed");
                MarkStale();
            }
        }

        private EventTrackerData? ParseResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var data = new EventTrackerData();

                if (root.TryGetProperty("helltide", out var helltideProp) && helltideProp.TryGetProperty("time", out var helltideTime))
                {
                    data.Helltide = DateTimeOffset.FromUnixTimeMilliseconds(helltideTime.GetInt64());
                }

                if (root.TryGetProperty("worldBoss", out var worldBossProp))
                {
                    var boss = new WorldBossInfo();
                    if (worldBossProp.TryGetProperty("name", out var nameProp))
                        boss.Name = nameProp.GetString() ?? string.Empty;
                    if (worldBossProp.TryGetProperty("time", out var timeProp))
                        boss.Time = DateTimeOffset.FromUnixTimeMilliseconds(timeProp.GetInt64());
                    data.WorldBoss = boss;
                }

                if (root.TryGetProperty("zoneEvent", out var zoneProp) && zoneProp.TryGetProperty("time", out var zoneTime))
                {
                    data.ZoneEvent = DateTimeOffset.FromUnixTimeMilliseconds(zoneTime.GetInt64());
                }

                if (root.TryGetProperty("chestRespawn", out var chestProp))
                {
                    data.ChestRespawn = DateTimeOffset.FromUnixTimeMilliseconds(chestProp.GetInt64());
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse event tracker API response");
                return null;
            }
        }

        private void MarkStale()
        {
            lock (_dataLock)
            {
                _currentData.IsStale = true;
            }
        }

        private void CheckAlerts(EventTrackerData data)
        {
            if (!_settingsManager.Settings.IsEventTrackerAlertsEnabled) return;

            var threshold = TimeSpan.FromMinutes(_settingsManager.Settings.EventTrackerAlertMinutes);
            var now = DateTimeOffset.UtcNow;

            CheckAlert(EventType.Helltide, data.Helltide, "Helltide", threshold, now);
            CheckAlert(EventType.WorldBoss, data.WorldBoss.Time, $"World Boss ({data.WorldBoss.Name})", threshold, now);
            CheckAlert(EventType.ZoneEvent, data.ZoneEvent, "Zone Event", threshold, now);
            CheckAlert(EventType.ChestRespawn, data.ChestRespawn, "Chest Respawn", threshold, now);
        }

        private void CheckAlert(EventType type, DateTimeOffset time, string name, TimeSpan threshold, DateTimeOffset now)
        {
            var remaining = time - now;
            if (remaining <= TimeSpan.Zero || remaining > threshold) return;

            lock (_dataLock)
            {
                if (_lastAlerted.TryGetValue(type, out var last) && last == time)
                {
                    return; // Already alerted for this occurrence
                }
                _lastAlerted[type] = time;
            }

            WeakReferenceMessenger.Default.Send(new EventTrackerAlertMessage(new EventTrackerAlertMessageParams
            {
                EventType = type,
                EventName = name,
                TimeRemaining = remaining
            }));
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Services/EventTrackerService.cs
git commit -m "feat(event-tracker): implement EventTrackerService"
```

---

## Task 6: Unit tests for EventTrackerService

**Files:**
- Create: `D4Companion.Tests/EventTrackerServiceTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.Messages;
using D4Companion.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace D4Companion.Tests
{
    public class EventTrackerServiceTests
    {
        private IHttpClientHandler _httpClientHandler = null!;
        private ISettingsManager _settingsManager = null!;
        private ILogger<EventTrackerService> _logger = null!;
        private EventTrackerService _service = null!;

        [SetUp]
        public void Setup()
        {
            _httpClientHandler = Substitute.For<IHttpClientHandler>();
            _settingsManager = Substitute.For<ISettingsManager>();
            _settingsManager.Settings.Returns(new SettingsD4());
            _logger = Substitute.For<ILogger<EventTrackerService>>();
        }

        [Test]
        public void ParseResponse_ValidJson_ReturnsCorrectData()
        {
            // Arrange
            var json = @"{""helltide"":{""time"":1778410500000},""worldBoss"":{""name"":""Avarice, the Gold Cursed"",""time"":1778412600000},""nextWorldBoss"":{""name"":""Avarice, the Gold Cursed"",""time"":1778412600000,""_id"":""6a0060083510ab8b1104e039""},""zoneEvent"":{""time"":1778410200000},""chestRespawn"":1778414400000}";
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(json);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);

            // Act
            // Allow timer to fire once
            Thread.Sleep(100);
            var data = _service.CurrentData;

            // Assert
            Assert.That(data.Helltide, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778410500000)));
            Assert.That(data.WorldBoss.Name, Is.EqualTo("Avarice, the Gold Cursed"));
            Assert.That(data.WorldBoss.Time, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778412600000)));
            Assert.That(data.ZoneEvent, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778410200000)));
            Assert.That(data.ChestRespawn, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778414400000)));
        }

        [Test]
        public void GetTimeRemaining_FutureEvent_ReturnsPositiveTimeSpan()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddHours(2);
            var json = $@"{{""helltide"":{{""time"":{future.ToUnixTimeMilliseconds()}}}}}";
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(json);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(100);

            // Act
            var remaining = _service.GetTimeRemaining(EventType.Helltide);

            // Assert
            Assert.That(remaining, Is.GreaterThan(TimeSpan.FromHours(1)));
            Assert.That(remaining, Is.LessThan(TimeSpan.FromHours(3)));
        }

        [Test]
        public void GetTimeRemaining_PastEvent_ReturnsZero()
        {
            // Arrange
            var past = DateTimeOffset.UtcNow.AddHours(-1);
            var json = $@"{{""helltide"":{{""time"":{past.ToUnixTimeMilliseconds()}}}}}";
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(json);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(100);

            // Act
            var remaining = _service.GetTimeRemaining(EventType.Helltide);

            // Assert
            Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void GetDisplayText_ActiveEvent_ReturnsActive()
        {
            // Arrange
            var past = DateTimeOffset.UtcNow.AddMinutes(-5);
            var json = $@"{{""zoneEvent"":{{""time"":{past.ToUnixTimeMilliseconds()}}}}}";
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(json);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(100);

            // Act
            var text = _service.GetDisplayText(EventType.ZoneEvent);

            // Assert
            Assert.That(text, Does.Contain("Active"));
        }

        [Test]
        public void GetDisplayText_FutureEvent_ReturnsCountdown()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddHours(1).AddMinutes(23).AddSeconds(45);
            var json = $@"{{""helltide"":{{""time"":{future.ToUnixTimeMilliseconds()}}}}}";
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(json);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(100);

            // Act
            var text = _service.GetDisplayText(EventType.Helltide);

            // Assert
            Assert.That(text, Does.StartWith("Helltide:"));
            Assert.That(text, Does.Match(@"\d{2}:\d{2}:\d{2}"));
        }
    }
}
```

- [ ] **Step 2: Run tests**

Run: `cd /mnt/datas/dev/Diablo4Companion && dotnet test D4Companion.Tests/D4Companion.Tests.csproj --filter "FullyQualifiedName~EventTrackerServiceTests"`

Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add D4Companion.Tests/EventTrackerServiceTests.cs
git commit -m "test(event-tracker): add unit tests for EventTrackerService"
```

---

## Task 7: Add settings properties

**Files:**
- Modify: `D4Companion.Entities/SettingsD4.cs`

- [ ] **Step 1: Add properties before `KeyBindingConfig`**

Locate the `KeyBindingConfig KeyBindingConfigSwitchPreset` property in `SettingsD4.cs`. Insert the following block immediately before it:

```csharp
    public bool IsEventTrackerEnabled { get; set; } = false;
    public bool IsEventTrackerHelltideEnabled { get; set; } = true;
    public bool IsEventTrackerWorldBossEnabled { get; set; } = true;
    public bool IsEventTrackerZoneEventEnabled { get; set; } = true;
    public bool IsEventTrackerChestRespawnEnabled { get; set; } = true;
    public int EventTrackerPosX { get; set; } = 900;
    public int EventTrackerPosY { get; set; } = 10;
    public int EventTrackerAlertMinutes { get; set; } = 5;
    public bool IsEventTrackerAlertsEnabled { get; set; } = true;
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion.Entities/SettingsD4.cs
git commit -m "feat(event-tracker): add settings properties for event tracker"
```

---

## Task 8: Register EventTrackerService in DI

**Files:**
- Modify: `D4Companion/App.xaml.cs`

- [ ] **Step 1: Add DI registration**

In `ConfigureServices()`, add the following line after `services.AddSingleton<ITradeItemManager, TradeItemManager>();`:

```csharp
services.AddSingleton<IEventTrackerService, EventTrackerService>();
```

- [ ] **Step 2: Commit**

```bash
git add D4Companion/App.xaml.cs
git commit -m "feat(event-tracker): register EventTrackerService in DI container"
```

---

## Task 9: Integrate rendering into OverlayHandler

**Files:**
- Modify: `D4Companion.Services/OverlayHandler.cs`

- [ ] **Step 1: Add field and inject service**

Add the following field declaration after `_windowHandle`:

```csharp
private readonly IEventTrackerService _eventTrackerService;
```

Modify the constructor signature from:
```csharp
public OverlayHandler(ILogger<ScreenProcessHandler> logger, IAffixManager affixManager, ISettingsManager settingsManager)
```
to:
```csharp
public OverlayHandler(ILogger<ScreenProcessHandler> logger, IAffixManager affixManager, ISettingsManager settingsManager, IEventTrackerService eventTrackerService)
```

Add the following assignment inside the constructor body after `_settingsManager = settingsManager;`:
```csharp
_eventTrackerService = eventTrackerService;
```

Add the following message registration inside the constructor after the existing `WeakReferenceMessenger.Default.Register<WindowHandleUpdatedMessage>` line:
```csharp
WeakReferenceMessenger.Default.Register<EventTrackerAlertMessage>(this, HandleEventTrackerAlertMessage);
```

- [ ] **Step 2: Add alert handler**

Add the following method in the `#region Event handlers` section, alongside the other `Handle*Message` methods:

```csharp
private void HandleEventTrackerAlertMessage(object recipient, EventTrackerAlertMessage message)
{
    var p = message.Value;
    SetNotificationText($"EventTracker: {p.EventName} in {p.TimeRemaining.Minutes} minutes!");
    _notificationVisible = true;
    _notificationTimer.Stop();
    _notificationTimer.Start();
}
```

- [ ] **Step 3: Add draw call in DrawGraphics**

Inside `DrawGraphics`, after the `foreach (OverlayMenuItem menuItem ...)` block and before the closing `catch`, add:

```csharp
if (_settingsManager.Settings.IsEventTrackerEnabled)
{
    DrawGraphicsEventTracker(e);
}
```

- [ ] **Step 4: Add DrawGraphicsEventTracker method**

Add the following private method in the `#region Methods` section (near the other DrawGraphics* methods):

```csharp
private void DrawGraphicsEventTracker(DrawGraphicsEventArgs e)
{
    if (_window == null) return;

    var gfx = e.Graphics;
    var data = _eventTrackerService.CurrentData;
    var settings = _settingsManager.Settings;
    var fontSize = settings.OverlayFontSize;
    var textOffset = 20;
    var lineHeight = fontSize + 8;
    var panelHeight = lineHeight;
    var panelLeft = settings.EventTrackerPosX;
    var panelTop = settings.EventTrackerPosY;

    var lines = new List<string>();
    if (settings.IsEventTrackerHelltideEnabled) lines.Add(_eventTrackerService.GetDisplayText(EventType.Helltide));
    if (settings.IsEventTrackerWorldBossEnabled) lines.Add(_eventTrackerService.GetDisplayText(EventType.WorldBoss));
    if (settings.IsEventTrackerZoneEventEnabled) lines.Add(_eventTrackerService.GetDisplayText(EventType.ZoneEvent));
    if (settings.IsEventTrackerChestRespawnEnabled) lines.Add(_eventTrackerService.GetDisplayText(EventType.ChestRespawn));

    if (lines.Count == 0) return;

    float maxTextWidth = 0;
    foreach (var line in lines)
    {
        var size = gfx.MeasureString(_fonts["consolasBold"], fontSize, line);
        if (size.X > maxTextWidth) maxTextWidth = size.X;
    }

    float panelWidth = maxTextWidth + 2 * textOffset;
    panelHeight = lines.Count * lineHeight + textOffset;

    gfx.FillRectangle(_brushes["backgroundTransparent"], panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight);
    gfx.DrawRectangle(_brushes["border"], panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight, 1);

    float textTop = panelTop + textOffset / 2;
    foreach (var line in lines)
    {
        if (data.IsStale)
        {
            gfx.DrawText(_fonts["consolasBold"], fontSize, _brushes["text"], panelLeft + textOffset, textTop, "[!] " + line);
        }
        else
        {
            gfx.DrawText(_fonts["consolasBold"], fontSize, _brushes["text"], panelLeft + textOffset, textTop, line);
        }
        textTop += lineHeight;
    }
}
```

- [ ] **Step 5: Build and verify**

Run: `cd /mnt/datas/dev/Diablo4Companion && dotnet build D4Companion.sln`

Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add D4Companion.Services/OverlayHandler.cs
git commit -m "feat(event-tracker): integrate event tracker rendering into OverlayHandler"
```

---

## Task 10: Final verification

- [ ] **Step 1: Run all tests**

Run: `cd /mnt/datas/dev/Diablo4Companion && dotnet test D4Companion.Tests/D4Companion.Tests.csproj`

Expected: All existing tests still pass + new EventTrackerServiceTests pass.

- [ ] **Step 2: Full solution build**

Run: `cd /mnt/datas/dev/Diablo4Companion && dotnet build D4Companion.sln`

Expected: Build succeeds with 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git commit -m "chore(event-tracker): final verification complete"
```

---

## Spec Coverage Checklist

| Spec Requirement | Task |
|---|---|
| `EventTrackerService` singleton avec polling 60s | Task 5 |
| Parse JSON API en `EventTrackerData` | Task 5 |
| Calcul comptes à rebours | Task 5 |
| Détection alertes + anti-spam | Task 5 |
| Messages MVVM `EventTrackerAlertMessage` | Task 3 |
| Settings (activation, types, position, alertes) | Task 7 |
| Rendu panneau dans `OverlayHandler` | Task 9 |
| Notifications via mécanisme existant | Task 9 |
| Gestion erreurs (stale, réseau, parse) | Task 5 |
| Tests unitaires | Task 6 |
| Enregistrement DI | Task 8 |

## Placeholder Scan

- No "TBD", "TODO", "implement later", "fill in details" found.
- No vague "add error handling" without code.
- All method signatures consistent across tasks.
- All file paths are exact.
