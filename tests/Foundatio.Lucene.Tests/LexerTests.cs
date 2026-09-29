namespace Foundatio.Lucene.Tests;

public class LexerTests
{
    [Theory]
    [InlineData("hello", "Term(hello)")]
    [InlineData("a AND b", "Term(a) And(AND) Term(b)")]
    [InlineData("a && b || c", "Term(a) And(&&) Term(b) Or(||) Term(c)")]
    [InlineData("a&&b", "Term(a&&b)")]
    [InlineData("NOT a", "Not(NOT) Term(a)")]
    [InlineData("!a -b +c", "Not(!) Term(a) Minus(-) Term(b) Plus(+) Term(c)")]
    [InlineData("! a", "Term(!) Term(a)")]
    [InlineData("a-b", "Term(a-b)")]
    [InlineData("title:hello", "Term(title) Colon(:) Term(hello)")]
    [InlineData("time:12:30:00", "Term(time) Colon(:) Term(12:30:00)")]
    [InlineData("field1:2", "Term(field1) Colon(:) Term(2)")]
    [InlineData("\"a b\"", "QuotedString(a b)")]
    [InlineData("/a.b/", "Regex(a.b)")]
    [InlineData("[1 TO 5]", "LeftBracket([) Term(1) To(TO) Term(5) RightBracket(])")]
    [InlineData("{-5..5}", "LeftBrace({) Term(-5) RangeDots(..) Term(5) RightBrace(})")]
    [InlineData("go TO x", "Term(go) Term(TO) Term(x)")]
    [InlineData(">=5 <-5", "GreaterThanOrEqual(>=) Term(5) LessThan(<) Term(-5)")]
    [InlineData("foo~2^3", "Term(foo) Tilde(~) ModifierValue(2) Caret(^) ModifierValue(3)")]
    [InlineData("foo^-5h", "Term(foo) Caret(^) ModifierValue(-5h)")]
    [InlineData("foo^\"America/Chicago\"", "Term(foo) Caret(^) ModifierValue(America/Chicago)")]
    [InlineData("foo\\:bar", "Term(foo\\:bar)")]
    [InlineData("(a)", "LeftParen(() Term(a) RightParen())")]
    public void Tokenize_ProducesExpectedTokens(string query, string expected)
    {
        var tokens = LuceneQuery.Tokenize(query);

        Assert.Equal(TokenType.EndOfFile, tokens[^1].Type);
        Assert.Equal(expected, string.Join(" ", tokens.Take(tokens.Count - 1).Select(t => $"{t.Type}({t.GetString()})")));
    }

    [Fact]
    public void Tokenize_TracksPositions()
    {
        var tokens = LuceneQuery.Tokenize("a\n  \"b c\"");

        Assert.Equal(0, tokens[0].Position);
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(1, tokens[0].Column);
        Assert.Equal(4, tokens[1].Position);
        Assert.Equal(5, tokens[1].Length);
        Assert.Equal(2, tokens[1].Line);
        Assert.Equal(3, tokens[1].Column);
        Assert.True(tokens[1].HasLeadingWhitespace);
    }

    [Fact]
    public void Tokenize_FlagsWildcardsAndEscapes()
    {
        var tokens = LuceneQuery.Tokenize("f*o b\\*r");

        Assert.True(tokens[0].HasWildcard);
        Assert.False(tokens[0].HasEscapes);
        Assert.False(tokens[1].HasWildcard);
        Assert.True(tokens[1].HasEscapes);
    }

    [Fact]
    public void Tokenize_ValuesAreSlicesOfTheSource()
    {
        const string query = "title:hello";
        var tokens = LuceneQuery.Tokenize(query);

        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetString(tokens[2].Value, out string? text, out int start, out _));
        Assert.Same(query, text);
        Assert.Equal(6, start);
    }
}
