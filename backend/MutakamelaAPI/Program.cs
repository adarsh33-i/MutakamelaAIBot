using MutakamelaAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IWhatsAppService, WhatsAppService>();
builder.Services.AddSingleton<IAIPolicyService, AIPolicyService>();
builder.Services.AddSingleton<ISessionManager, SessionManager>();

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
Console.WriteLine("  GET  /swagger         - API documentation");
Console.WriteLine("==========================================");

app.Run();
