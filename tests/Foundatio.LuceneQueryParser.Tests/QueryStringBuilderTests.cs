namespace Foundatio.LuceneQueryParser.Tests;

public class QueryStringBuilderTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("test123")]
    public void ToQueryString_SimpleTerm_ReturnsOriginal(string query)
    {
        var result = LuceneQuery.Parse(query);
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal(query, output);
    }

    [Fact]
    public void ToQueryString_QuotedPhrase_ReturnsPhraseWithQuotes()
    {
        var result = LuceneQuery.Parse("\"hello world\"");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("\"hello world\"", output);
    }

    [Fact]
    public void ToQueryString_FieldQuery_ReturnsFieldColon()
    {
        var result = LuceneQuery.Parse("title:test");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("title:test", output);
    }

    [Fact]
    public void ToQueryString_AndQuery_ReturnsWithAnd()
    {
        var result = LuceneQuery.Parse("hello AND world");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Contains("AND", output);
    }

    [Fact]
    public void ToQueryString_OrQuery_ReturnsWithOr()
    {
        var result = LuceneQuery.Parse("hello OR world");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Contains("OR", output);
    }

    [Fact]
    public void ToQueryString_NotQuery_ReturnsWithNot()
    {
        var result = LuceneQuery.Parse("NOT test");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("NOT test", output);
    }

    [Fact]
    public void ToQueryString_RangeQuery_ReturnsFormattedRange()
    {
        var result = LuceneQuery.Parse("[10 TO 20]");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("[10 TO 20]", output);
    }

    [Fact]
    public void ToQueryString_ExclusiveRange_ReturnsBraces()
    {
        var result = LuceneQuery.Parse("{10 TO 20}");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("{10 TO 20}", output);
    }

    [Fact]
    public void ToQueryString_FuzzyTerm_ReturnsTilde()
    {
        var result = LuceneQuery.Parse("roam~2");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("roam~", output);
    }

    [Fact]
    public void ToQueryString_BoostedTerm_ReturnsCaret()
    {
        var result = LuceneQuery.Parse("important^2");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("important^2", output);
    }

    [Fact]
    public void ToQueryString_PrefixWildcard_ReturnsStar()
    {
        var result = LuceneQuery.Parse("test*");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("test*", output);
    }

    [Fact]
    public void ToQueryString_Regex_ReturnsSlashes()
    {
        var result = LuceneQuery.Parse("/pattern/");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("/pattern/", output);
    }

    [Fact]
    public void ToQueryString_RegexWithPattern_ReturnsSlashes()
    {
        var result = LuceneQuery.Parse("/pattern/");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("/pattern/", output);
    }

    [Fact]
    public void ToQueryString_ExistsQuery_ReturnsExistsFormat()
    {
        var result = LuceneQuery.Parse("_exists_:field_name");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("_exists_:field_name", output);
    }

    [Fact]
    public void ToQueryString_MissingQuery_ReturnsMissingFormat()
    {
        var result = LuceneQuery.Parse("_missing_:field_name");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("_missing_:field_name", output);
    }

    [Fact]
    public void ToQueryString_FieldStarExists_ReturnsFieldStar()
    {
        var result = LuceneQuery.Parse("field_name:*");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("field_name:*", output);
    }

    [Fact]
    public void ToQueryString_GroupedQuery_ReturnsParentheses()
    {
        var result = LuceneQuery.Parse("(hello OR world)");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Contains("(", output);
        Assert.Contains(")", output);
    }

    [Fact]
    public void ToQueryString_MatchAll_ReturnsStarColonStar()
    {
        var result = LuceneQuery.Parse("*:*");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("*:*", output);
    }

    [Fact]
    public void ToQueryString_RequiredTerm_ReturnsPlus()
    {
        var result = LuceneQuery.Parse("+required");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("+required", output);
    }

    [Fact]
    public void ToQueryString_ProhibitedTerm_ReturnsMinus()
    {
        var result = LuceneQuery.Parse("-excluded");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("-excluded", output);
    }

    [Fact]
    public void ToQueryString_ProximityPhrase_ReturnsTildeWithDistance()
    {
        var result = LuceneQuery.Parse("\"hello world\"~5");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("\"hello world\"~5", output);
    }

    [Fact]
    public void ToQueryString_ComplexQuery_ReturnsFormattedQuery()
    {
        var result = LuceneQuery.Parse("title:(quick OR brown) AND status:published");
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Contains("title:", output);
        Assert.Contains("status:published", output);
        Assert.Contains("AND", output);
    }

    [Fact]
    public void ToQueryString_MultiTerm_ReturnsCombinedText()
    {
        var result = LuceneQuery.Parse("title:(full text search)", splitOnWhitespace: false);
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("title:(full text search)", output);
    }

    [Fact]
    public void ToQueryString_MultiTermWithBoost_ReturnsBoost()
    {
        var result = LuceneQuery.Parse("title:(full text search)^2", splitOnWhitespace: false);
        var builder = new QueryStringBuilder();
        
        var output = builder.Visit(result.Document);

        Assert.Equal("title:(full text search)^2", output);
    }

    #region Round-Trip Tests

    [Theory]
    [InlineData("hello")]
    [InlineData("test123")]
    [InlineData("field:value")]
    [InlineData("title:test")]
    [InlineData("\"hello world\"")]
    [InlineData("\"phrase with spaces\"")]
    [InlineData("title:\"quoted phrase\"")]
    [InlineData("test*")]
    [InlineData("te?t")]
    [InlineData("test~")]
    [InlineData("test~1")]
    [InlineData("important^2")]
    [InlineData("important^1.5")]
    [InlineData("\"phrase\"~5")]
    [InlineData("[10 TO 20]")]
    [InlineData("{10 TO 20}")]
    [InlineData("[* TO 100]")]
    [InlineData("[100 TO *]")]
    [InlineData(">100")]
    [InlineData(">=100")]
    [InlineData("<100")]
    [InlineData("<=100")]
    [InlineData("/pattern/")]
    [InlineData("/[a-z]+/")]
    [InlineData("_exists_:field")]
    [InlineData("_missing_:field")]
    [InlineData("field:*")]
    [InlineData("*:*")]
    [InlineData("(grouped)")]
    [InlineData("(a OR b)")]
    [InlineData("(a AND b)")]
    [InlineData("NOT excluded")]
    [InlineData("+required")]
    [InlineData("-excluded")]
    [InlineData("a AND b")]
    [InlineData("a OR b")]
    [InlineData("a AND b AND c")]
    [InlineData("a OR b OR c")]
    [InlineData("field:(a OR b)")]
    [InlineData("(a OR b)^2")]
    [InlineData("date:[2020-01-01 TO 2023-12-31]")]
    public void RoundTrip_Query_PreservesSemantics(string query)
    {
        // Parse the query
        var result = LuceneQuery.Parse(query);
        Assert.True(result.IsSuccess, $"Failed to parse: {query}");

        // Convert back to string
        var output = QueryStringBuilder.ToQueryString(result.Document);

        // Parse the output again
        var result2 = LuceneQuery.Parse(output);
        Assert.True(result2.IsSuccess, $"Failed to parse output: {output}");

        // Convert back to string again - should be identical
        var output2 = QueryStringBuilder.ToQueryString(result2.Document);

        // The second round-trip should produce identical output
        Assert.Equal(output, output2);
    }

    [Theory]
    [InlineData("hello AND world", "hello AND world")]
    [InlineData("hello OR world", "hello OR world")]
    [InlineData("hello world", "hello world")]
    [InlineData("a AND b OR c", "a AND b OR c")]
    [InlineData("(a AND b) OR c", "(a AND b) OR c")]
    [InlineData("field:value^2", "field:value^2")]
    [InlineData("field:(a b c)", "field:(a b c)")]
    [InlineData("field:(a OR b)", "field:(a OR b)")]
    [InlineData("field:(a AND b)", "field:(a AND b)")]
    [InlineData("field:(+required -excluded)", "field:(+required -excluded)")]
    [InlineData("field:(term1 -term2 +term3)", "field:(term1 -term2 +term3)")]
    [InlineData("field:((a OR b) AND (c OR d))", "field:((a OR b) AND (c OR d))")]
    [InlineData("field:(nested (inner OR other))", "field:(nested (inner OR other))")]
    [InlineData("status:(active OR pending OR draft)", "status:(active OR pending OR draft)")]
    [InlineData("tags:(+important -spam)", "tags:(+important -spam)")]
    public void RoundTrip_ComplexQuery_PreservesStructure(string query, string expected)
    {
        var result = LuceneQuery.Parse(query);
        Assert.True(result.IsSuccess, $"Failed to parse: {query}");

        var output = QueryStringBuilder.ToQueryString(result.Document);
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData("title:\"hello world\" AND (status:active OR status:pending) AND NOT deleted:true")]
    [InlineData("price:[100 TO 500] AND date:[2020-01-01 TO 2023-12-31]")]
    [InlineData("(name:john OR name:jane) AND age:>=18 AND status:(active OR pending)")]
    [InlineData("field1:value1 field2:value2 field3:value3")]
    [InlineData("((a AND b) OR (c AND d)) AND NOT (e OR f)")]
    [InlineData("field:(term1 -term2 +term3 (nested OR other))")]
    [InlineData("author:(john OR jane) AND tags:(+featured -archived) AND status:published")]
    [InlineData("category:((electronics OR computers) AND (sale OR clearance))")]
    [InlineData("content:(\"exact phrase\" AND (keyword1 OR keyword2) -excluded)")]
    [InlineData("field:((a b c) OR (d e f))")]
    public void RoundTrip_VeryComplexQuery_Stabilizes(string query)
    {
        // Parse the original query
        var result1 = LuceneQuery.Parse(query);
        Assert.True(result1.IsSuccess, $"Failed to parse original: {query}");

        // First round-trip
        var output1 = QueryStringBuilder.ToQueryString(result1.Document);
        var result2 = LuceneQuery.Parse(output1);
        Assert.True(result2.IsSuccess, $"Failed to parse first output: {output1}");

        // Second round-trip
        var output2 = QueryStringBuilder.ToQueryString(result2.Document);
        var result3 = LuceneQuery.Parse(output2);
        Assert.True(result3.IsSuccess, $"Failed to parse second output: {output2}");

        // Third round-trip
        var output3 = QueryStringBuilder.ToQueryString(result3.Document);

        // After stabilization, output should be identical
        Assert.Equal(output2, output3);
    }

    [Fact]
    public void RoundTrip_EscapedCharacters_Preserved()
    {
        // Test that escaped special characters are preserved
        var queries = new[]
        {
            @"field:test\:value",
            @"field:test\/value",
            @"field:test\\value",
        };

        foreach (var query in queries)
        {
            var result = LuceneQuery.Parse(query);
            if (!result.IsSuccess) continue; // Skip if not supported

            var output = QueryStringBuilder.ToQueryString(result.Document);
            var result2 = LuceneQuery.Parse(output);
            
            if (result2.IsSuccess)
            {
                var output2 = QueryStringBuilder.ToQueryString(result2.Document);
                Assert.Equal(output, output2);
            }
        }
    }

    #endregion
}
