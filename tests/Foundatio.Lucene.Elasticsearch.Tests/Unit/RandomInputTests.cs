using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

/// <summary>
/// Builds randomly generated queries, aggregations, and sorts against the shared mapping. Invalid input must be
/// reported with a <see cref="QueryException"/>; anything else (or a request the client can't serialize) is a bug.
/// </summary>
public class RandomInputTests
{
    private static readonly string[] Fields =
    [
        "text", "text2", "plaintext", "keyword", "multiWord", "number", "long", "double", "float", "bool", "date",
        "dateNanos", "geo", "alias", "textAlias", "dateAlias", "obj.name", "obj.desc", "children.name", "children.text",
        "children.num", "children.grand.name", "children.toys.name", "resellers.price", "children", "unknown", "_id"
    ];

    private static readonly string[] Values =
    [
        "hello", "hel*", "*llo", "h?llo", "\"hello world\"", "\"hello world\"~2", "hello~", "hello~1", "hello~AUTO",
        "/hel+o/", "42", "-5", "3.14", "abc", "true", "false", "2024-01-01", "2024-01", "2024", "2024-01-01T10:00:00Z",
        "now", "now-1d/d", "2024-01-01||+1M/d", "\"51.5,-0.12\"~5km", "\"London\"", "*", "[1 TO 5]", "{a TO *]",
        "[2024-01-01 TO now]^\"Europe/London\"", "[now/d TO *]^-5h", "[51.5,-0.12 TO 50.0,1.0]", ">5", ">=2024-01-01",
        "<now", "<=abc", "hello^2", "(a OR b)", "(x -y)", "\"\"", "\\*"
    ];

    private static readonly string[] Aggregations =
    [
        "min", "max", "avg", "sum", "stats", "exstats", "cardinality", "missing", "percentiles", "histogram", "date",
        "geogrid", "terms", "tophits"
    ];

    private static readonly string[] AggregationModifiers =
    [
        "", "", "~10", "~1d", "~1M", "~-1", "~abc", "~50,95,99.9", "~0", "~13", "^2", "^\"America/Chicago\"", "^-5h", "^x"
    ];

    private static readonly string[] GroupModifiers =
    [
        "@include:a", "@include:/a.*/", "@exclude:b", "@missing:x", "@missing:2024-01-01", "@min:2", "@offset:6h",
        "-@offset:6h", "@offset:abc", "@include:keyword"
    ];

    [Fact]
    public void BuildQuery_WithRandomQueries_ThrowsOnlyQueryExceptions()
    {
        var random = new Random(20240101);
        var parsers = new[]
        {
            TestMapping.CreateParser(),
            TestMapping.CreateParser(c => c.DefaultFields = ["text", "keyword", "number", "date", "bool", "children.name"]),
            TestMapping.CreateScoringParser(c =>
            {
                c.DefaultTimeZone = "America/Chicago";
                c.DefaultFields = ["text", "plaintext"];
            }),
            TestMapping.CreateParser(c => c.UseNested = false)
        };

        int built = 0;
        for (int i = 0; i < 20_000; i++)
        {
            string query = RandomQuery(random);
            foreach (var parser in parsers)
            {
                if (BuildsOrThrowsQueryException(query, () => ElasticAssert.Serialize(parser.BuildQuery(query))))
                    built++;
            }
        }

        Assert.InRange(built, 20_000, 80_000);
    }

    [Fact]
    public void BuildAggregations_WithRandomAggregations_ThrowsOnlyQueryExceptions()
    {
        var random = new Random(20240102);
        var parser = TestMapping.CreateParser(c => c.DefaultTimeZone = "America/Chicago");

        int built = 0;
        for (int i = 0; i < 20_000; i++)
        {
            string aggregations = RandomAggregations(random, depth: 0);
            bool success = BuildsOrThrowsQueryException(aggregations, () =>
            {
                foreach (var aggregation in parser.BuildAggregations(aggregations).Values)
                    ElasticAssert.Serialize(aggregation);
            });

            if (success)
                built++;
        }

        Assert.InRange(built, 2_000, 20_000);
    }

    [Fact]
    public void BuildSort_WithRandomSorts_ThrowsOnlyQueryExceptions()
    {
        var random = new Random(20240103);
        var parser = TestMapping.CreateParser();
        string[] prefixes = ["", "", "-", "+"];
        string[] suffixes = ["", "", ":asc", ":desc", ":x", "^2", "~1"];
        string[] extras = ["_score", "_doc", "-(text keyword)", "(", ")", "field^x", "\"quoted\""];
        int built = 0;

        for (int i = 0; i < 20_000; i++)
        {
            var parts = new List<string>();
            int count = random.Next(1, 5);
            for (int j = 0; j < count; j++)
            {
                parts.Add(random.Next(6) == 0
                    ? Pick(random, extras)
                    : Pick(random, prefixes) + Pick(random, Fields) + Pick(random, suffixes));
            }

            string sort = string.Join(' ', parts);
            bool success = BuildsOrThrowsQueryException(sort, () =>
            {
                foreach (var option in parser.BuildSort(sort))
                    ElasticAssert.Serialize(option);
            });

            if (success)
                built++;
        }

        Assert.InRange(built, 2_000, 20_000);
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

    private static string RandomQuery(Random random)
    {
        string[] prefixes = ["", "", "", "+", "-", "NOT ", "!"];
        string[] operators = [" ", " ", " AND ", " OR ", " && ", " || "];

        var builder = new System.Text.StringBuilder();
        int clauses = random.Next(1, 6);
        for (int i = 0; i < clauses; i++)
        {
            if (i > 0)
                builder.Append(Pick(random, operators));

            builder.Append(Pick(random, prefixes));
            builder.Append(random.Next(10) switch
            {
                0 => $"_exists_:{Pick(random, Fields)}",
                1 => $"_missing_:{Pick(random, Fields)}",
                2 => Pick(random, Values),
                3 => "*:*",
                4 => $"{Pick(random, Fields)}:({Pick(random, Values)} {Pick(random, Values)})",
                5 => $"({Pick(random, Fields)}:{Pick(random, Values)} OR {Pick(random, Fields)}:{Pick(random, Values)})",
                _ => $"{Pick(random, Fields)}:{Pick(random, Values)}"
            });
        }

        return builder.ToString();
    }

    private static string RandomAggregations(Random random, int depth)
    {
        var parts = new List<string>();
        int count = random.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            string type = Pick(random, Aggregations);
            if (depth < 2 && random.Next(3) == 0)
            {
                string inner = RandomAggregations(random, depth + 1);
                string modifiers = random.Next(2) == 0 ? Pick(random, GroupModifiers) + " " : "";
                parts.Add($"{type}:({Pick(random, Fields)}{Pick(random, AggregationModifiers)} {modifiers}{inner}){Pick(random, AggregationModifiers)}");
            }
            else
            {
                parts.Add($"{type}:{Pick(random, Fields)}{Pick(random, AggregationModifiers)}");
            }
        }

        return string.Join(' ', parts);
    }

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];
}
