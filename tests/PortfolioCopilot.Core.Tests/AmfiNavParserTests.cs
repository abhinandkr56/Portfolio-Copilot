namespace PortfolioCopilot.Core.Tests;

public class AmfiNavParserTests
{
    private static readonly IReadOnlyList<NavRecord> Sample = AmfiNavParser.Parse(Samples.NavText);

    [Fact]
    public void Sample_parses_to_nine_records() => Assert.Equal(9, Sample.Count);

    [Fact]
    public void Category_comes_from_the_schemes_line()
    {
        Assert.Equal("Equity Scheme - Flexi Cap Fund", Sample.Single(r => r.Isin == "INFTEST00001").Category);
        Assert.Equal("Hybrid Scheme - Dynamic Asset Allocation or Balanced Advantage", Sample.Single(r => r.Isin == "INFTEST00007").Category);
    }

    [Fact]
    public void Both_isins_on_one_line_produce_records()
    {
        var payout = Sample.Single(r => r.Isin == "INFTEST00008");
        var reinvest = Sample.Single(r => r.Isin == "INFTEST00009");

        Assert.Equal(new NavRecord("INFTEST00008", "900003", "Alpha Flexi Cap Fund - Direct Plan - IDCW", "Equity Scheme - Flexi Cap Fund", 21.45m, new DateOnly(2026, 11, 7)), payout);
        Assert.Equal(payout with { Isin = "INFTEST00009" }, reinvest);
    }

    [Fact]
    public void Na_nav_is_skipped() => Assert.DoesNotContain(Sample, r => r.Isin == "INFTEST00010");

    [Fact]
    public void Unknown_header_throws()
    {
        var ex = Assert.Throws<AmfiFormatException>(() => AmfiNavParser.Parse("SchemeCode,ISIN,NAV\n1,INFTEST00001,10"));

        Assert.StartsWith("Unrecognised AMFI header: ", ex.Message);
    }

    [Fact]
    public void Empty_file_throws() => Assert.Throws<AmfiFormatException>(() => AmfiNavParser.Parse("\n\n"));

    [Fact]
    public void Pipe_delimiter_is_accepted()
    {
        var text = Samples.NavText.Replace(';', '|');

        Assert.Equal(Sample, AmfiNavParser.Parse(text));
    }
}

/// <summary>
/// The 8-column layout (Plan and Option columns), from a trimmed copy of the live file downloaded 2026-11-14
/// (<c>samples/amfi/NAVAll.live-2026-11-13.sample.txt</c>: real lines, CRLF endings, whitespace-only blank lines).
/// </summary>
public class AmfiNavParserLiveLayoutTests
{
    private static readonly string Text = Samples.Read("amfi", "NAVAll.live-2026-11-13.sample.txt");
    private static readonly IReadOnlyList<NavRecord> Live = AmfiNavParser.Parse(Text);

    [Fact]
    public void The_sample_keeps_the_live_files_crlf_line_endings() => Assert.Contains("\r\n", Text);

    [Fact]
    public void Live_sample_parses_to_eleven_records() => Assert.Equal(11, Live.Count);

    [Fact]
    public void Plan_and_option_are_joined_back_into_the_scheme_name() =>
        Assert.Equal(new NavRecord("INF846K01WO1", "135762", "Axis Children's Fund - Direct Plan - Growth Option",
                "Children\u2019s Fund - Childrens' Fund", 31.0814m, new DateOnly(2026, 11, 13)),
            Live.Single(r => r.Isin == "INF846K01WO1"));

    [Fact]
    public void Empty_plan_and_option_leave_the_name_alone() =>
        Assert.Equal("Kotak Corporate Bond Fund", Live.Single(r => r.Isin == "INF178L01BU8").SchemeName);

    [Fact]
    public void Second_isin_alone_and_padded_is_still_read()
    {
        var r = Live.Single(r => r.Isin == "INF178L01BT0");

        Assert.Equal(("133786", 1202.9939m, new DateOnly(2016, 10, 21)), (r.SchemeCode, r.Nav, r.NavDate));
    }

    [Fact]
    public void Both_isins_on_one_line_produce_records() =>
        Assert.Equal(["INF2JJD01110", "INF2JJD01128"], Live.Where(r => r.SchemeCode == "154045").Select(r => r.Isin).Order());

    [Fact]
    public void Category_with_nested_parentheses_is_read_whole() =>
        Assert.Equal("Exchange Traded Funds (ETFs) - Gold ETF", Live.Single(r => r.Isin == "INF579M01BB5").Category);

    [Fact]
    public void Live_categories_classify()
    {
        AssetClass? Of(string isin) { var r = Live.Single(x => x.Isin == isin); return Classifier.ClassifyAmfiCategory(r.Category, r.SchemeName); }

        Assert.Equal(AssetClass.Equity, Of("INF2JJD01136"));
        Assert.Equal(AssetClass.Gold, Of("INF579M01BB5"));
        Assert.Equal(AssetClass.Debt, Of("INF846K01ZN6"));
    }

    [Fact]
    public void A_repeated_isin_keeps_the_latest_nav_date()
    {
        Assert.Equal(2, Live.Count(r => r.Isin == "INF204KA1US6"));

        var latest = AmfiNavParser.LatestPerIsin(Live);

        Assert.Equal(10, latest.Count);
        Assert.Equal(("131539", new DateOnly(2017, 10, 23)), latest.Where(r => r.Isin == "INF204KA1US6").Select(r => (r.SchemeCode, r.NavDate)).Single());
    }

    [Fact]
    public void A_header_with_an_unknown_ninth_column_throws()
    {
        var header = "Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date;AUM";

        Assert.Throws<AmfiFormatException>(() => AmfiNavParser.Parse(header + "\n"));
    }

    [Fact]
    public void A_header_with_reordered_columns_throws()
    {
        var header = "Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Option;Plan;Net Asset Value;Date";

        Assert.Throws<AmfiFormatException>(() => AmfiNavParser.Parse(header + "\n"));
    }
}

public class MfInstrumentsCsvParserTests
{
    [Fact]
    public void Sample_parses_by_header_name()
    {
        var instruments = Samples.Instruments;

        Assert.Equal(7, instruments.Count);
        Assert.Equal(new MfInstrument("INFTEST00007", "DELTAMF_MF", "Delta Balanced Advantage Fund - Direct Plan - Growth", "growth", "balanced", "direct"),
            instruments["INFTEST00007"]);
    }

    [Fact]
    public void Quoted_names_may_contain_commas()
    {
        var csv = "tradingsymbol,amc,name,dividend_type,scheme_type,plan\nINFX00000001,A_MF,\"X Fund, Direct\",growth,equity,direct\n";

        Assert.Equal("X Fund, Direct", MfInstrumentsCsvParser.Parse(csv)["INFX00000001"].Name);
    }
}
