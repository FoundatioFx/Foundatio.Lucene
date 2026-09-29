using System.Linq.Expressions;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Only fields discovered from the model (after filters) and registered custom fields can be queried. Everything
/// else is an invalid query, so excluded columns cannot be probed with prefix or range oracles.
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("passwordhash:secret*")]
    [InlineData("passwordhash:[a TO z]")]
    [InlineData("_exists_:passwordhash")]
    [InlineData("company.owner.passwordhash:hash-a*")]
    [InlineData("company.owner.passwordhash:\"hash-abc\"")]
    [InlineData("company.employees.passwordhash:*")]
    [InlineData("manager.passwordhash:secret-john")]
    [InlineData("name:x OR passwordhash:secret*")]
    public void BuildFilter_WithFilteredProperty_RejectsQuery(string query)
    {
        var unfiltered = new EntityFrameworkQueryParser();
        var parser = new EntityFrameworkQueryParser(c => c.UseEntityTypePropertyFilter(p => p.Name != "PasswordHash"));

        _db.Employees.Where(query, unfiltered).ToList();
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where(query, parser));

        Assert.Contains(ex.Errors, e => e.Code == QueryErrorCode.UnresolvedField && e.Message.Contains("not a queryable field"));
        Assert.Contains(ex.Result.UnresolvedFields, f => f.EndsWith("passwordhash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFilter_WithFilteredNavigation_RejectsEverythingReachedThroughIt()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseEntityTypeNavigationFilter(n => n.Name != nameof(Company.Owner)));

        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company.owner.username:alice.admin", parser));
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("_exists_:company.owner", parser));
        Assert.Throws<QueryValidationException>(() => _db.Companies.Where("owner.username:alice.admin", parser));
        Assert.Equal(3, _db.Employees.Where("company.name:\"Acme Corp\"", parser).Count());
    }

    [Fact]
    public void BuildFilter_WithFilteredSkipNavigation_RejectsIt()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseEntityTypeSkipNavigationFilter(n => n.Name != nameof(Employee.Projects)));

        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("projects.name:Apollo", parser));
        Assert.Equal(2, _db.Employees.Where("projects.name:Apollo", new EntityFrameworkQueryParser()).Count());
    }

    [Theory]
    [InlineData("nickname:john")]
    [InlineData("_exists_:nickname")]
    [InlineData("company.ceo:x")]
    [InlineData("name.length:5")]
    [InlineData("company.name.x:y")]
    [InlineData("company..name:x")]
    public void BuildFilter_WithUnknownField_RejectsQueryEvenWhenUnresolvedFieldsAreAllowed(string query)
    {
        var parser = new EntityFrameworkQueryParser(c => c.ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = true });

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where(query, parser));

        Assert.Equal(QueryErrorCode.UnresolvedField, ex.Errors.First().Code);
    }

    [Fact]
    public void BuildFilter_WithAliasToFilteredProperty_RejectsQuery()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.UseEntityTypePropertyFilter(p => p.Name != "PasswordHash");
            c.FieldMap = new FieldMap { { "pwd", "PasswordHash" } };
            c.FieldResolver = (field, _) => field == "secret" ? "Company.Owner.PasswordHash" : null;
        });

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("pwd:secret*", parser));
        Assert.Contains("(pwd)", ex.Message);
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("secret:hash*", parser));
    }

    [Fact]
    public void BuildSort_WithFilteredProperty_RejectsSort()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseEntityTypePropertyFilter(p => p.Name != "PasswordHash"));

        Assert.Throws<QueryValidationException>(() => _db.Employees.OrderBy("-passwordhash", parser));
        Assert.Throws<QueryValidationException>(() => _db.Employees.OrderBy("company.owner.passwordhash", parser));
    }

    [Fact]
    public void BuildFilter_WithRestrictedAndAllowedFields_EnforcesValidationOptions()
    {
        var restricted = new EntityFrameworkQueryParser(c => c.ValidationOptions = new QueryValidationOptions { RestrictedFields = { "salary" } });
        var allowed = new EntityFrameworkQueryParser(c =>
        {
            c.FieldMap = new FieldMap { { "who", "Name" } };
            c.ValidationOptions = new QueryValidationOptions { AllowedFields = { "who", "company" } };
        });

        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("salary:>1", restricted));
        Assert.Equal(1, _db.Employees.Where("who:\"John Doe\"", allowed).Count());
        Assert.Equal(3, _db.Employees.Where("company.name:\"Acme Corp\"", allowed).Count());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("age:30", allowed));
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("name:x", allowed));
    }

    [Fact]
    public void BuildFilter_WithLeadingWildcardsDisallowed_RejectsLeadingWildcards()
    {
        var parser = new EntityFrameworkQueryParser(c => c.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false });

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("name:*oh*", parser));

        Assert.Equal(QueryErrorCode.LeadingWildcardNotAllowed, Assert.Single(ex.Errors).Code);
        Assert.Equal(1, _db.Employees.Where("name:Jo*", parser).Count());
    }

    [Theory]
    [InlineData("name:/Jo.*/", "Regular expression")]
    [InlineData("name:John~", "Fuzzy")]
    [InlineData("name:John~2", "Fuzzy")]
    [InlineData("name:\"John Doe\"~2", "Proximity")]
    [InlineData("name:John^2", "Boost")]
    [InlineData("(name:John)^2", "Boost")]
    [InlineData("age:[1 TO 50]^2", "Boost")]
    [InlineData("age:[1 TO 50]~2", "Proximity")]
    [InlineData("name*:John", "Wildcard field names")]
    [InlineData("@include:missing", "include")]
    public void BuildFilter_WithUnsupportedSyntax_ThrowsValidationError(string query, string message)
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where(query, new EntityFrameworkQueryParser()));

        Assert.Contains(message, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFilter_WithRegexBuilder_UsesConfiguredTranslation()
    {
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        var parser = new EntityFrameworkQueryParser(c => c.UseRegex((field, pattern) => Expression.Call(field, startsWith, pattern)));

        Assert.Equal(["John Doe"], _db.Employees.Where("name:/John/", parser).Names());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("age:/3.*/", parser));
    }

    [Fact]
    public void BuildFilter_WithMaliciousValues_KeepsThemAsParameterValues()
    {
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("name:\"x' OR 1=1 --\" OR email:*\\'*", new EntityFrameworkQueryOptions { Model = _db.Model });
        string sql = SampleData.CreateOfflineSqlServer().Employees.Where(filter).ToSql();

        string where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

        Assert.Equal(["x' OR 1=1 --", "'"], DateQueryTests.CapturedValues(filter));
        Assert.Contains("[e].[Name] = @", where);
        Assert.DoesNotContain("1=1", where);
    }
}
