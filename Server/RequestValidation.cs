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

    private static bool IsHexId(string? value)
    {
        if (value is null || value.Length != 32) return false;
        foreach (var character in value)
            if (!Uri.IsHexDigit(character)) return false;
        return true;
    }
}
