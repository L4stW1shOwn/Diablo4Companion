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
