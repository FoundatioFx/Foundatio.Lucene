using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class ParserBehaviorTests
{
    [Fact]
    public void BuildQuery_WithParsedDocument_DoesNotModifyTheDocument()
    {
        var parser = TestMapping.CreateScoringParser(c => c.Includes = new Dictionary<string, string> { ["saved"] = "keyword:saved" });
        var document = LuceneQuery.Parse("user:x @include:saved number:[1 TO 5]").Document;
        string before = QueryStringBuilder.ToQueryString(document);

        var first = parser.BuildQuery(document, new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "keyword" } } });
        var second = parser.BuildQuery(document, new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "obj.desc" } }, UseScoring = false });

        Assert.Equal(before, QueryStringBuilder.ToQueryString(document));
        Assert.Equal("user", Assert.IsType<FieldQueryNode>(((BooleanQueryNode)document.Query!).Clauses[0].Query).Field);
        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'x'}}},{'term':{'keyword':{'value':'saved'}}},{'range':{'number':{'gte':'1','lte':'5'}}}]}}", first);
        ElasticAssert.Json("{'bool':{'filter':[{'match':{'obj.desc':{'query':'x'}}},{'term':{'keyword':{'value':'saved'}}},{'range':{'number':{'gte':'1','lte':'5'}}}]}}", second);
    }

    [Fact]
    public async Task BuildQueryAsync_WithParsedDocument_DoesNotModifyTheDocument()
    {
        var parser = TestMapping.CreateScoringParser(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("keyword:resolved"));
        var document = LuceneQuery.Parse("KEYWORD:x @include:saved").Document;
        string before = QueryStringBuilder.ToQueryString(document);

        var first = await parser.BuildQueryAsync(document, cancellationToken: TestContext.Current.CancellationToken);
        var second = await parser.BuildQueryAsync(document, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before, QueryStringBuilder.ToQueryString(document));
        Assert.Equal(ElasticAssert.Serialize(first), ElasticAssert.Serialize(second));
        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'x'}}},{'term':{'keyword':{'value':'resolved'}}}]}}", first);
    }

    [Fact]
    public void BuildQuery_WithDocumentMetadataSetByVisitor_DoesNotLeakIntoCallerDocument()
    {
        var parser = TestMapping.CreateScoringParser(c => c.FieldMap = new FieldMap { { "user", "keyword" } });
        var document = LuceneQuery.Parse("user:x").Document;

        parser.BuildQuery(document);

        var field = Assert.IsType<FieldQueryNode>(document.Query);
        Assert.False(field.HasData);
        Assert.Equal("user", field.Field);
    }

    public static IEnumerable<object[]> AsyncConfigurations() =>
    [
        ["include resolver", (Action<ElasticsearchQueryParserConfiguration>)(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>(null))],
        ["async field resolver", (Action<ElasticsearchQueryParserConfiguration>)(c => c.AsyncFieldResolver = (f, _, _) => ValueTask.FromResult<string?>(f))],
        ["geo location resolver", (Action<ElasticsearchQueryParserConfiguration>)(c => c.GeoLocationResolver = (_, _, _) => ValueTask.FromResult<string?>(null))],
        ["runtime field resolver", (Action<ElasticsearchQueryParserConfiguration>)(c => c.RuntimeFieldResolver = (_, _, _) => ValueTask.FromResult<ElasticRuntimeField?>(null))],
        ["nested filter resolver", (Action<ElasticsearchQueryParserConfiguration>)(c => c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(null))]
    ];

    [Theory]
    [MemberData(nameof(AsyncConfigurations))]
    public async Task Build_WithAsynchronousDependency_RequiresAsyncMethods(string dependency, Action<ElasticsearchQueryParserConfiguration> configure)
    {
        var parser = TestMapping.CreateParser(configure);

        Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("keyword:x"));
        Assert.Throws<InvalidOperationException>(() => parser.BuildAggregations("terms:keyword"));
        Assert.Throws<InvalidOperationException>(() => parser.BuildSort("keyword"));
        Assert.Throws<InvalidOperationException>(() => parser.ValidateQuery("keyword:x"));
        Assert.Throws<InvalidOperationException>(() => parser.ValidateAggregations("terms:keyword"));
        Assert.Throws<InvalidOperationException>(() => parser.ValidateSort("keyword"));
        Assert.True((await parser.ValidateQueryAsync("keyword:x", cancellationToken: TestContext.Current.CancellationToken)).IsValid);
        Assert.NotNull(await parser.BuildQueryAsync("keyword:x", cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotEmpty(await parser.BuildAggregationsAsync("terms:keyword", cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotEmpty(await parser.BuildSortAsync("keyword", cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotEmpty(dependency);
    }

    [Fact]
    public void BuildQuery_WithAsyncDependencyOnlyInOtherRequestOptions_UsesSyncPath()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("keyword:x", new ElasticsearchQueryOptions { DefaultFields = ["text"] });

        ElasticAssert.Json("{'term':{'keyword':{'value':'x'}}}", result);
        Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("keyword:x", new ElasticsearchQueryOptions { IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>(null) }));
    }

    [Fact]
    public Task BuildQuery_WithParallelRequestsAndDifferentOptions_KeepsRequestsIsolated()
    {
        var parser = TestMapping.CreateParser(c => c.Includes = new Dictionary<string, string> { ["saved"] = "keyword:saved" });
        var cases = new (ElasticsearchQueryOptions Options, string Query, string Expected)[]
        {
            (new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "keyword" } }, UseScoring = true }, "user:x", "{'term':{'keyword':{'value':'x'}}}"),
            (new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "obj.desc" } }, UseScoring = true }, "user:x", "{'match':{'obj.desc':{'query':'x'}}}"),
            (new ElasticsearchQueryOptions { DefaultFields = ["text"], UseScoring = true }, "hello", "{'match':{'text':{'query':'hello'}}}"),
            (new ElasticsearchQueryOptions { DefaultFields = ["keyword"] }, "hello", "{'bool':{'filter':{'term':{'keyword':{'value':'hello'}}}}}"),
            (new ElasticsearchQueryOptions { DefaultOperator = BooleanOperator.Or, UseScoring = true }, "keyword:a keyword:b", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}"),
            (new ElasticsearchQueryOptions { DefaultTimeZone = "Europe/London", UseScoring = true }, "date:>2024-01-01", "{'range':{'date':{'gt':'2024-01-01','time_zone':'Europe/London'}}}"),
            (new ElasticsearchQueryOptions { Includes = new Dictionary<string, string> { ["saved"] = "keyword:other" }, UseScoring = true }, "@include:saved", "{'term':{'keyword':{'value':'other'}}}"),
            (new ElasticsearchQueryOptions(), "@include:saved", "{'bool':{'filter':{'term':{'keyword':{'value':'saved'}}}}}")
        };

        var tasks = Enumerable.Range(0, 400).Select(i => Task.Run(() =>
        {
            var testCase = cases[i % cases.Length];
            var result = parser.BuildQuery(testCase.Query, testCase.Options);
            ElasticAssert.Json(testCase.Expected, result);
        }, TestContext.Current.CancellationToken));

        return Task.WhenAll(tasks);
    }

    [Fact]
    public Task BuildQueryAsync_WithParallelRequestsAndAsyncResolvers_KeepsRequestsIsolated()
    {
        var parser = TestMapping.CreateScoringParser(c => c.AsyncFieldResolver = async (field, context, cancellationToken) =>
        {
            await Task.Yield();
            return field == "user" ? context.GetValue<string>("target") : null;
        });

        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            string target = i % 2 == 0 ? "keyword" : "obj.name";
            var context = parser.CreateContext();
            context.SetValue("target", target);
            var result = await parser.BuildQueryAsync($"user:value{i}", context, TestContext.Current.CancellationToken);
            ElasticAssert.Json($"{{'term':{{'{target}':{{'value':'value{i}'}}}}}}", result);
        }, TestContext.Current.CancellationToken));

        return Task.WhenAll(tasks);
    }

    [Fact]
    public void SetOptions_WithRegisteredIndexOptions_AppliesThemBeneathRequestOptions()
    {
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);
        var ordersMapping = new TypeMapping { Properties = new Properties { { "status", new KeywordProperty() }, { "notes", new TextProperty() } } };
        parser.SetOptions("orders", new ElasticsearchQueryOptions
        {
            MappingResolver = ElasticMappingResolver.Create(ordersMapping),
            DefaultFields = ["notes"],
            FieldMap = new FieldMap { { "state", "status" } }
        });

        var registered = parser.BuildQuery("state:open hello", new ElasticsearchQueryOptions { Index = "orders" });
        var overridden = parser.BuildQuery("state:open", new ElasticsearchQueryOptions { Index = "orders", FieldMap = new FieldMap { { "state", "notes" } } });
        var unregistered = parser.BuildQuery("state:open", new ElasticsearchQueryOptions { Index = "customers" });

        ElasticAssert.Json("{'bool':{'must':[{'term':{'status':{'value':'open'}}},{'match':{'notes':{'query':'hello'}}}]}}", registered);
        ElasticAssert.Json("{'match':{'notes':{'query':'open'}}}", overridden);
        ElasticAssert.Json("{'term':{'state':{'value':'open'}}}", unregistered);
    }

    [Fact]
    public void SetOptions_WithIndexName_CanBeRetrievedCaseInsensitivelyAndRemoved()
    {
        var parser = new ElasticsearchQueryParser();
        var options = new ElasticsearchQueryOptions { DefaultFields = ["a"] };

        parser.SetOptions("Orders", options);

        Assert.Same(options, parser.GetOptions("orders"));
        Assert.True(parser.RemoveOptions("ORDERS"));
        Assert.Null(parser.GetOptions("orders"));
        Assert.False(parser.RemoveOptions("orders"));
        Assert.Throws<ArgumentException>(() => parser.SetOptions("", options));
        Assert.Throws<ArgumentNullException>(() => parser.SetOptions("x", null!));
    }

    [Fact]
    public void CreateContext_WithOptions_MergesConfigurationRegisteredAndRequestOptions()
    {
        var resolver = ElasticMappingResolver.Create(TestMapping.Create());
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(resolver);
            c.DefaultTimeZone = "UTC";
            c.DefaultFields = ["text"];
        });
        parser.SetOptions("idx", new ElasticsearchQueryOptions { DefaultTimeZone = "America/Chicago", UseScoring = true });
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var context = parser.CreateContext(new ElasticsearchQueryOptions { Index = "idx", StartDate = start, DefaultOperator = BooleanOperator.Or });

        Assert.Same(resolver, context.MappingResolver);
        Assert.Equal("America/Chicago", context.DefaultTimeZone);
        Assert.True(context.UseScoring);
        Assert.Equal(["text"], context.DefaultFields!);
        Assert.Equal(start, context.StartDate);
        Assert.Null(context.EndDate);
        Assert.Equal(BooleanOperator.Or, context.DefaultOperator);
        Assert.True(context.UseNested);
    }

    [Fact]
    public void BuildQuery_WithSharedContext_AccumulatesValidationResult()
    {
        var parser = TestMapping.CreateParser();
        var context = parser.CreateContext();

        parser.BuildQuery("keyword:x", context);

        Assert.Contains("keyword", context.ValidationResult.ReferencedFields);
        Assert.True(context.ValidationResult.IsValid);
    }

    [Fact]
    public void BuildQuery_WithNullContext_ThrowsArgumentNullException()
    {
        var parser = TestMapping.CreateParser();

        Assert.Throws<ArgumentNullException>(() => parser.BuildQuery("keyword:x", (ElasticsearchQueryVisitorContext)null!));
        Assert.Throws<ArgumentNullException>(() => parser.BuildQuery((QueryDocument)null!));
        Assert.Throws<ArgumentNullException>(() => parser.BuildAggregations(null!));
        Assert.Throws<ArgumentNullException>(() => parser.BuildSort(null!));
    }

    [Fact]
    public async Task BuildQueryAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.BuildQueryAsync("@include:slow", cancellationToken: cancellation.Token).AsTask());
    }
}
