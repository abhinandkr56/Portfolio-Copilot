namespace PortfolioCopilot.Core.Tests;

public class SamplesTests
{
    [Fact]
    public void Samples_are_copied_to_the_test_output()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "samples");

        Assert.True(File.Exists(Path.Combine(dir, "plan.sample.json")));
        Assert.True(File.Exists(Path.Combine(dir, "scenarios", "demo.json")));
        Assert.True(File.Exists(Path.Combine(dir, "kite", "mf-instruments.csv")));
    }
}
