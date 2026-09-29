using System.Text.RegularExpressions;
using Foundatio.Lucene.Ast;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests.Parity;

/// <summary>
/// Scenarios ported from Foundatio.Parsers' <c>SqlQueryParserTests</c>. Parsers produced Dynamic LINQ text; these
/// tests compare the SQL our expression produces with the SQL of the equivalent hand-written LINQ. Where the result
/// intentionally differs, the Parsers output is noted.
/// </summary>
public partial class ParsersSqlQueryParserTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 15, 30, 0, TimeSpan.Zero);

    private readonly ParsersSampleContext _db = ParsersSampleContext.CreateOffline();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("field:+term")]
    [InlineData("field:-term")]
    [InlineData("field:!term")]
    [InlineData("field:NOT term")]
    [InlineData("field:+(a OR b)")]
    [InlineData("field:-(a OR b)")]
    [InlineData("field:!(a OR b)")]
    [InlineData("field:NOT (a OR b)")]
    [InlineData("field:+[1 TO 2]")]
    [InlineData("field:-[1 TO 2]")]
    [InlineData("field:![1 TO 2]")]
    [InlineData("field:NOT [1 TO 2]")]
    public void Parse_WithPostColonOperator_ReportsErrorBeforeTheFieldName(string query)
    {
        var parser = ParsersSampleContext.CreateParser();

        var validation = parser.ValidateQuery<WorkItem>(query, Model);
        var ex = Assert.Throws<QueryParseException>(() => parser.BuildFilter<WorkItem>(query, Model));

        var error = Assert.Single(validation.ValidationErrors);
        Assert.Contains("before the field name", error.Message);
        Assert.True(error.Position > 0);
        Assert.Contains("before the field name", ex.Message);
    }

    [Theory]
    [InlineData("+@include:one Id:2", 1)]
    [InlineData("NOT @include:one", 2)]
    [InlineData("-@include:one", 2)]
    [InlineData("!@include:one", 2)]
    [InlineData("NOT @include:not-one", 1)]
    [InlineData("-@include:not-one", 1)]
    [InlineData("!@include:not-one", 1)]
    public void BuildFilter_WithPrefixedInclude_PreservesMatchingItems(string query, int expectedId)
    {
        var parser = new EntityFrameworkQueryParser(c => c
            .SetDefaultOperator(BooleanOperator.Or)
            .UseIncludes(new Dictionary<string, string> { ["one"] = "Id:1", ["not-one"] = "NOT Id:1" }));
        WorkItem[] items = [new() { Id = 1 }, new() { Id = 2 }];

        var filter = parser.BuildFilter<WorkItem>(query, Model);
        string sql = _db.WorkItems.Where(filter).ToSql();

        Assert.Equal(expectedId, Assert.Single(items.AsQueryable().Where(filter)).Id);
        Assert.Contains("WHERE", sql);
    }

    [Theory]
    [InlineData("salary:[1 TO 5]", ">= 1 AND <= 5")]
    [InlineData("salary:{1 TO 5}", "> 1 AND < 5")]
    [InlineData("salary:[1 TO 5}", ">= 1 AND < 5")]
    [InlineData("salary:>5", "> 5")]
    [InlineData("salary:>=5", ">= 5")]
    [InlineData("salary:<5", "< 5")]
    [InlineData("salary:<=5", "<= 5")]
    public void BuildFilter_WithRange_TranslatesComparisons(string query, string comparisons)
    {
        var parser = new EntityFrameworkQueryParser();

        string sql = _db.WorkItems.Where(query, parser).ToSql();

        foreach (string comparison in comparisons.Split(" AND "))
        {
            string[] parts = comparison.Split(' ');
            Assert.Contains($"[w].[Salary] {parts[0]} @", sql);
            Assert.Contains($"int = {parts[1]};", sql);
        }
    }

    [Theory]
    [InlineData("NOT title:Open")]
    [InlineData("!title:Open")]
    [InlineData("-title:Open")]
    public void BuildFilter_WithNegatedFieldComparison_NegatesComparison(string query)
    {
        string open = "Open";

        AssertSameSql(_db.WorkItems.Where(e => !(e.Title == open)), _db.WorkItems.Where(query, new EntityFrameworkQueryParser()));
    }

    [Theory]
    [InlineData("NOT datadefinitions.key:age")]
    [InlineData("!datadefinitions.key:age")]
    [InlineData("-datadefinitions.key:age")]
    public void BuildFilter_WithNegatedCustomField_NegatesCustomExpression(string query)
    {
        var options = new EntityFrameworkQueryOptions
        {
            AdditionalFields = [ParsersSampleContext.DynamicField("datadefinitions.key", typeof(string), 1)]
        };
        int id = 1;
        string age = "age";

        AssertSameSql(
            _db.Employees.Where(e => !e.DataValues.Any(dv => dv.DataDefinitionId == id && dv.StringValue == age)),
            _db.Employees.Where(query, ParsersSampleContext.CreateParser(), options));
    }

    [Theory]
    [InlineData("NOT _exists_:department")]
    [InlineData("!_exists_:department")]
    [InlineData("-_exists_:department")]
    [InlineData("_missing_:department")]
    public void BuildFilter_WithNegatedExistsOrMissing_ChecksForNull(string query)
    {
        AssertSameSql(_db.WorkItems.Where(e => e.Department == null), _db.WorkItems.Where(query, new EntityFrameworkQueryParser()));
    }

    [Theory]
    [InlineData("NOT _missing_:department")]
    [InlineData("!_missing_:department")]
    [InlineData("-_missing_:department")]
    [InlineData("_exists_:department")]
    public void BuildFilter_WithNegatedMissingOrExists_ChecksForValue(string query)
    {
        AssertSameSql(_db.WorkItems.Where(e => e.Department != null), _db.WorkItems.Where(query, new EntityFrameworkQueryParser()));
    }

    [Theory]
    [InlineData("NOT _exists_:Companies.Name", false)]
    [InlineData("!_exists_:Companies.Name", false)]
    [InlineData("-_exists_:Companies.Name", false)]
    [InlineData("_missing_:Companies.Name", false)]
    [InlineData("NOT _missing_:Companies.Name", true)]
    [InlineData("!_missing_:Companies.Name", true)]
    [InlineData("-_missing_:Companies.Name", true)]
    public void BuildFilter_WithExistsOnCollectionField_AppliesExclusionToWholeCollection(string query, bool exists)
    {
        var expected = exists
            ? _db.WorkItems.Where(e => e.Companies.Any(c => c.Name != null))
            : _db.WorkItems.Where(e => !e.Companies.Any(c => c.Name != null));

        AssertSameSql(expected, _db.WorkItems.Where(query, new EntityFrameworkQueryParser()));
    }

    [Fact]
    public void BuildFilter_WithProhibitedOrClause_UsesLuceneOccurrence()
    {
        var parser = new EntityFrameworkQueryParser();
        string open = "Open", closed = "Closed", pending = "Pending", inactive = "Inactive", a = "A", b = "B", c = "C", d = "D";

        // Same as Parsers: NOT is an explicit OR alternative.
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || !(e.Department == closed)), _db.WorkItems.Where("title:Open OR NOT department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || !(e.Department == closed)), _db.WorkItems.Where("title:Open OR !department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || !(e.Department == closed) && e.Status == pending), _db.WorkItems.Where("title:Open OR -department:Closed AND status:Pending", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || e.Status == pending && !(e.Department == closed)), _db.WorkItems.Where("title:Open OR status:Pending AND -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || !(e.Department == closed) && e.Status == pending), _db.WorkItems.Where("title:Open OR (-department:Closed AND status:Pending)", parser));

        // Intentional difference: a - clause is prohibited (Lucene), so it applies to the whole group. Parsers kept
        // it as an OR alternative: "Title = Open OR !(Department = Closed)".
        AssertSameSql(_db.WorkItems.Where(e => !(e.Department == closed) && e.Title == open), _db.WorkItems.Where("title:Open OR -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => !(e.Department == closed) && (e.Title == open || e.Status == pending)), _db.WorkItems.Where("title:Open OR status:Pending OR -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => !(e.Status == inactive) && !(e.Department == closed)), _db.WorkItems.Where("-status:Inactive OR -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => !e.Companies.Any(x => x.Name != null) && e.Title == open), _db.WorkItems.Where("title:Open OR -_exists_:Companies.Name", parser));

        // Intentional difference: AND binds tighter than OR. Parsers grouped to the right:
        // "Title = A OR !(Status = B) AND (Department = C OR Team = D)".
        AssertSameSql(_db.WorkItems.Where(e => e.Title == a || !(e.Status == b) && e.Department == c || e.Team == d), _db.WorkItems.Where("title:A OR -status:B AND department:C OR team:D", parser));
    }

    [Fact]
    public void BuildFilter_WithRequiredClause_RequiresClause()
    {
        var parser = new EntityFrameworkQueryParser();
        string open = "Open", active = "Active", support = "Support", closed = "Closed", pending = "Pending", a = "A", b = "B", c = "C", d = "D";
        int hundred = 100;

        AssertSameSql(_db.WorkItems.Where(e => e.Status == active), _db.WorkItems.Where("+status:Active", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open && e.Status == active), _db.WorkItems.Where("title:Open AND +status:Active", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active), _db.WorkItems.Where("title:Open OR +status:Active", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || e.Status == active && e.Department == support), _db.WorkItems.Where("title:Open OR (+status:Active AND department:Support)", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Department == support), _db.WorkItems.Where("title:Open OR status:Pending OR +department:Support", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active && e.Department == support), _db.WorkItems.Where("+status:Active OR +department:Support", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active && e.Department == support), _db.WorkItems.Where("title:Open OR +status:Active OR +department:Support", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active && !(e.Department == closed)), _db.WorkItems.Where("title:Open OR +status:Active OR -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active && e.Department == null), _db.WorkItems.Where("title:Open OR +status:Active OR -_exists_:department", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active && !(e.Department == closed)), _db.WorkItems.Where("+status:Active AND -department:Closed", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Salary > hundred), _db.WorkItems.Where("title:Open OR +salary:>100", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Department != null), _db.WorkItems.Where("title:Open OR +_exists_:department", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Department == null), _db.WorkItems.Where("title:Open OR +_missing_:department", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Status == active || e.Department == support), _db.WorkItems.Where("title:Open OR +(status:Active OR department:Support)", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open && e.Department == support), _db.WorkItems.Where("title:Open AND (status:Pending OR +department:Support)", parser));

        // Intentional difference: AND binds tighter than OR, so the required clause belongs to the AND group.
        // Parsers: "Status = Active AND Department = Support", "Status = B AND (Department = C OR Team = D)", and
        // "Title = Open AND Status = Active".
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open || e.Status == active && e.Department == support), _db.WorkItems.Where("title:Open OR +status:Active AND department:Support", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == a || e.Status == b && e.Department == c || e.Team == d), _db.WorkItems.Where("title:A OR +status:B AND department:C OR team:D", parser));
        AssertSameSql(_db.WorkItems.Where(e => e.Title == open && e.Status == active || e.Department == support), _db.WorkItems.Where("title:Open AND +status:Active OR department:Support", parser));
        _ = pending;
    }

    [Fact]
    public void BuildFilter_WithRequiredDefaultFieldClause_RequiresClause()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields(["Title"], SearchOperator.Equals));
        string open = "Open";

        AssertSameSql(_db.WorkItems.Where(e => e.Title == open), _db.WorkItems.Where("status:Pending OR +Open", parser));
    }

    [Fact]
    public void BuildFilter_WithRequiredClause_FiltersByRequiredClause()
    {
        var filter = new EntityFrameworkQueryParser().BuildFilter<WorkItem>("title:Open OR +salary:>100", Model).Compile();
        WorkItem[] items = [new() { Title = "Open", Salary = 50 }, new() { Title = "Closed", Salary = 150 }, new() { Title = "Open", Salary = 200 }];

        var results = items.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, item => Assert.True(item.Salary > 100));
    }

    [Fact]
    public void BuildFilter_WithProhibitedOrClause_ExcludesProhibitedMatches()
    {
        var filter = new EntityFrameworkQueryParser().BuildFilter<WorkItem>("title:Open OR -department:Closed", Model).Compile();
        WorkItem[] items = [new() { Title = "Open", Department = "Closed" }, new() { Title = "Open", Department = "Support" }, new() { Title = "Pending", Department = "Support" }];

        // Intentional difference: Parsers matched all three rows ("Title = Open OR !(Department = Closed)").
        Assert.Equal("Support", Assert.Single(items, item => filter(item)).Department);
    }

    [Fact]
    public void BuildFilter_WithNegatedCollectionExists_FiltersWholeCollection()
    {
        var filter = new EntityFrameworkQueryParser().BuildFilter<WorkItem>("-_exists_:Companies.Name", Model).Compile();
        WorkItem[] items =
        [
            new() { Id = 1, Companies = [] },
            new() { Id = 2, Companies = [new WorkCompany { Name = null }] },
            new() { Id = 3, Companies = [new WorkCompany { Name = null }, new WorkCompany { Name = "Acme" }] }
        ];

        Assert.Equal([1, 2], items.Where(filter).Select(item => item.Id));
    }

    [Theory]
    [InlineData("NOT salary:>100")]
    [InlineData("!salary:>100")]
    [InlineData("-salary:>100")]
    public void BuildFilter_WithNegatedOneSidedRange_NegatesComparison(string query)
    {
        int hundred = 100;

        AssertSameSql(_db.WorkItems.Where(e => !(e.Salary > hundred)), _db.WorkItems.Where(query, new EntityFrameworkQueryParser()));
    }

    [Fact]
    public void BuildFilter_WithDefaultFullTextFields_SearchesEachField()
    {
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["FullName", "Title"], SearchOperator.StartsWith));
        string name = "\"John*\"", title = "\"John*\"";

        AssertSameSql(
            _db.Employees.Where(e => EF.Functions.Contains(e.FullName, name) || EF.Functions.Contains(e.Title, title)),
            _db.Employees.Where("John", parser));
    }

    [Fact]
    public void BuildFilter_WithDateFilter_RoundsExclusiveLowerBoundLikeElasticsearch()
    {
        var parser = ParsersSampleContext.CreateParser();
        var nextDay = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // Intentional difference: Parsers produced "Created > 2024-01-01"; a date without a time is the whole day, so
        // "after 2024-01-01" starts on 2024-01-02.
        AssertSameSql(_db.Employees.Where(e => e.Created >= nextDay), _db.Employees.Where("created:>2024-01-01", parser));
    }

    [Fact]
    public void BuildFilter_WithTimeZone_InterpretsLocalDatesInThatZone()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultTimeZone(tokyo));
        var utcNow = new DateTime(2026, 9, 29, 3, 4, 5, 678, DateTimeKind.Utc);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tokyo);

        string sql = _db.Employees.Where($"created:>\"{localNow:yyyy-MM-ddTHH:mm:ss.fff}\"", parser).ToSql();

        Assert.Contains(utcNow.ToString("O"), sql);
    }

    [Fact]
    public void BuildFilter_WithDateOnlyDateMath_ComparesCalendarDates()
    {
        var parser = ParsersSampleContext.CreateParser(c => c.SetTimeProvider(new FixedTimeProvider(Now)));
        var date = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-90);

        AssertSameSql(_db.Employees.Where(e => e.Birthday < date), _db.Employees.Where("birthday:<now-90d", parser));
        AssertSameSql(_db.Employees.Where(e => e.Birthday == date), _db.Employees.Where("birthday:now-90d", parser));
    }

    [Fact]
    public void BuildFilter_WithTimeOnly_ComparesTimes()
    {
        var six = new TimeOnly(6, 0);

        AssertSameSql(_db.Employees.Where(e => e.HappyHour < six), _db.Employees.Where("happyhour:<\"6:00\"", ParsersSampleContext.CreateParser()));
    }

    [Fact]
    public void BuildFilter_WithExistsAndMissing_ChecksForNull()
    {
        var parser = ParsersSampleContext.CreateParser();

        AssertSameSql(_db.Employees.Where(e => e.Title != null), _db.Employees.Where("_exists_:title", parser));
        AssertSameSql(_db.Employees.Where(e => false), _db.Employees.Where("_missing_:salary", parser));
        AssertSameSql(_db.Companies.Where(e => e.Location == null), _db.Companies.Where("_missing_:location", parser));
    }

    [Fact]
    public void BuildFilter_WithDateMath_EvaluatesRelativeToClock()
    {
        var parser = ParsersSampleContext.CreateParser(c => c.SetTimeProvider(new FixedTimeProvider(Now)));
        var since = Now.UtcDateTime.AddDays(-90);

        AssertSameSql(_db.Employees.Where(e => e.Created > since), _db.Employees.Where("created:>now-90d", parser));
    }

    [Fact]
    public void BuildFilter_WithCollectionDefaultFields_UsesAny()
    {
        var fullText = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["Companies.Name"], SearchOperator.StartsWith));
        var nested = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["Companies.DataDefinitions.Key"], SearchOperator.StartsWith));
        string acme = "\"acme*\"", age = "age";

        AssertSameSql(_db.Employees.Where(e => e.Companies.Any(c => EF.Functions.Contains(c.Name, acme))), _db.Employees.Where("acme", fullText));
        AssertSameSql(_db.Employees.Where(e => e.Companies.Any(c => c.DataDefinitions.Any(d => d.Key.StartsWith(age)))), _db.Employees.Where("age", nested));
    }

    [Fact]
    public void BuildFilter_WithReferenceNavigationFullTextDefaultFields_SearchesEachField()
    {
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["CurrentCompany.Name", "CurrentCompany.Location"], SearchOperator.StartsWith));
        string name = "\"acme*\"", location = "\"acme*\"";

        AssertSameSql(
            _db.Employees.Where(e => EF.Functions.Contains(e.CurrentCompany!.Name, name) || EF.Functions.Contains(e.CurrentCompany!.Location!, location)),
            _db.Employees.Where("acme", parser));
    }

    [Fact]
    public void BuildFilter_WithNavigationAndSkipNavigationFields_UsesAny()
    {
        var parser = ParsersSampleContext.CreateParser();
        string age = "age";
        int salary = 80_000;

        AssertSameSql(_db.Companies.Where(e => e.DataDefinitions.Any(c => c.Key == age)), _db.Companies.Where("datadefinitions.key:age", parser));
        AssertSameSql(_db.Companies.Where(e => e.Employees.Any(c => c.Salary == salary)), _db.Companies.Where("employees.salary:80000", parser));
    }

    [Fact]
    public void BuildFilter_WithContainsDefaultOperatorOnFullTextField_UsesPrefixTerm()
    {
        string condition = "\"john*\"";

        // Intentional difference: Parsers produced "\"*john*\"", but SQL Server full-text search only supports
        // trailing wildcards, so a leading * never matched anything more than the prefix term.
        AssertSameSql(_db.Employees.Where(e => EF.Functions.Contains(e.FullName, condition)), _db.Employees.Where("john", ParsersSampleContext.CreateParser()));
    }

    [Fact]
    public void BuildFilter_WithCustomFieldAndNavigation_CombinesExpressions()
    {
        var parser = ParsersSampleContext.CreateParser();
        var options = new EntityFrameworkQueryOptions { AdditionalFields = [ParsersSampleContext.DynamicField("age", typeof(decimal), 1)] };
        string acme = "\"acme\"";
        int id = 1;
        decimal? thirty = 30;

        // Intentional difference: an explicit-field term on a full-text field uses CONTAINS (Parsers used equality).
        AssertSameSql(
            _db.Employees.Where(e => e.Companies.Any(c => EF.Functions.Contains(c.Name, acme)) && e.DataValues.Any(dv => dv.DataDefinitionId == id && dv.NumberValue == thirty)),
            _db.Employees.Where("companies.name:acme age:30", parser, options));
        AssertSameSql(
            _db.Employees.AsNoTracking().Where(e => e.Companies.Any(c => EF.Functions.Contains(c.Name, acme)) && e.DataValues.Any(dv => dv.DataDefinitionId == id && dv.NumberValue == thirty)),
            _db.Employees.AsNoTracking().Where("companies.name:acme age:30", parser, options));
        Assert.Throws<QueryValidationException>(() => _db.Employees.Where("companies.description:acme", parser, options));
    }

    private EntityFrameworkQueryOptions Model => new() { Model = _db.Model };

    internal static void AssertSameSql<T>(IQueryable<T> expected, IQueryable<T> actual)
    {
        Assert.Equal(Normalize(expected.ToSql()), Normalize(actual.ToSql()));
    }

    private static string Normalize(string sql) => ParameterName().Replace(sql, "@p");

    [GeneratedRegex(@"@\w+")]
    private static partial Regex ParameterName();
}
