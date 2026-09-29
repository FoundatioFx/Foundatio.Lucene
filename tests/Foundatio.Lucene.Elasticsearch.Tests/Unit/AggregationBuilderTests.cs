using System.Text.Json.Nodes;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class AggregationBuilderTests
{
    [Theory]
    [InlineData("min:number", "{'min_number':{'min':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    [InlineData("max:number~0", "{'max_number':{'max':{'field':'number','missing':0},'meta':{'@field_type':'integer'}}}")]
    [InlineData("avg:double~1.5", "{'avg_double':{'avg':{'field':'double','missing':1.5},'meta':{'@field_type':'double'}}}")]
    [InlineData("sum:long", "{'sum_long':{'sum':{'field':'long'},'meta':{'@field_type':'long'}}}")]
    [InlineData("stats:number", "{'stats_number':{'stats':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    [InlineData("exstats:number", "{'exstats_number':{'extended_stats':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    [InlineData("cardinality:keyword~0^100", "{'cardinality_keyword':{'cardinality':{'field':'keyword','missing':0,'precision_threshold':100}}}")]
    [InlineData("cardinality:text", "{'cardinality_text':{'cardinality':{'field':'text.keyword'}}}")]
    [InlineData("missing:keyword", "{'missing_keyword':{'missing':{'field':'keyword'}}}")]
    [InlineData("missing:text", "{'missing_text':{'missing':{'field':'text.keyword'}}}")]
    [InlineData("percentiles:number", "{'percentiles_number':{'percentiles':{'field':'number','keyed':false}}}")]
    [InlineData("percentiles:number~50,95,99.9", "{'percentiles_number':{'percentiles':{'field':'number','keyed':false,'percents':[50,95,99.9]}}}")]
    [InlineData("histogram:number", "{'histogram_number':{'histogram':{'field':'number','interval':50,'min_doc_count':0}}}")]
    [InlineData("histogram:(number~0.1)", "{'histogram_number':{'histogram':{'field':'number','interval':0.1,'min_doc_count':0}}}")]
    [InlineData("terms:keyword", "{'terms_keyword':{'terms':{'field':'keyword'},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:keyword~10^2", "{'terms_keyword':{'terms':{'field':'keyword','min_doc_count':2,'size':10},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:keyword~9000", "{'terms_keyword':{'terms':{'field':'keyword','shard_size':10000,'size':9000},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:unknownfield", "{'terms_unknownfield':{'terms':{'field':'unknownfield'}}}")]
    [InlineData("tophits:_~2", "{'tophits':{'top_hits':{'size':2}}}")]
    [InlineData("tophits:(_~3 @include:title @exclude:body)", "{'tophits':{'top_hits':{'size':3,'_source':{'excludes':'body','includes':'title'}}}}")]
    [InlineData("min:(number)", "{'min_number':{'min':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    [InlineData("    avg     :   number", "{'avg_number':{'avg':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    public void BuildAggregations_WithAggregationType_EmitsAggregation(string expression, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildAggregations_WithGeoGrid_AddsCentroidSubAggregations()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("geogrid:geo~6");

        ElasticAssert.Json("""
            {"geogrid_geo":{"geohash_grid":{"field":"geo","precision":6},"aggregations":{
              "avg_lat":{"avg":{"script":{"source":"doc['geo'].lat"}}},
              "avg_lon":{"avg":{"script":{"source":"doc['geo'].lon"}}}}}}
            """, result);
    }

    [Fact]
    public void BuildAggregations_WithManyAggregations_EmitsEveryAggregationWithConventionalNames()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("min:number max:number avg:number sum:number percentiles:number~50,100 cardinality:number missing:keyword date:date histogram:number geogrid:geo terms:text");

        ElasticAssert.Json("""
            {
              "min_number":{"min":{"field":"number"},"meta":{"@field_type":"integer"}},
              "max_number":{"max":{"field":"number"},"meta":{"@field_type":"integer"}},
              "avg_number":{"avg":{"field":"number"},"meta":{"@field_type":"integer"}},
              "sum_number":{"sum":{"field":"number"},"meta":{"@field_type":"integer"}},
              "percentiles_number":{"percentiles":{"field":"number","keyed":false,"percents":[50,100]}},
              "cardinality_number":{"cardinality":{"field":"number"}},
              "missing_keyword":{"missing":{"field":"keyword"}},
              "date_date":{"date_histogram":{"calendar_interval":"day","field":"date","format":"date_optional_time","min_doc_count":0}},
              "histogram_number":{"histogram":{"field":"number","interval":50,"min_doc_count":0}},
              "geogrid_geo":{"geohash_grid":{"field":"geo","precision":1},"aggregations":{"avg_lat":{"avg":{"script":{"source":"doc['geo'].lat"}}},"avg_lon":{"avg":{"script":{"source":"doc['geo'].lon"}}}}},
              "terms_text":{"terms":{"field":"text.keyword"},"meta":{"@field_type":"keyword"}}
            }
            """, result);
        Assert.Equal(["min_number", "max_number", "avg_number", "sum_number", "percentiles_number", "cardinality_number", "missing_keyword", "date_date", "histogram_number", "geogrid_geo", "terms_text"], result.Keys);
    }

    [Theory]
    [InlineData("terms:text")]
    [InlineData("terms:(text)")]
    public void BuildAggregations_WithAnalyzedField_UsesKeywordSubFieldAndItsType(string expression)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json("{'terms_text':{'terms':{'field':'text.keyword'},'meta':{'@field_type':'keyword'}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithAnalyzedFieldWithSortAndKeywordSubFields_PrefersKeywordSubField()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("terms:text2");

        ElasticAssert.Json("{'terms_text2':{'terms':{'field':'text2.keyword'},'meta':{'@field_type':'keyword'}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithMappedAlias_ReportsAliasTargetType()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("min:alias");

        ElasticAssert.Json("{'min_alias':{'min':{'field':'alias'},'meta':{'@field_type':'keyword'}}}", result);
    }

    [Theory]
    [InlineData("terms:(keyword~10 @include:a @include:b @exclude:c @missing:none @min:5)", "{'terms_keyword':{'terms':{'exclude':['c'],'field':'keyword','include':['a','b'],'min_doc_count':5,'missing':'none','size':10},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword @exclude:myexclude @include:myinclude @include:otherinclude @missing:mymissing @exclude:otherexclude @min:1)", "{'terms_keyword':{'terms':{'exclude':['myexclude','otherexclude'],'field':'keyword','include':['myinclude','otherinclude'],'min_doc_count':1,'missing':'mymissing'},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword @include:/a.*/ @exclude:/b.*/)", "{'terms_keyword':{'terms':{'exclude':'b.*','field':'keyword','include':'a.*'},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword~100 @missing:__missing__)", "{'terms_keyword':{'terms':{'field':'keyword','missing':'__missing__','size':100},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword~100 (@missing:__missing__))", "{'terms_keyword':{'terms':{'field':'keyword','missing':'__missing__','size':100},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword @min:3)^1", "{'terms_keyword':{'terms':{'field':'keyword','min_doc_count':3},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("terms:(keyword @missing:\"no value\")", "{'terms_keyword':{'terms':{'field':'keyword','missing':'no value'},'meta':{'@field_type':'keyword'}}}")]
    public void BuildAggregations_WithTermsModifiers_AppliesModifiers(string expression, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("terms:(keyword -max:number)", "{'max_number':'desc'}")]
    [InlineData("terms:(keyword +max:number)", "{'max_number':'asc'}")]
    [InlineData("terms:(keyword -cardinality:number)", "{'cardinality_number':'desc'}")]
    [InlineData("terms:(keyword -max:number +min:number)", "[{'max_number':'desc'},{'min_number':'asc'}]")]
    [InlineData("terms:(keyword +min:number -max:number)", "[{'min_number':'asc'},{'max_number':'desc'}]")]
    [InlineData("terms:(keyword -cardinality:obj.name)", "{'cardinality_obj.name[value]':'desc'}")]
    [InlineData("terms:(keyword +avg:obj.num -stats:number)", "[{'avg_obj.num[value]':'asc'},{'stats_number':'desc'}]")]
    public void BuildAggregations_WithOrderedSubAggregation_OrdersTermsBucketsInSourceOrder(string expression, string order)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        var terms = JsonNode.Parse(ElasticAssert.Serialize(result["terms_keyword"]))!["terms"]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(order.Replace('\'', '"')), terms["order"]), terms.ToJsonString());
    }

    [Fact]
    public void BuildAggregations_WithUnorderedSubAggregation_OmitsTermsOrder()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("terms:(keyword cardinality:number)");

        ElasticAssert.Json("{'terms_keyword':{'terms':{'field':'keyword'},'aggregations':{'cardinality_number':{'cardinality':{'field':'number'}}},'meta':{'@field_type':'keyword'}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithOrderedAndUnorderedSubAggregations_BuildsAllSubAggregations()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("terms:(keyword -max:number +min:number avg:number)");

        ElasticAssert.Json("""
            {"terms_keyword":{"terms":{"field":"keyword","order":[{"max_number":"desc"},{"min_number":"asc"}]},
              "aggregations":{
                "max_number":{"max":{"field":"number"},"meta":{"@field_type":"integer"}},
                "min_number":{"min":{"field":"number"},"meta":{"@field_type":"integer"}},
                "avg_number":{"avg":{"field":"number"},"meta":{"@field_type":"integer"}}},
              "meta":{"@field_type":"keyword"}}}
            """, result);
    }

    [Theory]
    [InlineData("-terms:keyword")]
    [InlineData("+terms:keyword")]
    public void BuildAggregations_WithPrefixOnTopLevelAggregation_IgnoresPrefix(string expression)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json("{'terms_keyword':{'terms':{'field':'keyword'},'meta':{'@field_type':'keyword'}}}", result);
    }

    [Theory]
    [InlineData("terms:(keyword~1000^2 tophits:(_~1000 @include:myinclude))", "{'terms_keyword':{'terms':{'field':'keyword','min_doc_count':2,'size':1000},'aggregations':{'tophits':{'top_hits':{'size':1000,'_source':{'includes':'myinclude'}}}},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("date:(date~month terms:(keyword~5 sum:number))", "{'date_date':{'date_histogram':{'calendar_interval':'month','field':'date','format':'date_optional_time','min_doc_count':0},'aggregations':{'terms_keyword':{'terms':{'field':'keyword','size':5},'aggregations':{'sum_number':{'sum':{'field':'number'},'meta':{'@field_type':'integer'}}},'meta':{'@field_type':'keyword'}}}}}")]
    [InlineData("terms:(number histogram:(number~5))", "{'terms_number':{'terms':{'field':'number'},'aggregations':{'histogram_number':{'histogram':{'field':'number','interval':5,'min_doc_count':0}}},'meta':{'@field_type':'integer'}}}")]
    [InlineData("histogram:(number~10 max:double)", "{'histogram_number':{'histogram':{'field':'number','interval':10,'min_doc_count':0},'aggregations':{'max_double':{'max':{'field':'double'},'meta':{'@field_type':'double'}}}}}")]
    [InlineData("missing:(keyword max:number)", "{'missing_keyword':{'missing':{'field':'keyword'},'aggregations':{'max_number':{'max':{'field':'number'},'meta':{'@field_type':'integer'}}}}}")]
    public void BuildAggregations_WithBucketSubAggregations_NestsSubAggregations(string expression, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("1s", "calendar_interval", "second")]
    [InlineData("second", "calendar_interval", "second")]
    [InlineData("m", "calendar_interval", "minute")]
    [InlineData("1m", "calendar_interval", "minute")]
    [InlineData("minute", "calendar_interval", "minute")]
    [InlineData("h", "calendar_interval", "hour")]
    [InlineData("1h", "calendar_interval", "hour")]
    [InlineData("hour", "calendar_interval", "hour")]
    [InlineData("d", "calendar_interval", "day")]
    [InlineData("1d", "calendar_interval", "day")]
    [InlineData("day", "calendar_interval", "day")]
    [InlineData("w", "calendar_interval", "week")]
    [InlineData("1w", "calendar_interval", "week")]
    [InlineData("week", "calendar_interval", "week")]
    [InlineData("M", "calendar_interval", "month")]
    [InlineData("1M", "calendar_interval", "month")]
    [InlineData("month", "calendar_interval", "month")]
    [InlineData("q", "calendar_interval", "quarter")]
    [InlineData("1q", "calendar_interval", "quarter")]
    [InlineData("quarter", "calendar_interval", "quarter")]
    [InlineData("y", "calendar_interval", "year")]
    [InlineData("1y", "calendar_interval", "year")]
    [InlineData("year", "calendar_interval", "year")]
    [InlineData("23m", "fixed_interval", "23m")]
    [InlineData("1.5h", "fixed_interval", "1.5h")]
    [InlineData("2d", "fixed_interval", "2d")]
    public void BuildAggregations_WithDateHistogramInterval_UsesCalendarOrFixedInterval(string interval, string property, string value)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations($"date:(date~{interval})");

        ElasticAssert.Json($"{{'date_date':{{'date_histogram':{{'{property}':'{value}','field':'date','format':'date_optional_time','min_doc_count':0}}}}}}", result);
    }

    [Theory]
    [InlineData("date:date^-5h", "-05:00", "-5h")]
    [InlineData("date:date^1h", "+01:00", "1h")]
    [InlineData("date:date^\"America/Chicago\"", "America/Chicago", "America/Chicago")]
    [InlineData("date:(date^UTC)", "UTC", "UTC")]
    public void BuildAggregations_WithDateHistogramTimeZone_NormalizesOffsetAndRecordsMeta(string expression, string timeZone, string meta)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json($"{{'date_date':{{'date_histogram':{{'calendar_interval':'day','field':'date','format':'date_optional_time','min_doc_count':0,'time_zone':'{timeZone}'}},'meta':{{'@timezone':'{meta}'}}}}}}", result);
    }

    [Theory]
    [InlineData("date:date")]
    [InlineData("date:(date)")]
    public void BuildAggregations_WithDefaultTimeZone_AppliesItAndReportsItInMetaForTermAndGroupForms(string expression)
    {
        var parser = TestMapping.CreateParser(c => c.DefaultTimeZone = "America/Chicago");

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json("{'date_date':{'date_histogram':{'calendar_interval':'day','field':'date','format':'date_optional_time','min_doc_count':0,'time_zone':'America/Chicago'},'meta':{'@timezone':'America/Chicago'}}}", result);
    }

    [Theory]
    [InlineData("min:date", "min_date", "min", "America/Chicago")]
    [InlineData("max:date^1h", "max_date", "max", "1h")]
    public void BuildAggregations_WithMinMaxOnDate_RecordsTimeZoneMeta(string expression, string name, string type, string timeZone)
    {
        var parser = TestMapping.CreateParser(c => c.DefaultTimeZone = "America/Chicago");

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json($"{{'{name}':{{'{type}':{{'field':'date'}},'meta':{{'@field_type':'date','@timezone':'{timeZone}'}}}}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithDateHistogramAndMetrics_BuildsTimeZonedHistogramWithMetrics()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("min:number max:number date:(date~1d^\"America/Chicago\" min:number max:number min:date @offset:\"-6h\")");

        ElasticAssert.Json("""
            {
              "min_number":{"min":{"field":"number"},"meta":{"@field_type":"integer"}},
              "max_number":{"max":{"field":"number"},"meta":{"@field_type":"integer"}},
              "date_date":{"date_histogram":{"calendar_interval":"day","field":"date","format":"date_optional_time","min_doc_count":0,"offset":"-6h","time_zone":"America/Chicago"},
                "aggregations":{
                  "min_number":{"min":{"field":"number"},"meta":{"@field_type":"integer"}},
                  "max_number":{"max":{"field":"number"},"meta":{"@field_type":"integer"}},
                  "min_date":{"min":{"field":"date"},"meta":{"@field_type":"date"}}},
                "meta":{"@timezone":"America/Chicago"}}
            }
            """, result);
    }

    [Fact]
    public void BuildAggregations_WithDateHistogramMissingAndTimeZone_UsesUtcMissingValue()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("date:(date^1h @missing:\"0001-01-01T00:00:00\" min:date^1h max:date^1h)");

        ElasticAssert.Json("""
            {"date_date":{"date_histogram":{"calendar_interval":"day","field":"date","format":"date_optional_time","min_doc_count":0,"missing":-62135596800000,"time_zone":"+01:00"},
              "aggregations":{
                "min_date":{"min":{"field":"date"},"meta":{"@field_type":"date","@timezone":"1h"}},
                "max_date":{"max":{"field":"date"},"meta":{"@field_type":"date","@timezone":"1h"}}},
              "meta":{"@timezone":"1h"}}}
            """, result);
    }

    [Theory]
    [InlineData("date:(date~1d @offset:\"-6h\")", "-6h")]
    [InlineData("date:(date~1d @offset:\"+6h\")", "+6h")]
    [InlineData("date:(date~1d @offset:6h)", "6h")]
    [InlineData("date:(date~1d -@offset:6h)", "-6h")]
    [InlineData("date:(date~week @offset:\"-6h\")", "-6h")]
    public void BuildAggregations_WithDateHistogramOffset_PreservesSignedOffset(string expression, string offset)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        Assert.Equal(offset, result["date_date"].DateHistogram!.Offset);
    }

    [Theory]
    [InlineData("date:(date~1d @offset:-6h)")]
    [InlineData("date:(date~1d @offset:+6h)")]
    public void BuildAggregations_WithPostColonOffsetOperator_ThrowsValidationException(string expression)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildAggregations(expression));

        Assert.Contains("before the field name", exception.Message);
    }

    [Fact]
    public void BuildAggregations_WithStartAndEndDate_ChoosesFixedIntervalAndExtendedBounds()
    {
        var parser = TestMapping.CreateParser();
        var options = new ElasticsearchQueryOptions
        {
            StartDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var result = parser.BuildAggregations("date:date", options);

        ElasticAssert.Json("{'date_date':{'date_histogram':{'extended_bounds':{'max':'2024-02-01T00:00:00Z','min':'2024-01-01T00:00:00Z'},'field':'date','fixed_interval':'7h','format':'date_optional_time','min_doc_count':0}}}", result);
    }

    [Theory]
    [InlineData(1, "15s")]
    [InlineData(60, "30s")]
    [InlineData(600, "6m")]
    [InlineData(6000, "1h")]
    [InlineData(60 * 24 * 7, "2h")]
    [InlineData(60 * 24 * 365, "4d")]
    public void BuildAggregations_WithDateRange_TargetsAboutOneHundredBuckets(int minutes, string interval)
    {
        var parser = TestMapping.CreateParser();
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var options = new ElasticsearchQueryOptions { StartDate = start, EndDate = start.AddMinutes(minutes) };

        var result = parser.BuildAggregations("date:date", options);

        Assert.Equal(interval, ElasticAssert.Serialize(result["date_date"].DateHistogram!.FixedInterval).Trim('"'));
    }

    [Fact]
    public void BuildAggregations_WithExplicitIntervalAndDateRange_KeepsIntervalAndAddsBounds()
    {
        var parser = TestMapping.CreateParser();
        var options = new ElasticsearchQueryOptions
        {
            StartDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };

        var result = parser.BuildAggregations("date:date~1h", options);

        ElasticAssert.Json("{'date_date':{'date_histogram':{'calendar_interval':'hour','extended_bounds':{'max':'2024-01-02T00:00:00Z','min':'2024-01-01T00:00:00Z'},'field':'date','format':'date_optional_time','min_doc_count':0}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithOnlyStartDate_UsesDailyIntervalWithoutBounds()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("date:date", new ElasticsearchQueryOptions { StartDate = DateTimeOffset.UtcNow });

        ElasticAssert.Json("{'date_date':{'date_histogram':{'calendar_interval':'day','field':'date','format':'date_optional_time','min_doc_count':0}}}", result);
    }

    [Theory]
    [InlineData("terms:children.name max:children.num", "{'nested_children':{'nested':{'path':'children'},'aggregations':{'terms_children.name':{'terms':{'field':'children.name'},'meta':{'@field_type':'keyword'}},'max_children.num':{'max':{'field':'children.num'},'meta':{'@field_type':'integer'}}}}}")]
    [InlineData("terms:children.grand.name", "{'nested_children':{'nested':{'path':'children'},'aggregations':{'nested_children.grand':{'nested':{'path':'children.grand'},'aggregations':{'terms_children.grand.name':{'terms':{'field':'children.grand.name'},'meta':{'@field_type':'keyword'}}}}}}}")]
    [InlineData("terms:(children.name terms:children.grand.name)", "{'nested_children':{'nested':{'path':'children'},'aggregations':{'terms_children.name':{'terms':{'field':'children.name'},'aggregations':{'nested_children.grand':{'nested':{'path':'children.grand'},'aggregations':{'terms_children.grand.name':{'terms':{'field':'children.grand.name'},'meta':{'@field_type':'keyword'}}}}},'meta':{'@field_type':'keyword'}}}}}")]
    [InlineData("terms:(children.name @include:apple @include:banana)", "{'nested_children':{'nested':{'path':'children'},'aggregations':{'terms_children.name':{'terms':{'field':'children.name','include':['apple','banana']},'meta':{'@field_type':'keyword'}}}}}")]
    [InlineData("terms:children.text", "{'nested_children':{'nested':{'path':'children'},'aggregations':{'terms_children.text':{'terms':{'field':'children.text'},'meta':{'@field_type':'text'}}}}}")]
    public void BuildAggregations_WithNestedField_WrapsInNestedAggregation(string expression, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("terms:(keyword -max:children.num)", "{'nested_children>max_children.num[value]':'desc'}")]
    [InlineData("terms:(children.name -max:children.grand.name)", "{'nested_children.grand>max_children.grand.name[value]':'desc'}")]
    public void BuildAggregations_WithOrderingByNestedSubAggregation_UsesBucketPath(string expression, string order)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations(expression);

        string json = ElasticAssert.Serialize(result);
        Assert.Contains(order.Replace('\'', '"').Trim('{', '}').Replace(">", "\\u003E"), json);
    }

    [Fact]
    public void BuildAggregations_WithNestedDisabled_AggregatesNestedFieldsFlat()
    {
        var parser = TestMapping.CreateParser(c => c.UseNested = false);

        var result = parser.BuildAggregations("max:children.num");

        ElasticAssert.Json("{'max_children.num':{'max':{'field':'children.num'},'meta':{'@field_type':'integer'}}}", result);
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithNestedFilterResolver_WrapsInFilterAggregation()
    {
        var parser = CreateFilteredParser();

        var result = await parser.BuildAggregationsAsync("max:resellers.price terms:resellers.name", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("""
            {"nested_resellers":{"nested":{"path":"resellers"},"aggregations":{
              "filtered_max_resellers.price":{"filter":{"term":{"resellers.name":{"value":"Official"}}},"aggregations":{"max_resellers.price":{"max":{"field":"resellers.price"},"meta":{"@field_type":"double"}}}},
              "filtered_terms_resellers.name":{"filter":{"term":{"resellers.name":{"value":"Official"}}},"aggregations":{"terms_resellers.name":{"terms":{"field":"resellers.name"},"meta":{"@field_type":"keyword"}}}}}}}
            """, result);
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithNestedFilterAndOrdering_UsesFilteredBucketPath()
    {
        var parser = CreateFilteredParser();

        var result = await parser.BuildAggregationsAsync("terms:(keyword -max:resellers.price)", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("""
            {"terms_keyword":{"terms":{"field":"keyword","order":{"nested_resellers>filtered_max_resellers.price>max_resellers.price[value]":"desc"}},
              "aggregations":{"nested_resellers":{"nested":{"path":"resellers"},"aggregations":{"filtered_max_resellers.price":{"filter":{"term":{"resellers.name":{"value":"Official"}}},
                "aggregations":{"max_resellers.price":{"max":{"field":"resellers.price"},"meta":{"@field_type":"double"}}}}}}},
              "meta":{"@field_type":"keyword"}}}
            """, result);
    }

    [Theory]
    [InlineData("terms:(heynow cardinality:user)", "{'terms_heynow':{'terms':{'field':'text.keyword'},'aggregations':{'cardinality_user':{'cardinality':{'field':'obj.name'}}},'meta':{'@field_type':'keyword'}}}")]
    [InlineData("min:count", "{'min_count':{'min':{'field':'number'},'meta':{'@field_type':'integer'}}}")]
    [InlineData("missing:user", "{'missing_user':{'missing':{'field':'obj.name'}}}")]
    [InlineData("geogrid:location~3", null)]
    public void BuildAggregations_WithFieldMap_UsesAliasInNameAndTargetField(string expression, string? expected)
    {
        var parser = TestMapping.CreateParser(c => c.FieldMap = new FieldMap
        {
            { "heynow", "text" },
            { "user", "obj.name" },
            { "count", "number" },
            { "location", "geo" }
        });

        var result = parser.BuildAggregations(expression);

        if (expected is not null)
            ElasticAssert.Json(expected, result);
        else
            Assert.Equal("geo", result["geogrid_location"].GeohashGrid!.Field!.Name);
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithGeoLocationResolver_DoesNotResolveAggregationFields()
    {
        int calls = 0;
        var parser = TestMapping.CreateParser(c => c.GeoLocationResolver = (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult<string?>("invalid");
        });

        var result = await parser.BuildAggregationsAsync("geogrid:geo~3", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, calls);
        Assert.NotNull(result["geogrid_geo"].GeohashGrid);
    }

    [Fact]
    public void BuildAggregations_WithIncludes_ExpandsTopLevelIncludes()
    {
        var parser = TestMapping.CreateParser(c => c.Includes = new Dictionary<string, string> { ["stats"] = "min:number max:number" });

        var result = parser.BuildAggregations("@include:stats terms:(keyword @include:a)");

        Assert.Equal(["min_number", "max_number", "terms_keyword"], result.Keys);
        ElasticAssert.Json("{'terms':{'field':'keyword','include':['a']},'meta':{'@field_type':'keyword'}}", result["terms_keyword"]);
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithIncludeResolver_ExpandsIncludes()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (name, _, _) => ValueTask.FromResult<string?>(name == "stats" ? "max:number" : null));

        var result = await parser.BuildAggregationsAsync("@include:stats", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'max_number':{'max':{'field':'number'},'meta':{'@field_type':'integer'}}}", result);
    }

    [Theory]
    [InlineData("!@include:ordering", "!")]
    [InlineData("NOT @include:ordering", "NOT")]
    public void BuildAggregations_WithNegatedInclude_ThrowsValidationException(string expression, string _)
    {
        var parser = TestMapping.CreateParser(c => c.Includes = new Dictionary<string, string> { ["ordering"] = "max:number" });

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildAggregations(expression));

        Assert.Contains("is not supported in aggregation expressions", Assert.Single(exception.Errors).Message);
        Assert.Empty(exception.Result.UnresolvedIncludes);
    }

    [Theory]
    [InlineData("!max:number")]
    [InlineData("NOT cardinality:number")]
    [InlineData("terms:(keyword !cardinality:number)")]
    [InlineData("terms:(keyword NOT max:number)")]
    [InlineData("terms:(!keyword)")]
    [InlineData("terms:(NOT keyword)")]
    [InlineData("terms:(!keyword -max:number)")]
    [InlineData("terms:(keyword +max:(NOT number))")]
    public void BuildAggregations_WithBooleanNegation_ThrowsValidationException(string expression)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildAggregations(expression));

        Assert.Contains("is not supported in aggregation expressions", exception.Message);
        Assert.Contains("use + for ascending or - for descending order", exception.Message);
    }

    [Theory]
    [InlineData("foo:number", "Unknown aggregation type")]
    [InlineData("avg", "Unexpected")]
    [InlineData("avg:", "Expected a value")]
    [InlineData("terms:*", "Unexpected 'terms:*'")]
    [InlineData("terms:key*", "must specify a field")]
    [InlineData("percentiles:number~50,101", "Invalid percentile")]
    [InlineData("percentiles:number~abc", "Invalid percentile")]
    [InlineData("geogrid:geo~13", "precision must be between 1 and 12")]
    [InlineData("geogrid:geo~0", "precision must be between 1 and 12")]
    [InlineData("min:number~abc", "Invalid missing value")]
    [InlineData("histogram:number~abc", "Invalid interval")]
    [InlineData("terms:keyword~-1", "Invalid size")]
    [InlineData("terms:keyword^x", "Invalid minimum document count")]
    [InlineData("cardinality:keyword^x", "Invalid precision threshold")]
    [InlineData("terms:(keyword @foo:bar)", "Unknown modifier")]
    [InlineData("terms:(keyword @min:x)", "Invalid @min")]
    [InlineData("date:(date @missing:notadate)", "Invalid @missing date")]
    [InlineData("date:date~soon", "Invalid interval")]
    [InlineData("date:(date~1d @offset:later)", "Invalid @offset")]
    [InlineData("min:(number max:number)", "does not support sub-aggregations")]
    [InlineData("terms:(keyword number)", "Only one field may be specified")]
    [InlineData("terms:(keyword @missing:)", "Expected a value")]
    [InlineData("terms:before\\^after", "Field names cannot contain '^'")]
    public void BuildAggregations_WithInvalidExpression_ThrowsValidationException(string expression, string message)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildAggregations(expression));

        Assert.Contains(message, exception.Message);
        Assert.False(parser.ValidateAggregations(expression).IsValid);
    }

    [Fact]
    public void BuildAggregations_WithEmptyExpression_ReturnsNoAggregations()
    {
        var parser = TestMapping.CreateParser();

        Assert.Empty(parser.BuildAggregations(""));
        Assert.Empty(parser.BuildAggregations("   "));
    }

    [Fact]
    public void BuildAggregations_WithTwoTopHits_KeepsLastTopHits()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildAggregations("tophits:(_~2 @include:a) tophits:(_~3)");

        ElasticAssert.Json("{'tophits':{'top_hits':{'size':3}}}", result);
    }

    [Fact]
    public void BuildAggregations_WithoutMapping_UsesFieldsAsWritten()
    {
        var parser = new ElasticsearchQueryParser();

        var result = parser.BuildAggregations("terms:(field1 @exclude:/A.*/ @include:/B.*/) date:field5");

        ElasticAssert.Json("{'terms_field1':{'terms':{'exclude':'A.*','field':'field1','include':'B.*'}},'date_field5':{'date_histogram':{'calendar_interval':'day','field':'field5','format':'date_optional_time','min_doc_count':0}}}", result);
    }

    private static ElasticsearchQueryParser CreateFilteredParser()
    {
        return TestMapping.CreateParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.NestedPath == "resellers" ? (Query)new TermQuery("resellers.name", "Official") : null));
    }
}
