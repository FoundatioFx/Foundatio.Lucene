using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests.Parity;

/// <summary>
/// Scenarios ported from Foundatio.Parsers' <c>SqlQuerySyntaxTests</c>.
/// </summary>
public class ParsersSqlQuerySyntaxTests : IDisposable
{
    private readonly ParsersSampleContext _db = ParsersSampleContext.CreateOffline();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("FullName:jo?n", "jo_n")]
    [InlineData("FullName:jo*n", "jo%n")]
    [InlineData("FullName:*ohn", "%ohn")]
    [InlineData("FullName:jo?n*", "jo_n%")]
    public void BuildFilter_WithAdvancedWildcard_TranslatesToSqlLike(string query, string expectedPattern)
    {
        string sql = _db.Employees.Where(query, new EntityFrameworkQueryParser()).ToQueryString();

        Assert.Contains(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"N'{expectedPattern}'", sql, StringComparison.Ordinal);
        Assert.Contains(" ESCAPE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFilter_WithAliasedInclude_TranslatesAdvancedWildcard()
    {
        var parser = new EntityFrameworkQueryParser(c => c
            .UseFieldMap(new FieldMap { ["name"] = "FullName" })
            .UseIncludes(new Dictionary<string, string> { ["find"] = "name:jo?n" })
            .UseValidationOptions(new QueryValidationOptions { AllowedFields = { "name" } }));

        string sql = _db.Employees.Where("@include:find", parser).ToQueryString();

        Assert.Contains(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jo_n", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilter_WithDefaultAndNavigationFields_TranslatesAdvancedWildcard()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("FullName", "Title"));

        string defaultSql = _db.Employees.Where("jo?n", parser).ToQueryString();
        string navigationSql = _db.Employees.Where("CurrentCompany.Name:jo?n", parser).ToQueryString();

        Assert.Equal(2, defaultSql.Split(" LIKE ").Length - 1);
        Assert.Contains("jo_n", defaultSql, StringComparison.Ordinal);
        Assert.Contains(" LIKE ", navigationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jo_n", navigationSql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilter_WithExcludedDefaultFields_TranslatesNegatedWildcard()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("FullName", "Title"));

        string sql = _db.Employees.Where("-jo?n*", parser).ToQueryString();

        Assert.Equal(2, sql.Split(" LIKE ").Length - 1);
        Assert.Contains("NOT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"jo\*n", "jo*n")]
    [InlineData(@"jo\?n", "jo?n")]
    public void BuildFilter_WithEscapedDefaultFieldWildcard_PreservesLiteralCharacter(string query, string expectedTerm)
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("FullName"));

        ParsersSqlQueryParserTests.AssertSameSql(_db.Employees.Where(e => e.FullName.StartsWith(expectedTerm)), _db.Employees.Where(query, parser));
    }

    [Theory]
    [InlineData(@"FullName:jo\*n", "jo*n")]
    [InlineData(@"FullName:jo\?n", "jo?n")]
    [InlineData("FullName:\"jo*n\"", "jo*n")]
    public void BuildFilter_WithEscapedOrQuotedWildcard_KeepsLiteralValue(string query, string expectedValue)
    {
        string sql = _db.Employees.Where(query, new EntityFrameworkQueryParser()).ToQueryString();

        Assert.DoesNotContain(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedValue, sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilter_WithAliasedField_PreservesPrefixSearch()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseFieldMap(new FieldMap { ["name"] = "FullName" }));
        string prefix = "Jo";

        ParsersSqlQueryParserTests.AssertSameSql(_db.Employees.Where(e => e.FullName.StartsWith(prefix)), _db.Employees.Where("name:Jo*", parser));
    }

    [Fact]
    public void BuildFilter_WithSqlLikeCharacters_EscapesLiteralCharacters()
    {
        string sql = _db.Employees.Where("FullName:jo%_?", new EntityFrameworkQueryParser()).ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"jo\%\__", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilter_WithAdvancedWildcardOnFullTextField_FallsBackToLike()
    {
        var parser = new EntityFrameworkQueryParser(c => c.AddFullTextFields("FullName"));

        // Intentional difference: Parsers rejected advanced wildcards on full-text fields; the column can still be
        // matched with LIKE.
        string sql = _db.Employees.Where("FullName:jo?n", parser).ToQueryString();

        Assert.Contains(" LIKE ", sql, StringComparison.Ordinal);
        Assert.True(parser.ValidateQuery<Employee>("FullName:jo?n", new EntityFrameworkQueryOptions { Model = _db.Model }).IsValid);
    }

    [Fact]
    public void Validate_WithUnknownField_ReturnsError()
    {
        var parser = new EntityFrameworkQueryParser();

        var validation = parser.ValidateQuery<Employee>("name:Jo?n", new EntityFrameworkQueryOptions { Model = _db.Model });

        Assert.False(validation.IsValid);
        Assert.Contains("not a queryable field", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("FullName:/jo.*/", "regular expression")]
    [InlineData("FullName:john~1", "fuzzy")]
    [InlineData("FullName:\"John Smith\"~2", "proximity")]
    [InlineData("FullName:john^2", "boost")]
    [InlineData("(FullName:john)^2", "boost")]
    [InlineData("Salary:[1 TO 5]^2", "boost")]
    [InlineData("Salary:1?0", "wildcard")]
    [InlineData("Salary:1*", "wildcard")]
    public void Validate_WithUnsupportedSqlSyntax_ReturnsErrorBeforeGeneration(string query, string kind)
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model));

        var result = parser.ValidateQuery<Employee>(query);
        var error = Assert.Throws<QueryValidationException>(() => parser.BuildFilter<Employee>(query));

        Assert.False(result.IsValid);
        Assert.Contains(kind, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(kind, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
