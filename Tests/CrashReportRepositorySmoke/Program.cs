using Microsoft.Extensions.Configuration;

var directory = Path.Combine(Path.GetTempPath(), "bumper-crash-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "reports.json");
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Storage:CrashReportPath"] = path
    }).Build();
    var now = DateTimeOffset.UtcNow;
    var repository = new CrashReportRepository(config);
    if (!repository.Record("player", new string('a', 64), "1.0", "WindowsPlayer", now)) throw new Exception("first report was not inserted");
    if (repository.Record("player", new string('a', 64), "1.0", "WindowsPlayer", now.AddMinutes(1))) throw new Exception("duplicate report was inserted");
    if (repository.Snapshot().Count != 1 || repository.Snapshot()[0].Occurrences != 2) throw new Exception("duplicate was not coalesced");

    var reloaded = new CrashReportRepository(config);
    if (reloaded.Snapshot().Count != 1 || reloaded.Snapshot()[0].Occurrences != 2) throw new Exception("coalesced report did not persist");
    if (!reloaded.Record("player", new string('a', 64), "1.0", "WindowsPlayer", now.AddDays(31))) throw new Exception("expired report was not replaced");
    if (reloaded.Snapshot().Count != 1 || reloaded.Snapshot()[0].FirstSeenUtc != now.AddDays(31)) throw new Exception("expired entry was not replaced");

    for (var index = 0; index < CrashReportRepository.MaxRetainedReports + 5; index++)
        reloaded.Record("player", index.ToString("x64"), "1.0", "WindowsPlayer", now.AddDays(32).AddSeconds(index));
    if (reloaded.Snapshot().Count != CrashReportRepository.MaxRetainedReports) throw new Exception("retention bound was exceeded");
    Console.WriteLine("Crash report repository dedupe, expiry, persistence, and bounded retention passed.");
}
finally
{
    Directory.Delete(directory, true);
}
