using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Document-level checks of which nested clauses must match the same nested object.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class NestedIntegrationTests(ElasticsearchFixture fixture)
{
    [Theory]
    [InlineData("parent.child.name:Alice", "docA,docB")]
    [InlineData("parent.name:Bob AND parent.child.name:Alice", "docB")]
    [InlineData("parent.child.name:Alice AND parent.name:Bob", "docB")]
    [InlineData("(parent.name:Bob AND parent.child.name:Alice) OR (parent.name:Sue AND parent.child.name:Charlie)", "docB")]
    [InlineData("parent:(parent.name:Bob parent.child.name:Alice)", "docB")]
    [InlineData("parent.name:Bob AND NOT parent.child.name:Alice", "")]
    [InlineData("parent:(parent.name:Bob AND NOT parent.child.name:Alice)", "docA")]
    [InlineData("parent.name:Bob OR parent.child.name:Alice", "docA,docB")]
    public async Task BuildQuery_WithMultiLevelNestedFields_CorrelatesClausesUnderSharedParent(string query, string expected)
    {
        var parser = new ElasticsearchQueryParser(c => c.UseMappings(NestedData.MultiLevelMapping));

        foreach (bool scoring in new[] { false, true })
        {
            var result = parser.BuildQuery(query, new ElasticsearchQueryOptions { UseScoring = scoring });

            Assert.Equal(expected, await fixture.GetMatchingIdsAsync(NestedData.MultiLevelIndex, result, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("parent.childA.name:Alice AND parent.childB.name:Bob", BooleanOperator.And, "docB")]
    [InlineData("+parent.childA.name:Alice +parent.childB.name:Bob", BooleanOperator.Or, "docB")]
    [InlineData("parent.childA.name:Alice parent.childB.name:Bob", BooleanOperator.Or, "docA,docB")]
    [InlineData("parent.childA.name:Alice AND NOT parent.childB.name:Bob", BooleanOperator.And, "")]
    [InlineData("+parent.childA.name:Alice -parent.childB.name:Bob", BooleanOperator.Or, "")]
    [InlineData("parent:(+parent.childA.name:Alice -parent.childB.name:Bob)", BooleanOperator.Or, "docA")]
    [InlineData("parent:(parent.childA.name:Alice AND NOT parent.childB.name:Bob)", BooleanOperator.And, "docA")]
    public async Task BuildQuery_WithSiblingNestedPaths_CorrelatesPositiveClausesUnderSharedParent(string query, BooleanOperator defaultOperator, string expected)
    {
        var parser = new ElasticsearchQueryParser(c => c.UseMappings(NestedData.SiblingMapping));

        foreach (bool scoring in new[] { false, true })
        {
            var result = parser.BuildQuery(query, new ElasticsearchQueryOptions { DefaultOperator = defaultOperator, UseScoring = scoring });

            Assert.Equal(expected, await fixture.GetMatchingIdsAsync(NestedData.SiblingIndex, result, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("+title:keep -parent.childA.name:Alice -parent.childB.name:Bob")]
    [InlineData("+title:keep parent.childA.name:NotAlice -parent.childA.name:Alice -parent.childB.name:Bob")]
    public async Task BuildQuery_WithRequiredRootAndExcludedNestedSiblings_ExcludesAnyProhibitedParent(string query)
    {
        var parser = new ElasticsearchQueryParser(c => c.UseMappings(NestedData.SiblingMapping));

        foreach (bool scoring in new[] { false, true })
        {
            var result = parser.BuildQuery(query, new ElasticsearchQueryOptions { DefaultOperator = BooleanOperator.Or, UseScoring = scoring });

            Assert.Equal("clean,none", await fixture.GetMatchingIdsAsync(NestedData.ExclusionIndex, result, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("resellers.price:10", false, "p1,p2,p3,p4")]
    [InlineData("resellers.price:10", true, "p1,p2,p4")]
    [InlineData("resellers.price:10 AND resellers.stock:5", false, "p2,p3")]
    [InlineData("resellers.price:10 AND resellers.stock:5", true, "p2")]
    [InlineData("resellers:(resellers.price:10 resellers.stock:5)", true, "p2")]
    [InlineData("resellers.price:8 OR resellers.stock:5", true, "p2,p4")]
    [InlineData("NOT resellers.stock:5", true, "p1,p3")]
    public async Task BuildQueryAsync_WithNestedFilterResolver_FiltersNestedDocumentsAndKeepsCorrelation(string query, bool filtered, string expected)
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(NestedData.ResellerMapping);
            if (filtered)
                c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(new TermQuery("resellers.name", "Official"));
        });

        foreach (bool scoring in new[] { false, true })
        {
            var result = await parser.BuildQueryAsync(query, new ElasticsearchQueryOptions { UseScoring = scoring }, TestContext.Current.CancellationToken);

            Assert.Equal(expected, await fixture.GetMatchingIdsAsync(NestedData.ResellerIndex, result, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("resellers.price:10", false, "p1,p2,p4")]
    [InlineData("resellers:(resellers.price:10)", false, "p1,p2,p4")]
    [InlineData("resellers:(resellers.price:10)", true, "p2")]
    [InlineData("resellers:(resellers.price:10 AND resellers.stock:5)", false, "p2")]
    [InlineData("resellers:(resellers.price:10 OR resellers.stock:5)", false, "p1,p2,p4")]
    public async Task BuildQueryAsync_WithGroupedLeafAndContainerFilters_ExcludesDisallowedNestedDocuments(string query, bool filterContainer, string expected)
    {
        // Arrange
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(NestedData.ResellerMapping);
            c.NestedFilterResolver = (filter, _, _) => ValueTask.FromResult<Query?>(filter.ResolvedField switch
            {
                "resellers.price" or "resellers.stock" => (Query)new TermQuery("resellers.name", "Official"),
                "resellers" when filterContainer => new TermQuery("resellers.stock", 5),
                _ => null
            });
        });

        foreach (bool scoring in new[] { false, true })
        {
            // Act
            var result = await parser.BuildQueryAsync(query, new ElasticsearchQueryOptions { UseScoring = scoring }, TestContext.Current.CancellationToken);
            string? matchingIds = await fixture.GetMatchingIdsAsync(NestedData.ResellerIndex, result, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(expected, matchingIds);
        }
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterPerDefaultField_MatchesOnlyTheRightDiscriminator()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(NestedData.ItemMapping);
            c.DefaultFields = ["items.status", "items.priority"];
            c.NestedFilterResolver = (filter, _, _) => ValueTask.FromResult<Query?>(filter.ResolvedField switch
            {
                "items.status" => (Query)new TermQuery("items.type", "status_filter"),
                "items.priority" => new TermQuery("items.type", "priority_filter"),
                _ => null
            });
        });

        var result = await parser.BuildQueryAsync("active", new ElasticsearchQueryOptions { UseScoring = true }, TestContext.Current.CancellationToken);

        Assert.Equal("doc1,doc2", await fixture.GetMatchingIdsAsync(NestedData.ItemIndex, result, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildQueryAsync_WithServerMapping_LoadsNestedPathsFromIndex()
    {
        var parser = new ElasticsearchQueryParser(c => c.UseMappings(ElasticMappingResolver.Create(fixture.Client, NestedData.MultiLevelIndex)));

        var result = await parser.BuildQueryAsync("PARENT.NAME:Bob AND parent.child.name:Alice", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("docB", await fixture.GetMatchingIdsAsync(NestedData.MultiLevelIndex, result, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildQuery_WithoutNestedQueries_MatchesAcrossNestedObjects()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(NestedData.ResellerMapping);
            c.UseNested = false;
        });

        var result = parser.BuildQuery("resellers.price:10 AND resellers.stock:5");

        Assert.Equal("", await fixture.GetMatchingIdsAsync(NestedData.ResellerIndex, result, TestContext.Current.CancellationToken));
    }
}
