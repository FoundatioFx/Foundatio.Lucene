using System.Collections.Concurrent;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class GeoQueryTests
{
    [Theory]
    [InlineData("geo:51.5,-0.12", "{'geo_distance':{'distance':'10mi','geo':'51.5,-0.12'}}")]
    [InlineData("geo:\"51.5,-0.12\"~5km", "{'geo_distance':{'distance':'5km','geo':'51.5,-0.12'}}")]
    [InlineData("geo:gcpvj0~25km", "{'geo_distance':{'distance':'25km','geo':'gcpvj0'}}")]
    [InlineData("geo:51.5,-0.12~1mi^2", "{'geo_distance':{'boost':2,'distance':'1mi','geo':'51.5,-0.12'}}")]
    [InlineData("-geo:\"51.5,-0.12\"~1mi", "{'bool':{'must_not':{'geo_distance':{'distance':'1mi','geo':'51.5,-0.12'}}}}")]
    [InlineData("geo:[51.5,-0.12 TO 50.0,1.0]", "{'geo_bounding_box':{'geo':{'bottom_right':'50.0,1.0','top_left':'51.5,-0.12'}}}")]
    [InlineData("geo:[40.92,-74.26 TO 40.49,-73.70]", "{'geo_bounding_box':{'geo':{'bottom_right':'40.49,-73.70','top_left':'40.92,-74.26'}}}")]
    public void BuildQuery_WithGeoField_EmitsGeoQueries(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("geo:[51.5,-0.12 TO *]")]
    [InlineData("geo:[* TO 51.5,-0.12]")]
    [InlineData("geo:>51.5,-0.12")]
    [InlineData("geo:\"\"~5km")]
    public void BuildQuery_WithIncompleteGeoQuery_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));
    }

    [Theory]
    [InlineData("geo:\"New York, NY\"~75mi", "New York, NY", "40.7128,-74.0060", "75mi")]
    [InlineData("geo:10001~10mi", "10001", "40.7506,-73.9972", "10mi")]
    [InlineData("geo:London", "London", "51.5,-0.12", "10mi")]
    public async Task BuildQueryAsync_WithGeoLocationResolver_ResolvesWholeValue(string query, string location, string coordinates, string distance)
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c => c.GeoLocationResolver = (text, _, _) =>
        {
            requested.Add(text);
            return ValueTask.FromResult<string?>(coordinates);
        });

        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(location, Assert.Single(requested));
        ElasticAssert.Json($"{{'geo_distance':{{'distance':'{distance}','geo':'{coordinates}'}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithGeoLocationResolverReturningNull_UsesValueAsWritten()
    {
        var parser = TestMapping.CreateScoringParser(c => c.GeoLocationResolver = (_, _, _) => ValueTask.FromResult<string?>(null));

        var result = await parser.BuildQueryAsync("geo:\"51.5,-0.12\"~5km", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'geo_distance':{'distance':'5km','geo':'51.5,-0.12'}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithGeoLocationResolver_OnlyResolvesGeoFields()
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c => c.GeoLocationResolver = (text, _, _) =>
        {
            requested.Add(text);
            return ValueTask.FromResult<string?>("1,2");
        });

        var result = await parser.BuildQueryAsync("keyword:London OR geo:Paris~1km OR geo:[3,4 TO 5,6] OR London", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Paris", Assert.Single(requested));
        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'London'}}},{'geo_distance':{'distance':'1km','geo':'1,2'}},{'geo_bounding_box':{'geo':{'bottom_right':'5,6','top_left':'3,4'}}},{'multi_match':{'query':'London'}}]}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithGeoLocationInInclude_ResolvesIncludedLocation()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.Includes = new Dictionary<string, string> { ["nearby"] = "geo:Dallas~75mi" };
            c.GeoLocationResolver = (text, _, _) => ValueTask.FromResult<string?>(text == "Dallas" ? "32.7767,-96.7970" : null);
        });

        var result = await parser.BuildQueryAsync("@include:nearby", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'geo_distance':{'distance':'75mi','geo':'32.7767,-96.7970'}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithFailingGeoLocationResolver_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.GeoLocationResolver = (_, _, _) => throw new InvalidOperationException("geocoder down"));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("geo:London", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("geocoder down", exception.Message);
    }

    [Fact]
    public void BuildQuery_WithGeoLocationResolver_ThrowsBecauseResolverIsAsynchronous()
    {
        var parser = TestMapping.CreateParser(c => c.GeoLocationResolver = (_, _, _) => ValueTask.FromResult<string?>("1,2"));

        var exception = Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("geo:London"));

        Assert.Contains("Async", exception.Message);
    }

    [Fact]
    public async Task BuildQueryAsync_WithGeoLocationResolverPerRequest_OverridesConfiguredResolver()
    {
        var parser = TestMapping.CreateScoringParser(c => c.GeoLocationResolver = (_, _, _) => ValueTask.FromResult<string?>("1,1"));
        var options = new ElasticsearchQueryOptions { GeoLocationResolver = (_, _, _) => ValueTask.FromResult<string?>("2,2") };

        var result = await parser.BuildQueryAsync("geo:home", options, TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'geo_distance':{'distance':'10mi','geo':'2,2'}}", result);
    }

    [Fact]
    public void BuildQuery_WithoutMapping_TreatsGeoTextAsTerm()
    {
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);

        var result = parser.BuildQuery("geo:[1,2 TO 3,4]");

        ElasticAssert.Json("{'range':{'geo':{'gte':'1,2','lte':'3,4'}}}", result);
    }
    [Theory]
    [InlineData("keyword:value", null)]
    [InlineData("@include:outer", "Dallas")]
    [InlineData("@include:OUTER", "Dallas")]
    [InlineData("geo:(@include:plain)", "Dallas")]
    public async Task BuildQueryAsync_WithUnusedGeoInclude_OnlyResolvesReachedLocations(string query, string? expectedLocation)
    {
        // Arrange
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.Includes = new Dictionary<string, string>
            {
                ["unused"] = "geo:unreachable",
                ["outer"] = "@include:nearby",
                ["nearby"] = "geo:Dallas~75mi",
                ["plain"] = "Dallas"
            };
            c.GeoLocationResolver = (text, _, _) =>
            {
                requested.Add(text);
                if (text == "unreachable")
                    throw new InvalidOperationException("unused geocoder");
                return ValueTask.FromResult<string?>("32.7767,-96.7970");
            };
        });

        // Act
        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (expectedLocation is null)
        {
            Assert.Empty(requested);
            ElasticAssert.Json("{'term':{'keyword':{'value':'value'}}}", result);
        }
        else
        {
            Assert.Equal(expectedLocation, Assert.Single(requested));
            string distance = query.Equals("@include:outer", StringComparison.OrdinalIgnoreCase) ? "75mi" : "10mi";
            ElasticAssert.Json($"{{'geo_distance':{{'distance':'{distance}','geo':'32.7767,-96.7970'}}}}", result);
        }
    }

}
