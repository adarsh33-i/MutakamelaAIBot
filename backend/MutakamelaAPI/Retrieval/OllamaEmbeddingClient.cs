using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MutakamelaAPI.Retrieval;

public interface IEmbeddingClient
{
    /// <summary>Null when embeddings are not configured or the model is unavailable.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
    string? ModelName { get; }
}

/// <summary>
/// Thin client for Ollama's embeddings endpoint. Disabled unless
/// <c>AI:EmbeddingModel</c> is set (e.g. "bge-m3" or "nomic-embed-text").
/// After the first hard failure it switches itself off for the process
/// lifetime so a missing model never slows every chat turn.
/// </summary>
public class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaEmbeddingClient> _logger;
    private readonly string _baseUrl;
    private volatile bool _disabled;

    public string? ModelName { get; }

    public OllamaEmbeddingClient(IHttpClientFactory factory, IConfiguration config, ILogger<OllamaEmbeddingClient> logger)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(20);
        _logger = logger;
        ModelName = string.IsNullOrWhiteSpace(config["AI:EmbeddingModel"]) ? null : config["AI:EmbeddingModel"];
        // AI:BaseUrl is the OpenAI-compatible /v1 root; the native embeddings API lives one level up.
        var v1 = (config["AI:BaseUrl"] ?? "http://localhost:11434/v1").TrimEnd('/');
        _baseUrl = v1.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? v1[..^3] : v1;
        _disabled = ModelName == null;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_disabled) return null;
        try
        {
            var body = new StringContent(JsonConvert.SerializeObject(new { model = ModelName, prompt = text }), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_baseUrl}/api/embeddings", body, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Embedding model {Model} unavailable ({Status}); falling back to lexical retrieval only. Run `ollama pull {Model}`.", ModelName, (int)response.StatusCode, ModelName);
                _disabled = true;
                return null;
            }
            var json = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
            var values = json["embedding"]?.Select(v => (float)v).ToArray();
            return values is { Length: > 0 } ? values : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Embedding endpoint unreachable ({Error}); lexical retrieval only.", ex.Message);
            _disabled = true;
            return null;
        }
    }
}
