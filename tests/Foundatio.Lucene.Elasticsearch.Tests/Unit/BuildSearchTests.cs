using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class BuildSearchTests
{
    [Fact]
    public void BuildSearch_WithAllParts_BuildsQueryAggregationsAndSort()
    {
        var parser = TestMapping.CreateParser();

        var search = parser.BuildSearch("keyword:a", "terms:keyword", "-date");

        ElasticAssert.Json("{'bool':{'filter':{'term':{'keyword':{'value':'a'}}}}}", search.Query);
        ElasticAssert.Json("{'terms_keyword':{'terms':{'field':'keyword'},'meta':{'@field_type':'keyword'}}}", search.Aggregations);
        ElasticAssert.Json("[{'date':{'order':'desc','unmapped_type':'date'}}]", search.Sort);
        Assert.Empty(search.RuntimeFields);
        Assert.Null(search.GetRuntimeMappings());
    }

    [Fact]
    public void BuildSearch_WithNullParts_LeavesThemNull()
    {
        var parser = TestMapping.CreateParser();

        var search = parser.BuildSearch(null, sort: "keyword");

        Assert.Null(search.Query);
        Assert.Null(search.Aggregations);
        Assert.NotNull(search.Sort);
    }

    [Fact]
    public void BuildSearch_WithOptions_AppliesOptionsToEveryPart()
    {
        var parser = TestMapping.CreateParser();
        var options = new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "text" } }, UseScoring = true };

        var search = parser.BuildSearch("user:x", "terms:user", "user", options);

        ElasticAssert.Json("{'match':{'text':{'query':'x'}}}", search.Query);
        ElasticAssert.Json("{'terms_user':{'terms':{'field':'text.keyword'},'meta':{'@field_type':'text'}}}", search.Aggregations);
        ElasticAssert.Json("[{'text.keyword':{'order':'asc','unmapped_type':'keyword'}}]", search.Sort);
    }

    [Fact]
    public void BuildSearch_WithInvalidPart_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser();

        Assert.Throws<QueryValidationException>(() => parser.BuildSearch("keyword:a", "foo:bar"));
        Assert.Throws<QueryValidationException>(() => parser.BuildSearch("keyword:a", sort: "!keyword"));
        Assert.Throws<QueryParseException>(() => parser.BuildSearch("keyword:(a"));
    }

    [Fact]
    public void BuildSearch_WithAsyncDependency_ThrowsInvalidOperationException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>(null));

        Assert.Throws<InvalidOperationException>(() => parser.BuildSearch("keyword:a"));
    }

    [Fact]
    public async Task BuildSearchAsync_WithRuntimeFieldsInEveryPart_CollectsEachRuntimeFieldOnce()
    {
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (field, _, _) => ValueTask.FromResult(field switch
        {
            "q_field" => new ElasticRuntimeField("q_field", RuntimeFieldType.Keyword, "emit('q')"),
            "a_field" => new ElasticRuntimeField("a_field", RuntimeFieldType.Long, "emit(1)"),
            "s_field" => new ElasticRuntimeField("s_field", RuntimeFieldType.Double, "emit(2.5)"),
            _ => null
        }));

        var search = await parser.BuildSearchAsync("q_field:x a_field:1", "terms:a_field", "-s_field -q_field", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a_field", "q_field", "s_field"], search.RuntimeFields.Select(f => f.Name).Order(StringComparer.Ordinal));
        ElasticAssert.Json("{'bool':{'filter':[{'term':{'q_field':{'value':'x'}}},{'term':{'a_field':{'value':1}}}]}}", search.Query);
        ElasticAssert.Json("[{'s_field':{'order':'desc','unmapped_type':'double'}},{'q_field':{'order':'desc','unmapped_type':'keyword'}}]", search.Sort);
        ElasticAssert.Json("""
            {"a_field":{"script":{"source":"emit(1)"},"type":"long"},
             "q_field":{"script":{"source":"emit('q')"},"type":"keyword"},
             "s_field":{"script":{"source":"emit(2.5)"},"type":"double"}}
            """, search.GetRuntimeMappings());
    }

    [Fact]
    public async Task BuildSearchAsync_WithIncludeResolver_ResolvesIncludesInEveryPart()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (name, _, _) => ValueTask.FromResult<string?>(name switch
        {
            "active" => "keyword:active",
            "stats" => "max:number",
            "newest" => "-date",
            _ => null
        }));

        var search = await parser.BuildSearchAsync("@include:active", "@include:stats", "@include:newest", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'filter':{'term':{'keyword':{'value':'active'}}}}}", search.Query);
        ElasticAssert.Json("{'max_number':{'max':{'field':'number'},'meta':{'@field_type':'integer'}}}", search.Aggregations);
        ElasticAssert.Json("[{'date':{'order':'desc','unmapped_type':'date'}}]", search.Sort);
    }

    [Fact]
    public async Task ApplyTo_WithSearchRequest_SetsBuiltPartsAndRuntimeMappings()
    {
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (field, _, _) =>
            ValueTask.FromResult(field == "computed" ? new ElasticRuntimeField("computed", RuntimeFieldType.Long, "emit(1)") : null));
        var search = await parser.BuildSearchAsync("computed:1", "max:number", "-computed", cancellationToken: TestContext.Current.CancellationToken);
        var request = new SearchRequest("index") { Size = 5 };

        var applied = search.ApplyTo(request);

        Assert.Same(request, applied);
        ElasticAssert.Json("""
            {"aggregations":{"max_number":{"max":{"field":"number"},"meta":{"@field_type":"integer"}}},
             "query":{"bool":{"filter":{"term":{"computed":{"value":1}}}}},
             "runtime_mappings":{"computed":{"script":{"source":"emit(1)"},"type":"long"}},
             "size":5,
             "sort":{"computed":{"order":"desc","unmapped_type":"long"}}}
            """, request);
    }

    [Fact]
    public void ApplyTo_WithPartialSearch_LeavesOtherRequestPartsUnchanged()
    {
        var parser = TestMapping.CreateParser();
        var search = parser.BuildSearch(null, sort: "keyword");
        var existingQuery = new Elastic.Clients.Elasticsearch.QueryDsl.TermQuery("keyword", "existing");
        var request = new SearchRequest { Query = existingQuery };

        search.ApplyTo(request);

        ElasticAssert.Json("{'query':{'term':{'keyword':{'value':'existing'}}},'sort':{'keyword':{'order':'asc','unmapped_type':'keyword'}}}", request);
    }

    [Fact]
    public void Apply_WithSearchRequestDescriptor_SetsBuiltParts()
    {
        var parser = TestMapping.CreateParser();
        var search = parser.BuildSearch("keyword:a", "terms:keyword", "-date");
        var descriptor = new SearchRequestDescriptor<object>();

        descriptor.Apply(search).Size(0);
        SearchRequest request = descriptor;

        ElasticAssert.Json("""
            {"aggregations":{"terms_keyword":{"terms":{"field":"keyword"},"meta":{"@field_type":"keyword"}}},
             "query":{"bool":{"filter":{"term":{"keyword":{"value":"a"}}}}},
             "size":0,
             "sort":{"date":{"order":"desc","unmapped_type":"date"}}}
            """, request);
    }
}
