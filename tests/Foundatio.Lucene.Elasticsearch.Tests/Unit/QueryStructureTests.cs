using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class QueryStructureTests
{
    [Theory]
    [InlineData("", "{'bool':{'filter':{'match_all':{}}}}")]
    [InlineData("   ", "{'bool':{'filter':{'match_all':{}}}}")]
    [InlineData("keyword:a", "{'bool':{'filter':{'term':{'keyword':{'value':'a'}}}}}")]
    [InlineData("keyword:a keyword:b", "{'bool':{'filter':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("keyword:a OR keyword:b", "{'bool':{'filter':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("keyword:a -keyword:b", "{'bool':{'filter':{'bool':{'filter':{'term':{'keyword':{'value':'a'}}},'must_not':{'term':{'keyword':{'value':'b'}}}}}}}")]
    [InlineData("-keyword:b", "{'bool':{'filter':{'bool':{'must_not':{'term':{'keyword':{'value':'b'}}}}}}}")]
    [InlineData("_exists_:keyword", "{'bool':{'filter':{'exists':{'field':'keyword'}}}}")]
    [InlineData("_missing_:keyword", "{'bool':{'filter':{'bool':{'must_not':{'exists':{'field':'keyword'}}}}}}")]
    [InlineData("keyword:a AND (keyword:b OR keyword:c)", "{'bool':{'filter':[{'term':{'keyword':{'value':'a'}}},{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'b'}}},{'term':{'keyword':{'value':'c'}}}]}}]}}")]
    public void BuildQuery_InFilterContext_WrapsQueryInBoolFilter(string query, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("", "{'match_all':{}}")]
    [InlineData("*:*", "{'match_all':{}}")]
    [InlineData("*", "{'match_all':{}}")]
    [InlineData("keyword:a", "{'term':{'keyword':{'value':'a'}}}")]
    [InlineData("text:\"hello world\"", "{'match_phrase':{'text':{'query':'hello world'}}}")]
    [InlineData("number:5", "{'term':{'number':{'value':5}}}")]
    [InlineData("keyword:a keyword:b", "{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("keyword:a AND (keyword:b OR keyword:c)", "{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'b'}}},{'term':{'keyword':{'value':'c'}}}]}}]}}")]
    [InlineData("_exists_:keyword", "{'exists':{'field':'keyword'}}")]
    [InlineData("_missing_:keyword", "{'bool':{'must_not':{'exists':{'field':'keyword'}}}}")]
    [InlineData("keyword:(*)", "{'exists':{'field':'keyword'}}")]
    [InlineData("-keyword:(*)", "{'bool':{'must_not':{'exists':{'field':'keyword'}}}}")]
    [InlineData("keyword:(a OR *)", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'exists':{'field':'keyword'}}]}}")]
    [InlineData("(keyword:a)", "{'term':{'keyword':{'value':'a'}}}")]
    [InlineData("((keyword:a))", "{'term':{'keyword':{'value':'a'}}}")]
    public void BuildQuery_InScoringContext_ReturnsQueryUnwrapped(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("keyword:a keyword:b", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("+keyword:a keyword:b -keyword:c", "{'bool':{'must':{'term':{'keyword':{'value':'a'}}},'must_not':{'term':{'keyword':{'value':'c'}}},'should':{'term':{'keyword':{'value':'b'}}}}}")]
    [InlineData("keyword:a AND keyword:b keyword:c", "{'bool':{'minimum_should_match':1,'should':[{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}},{'term':{'keyword':{'value':'c'}}}]}}")]
    public void BuildQuery_WithOrDefaultOperator_CombinesJuxtaposedClausesWithShould(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultOperator = BooleanOperator.Or);

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithRequiredClauseAndOrDefaultInFilterContext_DropsOptionalClauses()
    {
        var parser = TestMapping.CreateParser(c => c.DefaultOperator = BooleanOperator.Or);

        var result = parser.BuildQuery("+keyword:a keyword:b");

        ElasticAssert.Json("{'bool':{'filter':{'term':{'keyword':{'value':'a'}}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithDefaultConfiguration_UsesAndOperatorAndFilterContext()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildQuery("keyword:a keyword:b");

        Assert.Equal(BooleanOperator.And, parser.Configuration.DefaultOperator);
        Assert.False(parser.Configuration.UseScoring);
        ElasticAssert.Json("{'bool':{'filter':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithSearchMode_UsesScoringAndOrOperator()
    {
        var parser = TestMapping.CreateParser(c => c.UseSearchMode());

        var result = parser.BuildQuery("keyword:a keyword:b keyword:c");

        Assert.True(parser.Configuration.UseScoring);
        Assert.Equal(BooleanOperator.Or, parser.Configuration.DefaultOperator);
        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}},{'term':{'keyword':{'value':'c'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithPerRequestOptions_OverridesScoringAndDefaultOperator()
    {
        var parser = TestMapping.CreateParser();
        var options = new ElasticsearchQueryOptions { UseScoring = true, DefaultOperator = BooleanOperator.Or };

        var result = parser.BuildQuery("keyword:a keyword:b", options);

        ElasticAssert.Json("{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}", result);
    }

    [Theory]
    [InlineData("keyword:a OR NOT keyword:b", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'bool':{'must_not':{'term':{'keyword':{'value':'b'}}}}}]}}")]
    [InlineData("keyword:a OR !keyword:b", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'bool':{'must_not':{'term':{'keyword':{'value':'b'}}}}}]}}")]
    [InlineData("keyword:a OR -keyword:b", "{'bool':{'minimum_should_match':1,'must_not':{'term':{'keyword':{'value':'b'}}},'should':{'term':{'keyword':{'value':'a'}}}}}")]
    [InlineData("NOT keyword:a AND NOT keyword:b", "{'bool':{'must_not':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("keyword:a AND NOT keyword:b", "{'bool':{'must':{'term':{'keyword':{'value':'a'}}},'must_not':{'term':{'keyword':{'value':'b'}}}}}")]
    [InlineData("keyword:a AND -keyword:b", "{'bool':{'must':{'term':{'keyword':{'value':'a'}}},'must_not':{'term':{'keyword':{'value':'b'}}}}}")]
    [InlineData("NOT (keyword:a OR keyword:b)", "{'bool':{'must_not':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("!(keyword:a)", "{'bool':{'must_not':{'term':{'keyword':{'value':'a'}}}}}")]
    [InlineData("-keyword:(a OR b)", "{'bool':{'must_not':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("keyword:a OR (keyword:b AND NOT keyword:c)", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'bool':{'must':{'term':{'keyword':{'value':'b'}}},'must_not':{'term':{'keyword':{'value':'c'}}}}}]}}")]
    public void BuildQuery_WithNegation_FollowsLuceneClauseOccurrence(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("NOT keyword:value1")]
    [InlineData("-keyword:value1")]
    [InlineData("!keyword:value1")]
    [InlineData("NOT keyword:(value1)")]
    [InlineData("-keyword:(value1)")]
    [InlineData("!keyword:(value1)")]
    public void BuildQuery_WithNegatedTerm_ProducesMustNotClause(string query)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json("{'bool':{'filter':{'bool':{'must_not':{'term':{'keyword':{'value':'value1'}}}}}}}", result);
    }

    [Theory]
    [InlineData("NOT +keyword:value1")]
    [InlineData("NOT -keyword:value1")]
    public void BuildQuery_WithContradictoryPrefixes_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains("Unexpected operator", exception.Message);
    }

    [Theory]
    [InlineData("keyword:a OR keyword:b keyword:c", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'bool':{'must':[{'term':{'keyword':{'value':'b'}}},{'term':{'keyword':{'value':'c'}}}]}}]}}")]
    [InlineData("text:alpha OR text:beta AND text:gamma", "{'bool':{'minimum_should_match':1,'should':[{'match':{'text':{'query':'alpha'}}},{'bool':{'must':[{'match':{'text':{'query':'beta'}}},{'match':{'text':{'query':'gamma'}}}]}}]}}")]
    [InlineData("keyword:a AND keyword:b OR keyword:c AND keyword:d", "{'bool':{'minimum_should_match':1,'should':[{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}},{'bool':{'must':[{'term':{'keyword':{'value':'c'}}},{'term':{'keyword':{'value':'d'}}}]}}]}}")]
    public void BuildQuery_WithMixedOperators_BindsAndTighterThanOr(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithMixedOperatorsInFilterContext_KeepsNestedBoolFilters()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildQuery("keyword:a OR keyword:b keyword:c");

        ElasticAssert.Json("{'bool':{'filter':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'bool':{'filter':[{'term':{'keyword':{'value':'b'}}},{'term':{'keyword':{'value':'c'}}}]}}]}}}}", result);
    }

    [Theory]
    [InlineData("keyword:(a OR b)", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("keyword:(a b)", "{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    [InlineData("text:(\"New York\" OR Madison)", "{'bool':{'minimum_should_match':1,'should':[{'match_phrase':{'text':{'query':'New York'}}},{'match':{'text':{'query':'Madison'}}}]}}")]
    [InlineData("number:(>=1 AND <10)", "{'bool':{'must':[{'range':{'number':{'gte':'1'}}},{'range':{'number':{'lt':'10'}}}]}}")]
    [InlineData("number:(>=1 AND <10 AND >0)", "{'bool':{'must':[{'range':{'number':{'gte':'1'}}},{'range':{'number':{'lt':'10'}}},{'range':{'number':{'gt':'0'}}}]}}")]
    [InlineData("number:(>30 AND <=40) AND keyword:value", "{'bool':{'must':[{'bool':{'must':[{'range':{'number':{'gt':'30'}}},{'range':{'number':{'lte':'40'}}}]}},{'term':{'keyword':{'value':'value'}}}]}}")]
    [InlineData("obj:(name:x)", "{'term':{'name':{'value':'x'}}}")]
    public void BuildQuery_WithFieldGroup_AppliesFieldToUnfieldedDescendants(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("(keyword:a OR keyword:b)^3", "{'bool':{'boost':3,'must':{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}}}")]
    [InlineData("text:(alpha beta)^2", "{'bool':{'boost':2,'must':{'bool':{'must':[{'match':{'text':{'query':'alpha'}}},{'match':{'text':{'query':'beta'}}}]}}}}")]
    [InlineData("keyword:a^2 OR keyword:b", "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'boost':2,'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}")]
    public void BuildQuery_WithGroupBoost_BoostsTheWholeGroup(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("(keyword:a OR keyword:b)")]
    [InlineData("+(keyword:a OR keyword:b)")]
    [InlineData("@include:saved")]
    [InlineData("+@include:saved")]
    public void BuildQuery_WithEscapedGroupBoost_MatchesUnescapedBoost(string expression)
    {
        var parser = TestMapping.CreateScoringParser(c => c.Includes = new Dictionary<string, string> { ["saved"] = "keyword:a OR keyword:b" });

        var expected = parser.BuildQuery(expression + "^2");
        var actual = parser.BuildQuery(expression + @"^\+2");

        Assert.Equal(ElasticAssert.Serialize(expected), ElasticAssert.Serialize(actual));
        Assert.Contains("\"boost\":2", ElasticAssert.Serialize(actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildQuery_WithRequiredGroupAndOptionalClause_KeepsGroupIntact(bool scoring)
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.DefaultOperator = BooleanOperator.Or;
            c.UseScoring = scoring;
        });

        var result = parser.BuildQuery("+(keyword:a OR keyword:b) keyword:c");

        string group = "{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'a'}}},{'term':{'keyword':{'value':'b'}}}]}}";
        ElasticAssert.Json(scoring
            ? $"{{'bool':{{'must':{group},'should':{{'term':{{'keyword':{{'value':'c'}}}}}}}}}}"
            : $"{{'bool':{{'filter':{group}}}}}", result);
    }

    [Theory]
    [InlineData(BooleanOperator.And)]
    [InlineData(BooleanOperator.Or)]
    public void BuildQuery_WithUntranslatableRequiredClause_ThrowsValidationException(BooleanOperator defaultOperator)
    {
        var parser = TestMapping.CreateParser(c => c.DefaultOperator = defaultOperator);

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("+[1 TO 5] keyword:a"));

        Assert.Contains("Range queries require a field", exception.Message);
    }

    [Fact]
    public void BuildQuery_WithSpacedBang_TreatsBangAsTerm()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("! keyword:alpha");

        ElasticAssert.Json("{'bool':{'must':[{'multi_match':{'query':'!'}},{'term':{'keyword':{'value':'alpha'}}}]}}", result);
    }

    [Theory]
    [InlineData("keyword:-alpha")]
    [InlineData("keyword:NOT alpha")]
    [InlineData("keyword:+alpha")]
    [InlineData("keyword:!alpha")]
    [InlineData("keyword:-(a OR b)")]
    [InlineData("number:-[1 TO 5]")]
    [InlineData("number:NOT [1 TO 2]")]
    public void BuildQuery_WithPostColonOperator_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains("before the field name", exception.Message);
        Assert.False(parser.ValidateQuery(query).IsValid);
    }

    [Fact]
    public void BuildQuery_WithNullQuery_ThrowsArgumentNullException()
    {
        var parser = TestMapping.CreateParser();

        Assert.Throws<ArgumentNullException>(() => parser.BuildQuery((string)null!));
    }

    [Fact]
    public void TryBuildQuery_WithInvalidQuery_ReturnsFailure()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.TryBuildQuery("number:abc");
        var success = parser.TryBuildQuery("number:5");

        Assert.True(result.IsFailure);
        Assert.IsType<QueryValidationException>(result.Error);
        Assert.True(success.IsSuccess);
        Assert.NotNull(success.Value.Bool);
    }

    [Fact]
    public async Task TryBuildQueryAsync_WithSyntaxError_ReturnsFailure()
    {
        var parser = TestMapping.CreateParser();

        var result = await parser.TryBuildQueryAsync("keyword:(a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.IsType<QueryValidationException>(result.Error);
    }

    [Fact]
    public void BuildQuery_WithoutMapping_TreatsFieldsAsKeywords()
    {
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);

        var result = parser.BuildQuery("title:hello number:5 title:hel*");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'title':{'value':'hello'}}},{'term':{'number':{'value':'5'}}},{'prefix':{'title':{'value':'hel'}}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithMatchAllNode_ReturnsMatchAllQuery()
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery("keyword:a AND *:*");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'match_all':{}}]}}", result);
        Assert.IsType<MatchAllQuery>(parser.BuildQuery("*:*").MatchAll);
    }
}
