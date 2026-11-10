using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortfolioCopilot.Core.Testing;

public sealed record ExpectedFinding(string Rule, string Subject, Severity Severity);

public sealed record ScenarioExpected(
    IReadOnlyDictionary<AssetClass, decimal> Allocation,
    IReadOnlyList<ExpectedFinding> Findings,
    IReadOnlyDictionary<string, string>? DirectTwins,
    decimal? ActiveMonthlyEquivalent);

public sealed record Scenario(string Name, string Description, DateOnly Today, PortfolioInput Input, ScenarioExpected Expected);

/// <summary>
/// Turns a <c>samples/scenarios/*.json</c> file (plus the instruments CSV and AMFI NAV text) into a <see cref="PortfolioInput"/>.
/// Takes file contents, not paths: the caller does the IO. Used by Core.Tests and, later, Evals.
/// </summary>
public static class ScenarioParser
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Plan ParsePlan(string json) =>
        JsonSerializer.Deserialize<Plan>(json, JsonOptions) ?? throw new FormatException("The plan JSON is empty.");

    public static Scenario Parse(string scenarioJson, string instrumentsCsv, string navText)
    {
        var file = JsonSerializer.Deserialize<ScenarioFile>(scenarioJson, JsonOptions)
                   ?? throw new FormatException("The scenario JSON is empty.");

        var navs = AmfiNavParser.Parse(navText)
            .GroupBy(n => n.Isin, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var k = file.Kite;
        var input = new PortfolioInput(
            file.Plan,
            k.Holdings.Select(h => new StockHolding(h.Tradingsymbol, h.Isin, h.Exchange,
                h.Quantity + h.T1Quantity, h.AveragePrice, h.LastPrice, h.Pnl)).ToList(),
            k.MfHoldings.Select(m => new FundHolding(m.Tradingsymbol, m.Fund, m.Folio, m.Quantity,
                m.AveragePrice, m.LastPrice, ParseDate(m.LastPriceDate), m.Pnl)).ToList(),
            k.MfSips.Select(s => new Sip(s.SipId, s.Tradingsymbol, s.Fund, s.Status, s.Frequency,
                s.InstalmentAmount, ParseDate(s.NextInstalment), s.CompletedInstalments)).ToList(),
            k.MfOrders.Select(o => new MfOrder(o.OrderId, o.Tradingsymbol, o.Fund, o.Status, o.StatusMessage ?? "",
                o.Variety, Ist.ParseKiteTimestamp(o.OrderTimestamp))).ToList(),
            MfInstrumentsCsvParser.Parse(instrumentsCsv),
            navs);

        var e = file.Expected;
        var expected = new ScenarioExpected(
            e.Allocation,
            e.Findings.Select(f => new ExpectedFinding(f.Rule, f.Subject, f.Severity)).ToList(),
            e.DirectTwins,
            e.ActiveMonthlyEquivalent);

        return new Scenario(file.Name, file.Description ?? "", DateOnly.ParseExact(file.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture), input, expected);
    }

    private static DateOnly? ParseDate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : DateOnly.ParseExact(text.Trim()[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Kite field names are snake_case in the scenario files; the rest of the file is camelCase.
    private sealed record ScenarioFile(string Name, string? Description, string Today, Plan Plan, KiteData Kite, ExpectedData Expected);

    private sealed record KiteData(
        List<KiteHolding> Holdings,
        List<KiteMfHolding> MfHoldings,
        List<KiteSip> MfSips,
        List<KiteMfOrder> MfOrders);

    private sealed record KiteHolding(
        string Tradingsymbol, string Isin, string Exchange, decimal Quantity,
        [property: JsonPropertyName("t1_quantity")] decimal T1Quantity,
        [property: JsonPropertyName("average_price")] decimal AveragePrice,
        [property: JsonPropertyName("last_price")] decimal LastPrice,
        decimal Pnl);

    private sealed record KiteMfHolding(
        string Folio, string Fund, string Tradingsymbol, decimal Quantity,
        [property: JsonPropertyName("average_price")] decimal AveragePrice,
        [property: JsonPropertyName("last_price")] decimal LastPrice,
        [property: JsonPropertyName("last_price_date")] string? LastPriceDate,
        decimal Pnl);

    private sealed record KiteSip(
        [property: JsonPropertyName("sip_id")] string SipId,
        string Tradingsymbol, string Fund, string Status, string Frequency,
        [property: JsonPropertyName("instalment_amount")] decimal InstalmentAmount,
        [property: JsonPropertyName("next_instalment")] string? NextInstalment,
        [property: JsonPropertyName("completed_instalments")] int CompletedInstalments);

    private sealed record KiteMfOrder(
        [property: JsonPropertyName("order_id")] string OrderId,
        string Tradingsymbol, string Fund, string Status,
        [property: JsonPropertyName("status_message")] string? StatusMessage,
        string Variety,
        [property: JsonPropertyName("order_timestamp")] string OrderTimestamp);

    private sealed record ExpectedData(
        Dictionary<AssetClass, decimal> Allocation,
        List<ExpectedFindingData> Findings,
        Dictionary<string, string>? DirectTwins,
        decimal? ActiveMonthlyEquivalent);

    private sealed record ExpectedFindingData(string Rule, string Subject, Severity Severity);
}
