using System.Text.Json;

public sealed class CrashReportRepository
{
    public const int MaxRetainedReports = 1000;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly string path;
    private readonly Lock writeLock = new();
    private List<CrashReportRecord> reports;

    public CrashReportRepository(IConfiguration configuration)
    {
        path = configuration["Storage:CrashReportPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "crash-reports.json");
        reports = File.Exists(path)
            ? (JsonSerializer.Deserialize<List<CrashReportRecord>>(File.ReadAllText(path)) ?? new()).TakeLast(MaxRetainedReports).ToList()
            : new List<CrashReportRecord>();
        Prune(DateTimeOffset.UtcNow);
    }

    /// <summary>Stores only a digest and coarse build metadata. Duplicate player/digest pairs refresh their timestamp.</summary>
    public bool Record(string playerId, string fingerprint, string buildVersion, string platform, DateTimeOffset now)
    {
        lock (writeLock)
        {
            Prune(now);
            var existing = reports.FindIndex(item => item.PlayerId == playerId && item.Fingerprint == fingerprint);
            if (existing >= 0)
            {
                reports[existing] = reports[existing] with { LastSeenUtc = now, Occurrences = Math.Min(reports[existing].Occurrences + 1, int.MaxValue) };
                Persist();
                return false;
            }

            reports.Add(new CrashReportRecord(playerId, fingerprint, buildVersion, platform, now, now, 1));
            if (reports.Count > MaxRetainedReports) reports.RemoveRange(0, reports.Count - MaxRetainedReports);
            Persist();
            return true;
        }
    }

    public IReadOnlyList<CrashReportRecord> Snapshot()
    {
        lock (writeLock) return reports.ToArray();
    }

    private void Prune(DateTimeOffset now) => reports.RemoveAll(item => item.LastSeenUtc < now - Retention);

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(reports));
        File.Move(temporary, path, true);
    }
}

public sealed record CrashReportRecord(string PlayerId, string Fingerprint, string BuildVersion, string Platform,
    DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int Occurrences);
