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
    [TestFixture]
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

        [TearDown]
        public void TearDown()
        {
            // Note: EventTrackerService uses a System.Threading.Timer internally
            // but does not implement IDisposable. The timer will be GC'd when
            // the service instance goes out of scope.
        }

        /// <summary>
        /// Helper: creates the service and waits for the initial timer tick to complete.
        /// The timer fires immediately (dueTime=0) in the constructor, so we must
        /// wait briefly for the async callback to finish before asserting.
        /// </summary>
        private EventTrackerService CreateServiceAndWait(string jsonResponse, int timeoutMs = 500)
        {
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(jsonResponse);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);

            // Wait for the async timer callback to complete
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            return _service;
        }

        // ── ParseResponse (via CurrentData) ──────────────────────────────

        [Test]
        public void CurrentData_ValidJson_ReturnsParsedData()
        {
            // Arrange
            var json = @"{""helltide"":{""time"":1778410500000},""worldBoss"":{""name"":""Avarice, the Gold Cursed"",""time"":1778412600000},""nextWorldBoss"":{""name"":""Avarice, the Gold Cursed"",""time"":1778412600000,""_id"":""6a0060083510ab8b1104e039""},""zoneEvent"":{""time"":1778410200000},""chestRespawn"":1778414400000}";

            // Act
            var service = CreateServiceAndWait(json);
            var data = service.CurrentData;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(data.Helltide, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778410500000)));
                Assert.That(data.WorldBoss.Name, Is.EqualTo("Avarice, the Gold Cursed"));
                Assert.That(data.WorldBoss.Time, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778412600000)));
                Assert.That(data.ZoneEvent, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778410200000)));
                Assert.That(data.ChestRespawn, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778414400000)));
                Assert.That(data.IsStale, Is.False);
            });
        }

        [Test]
        public void CurrentData_EmptyJson_ReturnsDefaultValues()
        {
            // Arrange
            var json = @"{}";

            // Act
            var service = CreateServiceAndWait(json);
            var data = service.CurrentData;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(data.Helltide, Is.EqualTo(DateTimeOffset.MinValue));
                Assert.That(data.WorldBoss.Name, Is.EqualTo(string.Empty));
                Assert.That(data.WorldBoss.Time, Is.EqualTo(DateTimeOffset.MinValue));
                Assert.That(data.ZoneEvent, Is.EqualTo(DateTimeOffset.MinValue));
                Assert.That(data.ChestRespawn, Is.EqualTo(DateTimeOffset.MinValue));
            });
        }

        [Test]
        public void CurrentData_PartialJson_OnlyParsesPresentFields()
        {
            // Arrange — only helltide present
            var json = @"{""helltide"":{""time"":1778410500000}}";

            // Act
            var service = CreateServiceAndWait(json);
            var data = service.CurrentData;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(data.Helltide, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1778410500000)));
                Assert.That(data.WorldBoss.Name, Is.EqualTo(string.Empty));
                Assert.That(data.ZoneEvent, Is.EqualTo(DateTimeOffset.MinValue));
                Assert.That(data.ChestRespawn, Is.EqualTo(DateTimeOffset.MinValue));
            });
        }

        [Test]
        public void CurrentData_NullResponse_MarksDataStale()
        {
            // Arrange
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(Task.FromResult((string)null!));
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(200);

            // Act
            var isStale = _service.IsDataStale;

            // Assert
            Assert.That(isStale, Is.True);
        }

        [Test]
        public void CurrentData_EmptyResponse_MarksDataStale()
        {
            // Arrange
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(string.Empty);
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(200);

            // Act
            var isStale = _service.IsDataStale;

            // Assert
            Assert.That(isStale, Is.True);
        }

        [Test]
        public void CurrentData_InvalidJson_MarksDataStale()
        {
            // Arrange
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns("not valid json");
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(200);

            // Act
            var isStale = _service.IsDataStale;

            // Assert
            Assert.That(isStale, Is.True);
        }

        [Test]
        public void CurrentData_HttpException_MarksDataStale()
        {
            // Arrange
            _httpClientHandler.GetRequest(Arg.Any<string>()).Returns(Task.FromException<string>(new HttpRequestException("connection refused")));
            _service = new EventTrackerService(_httpClientHandler, _settingsManager, _logger);
            Thread.Sleep(200);

            // Act
            var isStale = _service.IsDataStale;

            // Assert
            Assert.That(isStale, Is.True);
        }

        // ── GetTimeRemaining ─────────────────────────────────────────────

        [Test]
        public void GetTimeRemaining_FutureEvent_ReturnsPositiveTimeSpan()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddHours(2);
            var json = $@"{{""helltide"":{{""time"":{future.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var remaining = service.GetTimeRemaining(EventType.Helltide);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(remaining, Is.GreaterThan(TimeSpan.FromHours(1)));
                Assert.That(remaining, Is.LessThan(TimeSpan.FromHours(3)));
            });
        }

        [Test]
        public void GetTimeRemaining_PastEvent_ReturnsZero()
        {
            // Arrange
            var past = DateTimeOffset.UtcNow.AddHours(-1);
            var json = $@"{{""helltide"":{{""time"":{past.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var remaining = service.GetTimeRemaining(EventType.Helltide);

            // Assert
            Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void GetTimeRemaining_WorldBoss_ReturnsBossTime()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddMinutes(30);
            var json = $@"{{""worldBoss"":{{""name"":""Avarice"",""time"":{future.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var remaining = service.GetTimeRemaining(EventType.WorldBoss);

            // Assert
            Assert.That(remaining, Is.GreaterThan(TimeSpan.FromMinutes(29)));
            Assert.That(remaining, Is.LessThan(TimeSpan.FromMinutes(31)));
        }

        [Test]
        public void GetTimeRemaining_ZoneEvent_ReturnsCorrectTime()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddMinutes(15);
            var json = $@"{{""zoneEvent"":{{""time"":{future.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var remaining = service.GetTimeRemaining(EventType.ZoneEvent);

            // Assert
            Assert.That(remaining, Is.GreaterThan(TimeSpan.FromMinutes(14)));
            Assert.That(remaining, Is.LessThan(TimeSpan.FromMinutes(16)));
        }

        [Test]
        public void GetTimeRemaining_ChestRespawn_ReturnsCorrectTime()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddMinutes(45);
            var json = $@"{{""chestRespawn"":{future.ToUnixTimeMilliseconds()}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var remaining = service.GetTimeRemaining(EventType.ChestRespawn);

            // Assert
            Assert.That(remaining, Is.GreaterThan(TimeSpan.FromMinutes(44)));
            Assert.That(remaining, Is.LessThan(TimeSpan.FromMinutes(46)));
        }

        // ── GetDisplayText ────────────────────────────────────────────────

        [Test]
        public void GetDisplayText_ActiveEvent_ReturnsActive()
        {
            // Arrange — event time in the past means it's currently active
            var past = DateTimeOffset.UtcNow.AddMinutes(-5);
            var json = $@"{{""zoneEvent"":{{""time"":{past.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var text = service.GetDisplayText(EventType.ZoneEvent);

            // Assert
            Assert.That(text, Does.Contain("Active"));
        }

        [Test]
        public void GetDisplayText_FutureHelltide_ReturnsCountdown()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddHours(1).AddMinutes(23).AddSeconds(45);
            var json = $@"{{""helltide"":{{""time"":{future.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var text = service.GetDisplayText(EventType.Helltide);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.StartWith("Helltide:"));
                Assert.That(text, Does.Match(@"\d{2}:\d{2}:\d{2}"));
            });
        }

        [Test]
        public void GetDisplayText_WorldBoss_IncludesBossName()
        {
            // Arrange
            var future = DateTimeOffset.UtcNow.AddMinutes(30);
            var json = $@"{{""worldBoss"":{{""name"":""Avarice, the Gold Cursed"",""time"":{future.ToUnixTimeMilliseconds()}}}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var text = service.GetDisplayText(EventType.WorldBoss);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.StartWith("World Boss (Avarice, the Gold Cursed):"));
                Assert.That(text, Does.Match(@"\d{2}:\d{2}:\d{2}"));
            });
        }

        [Test]
        public void GetDisplayText_ChestRespawn_ReturnsCorrectFormat()
        {
            // Arrange
            var past = DateTimeOffset.UtcNow.AddMinutes(-2);
            var json = $@"{{""chestRespawn"":{past.ToUnixTimeMilliseconds()}}}";

            // Act
            var service = CreateServiceAndWait(json);
            var text = service.GetDisplayText(EventType.ChestRespawn);

            // Assert
            Assert.That(text, Does.StartWith("Chest Respawn:"));
            Assert.That(text, Does.Contain("Active"));
        }

        // ── Alert messaging ───────────────────────────────────────────────

        [Test]
        public void CheckAlerts_WithinThreshold_SendsAlertMessage()
        {
            // Arrange
            var settings = new SettingsD4
            {
                IsEventTrackerAlertsEnabled = true,
                EventTrackerAlertMinutes = 10
            };
            _settingsManager.Settings.Returns(settings);

            // Event 5 minutes in the future — within the 10-minute threshold
            var eventTime = DateTimeOffset.UtcNow.AddMinutes(5);
            var json = $@"{{""helltide"":{{""time"":{eventTime.ToUnixTimeMilliseconds()}}}}}";

            EventTrackerAlertMessageParams? receivedParams = null;
            WeakReferenceMessenger.Default.Register<EventTrackerAlertMessage>(this, (r, m) =>
            {
                receivedParams = m.Value;
            });

            try
            {
                // Act
                var service = CreateServiceAndWait(json);

                // Assert
                Assert.That(receivedParams, Is.Not.Null, "Expected an alert message to be sent");
                Assert.That(receivedParams!.EventType, Is.EqualTo(EventType.Helltide));
                Assert.That(receivedParams.EventName, Is.EqualTo("Helltide"));
                Assert.That(receivedParams.TimeRemaining, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(receivedParams.TimeRemaining, Is.LessThan(TimeSpan.FromMinutes(10)));
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<EventTrackerAlertMessage>(this);
            }
        }

        [Test]
        public void CheckAlerts_AlertsDisabled_DoesNotSendAlertMessage()
        {
            // Arrange
            var settings = new SettingsD4
            {
                IsEventTrackerAlertsEnabled = false,
                EventTrackerAlertMinutes = 10
            };
            _settingsManager.Settings.Returns(settings);

            var eventTime = DateTimeOffset.UtcNow.AddMinutes(5);
            var json = $@"{{""helltide"":{{""time"":{eventTime.ToUnixTimeMilliseconds()}}}}}";

            bool messageReceived = false;
            WeakReferenceMessenger.Default.Register<EventTrackerAlertMessage>(this, (r, m) =>
            {
                messageReceived = true;
            });

            try
            {
                // Act
                var service = CreateServiceAndWait(json);

                // Assert
                Assert.That(messageReceived, Is.False, "No alert should be sent when alerts are disabled");
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<EventTrackerAlertMessage>(this);
            }
        }

        [Test]
        public void CheckAlerts_EventBeyondThreshold_DoesNotSendAlertMessage()
        {
            // Arrange
            var settings = new SettingsD4
            {
                IsEventTrackerAlertsEnabled = true,
                EventTrackerAlertMinutes = 5 // 5-minute threshold
            };
            _settingsManager.Settings.Returns(settings);

            // Event 30 minutes in the future — beyond the 5-minute threshold
            var eventTime = DateTimeOffset.UtcNow.AddMinutes(30);
            var json = $@"{{""helltide"":{{""time"":{eventTime.ToUnixTimeMilliseconds()}}}}}";

            bool messageReceived = false;
            WeakReferenceMessenger.Default.Register<EventTrackerAlertMessage>(this, (r, m) =>
            {
                messageReceived = true;
            });

            try
            {
                // Act
                var service = CreateServiceAndWait(json);

                // Assert
                Assert.That(messageReceived, Is.False, "No alert should be sent for events beyond the threshold");
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<EventTrackerAlertMessage>(this);
            }
        }

        [Test]
        public void CheckAlerts_PastEvent_DoesNotSendAlertMessage()
        {
            // Arrange
            var settings = new SettingsD4
            {
                IsEventTrackerAlertsEnabled = true,
                EventTrackerAlertMinutes = 10
            };
            _settingsManager.Settings.Returns(settings);

            // Event already in the past
            var eventTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            var json = $@"{{""helltide"":{{""time"":{eventTime.ToUnixTimeMilliseconds()}}}}}";

            bool messageReceived = false;
            WeakReferenceMessenger.Default.Register<EventTrackerAlertMessage>(this, (r, m) =>
            {
                messageReceived = true;
            });

            try
            {
                // Act
                var service = CreateServiceAndWait(json);

                // Assert
                Assert.That(messageReceived, Is.False, "No alert should be sent for past events");
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<EventTrackerAlertMessage>(this);
            }
        }
    }
}