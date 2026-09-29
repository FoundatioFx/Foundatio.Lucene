using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Runs a corpus of queries through Foundatio.Lucene and, for reference, through Elasticsearch's own
/// <c>query_string</c> parser, and checks both against independently written document expectations.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class SyntaxCompatibilityIntegrationTests(ElasticsearchFixture fixture)
{
    public static IEnumerable<TheoryDataRow<string, string, BooleanOperator?, string?, string?, bool>> MatchingCases()
    {
        // Null means the query is rejected; an empty string means a successful search with no matches. The first
        // column is Foundatio.Lucene, the second is query_string over the same index (default field text).
        (string Id, string Query, BooleanOperator? Operator, string? Native, string? QueryString)[] cases =
        [
            ("text-term", "text:alpha", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("text-case", "text:ALPHA", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("keyword-term", "keyword:john", BooleanOperator.Or, "a", "a"),
            ("keyword-case", "keyword:JOHN", BooleanOperator.Or, "", ""),
            ("keyword-uppercase", "keyword:ALPHA", BooleanOperator.Or, "l", "l"),
            ("source-component", "source:Services", BooleanOperator.Or, "a,b", "a,b"),
            ("source-qualified", "source:App.Services.Checkout", BooleanOperator.Or, "a", "a"),
            ("source-prefix", "source:App.Services.Check*", BooleanOperator.Or, "a", "a"),
            ("tag-analysis", "tag:VIP", BooleanOperator.Or, "a,b", "a,b"),
            ("tag-phrase", "tag:\"VIP Member\"", BooleanOperator.Or, "b", "b"),
            ("tag-prefix", "tag:VI*", BooleanOperator.Or, "a,b", "a,b"),
            ("tag-regex", "tag:/vip/", BooleanOperator.Or, "a,b", "a,b"),
            ("tag-regex-case", "tag:/VIP/", BooleanOperator.Or, "", ""),
            ("keyword-normalizer", "folded:CAFÉ", BooleanOperator.Or, "a,b", "a,b"),
            ("indexed-not-source", "_exists_:ignored", BooleanOperator.Or, "a,d,f", "a,d,f"),
            ("null-sentinel", "nullable:MISSING", BooleanOperator.Or, "a,f", "a,f"),
            ("null-existence", "_exists_:nullable", BooleanOperator.Or, "a,c,d,f", "a,c,d,f"),
            ("phrase", "text:\"alpha beta\"", BooleanOperator.Or, "a", "a"),
            ("and", "text:alpha AND text:beta", BooleanOperator.Or, "a,b", "a,b"),
            ("or", "text:alpha OR text:beta", BooleanOperator.Or, "a,b,c,l", "a,b,c,l"),
            ("implicit-default", "alpha beta", null, "a,b", "a,b,c,l"),
            ("implicit-and", "alpha beta", BooleanOperator.And, "a,b", "a,b"),
            ("implicit-or", "alpha beta", BooleanOperator.Or, "a,b,c,l", "a,b,c,l"),
            ("required-term", "+text:alpha text:gamma", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("negative-only", "NOT text:alpha", BooleanOperator.Or, "c,d,e,f,g,h,i,j,k", "c,d,e,f,g,h,i,j,k"),
            ("negative-minus", "-text:alpha", BooleanOperator.Or, "c,d,e,f,g,h,i,j,k", "c,d,e,f,g,h,i,j,k"),
            ("negative-bang", "!text:alpha", BooleanOperator.Or, "c,d,e,f,g,h,i,j,k", "c,d,e,f,g,h,i,j,k"),
            ("spaced-bang", "! text:alpha", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("and-not", "text:beta AND NOT text:alpha", BooleanOperator.Or, "c", "c"),
            // An explicit OR NOT is a boolean alternative in Foundatio.Lucene; query_string prohibits the clause.
            ("or-not", "text:alpha OR NOT text:beta", BooleanOperator.Or, "a,b,d,e,f,g,h,i,j,k,l", "l"),
            // A '-' clause prohibits even inside an explicit OR, as in Lucene.
            ("or-minus", "text:alpha OR -text:beta", BooleanOperator.Or, "l", "l"),
            // AND binds tighter than OR; query_string makes both AND operands required.
            ("mixed-operators", "text:alpha OR text:beta AND text:gamma", BooleanOperator.Or, "a,b,c,l", "b,c"),
            ("mixed-operators-and", "text:beta AND text:gamma OR text:alpha", BooleanOperator.Or, "a,b,c,l", "b,c"),
            ("parenthesized-and", "(text:alpha OR text:beta) AND text:gamma", BooleanOperator.Or, "b,c", "b,c"),
            ("parenthesized-or", "text:alpha OR (text:beta AND text:gamma)", BooleanOperator.Or, "a,b,c,l", "a,b,c,l"),
            ("field-group", "text:(alpha OR beta)", BooleanOperator.Or, "a,b,c,l", "a,b,c,l"),
            ("negative-group", "-text:(alpha OR beta)", BooleanOperator.Or, "d,e,f,g,h,i,j,k", "d,e,f,g,h,i,j,k"),
            ("post-colon-minus", "text:-alpha", BooleanOperator.Or, null, null),
            ("post-colon-not", "text:NOT alpha", BooleanOperator.Or, null, null),
            ("post-colon-range", "number:-[1 TO 5]", BooleanOperator.Or, null, null),
            ("bare-dots", "keyword:1..5", BooleanOperator.Or, "e", "e"),
            ("dot-range", "number:[1 .. 5]", BooleanOperator.Or, "a,b,c,f,g", null),
            ("dot-range-compact", "number:[1..5]", BooleanOperator.Or, "a,b,c,f,g", null),
            // Foundatio.Lucene reads an escaped dot as a literal dot in the field name (Foundatio.Parsers rejected it).
            ("escaped-dot", "field\\.with\\.dots:value", BooleanOperator.Or, "", ""),
            ("wildcard-question", "keyword:jo?n", BooleanOperator.Or, "a,b,c", "a,b,c"),
            ("wildcard-middle", "keyword:jo*n", BooleanOperator.Or, "a,b,c", "a,b,c"),
            ("wildcard-leading", "keyword:*john", BooleanOperator.Or, "a", "a"),
            ("wildcard-prefix", "keyword:john*", BooleanOperator.Or, "a,d,j", "a,d,j"),
            ("wildcard-mixed", "keyword:jo?n*", BooleanOperator.Or, "a,b,c,d,j,k", "a,b,c,d,j,k"),
            ("wildcard-escaped", "keyword:john\\*", BooleanOperator.Or, "j", "j"),
            ("wildcard-escaped-question", "keyword:jo\\?n", BooleanOperator.Or, "c", "c"),
            ("wildcard-text", "text:alp*", BooleanOperator.Or, "a,b,d,l", "a,b,d,l"),
            ("regex-prefix", "keyword:/val.*/", BooleanOperator.Or, "f,g", "f,g"),
            ("regex-nonprefix", "keyword:/[0-9]+/", BooleanOperator.Or, "", ""),
            ("fuzzy", "text:alphx~1", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("phrase-slop", "text:\"alpha beta\"~1", BooleanOperator.Or, "a,b", "a,b"),
            ("boost-membership", "text:alpha^8", BooleanOperator.Or, "a,b,l", "a,b,l"),
            ("exists", "_exists_:keyword", BooleanOperator.Or, "a,b,c,d,e,f,g,h,j,k,l", "a,b,c,d,e,f,g,h,j,k,l"),
            ("exists-star", "keyword:*", BooleanOperator.Or, "a,b,c,d,e,f,g,h,j,k,l", "a,b,c,d,e,f,g,h,j,k,l"),
            ("missing", "_missing_:keyword", BooleanOperator.Or, "i", ""),
            ("missing-migration", "NOT _exists_:keyword", BooleanOperator.Or, "i", "i"),
            ("match-all", "*:*", BooleanOperator.Or, "a,b,c,d,e,f,g,h,i,j,k,l", "a,b,c,d,e,f,g,h,i,j,k,l"),
            ("include", "@include:active", BooleanOperator.Or, "a", ""),
            ("range-inclusive", "number:[1 TO 5]", BooleanOperator.Or, "a,b,c,f,g", "a,b,c,f,g"),
            ("range-exclusive", "number:{1 TO 5}", BooleanOperator.Or, "b,f,g", "b,f,g"),
            ("range-open-upper", "number:[1 TO 5}", BooleanOperator.Or, "a,b,f,g", "a,b,f,g"),
            ("range-open-lower", "number:{1 TO 5]", BooleanOperator.Or, "b,c,f,g", "b,c,f,g"),
            ("range-unbounded", "number:[1 TO *]", BooleanOperator.Or, "a,b,c,d,e,f,g,h,i,j", "a,b,c,d,e,f,g,h,i,j"),
            ("range-negative", "number:[-1 TO 0]", BooleanOperator.Or, "k,l", "k,l"),
            ("comparison", "number:>5", BooleanOperator.Or, "d,e,h,i,j", "d,e,h,i,j"),
            ("comparison-inclusive", "number:>=5", BooleanOperator.Or, "c,d,e,h,i,j", "c,d,e,h,i,j"),
            ("comparison-less", "number:<1", BooleanOperator.Or, "k,l", "k,l"),
            ("comparison-less-inclusive", "number:<=1", BooleanOperator.Or, "a,k,l", "a,k,l"),
            ("field-group-range", "number:(>=1 AND <5)", BooleanOperator.Or, "a,b,f,g", "a,b,f,g"),
            ("typed-number", "number:5", BooleanOperator.Or, "c", "c"),
            ("typed-number-invalid", "number:abc", BooleanOperator.Or, null, null),
            ("date-utc", "date:[2024-01-01 TO 2024-01-01]", BooleanOperator.Or, "a,b,c", "a,b,c"),
            ("date-timezone", "date:[2024-01-01 TO *]^\"America/Chicago\"", BooleanOperator.Or, "c,d,g,h,j,k,l", null),
            ("date-nanos-timezone", "dateNanos:[2024-01-01 TO *]^\"America/Chicago\"", BooleanOperator.Or, "c,d,g,h,j,k,l", null),
            ("date-offset-timezone", "date:[2024-01-01 TO *]^-6h", BooleanOperator.Or, "c,d,g,h,j,k,l", null),
            ("date-numeric-caret", "date:[2024-01-01 TO *]^2", BooleanOperator.Or, null, "a,b,c,d,g,h,j,k,l"),
            ("date-math", "date:[2024-01-01||+1d/d TO *]", BooleanOperator.Or, "d,g,h,j,k,l", "d,g,h,j,k,l")
        ];

        foreach (var testCase in cases)
        {
            foreach (bool scoring in new[] { false, true })
                yield return new(testCase.Id, testCase.Query, testCase.Operator, testCase.Native, testCase.QueryString, scoring);
        }
    }

    [Theory]
    [MemberData(nameof(MatchingCases))]
    public async Task BuildQuery_WithCorpusCase_MatchesIndependentDocumentExpectations(string id, string text, BooleanOperator? defaultOperator, string? expectedNative, string? expectedQueryString, bool scoring)
    {
        var parser = CreateParser();
        var reference = new QueryStringQuery(text) { DefaultField = "text", AnalyzeWildcard = true, AllowLeadingWildcard = true };
        if (defaultOperator.HasValue)
            reference.DefaultOperator = defaultOperator == BooleanOperator.And ? Operator.And : Operator.Or;
        Query referenceQuery = scoring ? reference : new BoolQuery { Filter = [reference] };

        Query? query = null;
        try
        {
            query = parser.BuildQuery(text, new ElasticsearchQueryOptions { DefaultOperator = defaultOperator, UseScoring = scoring });
        }
        catch (QueryException) when (expectedNative is null)
        {
        }

        string? native = query is null ? null : await fixture.GetMatchingIdsAsync(CompatibilityData.Index, query, TestContext.Current.CancellationToken);
        string? queryString = await fixture.GetMatchingIdsAsync(CompatibilityData.Index, referenceQuery, TestContext.Current.CancellationToken);

        Assert.True(expectedNative == native, $"{id}: expected Foundatio.Lucene to match '{expectedNative}' but it matched '{native}'.");
        Assert.True(expectedQueryString == queryString, $"{id}: expected query_string to match '{expectedQueryString}' but it matched '{queryString}'.");
    }

    [Theory]
    [InlineData("text:alpha")]
    [InlineData("text:\"alpha beta\"")]
    [InlineData("text:alpha AND text:beta")]
    [InlineData("text:alpha OR text:gamma")]
    [InlineData("+text:alpha text:gamma")]
    public async Task BuildQuery_WithEquivalentScoringQuery_MatchesQueryStringScores(string text)
    {
        var parser = CreateParser();

        var native = await GetScoresAsync(parser.BuildQuery(text, ScoringOptions));
        var reference = await GetScoresAsync(ReferenceQuery(text));

        Assert.NotEmpty(native);
        Assert.Equal(reference.Keys.Order(StringComparer.Ordinal), native.Keys.Order(StringComparer.Ordinal));
        foreach (string id in native.Keys)
            AssertClose(reference[id], native[id]);
    }

    [Theory]
    [InlineData("text:alpha", "text:alpha^8")]
    [InlineData("text:\"alpha beta\"", "text:\"alpha beta\"^8")]
    [InlineData("(text:alpha OR text:gamma)", "(text:alpha OR text:gamma)^8")]
    [InlineData("text:alp*", "text:alp*^8")]
    public async Task BuildQuery_WithBoost_MultipliesScoresLikeQueryString(string baseline, string boosted)
    {
        var parser = CreateParser();

        var native = await GetScoresAsync(parser.BuildQuery(baseline, ScoringOptions));
        var nativeBoosted = await GetScoresAsync(parser.BuildQuery(boosted, ScoringOptions));
        var reference = await GetScoresAsync(ReferenceQuery(baseline));
        var referenceBoosted = await GetScoresAsync(ReferenceQuery(boosted));

        Assert.NotEmpty(native);
        Assert.Equal(native.Keys.Order(StringComparer.Ordinal), nativeBoosted.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(native.Keys.Order(StringComparer.Ordinal), reference.Keys.Order(StringComparer.Ordinal));
        foreach (string id in native.Keys)
        {
            AssertClose(native[id] * 8, nativeBoosted[id]);
            AssertClose(reference[id] * 8, referenceBoosted[id]);
            AssertClose(referenceBoosted[id], nativeBoosted[id]);
        }
    }

    [Fact]
    public async Task BuildQuery_WithBoostedDisjunction_ReordersResultsLikeQueryString()
    {
        var parser = CreateParser();
        const string text = "text:alpha^8 OR text:gamma";

        var native = await GetScoresAsync(parser.BuildQuery(text, ScoringOptions));
        var reference = await GetScoresAsync(ReferenceQuery(text));

        Assert.True(native["a"] > native["c"]);
        Assert.True(reference["a"] > reference["c"]);
        foreach (string id in native.Keys)
            AssertClose(reference[id], native[id]);
    }

    [Fact]
    public async Task BuildQuery_InFilterContext_MatchesWithZeroScores()
    {
        var parser = CreateParser();

        var native = await GetScoresAsync(parser.BuildQuery("text:alpha OR text:gamma"));

        Assert.Equal(["a", "b", "c", "l"], native.Keys.Order(StringComparer.Ordinal));
        Assert.All(native.Values, score => Assert.Equal(0, score));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("dateNanos")]
    public async Task BuildQuery_WithTimeZoneCaret_MatchesQueryStringTimeZone(string field)
    {
        var parser = CreateParser();
        string range = $"{field}:[2024-01-01 TO *]";

        var native = await fixture.GetMatchingIdsAsync(CompatibilityData.Index, parser.BuildQuery(range + "^\"America/Chicago\"", ScoringOptions), TestContext.Current.CancellationToken);
        var defaultTimeZone = await fixture.GetMatchingIdsAsync(CompatibilityData.Index, parser.BuildQuery(range, ScoringOptions with { DefaultTimeZone = "America/Chicago" }), TestContext.Current.CancellationToken);
        var reference = await fixture.GetMatchingIdsAsync(CompatibilityData.Index, new QueryStringQuery(range) { TimeZone = "America/Chicago" }, TestContext.Current.CancellationToken);

        Assert.Equal("c,d,g,h,j,k,l", native);
        Assert.Equal(native, defaultTimeZone);
        Assert.Equal(native, reference);
    }

    public static IEnumerable<TheoryDataRow<string, string, string, string, string, bool>> DateBoundaryCases()
    {
        (string Field, string Range, string Expected, string From, string To)[] cases =
        [
            ("date", "[\"2024-03-10T00:00:00\" TO \"2024-03-11T00:00:00\"}^\"America/Chicago\"", "s1,s2,s3,s4", "2024-03-10T06:00:00Z", "2024-03-11T05:00:00Z"),
            ("date", "[\"2024-11-03T00:00:00\" TO \"2024-11-04T00:00:00\"}^\"America/Chicago\"", "f1,f2,f3,f4", "2024-11-03T05:00:00Z", "2024-11-04T06:00:00Z"),
            ("date", "[2024-03-10 TO 2024-03-10]^\"America/Chicago\"", "s1,s2,s3,s4", "2024-03-10T06:00:00Z", "2024-03-11T05:00:00Z"),
            ("date", "[2024-11-03 TO 2024-11-03]^\"America/Chicago\"", "f1,f2,f3,f4", "2024-11-03T05:00:00Z", "2024-11-04T06:00:00Z"),
            ("dateNanos", "[\"2024-03-10T00:00:00\" TO \"2024-03-11T00:00:00\"}^\"America/Chicago\"", "s1,s2,s3,s4", "2024-03-10T06:00:00Z", "2024-03-11T05:00:00Z"),
            ("dateNanos", "[\"2024-11-03T00:00:00\" TO \"2024-11-04T00:00:00\"}^\"America/Chicago\"", "f1,f2,f3,f4", "2024-11-03T05:00:00Z", "2024-11-04T06:00:00Z"),
            ("date", "[\"2024-02-29T00:00:00Z\" TO \"2024-03-01T00:00:00Z\"}", "l1", "2024-02-29T00:00:00Z", "2024-03-01T00:00:00Z"),
            ("date", "[2024-02-28||+1d/d TO 2024-03-01||/d}", "l1", "2024-02-29T00:00:00Z", "2024-03-01T00:00:00Z"),
            ("date", "[\"2024-12-31T00:00:00Z\" TO \"2025-01-01T00:00:00Z\"}", "y0", "2024-12-31T00:00:00Z", "2025-01-01T00:00:00Z"),
            ("dateNanos", "[\"2024-01-01T00:00:00.000000001Z\" TO \"2024-01-01T00:00:00.000000002Z\"}", "n1", "2024-01-01T00:00:00.000000001Z", "2024-01-01T00:00:00.000000002Z"),
            ("dateNanos", "{\"2024-01-01T00:00:00.000000000Z\" TO \"2024-01-01T00:00:00.000000001Z\"]", "n1", "2024-01-01T00:00:00.000000001Z", "2024-01-01T00:00:00.000000002Z")
        ];

        foreach (var testCase in cases)
        {
            foreach (bool scoring in new[] { false, true })
                yield return new(testCase.Field, testCase.Range, testCase.Expected, testCase.From, testCase.To, scoring);
        }
    }

    [Theory]
    [MemberData(nameof(DateBoundaryCases))]
    public async Task BuildQueryAsync_WithServerMappingAndDateBoundary_MatchesIndependentUtcBounds(string field, string range, string expected, string from, string to, bool scoring)
    {
        var parser = new ElasticsearchQueryParser(c => c.UseMappings(ElasticMappingResolver.Create(fixture.Client, CompatibilityData.DateIndex)));
        Query reference = new DateRangeQuery(field) { Gte = from, Lt = to };
        if (!scoring)
            reference = new BoolQuery { Filter = [reference] };

        var native = await parser.BuildQueryAsync($"{field}:{range}", new ElasticsearchQueryOptions { UseScoring = scoring }, TestContext.Current.CancellationToken);

        foreach (var query in new[] { native, reference })
        {
            var response = await fixture.SearchAsync<CompatibilityData.DateBoundaryDocument>(CompatibilityData.DateIndex, query, cancellationToken: TestContext.Current.CancellationToken);
            ElasticsearchFixture.AssertComplete(response);
            Assert.Equal(expected, string.Join(',', response.Hits.Select(hit => hit.Id).Order(StringComparer.Ordinal)));
            if (!scoring)
                Assert.All(response.Hits, hit => Assert.Equal(0, hit.Score));
        }
    }

    [Theory]
    [InlineData("text:alpha OR keyword:johnny", "a")]
    [InlineData("NOT keyword:john", "")]
    [InlineData("keyword:johnny OR text:gamma", "")]
    [InlineData("+text:alpha text:gamma", "a")]
    [InlineData("text:alpha OR NOT text:beta", "a")]
    public async Task BuildQuery_WithCallerOwnedFilter_CannotBroadenResults(string text, string expected)
    {
        var parser = CreateParser();

        foreach (bool scoring in new[] { false, true })
        {
            var userQuery = parser.BuildQuery(text, new ElasticsearchQueryOptions { DefaultOperator = BooleanOperator.Or, UseScoring = scoring });
            var query = new BoolQuery { Must = [userQuery], Filter = [new TermQuery("keyword", "john")] };

            Assert.Equal(expected, await fixture.GetMatchingIdsAsync(CompatibilityData.Index, query, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("lowerkeyword", "VIP Member", "vip member")]
    [InlineData("whitespace_lower", "App.Services.Checkout, SECOND", "app.services.checkout,second")]
    [InlineData("components", "App.Services.Checkout", "app,app.services.checkout,checkout,services")]
    public async Task Analyze_WithCustomAnalyzer_ProducesExpectedTokens(string analyzer, string text, string expected)
    {
        var response = await fixture.Client.Indices.AnalyzeAsync(d => d.Index(CompatibilityData.Index).Analyzer(analyzer).Text(text), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(expected, string.Join(',', response.Tokens!.Select(token => token.Token).Order(StringComparer.Ordinal)));
    }

    private static readonly ElasticsearchQueryOptions ScoringOptions = new() { UseScoring = true, DefaultOperator = BooleanOperator.Or };

    private static ElasticsearchQueryParser CreateParser() => new(c =>
    {
        c.UseMappings(CompatibilityData.Mapping);
        c.DefaultFields = ["text"];
        c.Includes = new Dictionary<string, string> { ["active"] = "keyword:john" };
    });

    private static Query ReferenceQuery(string text) => new QueryStringQuery(text)
    {
        DefaultField = "text",
        DefaultOperator = Operator.Or,
        AnalyzeWildcard = true,
        AllowLeadingWildcard = true
    };

    private async Task<Dictionary<string, double>> GetScoresAsync(Query query)
    {
        var response = await fixture.SearchAsync<CompatibilityData.Document>(CompatibilityData.Index, query, cancellationToken: TestContext.Current.CancellationToken);
        ElasticsearchFixture.AssertComplete(response);

        var scores = response.Hits.ToDictionary(hit => hit.Id!, hit => hit.Score ?? double.NaN, StringComparer.Ordinal);
        Assert.All(scores.Values, score => Assert.True(double.IsFinite(score) && score >= 0));
        return scores;
    }

    private static void AssertClose(double expected, double actual)
    {
        double tolerance = 0.00001 * Math.Max(1, Math.Abs(expected));
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
