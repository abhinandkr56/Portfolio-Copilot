namespace PortfolioCopilot.Core;

/// <summary>
/// Settings from the Api's <c>Classification</c> config section. Core never reads config itself.
/// </summary>
public sealed record CoreOptions(IReadOnlyDictionary<string, AssetClass> SchemeTypeMap, int NavStaleDays)
{
    public static CoreOptions Default { get; } = new(
        new Dictionary<string, AssetClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["equity"] = AssetClass.Equity,
            ["elss"] = AssetClass.Equity,
            ["debt"] = AssetClass.Debt,
            ["liquid"] = AssetClass.Debt,
            ["balanced"] = AssetClass.Hybrid,
            ["hybrid"] = AssetClass.Hybrid,
            ["fof"] = AssetClass.Other,
            ["gold"] = AssetClass.Gold,
        },
        NavStaleDays: 4);
}
