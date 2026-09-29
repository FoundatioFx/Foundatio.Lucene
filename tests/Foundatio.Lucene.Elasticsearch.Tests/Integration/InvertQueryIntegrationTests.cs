using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Checks that <see cref="InvertQueryVisitor"/> output matches exactly the documents the original query does not,
/// within the scope set by the non-inverted fields.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class InvertQueryIntegrationTests(ElasticsearchFixture fixture)
{
    private static readonly string[] NonInvertedFields = ["organizationId"];

    [Theory]
    [InlineData("report", "", "(NOT report)")]
    [InlineData("status:open", "", "(NOT status:open)")]
    [InlineData("NOT status:open", "", "status:open")]
    [InlineData("status:open isDeleted:false description:report", "", "(NOT (status:open isDeleted:false description:report))")]
    [InlineData("(status:open OR status:regressed)", "", "(NOT (status:open OR status:regressed))")]
    [InlineData("status:open organizationId:1", "organizationId:1", "(NOT status:open) organizationId:1")]
    [InlineData("organizationId:1 (status:open OR status:regressed)", "organizationId:1", "organizationId:1 (NOT (status:open OR status:regressed))")]
    [InlineData("(status:open organizationId:1) isDeleted:true", "organizationId:1", "(NOT (status:open isDeleted:true)) organizationId:1")]
    [InlineData("organizationId:2 status:fixed -description:gamma", "organizationId:2", "organizationId:2 (NOT (status:fixed -description:gamma))")]
    public async Task InvertQuery_WithScope_MatchesTheComplementWithinTheScope(string query, string scope, string expectedInverted)
    {
        var parser = CreateParser();

        string inverted = InvertQueryVisitor.Run(query, NonInvertedFields);
        var original = await GetIdsAsync(parser.BuildQuery(query));
        var invertedIds = await GetIdsAsync(parser.BuildQuery(inverted));
        var scoped = await GetIdsAsync(parser.BuildQuery(scope));

        Assert.Equal(expectedInverted, inverted);
        Assert.NotEmpty(original);
        Assert.NotEmpty(invertedIds);
        Assert.Empty(original.Intersect(invertedIds));
        Assert.Equal(scoped.Order(), original.Union(invertedIds).Order());
    }

    [Theory]
    [InlineData("report", "")]
    [InlineData("status:open", "")]
    [InlineData("organizationId:1 description:report", "organizationId:1")]
    [InlineData("organizationId:1 (status:open OR status:regressed)", "organizationId:1")]
    public async Task InvertQuery_WithAlternateCriteria_AddsAlternateMatchesWithinScope(string query, string scope)
    {
        var parser = CreateParser();

        string inverted = InvertQueryVisitor.Run(query, NonInvertedFields, "isDeleted:true");
        var original = await GetIdsAsync(parser.BuildQuery(query));
        var invertedIds = await GetIdsAsync(parser.BuildQuery(inverted));
        var scoped = await GetIdsAsync(parser.BuildQuery(scope));
        var deleted = await GetIdsAsync(parser.BuildQuery("isDeleted:true"));

        Assert.Equal(scoped.Except(original).Union(scoped.Intersect(deleted)).Order(), invertedIds.Order());
    }

    [Fact]
    public async Task BuildQuery_WithInvertVisitorInPipeline_BuildsTheInvertedQuery()
    {
        var parser = CreateParser(c => c.AddVisitor(new InvertQueryVisitor(NonInvertedFields), -1));
        var plain = CreateParser();

        var inverted = await GetIdsAsync(parser.BuildQuery("organizationId:1 status:open"));
        var expected = await GetIdsAsync(plain.BuildQuery("organizationId:1 -status:open"));

        Assert.Equal(expected.Order(), inverted.Order());
        Assert.Equal(18, inverted.Count);
    }

    private static ElasticsearchQueryParser CreateParser(Action<ElasticsearchQueryParserConfiguration>? configure = null) => new(c =>
    {
        c.UseMappings(InvertData.Mapping);
        c.DefaultFields = ["description"];
        configure?.Invoke(c);
    });

    private async Task<List<string>> GetIdsAsync(Query query)
    {
        string? ids = await fixture.GetMatchingIdsAsync(InvertData.Index, query, TestContext.Current.CancellationToken);
        Assert.NotNull(ids);
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
