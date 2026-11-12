using System.Text.Json.Nodes;

namespace PortfolioCopilot.Core.Tests;

public class RedactorTests
{
    private const string Json = """
    {
      "profile": { "user_id": "AB1234", "user_name": "Demo User", "email": "demo@example.com", "access_token": "secret-token" },
      "funds": [ { "folio": "FOLIOTEST0001", "fund": "Alpha", "percentOfTotal": 22.0, "value": 22000, "pnl": 2000,
                   "averagePrice": 50, "lastPrice": 55, "order_id": "O1" } ],
      "sips": { "items": [ { "sip_id": "S1", "instalmentAmount": 10000 } ], "activeMonthlyEquivalent": 16500, "monthlyBudget": 40000 },
      "plan": { "monthlySipBudget": 40000, "driftBandPoints": 5 },
      "findings": [ { "message": "The monthly SIP of ₹5,000 in Beta is PAUSED.", "evidence": { "percentOfTotal": 18.0, "value": 18000 } } ]
    }
    """;

    private static readonly string[] AmountFields = ["value", "pnl", "averagePrice", "lastPrice", "instalmentAmount", "activeMonthlyEquivalent", "monthlyBudget", "monthlySipBudget"];
    private static readonly string[] DroppedFields = ["user_name", "email", "access_token", "order_id", "sip_id"];

    private static IEnumerable<string> Keys(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(p => Keys(p.Value).Prepend(p.Key)),
        JsonArray a => a.SelectMany(Keys),
        _ => [],
    };

    [Fact]
    public void Folio_and_user_id_are_masked()
    {
        var r = Redactor.Redact(JsonNode.Parse(Json), shareAmounts: false)!;

        Assert.Equal("****0001", (string?)r["funds"]![0]!["folio"]);
        Assert.Equal("AB***34", (string?)r["profile"]!["user_id"]);
    }

    [Fact]
    public void Email_token_names_and_ids_are_dropped()
    {
        var r = Redactor.Redact(JsonNode.Parse(Json), shareAmounts: true)!;

        Assert.Empty(Keys(r).Intersect(DroppedFields));
        var text = r.ToJsonString();
        Assert.DoesNotContain("secret-token", text);
        Assert.DoesNotContain("demo@example.com", text);
        Assert.DoesNotContain("FOLIOTEST0001", text);
    }

    [Fact]
    public void Amounts_are_removed_when_share_amounts_is_false()
    {
        var r = Redactor.Redact(JsonNode.Parse(Json), shareAmounts: false)!;

        Assert.Empty(Keys(r).Intersect(AmountFields));
        Assert.Equal(22.0m, (decimal)r["funds"]![0]!["percentOfTotal"]!);
        Assert.Equal("The monthly SIP of ₹*** in Beta is PAUSED.", (string?)r["findings"]![0]!["message"]);
    }

    [Fact]
    public void Amounts_are_kept_when_share_amounts_is_true()
    {
        var r = Redactor.Redact(JsonNode.Parse(Json), shareAmounts: true)!;

        Assert.Equal(AmountFields.Order(), Keys(r).Intersect(AmountFields).Order());
        Assert.Contains("₹5,000", (string?)r["findings"]![0]!["message"]);
    }

    [Fact]
    public void Input_is_not_modified()
    {
        var node = JsonNode.Parse(Json)!;

        Redactor.Redact(node, shareAmounts: false);

        Assert.Equal("FOLIOTEST0001", (string?)node["funds"]![0]!["folio"]);
    }
}

public class ForbiddenLanguageCheckTests
{
    [Theory]
    [InlineData("Consider Direct plan for Alpha Flexi Cap.", true)]
    [InlineData("You should sell INFY.", false)]
    [InlineData("I recommend buying more TCS.", false)]
    [InlineData("Equity is likely to outperform next year.", false)]
    [InlineData("Your equity is 84.5% against 70.0%. Rebalance toward target.", true)]
    [InlineData("Returns are guaranteed.", false)]
    [InlineData("Buy more HDFCBANK now.", false)]
    [InlineData("The target price is 2000.", false)]
    [InlineData("Consider switching to the Direct plan.", false)]
    [InlineData("You hold INFY, which is above your cap. Information only, not investment advice. You make the decisions.", true)]
    public void Table(string sentence, bool allowed) => Assert.Equal(allowed, ForbiddenLanguageCheck.Check(sentence).Passed);

