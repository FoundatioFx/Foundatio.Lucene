using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Transport;

namespace Foundatio.Lucene.Elasticsearch.MappingTests.Integration;

/// <summary>Starts one Elasticsearch container shared by every mapping integration test.</summary>
public sealed class ElasticsearchMappingFixture : IAsyncLifetime
{
    private const int ElasticsearchPort = 9200;
    private const string Username = "elastic";
    private const string Password = "elastic_password_123";

    private readonly IContainer _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.0.0")
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

    public ElasticsearchClient Client => _client ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _client = CreateClient();
    }

    /// <summary>Creates a client for the container, optionally with additional settings such as default index mappings.</summary>
    public ElasticsearchClient CreateClient(Action<ElasticsearchClientSettings>? configure = null)
    {
        var uri = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ElasticsearchPort)}");
        var settings = new ElasticsearchClientSettings(uri)
            .Authentication(new BasicAuthentication(Username, Password))
            .DisableDirectStreaming();
        configure?.Invoke(settings);

        return new ElasticsearchClient(settings);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>Creates a uniquely named index with the sort normalizer registered and returns its name.</summary>
    public async Task<string> CreateIndexAsync<T>(Action<TypeMappingDescriptor<T>> configureMappings, string? prefix = null, string? alias = null)
    {
        string index = $"{prefix ?? "test"}-{Guid.NewGuid():N}";
        var response = await Client.Indices.CreateAsync(index, c =>
        {
            c.Settings(s => s.NumberOfReplicas(0).Analysis(a => a.AddSortNormalizer()))
                .Mappings(configureMappings);

            if (alias is not null)
                c.Aliases(alias);
        }, TestContext.Current.CancellationToken);

        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Unable to create index {index}: {response.DebugInformation}");

        return index;
    }

    public async Task IndexAndRefreshAsync(string index, object document)
    {
        var response = await Client.IndexAsync(document, d => d.Index(index).Refresh(Refresh.True), TestContext.Current.CancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Unable to index document into {index}: {response.DebugInformation}");
    }
}

[CollectionDefinition(Name)]
public sealed class ElasticsearchMappingCollection : ICollectionFixture<ElasticsearchMappingFixture>
{
    public const string Name = "ElasticsearchMapping";
}
