using Microsoft.EntityFrameworkCore;

#pragma warning disable xUnit1051 // CancellationToken warning not needed for tests

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Tests for EntityFrameworkQueryParser that run against actual SQL Server to verify:
/// 1. Generated SQL matches between normal LINQ and Lucene-generated queries
/// 2. Actual query execution produces correct results
/// </summary>
[Trait("TestType", "Integration")]
[Collection(SqlServerCollection.Name)]
public class SqlServerQueryParserTests
{
    private readonly SqlServerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SqlServerQueryParserTests(SqlServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region SQL Generation Comparison Tests

    [Fact]
    public void SimpleTerm_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Name.Contains("John")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Name:*John*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FieldEquality_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Age == 30).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Age:30");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void RangeQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query - inclusive range
        string sqlExpected = db.Employees.Where(e => e.Age >= 25 && e.Age <= 40).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Age:[25 TO 40]");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void ExclusiveRangeQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query - exclusive range
        string sqlExpected = db.Employees.Where(e => e.Age > 25 && e.Age < 40).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Age:{25 TO 40}");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void GreaterThan_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Salary > 80000).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Salary:>80000");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void LessThanOrEqual_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Salary <= 90000).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Salary:<=90000");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void BooleanField_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.IsActive == true).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("IsActive:true");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void AndQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.IsActive == true && e.Age > 30).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("IsActive:true AND Age:>30");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void OrQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Age == 30 || e.Age == 35).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Age:30 OR Age:35");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void NotQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => !(e.IsActive == false)).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("NOT IsActive:false");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void ExistsQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Title != null).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("_exists_:Title");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void MissingQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Title == null).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("_missing_:Title");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void NavigationProperty_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query - navigation property
        string sqlExpected = db.Employees.Where(e => e.Company.Name.Contains("Acme")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Company.Name:*Acme*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void CollectionNavigation_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query - collection navigation with Any
        string sqlExpected = db.Companies.Where(c => c.Employees.Any(e => e.Salary > 100000)).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Company>("Employees.Salary:>100000");
        string sqlActual = db.Companies.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public async Task DateGreaterThan_GeneratesValidSQL()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Lucene query
        var filter = parser.BuildFilter<Employee>("HireDate:>2020-01-01");
        string sql = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Generated SQL:\n{sql}");

        // Verify SQL contains expected parts
        Assert.Contains("[HireDate]", sql);
        Assert.Contains(">", sql);
        Assert.Contains("2020-01-01", sql);

        // Verify results match expected behavior
        var results = await db.Employees.Where(filter).ToListAsync();
        var targetDate = new DateTime(2020, 1, 1);
        Assert.All(results, e => Assert.True(e.HireDate > targetDate));
    }

    [Fact]
    public async Task DateRange_GeneratesValidSQL()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Lucene query
        var filter = parser.BuildFilter<Employee>("HireDate:[2019-01-01 TO 2020-12-31]");
        string sql = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Generated SQL:\n{sql}");

        // Verify SQL contains expected parts
        Assert.Contains("[HireDate]", sql);
        Assert.Contains(">=", sql);
        Assert.Contains("<=", sql);
        Assert.Contains("2019-01-01", sql);
        Assert.Contains("2020-12-31", sql);

        // Verify results match expected behavior
        var results = await db.Employees.Where(filter).ToListAsync();
        var startDate = new DateTime(2019, 1, 1);
        var endDate = new DateTime(2020, 12, 31, 23, 59, 59, 999).AddTicks(9999);
        Assert.All(results, e => Assert.True(e.HireDate >= startDate && e.HireDate <= endDate));
    }

    [Fact]
    public void StartsWithWildcard_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Name != null && e.Name.StartsWith("John")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Name:John*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void EndsWithWildcard_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Name != null && e.Name.EndsWith("Doe")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Name:*Doe");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void PhraseQuery_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query
        string sqlExpected = db.Employees.Where(e => e.Title != null && e.Title.Contains("Software Developer")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Title:\"Software Developer\"");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void ComplexBooleanExpression_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Direct LINQ query: Active employees with (salary > 80000 OR age < 30)
        string sqlExpected = db.Employees.Where(e =>
            e.IsActive == true && (e.Salary > 80000 || e.Age < 30)).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("IsActive:true AND (Salary:>80000 OR Age:<30)");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    #endregion

    #region Query Execution Tests

    [Fact]
    public async Task SimpleTerm_ReturnsCorrectResults()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        var filter = parser.BuildFilter<Employee>("Name:*John*");
        var results = await db.Employees.Where(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public async Task RangeQuery_ReturnsCorrectResults()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        var filter = parser.BuildFilter<Employee>("Age:[30 TO 40]");
        var results = await db.Employees.Where(filter).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.All(results, e => Assert.InRange(e.Age, 30, 40));
    }

    [Fact]
    public async Task NavigationProperty_ReturnsCorrectResults()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        var filter = parser.BuildFilter<Employee>("Company.Name:*Acme*");
        var results = await db.Employees.Where(filter).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.All(results, e => Assert.Equal(1, e.CompanyId));
    }

    [Fact]
    public async Task CollectionNavigation_ReturnsCorrectResults()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        var filter = parser.BuildFilter<Company>("Employees.Salary:>100000");
        var results = await db.Companies.Where(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Tech Solutions", results[0].Name);
    }

    [Fact]
    public async Task ComplexQuery_ReturnsCorrectResults()
    {
        using var db = _fixture.CreateSampleContext();
        var parser = _fixture.Parser;

        // Active employees with salary > 90000
        var filter = parser.BuildFilter<Employee>("IsActive:true AND Salary:>90000");
        var sql = db.Employees.Where(filter).ToQueryString();
        _output.WriteLine($"Generated SQL:\n{sql}");

        // Verify SQL contains expected parts
        Assert.Contains("WHERE", sql);
        Assert.Contains("IsActive", sql);
        Assert.Contains("Salary", sql);

        var results = await db.Employees.Where(filter).ToListAsync();

        // Should return Jane Smith (salary 95000) and Bob Wilson (salary 105000) - both active with salary > 90000
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Jane Smith");
        Assert.Contains(results, e => e.Name == "Bob Wilson");
    }

    #endregion

    #region Full-Text Search Tests

    [Fact]
    public void FullTextSearch_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query using EF.Functions.Contains
        string sqlExpected = db.Employees.Where(e => EF.Functions.Contains(e.Name, "\"John\"")).ToQueryString();

        // Lucene query - should use EF.Functions.Contains for full-text indexed field
        var filter = parser.BuildFilter<Employee>("Name:John");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FullTextSearch_WithPrefix_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query using EF.Functions.Contains with prefix pattern
        string sqlExpected = db.Employees.Where(e => EF.Functions.Contains(e.Name, "\"John*\"")).ToQueryString();

        // Lucene query with prefix
        var filter = parser.BuildFilter<Employee>("Name:John*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FullTextSearch_Phrase_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query using EF.Functions.Contains for phrase
        string sqlExpected = db.Employees.Where(e => EF.Functions.Contains(e.Title!, "\"Software Developer\"")).ToQueryString();

        // Lucene phrase query
        var filter = parser.BuildFilter<Employee>("Title:\"Software Developer\"");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FullTextSearch_NonFullTextField_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query - Email field is NOT configured for full-text search
        // Note: Using null-forgiving operator to match Lucene query behavior (no explicit null check)
        string sqlExpected = db.Employees.Where(e => e.Email!.Contains("john")).ToQueryString();

        // Lucene query
        var filter = parser.BuildFilter<Employee>("Email:*john*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FullTextSearch_NavigationProperty_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query using EF.Functions.Contains on navigation property
        string sqlExpected = db.Employees.Where(e => EF.Functions.Contains(e.Company.Name, "\"Acme\"")).ToQueryString();

        // Lucene query - navigation property with full-text search
        var filter = parser.BuildFilter<Employee>("Company.Name:Acme");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void FullTextSearch_CollectionNavigation_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query using EF.Functions.Contains on collection navigation
        string sqlExpected = db.Companies.Where(c => c.Employees.Any(e => EF.Functions.Contains(e.Name, "\"John\""))).ToQueryString();

        // Lucene query - collection navigation with full-text search
        var filter = parser.BuildFilter<Company>("Employees.Name:John");
        string sqlActual = db.Companies.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void NavigationProperty_NonFullTextField_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query - Department.Name is NOT configured for full-text search
        string sqlExpected = db.Employees.Where(e => e.Department!.Name.Contains("Engineering")).ToQueryString();

        // Lucene query - should use regular Contains, not full-text
        var filter = parser.BuildFilter<Employee>("Department.Name:*Engineering*");
        string sqlActual = db.Employees.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    [Fact]
    public void CollectionNavigation_NonFullTextField_GeneratesSameSQL_AsDirectLINQ()
    {
        using var db = _fixture.CreateFullTextSampleContext();
        var parser = _fixture.FullTextParser;

        // Direct LINQ query - Employee.Email is NOT configured for full-text search
        // Note: No explicit null check to match Lucene query behavior (SQL handles null with LIKE)
        string sqlExpected = db.Companies.Where(c => c.Employees.Any(e => e.Email!.Contains("acme"))).ToQueryString();

        // Lucene query - should use regular Contains, not full-text
        var filter = parser.BuildFilter<Company>("Employees.Email:*acme*");
        string sqlActual = db.Companies.Where(filter).ToQueryString();

        _output.WriteLine($"Expected SQL:\n{sqlExpected}");
        _output.WriteLine($"Actual SQL:\n{sqlActual}");

        Assert.Equal(sqlExpected, sqlActual);
    }

    #endregion
}
