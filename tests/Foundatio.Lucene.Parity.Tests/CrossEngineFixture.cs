using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Foundatio.Lucene.Elasticsearch;
using Foundatio.Lucene.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Foundatio.Lucene.Parity.Tests;

/// <summary>
/// Starts a SQL Server and an Elasticsearch container, seeds identical data into both, and runs
/// the same Lucene query through the EntityFramework and Elasticsearch parsers so tests can assert
/// the two engines return the same result set for the supported-on-both construct set.
/// </summary>
public class CrossEngineFixture : IAsyncLifetime
{
    private const string IndexName = "parity-docs";
    private const int ElasticsearchPort = 9200;
    private const string EsUser = "elastic";
    private const string EsPassword = "elastic_password_123";

    private readonly MsSqlContainer _sql;
    private readonly IContainer _es;
    private ElasticsearchClient _esClient = null!;
    private string _connectionString = null!;

    // Names and categories are deliberately distinct, non-substring values so SQL Contains and ES
    // keyword term matching agree (true result-set parity for equality queries). Dates sit well
    // inside the test range boundaries so inclusive/exclusive day rounding never flips a result.
    private static readonly Doc[] SeedDocs =
    [
        new() { Id = 1, Name = "alpha",   Category = "engineering", Age = 30, Salary = 80000,  Active = true,  Created = new DateTime(2020, 6, 15), Notes = "note-one" },
        new() { Id = 2, Name = "bravo",   Category = "sales",       Age = 35, Salary = 95000,  Active = true,  Created = new DateTime(2019, 3, 10), Notes = null },
        new() { Id = 3, Name = "charlie", Category = "research",    Age = 40, Salary = 110000, Active = true,  Created = new DateTime(2018, 9, 20), Notes = "note-three" },
        new() { Id = 4, Name = "delta",   Category = "engineering", Age = 25, Salary = 55000,  Active = false, Created = new DateTime(2022, 11, 5), Notes = null },
        new() { Id = 5, Name = "echo",    Category = "research",    Age = 50, Salary = 130000, Active = false, Created = new DateTime(2021, 1, 25), Notes = "note-five" },
    ];

    public EntityFrameworkQueryParser EfParser { get; } = new();

    public ElasticsearchQueryParser EsParser { get; } = new(c =>
    {
        // Filter context (term/range) so string equality matches SQL's exact-ish Contains on the
        // non-substring test data, and date fields produce real date range queries.
        c.UseScoring = false;
        c.IsDateField = f => string.Equals(f, "created", StringComparison.OrdinalIgnoreCase);
    });

    public CrossEngineFixture()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        // Testcontainers.Elasticsearch does not yet support 9.x, so build the container directly
        // (mirrors the Elasticsearch.Tests fixture).
        _es = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.0.0")
            .WithPortBinding(ElasticsearchPort, true)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("ELASTIC_PASSWORD", EsPassword)
            .WithEnvironment("xpack.security.enabled", "true")
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(ElasticsearchPort)
                    .ForPath("/_cluster/health")
                    .WithBasicAuthentication(EsUser, EsPassword)))
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _es.StartAsync());

        _connectionString = _sql.GetConnectionString();

        var uri = new Uri($"http://{_es.Hostname}:{_es.GetMappedPublicPort(ElasticsearchPort)}");
        _esClient = new ElasticsearchClient(new ElasticsearchClientSettings(uri)
            .Authentication(new BasicAuthentication(EsUser, EsPassword)));

        await SeedSqlAsync();
        await SeedElasticsearchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sql.DisposeAsync();
        await _es.DisposeAsync();
    }

    /// <summary>Runs the query through the EF parser against SQL Server and returns matching ids.</summary>
    public List<int> QuerySql(string query)
    {
        using var db = CreateDb();
        var filter = EfParser.BuildFilter<Doc>(query);
        return db.Docs.Where(filter).Select(d => d.Id).ToList();
    }

    /// <summary>Runs the query through the ES parser against Elasticsearch and returns matching ids.</summary>
    public async Task<List<int>> QueryElasticsearchAsync(string query)
    {
        var esQuery = EsParser.BuildQuery(query);
        var response = await _esClient.SearchAsync<Doc>(s => s
            .Indices(IndexName)
            .Size(100)
            .Query(esQuery));

        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Elasticsearch query failed: {response.DebugInformation}");

        return response.Documents.Select(d => d.Id).ToList();
    }

    private ParityDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ParityDbContext>().UseSqlServer(_connectionString).Options);

    private async Task SeedSqlAsync()
    {
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        db.Docs.AddRange(SeedDocs);
        await db.SaveChangesAsync();
    }

    private async Task SeedElasticsearchAsync()
    {
        var create = await _esClient.Indices.CreateAsync<Doc>(IndexName, c => c
            .Mappings(m => m
                .Properties(p => p
                    .IntegerNumber(d => d.Id)
                    .Keyword(d => d.Name)
                    .Keyword(d => d.Category)
                    .IntegerNumber(d => d.Age)
                    .DoubleNumber(d => d.Salary)
                    .Boolean(d => d.Active)
                    .Date(d => d.Created)
                    .Keyword(d => d.Notes!))));

        if (!create.IsValidResponse)
            throw new InvalidOperationException($"Failed to create index: {create.DebugInformation}");

        var bulk = await _esClient.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(SeedDocs)
            .Refresh(Refresh.True));

        if (!bulk.IsValidResponse)
            throw new InvalidOperationException($"Failed to seed Elasticsearch: {bulk.DebugInformation}");
    }
}

/// <summary>Document shared by the SQL (EF entity) and Elasticsearch (indexed document) sides.</summary>
public class Doc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Age { get; set; }
    public double Salary { get; set; }
    public bool Active { get; set; }
    public DateTime Created { get; set; }
    public string? Notes { get; set; }
}

public class ParityDbContext(DbContextOptions<ParityDbContext> options) : DbContext(options)
{
    public DbSet<Doc> Docs => Set<Doc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use the seeded ids verbatim so they line up with the Elasticsearch documents.
        modelBuilder.Entity<Doc>().Property(d => d.Id).ValueGeneratedNever();
    }
}

[CollectionDefinition("CrossEngine")]
public class CrossEngineCollection : ICollectionFixture<CrossEngineFixture>;
