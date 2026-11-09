public static class RequestValidation
{
    private static readonly HashSet<string> TelemetryNames = new(StringComparer.Ordinal)
    {
        "match_started",
        "match_rematch"
    };

    public static bool IsPlayerId(string? value) => IsHexId(value);
    public static bool IsMatchId(string? value) => IsHexId(value);
    public static bool IsTelemetryName(string? value) => value is not null && TelemetryNames.Contains(value);
    public static bool IsCrashFingerprint(string? value) => IsHexDigest(value);
    public static bool IsCrashBuildVersion(string? value) => IsBoundedToken(value, 64);
    public static bool IsCrashPlatform(string? value) => IsBoundedToken(value, 32);

    private static bool IsHexDigest(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
            if (!Uri.IsHexDigit(character)) return false;
        return true;
    }

    private static bool IsBoundedToken(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
        foreach (var character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '+')) return false;
        return true;
    }

    private static bool IsHexId(string? value)
    {
        if (value is null || value.Length != 32) return false;
        foreach (var character in value)
            if (!Uri.IsHexDigit(character)) return false;
        return true;
    }
}
