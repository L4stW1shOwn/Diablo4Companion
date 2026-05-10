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
