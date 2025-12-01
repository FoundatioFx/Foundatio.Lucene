namespace Foundatio.LuceneQuery.Tests;

public class LexerTests
{
    [Theory]
    [InlineData("test", TokenType.Term, "test")]
    [InlineData("hello123", TokenType.Term, "hello123")]
    [InlineData("_underscore", TokenType.Term, "_underscore")]
    [InlineData("123", TokenType.Term, "123")]
    [InlineData("3.14", TokenType.Term, "3.14")]
    public void Tokenize_SingleToken_ReturnsExpectedType(string input, TokenType expectedType, string expectedValue)
    {
        var lexer = new LuceneLexer(input);
        var token = lexer.NextToken();

        Assert.Equal(expectedType, token.Type);
        Assert.Equal(expectedValue, token.GetString());
    }

    [Theory]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"quoted \\\"text\\\"\"", "quoted \"text\"")]
    [InlineData("\"with\\nnewline\"", "withnnewline")]
    public void Tokenize_QuotedString_ReturnsPhrase(string input, string expectedValue)
    {
        var lexer = new LuceneLexer(input);
        var token = lexer.NextToken();

        Assert.Equal(TokenType.QuotedString, token.Type);
        Assert.Equal(expectedValue, token.GetString());
    }

    [Theory]
    [InlineData("AND", TokenType.And)]
    [InlineData("OR", TokenType.Or)]
    [InlineData("NOT", TokenType.Not)]
    [InlineData("TO", TokenType.To)]
    public void Tokenize_Keywords_ReturnsExpectedType(string input, TokenType expectedType)
    {
        var lexer = new LuceneLexer(input);
        var token = lexer.NextToken();

        Assert.Equal(expectedType, token.Type);
    }

    [Theory]
    [InlineData("test*", TokenType.Prefix, "test*")]
    [InlineData("te*st", TokenType.Wildcard, "te*st")]
    [InlineData("te?st", TokenType.Wildcard, "te?st")]
    public void Tokenize_Wildcards_ReturnsExpectedType(string input, TokenType expectedType, string expectedValue)
    {
        var lexer = new LuceneLexer(input);
        var token = lexer.NextToken();

        Assert.Equal(expectedType, token.Type);
        Assert.Equal(expectedValue, token.GetString());
    }

    [Theory]
    [InlineData("/pattern/", "pattern")]
    [InlineData("/test\\.regex/", "test\\.regex")]
    public void Tokenize_Regex_ReturnsExpectedPattern(string input, string expectedPattern)
    {
        var lexer = new LuceneLexer(input);
        var token = lexer.NextToken();

        Assert.Equal(TokenType.Regex, token.Type);
        Assert.Equal(expectedPattern, token.GetString());
    }

    [Fact]
    public void Tokenize_FieldQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("title:test");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("title", tokens[0].GetString());
        Assert.Equal(TokenType.Colon, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("test", tokens[2].GetString());
    }

    [Fact]
    public void Tokenize_NestedField_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("obj.nested.field:value");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("obj.nested.field", tokens[0].GetString());
        Assert.Equal(TokenType.Colon, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("value", tokens[2].GetString());
    }

    [Fact]
    public void Tokenize_BooleanOperators_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("+required -excluded");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(5, tokens.Count); // +, required, whitespace, -, excluded
        Assert.Equal(TokenType.Plus, tokens[0].Type);
        Assert.Equal(TokenType.Term, tokens[1].Type);
        Assert.Equal("required", tokens[1].GetString());
        Assert.Equal(TokenType.Whitespace, tokens[2].Type);
        Assert.Equal(TokenType.Minus, tokens[3].Type);
        Assert.Equal(TokenType.Term, tokens[4].Type);
        Assert.Equal("excluded", tokens[4].GetString());
    }

    [Fact]
    public void Tokenize_RangeQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("[10 TO 20]");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(7, tokens.Count); // [, 10, ws, TO, ws, 20, ]
        Assert.Equal(TokenType.LeftBracket, tokens[0].Type);
        Assert.Equal(TokenType.Term, tokens[1].Type);
        Assert.Equal("10", tokens[1].GetString());
        Assert.Equal(TokenType.Whitespace, tokens[2].Type);
        Assert.Equal(TokenType.To, tokens[3].Type);
        Assert.Equal(TokenType.Whitespace, tokens[4].Type);
        Assert.Equal(TokenType.Term, tokens[5].Type);
        Assert.Equal("20", tokens[5].GetString());
        Assert.Equal(TokenType.RightBracket, tokens[6].Type);
    }

    [Fact]
    public void Tokenize_FuzzyQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("roam~2");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("roam", tokens[0].GetString());
        Assert.Equal(TokenType.Tilde, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("2", tokens[2].GetString());
    }

    [Fact]
    public void Tokenize_BoostQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("test^2.5");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("test", tokens[0].GetString());
        Assert.Equal(TokenType.Caret, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("2.5", tokens[2].GetString());
    }

    [Fact]
    public void Tokenize_ProximityQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("\"hello world\"~5");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.QuotedString, tokens[0].Type);
        Assert.Equal("hello world", tokens[0].GetString());
        Assert.Equal(TokenType.Tilde, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("5", tokens[2].GetString());
    }

    [Fact]
    public void Tokenize_ComplexQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("title:(quick OR brown) AND status:published");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.True(tokens.Count > 8);
        Assert.Contains(tokens, t => t.Type == TokenType.LeftParen);
        Assert.Contains(tokens, t => t.Type == TokenType.RightParen);
        Assert.Contains(tokens, t => t.Type == TokenType.Or);
        Assert.Contains(tokens, t => t.Type == TokenType.And);
    }

    [Fact]
    public void Tokenize_ExistsQuery_ReturnsCorrectTokens()
    {
        var lexer = new LuceneLexer("_exists_:field");
        var tokens = new List<Token>();

        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("_exists_", tokens[0].GetString());
    }

    [Fact]
    public void Tokenize_TracksPositionCorrectly()
    {
        var lexer = new LuceneLexer("a b\nc");
        
        var token1 = lexer.NextToken();
        Assert.Equal(1, token1.Line);
        Assert.Equal(1, token1.Column);
        Assert.Equal(0, token1.Position);

        var whitespace1 = lexer.NextToken(); // whitespace between a and b
        Assert.Equal(TokenType.Whitespace, whitespace1.Type);

        var token2 = lexer.NextToken();
        Assert.Equal(1, token2.Line);
        Assert.Equal(3, token2.Column);
        Assert.Equal(2, token2.Position);

        var whitespace2 = lexer.NextToken(); // newline
        Assert.Equal(TokenType.Whitespace, whitespace2.Type);

        var token3 = lexer.NextToken();
        Assert.Equal(2, token3.Line);
        Assert.Equal(1, token3.Column);
        Assert.Equal(4, token3.Position);
    }

    [Theory]
    [InlineData("2024-01-01", "2024-01-01")]
    [InlineData("2024-01-01T12:30:00", "2024-01-01T12:30:00")]
    [InlineData("2024-01-01T12:30:00.123Z", "2024-01-01T12:30:00.123Z")]
    [InlineData("2024-01-01T12:30:00+00:00", "2024-01-01T12:30:00+00:00")]
    [InlineData("now-1d", "now-1d")]
    [InlineData("now+1h", "now+1h")]
    [InlineData("now/d", "now/d")]
    public void Tokenize_DateTimeValues_ReturnsSingleTerm(string input, string expected)
    {
        var lexer = new LuceneLexer(input);
        var tokens = lexer.Tokenize();

        // Should be just one term + EOF
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal(expected, tokens[0].GetString());
        Assert.Equal(TokenType.EndOfFile, tokens[1].Type);
    }

    [Fact]
    public void Tokenize_DateTimeInFieldQuery_ReturnsSingleTerm()
    {
        var lexer = new LuceneLexer("timestamp:2024-01-02T02:02:01.34343Z");
        var tokens = lexer.Tokenize();

        // field : value EOF
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("timestamp", tokens[0].GetString());
        Assert.Equal(TokenType.Colon, tokens[1].Type);
        Assert.Equal(TokenType.Term, tokens[2].Type);
        Assert.Equal("2024-01-02T02:02:01.34343Z", tokens[2].GetString());
        Assert.Equal(TokenType.EndOfFile, tokens[3].Type);
    }

    [Fact]
    public void Tokenize_MinusAsOperator_StillWorks()
    {
        var lexer = new LuceneLexer("-excluded");
        var tokens = lexer.Tokenize();

        // - excluded EOF
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Minus, tokens[0].Type);
        Assert.Equal(TokenType.Term, tokens[1].Type);
        Assert.Equal("excluded", tokens[1].GetString());
    }

    [Fact]
    public void Tokenize_MinusAfterWhitespace_IsOperator()
    {
        var lexer = new LuceneLexer("hello -world");
        var tokens = lexer.Tokenize();

        // hello WS - world EOF
        Assert.Equal(5, tokens.Count);
        Assert.Equal(TokenType.Term, tokens[0].Type);
        Assert.Equal("hello", tokens[0].GetString());
        Assert.Equal(TokenType.Whitespace, tokens[1].Type);
        Assert.Equal(TokenType.Minus, tokens[2].Type);
        Assert.Equal(TokenType.Term, tokens[3].Type);
        Assert.Equal("world", tokens[3].GetString());
    }
}
