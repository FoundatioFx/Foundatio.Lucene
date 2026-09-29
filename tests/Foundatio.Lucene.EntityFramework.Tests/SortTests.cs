namespace Foundatio.Lucene.EntityFramework.Tests;

public class SortTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();
    private readonly EntityFrameworkQueryParser _parser = new(c => c.FieldMap = new FieldMap { { "pay", "Salary" } });

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("-salary", new[] { "Bob Wilson", "Jane Smith", "John Doe", "Alice Brown" })]
    [InlineData("salary:desc", new[] { "Bob Wilson", "Jane Smith", "John Doe", "Alice Brown" })]
    [InlineData("-pay", new[] { "Bob Wilson", "Jane Smith", "John Doe", "Alice Brown" })]
    [InlineData("name", new[] { "Alice Brown", "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("+age", new[] { "Alice Brown", "John Doe", "Jane Smith", "Bob Wilson" })]
    [InlineData("company.name -salary", new[] { "Jane Smith", "John Doe", "Alice Brown", "Bob Wilson" })]
    [InlineData("-company.name name", new[] { "Bob Wilson", "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("-(company.name name)", new[] { "Bob Wilson", "John Doe", "Jane Smith", "Alice Brown" })]
    [InlineData("-hiredate", new[] { "Alice Brown", "John Doe", "Jane Smith", "Bob Wilson" })]
    [InlineData("level:asc name:asc", new[] { "Alice Brown", "John Doe", "Bob Wilson", "Jane Smith" })]
    [InlineData("address.city name", new[] { "Jane Smith", "Alice Brown", "John Doe", "Bob Wilson" })]
    public void BuildSort_WithSortExpression_OrdersResults(string sort, string[] expected)
    {
        Assert.Equal(expected, _db.Employees.OrderBy(sort, _parser).Select(e => e.Name));
    }

    [Fact]
    public void BuildSort_ReturnsReusableSortThatCanBeExtended()
    {
        var sort = _parser.BuildSort<Employee>("company.name", new EntityFrameworkQueryOptions { Model = _db.Model });

        Assert.Equal(["Company.Name"], sort.Fields.Select(f => f.Field));
        Assert.Equal(["Alice Brown", "Jane Smith", "John Doe", "Bob Wilson"], sort.Apply(_db.Employees).ThenBy(e => e.Name).Select(e => e.Name));
        Assert.Equal(["Alice Brown", "John Doe"], sort.Apply(_db.Employees.Where("department.name:Engineering", _parser)).ThenBy(e => e.Age).Select(e => e.Name));
    }

    [Theory]
    [InlineData("nope", "not a queryable field")]
    [InlineData("company", "cannot be sorted on")]
    [InlineData("projects.name", "cannot be sorted on")]
    [InlineData("skills", "cannot be sorted on")]
    [InlineData("NOT name", "not supported in sort")]
    [InlineData("name*", "Sort expressions only support field names")]
    [InlineData("", "at least one field")]
    public void BuildSort_WithInvalidSort_ThrowsValidationError(string sort, string message)
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.OrderBy(sort, _parser));

        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void BuildSort_WithMaxSortFields_EnforcesLimit()
    {
        var parser = new EntityFrameworkQueryParser(c => c.ValidationOptions = new QueryValidationOptions { AllowedMaxSortFields = 1 });

        Assert.Throws<QueryValidationException>(() => _db.Employees.OrderBy("name age", parser));
        Assert.Equal("Alice Brown", _db.Employees.OrderBy("name", parser).First().Name);
    }

    [Fact]
    public void BuildSort_OnCompanyFromCollectionPath_ThrowsValidationError()
    {
        Assert.Throws<QueryValidationException>(() => _db.Companies.OrderBy("employees.name", _parser));
        Assert.Equal(["Tech Solutions", "Acme Corp"], _db.Companies.OrderBy("-foundedyear", _parser).Select(c => c.Name));
    }

    [Fact]
    public void BuildSort_WithDerivedPropertyTypes_BuildsTypedKeySelectors()
    {
        Assert.Equal([3, 2, 1], _db.TypeSamples.OrderBy("-dateonly", _parser).Select(t => t.Id));
        Assert.Equal([1, 3, 2], _db.TypeSamples.OrderBy("enum nullableint", _parser).Select(t => t.Id));
        Assert.Equal([2, 1, 3], _db.TypeSamples.OrderBy("-text", _parser).Select(t => t.Id));
    }
}
