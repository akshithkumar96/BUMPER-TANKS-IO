using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>Issues opaque bearer credentials and persists only their SHA-256 hashes.</summary>
public sealed class PlayerIdentityRepository : IPlayerIdentityRepository
{
    private readonly string path;
    private readonly ConcurrentDictionary<string, string> playersByTokenHash;
    private readonly object saveLock = new();

    public PlayerIdentityRepository(IConfiguration configuration)
    {
        path = configuration["Storage:IdentityPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "identities.json");
        if (File.Exists(path))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
                playersByTokenHash = new ConcurrentDictionary<string, string>(entries, StringComparer.Ordinal);
            }
            catch (JsonException) { playersByTokenHash = new ConcurrentDictionary<string, string>(StringComparer.Ordinal); }
        }
        else playersByTokenHash = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
    }

    public (string PlayerId, string AccessToken) CreateAnonymous()
    {
        var playerId = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        playersByTokenHash[Hash(token)] = playerId;
        Save();
        return (playerId, token);
    }

    public bool TryAuthenticate(string? token, out string playerId)
    {
        playerId = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64) return false;
        return playersByTokenHash.TryGetValue(Hash(token), out playerId!);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private void Save()
    {
        lock (saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(playersByTokenHash));
            File.Move(temporaryPath, path, true);
        }
    }
}
