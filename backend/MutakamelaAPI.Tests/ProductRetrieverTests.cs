using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MutakamelaAPI.Retrieval;
using MutakamelaAPI.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MutakamelaAPI.Tests;

/// <summary>No embedding model in CI: these exercise the lexical channel and the grounding contract.</summary>
public class ProductRetrieverTests
{
    private sealed class NoEmbeddings : IEmbeddingClient
    {
        public string? ModelName => null;
        public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult<float[]?>(null);
    }

    private static ProductRetriever Build() =>
        new(new NoEmbeddings(), new ConfigurationBuilder().Build(), NullLogger<ProductRetriever>.Instance);

    [Theory]
    [InlineData("i got stuck in forest", "IND-MOT-001")]
    [InlineData("تعطلت سيارتي", "IND-MOT-001")]
    [InlineData("my shop got flooded", "CORP-PRP-001")]
    [InlineData("shipping a container from china", "CORP-MAR-001")]
    [InlineData("schengen visa insurance", "IND-TRV-002")]
    [InlineData("Visit Visa Travel Insurance", "IND-TRV-002")]
    [InlineData("customer slipped in my restaurant", "CORP-LIA-001")]
    [InlineData("employee embezzled money", "CORP-PEC-001")]
    [InlineData("steam boiler exploded", "CORP-ENG-006")]
    public async Task Top_match_is_the_expected_product(string query, string expected)
    {
        var top = (await Build().RetrieveAsync(query, 3)).FirstOrDefault();
        Assert.NotNull(top);
        Assert.Equal(expected, top!.Id);
    }

    [Fact]
    public async Task Evaluation_set_hit_rate_meets_threshold()
    {
        var retriever = Build();
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "eval_queries.jsonl");
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        int top1 = 0, top3 = 0; var misses = new List<string>();
        foreach (var line in lines)
        {
            var row = JObject.Parse(line);
            var q = row["q"]!.ToString(); var expect = row["expect"]!.ToString();
            var results = await retriever.RetrieveAsync(q, 3);
            if (results.Count > 0 && results[0].Id == expect) top1++;
            if (results.Any(r => r.Id == expect)) top3++; else misses.Add($"{q} → {string.Join("/", results.Select(r => r.Id))}");
        }
        var top1Rate = (double)top1 / lines.Count; var top3Rate = (double)top3 / lines.Count;
        Assert.True(top1Rate >= 0.85, $"top-1 {top1Rate:P0} ({top1}/{lines.Count}); misses: {string.Join(" | ", misses)}");
        Assert.True(top3Rate >= 0.95, $"top-3 {top3Rate:P0} ({top3}/{lines.Count}); misses: {string.Join(" | ", misses)}");
    }

    [Fact]
    public void Context_contains_only_retrieved_products_and_is_small()
    {
        var retriever = Build();
        var matches = retriever.RetrieveAsync("my car broke down", 3).Result;
        var ctx = retriever.BuildContext(matches);
        Assert.Contains("[IND-MOT-001]", ctx);
        Assert.DoesNotContain("[CORP-ENG-006]", ctx);
        Assert.True(ctx.Length < 4000, $"context too large: {ctx.Length} chars");
    }

    [Fact]
    public void Arabic_normalisation_unifies_variants()
    {
        var a = TextNormalizer.Tokenize("السيارة").ToList();
        var b = TextNormalizer.Tokenize("سيارتي").ToList();
        var c = TextNormalizer.Tokenize("سياره").ToList();
        Assert.Equal(a, b); Assert.Equal(b, c);
    }
}

public class RagGroundingTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    public RagGroundingTests() { _sp = _fixture.BuildProvider(); }
    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    [Theory]
    [InlineData("my shop got flooded last night", "CORP-PRP-001")]
    [InlineData("shipping a container to jeddah", "CORP-MAR-001")]
    [InlineData("ادخار لتعليم اولادي", "IND-SAV-001")]
    public async Task When_the_llm_is_unreachable_retrieval_still_returns_the_right_product(string query, string expected)
    {
        // AI endpoint is unreachable in the fixture; the grounded fallback must answer from the catalog.
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("rag" + expected, query);
        Assert.Equal(expected, r.SelectedProduct?.Id);
        Assert.NotEqual("IDENTIFY", r.Stage);
    }
}

public class CategoryVersusSpecificNeedTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    public CategoryVersusSpecificNeedTests() { _sp = _fixture.BuildProvider(); }
    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    [Fact]
    public async Task Generic_category_request_still_lists_the_category()
    {
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("cat1", "show me savings plans");
        Assert.True(r.ProductOptions.Count >= 2, "expected a list of savings products");
        Assert.True(r.ProductOptions.All(p => p.Id.StartsWith("IND-SAV")));
    }

    [Fact]
    public async Task Specific_need_in_a_category_returns_the_single_best_product()
    {
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("cat2", "I want to save for my daughter's university fees");
        Assert.Equal("IND-SAV-001", r.SelectedProduct?.Id);
    }
}
