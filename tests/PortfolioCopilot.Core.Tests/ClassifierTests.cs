namespace PortfolioCopilot.Core.Tests;

public class ClassifierTests
{
    private static readonly Dictionary<string, NavRecord> NoNavs = new();

    private static FundHolding Fund(string isin, string name = "Some Fund") => Build.Fund(isin, name, 1000);

    private static MfInstrument Instrument(string isin, string schemeType, string plan = "direct", string name = "Some Fund") =>
        new(isin, "AMC_MF", name, "growth", schemeType, plan);

    [Fact]
    public void Override_wins_for_stocks_by_symbol_and_isin()
    {
        var plan = Build.Plan(overrides: new() { ["GOLDBEES"] = AssetClass.Gold, ["INEX00000001"] = AssetClass.Other });

        Assert.Equal(AssetClass.Gold, Classifier.ClassifyStock(Build.Stock("GOLDBEES", 1), plan));
        Assert.Equal(AssetClass.Other, Classifier.ClassifyStock(Build.Stock("XYZ", 1, "INEX00000001"), plan));
    }

    [Fact]
    public void Override_wins_over_instrument_for_funds()
    {
        var plan = Build.Plan(overrides: new() { ["INFX00000001"] = AssetClass.Debt });
        var instruments = new Dictionary<string, MfInstrument> { ["INFX00000001"] = Instrument("INFX00000001", "equity") };

        var result = Classifier.ClassifyFund(Fund("INFX00000001"), plan, instruments, NoNavs, CoreOptions.Default);

        Assert.Equal(AssetClass.Debt, result.AssetClass);
    }

    [Fact]
    public void Stock_without_override_is_equity() =>
        Assert.Equal(AssetClass.Equity, Classifier.ClassifyStock(Build.Stock("INFY", 1), Build.Plan()));

    [Theory]
    [InlineData("equity", AssetClass.Equity)]
    [InlineData("elss", AssetClass.Equity)]
    [InlineData("debt", AssetClass.Debt)]
    [InlineData("liquid", AssetClass.Debt)]
    [InlineData("balanced", AssetClass.Hybrid)]
    [InlineData("hybrid", AssetClass.Hybrid)]
    [InlineData("fof", AssetClass.Other)]
    [InlineData("gold", AssetClass.Gold)]
    [InlineData("EQUITY", AssetClass.Equity)]
    public void Scheme_type_map_entries(string schemeType, AssetClass expected)
    {
        var instruments = new Dictionary<string, MfInstrument> { ["INFX00000001"] = Instrument("INFX00000001", schemeType) };

        var result = Classifier.ClassifyFund(Fund("INFX00000001"), Build.Plan(), instruments, NoNavs, CoreOptions.Default);

        Assert.Equal(expected, result.AssetClass);
        Assert.False(result.IsFallback);
    }

    [Theory]
    [InlineData("Equity Scheme - Flexi Cap Fund", "X Fund", AssetClass.Equity)]
    [InlineData("Debt Scheme - Short Duration Fund", "X Fund", AssetClass.Debt)]
    [InlineData("Hybrid Scheme - Balanced Advantage", "X Fund", AssetClass.Hybrid)]
    [InlineData("Other Scheme - Gold ETF", "X Gold ETF", AssetClass.Gold)]
    [InlineData("Other Scheme - FoF Domestic", "X Gold Savings Fund", AssetClass.Gold)]
    // Category names in the live 2026 file
    [InlineData("Equity Schemes - Flexi Cap Fund", "X Fund", AssetClass.Equity)]
    [InlineData("Income/Debt Oriented Schemes - Corporate Bond Fund", "X Fund", AssetClass.Debt)]
    [InlineData("Hybrid Schemes - Arbitrage Fund", "X Fund", AssetClass.Hybrid)]
    [InlineData("Exchange Traded Funds (ETFs) - Gold ETF", "X Gold ETF", AssetClass.Gold)]
    public void Amfi_category_fallback(string category, string schemeName, AssetClass expected)
    {
        var navs = new Dictionary<string, NavRecord> { ["INFX00000001"] = new("INFX00000001", "1", schemeName, category, 10, new DateOnly(2026, 11, 7)) };

        var result = Classifier.ClassifyFund(Fund("INFX00000001"), Build.Plan(), new Dictionary<string, MfInstrument>(), navs, CoreOptions.Default);

        Assert.Equal(expected, result.AssetClass);
    }

