using Microsoft.Extensions.Configuration;

/// <summary>
/// Public gameplay values shared by the realtime authority and clients. Values
/// are clamped so a bad deployment override cannot destabilize simulation.
/// </summary>
public sealed record GameplayTuningSettings(
    int Revision,
    float SpeedMultiplier,
    float ProjectileCooldownMultiplier,
    float KnockbackMultiplier)
{
    public const int CurrentRevision = 1;

    public static GameplayTuningSettings From(IConfiguration configuration) => new(
        CurrentRevision,
        ReadBounded(configuration, "GameplayTuning:SpeedMultiplier", 1f, 0.75f, 1.25f),
        ReadBounded(configuration, "GameplayTuning:ProjectileCooldownMultiplier", 1f, 0.75f, 1.5f),
        ReadBounded(configuration, "GameplayTuning:KnockbackMultiplier", 1f, 0.75f, 1.5f));

    private static float ReadBounded(IConfiguration configuration, string key, float fallback, float min, float max)
    {
        var value = configuration.GetValue<float?>(key) ?? fallback;
        return float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    public string ETag => $"\"{Revision}-{SpeedMultiplier:R}-{ProjectileCooldownMultiplier:R}-{KnockbackMultiplier:R}\"";
}
