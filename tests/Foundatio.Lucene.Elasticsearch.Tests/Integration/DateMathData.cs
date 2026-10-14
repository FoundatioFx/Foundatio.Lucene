using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Documents at day, month, hour, and year boundaries for the date math tests.
/// </summary>
public static class DateMathData
{
    public const string Index = "datemath";
    public const string RealWorldIndex = "datemath-realworld";

    public static TypeMapping Mapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "label", new KeywordProperty() },
            { "timestamp", new DateProperty() }
        }
    };

    public static Task SeedAsync(ElasticsearchFixture fixture)
    {
        Document[] boundaries =
        [
            new("A", "start-of-day", new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc)),
            new("B", "noon", new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)),
            new("C", "end-of-day", new DateTime(2024, 1, 15, 23, 59, 59, DateTimeKind.Utc)),
            new("D", "start-of-next-day", new DateTime(2024, 1, 16, 0, 0, 0, DateTimeKind.Utc)),
            new("E", "end-of-prev-day", new DateTime(2024, 1, 14, 23, 59, 59, DateTimeKind.Utc)),
            new("F", "start-of-feb", new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("G", "end-of-jan", new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc))
        ];

        // January 2024 ISO weeks start on Mondays: Jan 1, 8, 15, 22, 29.
        Document[] realWorld =
        [
            new("2023-mid", "mid-2023", new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc)),
            new("2023-end", "end-of-2023", new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc)),
            new("jan-01", "start-of-jan", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("jan-07-sun", "jan-week1-sun", new DateTime(2024, 1, 7, 14, 0, 0, DateTimeKind.Utc)),
            new("jan-08-mon", "jan-week2-mon", new DateTime(2024, 1, 8, 9, 0, 0, DateTimeKind.Utc)),
            new("jan-10-wed", "jan-week2-wed", new DateTime(2024, 1, 10, 15, 30, 0, DateTimeKind.Utc)),
            new("jan-14-sun", "jan-week2-sun", new DateTime(2024, 1, 14, 18, 0, 0, DateTimeKind.Utc)),
            new("jan-15-mon", "jan-week3-mon", new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc)),
            new("jan-21-sun", "jan-week3-sun", new DateTime(2024, 1, 21, 20, 0, 0, DateTimeKind.Utc)),
            new("jan-31", "end-of-jan", new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc)),
            new("feb-01", "start-of-feb", new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("feb-14", "mid-feb", new DateTime(2024, 2, 14, 14, 0, 0, DateTimeKind.Utc)),
            new("feb-29", "end-of-feb", new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc)),
            new("mar-01", "start-of-mar", new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("mar-15", "mid-mar", new DateTime(2024, 3, 15, 12, 0, 0, DateTimeKind.Utc)),
            new("jun-15", "mid-jun", new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)),
            new("sep-30", "end-of-sep", new DateTime(2024, 9, 30, 23, 59, 59, DateTimeKind.Utc)),
            new("dec-31", "end-of-2024", new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc)),
            new("2025-start", "start-of-2025", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("2025-mid", "mid-2025", new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc)),
            new("2025-end", "end-of-2025", new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc))
        ];

        return Task.WhenAll(
            fixture.CreateIndexAsync(Index, Mapping, boundaries),
            fixture.CreateIndexAsync(RealWorldIndex, Mapping, realWorld));
    }

    public sealed record Document(string Id, string Label, DateTime Timestamp);
}
