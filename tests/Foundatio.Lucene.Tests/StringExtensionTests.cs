using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Tests;

public class StringExtensionTests
{
    #region Unescape Tests

    [Theory]
    [InlineData("", "")]
    [InlineData("none", "none")]
    [InlineData(@"Escaped \. in the code", "Escaped . in the code")]
    [InlineData(@"Escap\e", "Escape")]
    [InlineData(@"Double \\ backslash", @"Double \ backslash")]
    [InlineData(@"At end \", @"At end \")]
    public void Unescape_Works(string test, string expected)
    {
        string? result = test.Unescape();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Unescape_NullInput_ReturnsNull()
    {
        string? input = null;
        Assert.Null(input.Unescape());
    }

    [Theory]
    [InlineData(@"\+", "+")]
    [InlineData(@"\-", "-")]
    [InlineData(@"\!", "!")]
    [InlineData(@"\(", "(")]
    [InlineData(@"\)", ")")]
    [InlineData(@"\{", "{")]
    [InlineData(@"\}", "}")]
    [InlineData(@"\[", "[")]
    [InlineData(@"\]", "]")]
    [InlineData(@"\^", "^")]
    [InlineData(@"\""", "\"")]
    [InlineData(@"\~", "~")]
    [InlineData(@"\*", "*")]
    [InlineData(@"\?", "?")]
    [InlineData(@"\:", ":")]
    [InlineData(@"\/", "/")]
    public void Unescape_SpecialCharacters_Works(string escaped, string expected)
    {
        string? result = escaped.Unescape();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Unescape_EscapedColon_InFieldValue()
    {
        // Example: field:value\:with\:colons
        string input = @"value\:with\:colons";
        string expected = "value:with:colons";
        Assert.Equal(expected, input.Unescape());
    }

    [Fact]
    public void Unescape_EscapedSpaces_Works()
    {
        // Example: field\ name:value
        string input = @"field\ name";
        string expected = "field name";
        Assert.Equal(expected, input.Unescape());
    }

    [Fact]
    public void Unescape_MultipleEscapes_Works()
    {
        string input = @"hello\+world\-test";
        string expected = "hello+world-test";
        Assert.Equal(expected, input.Unescape());
    }

    #endregion

    #region Escape Tests

    [Theory]
    [InlineData("", "")]
    [InlineData("simple", "simple")]
    [InlineData("hello world", "hello world")]
    public void Escape_NoSpecialChars_ReturnsUnchanged(string input, string expected)
    {
        string? result = input.Escape();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Escape_NullInput_ReturnsNull()
    {
        string? input = null;
        Assert.Null(input.Escape());
    }

    [Theory]
    [InlineData("+", @"\+")]
    [InlineData("-", @"\-")]
    [InlineData("!", @"\!")]
    [InlineData("(", @"\(")]
    [InlineData(")", @"\)")]
    [InlineData("{", @"\{")]
    [InlineData("}", @"\}")]
    [InlineData("[", @"\[")]
    [InlineData("]", @"\]")]
    [InlineData("^", @"\^")]
    [InlineData("\"", "\\\"")]
    [InlineData("~", @"\~")]
    [InlineData("*", @"\*")]
    [InlineData("?", @"\?")]
    [InlineData(":", @"\:")]
    [InlineData("\\", @"\\")]
    [InlineData("/", @"\/")]
    public void Escape_SpecialCharacters_Escapes(string input, string expected)
    {
        string? result = input.Escape();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Escape_MixedContent_EscapesOnlySpecial()
    {
        string input = "hello+world";
        string expected = @"hello\+world";
        Assert.Equal(expected, input.Escape());
    }

    [Fact]
    public void Escape_MultipleSpecialChars_EscapesAll()
    {
        string input = "(test)";
        string expected = @"\(test\)";
        Assert.Equal(expected, input.Escape());
    }

    #endregion

    #region Roundtrip Tests

    [Theory]
    [InlineData("hello world")]
    [InlineData("simple")]
    [InlineData("test+value")]
    [InlineData("field:value")]
    [InlineData("(grouped)")]
    [InlineData("[1 TO 10]")]
    public void EscapeUnescape_Roundtrip_Works(string original)
    {
        string? escaped = original.Escape();
        string? unescaped = escaped.Unescape();
        Assert.Equal(original, unescaped);
    }

    #endregion
}
