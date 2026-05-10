using CommunityToolkit.Mvvm.Messaging;
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace D4Companion.Services
{
    public class EventTrackerService : IEventTrackerService, IDisposable
    {
        private readonly IHttpClientHandler _httpClientHandler;
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger<EventTrackerService> _logger;
        private readonly System.Threading.Timer _timer;
        private readonly SemaphoreSlim _timerSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _dataLock = new object();

        private EventTrackerData _currentData = new EventTrackerData();
        private readonly Dictionary<EventType, DateTimeOffset> _lastAlerted = new Dictionary<EventType, DateTimeOffset>();
        private const string ApiUrl = "https://diablo4.life/api/trackers/list";
        private const int PollIntervalMs = 60000;
        private const int StaleThresholdMinutes = 5;
        private const int MaxResponseLength = 1024 * 1024; // 1 MB
        private static readonly DateTimeOffset MinValidTimestamp = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset MaxValidTimestamp = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private bool _disposed;

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
                    return new EventTrackerData
                    {
                        Helltide = _currentData.Helltide,
                        WorldBoss = new WorldBossInfo
                        {
                            Name = _currentData.WorldBoss.Name,
                            Time = _currentData.WorldBoss.Time
                        },
                        ZoneEvent = _currentData.ZoneEvent,
                        ChestRespawn = _currentData.ChestRespawn,
                        LastUpdated = _currentData.LastUpdated,
                        IsStale = _currentData.IsStale
                    };
                }
            }
        }

        public bool IsDataStale
        {
            get
            {
                lock (_dataLock)
                {
                    return _currentData.IsStale ||
                           (DateTimeOffset.UtcNow - _currentData.LastUpdated) > TimeSpan.FromMinutes(StaleThresholdMinutes);
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
            DateTimeOffset target;
            string worldBossName;

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
                worldBossName = _currentData.WorldBoss.Name;
            }

            var remaining = target - DateTimeOffset.UtcNow;
            remaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            var timeText = remaining > TimeSpan.Zero ? $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}" : "Active";

            return eventType switch
            {
                EventType.Helltide => $"Helltide: {timeText}",
                EventType.WorldBoss => $"World Boss ({worldBossName}): {timeText}",
                EventType.ZoneEvent => $"Zone Event: {timeText}",
                EventType.ChestRespawn => $"Chest Respawn: {timeText}",
                _ => string.Empty
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timerSemaphore?.Dispose();
        }

        private void OnTimerTick(object? state)
        {
            _ = Task.Run(async () =>
            {
                if (!await _timerSemaphore.WaitAsync(0))
                {
                    _logger.LogWarning("EventTrackerService timer tick skipped — previous poll still in progress");
                    return;
                }

                try
                {
                    await PollAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EventTrackerService polling failed");
                    MarkStale();
                }
                finally
                {
                    _timerSemaphore.Release();
                }
            });
        }

        private async Task PollAsync()
        {
            // Note: IHttpClientHandler.GetRequest does not accept a CancellationToken,
            // so request timeout cannot be controlled here. Consider extending the interface.
            var json = await _httpClientHandler.GetRequest(ApiUrl);

            if (json.Length > MaxResponseLength)
            {
                _logger.LogError("Event tracker API response exceeded maximum allowed size ({MaxLength} bytes). Received {ActualLength} bytes.", MaxResponseLength, json.Length);
                MarkStale();
                return;
            }

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

        private EventTrackerData? ParseResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var data = new EventTrackerData();

                if (root.TryGetProperty("helltide", out var helltideProp) && helltideProp.TryGetProperty("time", out var helltideTime))
                {
                    var ts = DateTimeOffset.FromUnixTimeMilliseconds(helltideTime.GetInt64());
                    if (IsValidTimestamp(ts))
                        data.Helltide = ts;
                    else
                        _logger.LogWarning("Event tracker API returned invalid helltide timestamp: {Timestamp}", ts);
                }

                if (root.TryGetProperty("worldBoss", out var worldBossProp))
                {
                    var boss = new WorldBossInfo();
                    if (worldBossProp.TryGetProperty("name", out var nameProp))
                        boss.Name = nameProp.GetString() ?? string.Empty;
                    if (worldBossProp.TryGetProperty("time", out var timeProp))
                    {
                        var ts = DateTimeOffset.FromUnixTimeMilliseconds(timeProp.GetInt64());
                        if (IsValidTimestamp(ts))
                            boss.Time = ts;
                        else
                            _logger.LogWarning("Event tracker API returned invalid worldBoss timestamp: {Timestamp}", ts);
                    }
                    data.WorldBoss = boss;
                }

                if (root.TryGetProperty("zoneEvent", out var zoneProp) && zoneProp.TryGetProperty("time", out var zoneTime))
                {
                    var ts = DateTimeOffset.FromUnixTimeMilliseconds(zoneTime.GetInt64());
                    if (IsValidTimestamp(ts))
                        data.ZoneEvent = ts;
                    else
                        _logger.LogWarning("Event tracker API returned invalid zoneEvent timestamp: {Timestamp}", ts);
                }

                if (root.TryGetProperty("chestRespawn", out var chestProp))
                {
                    var ts = DateTimeOffset.FromUnixTimeMilliseconds(chestProp.GetInt64());
                    if (IsValidTimestamp(ts))
                        data.ChestRespawn = ts;
                    else
                        _logger.LogWarning("Event tracker API returned invalid chestRespawn timestamp: {Timestamp}", ts);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse event tracker API response");
                return null;
            }
        }

        private static bool IsValidTimestamp(DateTimeOffset value) => value >= MinValidTimestamp && value <= MaxValidTimestamp;

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
