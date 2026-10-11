using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Executes generated aggregations, sorts, and complete search requests to prove Elasticsearch accepts them and
/// returns the expected results.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class SearchIntegrationTests(ElasticsearchFixture fixture)
{
    [Fact]
    public async Task BuildAggregations_WithEveryAggregationType_IsAcceptedAndComputesValues()
    {
        var parser = CreateParser();

        var aggregations = parser.BuildAggregations(
            "min:price max:price avg:price sum:price stats:price exstats:price cardinality:category^100 percentiles:price~50,95 "
            + "missing:tags date:created~1d histogram:price~10 geogrid:location~3 terms:title terms:(tags @include:/data.*/ @exclude:ml) "
            + "terms:(category~5 -max:price +min:price tophits:(_~1 @include:title))");
        var response = await fixture.SearchAsync<object>(SearchData.Index, null, s => s.Size(0).Aggregations(aggregations), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        var results = response.Aggregations!;
        Assert.Equal(0, results.GetMin("min_price")!.Value);
        Assert.Equal(59.99, results.GetMax("max_price")!.Value!.Value, 0.001);
        Assert.Equal(179.96, results.GetSum("sum_price")!.Value!.Value, 0.001);
        Assert.Equal(5, results.GetStats("stats_price")!.Count);
        Assert.Equal(3, results.GetCardinality("cardinality_category")!.Value);
        Assert.Equal(1, results.GetMissing("missing_tags")!.DocCount);
        Assert.Equal(4, results.GetDateHistogram("date_created")!.Buckets.Sum(b => b.DocCount));
        Assert.NotEmpty(results.GetGeohashGrid("geogrid_location")!.Buckets);
        Assert.Equal(5, results.GetStringTerms("terms_title")!.Buckets.Count);
        Assert.Equal(["database"], results.GetStringTerms("terms_tags")!.Buckets.Select(b => b.Key.ToString()));

        var categories = results.GetStringTerms("terms_category")!.Buckets.ToList();
        Assert.Equal(["technology", "database", "draft"], categories.Select(b => b.Key.ToString()));
        Assert.All(categories, bucket => Assert.Single(bucket.Aggregations!.GetTopHits("tophits")!.Hits.Hits));
    }

    [Fact]
    public async Task BuildAggregations_WithDateHistogramTimeZoneOffsetAndBounds_IsAccepted()
    {
        var parser = CreateParser();
        var options = new ElasticsearchQueryOptions
        {
            StartDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var automatic = parser.BuildAggregations("date:created", options);
        var monthly = parser.BuildAggregations("date:(created~month^\"America/Chicago\" @offset:\"-6h\" @missing:\"2024-01-01\" max:price)", options);

        var automaticResponse = await fixture.SearchAsync<object>(SearchData.Index, null, s => s.Size(0).Aggregations(automatic), TestContext.Current.CancellationToken);
        var monthlyResponse = await fixture.SearchAsync<object>(SearchData.Index, null, s => s.Size(0).Aggregations(monthly), TestContext.Current.CancellationToken);

        Assert.True(automaticResponse.IsValidResponse, automaticResponse.DebugInformation);
        Assert.True(monthlyResponse.IsValidResponse, monthlyResponse.DebugInformation);
        Assert.InRange(automaticResponse.Aggregations!.GetDateHistogram("date_created")!.Buckets.Count, 80, 120);
        Assert.Equal(5, monthlyResponse.Aggregations!.GetDateHistogram("date_created")!.Buckets.Sum(b => b.DocCount));
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithNestedAndFilteredAggregations_IsAcceptedAndFiltersNestedDocuments()
    {
        var parser = CreateParser(c => c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(new TermQuery("resellers.name", "Official")));

        var aggregations = await parser.BuildAggregationsAsync("terms:resellers.name max:resellers.price terms:(category -max:resellers.price)", cancellationToken: TestContext.Current.CancellationToken);
        var response = await fixture.SearchAsync<object>(SearchData.Index, null, s => s.Size(0).Aggregations(aggregations), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        var nested = response.Aggregations!.GetNested("nested_resellers")!;
        var names = nested.Aggregations!.GetFilter("filtered_terms_resellers.name")!.Aggregations!.GetStringTerms("terms_resellers.name")!;
        Assert.Equal(["Official"], names.Buckets.Select(b => b.Key.ToString()));
        Assert.Equal(55, nested.Aggregations.GetFilter("filtered_max_resellers.price")!.Aggregations!.GetMax("max_resellers.price")!.Value);
        Assert.Equal("technology", response.Aggregations.GetStringTerms("terms_category")!.Buckets.First().Key.ToString());
    }

    [Theory]
    [InlineData("terms:(category -cardinality:title.keyword)", "technology,database,draft")]
    [InlineData("terms:(category +sum:price)", "draft,database,technology")]
    [InlineData("terms:(category -max:resellers.price)", "technology,database,draft")]
    [InlineData("terms:(tags -max:resellers.price +min:price)", "ml,database,search,lucene")]
    public async Task BuildAggregations_WithOrderedSubAggregation_OrdersBucketsInElasticsearch(string expression, string expected)
    {
        var parser = CreateParser();

        var aggregations = parser.BuildAggregations(expression);
        var response = await fixture.SearchAsync<object>(SearchData.Index, null, s => s.Size(0).Aggregations(aggregations), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        string name = aggregations.Keys.Single();
        Assert.Equal(expected, string.Join(',', response.Aggregations!.GetStringTerms(name)!.Buckets.Select(b => b.Key.ToString())));
    }

    [Theory]
    [InlineData("-price", "5,3,2,1,4")]
    [InlineData("price", "4,1,2,3,5")]
    [InlineData("category -price", "3,4,5,2,1")]
    [InlineData("-cat title", "2,1,5,4,3")]
    [InlineData("-created", "5,3,2,1,4")]
    [InlineData("unknownfield -price", "5,3,2,1,4")]
    [InlineData("-_score -price", "5,3,2,1,4")]
    [InlineData("-resellers.price", "5,3,2,1,4")]
    [InlineData("resellers.price", "1,2,3,5,4")]
    [InlineData("TITLE", "2,3,1,5,4")]
    [InlineData("location:\"51.5,-0.12\"", "3,1,5,2,4")]
    [InlineData("location:gcpvj0", "3,1,5,2,4")]
    public async Task BuildSort_WithSortExpression_IsAcceptedAndOrdersDocuments(string sort, string expected)
    {
        var parser = CreateParser();

        var sorts = parser.BuildSort(sort);
        var response = await fixture.SearchAsync<SearchData.Document>(SearchData.Index, null, s => s.Sort(sorts), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(expected, string.Join(',', response.Hits.Select(h => h.Id)));
    }

    [Fact]
    public async Task BuildSortAsync_WithNestedFilter_SortsByFilteredNestedValues()
    {
        var parser = CreateParser(c => c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(new TermQuery("resellers.name", "Other")));

        var sorts = await parser.BuildSortAsync("resellers.price", cancellationToken: TestContext.Current.CancellationToken);
        var response = await fixture.SearchAsync<SearchData.Document>(SearchData.Index, null, s => s.Sort(sorts), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal("1,3,5,2,4", string.Join(',', response.Hits.Select(h => h.Id)));
    }

    [Fact]
    public async Task BuildSearchAsync_WithRuntimeFields_ExecutesQueryAggregationsAndSortTogether()
    {
        var parser = CreateParser(c => c.RuntimeFieldResolver = (field, _, _) => ValueTask.FromResult(field == "doubled"
            ? new ElasticRuntimeField("doubled", RuntimeFieldType.Double, "emit(doc['price'].value * 2)")
            : null));

        var search = await parser.BuildSearchAsync("doubled:>70 category:technology", "max:doubled terms:category", "-doubled", cancellationToken: TestContext.Current.CancellationToken);
        var request = search.ApplyTo(new SearchRequest(SearchData.Index));
        var response = await fixture.Client.SearchAsync<SearchData.Document>(request, TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal("doubled", Assert.Single(search.RuntimeFields).Name);
        Assert.Equal("5,2", string.Join(',', response.Hits.Select(h => h.Id)));
        Assert.Equal(119.98, response.Aggregations!.GetMax("max_doubled")!.Value!.Value, 0.001);
    }

    [Fact]
    public async Task BuildSearch_WithDescriptor_ExecutesSearch()
    {
        var parser = CreateParser();
        var search = parser.BuildSearch("title:database OR tags:database", "terms:category", "-price");

        var response = await fixture.Client.SearchAsync<SearchData.Document>(s => s.Indices(SearchData.Index).Apply(search), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal("3,1", string.Join(',', response.Hits.Select(h => h.Id)));
        Assert.Equal(2, response.Aggregations!.GetStringTerms("terms_category")!.Buckets.Count);
    }

    [Theory]
    [InlineData("location:[45,-125 TO 30,-70]", "1,2,5")]
    [InlineData("location:\"40.7128,-74.0060\"~100km", "1")]
    [InlineData("location:London~50km", "3")]
    [InlineData("-location:London~50km", "1,2,4,5")]
    public async Task BuildQueryAsync_WithGeoQueries_MatchesDocumentsByLocation(string query, string expected)
    {
        var parser = CreateParser(c => c.GeoLocationResolver = (text, _, _) => ValueTask.FromResult<string?>(text == "London" ? "51.5,-0.12" : null));

        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, await fixture.GetMatchingIdsAsync(SearchData.Index, result, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("cat:technology", "1,2,5")]
    [InlineData("title:database", "3")]
    [InlineData("title:databa*", "3")]
    [InlineData("title:\"design patterns\"", "3")]
    [InlineData("title.keyword:\"Unpublished Draft\"", "4")]
    [InlineData("price:[30 TO 50]", "2,3")]
    [InlineData("created:[2024-01-01 TO 2024-01-31]", "1,2")]
    [InlineData("_missing_:created", "4")]
    [InlineData("database", "1,3")]
    public async Task BuildQuery_WithCatalogQueries_MatchesExpectedDocuments(string query, string expected)
    {
        var parser = CreateParser(c => c.DefaultFields = ["title", "tags"]);

        var result = parser.BuildQuery(query);

        Assert.Equal(expected, await fixture.GetMatchingIdsAsync(SearchData.Index, result, TestContext.Current.CancellationToken));
    }

    private static ElasticsearchQueryParser CreateParser(Action<ElasticsearchQueryParserConfiguration>? configure = null) => new(c =>
    {
        c.UseMappings(SearchData.Mapping);
        configure?.Invoke(c);
    });
}
