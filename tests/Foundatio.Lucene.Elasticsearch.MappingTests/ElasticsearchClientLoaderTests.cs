using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Transport;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

/// <summary>The client-backed server loader, exercised against canned get-mapping responses.</summary>
public class ElasticsearchClientLoaderTests(ITestOutputHelper output) : MappingTestBase(output)
{
    private const string MultiIndexResponse = """
        {
          "logs-2024.01": { "mappings": { "properties": {
            "old_only": { "type": "keyword" },
            "shared": { "type": "text" },
            "obj": { "properties": { "old_child": { "type": "keyword" } } },
            "message": { "type": "text", "fields": { "keyword": { "type": "keyword" } } }
          } } },
          "logs-2024.02": { "mappings": { "_meta": { "version": "2" }, "properties": {
            "new_only": { "type": "long" },
            "shared": { "type": "keyword" },
            "obj": { "properties": { "new_child": { "type": "date" } } },
            "message": { "type": "text" }
          } } }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithSingleIndex_ResolvesThroughSynchronousAndAsynchronousClientApis(bool asynchronous)
    {
        // Arrange
        int requests = 0;
        var client = CreateClient("""{ "test": { "mappings": { "properties": { "title": { "type": "text", "fields": { "keyword": { "type": "keyword" } } } } } } }""",
            200, () => Interlocked.Increment(ref requests));
        using var resolver = ElasticMappingResolver.Create(client, "test", Logger);

        // Act
        if (asynchronous)
            Assert.True(await resolver.EnsureFieldsAsync(["title"], TestCancellationToken));

        string aggregation = resolver.GetAggregationsFieldName("Title");

        // Assert
        Assert.True(resolver.CanResolveSynchronously);
        Assert.True(resolver.IsLoaded);
        Assert.Equal("title.keyword", aggregation);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Create_WithCodeMappingAndClient_MergesCodeSubFields()
    {
        // Arrange
        var client = CreateClient("""{ "test": { "mappings": { "properties": { "title": { "type": "text" } } } } }""", 200);
        using var resolver = ElasticMappingResolver.Create<Document>(m => m.Properties(p => p
            .Text(d => d.Title!, t => t.AddKeywordAndSortFields())
            .Keyword(d => d.Status!)), client, "test", Logger);

        // Act & Assert
        Assert.Equal("title.sort", resolver.GetSortFieldName("title"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("status"));
    }

    [Fact]
    public async Task Create_WithTargetResolvingToSeveralIndices_MergesMappingsNewestIndexFirst()
    {
        // Arrange
        var logger = new CapturingLogger();
        var client = CreateClient(MultiIndexResponse, 200);
        using var resolver = ElasticMappingResolver.Create(client, "logs-*", logger);

        // Act
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("old_only"));
        Assert.IsType<LongNumberProperty>(resolver.GetMappingProperty("new_only"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("shared"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("obj.old_child"));
        Assert.IsType<DateProperty>(resolver.GetMappingProperty("obj.new_child"));
        Assert.Equal("message.keyword", resolver.GetAggregationsFieldName("message"));
        var conflict = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("shared (keyword vs text)", conflict.Message);
    }

    [Fact]
    public void Create_WithRevisionResolverAndSeveralIndices_SeesMergedMappingLevelSettings()
    {
        // Arrange
        var client = CreateClient(MultiIndexResponse, 200);
        using var resolver = ElasticMappingResolver.Create(client, "logs-*", Logger);
        string? revision = null;
        resolver.ServerMappingRevisionResolver = mapping => revision = mapping.Meta?["version"]?.ToString();

        // Act
        bool found = resolver.GetMapping("new_only").Found;

        // Assert - mapping level settings come from the newest index.
        Assert.True(found);
        Assert.Equal("2", revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithErrorResponse_TreatsServerMappingAsUnavailable(bool asynchronous)
    {
        // Arrange
        var logger = new CapturingLogger();
        var client = CreateClient("""{ "error": { "type": "index_not_found_exception", "reason": "no such index [missing]" }, "status": 404 }""", 404);
        using var resolver = ElasticMappingResolver.Create(client, "missing", logger);

        // Act
        if (asynchronous)
            await resolver.EnsureLoadedAsync(TestCancellationToken);

        var mapping = resolver.GetMapping("title");

        // Assert
        Assert.False(mapping.Found);
        Assert.False(resolver.IsLoaded);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("no such index [missing]"));
    }

    [Fact]
    public void Create_WithEmptyResponse_TreatsServerMappingAsUnavailable()
    {
        // Arrange
        var client = CreateClient("{}", 200);
        using var resolver = ElasticMappingResolver.Create(client, "nothing-*", Logger);

        // Act
        var mapping = resolver.GetMapping("title");

        // Assert
        Assert.False(mapping.Found);
        Assert.False(resolver.IsLoaded);
    }

    private static ElasticsearchClient CreateClient(string responseBody, int statusCode, Action? onRequest = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase) { ["x-elastic-product"] = ["Elasticsearch"] };
        var invoker = new InMemoryRequestInvoker(Encoding.UTF8.GetBytes(responseBody), statusCode, null, "application/json", headers);
        var settings = new ElasticsearchClientSettings(new SingleNodePool(new Uri("http://localhost:9200")), invoker)
            .DisableDirectStreaming()
            .OnRequestCompleted(_ => onRequest?.Invoke());

        return new ElasticsearchClient(settings);
    }

    private sealed class Document
    {
        public string? Title { get; set; }

        public string? Status { get; set; }
    }
}
