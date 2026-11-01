using System.Collections.Concurrent;
using System.Text.Json;

public sealed class TelemetryRepository
{
    private readonly ConcurrentQueue<TelemetryEvent> events = new();
    private readonly string path;
    private readonly Lock writeLock = new();

    public TelemetryRepository(IConfiguration configuration)
    {
        path = configuration["Storage:TelemetryPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "telemetry.json");
    }

    public void Track(TelemetryEvent telemetryEvent)
    {
        events.Enqueue(telemetryEvent with { OccurredUtc = DateTimeOffset.UtcNow });
        lock (writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(events.ToArray()));
        }
    }
}

public sealed record TelemetryEvent(string Name, string PlayerId, DateTimeOffset OccurredUtc);
