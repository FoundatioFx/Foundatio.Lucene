using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Verifies the EF parser runs the shared visitor pipeline (field aliases, @includes, date math)
/// and fails loudly for constructs SQL cannot honor (fuzzy, proximity/slop).
/// </summary>
public class EntityFrameworkPipelineTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SampleContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new SampleContext(options);
        context.Companies.Add(new Company { Id = 1, Name = "Acme Corp", Location = "New York", FoundedYear = 2000, IsPublic = true });
        context.Employees.AddRange(
            new Employee { Id = 1, Name = "John Doe", Email = "john@acme.com", Title = "Software Developer", Salary = 80000, Age = 30, HireDate = new DateTime(2020, 1, 15), IsActive = true, CompanyId = 1 },
            new Employee { Id = 2, Name = "Jane Smith", Email = "jane@acme.com", Title = "Project Manager", Salary = 95000, Age = 35, HireDate = new DateTime(2019, 6, 1), IsActive = true, CompanyId = 1 },
            new Employee { Id = 3, Name = "Bob Wilson", Email = "bob@tech.com", Title = "Senior Developer", Salary = 110000, Age = 40, HireDate = new DateTime(2018, 3, 20), IsActive = true, CompanyId = 1 });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public void BuildFilter_WithPerRequestFieldMapAlias_ResolvesToRealField()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();
        var options = new EntityFrameworkQueryOptionsBuilder()
            .WithFieldMap(new FieldMap { { "user", "Name" } })
            .Build();

        var filter = parser.BuildFilter<Employee>("user:John", context: null, options: options);
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void BuildFilter_WithPerRequestIncludes_ExpandsReference()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();
        var options = new EntityFrameworkQueryOptionsBuilder()
            .WithIncludes(new Dictionary<string, string> { ["seniors"] = "Title:Senior" })
            .Build();

        var filter = parser.BuildFilter<Employee>("@include:seniors", context: null, options: options);
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Bob Wilson", results[0].Name);
    }

    [Fact]
    public void BuildFilter_WithDateMath_ResolvesNowRelativeToTimeProvider()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser(c =>
            c.SetTimeProvider(new FixedTimeProvider(new DateTimeOffset(2020, 1, 20, 0, 0, 0, TimeSpan.Zero))));

        // John was hired 2020-01-15, inside [now-7d TO now]; the others were hired years earlier.
        var filter = parser.BuildFilter<Employee>("HireDate:[now-7d TO now]");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void BuildFilter_FuzzyQuery_ThrowsUnsupported()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var ex = Assert.Throws<QueryBuildException>(() => parser.BuildFilter<Employee>("Name:jon~2"));
        Assert.Equal(QueryErrorCode.UnsupportedQueryType, ex.ErrorCode);
    }

    [Fact]
    public void TryBuildFilter_ProximityQuery_ReturnsFailure()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var result = parser.TryBuildFilter<Employee>("Title:\"Software Developer\"~2");

        Assert.False(result.IsSuccess);
    }
}
