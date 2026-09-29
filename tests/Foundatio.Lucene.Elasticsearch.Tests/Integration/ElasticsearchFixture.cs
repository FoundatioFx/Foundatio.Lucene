using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Starts one Elasticsearch container for all integration tests and seeds every test index up front.
/// </summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private const int ElasticsearchPort = 9200;
    private const string Username = "elastic";
    private const string Password = "elastic_password_123";

    private readonly IContainer _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.0")
        .WithPortBinding(ElasticsearchPort, true)
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("ELASTIC_PASSWORD", Password)
        .WithEnvironment("xpack.security.enabled", "true")
        .WithEnvironment("xpack.security.http.ssl.enabled", "false")
        .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r
                .ForPort(ElasticsearchPort)
                .ForPath("/_cluster/health")
                .WithBasicAuthentication(Username, Password)))
        .Build();

    private ElasticsearchClient? _client;

    public ElasticsearchClient Client => _client ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var uri = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ElasticsearchPort)}");
        _client = new ElasticsearchClient(new ElasticsearchClientSettings(uri)
            .Authentication(new BasicAuthentication(Username, Password))
            .DisableDirectStreaming());

        await Task.WhenAll(
            RequiredClauseData.SeedAsync(this),
            CompatibilityData.SeedAsync(this),
            DateMathData.SeedAsync(this),
            NestedData.SeedAsync(this),
            InvertData.SeedAsync(this),
            SearchData.SeedAsync(this));
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>
    /// Creates an index with one shard (so scores are deterministic), indexes the documents, and refreshes it.
    /// </summary>
    public async Task CreateIndexAsync<T>(string index, TypeMapping mapping, IEnumerable<T> documents, Action<IndexSettingsDescriptor>? settings = null) where T : class
    {
        var created = await Client.Indices.CreateAsync(index, c => c
            .Settings(s =>
            {
                s.NumberOfShards(1).NumberOfReplicas(0);
                settings?.Invoke(s);
            })
            .Mappings(mapping));
        if (!created.IsValidResponse)
            throw new InvalidOperationException($"Failed to create index {index}: {created.DebugInformation}");

        var indexed = await Client.IndexManyAsync(documents, index);
        if (!indexed.IsValidResponse || indexed.Errors)
            throw new InvalidOperationException($"Failed to index documents into {index}: {indexed.DebugInformation}");

        var refreshed = await Client.Indices.RefreshAsync(index);
        if (!refreshed.IsValidResponse)
            throw new InvalidOperationException($"Failed to refresh index {index}: {refreshed.DebugInformation}");
    }

    /// <summary>
    /// Runs a search and returns the response, which callers check for validity.
    /// </summary>
    public Task<SearchResponse<T>> SearchAsync<T>(string index, Query? query, Action<SearchRequestDescriptor<T>>? configure = null, CancellationToken cancellationToken = default)
    {
        return Client.SearchAsync<T>(s =>
        {
            s.Indices(index).Size(1000).TrackTotalHits(new Elastic.Clients.Elasticsearch.Core.Search.TrackHits(true)).AllowPartialSearchResults(false);
            if (query is not null)
                s.Query(query);
            configure?.Invoke(s);
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a search and returns the sorted, comma-separated ids of the matching documents, or null when
    /// Elasticsearch rejects the request.
    /// </summary>
    public async Task<string?> GetMatchingIdsAsync(string index, Query query, CancellationToken cancellationToken = default)
    {
        var response = await SearchAsync<object>(index, query, cancellationToken: cancellationToken);
        if (!response.IsValidResponse)
        {
            Assert.Equal(400, response.ApiCallDetails.HttpStatusCode);
            return null;
        }

        AssertComplete(response);
        return string.Join(',', response.Hits.Select(hit => hit.Id).Order(StringComparer.Ordinal));
    }

    public static void AssertComplete<T>(SearchResponse<T> response)
    {
        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.False(response.TimedOut);
        Assert.Equal(0, response.Shards.Failed);
        Assert.Equal(response.Total, response.Hits.Count);
    }
}

[CollectionDefinition(Name)]
public class ElasticsearchCollection : ICollectionFixture<ElasticsearchFixture>
{
    public const string Name = "Elasticsearch";
}
