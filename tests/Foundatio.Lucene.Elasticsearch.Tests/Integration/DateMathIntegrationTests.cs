using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Checks date math ranges with rounding and inclusive or exclusive bounds three ways: a native
/// <c>range</c> query, the parser (which passes date math through to Elasticsearch, or evaluates it itself with
/// <see cref="DateMathEvaluatorVisitor"/>), and Elasticsearch's <c>query_string</c> parser.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class DateMathIntegrationTests(ElasticsearchFixture fixture)
{
    [Theory]
    [InlineData("timestamp:[2024-01-15||/d TO 2024-01-15||/d]", null, "2024-01-15||/d", null, "2024-01-15||/d", null, "A,B,C")]
    [InlineData("timestamp:[2024-01-15||/d TO 2024-01-16||/d}", null, "2024-01-15||/d", null, null, "2024-01-16||/d", "A,B,C")]
    [InlineData("timestamp:[2024-01-15||/d TO 2024-01-15||/d}", null, "2024-01-15||/d", null, null, "2024-01-15||/d", "")]
    [InlineData("timestamp:{2024-01-14||/d TO 2024-01-15||/d]", null, null, "2024-01-14||/d", "2024-01-15||/d", null, "A,B,C")]
    [InlineData("timestamp:{2024-01-15||/d TO 2024-01-15||/d]", null, null, "2024-01-15||/d", "2024-01-15||/d", null, "")]
    [InlineData("timestamp:{2024-01-14||/d TO 2024-01-16||/d}", null, null, "2024-01-14||/d", null, "2024-01-16||/d", "A,B,C")]
    [InlineData("timestamp:{2024-01-15||/d TO 2024-01-15||/d}", null, null, "2024-01-15||/d", null, "2024-01-15||/d", "")]
    [InlineData("timestamp:[2024-01-15||/M TO 2024-01-15||/M]", null, "2024-01-15||/M", null, "2024-01-15||/M", null, "A,B,C,D,E,G")]
    [InlineData("timestamp:[2024-01-15||/M TO 2024-02-01||/M}", null, "2024-01-15||/M", null, null, "2024-02-01||/M", "A,B,C,D,E,G")]
    [InlineData("timestamp:{2024-01-15||/M TO 2024-02-01||/M]", null, null, "2024-01-15||/M", "2024-02-01||/M", null, "F")]
    [InlineData("timestamp:{2024-01-15||/M TO 2024-02-01||/M}", null, null, "2024-01-15||/M", null, "2024-02-01||/M", "")]
    [InlineData("timestamp:[2024-01-15T12:30:00Z||/h TO 2024-01-15T12:30:00Z||/h]", null, "2024-01-15T12:30:00Z||/h", null, "2024-01-15T12:30:00Z||/h", null, "B")]
    [InlineData("timestamp:[2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}", null, "2024-01-15T00:00:00Z||/h", null, null, "2024-01-15T12:00:00Z||/h", "A")]
    [InlineData("timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:30:00Z||/h]", null, null, "2024-01-15T00:00:00Z||/h", "2024-01-15T12:30:00Z||/h", null, "B")]
    [InlineData("timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}", null, null, "2024-01-15T00:00:00Z||/h", null, "2024-01-15T12:00:00Z||/h", "")]
    [InlineData("timestamp:[2024-01-14||+1d/d TO 2024-01-14||+2d/d]", null, "2024-01-14||+1d/d", null, "2024-01-14||+2d/d", null, "A,B,C,D")]
    [InlineData("timestamp:[2024-01-16||-1d/d TO 2024-01-16||/d}", null, "2024-01-16||-1d/d", null, null, "2024-01-16||/d", "A,B,C")]
    [InlineData("timestamp:[2024-01-14||/d TO 2024-01-16||/d]", null, "2024-01-14||/d", null, "2024-01-16||/d", null, "A,B,C,D,E")]
    [InlineData("timestamp:[* TO now/d]", null, null, null, "now/d", null, "A,B,C,D,E,F,G")]
    [InlineData("timestamp:{* TO now/d}", null, null, null, null, "now/d", "A,B,C,D,E,F,G")]
    [InlineData("timestamp:[2024-06-15||/y TO 2024-06-15||/y]", null, "2024-06-15||/y", null, "2024-06-15||/y", null, "A,B,C,D,E,F,G")]
    [InlineData("timestamp:[2024-06-15||/y TO 2024-06-15||/y}", null, "2024-06-15||/y", null, null, "2024-06-15||/y", "")]
    [InlineData("timestamp:{2023-06-15||/y TO 2024-06-15||/y]", null, null, "2023-06-15||/y", "2024-06-15||/y", null, "A,B,C,D,E,F,G")]
    [InlineData("timestamp:{2024-06-15||/y TO 2024-06-15||/y}", null, null, "2024-06-15||/y", null, "2024-06-15||/y", "")]
    [InlineData("timestamp:>2024-01-15||/d", "timestamp:>2024-01-15||\\/d", null, "2024-01-15||/d", null, null, "D,F,G")]
    [InlineData("timestamp:>=2024-01-15||/d", "timestamp:>=2024-01-15||\\/d", "2024-01-15||/d", null, null, null, "A,B,C,D,F,G")]
    [InlineData("timestamp:<2024-01-15||/d", "timestamp:<2024-01-15||\\/d", null, null, null, "2024-01-15||/d", "E")]
    [InlineData("timestamp:<=2024-01-15||/d", "timestamp:<=2024-01-15||\\/d", null, null, "2024-01-15||/d", null, "A,B,C,E")]
    [InlineData("timestamp:>2024-01-15||/M", "timestamp:>2024-01-15||\\/M", null, "2024-01-15||/M", null, null, "F")]
    [InlineData("timestamp:<=2024-01-15||/M", "timestamp:<=2024-01-15||\\/M", null, null, "2024-01-15||/M", null, "A,B,C,D,E,G")]
    [InlineData("timestamp:>now/d", "timestamp:>now\\/d", null, "now/d", null, null, "")]
    [InlineData("timestamp:<now/d", "timestamp:<now\\/d", null, null, null, "now/d", "A,B,C,D,E,F,G")]
    [InlineData("timestamp:>=2024-01-14||+1d/d", "timestamp:>=2024-01-14||+1d\\/d", "2024-01-14||+1d/d", null, null, null, "A,B,C,D,F,G")]
    [InlineData("timestamp:<2024-01-16||-1d/d", "timestamp:<2024-01-16||-1d\\/d", null, null, null, "2024-01-16||-1d/d", "E")]
    [InlineData("timestamp:>2024-01-15T12:30:00Z||/h", "timestamp:>2024-01-15T12\\:30\\:00Z||\\/h", null, "2024-01-15T12:30:00Z||/h", null, null, "C,D,F,G")]
    [InlineData("timestamp:<=2024-01-15T12:30:00Z||/h", "timestamp:<=2024-01-15T12\\:30\\:00Z||\\/h", null, null, "2024-01-15T12:30:00Z||/h", null, "A,B,E")]
    [InlineData("timestamp:>=2024-06-15||/y", "timestamp:>=2024-06-15||\\/y", "2024-06-15||/y", null, null, null, "A,B,C,D,E,F,G")]
    [InlineData("timestamp:<2024-06-15||/y", "timestamp:<2024-06-15||\\/y", null, null, null, "2024-06-15||/y", "")]
    public async Task BuildQuery_WithDateMathRange_MatchesNativeRangeAndQueryString(string query, string? queryString, string? gte, string? gt, string? lte, string? lt, string expected)
    {
        var native = await GetIdsAsync(CreateRange(gte, gt, lte, lt));
        var passedThrough = await GetIdsAsync(CreateParser().BuildQuery(query));
        var evaluated = await GetIdsAsync(CreateParser(new DateMathEvaluatorVisitor(isDateField: f => f == "timestamp")).BuildQuery(query));
        var reference = await GetIdsAsync(new QueryStringQuery(queryString ?? query));

        Assert.Equal(expected, native);
        Assert.Equal(expected, passedThrough);
        Assert.Equal(expected, evaluated);
        Assert.Equal(expected, reference);
    }

    [Fact]
    public void BuildQuery_WithDateMath_PassesExpressionsThroughUnchanged()
    {
        var query = CreateParser().BuildQuery("timestamp:[now-1d/d TO now/d}");

        Utility.ElasticAssert.Json("{'bool':{'filter':{'range':{'timestamp':{'gte':'now-1d/d','lt':'now/d'}}}}}", query);
    }

    internal static DateRangeQuery CreateRange(string? gte, string? gt, string? lte, string? lt)
    {
        var range = new DateRangeQuery("timestamp");
        if (gte is not null)
            range.Gte = gte;
        if (gt is not null)
            range.Gt = gt;
        if (lte is not null)
            range.Lte = lte;
        if (lt is not null)
            range.Lt = lt;

        return range;
    }

    private static ElasticsearchQueryParser CreateParser(DateMathEvaluatorVisitor? evaluator = null) => new(c =>
    {
        c.UseMappings(DateMathData.Mapping);
        if (evaluator is not null)
            c.AddVisitor(evaluator);
    });

    private async Task<string> GetIdsAsync(Query query)
    {
        string? ids = await fixture.GetMatchingIdsAsync(DateMathData.Index, new BoolQuery { Filter = [query] }, TestContext.Current.CancellationToken);
        Assert.NotNull(ids);
        return ids;
    }
}
