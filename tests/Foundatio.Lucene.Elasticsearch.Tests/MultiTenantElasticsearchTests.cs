using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Elasticsearch;

namespace Foundatio.Lucene.Tests;

public class MultiTenantElasticsearchTests
{
    [Fact]
    public void BuildQuery_WithPerRequestFieldMap_OverridesGlobalFieldMap()
    {
        // Arrange - Global config with one field map
        var globalFieldMap = new FieldMap { { "user", "account.user" } };
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.FieldMap = globalFieldMap;
            c.UseScoring = true;
        });

        // Act - Per-request field map overrides global
        var perRequestFieldMap = new FieldMap { { "user", "tenant.userName" } };
        var options = new ElasticsearchQueryOptions { FieldMap = perRequestFieldMap };
        var query = parser.BuildQuery("user:john", options);

        // Assert - Should use per-request field map
        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("tenant.userName", query.Match.Field?.ToString());
    }

    [Fact]
    public void BuildQuery_WithPerRequestDefaultFields_OverridesGlobal()
    {
        // Arrange
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.DefaultFields = ["title", "content"];
            c.UseScoring = true;
        });

        // Act - Override with per-request default fields
        var options = new ElasticsearchQueryOptions
        {
            DefaultFields = ["name", "description"]
        };
        var query = parser.BuildQuery("test", options);

        // Assert
        Assert.NotNull(query);
        Assert.NotNull(query.MultiMatch);
        Assert.NotNull(query.MultiMatch.Fields);
        Assert.Equal(2, query.MultiMatch.Fields.Count());
    }

    [Fact]
    public async Task BuildQueryAsync_ConcurrentRequests_ThreadSafe()
    {
        // Arrange - Single parser instance
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = true;
        });

        // Simulate 3 tenants with different configurations
        var tenant1Options = new ElasticsearchQueryOptions
        {
            FieldMap = new FieldMap { { "user", "tenant1.userName" } },
            DefaultFields = ["tenant1.title"]
        };

        var tenant2Options = new ElasticsearchQueryOptions
        {
            FieldMap = new FieldMap { { "user", "tenant2.userEmail" } },
            DefaultFields = ["tenant2.name"]
        };

        var tenant3Options = new ElasticsearchQueryOptions
        {
            FieldMap = new FieldMap { { "user", "tenant3.userFullName" } },
            DefaultFields = ["tenant3.description"]
        };

        // Act - Execute concurrent requests (300 total = very high concurrency)
        var tasks = new List<Task<Query>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => parser.BuildQuery("user:john", tenant1Options)));
            tasks.Add(Task.Run(() => parser.BuildQuery("user:jane", tenant2Options)));
            tasks.Add(Task.Run(() => parser.BuildQuery("user:bob", tenant3Options)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All queries should complete successfully and use correct field mappings
        Assert.Equal(300, results.Length);
        Assert.All(results, q => Assert.NotNull(q));

        // Verify tenant 1 queries (every 3rd starting at 0)
        for (int i = 0; i < results.Length; i += 3)
        {
            Assert.NotNull(results[i].Match);
            Assert.Equal("tenant1.userName", results[i].Match!.Field?.ToString());
        }

        // Verify tenant 2 queries (every 3rd starting at 1)
        for (int i = 1; i < results.Length; i += 3)
        {
            Assert.NotNull(results[i].Match);
            Assert.Equal("tenant2.userEmail", results[i].Match!.Field?.ToString());
        }

        // Verify tenant 3 queries (every 3rd starting at 2)
        for (int i = 2; i < results.Length; i += 3)
        {
            Assert.NotNull(results[i].Match);
            Assert.Equal("tenant3.userFullName", results[i].Match!.Field?.ToString());
        }
    }

    [Fact]
    public void BuildQuery_NoPerRequestOptions_UsesGlobalConfig()
    {
        // Arrange
        var globalFieldMap = new FieldMap { { "user", "account.user" } };
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.FieldMap = globalFieldMap;
            c.UseScoring = true;
        });

        // Act - No per-request options provided
        var query = parser.BuildQuery("user:john");

        // Assert - Should use global field map
        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("account.user", query.Match.Field?.ToString());
    }

    [Fact]
    public void BuildQuery_PerRequestOptionsEmpty_UsesGlobalConfig()
    {
        // Arrange
        var globalFieldMap = new FieldMap { { "user", "account.user" } };
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.FieldMap = globalFieldMap;
            c.UseScoring = true;
        });

        // Act - Empty per-request options
        var query = parser.BuildQuery("user:john", ElasticsearchQueryOptions.Empty);

        // Assert - Should use global field map
        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("account.user", query.Match.Field?.ToString());
    }
}
