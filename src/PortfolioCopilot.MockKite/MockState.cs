using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace PortfolioCopilot.MockKite;

/// <summary>Marker for <c>WebApplicationFactory&lt;MockKiteMarker&gt;</c> in tests (the generated Program class is internal).</summary>
public sealed class MockKiteMarker;

public sealed record MockRequest(string Method, string Path, int Status, string? Checksum);

/// <summary>The mock's mutable state: the current scenario, pending failures and a request log for tests.</summary>
public sealed class MockState
{
    private readonly object _gate = new();
    private string? _failMode;
    private int _failRemaining;

    public MockState(string samplesDir)
    {
        SamplesDir = samplesDir;
        ScenarioName = "demo";
        Scenario = LoadScenario("demo") ?? throw new InvalidOperationException($"No demo scenario in {samplesDir}.");
    }

    public string SamplesDir { get; }

    public string ScenarioName { get; private set; }

    public JsonObject Scenario { get; private set; }

    public ConcurrentQueue<MockRequest> Requests { get; } = new();

    public ConcurrentQueue<string> Unexpected { get; } = new();

    public bool TrySwitch(string name)
    {
        var scenario = LoadScenario(name);
        if (scenario is null)
            return false;
        lock (_gate)
        {
            ScenarioName = name;
            Scenario = scenario;
        }
        return true;
    }

    /// <summary>Sets the next <paramref name="count"/> Kite requests to fail with <paramref name="mode"/>; "none" clears it.</summary>
    public void SetFailure(string mode, int count)
    {
        lock (_gate)
        {
            _failMode = mode == "none" ? null : mode;
            _failRemaining = _failMode is null ? 0 : Math.Max(1, count);
        }
    }

    public string? TakeFailure()
    {
        lock (_gate)
        {
            if (_failMode is null || _failRemaining <= 0)
                return null;
            var mode = _failMode;
            if (--_failRemaining == 0)
                _failMode = null;
            return mode;
        }
    }

    public void Reset()
    {
        SetFailure("none", 0);
        TrySwitch("demo");
        Requests.Clear();
        Unexpected.Clear();
    }

    private JsonObject? LoadScenario(string name)
    {
        if (name.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
            return null;
        var path = Path.Combine(SamplesDir, "scenarios", name + ".json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
    }
}
