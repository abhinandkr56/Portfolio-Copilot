// NavSync (doc 02): download the current AMFI NAV file, parse it with Core's AmfiNavParser and upsert the
// latest NAV per ISIN into NavRecords. Started by hand from the Aspire dashboard (explicit start).
// The Api owns the schema (EF migrations), so the AppHost makes NavSync wait for the Api.
// Exit code: 0 on success, 1 on any failure (download, unknown format, missing table).

using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PortfolioCopilot.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("copilotdb");
builder.Logging.AddFilter("Npgsql", LogLevel.Warning); // Npgsql logs every command's SQL at Information

using var host = builder.Build();
await host.StartAsync(); // starts the telemetry providers so the logs reach the Aspire dashboard

var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NavSync");
var url = builder.Configuration["NavSync:Url"] ?? NavSync.DefaultUrl;
var exitCode = 0;
try
{
    var stopwatch = Stopwatch.StartNew();
    var text = await NavSync.DownloadAsync(url);
    log.LogInformation("Downloaded {Bytes:N0} characters from {Url}", text.Length, url);

    var parsed = AmfiNavParser.Parse(text);
    var latest = AmfiNavParser.LatestPerIsin(parsed);
    if (latest.Count == 0)
        throw new AmfiFormatException("The AMFI file parsed to no NAV records.");
    // Most funds carry the last business day; liquid/overnight funds also publish weekend dates ahead, so the max can be in the future.
    var main = latest.GroupBy(r => r.NavDate).MaxBy(g => g.Count())!;
    log.LogInformation("Parsed {Records:N0} NAV records for {Isins:N0} ISINs; NAV date {NavDate:yyyy-MM-dd} for {OnMain:N0} of them, latest {Latest:yyyy-MM-dd}",
        parsed.Count, latest.Count, main.Key, main.Count(), latest.Max(r => r.NavDate));

    var (inserted, updated) = await NavSync.UpsertAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), latest);
    log.LogInformation("NavRecords upserted in {Seconds:N1}s: {Inserted:N0} inserted, {Updated:N0} updated, {Unchanged:N0} unchanged",
        stopwatch.Elapsed.TotalSeconds, inserted, updated, latest.Count - inserted - updated);
}
catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
{
    log.LogError("The NavRecords table doesn't exist yet. Start the api first: it applies the migrations in Development.");
    exitCode = 1;
}
catch (AmfiFormatException ex)
{
    log.LogError("{Message} Nothing was imported; the parser needs updating for the new layout (doc 05 §AMFI NAV parser).", ex.Message);
    exitCode = 1;
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    log.LogError(ex, "Couldn't download the AMFI NAV file from {Url}", url);
    exitCode = 1;
}

await host.StopAsync();
return exitCode;

static class NavSync
{
    public const string DefaultUrl = "https://portal.amfiindia.com/spages/NAVAll.txt";

    public static async Task<string> DownloadAsync(string url)
    {
        // A plain client: the ServiceDefaults resilience handler's 10-second attempt timeout is too short for a 1.5 MB file on a slow link.
        using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PortfolioCopilot-NavSync/1.0");
        return await http.GetStringAsync(url);
    }

    /// <summary>One statement for the whole file; rows whose values didn't change aren't rewritten.</summary>
    public static async Task<(int Inserted, int Updated)> UpsertAsync(NpgsqlDataSource db, IReadOnlyList<NavRecord> records)
    {
        const string sql = """
            INSERT INTO "NavRecords" ("Isin", "SchemeCode", "SchemeName", "Category", "Nav", "NavDate")
            SELECT * FROM unnest(@isin, @code, @name, @category, @nav, @date)
            ON CONFLICT ("Isin") DO UPDATE SET
                "SchemeCode" = EXCLUDED."SchemeCode", "SchemeName" = EXCLUDED."SchemeName",
                "Category" = EXCLUDED."Category", "Nav" = EXCLUDED."Nav", "NavDate" = EXCLUDED."NavDate"
            WHERE ("NavRecords"."SchemeCode", "NavRecords"."SchemeName", "NavRecords"."Category", "NavRecords"."Nav", "NavRecords"."NavDate")
                IS DISTINCT FROM (EXCLUDED."SchemeCode", EXCLUDED."SchemeName", EXCLUDED."Category", EXCLUDED."Nav", EXCLUDED."NavDate")
            RETURNING (xmax = 0) AS inserted
            """;

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("isin", records.Select(r => r.Isin).ToArray());
        cmd.Parameters.AddWithValue("code", records.Select(r => r.SchemeCode).ToArray());
        cmd.Parameters.AddWithValue("name", records.Select(r => r.SchemeName).ToArray());
        cmd.Parameters.AddWithValue("category", records.Select(r => r.Category).ToArray());
        cmd.Parameters.AddWithValue("nav", records.Select(r => r.Nav).ToArray());
        cmd.Parameters.AddWithValue("date", records.Select(r => r.NavDate).ToArray());

        int inserted = 0, updated = 0;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetBoolean(0)) inserted++;
            else updated++;
        }
        return (inserted, updated);
    }
}
