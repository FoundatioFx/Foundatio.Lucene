using System.Globalization;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Visitors;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Common relative date ranges ("yesterday", "last week", "this month", "year to date", ...). Anchored cases evaluate
/// <c>now</c> with a fake clock through <see cref="DateMathEvaluatorVisitor"/> and compare with Elasticsearch
/// evaluating the same expressions anchored to that instant (<c>2024-02-15||-1d/d</c>). Live cases compare the
/// parser's pass-through and evaluated forms with Elasticsearch's own clock.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class DateMathRealWorldIntegrationTests(ElasticsearchFixture fixture)
{
    public static IEnumerable<TheoryDataRow<string, string, string, string, string?>> AnchoredCases()
    {
        (string Name, string Now, string Query, string Present, string? Count)[] cases =
        [
            ("yesterday", "2024-02-15T12:00:00Z", "[now-1d/d TO now-1d/d]", "feb-14", "1"),
            ("yesterday-exclusive-upper", "2024-02-15T12:00:00Z", "[now-1d/d TO now/d}", "feb-14", "1"),
            ("last-week", "2024-01-15T12:00:00Z", "[now-1w/w TO now-1w/w]", "jan-08-mon,jan-10-wed,jan-14-sun", "3"),
            ("last-week-exclusive-upper", "2024-01-15T12:00:00Z", "[now-1w/w TO now/w}", "jan-08-mon,jan-10-wed,jan-14-sun", "3"),
            ("this-week", "2024-01-15T12:00:00Z", "[now/w TO now/w]", "jan-15-mon,jan-21-sun", "2"),
            ("this-week-mid-week", "2024-01-10T12:00:00Z", "[now/w TO now/w]", "jan-08-mon,jan-10-wed,jan-14-sun", "3"),
            ("last-month", "2024-02-15T12:00:00Z", "[now-1M/M TO now-1M/M]", "jan-01,jan-07-sun,jan-08-mon,jan-10-wed,jan-14-sun,jan-15-mon,jan-21-sun,jan-31", "8"),
            ("last-month-exclusive-upper", "2024-02-15T12:00:00Z", "[now-1M/M TO now/M}", "jan-01,jan-31", "8"),
            ("last-month-leap-february", "2024-03-15T12:00:00Z", "[now-1M/M TO now-1M/M]", "feb-01,feb-14,feb-29", "3"),
            ("this-month", "2024-02-15T12:00:00Z", "[now/M TO now/M]", "feb-01,feb-14,feb-29", "3"),
            ("this-month-january", "2024-01-10T12:00:00Z", "[now/M TO now/M]", "jan-01,jan-31", "8"),
            ("last-year", "2024-06-15T12:00:00Z", "[now-1y/y TO now-1y/y]", "2023-mid,2023-end", "2"),
            ("last-year-exclusive-upper", "2024-06-15T12:00:00Z", "[now-1y/y TO now/y}", "2023-mid,2023-end", "2"),
            ("this-year", "2024-06-15T12:00:00Z", "[now/y TO now/y]", "jan-01,mar-15,jun-15,sep-30,dec-31", "16"),
            ("this-year-2025", "2025-06-15T12:00:00Z", "[now/y TO now/y]", "2025-start,2025-mid,2025-end", "3"),
            ("year-to-date", "2024-03-15T12:00:00Z", "[now/y TO now/d]", "jan-01,jan-31,feb-01,feb-29,mar-01,mar-15", "13"),
            ("year-to-date-month", "2024-02-14T12:00:00Z", "[now/y TO now/M]", "jan-01,jan-31,feb-01,feb-14,feb-29", "11"),
            ("last-two-years", "2025-06-15T12:00:00Z", "[now-2y/y TO now-1y/y]", "2023-mid,2023-end,jan-01,dec-31", "18"),
            ("cross-month", "2024-01-15T12:00:00Z", "[now-1M/M TO now+1M/M]", "2023-end,jan-01,jan-31,feb-01,feb-29", null),
            ("since-yesterday", "2024-02-15T12:00:00Z", ">=now-1d/d", "feb-14", null),
            ("before-this-year", "2024-06-15T12:00:00Z", "<now/y", "2023-mid,2023-end", "2")
        ];

        foreach (var testCase in cases)
            yield return new(testCase.Name, testCase.Now, testCase.Query, testCase.Present, testCase.Count);
    }

    [Theory]
    [MemberData(nameof(AnchoredCases))]
    public async Task BuildQuery_WithFakeClockAndEvaluatedDateMath_MatchesElasticsearchAnchoredDateMath(string name, string now, string range, string present, string? count)
    {
        var anchor = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture);
        string anchored = range.Replace("now", anchor.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + "||", StringComparison.Ordinal);
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(DateMathData.Mapping);
            c.TimeProvider = new FakeTimeProvider(anchor);
            c.AddVisitor(new DateMathEvaluatorVisitor(isDateField: f => f == "timestamp"));
        });

        var evaluated = await GetIdsAsync(parser.BuildQuery($"timestamp:{range}"));
        var native = await GetIdsAsync(ToNativeRange(anchored));
        var reference = await GetIdsAsync(new QueryStringQuery(ToQueryString($"timestamp:{anchored}")));

        Assert.True(native == evaluated, $"{name}: evaluated '{evaluated}' but Elasticsearch matched '{native}'.");
        Assert.Equal(native, reference);
        var ids = evaluated.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(present.Split(','), id => Assert.Contains(id, ids));
        if (count is not null)
            Assert.Equal(int.Parse(count, CultureInfo.InvariantCulture), ids.Length);
    }

    [Theory]
    [InlineData("[now-1d/d TO now-1d/d]")]
    [InlineData("[now-1w/w TO now-1w/w]")]
    [InlineData("[now/w TO now/w]")]
    [InlineData("[now-1M/M TO now-1M/M]")]
    [InlineData("[now-1M/M TO now/M}")]
    [InlineData("[now/M TO now/M]")]
    [InlineData("[now-1y/y TO now-1y/y]")]
    [InlineData("[now/y TO now/y]")]
    [InlineData("[now/y TO now/d]")]
    [InlineData("[now-30d/d TO now/d]")]
    [InlineData("[now-90d/d TO now/d]")]
    [InlineData("[now-12M/M TO now/M]")]
    [InlineData("[now-3y/y TO now]")]
    public async Task BuildQuery_WithLiveClock_MatchesElasticsearchDateMath(string range)
    {
        var passThrough = new ElasticsearchQueryParser(c => c.UseMappings(DateMathData.Mapping));
        var evaluating = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(DateMathData.Mapping);
            c.AddVisitor(new DateMathEvaluatorVisitor(isDateField: f => f == "timestamp"));
        });

        var native = await GetIdsAsync(ToNativeRange(range));
        var passedThrough = await GetIdsAsync(passThrough.BuildQuery($"timestamp:{range}"));
        var evaluated = await GetIdsAsync(evaluating.BuildQuery($"timestamp:{range}"));
        var reference = await GetIdsAsync(new QueryStringQuery($"timestamp:{range}"));

        Assert.Equal(native, passedThrough);
        Assert.Equal(native, evaluated);
        Assert.Equal(native, reference);
    }

    private static DateRangeQuery ToNativeRange(string range)
    {
        if (range[0] is '>' or '<')
        {
            string op = range[1] == '=' ? range[..2] : range[..1];
            string value = range[op.Length..];
            return op switch
            {
                ">" => DateMathIntegrationTests.CreateRange(null, value, null, null),
                ">=" => DateMathIntegrationTests.CreateRange(value, null, null, null),
                "<" => DateMathIntegrationTests.CreateRange(null, null, null, value),
                _ => DateMathIntegrationTests.CreateRange(null, null, value, null)
            };
        }

        string[] bounds = range[1..^1].Split(" TO ");
        bool minInclusive = range[0] == '[';
        bool maxInclusive = range[^1] == ']';
        string? min = bounds[0] == "*" ? null : bounds[0];
        string? max = bounds[1] == "*" ? null : bounds[1];
        return DateMathIntegrationTests.CreateRange(
            minInclusive ? min : null, minInclusive ? null : min,
            maxInclusive ? max : null, maxInclusive ? null : max);
    }

    private static string ToQueryString(string query)
    {
        int colon = query.IndexOf(':');
        string value = query[(colon + 1)..];
        return value[0] is '>' or '<'
            ? query[..(colon + 1)] + value.Replace(":", "\\:", StringComparison.Ordinal).Replace("/", "\\/", StringComparison.Ordinal)
            : query;
    }

    private async Task<string> GetIdsAsync(Query query)
    {
        string? ids = await fixture.GetMatchingIdsAsync(DateMathData.RealWorldIndex, new BoolQuery { Filter = [query] }, TestContext.Current.CancellationToken);
        Assert.NotNull(ids);
        return ids;
    }
}