    [Fact]
    public void Unknown_scheme_type_falls_through_to_amfi_category()
    {
        var instruments = new Dictionary<string, MfInstrument> { ["INFX00000001"] = Instrument("INFX00000001", "something-new") };
        var navs = new Dictionary<string, NavRecord> { ["INFX00000001"] = new("INFX00000001", "1", "X", "Debt Scheme - Gilt", 10, new DateOnly(2026, 11, 7)) };

        Assert.Equal(AssetClass.Debt, Classifier.ClassifyFund(Fund("INFX00000001"), Build.Plan(), instruments, navs, CoreOptions.Default).AssetClass);
    }

    [Fact]
    public void Unknown_fund_is_other_and_warns_once_per_isin()
    {
        var unknown = Classifier.ClassifyFund(Fund("INFX00000099"), Build.Plan(), new Dictionary<string, MfInstrument>(), NoNavs, CoreOptions.Default);
        Assert.Equal(new Classification(AssetClass.Other, true), unknown);

        var input = Build.Input(funds: [Build.Fund("INFX00000099", "Mystery Fund", 100), Build.Fund("INFX00000099", "Mystery Fund", 100)]);
        Assert.Single(PortfolioCalculator.Compute(input).Warnings);
    }

    [Fact]
    public void Plan_type_from_instrument_wins_over_name()
    {
        var instruments = new Dictionary<string, MfInstrument> { ["INFX00000001"] = Instrument("INFX00000001", "equity", plan: "regular") };

        Assert.Equal(PlanType.Regular, Classifier.ClassifyPlan("INFX00000001", "X Fund - Direct Plan", instruments));
    }

    [Theory]
    [InlineData("X Fund - Direct Plan - Growth", PlanType.Direct)]
    [InlineData("X Fund - REGULAR Plan - Growth", PlanType.Regular)]
    [InlineData("X Fund - Growth", PlanType.Unknown)]
    public void Plan_type_from_name_or_unknown(string name, PlanType expected) =>
        Assert.Equal(expected, Classifier.ClassifyPlan("INFX00000001", name, new Dictionary<string, MfInstrument>()));
}

public class DirectTwinTests
{
    [Fact]
    public void Found_with_same_amc_dividend_type_and_name_swap()
    {
        var twin = Classifier.FindDirectTwin("INFTEST00002", Samples.Instruments);

        Assert.Equal("INFTEST00001", twin?.Isin);
    }

    [Fact]
    public void Name_match_is_case_insensitive_and_whitespace_normalised()
    {
        var instruments = new Dictionary<string, MfInstrument>
        {
            ["R"] = new("R", "A_MF", "X  Fund - regular Plan", "growth", "equity", "regular"),
            ["D"] = new("D", "A_MF", "x fund - DIRECT plan", "growth", "equity", "direct"),
        };

        Assert.Equal("D", Classifier.FindDirectTwin("R", instruments)?.Isin);
    }

    [Fact]
    public void Not_found_when_no_direct_version_exists()
    {
        var instruments = new Dictionary<string, MfInstrument>
        {
            ["R"] = new("R", "A_MF", "X Fund - Regular Plan", "growth", "equity", "regular"),
        };

        Assert.Null(Classifier.FindDirectTwin("R", instruments));
    }

    [Fact]
    public void Different_amc_is_not_a_twin()
    {
        var instruments = new Dictionary<string, MfInstrument>
        {
            ["R"] = new("R", "A_MF", "X Fund - Regular Plan", "growth", "equity", "regular"),
            ["D"] = new("D", "B_MF", "X Fund - Direct Plan", "growth", "equity", "direct"),
        };

        Assert.Null(Classifier.FindDirectTwin("R", instruments));
    }

    [Fact]
    public void Different_dividend_type_is_not_a_twin()
    {
        var instruments = new Dictionary<string, MfInstrument>
        {
            ["R"] = new("R", "A_MF", "X Fund - Regular Plan", "growth", "equity", "regular"),
            ["D"] = new("D", "A_MF", "X Fund - Direct Plan", "idcw", "equity", "direct"),
        };

        Assert.Null(Classifier.FindDirectTwin("R", instruments));
    }

    [Fact]
    public void Regular_fund_without_twin_still_fires_without_twin_evidence()
    {
        var input = Build.Input(
            funds: [Build.Fund("R", "X Fund - Regular Plan", 1000)],
            instruments: new Dictionary<string, MfInstrument> { ["R"] = new("R", "A_MF", "X Fund - Regular Plan", "growth", "equity", "regular") });

        var finding = Assert.Single(RulesEngine.Evaluate(input, Time.Default), f => f.Rule == RuleNames.RegularPlan);

        Assert.Equal(["isin"], finding.Evidence.Keys);
        Assert.Equal("X Fund - Regular Plan is a Regular plan.", finding.Message);
    }
}
