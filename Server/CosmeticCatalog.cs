public sealed record CosmeticDefinition(string Id, string Category, int Price);

public static class CosmeticCatalog
{
    private static readonly Dictionary<string, CosmeticDefinition> Items = new(StringComparer.Ordinal)
    {
        ["flag_checkered"] = new("flag_checkered", "flag", 20),
        ["flag_comet"] = new("flag_comet", "flag", 40),
        ["sound_arcade"] = new("sound_arcade", "sound", 20),
        ["sound_laser"] = new("sound_laser", "sound", 40)
    };

    public static IReadOnlyCollection<CosmeticDefinition> All => Items.Values
        .OrderBy(item => item.Category, StringComparer.Ordinal)
        .ThenBy(item => item.Price)
        .ThenBy(item => item.Id, StringComparer.Ordinal)
        .ToArray();

    public static bool TryGet(string? id, out CosmeticDefinition definition)
    {
        if (id is not null && Items.TryGetValue(id, out var item))
        {
            definition = item;
            return true;
        }
        definition = null!;
        return false;
    }
}
