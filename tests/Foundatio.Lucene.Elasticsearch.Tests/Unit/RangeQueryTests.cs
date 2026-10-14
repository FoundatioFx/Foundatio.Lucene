using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class RangeQueryTests
{
    [Theory]
    [InlineData("number:[1 TO 5]", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("number:{1 TO 5}", "{'range':{'number':{'gt':'1','lt':'5'}}}")]
    [InlineData("number:[1 TO 5}", "{'range':{'number':{'gte':'1','lt':'5'}}}")]
    [InlineData("number:{1 TO 5]", "{'range':{'number':{'gt':'1','lte':'5'}}}")]
    [InlineData("number:[1 .. 5]", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("number:[1..5]", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("number:{1 .. 5}", "{'range':{'number':{'gt':'1','lt':'5'}}}")]
    [InlineData("number:[1 TO *]", "{'range':{'number':{'gte':'1'}}}")]
    [InlineData("number:{* TO 5]", "{'range':{'number':{'lte':'5'}}}")]
    [InlineData("number:[* TO *]", "{'range':{'number':{}}}")]
    [InlineData("number:>5", "{'range':{'number':{'gt':'5'}}}")]
    [InlineData("number:>=5", "{'range':{'number':{'gte':'5'}}}")]
    [InlineData("number:<5", "{'range':{'number':{'lt':'5'}}}")]
    [InlineData("number:<=5", "{'range':{'number':{'lte':'5'}}}")]
    [InlineData("number:[-5 TO -1]", "{'range':{'number':{'gte':'-5','lte':'-1'}}}")]
    [InlineData("double:[1.5 TO 2.5]", "{'range':{'double':{'gte':'1.5','lte':'2.5'}}}")]
    [InlineData("keyword:[a TO c]", "{'range':{'keyword':{'gte':'a','lte':'c'}}}")]
    [InlineData("unmapped:[a TO c]", "{'range':{'unmapped':{'gte':'a','lte':'c'}}}")]
    [InlineData("number:[1 TO 5]^2", "{'range':{'number':{'boost':2,'gte':'1','lte':'5'}}}")]
    public void BuildQuery_WithRange_EmitsRangeQueryWithStringBounds(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithRangeOrTerm_CombinesWithShould()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("number:[1 TO 2} OR keyword:value1");

        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'range':{'number':{'gte':'1','lt':'2'}}},{'term':{'keyword':{'value':'value1'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithFieldlessRangeAndNoDefaultFields_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("[1 TO 5]"));

        Assert.Contains("Range queries require a field", exception.Message);
    }

    [Theory]
    [InlineData("number", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("number;long", "{'bool':{'minimum_should_match':1,'should':[{'range':{'number':{'gte':'1','lte':'5'}}},{'range':{'long':{'gte':'1','lte':'5'}}}]}}")]
    public void BuildQuery_WithFieldlessRange_UsesDefaultFields(string fields, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = fields.Split(';'));

        var result = parser.BuildQuery("[1 TO 5]");

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("date:[2024-01-01 TO 2024-02-01]", "{'range':{'date':{'gte':'2024-01-01||/d','lte':'2024-02-01||/d'}}}")]
    [InlineData("date:{2024-01-01 TO 2024-02-01}", "{'range':{'date':{'gt':'2024-01-01||/d','lt':'2024-02-01||/d'}}}")]
    [InlineData("date:[* TO 2017-01-31}", "{'range':{'date':{'lt':'2017-01-31||/d'}}}")]
    [InlineData("date:[2017-01-31 TO   *  }", "{'range':{'date':{'gte':'2017-01-31||/d'}}}")]
    [InlineData("date:[now-1d/d TO now/d]", "{'range':{'date':{'gte':'now-1d/d','lte':'now/d'}}}")]
    [InlineData("date:>=now-1d", "{'range':{'date':{'gte':'now-1d'}}}")]
    [InlineData("date:<now/d", "{'range':{'date':{'lt':'now/d'}}}")]
    [InlineData("date:[2024-02-28||+1d/d TO 2024-03-01||/d}", "{'range':{'date':{'gte':'2024-02-28||+1d/d','lt':'2024-03-01||/d'}}}")]
    [InlineData("date:[2017-01-01T00\\:00\\:00Z TO 2017-01-31}", "{'range':{'date':{'gte':'2017-01-01T00:00:00Z','lt':'2017-01-31||/d'}}}")]
    [InlineData("date:[\"2024-01-01T00:00:00Z\" TO \"2024-02-01T00:00:00Z\"}", "{'range':{'date':{'gte':'2024-01-01T00:00:00Z','lt':'2024-02-01T00:00:00Z'}}}")]
    [InlineData("date:>2024-01-15T12:30:00Z||/h", "{'range':{'date':{'gt':'2024-01-15T12:30:00Z||/h'}}}")]
    [InlineData("dateNanos:[2024-01-01 TO *]", "{'range':{'dateNanos':{'gte':'2024-01-01||/d'}}}")]
    public void BuildQuery_WithDateRange_PassesDateMathThroughToElasticsearch(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("date:[2024-01-01 TO *]^\"America/Chicago\"", "America/Chicago")]
    [InlineData("dateNanos:[2024-01-01 TO *]^\"America/Chicago\"", "America/Chicago")]
    [InlineData("date:[2024-01-01 TO *]^\"Europe/London\"", "Europe/London")]
    [InlineData("date:[2024-01-01 TO *]^UTC", "UTC")]
    [InlineData("date:[2024-01-01 TO *]^-5h", "-05:00")]
    [InlineData("date:[2024-01-01 TO *]^1h", "+01:00")]
    [InlineData("date:[2024-01-01 TO *]^+330m", "+05:30")]
    [InlineData("date:[2024-01-01 TO *]^\"+05:30\"", "+05:30")]
    [InlineData("date:[2024-01-01 TO *]^\"-05:00\"", "-05:00")]
    [InlineData("date:>=2024-01-01^\"America/Chicago\"", "America/Chicago")]
    public void BuildQuery_WithDateRangeCaret_EmitsTimeZone(string query, string timeZone)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json($"{{'range':{{'{query[..query.IndexOf(':')]}':{{'gte':'2024-01-01||/d','time_zone':'{timeZone}'}}}}}}", result);
    }

    [Theory]
    [InlineData("date:[2024-01-01 TO *]^2")]
    [InlineData("date:[2024-01-01 TO *]^1.5")]
    [InlineData("dateNanos:[2024-01-01 TO *]^2")]
    public void BuildQuery_WithNumericCaretOnDateRange_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains("time zone", exception.Message);
    }

    [Theory]
    [InlineData("date:[2024-01-01 TO now]", "{'range':{'date':{'gte':'2024-01-01||/d','lte':'now','time_zone':'America/Chicago'}}}")]
    [InlineData("date:>2024-01-01", "{'range':{'date':{'gt':'2024-01-01||/d','time_zone':'America/Chicago'}}}")]
    [InlineData("date:[2024-01-01 TO *]^UTC", "{'range':{'date':{'gte':'2024-01-01||/d','time_zone':'UTC'}}}")]
    [InlineData("date:[2024-01-01 TO *]^-5h", "{'range':{'date':{'gte':'2024-01-01||/d','time_zone':'-05:00'}}}")]
    [InlineData("number:[1 TO 5]", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("date:2024-01-01", "{'range':{'date':{'gte':'2024-01-01||/d','lte':'2024-01-01||/d','time_zone':'America/Chicago'}}}")]
    public void BuildQuery_WithDefaultTimeZone_AppliesItToDateRangesWithoutCaret(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultTimeZone = "America/Chicago");

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithDefaultTimeZonePerRequest_OverridesConfiguredTimeZone()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultTimeZone = "America/Chicago");

        var result = parser.BuildQuery("date:>2024-01-01", new ElasticsearchQueryOptions { DefaultTimeZone = "Europe/London" });

        ElasticAssert.Json("{'range':{'date':{'gt':'2024-01-01||/d','time_zone':'Europe/London'}}}", result);
    }

    [Fact]
    public void BuildQuery_WithDateRangeOrTerm_KeepsTimeZoneOnDateRangeOnly()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultTimeZone = "America/Chicago");

        var result = parser.BuildQuery("date:[2017-01-01T00\\:00\\:00Z TO 2017-01-31} OR text:value1");

        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'range':{'date':{'gte':'2017-01-01T00:00:00Z','lt':'2017-01-31||/d','time_zone':'America/Chicago'}}},{'match':{'text':{'query':'value1'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithRangeOnDateAlias_UsesAliasTargetType()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("dateAlias:[2024-01-01 TO *]^\"America/Chicago\"");

        ElasticAssert.Json("{'range':{'dateAlias':{'gte':'2024-01-01||/d','time_zone':'America/Chicago'}}}", result);
    }
}
