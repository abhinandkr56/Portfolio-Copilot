namespace PortfolioCopilot.Core;

public enum AssetClass { Equity, Debt, Gold, Other, Hybrid /* input only: split before allocation */ }

public enum PlanType { Direct, Regular, Unknown }

public enum Severity { Act, Watch, Info } // sort order: Act first

/// <param name="Quantity">Already includes T1 quantity.</param>
public record StockHolding(string Symbol, string Isin, string Exchange, decimal Quantity,
    decimal AveragePrice, decimal LastPrice, decimal Pnl);

public record FundHolding(string Isin, string Fund, string Folio, decimal Quantity,
    decimal AveragePrice, decimal LastPrice, DateOnly? LastPriceDate, decimal Pnl);

public record Sip(string SipId, string Isin, string Fund, string Status, string Frequency,
    decimal InstalmentAmount, DateOnly? NextInstalment, int CompletedInstalments);

public record MfOrder(string OrderId, string Isin, string Fund, string Status, string StatusMessage,
    string Variety, DateTimeOffset OrderTimestamp);

/// <param name="Plan">"direct" | "regular"</param>
public record MfInstrument(string Isin, string Amc, string Name, string DividendType,
    string SchemeType, string Plan);

public record NavRecord(string Isin, string SchemeCode, string SchemeName, string Category,
    decimal Nav, DateOnly NavDate);

public record Plan(
    Dictionary<AssetClass, decimal> TargetAllocation,   // Equity/Debt/Gold/Other, sums to 100
    decimal DriftBandPoints,
    decimal MaxSingleStockPercent,
    decimal MaxSingleFundPercent,
    bool PreferDirectPlans,
    decimal MonthlySipBudget,
    decimal HybridEquityPercent,                        // default 65
    Dictionary<string, AssetClass> AssetClassOverrides, // key: stock symbol or ISIN
    string? Notes);

public record PortfolioInput(Plan Plan, IReadOnlyList<StockHolding> Stocks,
    IReadOnlyList<FundHolding> Funds, IReadOnlyList<Sip> Sips, IReadOnlyList<MfOrder> MfOrders,
    IReadOnlyDictionary<string, MfInstrument> Instruments, IReadOnlyDictionary<string, NavRecord> Navs);

public record Finding(string Rule, string Subject, Severity Severity,
    IReadOnlyDictionary<string, object> Evidence, string Message, string Suggestion);
