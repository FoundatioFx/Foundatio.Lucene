using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// The samples in docs/guide/entity-framework.md, compiled and run.
/// </summary>
public class DocumentationSampleTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task QuickStart_FiltersAndSorts()
    {
        var db = _db;
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name", "Title"));

        var employees = await db.Employees
            .Where("Jo* AND salary:[50000 TO *] -status:Terminated", parser)
            .OrderBy("-hiredate name", parser)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["John Doe"], employees.Select(e => e.Name));

        var modelParser = new EntityFrameworkQueryParser(c => c.UseModel(db.Model));
        Expression<Func<Employee, bool>> filter = modelParser.BuildFilter<Employee>("name:\"John Doe\" OR age:>=40");
        var filter2 = parser.BuildFilter<Employee>("age:>=40", new EntityFrameworkQueryOptions { Model = db.Model });

        Assert.Equal(["Bob Wilson", "John Doe"], db.Employees.Where(filter).Names());
        Assert.Equal(["Bob Wilson"], db.Employees.Where(filter2).Names());
    }

    [Fact]
    public void Security_FiltersRestrictQueryableFields()
    {
        var parser = new EntityFrameworkQueryParser(c => c
            .UseEntityTypePropertyFilter(p => p.Name is not ("PasswordHash" or "ApiKey"))
            .UseEntityTypeNavigationFilter(n => n.Name != nameof(Company.Owner))
            .UseEntityTypeSkipNavigationFilter(n => n.Name != "AuditLogs"));

        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company.owner.username:x", parser));
        Assert.Equal(2, _db.Employees.Where("projects.name:Apollo", parser).Count());
    }

    [Fact]
    public void DefaultFields_TokenizerNormalizesPhoneNumbers()
    {
        _db.Users.Add(new User { UserName = "2145550100", PasswordHash = "" });
        _db.SaveChanges();

        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields(["UserName"], SearchOperator.StartsWith);
            c.UseSearchTokenizer(term =>
            {
                if (term.Field.Name != "UserName")
                    return;

                string digits = new([.. term.Term.Where(char.IsAsciiDigit)]);
                term.Tokens = digits.Length > 0 ? [digits] : [];
                term.Operator = SearchOperator.StartsWith;
            });
        });

        Assert.Equal(["2145550100"], _db.Users.Where("\"(214) 555\"", parser).Select(u => u.UserName));
    }

    [Fact]
    public void Regex_WithTranslation_BuildsPredicate()
    {
        var isMatch = typeof(Regex).GetMethod(nameof(Regex.IsMatch), [typeof(string), typeof(string)])!;
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).UseRegex((field, pattern) => Expression.Call(isMatch, field, pattern)));

        var filter = parser.BuildFilter<Employee>("name:/Jo.*/").Compile();

        Assert.Equal(["John Doe"], _db.Employees.AsEnumerable().Where(filter).Select(e => e.Name));
    }

    [Fact]
    public async Task CustomFields_QueryDataValues()
    {
        var db = _db;
        db.Contacts.Add(new Contact { Name = "John", DataValues = [new DataValue { DataDefinitionId = 1, IntegerValue = 30 }, new DataValue { DataDefinitionId = 2, StringValue = "New York" }] });
        db.SaveChanges();

        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(DataValueBuilder));
        var tenantOptions = new EntityFrameworkQueryOptions
        {
            AdditionalFields =
            [
                new EntityFieldInfo { Name = "age", ClrType = typeof(int), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 1, ["Column"] = "IntegerValue" } },
                new EntityFieldInfo { Name = "city", ClrType = typeof(string), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 2, ["Column"] = "StringValue" } }
            ]
        };

        var contacts = await db.Contacts.Where("age:>=30 AND city:New*", parser, tenantOptions).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("John", Assert.Single(contacts).Name);
    }

    private static Expression? DataValueBuilder(CustomFieldContext context)
    {
        if (context.Field.Data.GetValueOrDefault("DataDefinitionId") is not int definitionId
            || context.Field.Data["Column"] is not string column)
            return null;

        var row = Expression.Parameter(typeof(DataValue), "dv");
        var body = Expression.AndAlso(
            Expression.Equal(Expression.Property(row, nameof(DataValue.DataDefinitionId)), context.Parameterize(definitionId)),
            context.BuildDefault(Expression.Property(row, column)));

        return context.Any(Expression.Property(context.Instance, nameof(Contact.DataValues)), Expression.Lambda(body, row));
    }

    [Fact]
    public void CustomVisitor_ReplacesNodePredicate()
    {
        var parser = new EntityFrameworkQueryParser(c => c.AddVisitor(new TeamVisitor()));

        Assert.Equal(["Jane Smith"], _db.Employees.Where("@team:Apollo -name:\"John Doe\"", parser).Names());
    }

    private sealed class TeamVisitor : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field != "@team" || node.Query is not TermNode term)
                return base.Visit(node, context);

            string team = term.UnescapedTerm;
            Expression<Func<Employee, bool>> filter = e => e.Projects.Any(p => p.Name == team);
            node.SetFilterExpression(filter);
            return node;
        }
    }

    [Fact]
    public void Sorting_AppliesReusableSort()
    {
        var db = _db;
        var parser = new EntityFrameworkQueryParser();

        var sorted = db.Employees.OrderBy("-salary company.name +name", parser);
        EntityFrameworkSort<Employee> sort = parser.BuildSort<Employee>("hiredate:desc name", new EntityFrameworkQueryOptions { Model = db.Model });
        IOrderedQueryable<Employee> page = sort.Apply(db.Employees.Where("isactive:true", parser)).ThenBy(e => e.Id);

        Assert.Equal("Bob Wilson", sorted.First().Name);
        Assert.Equal(["John Doe", "Jane Smith", "Bob Wilson"], page.Select(e => e.Name));
        Assert.Equal(["HireDate", "Name"], sort.Fields.Select(f => f.Field));
    }

    [Fact]
    public void Options_BuilderCreatesTenantOptions()
    {
        var userTimeZone = TimeZoneInfo.Utc;
        var options = EntityFrameworkQueryOptions.CreateBuilder()
            .WithFieldMap(m => m.Map("who", "Name").Map("org", "Company.Name"))
            .WithDefaultFields("Name", "Title")
            .WithDefaultSearchOperator(SearchOperator.Contains)
            .WithTimeZone(userTimeZone)
            .WithValidationOptions(v => v.AllowLeadingWildcards = false)
            .WithIntField("age2", new Dictionary<string, object?> { ["DataDefinitionId"] = 1, ["Column"] = "IntegerValue" })
            .Build();

        Assert.Equal(["Alice Brown", "Jane Smith", "John Doe"], _db.Employees.Where("org:\"Acme Corp\"", new EntityFrameworkQueryParser(), options).Names());
    }

    [Fact]
    public async Task AsyncResolvers_UseAsyncMethods()
    {
        var db = _db;
        var ct = TestContext.Current.CancellationToken;
        var savedQueries = new Dictionary<string, string> { ["engineers"] = "department.name:Engineering" };
        var parser = new EntityFrameworkQueryParser(c => c.IncludeResolver = (name, context, token) => ValueTask.FromResult(savedQueries.GetValueOrDefault(name)));

        var query = await db.Employees.WhereAsync("@include:engineers -status:Terminated", parser, cancellationToken: ct);
        var filter = await parser.BuildFilterAsync<Employee>("@include:engineers", new() { Model = db.Model }, ct);

        Assert.Equal(["John Doe"], query.Names());
        Assert.Equal(["Alice Brown", "John Doe"], db.Employees.Where(filter).Names());
    }

    [Fact]
    public void Validation_ReportsErrors()
    {
        var parser = new EntityFrameworkQueryParser();
        var options = new EntityFrameworkQueryOptions { Model = _db.Model };

        QueryValidationResult result = parser.ValidateQuery<Employee>("nope:1 AND age:old", options);
        QueryValidationResult sortResult = parser.ValidateSort<Employee>("-salary nope", options);
        QueryResult<Expression<Func<Employee, bool>>> built = parser.TryBuildFilter<Employee>("age:old", options);

        Assert.Equal([QueryErrorCode.UnresolvedField, QueryErrorCode.TypeConversionError], result.ValidationErrors.Select(e => e.Code));
        Assert.False(sortResult.IsValid);
        Assert.False(built.IsSuccess);
        Assert.Contains("(old)", built.ErrorMessage);
    }
}
