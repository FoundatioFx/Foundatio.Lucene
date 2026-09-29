using System.Collections.Concurrent;
using Elastic.Clients.Elasticsearch.Mapping;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class FieldResolutionTests
{
    [Theory]
    [InlineData("KEYWORD:x", "{'term':{'keyword':{'value':'x'}}}")]
    [InlineData("Keyword:x", "{'term':{'keyword':{'value':'x'}}}")]
    [InlineData("obj.NAME:x", "{'term':{'obj.name':{'value':'x'}}}")]
    [InlineData("OBJ.Desc:x", "{'match':{'obj.desc':{'query':'x'}}}")]
    [InlineData("multiword:x", "{'match':{'multiWord':{'query':'x'}}}")]
    [InlineData("_exists_:KEYWORD", "{'exists':{'field':'keyword'}}")]
    [InlineData("NUMBER:[1 TO 2]", "{'range':{'number':{'gte':'1','lte':'2'}}}")]
    [InlineData("unmapped:x", "{'term':{'unmapped':{'value':'x'}}}")]
    public void BuildQuery_WithMappingAndDifferentCasing_UsesCanonicalFieldName(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("alias:x", "{'term':{'alias':{'value':'x'}}}")]
    [InlineData("textAlias:hello", "{'match':{'textAlias':{'query':'hello'}}}")]
    [InlineData("textAlias:\"hello world\"", "{'match_phrase':{'textAlias':{'query':'hello world'}}}")]
    public void BuildQuery_WithMappedFieldAlias_UsesAliasTargetType(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("user:x", "{'term':{'keyword':{'value':'x'}}}")]
    [InlineData("USER:x", "{'term':{'keyword':{'value':'x'}}}")]
    [InlineData("desc:hello", "{'match':{'obj.desc':{'query':'hello'}}}")]
    [InlineData("data.name:x", "{'term':{'obj.name':{'value':'x'}}}")]
    [InlineData("_exists_:user", "{'exists':{'field':'keyword'}}")]
    [InlineData("count:[1 TO 5]", "{'range':{'number':{'gte':'1','lte':'5'}}}")]
    [InlineData("kids:(kids.name:x)", "{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}}")]
    [InlineData("keyword:x", "{'term':{'keyword':{'value':'x'}}}")]
    public void BuildQuery_WithFieldMap_ResolvesAliasesToMappedFields(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.FieldMap = new FieldMap
        {
            { "user", "keyword" },
            { "desc", "obj.desc" },
            { "data", "obj" },
            { "count", "number" },
            { "kids", "children" }
        });

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithFieldMapPerRequest_OverridesConfiguredFieldMap()
    {
        var parser = TestMapping.CreateScoringParser(c => c.FieldMap = new FieldMap { { "user", "keyword" } });

        var result = parser.BuildQuery("user:x", new ElasticsearchQueryOptions { FieldMap = new FieldMap { { "user", "obj.name" } } });

        ElasticAssert.Json("{'term':{'obj.name':{'value':'x'}}}", result);
    }

    [Fact]
    public void BuildQuery_WithFieldResolver_ResolvesBeforeFieldMap()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.FieldResolver = (field, _) => field == "who" ? "keyword" : null;
            c.FieldMap = new FieldMap { { "who", "obj.name" }, { "what", "obj.desc" } };
        });

        var result = parser.BuildQuery("who:x what:y");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'x'}}},{'match':{'obj.desc':{'query':'y'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithThrowingFieldResolver_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.FieldResolver = (_, _) => throw new InvalidOperationException("resolver broke"));

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("keyword:x"));

        Assert.Contains("resolver broke", exception.Message);
    }

    [Fact]
    public async Task BuildQueryAsync_WithAsyncFieldResolver_ResolvesFieldsBeforeBuilding()
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c => c.AsyncFieldResolver = async (field, _, cancellationToken) =>
        {
            requested.Add(field);
            await Task.Delay(10, cancellationToken);
            return field switch
            {
                "who" => "keyword",
                "when" => "date",
                _ => null
            };
        });

        var result = await parser.BuildQueryAsync("who:x when:>2024-01-01 keyword:y", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["keyword", "when", "who"], requested.Order(StringComparer.Ordinal));
        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'x'}}},{'range':{'date':{'gt':'2024-01-01'}}},{'term':{'keyword':{'value':'y'}}}]}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithAsyncFieldResolverReturningNull_FallsBackToSyncResolver()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.AsyncFieldResolver = (field, _, _) => ValueTask.FromResult(field == "a" ? "keyword" : null);
            c.FieldResolver = (field, _) => field == "b" ? "obj.name" : null;
        });

        var result = await parser.BuildQueryAsync("a:x b:y", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'x'}}},{'term':{'obj.name':{'value':'y'}}}]}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithThrowingAsyncFieldResolver_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.AsyncFieldResolver = (_, _, _) => throw new InvalidOperationException("lookup failed"));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("keyword:x", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("lookup failed", exception.Message);
        Assert.Contains(exception.Errors, e => e.Code == QueryErrorCode.UnresolvedField);
    }

    [Fact]
    public void BuildQuery_WithAsyncFieldResolver_ThrowsInvalidOperationException()
    {
        var parser = TestMapping.CreateParser(c => c.AsyncFieldResolver = (field, _, _) => ValueTask.FromResult<string?>(field));

        var exception = Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("keyword:x"));

        Assert.Contains("Async", exception.Message);
    }

    [Theory]
    [InlineData("keyword:value", true)]
    [InlineData("KEYWORD:value", true)]
    [InlineData("alias:value", true)]
    [InlineData("obj.name:value", true)]
    [InlineData("children.grand.name:value", true)]
    [InlineData("text.keyword:value", true)]
    [InlineData("missing:value", false)]
    [InlineData("obj.missing:value", false)]
    [InlineData("_exists_:missing", false)]
    [InlineData("keyword:(a OR b) missing:c", false)]
    public void ValidateQuery_WithUnresolvedFieldsDisallowed_RejectsFieldsMissingFromMapping(string query, bool isValid)
    {
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false });

        var result = parser.ValidateQuery(query);

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
        {
            Assert.NotEmpty(result.UnresolvedFields);
            Assert.Contains(result.ValidationErrors, e => e.Code == QueryErrorCode.UnresolvedField);
        }
    }

    [Fact]
    public void BuildQuery_WithUnresolvedFieldsDisallowed_ReportsOriginalFieldName()
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.FieldMap = new FieldMap { { "known", "keyword" }, { "ghost", "missing_physical" } };
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false };
        });

        var valid = parser.BuildQuery("known:value");
        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("ghost:value"));

        Assert.NotNull(valid.Bool);
        Assert.Contains("resolved", exception.Message);
        Assert.Contains("ghost", exception.Result.ReferencedFields);
        Assert.Contains("ghost", exception.Result.UnresolvedFields);
        Assert.DoesNotContain("missing_physical", exception.Result.UnresolvedFields);
    }

    [Fact]
    public void BuildQuery_WithUnresolvedFieldsAllowed_UsesFieldAsWritten()
    {
        var parser = TestMapping.CreateScoringParser();
        var context = parser.CreateContext();

        var result = parser.BuildQuery("missing:value", context);

        ElasticAssert.Json("{'term':{'missing':{'value':'value'}}}", result);
        Assert.True(context.ValidationResult.IsValid);
        Assert.Contains("missing", context.ValidationResult.UnresolvedFields);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("sort")]
    [InlineData("aggregation")]
    public void Validate_WithUnresolvedFieldsDisallowed_AppliesToEveryExpressionType(string type)
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.FieldMap = new FieldMap { { "alias2", "keyword" }, { "ghost", "missing" } };
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false };
        });

        QueryValidationResult Validate(string field) => type switch
        {
            "sort" => parser.ValidateSort(field),
            "aggregation" => parser.ValidateAggregations($"terms:{field}"),
            _ => parser.ValidateQuery($"{field}:value")
        };

        Assert.True(Validate("alias2").IsValid);
        var invalid = Validate("ghost");
        Assert.False(invalid.IsValid);
        Assert.Contains("ghost", invalid.UnresolvedFields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateQuery_WithMissingDefaultField_RejectsOnlyWhenDefaultFieldsAreUsed(bool mixed)
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.DefaultFields = mixed ? ["keyword", "missing"] : ["missing"];
            c.Includes = new Dictionary<string, string> { ["fragment"] = "value" };
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false };
        });

        var usesDefaults = parser.ValidateQuery("value");
        var usesDefaultsInInclude = parser.ValidateQuery("@include:fragment");
        var explicitFields = parser.ValidateQuery("keyword:value OR keyword:(value) OR number:[1 TO 2] OR _exists_:keyword");

        Assert.False(usesDefaults.IsValid);
        Assert.Equal(["missing"], usesDefaults.UnresolvedFields);
        Assert.False(usesDefaultsInInclude.IsValid);
        Assert.True(explicitFields.IsValid, explicitFields.Message);
        Assert.Throws<QueryValidationException>(() => parser.BuildQuery("value"));
    }

    [Fact]
    public void BuildQuery_WithFieldMapToUnmappedField_UsesTargetAndReportsAliasAsUnresolved()
    {
        var parser = TestMapping.CreateScoringParser(c => c.FieldMap = new FieldMap { { "ghost", "dynamic_field" } });
        var context = parser.CreateContext();

        var result = parser.BuildQuery("ghost:value", context);

        ElasticAssert.Json("{'term':{'dynamic_field':{'value':'value'}}}", result);
        Assert.Contains("ghost", context.ValidationResult.UnresolvedFields);
        Assert.Contains("dynamic_field", context.ValidationResult.ResolvedFields);
    }

    [Fact]
    public void BuildQuery_WithRuntimeFieldOnContext_TreatsFieldAsResolvedAndTyped()
    {
        var parser = TestMapping.CreateScoringParser(c => c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false });
        var context = parser.CreateContext();
        context.AddRuntimeField(new ElasticRuntimeField("computed", RuntimeFieldType.Long, "emit(doc['number'].value * 2)"));

        var result = parser.BuildQuery("computed:10 OR COMPUTED:[1 TO 5]", context);

        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'term':{'computed':{'value':10}}},{'range':{'computed':{'gte':'1','lte':'5'}}}]}}", result);
        Assert.Empty(context.ValidationResult.UnresolvedFields);
    }

    [Fact]
    public async Task BuildQueryAsync_WithRuntimeFieldResolver_CollectsRuntimeFieldsOnContext()
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateScoringParser(c => c.RuntimeFieldResolver = (field, _, _) =>
        {
            requested.Add(field);
            return ValueTask.FromResult(field == "computed" ? new ElasticRuntimeField("computed", RuntimeFieldType.Double, "emit(1.5)") : null);
        });
        var context = parser.CreateContext();

        var result = await parser.BuildQueryAsync("computed:1.5 keyword:x", context, TestContext.Current.CancellationToken);

        var runtimeField = Assert.Single(context.RuntimeFields);
        Assert.Equal("computed", runtimeField.Name);
        Assert.Equal(["computed"], requested);
        ElasticAssert.Json("{'bool':{'must':[{'term':{'computed':{'value':1.5}}},{'term':{'keyword':{'value':'x'}}}]}}", result);
        ElasticAssert.Json("{'script':{'source':'emit(1.5)'},'type':'double'}", runtimeField.ToRuntimeField());
    }

    [Fact]
    public async Task BuildQueryAsync_WithRuntimeFieldResolverAndUnresolvedFieldsDisallowed_AcceptsRuntimeFields()
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.DefaultFields = ["runtime"];
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false };
            c.RuntimeFieldResolver = (field, _, _) => ValueTask.FromResult(field == "runtime" ? new ElasticRuntimeField("runtime") : null);
        });
        var context = parser.CreateContext();

        var result = await parser.BuildQueryAsync("value", context, TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'filter':{'term':{'runtime':{'value':'value'}}}}}", result);
        Assert.Single(context.RuntimeFields);
    }

    [Fact]
    public async Task ValidateQueryAsync_WithRuntimeFieldResolverReturningNull_ReportsUnresolvedFieldOnce()
    {
        int calls = 0;
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false };
            c.RuntimeFieldResolver = (_, _, _) =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult<ElasticRuntimeField?>(null);
            };
        });

        var result = await parser.ValidateQueryAsync("missing:value OR missing:other", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(1, calls);
        Assert.Contains("missing", result.UnresolvedFields);
    }

    [Fact]
    public async Task ValidateQueryAsync_WithThrowingRuntimeFieldResolver_ReportsResolverError()
    {
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (_, _, _) => throw new InvalidOperationException("runtime lookup failed"));

        var result = await parser.ValidateQueryAsync("missing:value", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("runtime lookup failed", result.Message);
    }

    [Fact]
    public async Task BuildQueryAsync_WithRuntimeFieldResolver_DoesNotResolveMappedFields()
    {
        var requested = new ConcurrentBag<string>();
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (field, _, _) =>
        {
            requested.Add(field);
            return ValueTask.FromResult<ElasticRuntimeField?>(null);
        });

        await parser.BuildQueryAsync("keyword:x obj.name:y alias:z", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(requested);
    }

    [Fact]
    public async Task BuildAggregationsAsync_WithRuntimeFieldResolver_UsesRuntimeFieldType()
    {
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (field, _, _) =>
            ValueTask.FromResult(field == "score" ? new ElasticRuntimeField("score", RuntimeFieldType.Long, "emit(1)") : null));
        var context = parser.CreateContext();

        var result = await parser.BuildAggregationsAsync("max:score", context, TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'max_score':{'max':{'field':'score'},'meta':{'@field_type':'long'}}}", result);
        Assert.Single(context.RuntimeFields);
    }

    [Fact]
    public async Task BuildSortAsync_WithRuntimeFieldResolver_UsesRuntimeFieldTypeAsUnmappedType()
    {
        var parser = TestMapping.CreateParser(c => c.RuntimeFieldResolver = (field, _, _) =>
            ValueTask.FromResult(field == "score" ? new ElasticRuntimeField("score", RuntimeFieldType.Double, "emit(1)") : null));

        var result = await parser.BuildSortAsync("-score", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("[{'score':{'order':'desc','unmapped_type':'double'}}]", result);
    }

    [Fact]
    public void BuildQuery_WithMappingResolverPerRequest_UsesThatIndexMapping()
    {
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);
        var otherMapping = new TypeMapping { Properties = new Properties { { "keyword", new TextProperty() } } };

        var withoutMapping = parser.BuildQuery("keyword:x");
        var withMapping = parser.BuildQuery("keyword:x", new ElasticsearchQueryOptions { MappingResolver = ElasticMappingResolver.Create(otherMapping) });

        ElasticAssert.Json("{'term':{'keyword':{'value':'x'}}}", withoutMapping);
        ElasticAssert.Json("{'match':{'keyword':{'query':'x'}}}", withMapping);
    }

    [Fact]
    public void UseMappings_WithResolver_UsesResolver()
    {
        var resolver = ElasticMappingResolver.Create(TestMapping.Create());
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(resolver);
            c.UseScoring = true;
        });

        var result = parser.BuildQuery("text:hello");

        Assert.Same(resolver, parser.Configuration.MappingResolver);
        ElasticAssert.Json("{'match':{'text':{'query':'hello'}}}", result);
    }
}
