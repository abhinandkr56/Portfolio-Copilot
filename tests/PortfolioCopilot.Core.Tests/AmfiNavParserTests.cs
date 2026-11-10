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
