using System.Collections.Concurrent;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class IncludeTests
{
    private static readonly Dictionary<string, string> Includes = new()
    {
        ["saved"] = "keyword:a OR keyword:b",
        ["stuff"] = "keyword:value2",
        ["outer"] = "@include:saved keyword:c",
        ["self"] = "@include:self",
        ["aliased"] = "user:x",
        ["bad"] = "keyword:(a"
    };

    [Theory]
    [InlineData("@include:stuff", "{'term':{'keyword':{'value':'value2'}}}")]
    [InlineData("keyword:value1 @include:stuff", "{'bool':{'must':[{'term':{'keyword':{'value':'value1'}}},{'term':{'keyword':{'value':'value2'}}}]}}")]
    [InlineData("@include:saved keyword:c", "{'bool':{'must':[{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}},{'term':{'keyword':{'value':'c'}}}]}}")]
    [InlineData("@include:outer", "{'bool':{'must':[{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}},{'term':{'keyword':{'value':'c'}}}]}}")]
    [InlineData("-@include:saved", "{'bool':{'must_not':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("NOT @include:stuff", "{'bool':{'must_not':{'term':{'keyword':{'value':'value2'}}}}}")]
    [InlineData("keyword:x -@include:stuff", "{'bool':{'must':{'term':{'keyword':{'value':'x'}}},'must_not':{'term':{'keyword':{'value':'value2'}}}}}")]
    [InlineData("@include:saved^2", "{'bool':{'boost':2,'must':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("@INCLUDE:STUFF", "{'term':{'keyword':{'value':'value2'}}}")]
    [InlineData("@include:\"stuff\"", "{'term':{'keyword':{'value':'value2'}}}")]
    public void BuildQuery_WithStaticIncludes_ExpandsIncludeInPlace(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.Includes = Includes);

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithIncludeAndOrDefaultOperator_ParsesIncludeWithSameOperator()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.DefaultOperator = BooleanOperator.Or;
            c.Includes = new Dictionary<string, string> { ["pair"] = "keyword:a keyword:b" };
        });

        var result = parser.BuildQuery("+@include:pair keyword:c");

        ElasticAssert.Json("{'bool':{'must':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}},'should':{'term':{'keyword':{'value':'c'}}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithIncludeUsingAlias_ResolvesIncludedFields()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.Includes = Includes;
            c.FieldMap = new FieldMap { { "user", "keyword" } };
        });
        var context = parser.CreateContext();

        var result = parser.BuildQuery("@include:aliased", context);

        ElasticAssert.Json("{'term':{'keyword':{'value':'x'}}}", result);
        Assert.Contains("user", context.ValidationResult.ReferencedFields);
        Assert.Contains("aliased", context.ValidationResult.ReferencedIncludes);
    }

    [Theory]
    [InlineData("@include:missing", QueryErrorCode.UnresolvedInclude)]
    [InlineData("@include:self", QueryErrorCode.UnresolvedInclude)]
    [InlineData("@include:bad", QueryErrorCode.UnresolvedInclude)]
    public void BuildQuery_WithInvalidInclude_ThrowsValidationException(string query, QueryErrorCode code)
    {
        var parser = TestMapping.CreateParser(c => c.Includes = Includes);

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains(exception.Errors, e => e.Code == code);
    }

    [Fact]
    public void BuildQuery_WithUnresolvedIncludesAllowed_BuildsRemainingQuery()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.Includes = Includes;
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedIncludes = true };
        });
        var context = parser.CreateContext();

        var result = parser.BuildQuery("keyword:x @include:missing", context);

        Assert.Contains("missing", context.ValidationResult.UnresolvedIncludes);
        Assert.Contains("keyword", ElasticAssert.Serialize(result));
    }

    [Fact]
    public void BuildQuery_WithIncludesPerRequest_OverridesConfiguredIncludes()
    {
        var parser = TestMapping.CreateScoringParser(c => c.Includes = Includes);

        var result = parser.BuildQuery("@include:stuff", new ElasticsearchQueryOptions { Includes = new Dictionary<string, string> { ["stuff"] = "keyword:other" } });

        ElasticAssert.Json("{'term':{'keyword':{'value':'other'}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithIncludeResolver_ResolvesIncludesBeforeBuilding()
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.FieldMap = new FieldMap { { "included", "keyword" } };
            c.IncludeResolver = async (name, _, cancellationToken) =>
            {
                requested.Add(name);
                await Task.Delay(10, cancellationToken);
                return name switch
                {
                    "other" => "included:value @include:nested",
                    "nested" => "obj.name:deep",
                    _ => null
                };
            };
        });

        var first = await parser.BuildQueryAsync("@include:other", cancellationToken: TestContext.Current.CancellationToken);
        var second = await parser.BuildQueryAsync("@include:other", cancellationToken: TestContext.Current.CancellationToken);

        string expected = "{'bool':{'must':[{'term':{'keyword':{'value':'value'}}},{'term':{'obj.name':{'value':'deep'}}}]}}";
        ElasticAssert.Json(expected, first);
        ElasticAssert.Json(expected, second);
        Assert.Equal(["nested", "nested", "other", "other"], requested.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BuildQueryAsync_WithIncludeResolverReturningNull_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>(null));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("@include:missing", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("missing", exception.Result.UnresolvedIncludes);
    }

    [Fact]
    public async Task BuildQueryAsync_WithThrowingIncludeResolver_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (_, _, _) => throw new InvalidOperationException("store offline"));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("@include:any", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("store offline", exception.Message);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNegatedResolvedInclude_ExcludesWholeInclude()
    {
        var parser = TestMapping.CreateScoringParser(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("keyword:a OR keyword:b"));

        var result = await parser.BuildQueryAsync("keyword:c -@include:x", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'must':{'term':{'keyword':{'value':'c'}}},'must_not':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithConcurrentIncludeRequests_KeepsContextsIndependent()
    {
        const int requestCount = 32;
        int arrived = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = async (name, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref arrived) == requestCount)
                ready.SetResult();

            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return $"keyword:{name}";
        });

        var requests = Enumerable.Range(0, requestCount).Select(index => Task.Run(async () =>
        {
            var context = parser.CreateContext(new ElasticsearchQueryOptions { DefaultOperator = BooleanOperator.Or, UseScoring = index % 2 == 0 });
            var query = await parser.BuildQueryAsync($"+@include:value{index} keyword:optional", context, TestContext.Current.CancellationToken);
            return (Index: index, Query: query, Context: context);
        }, TestContext.Current.CancellationToken)).ToArray();
        var results = await Task.WhenAll(requests);

        foreach (var (index, query, context) in results)
        {
            Assert.True(context.ValidationResult.IsValid, context.ValidationResult.Message);
            Assert.Equal($"value{index}", Assert.Single(context.ValidationResult.ReferencedIncludes));
            string json = ElasticAssert.Serialize(query);
            Assert.Contains($"\"value{index}\"", json);
            Assert.DoesNotContain(results.Where(r => r.Index != index), other => json.Contains($"\"value{other.Index}\""));
        }
    }

    [Fact]
    public void BuildQuery_WithIncludeResolver_ThrowsInvalidOperationException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("keyword:x"));

        var exception = Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("@include:x"));

        Assert.Contains("Async", exception.Message);
    }

    [Fact]
    public void BuildQuery_WithIncludeDepthLimit_ThrowsValidationException()
    {
        var chain = Enumerable.Range(0, 5).ToDictionary(i => $"level{i}", i => i == 4 ? "keyword:end" : $"@include:level{i + 1}");
        var parser = TestMapping.CreateParser(c =>
        {
            c.Includes = chain;
            c.ValidationOptions = new QueryValidationOptions { MaxIncludeDepth = 3 };
        });

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("@include:level0"));

        Assert.Contains(exception.Errors, e => e.Code == QueryErrorCode.MaxDepthExceeded);
    }
    [Theory]
    [InlineData("@include:skip", "{'term':{'@include':{'value':'skip'}}}")]
    [InlineData("@include:outer", "{'bool':{'must':[{'term':{'keyword':{'value':'value'}}},{'term':{'@include':{'value':'skip'}}}]}}")]
    public async Task BuildQueryAsync_WithSkippedInclude_DoesNotInvokeResolver(string query, string expected)
    {
        // Arrange
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.Includes = new Dictionary<string, string> { ["outer"] = "keyword:value @include:skip" };
            c.ShouldSkipInclude = (node, _) => Foundatio.Lucene.Visitors.IncludeVisitor.GetIncludeName(node) == "skip";
            c.IncludeResolver = (name, _, _) =>
            {
                requested.Add(name);
                throw new InvalidOperationException("skipped include was resolved");
            };
        });

        // Act
        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(requested);
        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData(QueryType.Sort)]
    [InlineData(QueryType.Aggregation)]
    public async Task BuildQueryAsync_AfterAnotherExpression_SkipsIncludesWithQueryContext(QueryType previousType)
    {
        // Arrange
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.ShouldSkipInclude = (_, context) => context.QueryType == QueryType.Query;
            c.IncludeResolver = (name, _, _) =>
            {
                requested.Add(name);
                throw new InvalidOperationException("skipped include was resolved using the previous expression type");
            };
        });
        var context = parser.CreateContext();
        if (previousType == QueryType.Sort)
            await parser.BuildSortAsync("keyword", context, TestContext.Current.CancellationToken);
        else
            await parser.BuildAggregationsAsync("terms:keyword", context, TestContext.Current.CancellationToken);
        Assert.Equal(previousType, context.QueryType);

        // Act
        var result = await parser.BuildQueryAsync("@include:skip", context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(requested);
        Assert.Equal(QueryType.Query, context.QueryType);
        Assert.Equal(QueryType.Query, context.ValidationResult.QueryType);
        ElasticAssert.Json("{'term':{'@include':{'value':'skip'}}}", result);
    }

}
