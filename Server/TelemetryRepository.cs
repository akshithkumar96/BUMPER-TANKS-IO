using System.Collections.Concurrent;
using System.Text.Json;

public sealed class TelemetryRepository
{
    private const int MaxRetainedEvents = 10000;
    private readonly ConcurrentQueue<TelemetryEvent> events = new();
    private readonly string path;
    private readonly Lock writeLock = new();

    public TelemetryRepository(IConfiguration configuration)
    {
        path = configuration["Storage:TelemetryPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "telemetry.json");
        if (!File.Exists(path)) return;
        var stored = JsonSerializer.Deserialize<TelemetryEvent[]>(File.ReadAllText(path)) ?? Array.Empty<TelemetryEvent>();
        foreach (var item in stored.TakeLast(MaxRetainedEvents)) events.Enqueue(item);
    }

    public void Track(TelemetryEvent telemetryEvent)
    {
        events.Enqueue(telemetryEvent with { OccurredUtc = DateTimeOffset.UtcNow });
        while (events.Count > MaxRetainedEvents) events.TryDequeue(out _);
        lock (writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(events.ToArray()));
            File.Move(temporary, path, true);
        }
    }
}

public sealed record TelemetryEvent(string Name, string PlayerId, DateTimeOffset OccurredUtc);
