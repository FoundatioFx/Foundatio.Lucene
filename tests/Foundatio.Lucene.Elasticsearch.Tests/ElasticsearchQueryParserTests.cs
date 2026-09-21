using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch.Tests;

public class ElasticsearchQueryParserTests
{
    private readonly ElasticsearchQueryParser _parser = new(c => c.UseScoring = true);

    [Fact]
    public async Task CanParseSimpleTerm()
    {
        var query = _parser.BuildQuery("test");

        Assert.NotNull(query);
        Assert.NotNull(query.MultiMatch);
        Assert.Equal("test", query.MultiMatch.Query);
    }

    [Fact]
    public async Task CanParseFieldQuery()
    {
        var query = _parser.BuildQuery("title:hello");

        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("title", query.Match.Field);
        Assert.Equal("hello", query.Match.Query);
    }

    [Fact]
    public async Task CanParseBooleanAndQuery()
    {
        var query = _parser.BuildQuery("foo AND bar");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Must);
        Assert.Equal(2, boolQuery.Must.Count);
    }

    [Fact]
    public async Task CanParseBooleanOrQuery()
    {
        var query = _parser.BuildQuery("foo OR bar");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Should);
        Assert.Equal(2, boolQuery.Should.Count);
    }

    [Fact]
    public async Task CanParseNotQuery()
    {
        var query = _parser.BuildQuery("NOT foo");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.MustNot);
        Assert.Single(boolQuery.MustNot);
    }

    [Fact]
    public async Task CanParsePhraseQuery()
    {
        var query = _parser.BuildQuery("\"hello world\"");

        Assert.NotNull(query);
        Assert.NotNull(query.MultiMatch);
        Assert.Equal("hello world", query.MultiMatch.Query);
        Assert.Equal(TextQueryType.Phrase, query.MultiMatch.Type);
    }

    [Fact]
    public async Task CanParseFieldPhraseQuery()
    {
        var query = _parser.BuildQuery("title:\"hello world\"");

        Assert.NotNull(query);
        Assert.NotNull(query.MatchPhrase);
        Assert.Equal("title", query.MatchPhrase.Field);
        Assert.Equal("hello world", query.MatchPhrase.Query);
    }

    [Fact]
    public async Task CanParsePrefixQuery()
    {
        var query = _parser.BuildQuery("test*");

        Assert.NotNull(query);
        Assert.NotNull(query.QueryString);
        Assert.Equal("test*", query.QueryString.Query);
    }

    [Fact]
    public async Task CanParseFieldPrefixQuery()
    {
        var query = _parser.BuildQuery("title:test*");

        Assert.NotNull(query);
        Assert.NotNull(query.Prefix);
        Assert.Equal("title", query.Prefix.Field);
        Assert.Equal("test", query.Prefix.Value);
    }

    [Fact]
    public async Task CanParseWildcardQuery()
    {
        var query = _parser.BuildQuery("te?t");

        Assert.NotNull(query);
        Assert.NotNull(query.QueryString);
        Assert.Equal("te?t", query.QueryString.Query);
    }

    [Fact]
    public async Task CanParseFieldWildcardQuery()
    {
        var query = _parser.BuildQuery("title:te*st");

        Assert.NotNull(query);
        Assert.NotNull(query.Wildcard);
        Assert.Equal("title", query.Wildcard.Field);
        Assert.Equal("te*st", query.Wildcard.Value);
    }

    [Fact]
    public async Task CanParseFuzzyQuery()
    {
        var query = _parser.BuildQuery("test~");

        Assert.NotNull(query);
        Assert.NotNull(query.QueryString);
        Assert.Equal("test~", query.QueryString.Query);
    }

    [Fact]
    public async Task CanParseFuzzyQueryWithDistance()
    {
        var query = _parser.BuildQuery("test~2");

        Assert.NotNull(query);
        Assert.NotNull(query.QueryString);
        Assert.Equal("test~2", query.QueryString.Query);
    }

    [Fact]
    public async Task CanParseExistsQuery()
    {
        var query = _parser.BuildQuery("_exists_:title");

        Assert.NotNull(query);
        Assert.NotNull(query.Exists);
        Assert.Equal("title", query.Exists.Field);
    }

    [Fact]
    public async Task CanParseMissingQuery()
    {
        var query = _parser.BuildQuery("_missing_:title");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.MustNot);
        Assert.Single(boolQuery.MustNot);
        var mustNotQuery = boolQuery.MustNot.First();
        Assert.NotNull(mustNotQuery.Exists);
        Assert.Equal("title", mustNotQuery.Exists.Field);
    }

    [Fact]
    public async Task CanParseRangeQuery()
    {
        var query = _parser.BuildQuery("price:[100 TO 500]");

        Assert.NotNull(query);
        Assert.NotNull(query.Range);
    }

    [Fact]
    public async Task CanParseOpenRangeQuery()
    {
        var query = _parser.BuildQuery("price:[100 TO *]");

        Assert.NotNull(query);
        Assert.NotNull(query.Range);
    }

    [Fact]
    public async Task CanParseExclusiveRangeQuery()
    {
        var query = _parser.BuildQuery("price:{100 TO 500}");

        Assert.NotNull(query);
        Assert.NotNull(query.Range);
    }

    [Fact]
    public async Task CanParseGroupedQuery()
    {
        var query = _parser.BuildQuery("(foo OR bar) AND baz");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Must);
        Assert.Equal(2, boolQuery.Must.Count);
    }

    [Fact]
    public async Task CanParseNestedBooleanQuery()
    {
        var query = _parser.BuildQuery("a AND b OR c");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
    }

    [Fact]
    public async Task CanParseRequiredTerm()
    {
        var query = _parser.BuildQuery("+required");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Must);
        Assert.Single(boolQuery.Must);
    }

    [Fact]
    public async Task CanParseProhibitedTerm()
    {
        var query = _parser.BuildQuery("-prohibited");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.MustNot);
        Assert.Single(boolQuery.MustNot);
    }

    [Fact]
    public async Task CanParseRegexQuery()
    {
        var query = _parser.BuildQuery("/test.*/");

        Assert.NotNull(query);
        Assert.NotNull(query.QueryString);
        Assert.Equal("/test.*/", query.QueryString.Query);
    }

    [Fact]
    public async Task CanParseFieldRegexQuery()
    {
        var query = _parser.BuildQuery("title:/test.*/");

        Assert.NotNull(query);
        Assert.NotNull(query.Regexp);
        Assert.Equal("title", query.Regexp.Field);
        Assert.Equal("test.*", query.Regexp.Value);
    }

    [Fact]
    public async Task CanParseMatchAllQuery()
    {
        var query = _parser.BuildQuery("*:*");

        Assert.NotNull(query);
        Assert.NotNull(query.MatchAll);
    }

    [Fact]
    public async Task CanParseBoostedTerm()
    {
        var query = _parser.BuildQuery("test^2");

        Assert.NotNull(query);
        Assert.NotNull(query.MultiMatch);
        Assert.Equal("test", query.MultiMatch.Query);
        Assert.Equal(2.0f, query.MultiMatch.Boost);
    }

    [Fact]
    public async Task CanParseBoostedFieldQuery()
    {
        var query = _parser.BuildQuery("title:test^3.5");

        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("title", query.Match.Field);
        Assert.Equal("test", query.Match.Query);
        Assert.Equal(3.5f, query.Match.Boost);
    }

    [Fact]
    public async Task CanParseComplexQuery()
    {
        var query = _parser.BuildQuery("(title:hello AND content:world) OR author:john");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Should);
        Assert.Equal(2, boolQuery.Should.Count);
    }

    [Fact]
    public async Task CanParseProximityPhrase()
    {
        var query = _parser.BuildQuery("\"hello world\"~5");

        Assert.NotNull(query);
        Assert.NotNull(query.MultiMatch);
        Assert.Equal("hello world", query.MultiMatch.Query);
        Assert.Equal(5, query.MultiMatch.Slop);
    }

    [Fact]
    public async Task CanParseEmptyQuery()
    {
        var query = _parser.BuildQuery("");

        Assert.NotNull(query);
        Assert.NotNull(query.MatchAll);
    }

    [Fact]
    public async Task CanParseWhitespaceOnlyQuery()
    {
        var query = _parser.BuildQuery("   ");

        Assert.NotNull(query);
        Assert.NotNull(query.MatchAll);
    }

    [Fact]
    public async Task CanParseMultipleTerms()
    {
        var query = _parser.BuildQuery("foo bar baz");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Should);
        Assert.Equal(3, boolQuery.Should.Count);
    }

    [Fact]
    public async Task CanParseFieldWithNestedPath()
    {
        var query = _parser.BuildQuery("user.name:john");

        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("user.name", query.Match.Field);
        Assert.Equal("john", query.Match.Query);
    }

    [Fact]
    public async Task CanApplyFieldMapping()
    {
        var fieldMap = new FieldMap { { "user", "account.user" } };
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.FieldMap = fieldMap;
            c.UseScoring = true;
        });

        var query = parser.BuildQuery("user:john");

        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("account.user", query.Match.Field);
    }

    [Fact]
    public async Task CanParseTermQuery()
    {
        var query = _parser.BuildQuery("status:active");

        Assert.NotNull(query);
        Assert.NotNull(query.Match);
        Assert.Equal("status", query.Match.Field);
        Assert.Equal("active", query.Match.Query);
    }

    [Fact]
    public async Task CanParseDefaultOperatorAnd()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.DefaultOperator = BooleanOperator.And;
            c.UseScoring = true;
        });
        var query = parser.BuildQuery("foo bar");

        Assert.NotNull(query);
        var boolQuery = query.Bool;
        Assert.NotNull(boolQuery);
        Assert.NotNull(boolQuery.Must);
        Assert.Equal(2, boolQuery.Must.Count);
    }

    [Fact]
    public async Task BuildQueryAsync_ReturnsQuery_WhenValidInput()
    {
        var result = _parser.BuildQuery("test");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task BuildQuery_ReturnsMatchAll_WhenNoNodes()
    {
        var query = _parser.BuildQuery("*:*");

        Assert.NotNull(query);
        Assert.NotNull(query.MatchAll);
    }
}
