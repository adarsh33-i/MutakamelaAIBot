using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MutakamelaAPI.Retrieval;

/// <summary>
/// Hybrid retriever over the 27-product catalog.
///
/// Lexical channel: BM25 over a bilingual "document" built from each product's
/// name, description, coverage, keywords, suitable_for and the curated
/// <c>situations</c> phrases. Always available, no model needed.
///
/// Dense channel: cosine similarity between an Ollama embedding of the query and
/// pre-computed product embeddings (cached on disk, keyed by catalog hash).
/// Active only when <c>AI:EmbeddingModel</c> is configured and reachable.
///
/// The two channels are min-max normalised and blended; exact name/id mentions
/// are boosted so "Visit Visa Travel Insurance" always wins over semantics.
/// </summary>
public class ProductRetriever : IProductRetriever
{
    private const double LexicalWeight = 0.55;
    private const double VectorWeight = 0.45;
    private const double K1 = 1.4, B = 0.75;

    private readonly IEmbeddingClient _embeddings;
    private readonly ILogger<ProductRetriever> _logger;
    private readonly List<JToken> _products = new();
    private readonly List<ProductDoc> _docs = new();
    private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);
    private double _avgDocLength;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private Dictionary<string, float[]>? _vectors;
    private bool _vectorIndexAttempted;

    public bool VectorSearchEnabled => _vectors is { Count: > 0 };

    private sealed class ProductDoc
    {
        public string Id = string.Empty;
        public Dictionary<string, int> TermFreq = new(StringComparer.Ordinal);
        public int Length;
        public string EmbeddingText = string.Empty;
        public HashSet<string> ExactNames = new(StringComparer.OrdinalIgnoreCase);
    }

    public ProductRetriever(IEmbeddingClient embeddings, IConfiguration config, ILogger<ProductRetriever> logger)
    {
        _embeddings = embeddings;
        _logger = logger;
        _cachePath = Path.Combine(config["Agent:DataDir"] is { } d ? Path.GetDirectoryName(Path.GetFullPath(d))! : Path.Combine(AppContext.BaseDirectory, "App_Data"), "product-embeddings.json");

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "products.json");
        try
        {
            if (JObject.Parse(File.ReadAllText(path))["products"] is JArray products)
                foreach (var p in products) _products.Add(p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductRetriever could not load {Path}", path);
        }
        BuildLexicalIndex();
    }

    // ------------------------------------------------------------ indexing

    private void BuildLexicalIndex()
    {
        foreach (var p in _products)
        {
            var doc = new ProductDoc { Id = p["id"]?.ToString() ?? string.Empty };
            string Join(string key) => p[key] is JArray a ? string.Join(". ", a.Select(x => x.ToString())) : p[key]?.ToString() ?? string.Empty;

            // Situations and keywords are repeated so customer phrasing outweighs marketing copy.
            var weighted = new StringBuilder()
                .AppendLine(p["name"]?.ToString()).AppendLine(p["name"]?.ToString())
                .AppendLine(p["name_ar"]?.ToString()).AppendLine(p["name_ar"]?.ToString())
                .AppendLine(p["lob"]?.ToString())
                .AppendLine(Join("situations")).AppendLine(Join("situations"))
                .AppendLine(Join("situations_ar")).AppendLine(Join("situations_ar"))
                .AppendLine(Join("keywords")).AppendLine(Join("keywords"))
                .AppendLine(Join("coverage")).AppendLine(Join("coverage_ar"))
                .AppendLine(Join("suitable_for"))
                .AppendLine(p["description"]?.ToString()).AppendLine(p["description_ar"]?.ToString())
                .ToString();

            foreach (var token in TextNormalizer.Tokenize(weighted))
            {
                doc.TermFreq[token] = doc.TermFreq.GetValueOrDefault(token) + 1;
                doc.Length++;
            }
            foreach (var term in doc.TermFreq.Keys) _docFreq[term] = _docFreq.GetValueOrDefault(term) + 1;

            doc.ExactNames.Add(doc.Id);
            foreach (var key in new[] { "name", "name_ar" })
                if (p[key]?.ToString() is { Length: > 0 } n) doc.ExactNames.Add(n);

            doc.EmbeddingText =
                $"{p["name"]} ({p["name_ar"]}). {p["lob"]} insurance. {p["description"]} " +
                $"Coverage: {Join("coverage")}. Suitable for: {Join("suitable_for")}. " +
                $"Typical situations: {Join("situations")}. {Join("situations_ar")}. Keywords: {Join("keywords")}.";
            _docs.Add(doc);
        }
        _avgDocLength = _docs.Count == 0 ? 1 : _docs.Average(d => d.Length);
        _logger.LogInformation("Product lexical index built: {Products} products, {Terms} terms", _docs.Count, _docFreq.Count);
    }

    private string CatalogHash()
    {
        var text = string.Concat(_docs.Select(d => d.EmbeddingText));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + _embeddings.ModelName)))[..16];
    }

    private async Task EnsureVectorIndexAsync(CancellationToken ct)
    {
        if (_vectorIndexAttempted || _embeddings.ModelName == null) return;
        await _indexLock.WaitAsync(ct);
        try
        {
            if (_vectorIndexAttempted) return;
            _vectorIndexAttempted = true;
            var hash = CatalogHash();

            try
            {
                if (File.Exists(_cachePath))
                {
                    var cached = JObject.Parse(await File.ReadAllTextAsync(_cachePath, ct));
                    if (cached["hash"]?.ToString() == hash && cached["vectors"] is JObject vs)
                    {
                        _vectors = vs.Properties().ToDictionary(p => p.Name, p => p.Value.Select(v => (float)v).ToArray());
                        _logger.LogInformation("Loaded {Count} cached product embeddings", _vectors.Count);
                        return;
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Ignoring unreadable embedding cache"); }

            var vectors = new Dictionary<string, float[]>();
            foreach (var doc in _docs)
            {
                var v = await _embeddings.EmbedAsync(doc.EmbeddingText, ct);
                if (v == null) { _logger.LogInformation("Dense retrieval disabled (no embeddings)."); return; }
                vectors[doc.Id] = v;
            }
            _vectors = vectors;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                await File.WriteAllTextAsync(_cachePath, JsonConvert.SerializeObject(new { hash, model = _embeddings.ModelName, vectors }), ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not write embedding cache"); }
            _logger.LogInformation("Built {Count} product embeddings with {Model}", vectors.Count, _embeddings.ModelName);
        }
        finally { _indexLock.Release(); }
    }

    // ------------------------------------------------------------ retrieval

    public async Task<IReadOnlyList<ProductMatch>> RetrieveAsync(string query, int topK = 3, CancellationToken ct = default)
    {
        if (_docs.Count == 0 || string.IsNullOrWhiteSpace(query)) return Array.Empty<ProductMatch>();
        await EnsureVectorIndexAsync(ct);

        var lexical = LexicalScores(query);
        var dense = VectorSearchEnabled ? await DenseScoresAsync(query, ct) : null;

        var lexNorm = Normalize(lexical);
        var denseNorm = dense == null ? null : Normalize(dense);

        var results = new List<ProductMatch>();
        for (var i = 0; i < _docs.Count; i++)
        {
            var doc = _docs[i];
            var lex = lexNorm[i];
            var vec = denseNorm?[i] ?? 0;
            var score = denseNorm == null ? lex : LexicalWeight * lex + VectorWeight * vec;

            // Exact product name / id mention dominates everything else.
            if (doc.ExactNames.Any(n => query.Contains(n, StringComparison.OrdinalIgnoreCase))) score += 1.0;

            if (score > 0) results.Add(new ProductMatch(_products[i], score, lex, vec));
        }
        return results.OrderByDescending(r => r.Score).Take(topK).ToList();
    }

    private double[] LexicalScores(string query)
    {
        var terms = TextNormalizer.Tokenize(query).Distinct().ToList();
        var scores = new double[_docs.Count];
        if (terms.Count == 0) return scores;
        var n = _docs.Count;
        for (var i = 0; i < n; i++)
        {
            var doc = _docs[i];
            double s = 0;
            foreach (var term in terms)
            {
                if (!doc.TermFreq.TryGetValue(term, out var tf)) continue;
                var df = _docFreq.GetValueOrDefault(term);
                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                s += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * doc.Length / _avgDocLength));
            }
            scores[i] = s;
        }
        return scores;
    }

    private async Task<double[]?> DenseScoresAsync(string query, CancellationToken ct)
    {
        var q = await _embeddings.EmbedAsync(query, ct);
        if (q == null || _vectors == null) return null;
        var scores = new double[_docs.Count];
        for (var i = 0; i < _docs.Count; i++)
            scores[i] = _vectors.TryGetValue(_docs[i].Id, out var v) ? Cosine(q, v) : 0;
        return scores;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static double[] Normalize(double[] values)
    {
        var max = values.Max();
        var min = values.Min();
        if (max <= 0 || max - min < 1e-9) return values.Select(v => v > 0 ? 1.0 : 0.0).ToArray();
        return values.Select(v => (v - min) / (max - min)).ToArray();
    }

    // ------------------------------------------------------------ prompt context

    public string BuildContext(IReadOnlyList<ProductMatch> matches)
    {
        if (matches.Count == 0) return "(no closely matching products)";
        var sb = new StringBuilder();
        foreach (var m in matches)
        {
            var p = m.Product;
            string Join(string key, int take) => p[key] is JArray a ? string.Join(", ", a.Select(x => x.ToString()).Take(take)) : string.Empty;
            sb.AppendLine($"[{p["id"]}] {p["name"]} ({p["name_ar"]}) — {p["lob"]} — relevance {m.Score:0.00}");
            sb.AppendLine($"  Description: {p["description"]}");
            sb.AppendLine($"  Coverage: {Join("coverage", 6)}");
            sb.AppendLine($"  Features: {Join("features", 4)}");
            sb.AppendLine($"  Suitable for: {Join("suitable_for", 4)}");
            sb.AppendLine($"  URL: {p["url"]}");
        }
        return sb.ToString().TrimEnd();
    }
}
