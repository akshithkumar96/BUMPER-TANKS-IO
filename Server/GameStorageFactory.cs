using Microsoft.EntityFrameworkCore;

public sealed record GameStores(IProfileRepository Profiles, IPlayerIdentityRepository Identities,
    ITelemetryRepository Telemetry, ICrashReportRepository CrashReports);

public static class GameStorageFactory
{
    public static GameStores Create(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredProvider = configuration["Storage:Provider"]?.Trim();
        var provider = string.IsNullOrEmpty(configuredProvider)
            ? environment.IsDevelopment() ? "Json" : "PostgreSQL"
            : configuredProvider;

        if (string.Equals(provider, "Json", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("JSON storage is Development-only. Configure Storage:Provider=PostgreSQL for persistent environments.");
            return new GameStores(new ProfileRepository(configuration), new PlayerIdentityRepository(configuration),
                new TelemetryRepository(configuration), new CrashReportRepository(configuration));
        }

        if (!string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported Storage:Provider '{provider}'. Use PostgreSQL, or JSON in Development.");

        var connectionString = configuration.GetConnectionString("GameDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:GameDatabase is required when PostgreSQL storage is selected.");

        var options = new DbContextOptionsBuilder<GameStorageContext>().UseNpgsql(connectionString).Options;
        using (var db = new GameStorageContext(options)) db.Database.Migrate();
        return new GameStores(new PostgresProfileRepository(options), new PostgresPlayerIdentityRepository(options),
            new PostgresTelemetryRepository(options), new PostgresCrashReportRepository(options));
    }
}
