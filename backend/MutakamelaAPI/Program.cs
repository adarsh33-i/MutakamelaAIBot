using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Retrieval;
using MutakamelaAPI.Rules;
using MutakamelaAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("OfficialInsuranceSite", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MutakamelaInsuranceAssistant/1.0");
});
builder.Services.AddSingleton<IWhatsAppService, WhatsAppService>();
builder.Services.AddSingleton<IAIPolicyService, AIPolicyService>();
builder.Services.AddSingleton<ISessionManager, SessionManager>();

// Agentic engine: rules, flow specs, durable job store, browser agent, orchestrator.
// Agent:Browser = "simulated" (default, no network) | "playwright" (refuses unverified flows).
// Product RAG: BM25 lexical index always on; dense embeddings when AI:EmbeddingModel is set.
builder.Services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();
builder.Services.AddSingleton<IProductRetriever, ProductRetriever>();
builder.Services.AddSingleton<IRulesEngine, RulesEngine>();
builder.Services.AddSingleton<IFlowRegistry, FlowRegistry>();
builder.Services.AddSingleton<IApplicationJobStore, FileApplicationJobStore>();
if (string.Equals(builder.Configuration["Agent:Browser"], "playwright", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IBrowserAgent, PlaywrightBrowserAgent>();
else
    builder.Services.AddSingleton<IBrowserAgent, SimulatedBrowserAgent>();
builder.Services.AddSingleton<ISlotExtractor, LlmSlotExtractor>();
builder.Services.AddSingleton<IApplicationOrchestrator, ApplicationOrchestrator>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("==========================================");
Console.WriteLine("🏢 MUTAKAMELA INSURANCE - .NET Web API");
Console.WriteLine("==========================================");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine("Endpoints:");
Console.WriteLine("  GET  /api/webhook     - Webhook verification");
Console.WriteLine("  POST /api/webhook     - Receive WhatsApp messages");
Console.WriteLine("  GET  /api/health      - Health check");
Console.WriteLine("  POST /api/chat        - Direct chat endpoint");
Console.WriteLine("  GET  /api/applications - Agentic portal jobs (flows, status, approve, cancel)");
Console.WriteLine($"  Browser agent: {app.Configuration["Agent:Browser"] ?? "simulated"}");
Console.WriteLine("  GET  /swagger         - API documentation");
Console.WriteLine("==========================================");

app.Run();
