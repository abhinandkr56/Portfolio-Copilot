using System.Text.Json.Nodes;

namespace PortfolioCopilot.MockKite;

/// <summary>
/// Scenario files hold Kite-shaped arrays with minimal fields. Before serialising, the mock fills the other
/// documented fields (doc 04 / kite.trade docs) with 0 or "" so responses look like Kite's.
/// </summary>
public static class KiteShapes
{
    private static readonly Dictionary<string, (string[] Numbers, string[] Strings)> Fields = new()
    {
        ["holdings"] = (
            ["instrument_token", "quantity", "t1_quantity", "realised_quantity", "used_quantity", "authorised_quantity",
             "opening_quantity", "collateral_quantity", "average_price", "last_price", "close_price", "pnl", "day_change", "day_change_percentage", "price"],
            ["tradingsymbol", "exchange", "isin", "product", "authorised_date"]),
        ["mfHoldings"] = (
            ["quantity", "average_price", "last_price", "pnl", "pledged_quantity"],
            ["folio", "fund", "tradingsymbol", "last_price_date"]),
        ["mfSips"] = (
            ["instalment_amount", "instalments", "completed_instalments", "pending_instalments", "instalment_day", "trigger_price"],
            ["sip_id", "tradingsymbol", "fund", "dividend_type", "transaction_type", "status", "sip_type", "created", "frequency",
             "next_instalment", "last_instalment", "tag"]),
        ["mfOrders"] = (
            ["amount", "quantity", "price", "last_price", "average_price"],
            ["order_id", "exchange_order_id", "tradingsymbol", "status", "status_message", "folio", "fund", "order_timestamp",
             "exchange_timestamp", "settlement_id", "transaction_type", "variety", "purchase_type", "placed_by", "last_price_date", "tag"]),
    };

    public static JsonArray Items(JsonObject scenario, string key)
    {
        var source = scenario["kite"]?[key] as JsonArray ?? [];
        var (numbers, strings) = Fields[key];
        var result = new JsonArray();
        foreach (var item in source)
        {
            if (item?.DeepClone() is not JsonObject obj)
                continue;
            foreach (var n in numbers)
                if (!obj.ContainsKey(n)) obj[n] = 0;
            foreach (var s in strings)
                if (!obj.ContainsKey(s)) obj[s] = "";
            result.Add(obj);
        }
        return result;
    }

    public static JsonObject Profile(JsonObject scenario)
    {
        var p = scenario["profile"] as JsonObject;
        return new JsonObject
        {
            ["user_id"] = p?["user_id"]?.GetValue<string>() ?? "AB1234",
            ["user_name"] = p?["user_name"]?.GetValue<string>() ?? "Demo User",
            ["user_shortname"] = "Demo",
            ["user_type"] = "individual",
            ["email"] = "demo@example.invalid",
            ["broker"] = "ZERODHA",
            ["exchanges"] = new JsonArray("NSE", "BSE", "MF"),
            ["products"] = new JsonArray("CNC"),
            ["order_types"] = new JsonArray("MARKET", "LIMIT"),
            ["avatar_url"] = null,
            ["meta"] = new JsonObject { ["demat_consent"] = "consent" },
        };
    }
}
