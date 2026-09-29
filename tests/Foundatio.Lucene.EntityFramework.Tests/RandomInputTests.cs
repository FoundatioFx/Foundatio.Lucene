using System.Text;
using Foundatio.Lucene.Ast;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Builds randomly generated queries and sorts, translates them to SQL Server, and runs them in memory. Invalid
/// input must be reported with a <see cref="QueryException"/>; anything else, including a filter EF Core can't
/// translate, is a bug.
/// </summary>
public class RandomInputTests : IDisposable
{
    private static readonly string[] EmployeeFields =
    [
        "name", "email", "title", "salary", "age", "hiredate", "isactive", "status", "level", "skills", "company",
        "company.name", "company.foundedyear", "company.owner.username", "department.budget", "department.name",
        "manager.name", "manager.age", "projects.name", "reports.age", "address.city", "passwordhash", "unknown"
    ];

    private static readonly string[] TypeSampleFields =
    [
        "text", "int", "nullableint", "long", "short", "byte", "decimal", "double", "float", "bool", "nullablebool",
        "guid", "char", "enum", "nullableenum", "datetime", "nullabledatetime", "datetimeoffset", "dateonly", "timeonly",
        "timespan", "bytes", "unknown"
    ];

    private static readonly string[] Values =
    [
        "john", "jo*", "*oh*", "j?hn", "\"john doe\"", "\"john doe\"~2", "john~", "john~1", "/jo.*/", "42", "-5",
        "3.14", "1e10", "999999999999999999999", "abc", "true", "false", "Active", "1", "11111111-1111-1111-1111-111111111111",
        "a", "2024-01-01", "2024-01", "2024-01-01T10:00:00Z", "now", "now-1d/d", "2024-01-01||+1M/d", "09:00", "01:00:00",
        "*", "[1 TO 5]", "{a TO *]", "[2024-01-01 TO now]^\"Europe/London\"", "[now/d TO *]^-5h", "[09:00 TO 18:00]",
        ">5", ">=2024-01-01", "<now", "<=abc", "john^2", "(a OR b)", "(x -y)", "\"\"", "\\*", "100%", "a_b", "[x]"
    ];

    private readonly SampleContext _memory = SampleData.CreateInMemory();
    private readonly SampleContext _sqlServer = SampleData.CreateOfflineSqlServer();

    public void Dispose()
    {
        _memory.Dispose();
        _sqlServer.Dispose();
    }

    [Fact]
    public void Where_WithRandomEmployeeQueries_ThrowsOnlyQueryExceptions()
    {
        var parsers = new[]
        {
            new EntityFrameworkQueryParser(),
            new EntityFrameworkQueryParser(c => c.DefaultFields = ["Name", "Email", "Age", "Company.Name"]),
            new EntityFrameworkQueryParser(c =>
            {
                c.DefaultOperator = BooleanOperator.Or;
                c.DefaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
            })
        };

        int built = RunQueries(new Random(20240201), EmployeeFields, parsers, (query, parser) =>
        {
            _sqlServer.Employees.Where(query, parser).ToSql();
            if (CanRunInMemory(query))
                _memory.Employees.Where(query, parser).ToList();
        });

        Assert.InRange(built, 1_200, 6_000);
    }

    [Fact]
    public void Where_WithRandomTypedQueries_ThrowsOnlyQueryExceptions()
    {
        var parsers = new[]
        {
            new EntityFrameworkQueryParser(),
            new EntityFrameworkQueryParser(c => c.DefaultFields = ["Text", "Int", "DateTime", "Guid", "Enum"])
        };

        int built = RunQueries(new Random(20240202), TypeSampleFields, parsers, (query, parser) =>
        {
            _sqlServer.TypeSamples.Where(query, parser).ToSql();
            _memory.TypeSamples.Where(query, parser).ToList();
        });

        Assert.InRange(built, 400, 4_000);
    }

    [Fact]
    public void OrderBy_WithRandomSorts_ThrowsOnlyQueryExceptions()
    {
        var random = new Random(20240203);
        var parser = new EntityFrameworkQueryParser();
        string[] prefixes = ["", "", "-", "+"];
        string[] suffixes = ["", "", ":asc", ":desc", ":x", "^2"];
        string[] extras = ["-(name age)", "(", ")", "\"quoted\"", "_score"];

        int built = 0;
        for (int i = 0; i < 5_000; i++)
        {
            var parts = new List<string>();
            int count = random.Next(1, 5);
            for (int j = 0; j < count; j++)
            {
                parts.Add(random.Next(6) == 0
                    ? Pick(random, extras)
                    : Pick(random, prefixes) + Pick(random, EmployeeFields) + Pick(random, suffixes));
            }

            string sort = string.Join(' ', parts);
            bool success = BuildsOrThrowsQueryException(sort, () =>
            {
                _sqlServer.Employees.OrderBy(sort, parser).ToSql();
                if (CanRunInMemory(sort))
                    _memory.Employees.OrderBy(sort, parser).ToList();
            });

            if (success)
                built++;
        }

        Assert.InRange(built, 500, 5_000);
    }

    /// <summary>
    /// The in-memory provider can't filter primitive collections (<c>skills</c>) or sort by complex type properties
    /// (<c>address.city</c>) after another key, even with hand-written LINQ; SQL Server translation still covers them.
    /// </summary>
    private static bool CanRunInMemory(string input)
    {
        return !input.Contains("skills", StringComparison.Ordinal) && !input.Contains("address", StringComparison.Ordinal);
    }

    private static int RunQueries(Random random, string[] fields, EntityFrameworkQueryParser[] parsers, Action<string, EntityFrameworkQueryParser> run)
    {
        int built = 0;
        for (int i = 0; i < 2_000; i++)
        {
            string query = RandomQuery(random, fields);
            foreach (var parser in parsers)
            {
                if (BuildsOrThrowsQueryException(query, () => run(query, parser)))
                    built++;
            }
        }

        return built;
    }

    private static bool BuildsOrThrowsQueryException(string input, Action build)
    {
        try
        {
            build();
            return true;
        }
        catch (QueryException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Assert.Fail($"'{input}' threw {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string RandomQuery(Random random, string[] fields)
    {
        string[] prefixes = ["", "", "", "+", "-", "NOT ", "!"];
        string[] operators = [" ", " ", " AND ", " OR ", " && ", " || "];

        var builder = new StringBuilder();
        int clauses = random.Next(1, 6);
        for (int i = 0; i < clauses; i++)
        {
            if (i > 0)
                builder.Append(Pick(random, operators));

            builder.Append(Pick(random, prefixes));
            builder.Append(random.Next(10) switch
            {
                0 => $"_exists_:{Pick(random, fields)}",
                1 => $"_missing_:{Pick(random, fields)}",
                2 => Pick(random, Values),
                3 => "*:*",
                4 => $"{Pick(random, fields)}:({Pick(random, Values)} {Pick(random, Values)})",
                5 => $"({Pick(random, fields)}:{Pick(random, Values)} OR {Pick(random, fields)}:{Pick(random, Values)})",
                _ => $"{Pick(random, fields)}:{Pick(random, Values)}"
            });
        }

        return builder.ToString();
    }

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];
}
