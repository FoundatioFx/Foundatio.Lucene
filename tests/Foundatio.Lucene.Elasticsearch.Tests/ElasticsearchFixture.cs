using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace Foundatio.Lucene.Elasticsearch.Tests;

/// <summary>
/// xUnit fixture that manages an Elasticsearch container for integration tests.
/// The container is started once and shared across all tests in the collection.
/// </summary>
public class ElasticsearchFixture : IAsyncLifetime
{
    private readonly IContainer _container;
    private ElasticsearchClient? _client;

    public const string TestIndexName = "test-documents";
    private const int ElasticsearchPort = 9200;
    private const string Username = "elastic";
    private const string Password = "elastic_password_123";

    public ElasticsearchFixture()
    {
        // Use Elasticsearch 9.x which is required for the Elastic.Clients.Elasticsearch 9.x client
        // The 9.x client sends version 9 headers that ES 8.x rejects
        // Use generic container builder for ES 9.x since Testcontainers.Elasticsearch doesn't support it yet
        _container = new ContainerBuilder()
            .WithImage("docker.elastic.co/elasticsearch/elasticsearch:9.0.0")
            .WithPortBinding(ElasticsearchPort, true)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("ELASTIC_PASSWORD", Password)
            .WithEnvironment("xpack.security.enabled", "true")
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(ElasticsearchPort)
                    .ForPath("/_cluster/health")
                    .WithBasicAuthentication(Username, Password)))
            .Build();
    }

    public ElasticsearchClient Client => _client
        ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public ElasticsearchQueryParser Parser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(ElasticsearchPort);
        var uri = new Uri($"http://{host}:{port}");

        var settings = new ElasticsearchClientSettings(uri)
            .Authentication(new BasicAuthentication(Username, Password));

        _client = new ElasticsearchClient(settings);

        Parser = new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = true;
            c.DefaultFields = ["title", "content", "author"];
        });

        await CreateIndexAndSeedDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }

    private async Task CreateIndexAndSeedDataAsync()
    {
        // Create the index with mappings
        var createIndexResponse = await Client.Indices.CreateAsync<TestDocument>(TestIndexName, c => c
            .Mappings(m => m
                .Properties(p => p
                    .Text(t => t.Title)
                    .Text(t => t.Content)
                    .Text(t => t.Author)
                    .IntegerNumber(t => t.Year)
                    .FloatNumber(t => t.Price)
                    .Date(t => t.PublishedDate)
                    .Keyword(t => t.Category)
                    .Keyword(t => t.Tags)
                    .Boolean(t => t.IsPublished)
                    .GeoPoint(t => t.Location)
                )
            )
        );

        if (!createIndexResponse.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to create index: {createIndexResponse.DebugInformation}");
        }

        // Seed test data
        var documents = new List<TestDocument>
        {
            new()
            {
                Id = "1",
                Title = "Introduction to Elasticsearch",
                Content = "Elasticsearch is a distributed search and analytics engine.",
                Author = "John Smith",
                Year = 2023,
                Price = 29.99f,
                PublishedDate = new DateTime(2023, 1, 15),
                Category = "technology",
                Tags = ["search", "database", "nosql"],
                IsPublished = true,
                Location = new GeoLocation { Lat = 40.7128, Lon = -74.0060 }
            },
            new()
            {
                Id = "2",
                Title = "Advanced Lucene Queries",
                Content = "Learn how to write complex Lucene query syntax.",
                Author = "Jane Doe",
                Year = 2022,
                Price = 39.99f,
                PublishedDate = new DateTime(2022, 6, 20),
                Category = "technology",
                Tags = ["lucene", "search", "query"],
                IsPublished = true,
                Location = new GeoLocation { Lat = 34.0522, Lon = -118.2437 }
            },
            new()
            {
                Id = "3",
                Title = "Database Design Patterns",
                Content = "Explore common patterns for designing scalable databases.",
                Author = "John Smith",
                Year = 2024,
                Price = 49.99f,
                PublishedDate = new DateTime(2024, 3, 10),
                Category = "database",
                Tags = ["database", "patterns", "design"],
                IsPublished = true,
                Location = new GeoLocation { Lat = 51.5074, Lon = -0.1278 }
            },
            new()
            {
                Id = "4",
                Title = "Unpublished Draft",
                Content = "This is a draft document that is not yet published.",
                Author = "Bob Wilson",
                Year = 2024,
                Price = 0.0f,
                PublishedDate = null,
                Category = "draft",
                Tags = ["draft"],
                IsPublished = false,
                Location = null
            },
            new()
            {
                Id = "5",
                Title = "Machine Learning Basics",
                Content = "An introduction to machine learning concepts and algorithms.",
                Author = "Alice Johnson",
                Year = 2023,
                Price = 59.99f,
                PublishedDate = new DateTime(2023, 9, 5),
                Category = "technology",
                Tags = ["ml", "ai", "algorithms"],
                IsPublished = true,
                Location = new GeoLocation { Lat = 37.7749, Lon = -122.4194 }
            }
        };

        var bulkResponse = await Client.BulkAsync(b => b
            .Index(TestIndexName)
            .IndexMany(documents)
            .Refresh(Refresh.True)
        );

        if (!bulkResponse.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index documents: {bulkResponse.DebugInformation}");
        }
    }
}

public class TestDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int Year { get; set; }
    public float Price { get; set; }
    public DateTime? PublishedDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public bool IsPublished { get; set; }
    public GeoLocation? Location { get; set; }
}

public class GeoLocation
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

[CollectionDefinition("Elasticsearch")]
public class ElasticsearchCollection : ICollectionFixture<ElasticsearchFixture>
{
}