    [Fact]
    public void Problems_name_the_phrase() =>
        Assert.Contains("You should sell", ForbiddenLanguageCheck.Check("You should sell INFY.").Problems);
}

public class NumberCheckTests
{
    private const string Review = """
    { "allocation": { "classes": [ { "assetClass": "Equity", "actualPercent": 84.5123, "targetPercent": 70, "driftPoints": 14.5123 },
                                   { "assetClass": "Debt", "actualPercent": 11.5, "driftPoints": -8.5 } ] },
      "findings": [ { "message": "Active SIPs add up to ₹18,000 a month.", "evidence": { "value": 18000, "lastDate": "2026-11-05" } } ] }
    """;

    private static GuardrailResult Check(string answer) => NumberCheck.Check(answer, [Review]);

    [Theory]
    [InlineData("Equity is 84.5123% of the portfolio.")]
    [InlineData("Equity is 84.5% against 70%, drift +14.5 points.")]
    [InlineData("Equity is 85% of the portfolio.")]
    [InlineData("Debt drift is -8.5 points.")]
    [InlineData("Your SIPs total ₹18,000 a month.")]
    [InlineData("Your SIPs total 18000 a month.")]
    [InlineData("Debt is 8.5 points under target.")]
    public void Passes_with_exact_and_rounded_numbers(string answer) => Assert.True(Check(answer).Passed);

    [Fact]
    public void Fails_on_an_invented_number_and_lists_it()
    {
        var result = Check("Equity is 84.5% and will need 23.4 points of selling, about 31 lakh.");

        Assert.False(result.Passed);
        Assert.Equal(["23.4", "31"], result.Problems);
    }

    [Theory]
    [InlineData("You have 3 Regular plans and 12 SIPs.")]
    [InlineData("Your plan runs until 2041.")]
    [InlineData("The last failed order was on 2026-11-05.")]
    [InlineData("A new order on 2027-01-31 is unrelated.")]
    public void Allows_small_integers_years_and_dates(string answer) => Assert.True(Check(answer).Passed);

    [Fact]
    public void Thirteen_is_not_a_free_number() => Assert.False(Check("You have 13 funds.").Passed);

    [Fact]
    public void Ignores_the_disclaimer() =>
        Assert.True(Check("Equity is 84.5%. Information only, not investment advice. You make the decisions.").Passed);

    [Theory]
    [InlineData("₹18,000", 18000)]
    [InlineData("+14.5", 14.5)]
    [InlineData("84.5%", 84.5)]
    [InlineData("₹1,00,000", 100000)]
    [InlineData("-8.5", -8.5)]
    public void Extracts_formatted_numbers(string text, decimal expected) =>
        Assert.Equal([expected], NumberCheck.ExtractNumbers(text));
}

public class InrTests
{
    [Theory]
    [InlineData(0, "₹0")]
    [InlineData(500, "₹500")]
    [InlineData(5000, "₹5,000")]
    [InlineData(100000, "₹1,00,000")]
    [InlineData(23666.67, "₹23,666.67")]
    [InlineData(12345678.5, "₹1,23,45,678.50")]
    [InlineData(-600, "-₹600")]
    public void Indian_grouping(decimal amount, string expected) => Assert.Equal(expected, Inr.Format(amount));
}

public class DisclaimerTests
{
    [Theory]
    [InlineData("Equity is 84.5%.", "Equity is 84.5%.\n\nInformation only, not investment advice. You make the decisions.")]
    [InlineData("Equity is 84.5%. Information only, not investment advice. You make the decisions.", "Equity is 84.5%.\n\nInformation only, not investment advice. You make the decisions.")]
    [InlineData("Information only, not investment advice. You make the decisions. Equity is 84.5%.", "Equity is 84.5%.\n\nInformation only, not investment advice. You make the decisions.")]
    [InlineData("", "Information only, not investment advice. You make the decisions.")]
    public void Ensure_ends_with_exactly_one_disclaimer(string answer, string expected) =>
        Assert.Equal(expected, Disclaimer.Ensure(answer));
}
