using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Translation to SQL Server without a server: <c>ToQueryString()</c> shows the SQL and parameter values.
/// </summary>
public partial class SqlTranslationTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateOfflineSqlServer();
    private readonly EntityFrameworkQueryParser _parser = new();

    private readonly EntityFrameworkQueryParser _fullTextParser = new(c => c
        .AddFullTextFields("Employee.Name", "Title", "Company.Name")
        .SetDefaultFields("Name"));

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("_exists_:manager.age", true)]
    [InlineData("_missing_:manager.age", false)]
    public void BuildFilter_WithNonNullableFieldUnderOptionalReference_TranslatesNavigationExistence(string query, bool exists)
    {
        // Arrange
        var expected = exists
            ? _db.Employees.Where(e => e.Manager != null)
            : _db.Employees.Where(e => e.Manager == null);

        // Act
        string sql = _db.Employees.Where(query, _parser).ToSql();

        // Assert
        Assert.Equal(expected.ToSql(), sql);
    }

    [Theory]
    [InlineData("_exists_:detail.number", true)]
    [InlineData("_missing_:detail.number", false)]
    [InlineData("detail.number:[* TO *]", true)]
    public void BuildFilter_WithRequiredComplexProperty_PreservesSqlTranslation(string query, bool exists)
    {
        // Arrange
        using var db = new RequiredComplexContext(new DbContextOptionsBuilder<RequiredComplexContext>()
            .UseSqlServer("Server=localhost;Database=offline;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        var expected = exists ? db.Records.Where(_ => true) : db.Records.Where(_ => false);

        // Act
        string sql = db.Records.Where(query, _parser).ToSql();

        // Assert
        Assert.Equal(expected.ToSql(), sql);
    }

    private sealed class RequiredComplexContext(DbContextOptions<RequiredComplexContext> options) : DbContext(options)
    {
        public DbSet<ComplexRecord> Records => Set<ComplexRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ComplexRecord>().ComplexProperty(e => e.Detail).IsRequired();
        }
    }

    private sealed class ComplexRecord
    {
        public int Id { get; set; }
        public ComplexDetail Detail { get; set; } = new();
    }

    private sealed class ComplexDetail
    {
        public int Number { get; set; }
    }

    [Fact]
    public void BuildFilter_WithValues_UsesSqlParametersInsteadOfLiterals()
    {
        string sql = _db.Employees.Where("name:\"John Doe\" AND age:[25 TO 40] AND hiredate:>2020-01-01 AND company.name:Acme* AND status:Active", _parser).ToSql();
        string where = Where(sql);

        Assert.Equal(6, Regex.Matches(sql, "^DECLARE @", RegexOptions.Multiline).Count);
        Assert.DoesNotContain("John", where);
        Assert.DoesNotContain("25", where);
        Assert.DoesNotContain("2020", where);
        Assert.DoesNotContain("Acme", where);
        Assert.Contains("DECLARE @Value nvarchar(100) = N'John Doe';", sql);
    }

    [Fact]
    public void BuildFilter_WithDifferentValues_ProducesTheSameSqlShape()
    {
        string first = _db.Employees.Where("name:John* AND age:>30 AND hiredate:[2020-01-01 TO now]", _parser).ToSql();
        string second = _db.Employees.Where("name:Bob* AND age:>45 AND hiredate:[2019-06-01 TO now-1d]", _parser).ToSql();

        Assert.Equal(WithoutDeclarations(first), WithoutDeclarations(second));
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(@"name:jo\[?n", @"jo\[_n")]
    [InlineData("name:a%b?", @"a\%b_")]
    [InlineData("name:a_b?", @"a\_b_")]
    [InlineData(@"name:a\\b?", @"a\\b_")]
    [InlineData(@"name:jo\*?", "jo*_")]
    [InlineData(@"name:jo\?*n", "jo?%n")]
    [InlineData("name:*ohn", "%ohn")]
    [InlineData("name:j*n", "j%n")]
    [InlineData("name:joh?", "joh_")]
    [InlineData("name:jo?n*", "jo_n%")]
    public void BuildFilter_WithWildcards_EscapesLikePattern(string query, string pattern)
    {
        string sql = _db.Employees.Where(query, _parser).ToSql();

        Assert.Contains($"= N'{pattern}';", sql);
        Assert.Contains(@"LIKE @Value ESCAPE N'\'", sql);
    }

    [Theory]
    [InlineData("name:Jo*", "LIKE @Value_startswith", "Jo%")]
    [InlineData("name:100%*", "LIKE @Value_startswith", @"100\%%")]
    [InlineData("name:*oh*", "LIKE @Value_contains", "%oh%")]
    [InlineData("name:*_*", "LIKE @Value_contains", @"%\_%")]
    [InlineData(@"name:jo\*n", "= @Value", "jo*n")]
    [InlineData("name:\"jo*n\"", "= @Value", "jo*n")]
    public void BuildFilter_WithPrefixContainsOrLiteral_UsesStartsWithContainsOrEquality(string query, string where, string value)
    {
        string sql = _db.Employees.Where(query, _parser).ToSql();

        Assert.Contains(where, sql);
        Assert.Contains($"= N'{value}';", sql);
    }

    [Theory]
    [InlineData("name:John", "\"John\"")]
    [InlineData("name:Jo*", "\"Jo*\"")]
    [InlineData("title:\"Software Developer\"", "\"Software Developer\"")]
    [InlineData("name:\"x\\\" OR NEAR(y\"", "\"x OR NEAR(y\"")]
    [InlineData("name:\"say \\\"hi\\\"\"", "\"say hi\"")]
    [InlineData("name:\"  lots   of\tspace \"", "\"lots of space\"")]
    [InlineData(@"name:jo\*n*", "\"jo n*\"")]
    [InlineData("john", "\"john*\"")]
    [InlineData("\"john smith\"", "\"john smith*\"")]
    public void BuildFilter_WithFullTextField_UsesContainsWithQuotedSearchCondition(string query, string condition)
    {
        string sql = _db.Employees.Where(query, _fullTextParser).ToSql();

        Assert.Contains("CONTAINS([e].[", sql);
        Assert.Contains($"= N'{condition.Replace("'", "''")}';", sql);
    }

    [Fact]
    public void BuildFilter_WithFullTextFieldOnNavigation_UsesContainsOnRelatedColumn()
    {
        string sql = _db.Employees.Where("company.name:Acme", _fullTextParser).ToSql();

        Assert.Contains("CONTAINS([c].[Name], @Value)", sql);
    }

    [Theory]
    [InlineData("name:*oh*", "LIKE @Value_contains")]
    [InlineData("name:j?hn", "LIKE @Value ESCAPE")]
    [InlineData("email:John", "[e].[Email] = @Value")]
    public void BuildFilter_WithFullTextFieldAndInfixWildcard_FallsBackToLike(string query, string expected)
    {
        string sql = _db.Employees.Where(query, _fullTextParser).ToSql();

        Assert.Contains(expected, sql);
        Assert.DoesNotContain("CONTAINS(", sql);
    }

    [Fact]
    public void BuildFilter_WithBlankFullTextPhrase_ThrowsValidationError()
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("name:\"  \"", _fullTextParser));
        var quotes = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("name:\"\\\"\\\"\"", _fullTextParser));

        Assert.Contains("need text to search for", ex.Message);
        Assert.Contains("need text to search for", quotes.Message);
    }

    [Fact]
    public void BuildSort_WithFields_TranslatesToOrderBy()
    {
        string sql = _db.Employees.OrderBy("-salary company.name +name", _parser).ToSql();

        Assert.Contains("ORDER BY [e].[Salary] DESC, [c].[Name], [e].[Name]", sql);
    }

    [Fact]
    public void BuildFilter_WithEnumStoredAsString_ComparesStrings()
    {
        string sql = _db.Employees.Where("level:Senior", _parser).ToSql();

        Assert.Contains("DECLARE @Value nvarchar(20) = N'Senior';", sql);
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("level:>Junior", _parser));
    }

    [Fact]
    public void BuildFilter_WithPrimitiveCollection_TranslatesToJsonQuery()
    {
        string sql = _db.Employees.Where("skills:c* OR skills:go", _parser).ToSql();

        Assert.Contains("OPENJSON([e].[Skills])", sql);
    }

    [Fact]
    public void BuildFilter_WithDateBounds_ComparesAgainstStartOfNextPeriod()
    {
        string sql = _db.TypeSamples.Where("datetime:[2024-01-01 TO 2024-01-31]", _parser).ToSql();

        Assert.Contains("'2024-01-01T00:00:00.0000000Z'", sql);
        Assert.Contains("'2024-02-01T00:00:00.0000000Z'", sql);
        Assert.Matches(@"\[t\]\.\[DateTime\] >= @\w+ AND \[t\]\.\[DateTime\] < @\w+", sql);
    }

    private static string Where(string sql) => sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

    private static string WithoutDeclarations(string sql) => DeclarationRegex().Replace(sql, "");

    [GeneratedRegex("^DECLARE .*$", RegexOptions.Multiline)]
    private static partial Regex DeclarationRegex();
}
