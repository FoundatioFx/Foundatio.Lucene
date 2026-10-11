using System.Collections.Concurrent;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class NestedQueryTests
{
    [Theory]
    [InlineData("children.num:5", "{'nested':{'path':'children','query':{'term':{'children.num':{'value':5}}}}}")]
    [InlineData("children.num:[10 TO 20]", "{'nested':{'path':'children','query':{'range':{'children.num':{'gte':'10','lte':'20'}}}}}")]
    [InlineData("children.num:>=5", "{'nested':{'path':'children','query':{'range':{'children.num':{'gte':'5'}}}}}")]
    [InlineData("children.name:ab*", "{'nested':{'path':'children','query':{'prefix':{'children.name':{'value':'ab'}}}}}")]
    [InlineData("children.text:ab*", "{'nested':{'path':'children','query':{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'fields':['children.text'],'query':'ab*'}}}}")]
    [InlineData("_exists_:children.name", "{'nested':{'path':'children','query':{'exists':{'field':'children.name'}}}}")]
    [InlineData("_missing_:children.name", "{'bool':{'must_not':{'nested':{'path':'children','query':{'exists':{'field':'children.name'}}}}}}")]
    [InlineData("_exists_:children", "{'nested':{'path':'children','query':{'exists':{'field':'children'}}}}")]
    [InlineData("_missing_:children", "{'bool':{'must_not':{'nested':{'path':'children','query':{'exists':{'field':'children'}}}}}}")]
    [InlineData("children.grand.name:x", "{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'x'}}}}}")]
    [InlineData("NOT children.name:x", "{'bool':{'must_not':{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}}}}")]
    [InlineData("CHILDREN.Name:x", "{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}}")]
    public void BuildQuery_WithNestedField_WrapsQueryInNestedQuery(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("children.name:x children.num:5", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("children.name:x AND children.num:5", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("(children.name:x) AND (children.num:5)", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("children.name:x OR children.num:5", "{'nested':{'path':'children','query':{'bool':{'minimum_should_match':1,'should':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("children.name:x^2 children.num:5", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'boost':2,'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("keyword:match_parent children.name:match_child", "{'bool':{'must':[{'term':{'keyword':{'value':'match_parent'}}},{'nested':{'path':'children','query':{'term':{'children.name':{'value':'match_child'}}}}}]}}")]
    [InlineData("children.name:x OR keyword:y", "{'bool':{'minimum_should_match':1,'should':[{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}},{'term':{'keyword':{'value':'y'}}}]}}")]
    public void BuildQuery_WithSiblingClausesOnSamePath_CombinesThemInOneNestedQuery(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithSamePathClausesInFilterContext_UsesFilterInsideNestedQuery()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildQuery("children.name:x children.num:5");

        ElasticAssert.Json("{'bool':{'filter':{'nested':{'path':'children','query':{'bool':{'filter':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}}}", result);
    }

    [Theory]
    [InlineData("-children.name:x -children.num:5", "{'bool':{'must_not':[{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}},{'nested':{'path':'children','query':{'term':{'children.num':{'value':5}}}}}]}}")]
    [InlineData("children.name:x AND NOT children.num:5", "{'bool':{'must':{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}},'must_not':{'nested':{'path':'children','query':{'term':{'children.num':{'value':5}}}}}}}")]
    [InlineData("-children.name:x keyword:a", "{'bool':{'must':{'term':{'keyword':{'value':'a'}}},'must_not':{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}}}}")]
    public void BuildQuery_WithExcludedNestedClause_ExcludesAtDocumentLevel(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("children:(children.name:x)", "{'nested':{'path':'children','query':{'term':{'children.name':{'value':'x'}}}}}")]
    [InlineData("children:(children.name:x children.num:5)", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}")]
    [InlineData("children:(children.name:x children.num:5)^2", "{'nested':{'path':'children','query':{'bool':{'boost':2,'must':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}}}}}")]
    [InlineData("children:(children.name:x OR children.grand.name:y)", "{'nested':{'path':'children','query':{'bool':{'minimum_should_match':1,'should':[{'term':{'children.name':{'value':'x'}}},{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'y'}}}}}]}}}}")]
    [InlineData("NOT children:(children.name:excluded)", "{'bool':{'must_not':{'nested':{'path':'children','query':{'term':{'children.name':{'value':'excluded'}}}}}}}")]
    [InlineData("children:(-children:(children.name:excluded) OR children.num:10)", "{'nested':{'path':'children','query':{'bool':{'minimum_should_match':1,'must_not':{'nested':{'path':'children','query':{'term':{'children.name':{'value':'excluded'}}}}},'should':{'term':{'children.num':{'value':10}}}}}}}")]
    [InlineData("keyword:value1 children:(children.name:value1)", "{'bool':{'must':[{'term':{'keyword':{'value':'value1'}}},{'nested':{'path':'children','query':{'term':{'children.name':{'value':'value1'}}}}}]}}")]
    public void BuildQuery_WithNestedGroup_BuildsOneNestedQueryForTheGroup(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithNestedGroupAndCorrelatedExclusion_ExcludesWithinTheSameNestedDocument()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("children:(+children.grand.name:a -children.toys.name:b)");

        ElasticAssert.Json("{'nested':{'path':'children','query':{'bool':{'must':{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'a'}}}}},'must_not':{'nested':{'path':'children.toys','query':{'term':{'children.toys.name':{'value':'b'}}}}}}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithNestedGroupAlias_ResolvesGroupFieldThroughFieldMap()
    {
        var parser = TestMapping.CreateScoringParser(c => c.FieldMap = new FieldMap { { "blah", "children" } });

        var result = parser.BuildQuery("keyword:value1 blah:(blah.name:value1 blah.num:4)");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'value1'}}},{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'value1'}}},{'term':{'children.num':{'value':4}}}]}}}}]}}", result);
    }

    [Theory]
    [InlineData("children.name:x AND children.grand.name:y", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'y'}}}}}]}}}}")]
    [InlineData("children.grand.name:a AND children.toys.name:b", "{'nested':{'path':'children','query':{'bool':{'must':[{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'a'}}}}},{'nested':{'path':'children.toys','query':{'term':{'children.toys.name':{'value':'b'}}}}}]}}}}")]
    [InlineData("children.grand.name:a OR children.toys.name:b", "{'nested':{'path':'children','query':{'bool':{'minimum_should_match':1,'should':[{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'a'}}}}},{'nested':{'path':'children.toys','query':{'term':{'children.toys.name':{'value':'b'}}}}}]}}}}")]
    [InlineData("children.name:x children.grand.name:a children.toys.name:b", "{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'a'}}}}},{'nested':{'path':'children.toys','query':{'term':{'children.toys.name':{'value':'b'}}}}}]}}}}")]
    [InlineData("children.name:x children.grand.name:y resellers.name:z", "{'bool':{'must':[{'nested':{'path':'children','query':{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'y'}}}}}]}}}},{'nested':{'path':'resellers','query':{'term':{'resellers.name':{'value':'z'}}}}}]}}")]
    [InlineData("+children.grand.name:a -children.toys.name:b", "{'bool':{'must':{'nested':{'path':'children.grand','query':{'term':{'children.grand.name':{'value':'a'}}}}},'must_not':{'nested':{'path':'children.toys','query':{'term':{'children.toys.name':{'value':'b'}}}}}}}")]
    public void BuildQuery_WithMultiLevelNestedFields_CorrelatesUnderTheSharedAncestor(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithNestedDisabled_QueriesNestedFieldsFlat()
    {
        var parser = TestMapping.CreateScoringParser(c => c.UseNested = false);

        var result = parser.BuildQuery("children.name:x children.num:5");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'children.name':{'value':'x'}}},{'term':{'children.num':{'value':5}}}]}}", result);
    }

    [Theory]
    [InlineData("children.name", "{'nested':{'path':'children','query':{'term':{'children.name':{'value':'hello'}}}}}")]
    [InlineData("children.name;children.text", "{'bool':{'should':{'nested':{'path':'children','query':{'bool':{'should':[{'term':{'children.name':{'value':'hello'}}},{'match':{'children.text':{'query':'hello'}}}]}}}}}}")]
    [InlineData("text;children.text", "{'bool':{'should':[{'match':{'text':{'query':'hello'}}},{'nested':{'path':'children','query':{'match':{'children.text':{'query':'hello'}}}}}]}}")]
    [InlineData("children.name;keyword;text", "{'bool':{'should':[{'nested':{'path':'children','query':{'term':{'children.name':{'value':'hello'}}}}},{'match':{'text':{'query':'hello'}}},{'term':{'keyword':{'value':'hello'}}}]}}")]
    [InlineData("children.name;resellers.name", "{'bool':{'should':[{'nested':{'path':'children','query':{'term':{'children.name':{'value':'hello'}}}}},{'nested':{'path':'resellers','query':{'term':{'resellers.name':{'value':'hello'}}}}}]}}")]
    public void BuildQuery_WithNestedDefaultFields_SearchesNestedFields(string fields, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = fields.Split(';'));

        var result = parser.BuildQuery("hello");

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithNumericValueOnMixedNestedDefaultFields_UsesTypedValues()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text", "children.text", "children.num"]);

        var result = parser.BuildQuery("42");

        ElasticAssert.Json("{'bool':{'should':[{'match':{'text':{'query':'42'}}},{'nested':{'path':'children','query':{'bool':{'should':[{'match':{'children.text':{'query':'42'}}},{'term':{'children.num':{'value':42}}}]}}}}]}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterResolver_AddsFilterToNestedQuery()
    {
        var parser = CreateFilteredParser();

        var result = await parser.BuildQueryAsync("resellers.price:10", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'nested':{'path':'resellers','query':{'bool':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'must':{'term':{'resellers.price':{'value':10}}}}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterAndSamePathClauses_KeepsClausesCorrelated()
    {
        var parser = CreateFilteredParser();

        var result = await parser.BuildQueryAsync("resellers.name:Official AND resellers.price:10", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'nested':{'path':'resellers','query':{'bool':{'must':[{'bool':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'must':{'term':{'resellers.name':{'value':'Official'}}}}},{'bool':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'must':{'term':{'resellers.price':{'value':10}}}}}]}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterAndNestedGroup_AddsFilterToGroupQuery()
    {
        var parser = CreateFilteredParser();

        var result = await parser.BuildQueryAsync("resellers:(resellers.name:Official resellers.price:10)", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'nested':{'path':'resellers','query':{'bool':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'must':{'bool':{'must':[{'bool':{'must':{'term':{'resellers.name':{'value':'Official'}}},'filter':{'term':{'resellers.name':{'value':'Official'}}}}},{'bool':{'must':{'term':{'resellers.price':{'value':10}}},'filter':{'term':{'resellers.name':{'value':'Official'}}}}}]}}}}}}", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildQueryAsync_WithLeafFilterAndNestedGroup_PreservesUngroupedPredicate(bool scoring)
    {
        // Arrange
        var parser = TestMapping.CreateParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.ResolvedField == "children.name" ? (Query)new TermQuery("children.num", 1) : null));
        var options = new ElasticsearchQueryOptions { UseScoring = scoring };
        const string nested = "{'nested':{'path':'children','query':{'bool':{'must':{'term':{'children.name':{'boost':2,'value':'x'}}},'filter':{'term':{'children.num':{'value':1}}}}}}}";
        string expected = scoring ? nested : "{'bool':{'filter':" + nested + "}}";

        // Act
        var ungrouped = await parser.BuildQueryAsync("children.name:x^2", options, TestContext.Current.CancellationToken);
        var grouped = await parser.BuildQueryAsync("children:(children.name:x^2)", options, TestContext.Current.CancellationToken);

        // Assert
        ElasticAssert.Json(expected, ungrouped);
        ElasticAssert.Json(expected, grouped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildQueryAsync_WithDistinctLeafAndContainerFilters_PreservesBothPredicates(bool scoring)
    {
        // Arrange
        var parser = TestMapping.CreateParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.ResolvedField switch
            {
                "children" => (Query)new TermQuery("children.text", "container"),
                "children.name" => new TermQuery("children.num", 1),
                _ => null
            }));
        const string nested = "{'nested':{'path':'children','query':{'bool':{'must':{'bool':{'must':{'term':{'children.name':{'value':'x'}}},'filter':{'term':{'children.num':{'value':1}}}}},'filter':{'term':{'children.text':{'value':'container'}}}}}}}";
        string expected = scoring ? nested : "{'bool':{'filter':" + nested + "}}";

        // Act
        var result = await parser.BuildQueryAsync("children:(children.name:x)", new ElasticsearchQueryOptions { UseScoring = scoring }, TestContext.Current.CancellationToken);

        // Assert
        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("AND", false)]
    [InlineData("AND", true)]
    [InlineData("OR", false)]
    [InlineData("OR", true)]
    public async Task BuildQueryAsync_WithGroupedSiblingLeafFilters_PreservesEachPredicateAndCorrelation(string operation, bool scoring)
    {
        // Arrange
        var parser = TestMapping.CreateParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.ResolvedField switch
            {
                "children.name" => (Query)new TermQuery("children.num", 1),
                "children.num" => new TermQuery("children.name", "allowed"),
                _ => null
            }));
        const string clauses = "[{'bool':{'must':{'term':{'children.name':{'value':'x'}}},'filter':{'term':{'children.num':{'value':1}}}}},{'bool':{'must':{'term':{'children.num':{'value':5}}},'filter':{'term':{'children.name':{'value':'allowed'}}}}}]";
        string occur = operation == "OR" ? "'minimum_should_match':1,'should'" : scoring ? "'must'" : "'filter'";
        string nested = "{'nested':{'path':'children','query':{'bool':{" + occur + ":" + clauses + "}}}}";
        string expected = scoring ? nested : "{'bool':{'filter':" + nested + "}}";
        var options = new ElasticsearchQueryOptions { UseScoring = scoring };

        // Act
        var grouped = await parser.BuildQueryAsync($"children:(children.name:x {operation} children.num:5)", options, TestContext.Current.CancellationToken);
        var ungrouped = await parser.BuildQueryAsync($"children.name:x {operation} children.num:5", options, TestContext.Current.CancellationToken);

        // Assert
        ElasticAssert.Json(expected, grouped);
        ElasticAssert.Json(expected, ungrouped);
    }

    [Theory]
    [InlineData("children:(children.grand.name:x)")]
    [InlineData("children:(children.grand:(children.grand.name:x))")]
    public async Task BuildQueryAsync_WithDeeperGroupedLeafFilter_PreservesBothNestedPaths(string query)
    {
        // Arrange
        var parser = TestMapping.CreateScoringParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.ResolvedField == "children.grand.name" ? (Query)new TermQuery("children.grand.name", "allowed") : null));

        // Act
        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        ElasticAssert.Json("{'nested':{'path':'children','query':{'nested':{'path':'children.grand','query':{'bool':{'must':{'term':{'children.grand.name':{'value':'x'}}},'filter':{'term':{'children.grand.name':{'value':'allowed'}}}}}}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedDisabledAndLeafFilter_PreservesFlatQuery()
    {
        // Arrange
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.UseNested = false;
            c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(new TermQuery("children.num", 1));
        });

        // Act
        var result = await parser.BuildQueryAsync("children:(children.name:x)", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        ElasticAssert.Json("{'term':{'children.name':{'value':'x'}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterForSeveralPaths_AppliesFilterPerPath()
    {
        var parser = TestMapping.CreateScoringParser(c => c.NestedFilterResolver = (filter, _, _) => ValueTask.FromResult<Query?>(filter.NestedPath switch
        {
            "resellers" => (Query)new TermQuery("resellers.name", "Official"),
            "children" => new TermQuery("children.name", "sale"),
            _ => null
        }));

        var result = await parser.BuildQueryAsync("resellers.price:10 AND children.num:1", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'must':[{'nested':{'path':'resellers','query':{'bool':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'must':{'term':{'resellers.price':{'value':10}}}}}}},{'nested':{'path':'children','query':{'bool':{'filter':{'term':{'children.name':{'value':'sale'}}},'must':{'term':{'children.num':{'value':1}}}}}}}]}}", result);
    }

    [Theory]
    [InlineData("resellers.price:10")]
    [InlineData("resellers:(resellers.price:10)")]
    public async Task BuildQueryAsync_WithNestedFilterResolverReturningNull_ProducesUnfilteredNestedQuery(string query)
    {
        var parser = TestMapping.CreateScoringParser(c => c.NestedFilterResolver = (_, _, _) => ValueTask.FromResult<Query?>(null));

        var result = await parser.BuildQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'nested':{'path':'resellers','query':{'term':{'resellers.price':{'value':10}}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterForDefaultFields_FiltersEachField()
    {
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.DefaultFields = ["children.name", "children.text"];
            c.NestedFilterResolver = (filter, _, _) => ValueTask.FromResult<Query?>(filter.ResolvedField switch
            {
                "children.name" => (Query)new TermQuery("children.num", 1),
                "children.text" => new TermQuery("children.num", 2),
                _ => null
            });
        });

        var result = await parser.BuildQueryAsync("active", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("{'bool':{'should':{'nested':{'path':'children','query':{'bool':{'should':[{'bool':{'filter':{'term':{'children.num':{'value':1}}},'must':{'term':{'children.name':{'value':'active'}}}}},{'bool':{'filter':{'term':{'children.num':{'value':2}}},'must':{'match':{'children.text':{'query':'active'}}}}}]}}}}}}", result);
    }

    [Fact]
    public async Task BuildQueryAsync_WithNestedFilterResolver_ReceivesOriginalAndResolvedFields()
    {
        var requests = new ConcurrentBag<NestedFilterContext>();
        var parser = TestMapping.CreateScoringParser(c =>
        {
            c.FieldMap = new FieldMap { { "price", "resellers.price" } };
            c.NestedFilterResolver = (filter, _, _) =>
            {
                requests.Add(filter);
                return ValueTask.FromResult<Query?>(null);
            };
        });

        await parser.BuildQueryAsync("price:10 children.grand.name:x keyword:a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(new NestedFilterContext("resellers", "price", "resellers.price"), requests);
        Assert.Contains(new NestedFilterContext("children.grand", "children.grand.name", "children.grand.name"), requests);
        Assert.DoesNotContain(requests, r => r.ResolvedField == "keyword");
    }

    [Fact]
    public async Task BuildQueryAsync_WithFailingNestedFilterResolver_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.NestedFilterResolver = (_, _, _) => throw new InvalidOperationException("tenant lookup failed"));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("resellers.price:10", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("tenant lookup failed", exception.Message);
    }

    [Fact]
    public void BuildQuery_WithNestedFilterResolver_ThrowsBecauseResolverIsAsynchronous()
    {
        var parser = CreateFilteredParser();

        Assert.Throws<InvalidOperationException>(() => parser.BuildQuery("resellers.price:10"));
    }

    private static ElasticsearchQueryParser CreateFilteredParser()
    {
        return TestMapping.CreateScoringParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(filter.NestedPath == "resellers" ? (Query)new TermQuery("resellers.name", "Official") : null));
    }

    [Fact]
    public void BuildQuery_WithNestedGroupInOrDefaultMode_UsesShouldInsideNestedQuery()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultOperator = BooleanOperator.Or);

        var result = parser.BuildQuery("children:(children.name:a children.name:b)");

        ElasticAssert.Json("{'nested':{'path':'children','query':{'bool':{'minimum_should_match':1,'should':[{'term':{'children.name':{'value':'a'}}},{'term':{'children.name':{'value':'b'}}}]}}}}", result);
    }
}
