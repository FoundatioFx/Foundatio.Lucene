using System.Linq.Expressions;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.EntityFramework.Tests;

public class NavigationTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();
    private readonly EntityFrameworkQueryParser _parser = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("company.name:\"Acme Corp\"", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("COMPANY.NAME:\"Acme Corp\"", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("department.name:Engineering", new[] { "Alice Brown", "John Doe" })]
    [InlineData("department.budget:>600000", new[] { "Alice Brown", "John Doe" })]
    [InlineData("company.owner.username:alice.admin", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("_exists_:department.budget", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("_missing_:department.budget", new[] { "Bob Wilson" })]
    [InlineData("address.city:\"New York\"", new[] { "Alice Brown", "John Doe" })]
    [InlineData("address.street:*Oak*", new[] { "Jane Smith" })]
    [InlineData("manager.name:\"John Doe\"", new[] { "Alice Brown" })]
    [InlineData("_exists_:manager", new[] { "Alice Brown" })]
    [InlineData("_missing_:manager", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("manager:(*)", new[] { "Alice Brown" })]
    [InlineData("-department.budget:(*)", new[] { "Bob Wilson" })]
    [InlineData("manager.name:(*)", new[] { "Alice Brown" })]
    [InlineData("company.name:(Acme* OR Tech*)", new[] { "Alice Brown", "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("company.name:(name:\"John Doe\")", new[] { "John Doe" })]
    public void BuildFilter_WithReferenceNavigationPath_AccessesRelatedEntity(string query, string[] expected)
    {
        Assert.Equal(expected, _db.Employees.Where(query, _parser).Names());
    }

    [Theory]
    [InlineData("_exists_:manager.age", new[] { "Alice Brown" })]
    [InlineData("manager.age:(*)", new[] { "Alice Brown" })]
    [InlineData("manager.age:[* TO *]", new[] { "Alice Brown" })]
    [InlineData("_missing_:manager.age", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("NOT _exists_:manager.age", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("-manager.age:[* TO *]", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    public void BuildFilter_WithNonNullableFieldUnderOptionalReference_RequiresNavigationExistence(string query, string[] expected)
    {
        // Arrange
        var options = new EntityFrameworkQueryOptions { Model = _db.Model };

        // Act
        var filter = _parser.BuildFilter<Employee>(query, options);
        var names = _db.Employees.Where(filter).Names();
        var compiledNames = _db.Employees.AsEnumerable().Where(filter.Compile()).Select(e => e.Name).Order();

        // Assert
        Assert.Equal(expected, names);
        Assert.Equal(expected, compiledNames);
    }

    [Theory]
    [InlineData("_exists_:manager.manager.age", new string[0])]
    [InlineData("_missing_:manager.manager.age", new[] { "Alice Brown", "Bob Wilson", "Jane Smith", "John Doe" })]
    public void BuildFilter_WithMultipleOptionalReferences_RequiresEveryNavigation(string query, string[] expected)
    {
        // Arrange
        var options = new EntityFrameworkQueryOptions { Model = _db.Model };

        // Act
        var filter = _parser.BuildFilter<Employee>(query, options);
        var compiledNames = _db.Employees.AsEnumerable().Where(filter.Compile()).Select(e => e.Name).Order();

        // Assert
        Assert.Equal(expected, _db.Employees.Where(filter).Names());
        Assert.Equal(expected, compiledNames);
    }

    [Fact]
    public void BuildFilter_WithOptionalReferenceUnderCollection_RequiresMatchingNavigation()
    {
        // Arrange
        const string query = "_exists_:employees.manager.age";

        // Act
        var names = _db.Companies.Where(query, _parser).Select(c => c.Name).ToArray();

        // Assert
        Assert.Equal(["Acme Corp"], names);
    }

    [Theory]
    [InlineData("_exists_:age", 4)]
    [InlineData("_missing_:age", 0)]
    [InlineData("_exists_:company.foundedyear", 4)]
    [InlineData("_missing_:company.foundedyear", 0)]
    public void BuildFilter_WithRequiredNonNullableField_PreservesExistence(string query, int expected)
    {
        // Arrange
        var employees = _db.Employees;

        // Act
        int count = employees.Where(query, _parser).Count();

        // Assert
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("_exists_:manager.age", 4)]
    [InlineData("_missing_:manager.age", 3)]
    [InlineData("manager.age:[* TO *]", 4)]
    public void BuildFilter_WithCustomNavigationExistence_PreservesBuilderSemantics(string query, int expected)
    {
        // Arrange
        int calls = 0;
        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(context =>
        {
            if (context.Field.FullName != "Manager.Age")
                return null;

            calls++;
            return context.Node is MissingNode
                ? Expression.Equal(context.Instance, Expression.Constant(null, context.Instance.Type))
                : Expression.Constant(true);
        }));

        // Act
        int count = _db.Employees.Where(query, parser).Count();

        // Assert
        Assert.Equal(expected, count);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("projects.name:Apollo", new[] { "Jane Smith", "John Doe" })]
    [InlineData("-projects.name:Apollo", new[] { "Alice Brown", "Bob Wilson" })]
    [InlineData("_exists_:projects", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("_missing_:projects", new[] { "Alice Brown" })]
    [InlineData("_exists_:projects.name", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    [InlineData("_exists_:reports", new[] { "John Doe" })]
    [InlineData("reports.name:\"Alice Brown\"", new[] { "John Doe" })]
    [InlineData("projects.name:Apollo AND projects.name:Zephyr", new[] { "Jane Smith" })]
    [InlineData("skills:csharp", new[] { "Bob Wilson", "John Doe" })]
    [InlineData("-skills:go", new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("_exists_:skills", new[] { "Bob Wilson", "Jane Smith", "John Doe" })]
    public void BuildFilter_WithCollectionPath_UsesAny(string query, string[] expected)
    {
        Assert.Equal(expected, _db.Employees.Where(query, _parser).Names());
    }

    [Theory]
    [InlineData("employees.name:\"Bob Wilson\"", new[] { "Tech Solutions" })]
    [InlineData("employees.age:>=35", new[] { "Acme Corp", "Tech Solutions" })]
    [InlineData("-employees.age:<30", new[] { "Tech Solutions" })]
    [InlineData("departments.employees.name:\"Jane Smith\"", new[] { "Acme Corp" })]
    [InlineData("employees.projects.name:Zephyr", new[] { "Acme Corp", "Tech Solutions" })]
    [InlineData("_exists_:departments.budget", new[] { "Acme Corp" })]
    [InlineData("departments.budget:[* TO *]", new[] { "Acme Corp" })]
    [InlineData("_missing_:departments.budget", new[] { "Tech Solutions" })]
    public void BuildFilter_WithNestedCollectionPath_NestsAny(string query, string[] expected)
    {
        Assert.Equal(expected, _db.Companies.Where(query, _parser).Select(c => c.Name).AsEnumerable().Order());
    }

    [Fact]
    public void BuildFilter_WithExistsOnCollection_ExcludesEmptyCollections()
    {
        _db.Projects.Add(new Project { Name = "Orphan" });
        _db.SaveChanges();

        Assert.Equal(["Apollo", "Zephyr"], _db.Projects.Where("_exists_:employees", _parser).Select(p => p.Name).AsEnumerable().Order());
        Assert.Equal(["Orphan"], _db.Projects.Where("_missing_:employees", _parser).Select(p => p.Name));
        Assert.Equal(["Zephyr"], _db.Projects.Where("employees.name:\"Bob Wilson\"", _parser).Select(p => p.Name));
    }

    [Fact]
    public void BuildFilter_WithMissingOnCollectionField_RequiresNoElementToHaveAValue()
    {
        // Tech's only department has no budget, Acme's two departments both have one.
        _db.Departments.Add(new Department { Name = "Legal", Budget = null, CompanyId = _db.Companies.Single(c => c.Name == "Acme Corp").Id });
        _db.SaveChanges();

        Assert.Equal(["Tech Solutions"], _db.Companies.Where("NOT _exists_:departments.budget", _parser).Select(c => c.Name));
        Assert.Equal(["Tech Solutions"], _db.Companies.Where("_missing_:departments.budget", _parser).Select(c => c.Name));
    }

    [Fact]
    public void BuildFilter_WithValueOnNavigation_ThrowsValidationError()
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company:acme", _parser));

        Assert.Contains("is a navigation", Assert.Single(ex.Errors).Message);
    }

    [Fact]
    public void BuildFilter_WithMaxFieldDepth_LimitsNavigationPaths()
    {
        var oneLevel = new EntityFrameworkQueryParser(c => c.SetMaxFieldDepth(1));
        var rootOnly = new EntityFrameworkQueryParser(c => c.SetMaxFieldDepth(0));

        Assert.Equal(3, _db.Employees.Where("company.name:\"Acme Corp\"", oneLevel).Count());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company.owner.username:alice.admin", oneLevel));
        Assert.Equal(4, _db.Employees.Where("_exists_:company", rootOnly).Count());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company.name:\"Acme Corp\"", rootOnly));
    }

    [Fact]
    public void BuildFilter_WithSelfReferencingPath_AllowsCyclesWithinDepth()
    {
        Assert.Equal(["Alice Brown"], _db.Employees.Where("manager.company.employees.name:\"Jane Smith\"", _parser).Names());
    }

    [Fact]
    public void BuildFilter_WithShadowProperty_UsesEfProperty()
    {
        var filter = _parser.BuildFilter<Employee>("_missing_:internalnotes", new EntityFrameworkQueryOptions { Model = _db.Model });

        Assert.Contains("Property(", filter.ToString());
        Assert.Equal(4, _db.Employees.Where(filter).Count());
    }

    [Fact]
    public void GetField_WithPath_DescribesField()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).AddFullTextFields("Company.Name"));

        var field = parser.GetField<Employee>("company.name");

        Assert.NotNull(field);
        Assert.Equal("Company.Name", field.FullName);
        Assert.Equal(EntityFieldKind.Property, field.Kind);
        Assert.True(field.IsString);
        Assert.True(field.IsFullTextSearch);
        Assert.Equal("Company", field.Parent?.Name);
        Assert.True(field.Parent?.IsNavigation);
        Assert.True(parser.GetField<Company>("employees")?.IsCollection);
        Assert.Equal(typeof(string), parser.GetField<Employee>("skills")?.ElementType);
        Assert.Null(parser.GetField<Employee>("nope"));
        Assert.Same(field, parser.GetField<Employee>("Company.Name"));
    }
}
