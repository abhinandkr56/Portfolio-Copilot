using Microsoft.Extensions.Time.Testing;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Core.Tests;

/// <summary>Reads the fixtures copied from <c>samples/</c> into the test output.</summary>
public static class Samples
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "samples");

    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Dir, .. parts]));

    public static string InstrumentsCsv => Read("kite", "mf-instruments.csv");

    public static string NavText => Read("amfi", "NAVAll.sample.txt");

    public static IReadOnlyDictionary<string, MfInstrument> Instruments => MfInstrumentsCsvParser.Parse(InstrumentsCsv);

    public static IEnumerable<string> ScenarioNames =>
        Directory.GetFiles(Path.Combine(Dir, "scenarios"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n)!;

    public static Scenario Scenario(string name) =>
        ScenarioParser.Parse(Read("scenarios", name + ".json"), InstrumentsCsv, NavText);
}

public static class Time
{
    /// <summary>"Today" at 12:00 IST, the reference time for the scenarios.</summary>
    public static FakeTimeProvider At(DateOnly todayIst) =>
        new(new DateTimeOffset(todayIst.ToDateTime(new TimeOnly(12, 0)), Ist.Offset));

    public static FakeTimeProvider Default => At(new DateOnly(2026, 11, 10));
}

/// <summary>Small builders so unit tests can state only what matters to them.</summary>
public static class Build
{
    public static Plan Plan(
        decimal equity = 70, decimal debt = 20, decimal gold = 10, decimal other = 0,
        decimal band = 5, decimal stockCap = 10, decimal fundCap = 25, bool preferDirect = true,
        decimal sipBudget = 40000, decimal hybridEquity = 65, Dictionary<string, AssetClass>? overrides = null)
    {
        var targets = new Dictionary<AssetClass, decimal> { [AssetClass.Equity] = equity, [AssetClass.Debt] = debt, [AssetClass.Gold] = gold };
        if (other > 0) targets[AssetClass.Other] = other;
        return new Plan(targets, band, stockCap, fundCap, preferDirect, sipBudget, hybridEquity,
            overrides ?? new Dictionary<string, AssetClass> { ["GOLDBEES"] = AssetClass.Gold }, null);
    }

    public static StockHolding Stock(string symbol, decimal value, string? isin = null) =>
        new(symbol, isin ?? "INE" + symbol, "NSE", 1, value, value, 0);

    public static FundHolding Fund(string isin, string name, decimal value, DateOnly? priceDate = null) =>
        new(isin, name, "FOLIO" + isin, 1, value, value, priceDate ?? new DateOnly(2026, 11, 7), 0);

    public static Sip Sip(string fund, decimal amount, string frequency = "monthly", string status = "ACTIVE", string isin = "INFTEST00001") =>
        new("SIP" + fund, isin, fund, status, frequency, amount, null, 0);

    public static MfOrder Order(string fund, DateTimeOffset at, string status = "REJECTED", string variety = "sip", string isin = "INFTEST00001") =>
        new("ORD" + at.Ticks, isin, fund, status, "SIP: Insufficient balance.", variety, at);

    public static PortfolioInput Input(Plan? plan = null, IEnumerable<StockHolding>? stocks = null, IEnumerable<FundHolding>? funds = null,
        IEnumerable<Sip>? sips = null, IEnumerable<MfOrder>? orders = null,
        IReadOnlyDictionary<string, MfInstrument>? instruments = null, IReadOnlyDictionary<string, NavRecord>? navs = null) =>
        new(plan ?? Plan(), (stocks ?? []).ToList(), (funds ?? []).ToList(), (sips ?? []).ToList(), (orders ?? []).ToList(),
            instruments ?? Samples.Instruments, navs ?? new Dictionary<string, NavRecord>());
}
