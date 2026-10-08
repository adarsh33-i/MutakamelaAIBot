using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using MutakamelaAPI.Retrieval;
using MutakamelaAPI.Rules;
using MutakamelaAPI.Services;

namespace MutakamelaAPI.Tests;

/// <summary>
/// Builds the agentic engine in-process with the simulated browser agent and a
/// throw-away data directory. The AI endpoint is deliberately unreachable: no
/// job step may depend on the LLM.
/// </summary>
public sealed class AgentFixture : IDisposable
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "mutakamela-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public IConfiguration Config { get; }

    public AgentFixture()
    {
        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Browser"] = "simulated",
            ["Agent:DataDir"] = Path.Combine(DataDir, "jobs"),
            ["Agent:EvidenceDir"] = Path.Combine(DataDir, "evidence"),
            ["AI:BaseUrl"] = "http://127.0.0.1:9/v1",
            ["AI:OfficialSiteKnowledgeEnabled"] = "false"
        }).Build();
    }

    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(Config);
        services.AddHttpClient();
        services.AddHttpClient("OfficialInsuranceSite");
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();
        services.AddSingleton<IProductRetriever, ProductRetriever>();
        services.AddSingleton<IRulesEngine, RulesEngine>();
        services.AddSingleton<IFlowRegistry, FlowRegistry>();
        services.AddSingleton<IApplicationJobStore, FileApplicationJobStore>();
        services.AddSingleton<IBrowserAgent, SimulatedBrowserAgent>();
        services.AddSingleton<ISlotExtractor, LlmSlotExtractor>();
        services.AddSingleton<IApplicationOrchestrator, ApplicationOrchestrator>();
        services.AddSingleton<IAIPolicyService>(sp => new AIPolicyService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            sp.GetRequiredService<IHttpClientFactory>(), Config,
            sp.GetRequiredService<ILogger<AIPolicyService>>(), sp.GetRequiredService<ISessionManager>(),
            sp.GetRequiredService<IApplicationOrchestrator>(),
            sp.GetRequiredService<IProductRetriever>()));
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true); } catch { /* best effort */ }
    }
}

public static class ChatExtensions
{
    public static Task<AIPolicyResponse> Say(this IAIPolicyService ai, string session, string message, string lang = "en") =>
        ai.ProcessMessageAsync(session, message, lang);
}
