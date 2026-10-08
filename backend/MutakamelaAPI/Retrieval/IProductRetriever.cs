using Newtonsoft.Json.Linq;

namespace MutakamelaAPI.Retrieval;

public record ProductMatch(JToken Product, double Score, double LexicalScore, double VectorScore)
{
    public string Id => Product["id"]?.ToString() ?? string.Empty;
    public string Name => Product["name"]?.ToString() ?? string.Empty;
}

/// <summary>
/// Retrieval step of the product RAG pipeline: given a customer message, return
/// the few catalog products most likely to be meant, with scores. The LLM is
/// then grounded in only those records and its answer is verified against them.
/// </summary>
public interface IProductRetriever
{
    Task<IReadOnlyList<ProductMatch>> RetrieveAsync(string query, int topK = 3, CancellationToken ct = default);

    /// <summary>Compact text for the top matches, for the LLM prompt.</summary>
    string BuildContext(IReadOnlyList<ProductMatch> matches);

    /// <summary>True when the dense (embedding) index is active in addition to lexical search.</summary>
    bool VectorSearchEnabled { get; }
}
