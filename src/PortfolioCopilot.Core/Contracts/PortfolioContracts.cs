namespace PortfolioCopilot.Core.Contracts;

// Response shapes from doc 03. Money is rounded to 2 dp and percentages to 1 dp here, for display only;
// the rules always compare the unrounded values in ComputedPortfolio.

public sealed record StockRow(
    string Symbol, string Isin, string Exchange, decimal Quantity, decimal AveragePrice, decimal LastPrice,
    decimal Value, decimal Pnl, AssetClass AssetClass, string AssetClassLabel, decimal PercentOfTotal);

public sealed record FundRow(
    string Fund, string Isin, string FolioMasked, decimal Quantity, decimal AveragePrice, decimal LastPrice,
    DateOnly? LastPriceDate, decimal Value, decimal Pnl, AssetClass AssetClass, string AssetClassLabel,
    PlanType Plan, decimal PercentOfTotal);

public sealed record HoldingsResponse(DateTimeOffset AsOfUtc, decimal TotalValue, IReadOnlyList<StockRow> Stocks, IReadOnlyList<FundRow> Funds);

public sealed record SipDto(
    string SipId, string Fund, string Isin, string Status, string Frequency, decimal InstalmentAmount,
    decimal MonthlyEquivalent, DateOnly? NextInstalment, int CompletedInstalments);

public sealed record FailedOrderDto(string OrderId, string Fund, DateOnly Date, string Message);

public sealed record SipsResponse(
    DateTimeOffset AsOfUtc, decimal ActiveMonthlyEquivalent, decimal MonthlyBudget,
    IReadOnlyList<SipDto> Sips, IReadOnlyList<FailedOrderDto> RecentFailedOrders);

/// <param name="Severity">The AllocationDrift severity for this class, or null when it's within the band.</param>
public sealed record AllocationClassDto(
    AssetClass AssetClass, decimal Value, decimal ActualPercent, decimal TargetPercent, decimal DriftPoints, Severity? Severity);

public sealed record AllocationResponse(DateTimeOffset AsOfUtc, decimal TotalValue, IReadOnlyList<AllocationClassDto> Classes);

public sealed record FindingDto(
    string Rule, string Subject, Severity Severity, IReadOnlyDictionary<string, object> Evidence, string Message, string Suggestion);

public sealed record ReviewRequest(bool Explain = false);

/// <summary>Set when <c>explain: true</c> (Phase 5). <see cref="Error"/> is a short message when the AI call failed.</summary>
public sealed record AiInfo(string? Provider, string? Model, bool? NumberCheckPassed, int? Attempts, string? Error);

public sealed record PortfolioReview(
    DateTimeOffset AsOfUtc, DateOnly? NavDate, AllocationResponse Allocation, IReadOnlyList<FindingDto> Findings,
    string? Summary, AiInfo? Ai, string Disclaimer);

public sealed record NavImportRequest(string Path);

public sealed record NavImportResponse(int Imported, DateOnly? NavDate);

public sealed record ScenarioResponse(string Scenario, DateOnly Today);
