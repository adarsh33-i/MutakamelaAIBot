using Newtonsoft.Json;

namespace MutakamelaAPI.Browser;

public interface IFlowRegistry
{
    IReadOnlyCollection<FlowSpec> All { get; }
    FlowSpec? Get(string flowId);
}

/// <summary>Loads every Browser/Flows/*.json spec at startup.</summary>
public class FlowRegistry : IFlowRegistry
{
    private readonly Dictionary<string, FlowSpec> _flows = new(StringComparer.OrdinalIgnoreCase);

    public FlowRegistry(ILogger<FlowRegistry> logger)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Browser", "Flows");
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("Flow directory {Dir} not found; no portal journeys available.", dir);
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var spec = JsonConvert.DeserializeObject<FlowSpec>(File.ReadAllText(file));
                if (spec == null || string.IsNullOrWhiteSpace(spec.Id)) continue;
                _flows[spec.Id] = spec;
                logger.LogInformation("Loaded flow {Flow} ({Screens} screens, verified={Verified})", spec.Id, spec.Screens.Count, spec.Verified);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load flow spec {File}", file);
            }
        }
    }

    public IReadOnlyCollection<FlowSpec> All => _flows.Values;
    public FlowSpec? Get(string flowId) => _flows.TryGetValue(flowId ?? string.Empty, out var spec) ? spec : null;
}
