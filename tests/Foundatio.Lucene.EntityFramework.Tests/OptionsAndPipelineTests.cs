using System.Collections.Concurrent;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.EntityFramework.Tests;

public class OptionsAndPipelineTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void BuildFilter_WithFieldMapAndIncludes_ResolvesAliasesAndExpandsIncludes()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.UseFieldMap(new FieldMap { { "who", "Name" }, { "org", "Company" } });
            c.UseIncludes(new Dictionary<string, string> { ["seniors"] = "level:Senior", ["acme"] = "org.name:\"Acme Corp\"" });
        });

        Assert.Equal(["John Doe"], _db.Employees.Where("who:\"John Doe\"", parser).Names());
        Assert.Equal(["Alice Brown", "Jane Smith", "John Doe"], _db.Employees.Where("org.name:\"Acme Corp\"", parser).Names());
        Assert.Equal(["Jane Smith"], _db.Employees.Where("@include:seniors @include:acme", parser).Names());
        Assert.Equal(["Bob Wilson"], _db.Employees.Where("@include:seniors -@include:acme", parser).Names());
    }

    [Fact]
    public void BuildFilter_WithRegisteredAndPerRequestOptions_LayersOptions()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Title"));
        parser.SetOptions<Employee>(o => o.WithFieldMap(new FieldMap { { "who", "Name" } }).WithDefaultFields("Name"));

        Assert.Equal(["John Doe"], _db.Employees.Where("Jo*", parser).Names());
        Assert.Equal(["John Doe"], _db.Employees.Where("who:\"John Doe\"", parser).Names());
        Assert.Equal(["Bob Wilson"], _db.Employees.Where("Senior", parser, new EntityFrameworkQueryOptions { DefaultFields = ["Title"] }).Names());
        Assert.Equal([typeof(Employee)], parser.RegisteredEntityTypes);
        Assert.NotNull(parser.GetOptions<Employee>());

        Assert.True(parser.RemoveOptions<Employee>());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("who:\"John Doe\"", parser));
    }

    [Fact]
    public void BuildFilter_WithOptionsBuilder_BuildsImmutableOptions()
    {
        var validation = new QueryValidationOptions { AllowLeadingWildcards = false };
        var options = EntityFrameworkQueryOptions.CreateBuilder()
            .WithFieldMap(m => m.Map("who", "Name"))
            .WithDefaultFields("Name")
            .WithDefaultSearchOperator(SearchOperator.Contains)
            .WithDefaultOperator(BooleanOperator.Or)
            .WithValidationOptions(validation)
            .WithDefaultTimeZone(TimeZoneInfo.Utc)
            .WithIntField("score", new Dictionary<string, object?> { ["Column"] = "Age" })
            .Build();

        var parser = new EntityFrameworkQueryParser();

        Assert.Equal(["Jane Smith", "John Doe"], _db.Employees.Where("Doe Smith", parser, options).Names());
        Assert.Equal(SearchOperator.Contains, options.DefaultSearchOperator);
        Assert.Same(validation, options.ValidationOptions);
        Assert.Equal("score", Assert.Single(options.AdditionalFields!).Name);
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("who:*oe", parser, options));
    }

    [Fact]
    public async Task BuildFilter_ConcurrentTenantsWithDifferentOptions_KeepsRequestsIsolated()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model));
        var tenants = new[]
        {
            (Options: new EntityFrameworkQueryOptions { FieldMap = new FieldMap { { "x", "Name" } }, DefaultFields = ["Name"] }, Query: "x:\"John Doe\"", Expected: "John Doe"),
            (Options: new EntityFrameworkQueryOptions { FieldMap = new FieldMap { { "x", "Title" } }, DefaultFields = ["Title"] }, Query: "x:\"Senior Developer\"", Expected: "Bob Wilson"),
            (Options: new EntityFrameworkQueryOptions { FieldMap = new FieldMap { { "x", "Email" } }, DefaultFields = ["Email"] }, Query: "x:\"jane@acme.com\"", Expected: "Jane Smith"),
            (Options: new EntityFrameworkQueryOptions { AdditionalFields = [new EntityFieldInfo { Name = "x", ClrType = typeof(string) }], CustomFieldExpressionBuilder = AliceOnly }, Query: "x:anything", Expected: "Alice Brown")
        };

        var employees = _db.Employees.ToList();
        var failures = new ConcurrentBag<string>();
        await Parallel.ForAsync(0, 2000, TestContext.Current.CancellationToken, (i, _) =>
        {
            var tenant = tenants[i % tenants.Length];
            var filter = parser.BuildFilter<Employee>(tenant.Query, tenant.Options).Compile();
            var matches = employees.Where(filter).Select(e => e.Name).ToList();
            if (matches is not [var name] || name != tenant.Expected)
                failures.Add($"{tenant.Query}: {string.Join(", ", matches)}");

            return ValueTask.CompletedTask;
        });

        Assert.Empty(failures);
    }

    private static System.Linq.Expressions.Expression? AliceOnly(CustomFieldContext context)
    {
        return context.Field.Name == "x"
            ? System.Linq.Expressions.Expression.Equal(
                System.Linq.Expressions.Expression.Property(context.Instance, nameof(Employee.Name)),
                context.Parameterize("Alice Brown"))
            : null;
    }

    [Fact]
    public void BuildFilter_WithFieldDiscovery_CachesMetadataPerParserNotGlobally()
    {
        var filtered = new EntityFrameworkQueryParser(c => c.UseEntityTypePropertyFilter(p => p.Name != nameof(Employee.Age)));
        var unfiltered = new EntityFrameworkQueryParser();

        Assert.Equal(["John Doe"], _db.Employees.Where("age:30", unfiltered).Names());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("age:30", filtered));
        Assert.Equal(["John Doe"], _db.Employees.Where("age:30", unfiltered).Names());

        var shallow = new EntityFrameworkQueryParser(c => c.SetMaxFieldDepth(0));
        Assert.Equal(3, _db.Employees.Where("company.name:\"Acme Corp\"", unfiltered).Count());
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("company.name:\"Acme Corp\"", shallow));
    }

    [Fact]
    public async Task BuildFilterAsync_WithAsyncResolvers_ResolvesBeforeBuilding()
    {
        var resolvedFields = new ConcurrentBag<string>();
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.UseModel(_db.Model);
            c.IncludeResolver = async (name, _, ct) =>
            {
                await Task.Delay(1, ct);
                return name == "engineers" ? "who:(\"John Doe\" OR \"Alice Brown\")" : null;
            };
            c.AsyncFieldResolver = async (field, _, ct) =>
            {
                await Task.Delay(1, ct);
                resolvedFields.Add(field);
                return field == "who" ? "Name" : null;
            };
        });

        Assert.Throws<InvalidOperationException>(() => parser.BuildFilter<Employee>("who:x"));

        var filter = await parser.BuildFilterAsync<Employee>("@include:engineers -who:\"Alice Brown\"", cancellationToken: TestContext.Current.CancellationToken);
        var queryable = await _db.Employees.WhereAsync("@include:engineers", parser, cancellationToken: TestContext.Current.CancellationToken);
        var validation = await parser.ValidateQueryAsync<Employee>("@include:missing", cancellationToken: TestContext.Current.CancellationToken);
        var sort = await parser.BuildSortAsync<Employee>("-who", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["John Doe"], _db.Employees.Where(filter).Names());
        Assert.Equal(["Alice Brown", "John Doe"], queryable.Names());
        Assert.Contains("who", resolvedFields);
        Assert.False(validation.IsValid);
        Assert.Contains("missing", validation.UnresolvedIncludes);
        Assert.Equal("Name", Assert.Single(sort.Fields).Field);
        Assert.Equal("John Doe", sort.Apply(_db.Employees).First().Name);
    }

    [Fact]
    public async Task ValidateQuery_WithAsyncResolvers_RequiresAsyncMethods()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.UseModel(_db.Model);
            c.AsyncFieldResolver = (field, _, _) => ValueTask.FromResult<string?>(field == "who" ? "Name" : null);
        });

        Assert.Throws<InvalidOperationException>(() => parser.ValidateQuery<Employee>("who:x"));
        Assert.True((await parser.ValidateQueryAsync<Employee>("who:x", cancellationToken: TestContext.Current.CancellationToken)).IsValid);

        var success = await parser.TryBuildFilterAsync<Employee>("who:\"Jane Smith\"", cancellationToken: TestContext.Current.CancellationToken);
        var failure = await parser.TryBuildFilterAsync<Employee>("who:(x", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["Jane Smith"], _db.Employees.Where(success.Value).Names());
        Assert.IsType<QueryParseException>(failure.Error);
    }

    [Fact]
    public void ValidateSort_WithValidAndInvalidSorts_ReportsErrors()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).UseFieldMap(new FieldMap { { "pay", "Salary" } }));

        var valid = parser.ValidateSort<Employee>("-pay company.name");
        var invalid = parser.ValidateSort<Employee>("nope projects.name");

        Assert.True(valid.IsValid);
        Assert.Contains("pay", valid.ReferencedFields);
        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.ValidationErrors.Count);
        Assert.Contains("nope", invalid.UnresolvedFields);

        var throwing = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).UseValidationOptions(new QueryValidationOptions { ShouldThrow = true }));
        Assert.Throws<QueryValidationException>(() => throwing.ValidateSort<Employee>("nope"));
    }

    [Fact]
    public async Task BuildFilterAsync_WhenCancelled_Throws()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.UseModel(_db.Model);
            c.IncludeResolver = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            };
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await parser.BuildFilterAsync<Employee>("@include:x", cancellationToken: cts.Token));
    }

    [Fact]
    public void Validate_WithValidAndInvalidQueries_ReportsFieldsAndErrors()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).UseFieldMap(new FieldMap { { "who", "Name" } }));

        var valid = parser.ValidateQuery<Employee>("who:John* AND age:>30");
        var invalid = parser.ValidateQuery<Employee>("nope:1 AND age:old AND name:x~");
        var syntax = parser.ValidateQuery<Employee>("name:(john");

        Assert.True(valid.IsValid);
        Assert.Contains("who", valid.ReferencedFields);
        Assert.Contains("Name", valid.ResolvedFields);
        Assert.Contains(QueryOperations.Prefix, valid.Operations.Keys);

        Assert.False(invalid.IsValid);
        Assert.Equal(3, invalid.ValidationErrors.Count);
        Assert.Contains("nope", invalid.UnresolvedFields);

        Assert.False(syntax.IsValid);
    }

    [Fact]
    public void Validate_WithShouldThrow_ThrowsForInvalidQueries()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).UseValidationOptions(new QueryValidationOptions { ShouldThrow = true }));

        Assert.Throws<QueryValidationException>(() => parser.ValidateQuery<Employee>("nope:1"));
        Assert.True(parser.ValidateQuery<Employee>("name:x").IsValid);
    }

    [Fact]
    public void TryBuildFilter_WithInvalidQuery_ReturnsFailureInsteadOfThrowing()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model));

        var syntax = parser.TryBuildFilter<Employee>("name:(john");
        var invalid = parser.TryBuildFilter<Employee>("age:abc");
        var valid = parser.TryBuildFilter<Employee>("age:30");

        Assert.False(syntax.IsSuccess);
        Assert.IsType<QueryParseException>(syntax.Error);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(QueryErrorCode.TypeConversionError, Assert.IsType<QueryValidationException>(invalid.Error).Errors.Single().Code);
        Assert.True(valid.IsSuccess);
        Assert.Equal(["John Doe"], _db.Employees.Where(valid.Value).Names());
    }

    [Fact]
    public void BuildFilter_WithoutModel_ThrowsHelpfulError()
    {
        var parser = new EntityFrameworkQueryParser();

        var ex = Assert.Throws<InvalidOperationException>(() => parser.BuildFilter<Employee>("name:x"));

        Assert.Contains("No EF Core model", ex.Message);
        Assert.Throws<InvalidOperationException>(() => new EntityFrameworkQueryParser(c => c.UseModel(_db.Model)).BuildFilter<SampleData.Unmapped>("x:1"));
    }

    [Fact]
    public void BuildFilter_NonGeneric_ReturnsTypedLambda()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model));

        var filter = parser.BuildFilter(typeof(Employee), "age:30");

        Assert.Equal(typeof(Func<Employee, bool>), filter.Type);
        Assert.Equal(["John Doe"], _db.Employees.Where((System.Linq.Expressions.Expression<Func<Employee, bool>>)filter).Names());
    }

    [Fact]
    public void BuildFilter_WithCustomVisitor_CanReplaceNodePredicate()
    {
        var parser = new EntityFrameworkQueryParser(c => c.AddVisitor(new TeamVisitor()));

        Assert.Equal(["Jane Smith", "John Doe"], _db.Employees.Where("@team:apollo", parser).Names());
        Assert.Equal(["Alice Brown", "Bob Wilson"], _db.Employees.Where("-@team:apollo", parser).Names());
    }

    private sealed class TeamVisitor : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field == "@team" && node.Query is TermNode term)
            {
                string project = term.UnescapedTerm;
                System.Linq.Expressions.Expression<Func<Employee, bool>> filter = e => e.Projects.Any(p => p.Name.ToLower() == project);
                node.SetFilterExpression(filter);
                return node;
            }

            return base.Visit(node, context);
        }
    }

    [Fact]
    public void BuildFilter_ErrorPositions_PointAtOffendingNodes()
    {
        var parser = new EntityFrameworkQueryParser();

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("name:x AND nope:y AND age:abc", parser));

        Assert.Equal([16, 26], ex.Errors.Select(e => e.Position));
    }
}
