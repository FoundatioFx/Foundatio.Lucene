using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Queries executed on SQL Server, covering what the in-memory provider cannot show: relational null semantics,
/// LIKE escaping, collations, JSON primitive collections, full-text search, and <c>datetime2</c> bounds.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("TestType", "Integration")]
public class SqlServerIntegrationTests(SqlServerFixture fixture)
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private readonly EntityFrameworkQueryParser _parser = new();

    private readonly EntityFrameworkQueryParser _fullTextParser = new(c => c
        .AddFullTextFields("Employee.Name", "Title", "Company.Name")
        .SetDefaultFields("Name"));

    [Theory]
    [InlineData("-a:true b:true", new[] { 2, 3 })]
    [InlineData("+a:true b:true", new[] { 6, 7 })]
    [InlineData("a:true OR NOT b:true", new[] { 0, 1, 4, 5, 6, 7 })]
    [InlineData("a:true OR -b:true", new[] { 4, 5 })]
    [InlineData("a:true AND b:true OR c:true", new[] { 1, 3, 5, 6, 7 })]
    [InlineData("-a:true -c:true", new[] { 0, 2 })]
    [InlineData("-*:*", new int[0])]
    public void BuildFilter_OnSqlServer_FollowsLuceneBooleanSemantics(string query, int[] expected)
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(expected, db.Flags.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("-email:*acme*", new[] { "Alice Brown", "Bob Wilson" })]
    [InlineData("NOT email:\"john@acme.com\"", new[] { "Alice Brown", "Bob Wilson", "Jane Smith" })]
    [InlineData("-email:j*", new[] { "Alice Brown", "Bob Wilson" })]
    [InlineData("-department.budget:>600000", new[] { "Bob Wilson", "Jane Smith" })]
    public void BuildFilter_WithNegationOnNullableColumn_IncludesRowsWithoutAValue(string query, string[] expected)
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(expected, db.Employees.Where(query, _parser).Names());
    }

    [Theory]
    [InlineData("-nullableint:>=5", new[] { 2 })]
    [InlineData("-text:[alpha TO beta]", new[] { 3 })]
    [InlineData("-nullabledatetime:>2024-02-15", new[] { 1, 2 })]
    [InlineData("NOT (nullableint:>=5 AND text:[alpha TO beta])", new[] { 2, 3 })]
    [InlineData("-(nullableint:<10 OR nullabledatetime:<2024-03-01)", new[] { 3 })]
    [InlineData("nullableint:>=10 OR NOT nullableint:>=5", new[] { 2, 3 })]
    [InlineData("text:[alpha TO beta]", new[] { 1, 2 })]
    [InlineData("char:[a TO b]", new[] { 1, 2 })]
    [InlineData("enum:[OnLeave TO Terminated]", new[] { 2, 3 })]
    [InlineData("guid:22222222-2222-2222-2222-222222222222", new[] { 2 })]
    [InlineData("timeonly:[09:00 TO 18:00]", new[] { 1, 2 })]
    [InlineData("timespan:<01:00:00", new[] { 3 })]
    [InlineData("decimal:12.5", new[] { 1 })]
    [InlineData("datetime:[2024-01-01 TO 2024-02-01}", new[] { 1, 2 })]
    [InlineData("datetime:<=2024-01-31", new[] { 1, 2 })]
    [InlineData("datetime:>2024-01-31", new[] { 3 })]
    [InlineData("datetimeoffset:2024-02-01", new[] { 2 })]
    [InlineData("dateonly:[2024-01-01 TO 2024-02-01}", new[] { 1, 2 })]
    [InlineData("_missing_:nullabledatetime", new[] { 1 })]
    public void BuildFilter_WithTypedValues_MatchesOnSqlServer(string query, int[] expected)
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(expected, db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("datetime:2024-01-31", new[] { 2, 3 })]
    [InlineData("nullabledatetime:[2024-03-10T01:00 TO 2024-03-10T03:30]", new[] { 3 })]
    [InlineData("nullabledatetime:[2024-03-10T01:00 TO 2024-03-10T03:29]", new int[0])]
    public void BuildFilter_WithTimeZone_MatchesOnSqlServer(string query, int[] expected)
    {
        using var db = fixture.CreateSampleContext();
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultTimeZone(Chicago));

        Assert.Equal(expected, db.TypeSamples.Where(query, parser).Ids());
    }

    [Theory]
    [InlineData("name:Pro*ject", new[] { "Pro*ject", "Pro?ject", "Project" })]
    [InlineData(@"name:P?o\*j*", new[] { "Pro*ject" })]
    [InlineData(@"name:Pro\*ject", new[] { "Pro*ject" })]
    [InlineData(@"name:Pro\?ject", new[] { "Pro?ject" })]
    [InlineData(@"name:a\[b\]?", new[] { "a[b]c" })]
    [InlineData(@"name:*\[*", new[] { "a[b]c" })]
    [InlineData("name:100%", new[] { "100%" })]
    [InlineData("name:?00%", new[] { "100%" })]
    [InlineData("name:100%*", new[] { "100%" })]
    [InlineData("name:a_?", new[] { "a_b" })]
    [InlineData("name:*_*", new[] { "a_b" })]
    [InlineData(@"name:ba?k\\s*", new[] { @"back\slash" })]
    [InlineData(@"name:back\\*", new[] { @"back\slash" })]
    [InlineData("name:\"PROJECT\"", new[] { "Project" })]
    public void BuildFilter_WithWildcardsAndSpecialCharacters_EscapesLikeOnSqlServer(string query, string[] expected)
    {
        using var db = fixture.CreateSampleContext();
        using var transaction = db.Database.BeginTransaction();
        db.Projects.AddRange(
            new Project { Name = "Pro*ject" }, new Project { Name = "Pro?ject" }, new Project { Name = "Project" },
            new Project { Name = "100%" }, new Project { Name = "1000" }, new Project { Name = "a_b" }, new Project { Name = "axb" },
            new Project { Name = "a[b]c" }, new Project { Name = "abc" }, new Project { Name = @"back\slash" });
        db.SaveChanges();

        var names = db.Projects.Where(query, _parser).Select(p => p.Name).AsEnumerable().Order(StringComparer.Ordinal);

        Assert.Equal(expected, names);
    }

    [Fact]
    public void BuildFilter_WithCaseInsensitiveCollation_MatchesRegardlessOfCase()
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(["John Doe"], db.Employees.Where("name:\"john doe\"", _parser).Names());
        Assert.Equal(["John Doe"], db.Employees.Where("name:JO*", _parser).Names());
        Assert.Equal(["Jane Smith"], db.Employees.Where("name:j?NE*", _parser).Names());
    }

    [Fact]
    public void BuildFilter_WithPrimitiveCollection_QueriesJsonValues()
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(["Bob Wilson", "John Doe"], db.Employees.Where("skills:c*", _parser).Names());
        Assert.Equal(["Bob Wilson", "John Doe"], db.Employees.Where("skills:csharp", _parser).Names());
        Assert.Equal(["Bob Wilson", "Jane Smith", "John Doe"], db.Employees.Where("_exists_:skills", _parser).Names());
    }

    [Fact]
    public void BuildFilter_WithNavigationsAndOwnedTypes_MatchesOnSqlServer()
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(["Alice Brown", "Jane Smith", "John Doe"], db.Employees.Where("company.owner.username:alice.admin", _parser).Names());
        Assert.Equal(["Jane Smith", "John Doe"], db.Employees.Where("projects.name:Apollo", _parser).Names());
        Assert.Equal(["Alice Brown", "John Doe"], db.Employees.Where("address.city:\"New York\"", _parser).Names());
        Assert.Equal(["Bob Wilson", "Jane Smith", "John Doe"], db.Employees.Where("level:Senior OR level:Mid", _parser).Names());
        Assert.Equal(["Tech Solutions"], db.Companies.Where("-employees.age:<30", _parser).Select(c => c.Name));
    }

    [Theory]
    [InlineData("name:John", new[] { "John Doe" })]
    [InlineData("name:jo*", new[] { "John Doe" })]
    [InlineData("title:\"Software Developer\"", new[] { "John Doe" })]
    [InlineData("title:developer", new[] { "Alice Brown", "Bob Wilson", "John Doe" })]
    [InlineData("company.name:Acme", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("jan*", new[] { "Jane Smith" })]
    [InlineData("name:\"Nobody\\\" OR \\\"John\"", new string[0])]
    [InlineData("name:\"Nobody\\\" OR John OR \\\"Jane\"", new string[0])]
    [InlineData("name:\"Doe\\\" NEAR \\\"John\"", new string[0])]
    [InlineData("name:\"John Doe\"", new[] { "John Doe" })]
    [InlineData("name:*oh*", new[] { "John Doe" })]
    [InlineData("-name:John", new[] { "Alice Brown", "Bob Wilson", "Jane Smith" })]
    public void BuildFilter_WithFullTextFields_UsesFullTextSearch(string query, string[] expected)
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(expected, db.Employees.Where(query, _fullTextParser).Names());
    }

    [Fact]
    public void BuildSort_OnSqlServer_OrdersResults()
    {
        using var db = fixture.CreateSampleContext();

        Assert.Equal(["Bob Wilson", "Jane Smith", "John Doe", "Alice Brown"], db.Employees.OrderBy("-salary", _parser).Select(e => e.Name));
        Assert.Equal(["Jane Smith", "John Doe", "Alice Brown", "Bob Wilson"], db.Employees.OrderBy("company.name -salary", _parser).Select(e => e.Name));
    }

    [Fact]
    public void BuildFilter_WithCustomDataValueFields_QueriesValueRowsOnSqlServer()
    {
        using var db = fixture.CreateSampleContext();
        using var transaction = db.Database.BeginTransaction();
        var john = new Contact { Name = "John", DataValues = [new DataValue { DataDefinitionId = 1, IntegerValue = 30 }, new DataValue { DataDefinitionId = 2, StringValue = "New York" }] };
        var jane = new Contact { Name = "Jane", DataValues = [new DataValue { DataDefinitionId = 1, IntegerValue = 25 }] };
        db.Contacts.AddRange(john, jane);
        db.SaveChanges();

        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(CustomFieldTests.DataValueBuilder));
        var options = new EntityFrameworkQueryOptions
        {
            AdditionalFields =
            [
                new EntityFieldInfo { Name = "age", ClrType = typeof(int), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 1, ["Column"] = nameof(DataValue.IntegerValue) } },
                new EntityFieldInfo { Name = "city", ClrType = typeof(string), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 2, ["Column"] = nameof(DataValue.StringValue) } }
            ]
        };

        Assert.Equal(["John"], db.Contacts.Where("age:>=30", parser, options).Select(c => c.Name));
        Assert.Equal(["Jane"], db.Contacts.Where("_missing_:city", parser, options).Select(c => c.Name));
        Assert.Equal(["John"], db.Contacts.Where("city:new*", parser, options).Select(c => c.Name));
    }

    [Fact]
    public async Task BuildFilter_WithDifferentValuesForSameShape_ReturnsCorrectResultsFromCachedPlan()
    {
        await using var db = fixture.CreateSampleContext();

        for (int age = 20; age <= 45; age += 5)
        {
            int expected = db.Employees.Count(e => e.Age >= age);
            Assert.Equal(expected, await db.Employees.Where($"age:>={age}", _parser).CountAsync(TestContext.Current.CancellationToken));
        }
    }
}
