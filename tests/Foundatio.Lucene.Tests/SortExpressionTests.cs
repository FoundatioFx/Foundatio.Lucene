using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Tests;

public class SortExpressionTests
{
    [Theory]
    [InlineData("created", "created")]
    [InlineData("-created", "-created")]
    [InlineData("+created", "created")]
    [InlineData("-a +b c", "-a b c")]
    [InlineData("a:desc b:asc C:DESC d:Asc", "-a b -C d")]
    [InlineData("-(a b +c)", "-a -b c")]
    [InlineData("+(-a b)", "-a b")]
    [InlineData("-(a (b +c))", "-a -b c")]
    [InlineData("a AND b OR c", "a b c")]
    [InlineData("-a:asc", "a")]
    [InlineData("data.created -user.name", "data.created -user.name")]
    [InlineData("a\\-b", "a-b")]
    [InlineData("\"field with space\"", "field with space")]
    [InlineData("-\"quoted\"", "-quoted")]
    public void Parse_ValidSort_ReturnsFieldsInOrder(string sort, string expected)
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        var fields = SortExpression.Parse(sort, result);

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(expected, string.Join(" ", fields));
        Assert.All(fields, f => Assert.Equal(f.Field, f.OriginalField));
    }

    [Fact]
    public void Parse_ValidSort_RecordsPositionsAndDirections()
    {
        var fields = SortExpression.Parse("-created name:asc", new QueryValidationResult());

        Assert.Equal(2, fields.Count);
        Assert.Equal(SortDirection.Descending, fields[0].Direction);
        Assert.Equal(0, fields[0].Position);
        Assert.Equal(SortDirection.Ascending, fields[1].Direction);
        Assert.Equal(9, fields[1].Position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptySort_ReturnsNoFields(string sort)
    {
        var result = new QueryValidationResult();

        var fields = SortExpression.Parse(sort, result);

        Assert.Empty(fields);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("NOT a")]
    [InlineData("!a")]
    [InlineData("b NOT a")]
    [InlineData("b OR NOT a")]
    [InlineData("-(b !a)")]
    public void Parse_NotOperator_ReturnsOrderingOperatorError(string sort)
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        SortExpression.Parse(sort, result);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.Contains("Boolean operator (NOT|!) is not supported in sort expressions", error.Message);
        Assert.Contains("use + for ascending or - for descending", error.Message);
    }

    [Theory]
    [InlineData("a*")]
    [InlineData("a?b")]
    [InlineData("a~")]
    [InlineData("a^2")]
    [InlineData("a:foo")]
    [InlineData("a:[1 TO 2]")]
    [InlineData("a:(b c)")]
    [InlineData("/re/")]
    [InlineData("_exists_:a")]
    [InlineData("_missing_:a")]
    [InlineData("*")]
    [InlineData("\"a b\"~2")]
    [InlineData("a:desc*")]
    public void Parse_UnsupportedNode_ReturnsError(string sort)
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        var fields = SortExpression.Parse(sort, result);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.StartsWith("Sort expressions only support field names", error.Message);
        Assert.Empty(fields);
    }

    [Fact]
    public void Parse_SyntaxError_AddsParseErrors()
    {
        var result = new QueryValidationResult();

        SortExpression.Parse("(a", result);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_ParserOptions_AreUsed()
    {
        var result = new QueryValidationResult();

        var fields = SortExpression.Parse("a b", result, new LuceneParserOptions { DefaultOperator = BooleanOperator.Or });

        Assert.Equal("a b", string.Join(" ", fields));
    }

    [Fact]
    public void Parse_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SortExpression.Parse(null!, new QueryValidationResult()));
        Assert.Throws<ArgumentNullException>(() => SortExpression.Parse("a", null!));
        Assert.Throws<ArgumentNullException>(() => SortExpression.FromDocument(null!, new QueryValidationResult()));
    }

    [Fact]
    public void SortField_Constructor_ValidatesAndInitializes()
    {
        var field = new SortField("name", SortDirection.Descending);
        field.Data["x"] = 1;

        Assert.Equal("name", field.OriginalField);
        Assert.Equal("-name", field.ToString());
        Assert.Equal(1, field.Data["x"]);
        Assert.Throws<ArgumentNullException>(() => new SortField(null!));
        Assert.Throws<ArgumentException>(() => new SortField(""));
    }
}
