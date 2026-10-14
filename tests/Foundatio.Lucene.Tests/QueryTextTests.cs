using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Tests;

public class QueryTextTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("hello\\ world", "hello world")]
    [InlineData("a\\:b", "a:b")]
    [InlineData("\\+\\-\\!\\(\\)\\{\\}\\[\\]\\^\\\"\\~\\*\\?\\:\\\\\\/", "+-!(){}[]^\"~*?:\\/")]
    [InlineData("a\\\\b", "a\\b")]
    [InlineData("trailing\\", "trailing\\")]
    [InlineData("\\", "\\")]
    [InlineData("\\a\\b", "ab")]
    [InlineData("", "")]
    public void Unescape_Text_RemovesEscapes(string input, string expected)
    {
        Assert.Equal(expected, QueryText.Unescape(input));
        Assert.Equal(expected, input.Unescape());
    }

    [Fact]
    public void Unescape_NoEscapes_ReturnsSameInstance()
    {
        string text = "no escapes here";

        Assert.Same(text, QueryText.Unescape(text));
    }

    [Fact]
    public void Unescape_LongText_Works()
    {
        string text = string.Concat(Enumerable.Repeat("a\\:", 500));

        Assert.Equal(string.Concat(Enumerable.Repeat("a:", 500)), QueryText.Unescape(text));
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("hello world", "hello\\ world")]
    [InlineData("a:b", "a\\:b")]
    [InlineData("a*b?", "a\\*b\\?")]
    [InlineData("+-!(){}[]^\"~*?:\\/", "\\+\\-\\!\\(\\)\\{\\}\\[\\]\\^\\\"\\~\\*\\?\\:\\\\\\/")]
    [InlineData("a&&b||c", "a\\&\\&b\\|\\|c")]
    [InlineData("tab\there", "tab\\\there")]
    [InlineData("", "")]
    public void Escape_Text_EscapesSpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, QueryText.Escape(input));
        Assert.Equal(expected, input.Escape());
    }

    [Fact]
    public void Escape_NoSpecialCharacters_ReturnsSameInstance()
    {
        string text = "plain";

        Assert.Same(text, QueryText.Escape(text));
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with space")]
    [InlineData("a:b")]
    [InlineData("-leading")]
    [InlineData("+plus")]
    [InlineData("!bang")]
    [InlineData("*star")]
    [InlineData("q?")]
    [InlineData("(paren)")]
    [InlineData("[bracket]")]
    [InlineData("{brace}")]
    [InlineData("quote\"inside")]
    [InlineData("back\\slash")]
    [InlineData("trailing\\")]
    [InlineData("/regex/")]
    [InlineData("a&&b")]
    [InlineData("a||b")]
    [InlineData("^caret~tilde")]
    [InlineData("2024-01-01T10:30:00Z")]
    [InlineData("ünïcödé")]
    public void Escape_ThenParseAsFieldValue_RoundTripsLiteralValue(string value)
    {
        // Act
        var result = LuceneQuery.Parse($"field:{QueryText.Escape(value)}");

        // Assert
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
        var field = Assert.IsType<FieldQueryNode>(result.Document.Query);
        var term = Assert.IsType<TermNode>(field.Query);
        Assert.False(term.IsPrefix);
        Assert.False(term.IsWildcard);
        Assert.Null(term.ProximityText);
        Assert.Null(term.BoostText);
        Assert.Equal(value, term.UnescapedTerm);
    }

    [Theory]
    [InlineData("simple phrase")]
    [InlineData("with \"quotes\"")]
    [InlineData("back\\slash")]
    [InlineData("trailing\\")]
    [InlineData("special +-!():*? chars")]
    public void EscapePhrase_ThenParseAsPhrase_RoundTripsLiteralValue(string value)
    {
        // Act
        var result = LuceneQuery.Parse($"field:\"{QueryText.EscapePhrase(value)}\"");

        // Assert
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
        var phrase = Assert.IsType<PhraseNode>(((FieldQueryNode)result.Document.Query!).Query);
        Assert.Equal(value, phrase.Phrase);
    }

    [Fact]
    public void EscapePhrase_OnlyEscapesQuotesAndBackslashes()
    {
        Assert.Equal("a \\\"b\\\" c\\\\ d:e*", QueryText.EscapePhrase("a \"b\" c\\ d:e*"));
    }

    [Fact]
    public void Escape_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => QueryText.Escape(null!));
        Assert.Throws<ArgumentNullException>(() => QueryText.EscapePhrase(null!));
        Assert.Throws<ArgumentNullException>(() => QueryText.Unescape(null!));
    }

    [Fact]
    public void StringExtensions_NullInput_ReturnsNull()
    {
        Assert.Null(((string?)null).Escape());
        Assert.Null(((string?)null).Unescape());
    }
}
