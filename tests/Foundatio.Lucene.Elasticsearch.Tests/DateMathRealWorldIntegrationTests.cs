using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Elasticsearch.Tests;

/// <summary>
/// Integration tests for common real-world date range patterns: "yesterday", "last week",
/// "this week", "last month", "this month", "last year", "this year", and "year to date".
///
/// Uses the same triple-verification approach as DateMathIntegrationTests:
/// 1. Native DateRangeQuery (ES handles date math natively via gt/gte/lt/lte with anchored dates)
/// 2. Library's ElasticsearchQueryParser with FakeTimeProvider (pre-evaluates 'now' to the same anchor)
/// 3. ES QueryStringQuery (ES parses Lucene syntax and handles date math natively with anchored dates)
/// All three must return identical document sets, proving the library's 'now' handling matches ES exactly.
///
/// Test data spans 2023–2025 with documents at month boundaries, week boundaries,
/// and mid-period to thoroughly exercise all rounding units.
/// </summary>
[Collection("Elasticsearch")]
public class DateMathRealWorldIntegrationTests : IAsyncLifetime
{
    private readonly ElasticsearchFixture _fixture;
    private const string IndexName = "test-datemath-realworld";

    public DateMathRealWorldIntegrationTests(ElasticsearchFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.Client.Indices.DeleteAsync(IndexName);

        var createResponse = await _fixture.Client.Indices.CreateAsync<DateMathDocument>(IndexName, c => c
            .Mappings(m => m
                .Properties(p => p
                    .Date(d => d.Timestamp)
                    .Keyword(d => d.Label)
                )
            )
        );

        if (!createResponse.IsValidResponse)
            throw new InvalidOperationException($"Failed to create index: {createResponse.DebugInformation}");

        // Data spread across 2023–2025 for real-world date range pattern testing.
        // All timestamps are UTC.
        //
        // January 2024 ISO week calendar (weeks start Monday):
        //   Week 1:  Mon Jan 1  – Sun Jan 7
        //   Week 2:  Mon Jan 8  – Sun Jan 14
        //   Week 3:  Mon Jan 15 – Sun Jan 21
        //   Week 4:  Mon Jan 22 – Sun Jan 28
        //   Week 5:  Mon Jan 29 – Wed Jan 31
        var documents = new List<DateMathDocument>
        {
            // ── 2023 ──
            new() { Id = "2023-mid",     Label = "mid-2023",        Timestamp = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc) },
            new() { Id = "2023-end",     Label = "end-of-2023",     Timestamp = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc) },

            // ── January 2024 ──
            new() { Id = "jan-01",       Label = "start-of-jan",    Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "jan-07-sun",   Label = "jan-week1-sun",   Timestamp = new DateTime(2024, 1, 7, 14, 0, 0, DateTimeKind.Utc) },
            new() { Id = "jan-08-mon",   Label = "jan-week2-mon",   Timestamp = new DateTime(2024, 1, 8, 9, 0, 0, DateTimeKind.Utc) },
            new() { Id = "jan-10-wed",   Label = "jan-week2-wed",   Timestamp = new DateTime(2024, 1, 10, 15, 30, 0, DateTimeKind.Utc) },
            new() { Id = "jan-14-sun",   Label = "jan-week2-sun",   Timestamp = new DateTime(2024, 1, 14, 18, 0, 0, DateTimeKind.Utc) },
            new() { Id = "jan-15-mon",   Label = "jan-week3-mon",   Timestamp = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc) },
            new() { Id = "jan-21-sun",   Label = "jan-week3-sun",   Timestamp = new DateTime(2024, 1, 21, 20, 0, 0, DateTimeKind.Utc) },
            new() { Id = "jan-31",       Label = "end-of-jan",      Timestamp = new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc) },

            // ── February 2024 (leap year!) ──
            new() { Id = "feb-01",       Label = "start-of-feb",    Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "feb-14",       Label = "mid-feb",         Timestamp = new DateTime(2024, 2, 14, 14, 0, 0, DateTimeKind.Utc) },
            new() { Id = "feb-29",       Label = "end-of-feb",      Timestamp = new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc) },

            // ── March 2024 ──
            new() { Id = "mar-01",       Label = "start-of-mar",    Timestamp = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "mar-15",       Label = "mid-mar",         Timestamp = new DateTime(2024, 3, 15, 12, 0, 0, DateTimeKind.Utc) },

            // ── Later 2024 ──
            new() { Id = "jun-15",       Label = "mid-jun",         Timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc) },
            new() { Id = "sep-30",       Label = "end-of-sep",      Timestamp = new DateTime(2024, 9, 30, 23, 59, 59, DateTimeKind.Utc) },
            new() { Id = "dec-31",       Label = "end-of-2024",     Timestamp = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc) },

            // ── 2025 ──
            new() { Id = "2025-start",   Label = "start-of-2025",   Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "2025-mid",     Label = "mid-2025",        Timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc) },
            new() { Id = "2025-end",     Label = "end-of-2025",     Timestamp = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc) },
        };

        var bulkResponse = await _fixture.Client.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(documents)
            .Refresh(Refresh.True)
        );

        if (!bulkResponse.IsValidResponse)
            throw new InvalidOperationException($"Failed to index documents: {bulkResponse.DebugInformation}");
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.Client.Indices.DeleteAsync(IndexName);
    }

    #region Helpers

    /// <summary>
    /// Creates a parser with a FakeTimeProvider so that 'now' resolves to the given anchor time.
    /// </summary>
    private ElasticsearchQueryParser CreateParser(DateTimeOffset fakeNow)
    {
        var timeProvider = new FakeTimeProvider(fakeNow);
        return new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = false;
            c.UseDateFields(f => f == "timestamp");
            c.TimeProvider = timeProvider;
        });
    }

    /// <summary>
    /// Creates a parser using the real system clock (for live 'now' equivalence tests).
    /// </summary>
    private ElasticsearchQueryParser CreateParser()
    {
        return new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = false;
            c.UseDateFields(f => f == "timestamp");
        });
    }

    private async Task<List<string>> QueryNativeDateRange(
        string? gte = null, string? gt = null,
        string? lte = null, string? lt = null)
    {
        var dateRange = new DateRangeQuery((Field)"timestamp");
        if (gte is not null) dateRange.Gte = gte;
        if (gt is not null) dateRange.Gt = gt;
        if (lte is not null) dateRange.Lte = lte;
        if (lt is not null) dateRange.Lt = lt;

        var response = await _fixture.Client.SearchAsync<DateMathDocument>(s => s
            .Indices(IndexName)
            .Size(100)
            .Query(new BoolQuery { Filter = [dateRange] }),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    private async Task<List<string>> QueryWithParser(string luceneQuery, DateTimeOffset? fakeNow = null)
    {
        var parser = fakeNow.HasValue ? CreateParser(fakeNow.Value) : CreateParser();
        var query = parser.BuildQuery(luceneQuery);

        var response = await _fixture.Client.SearchAsync<DateMathDocument>(s => s
            .Indices(IndexName)
            .Size(100)
            .Query(query),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    private async Task<List<string>> QueryWithQueryString(string luceneQuery)
    {
        var response = await _fixture.Client.SearchAsync<DateMathDocument>(s => s
            .Indices(IndexName)
            .Size(100)
            .Query(new BoolQuery
            {
                Filter = [new QueryStringQuery(luceneQuery)]
            }),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// Asserts that all three query methods return identical results and optionally that
    /// specific document IDs are present/absent in the results.
    /// </summary>
    private void AssertAllMatch(
        List<string> nativeResults,
        List<string> parserResults,
        List<string> queryStringResults,
        string[]? expectedPresent = null,
        string[]? expectedAbsent = null,
        int? expectedCount = null)
    {
        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);

        if (expectedCount is not null)
            Assert.Equal(expectedCount.Value, parserResults.Count);

        if (expectedPresent is not null)
        {
            foreach (var id in expectedPresent)
                Assert.Contains(id, parserResults);
        }

        if (expectedAbsent is not null)
        {
            foreach (var id in expectedAbsent)
                Assert.DoesNotContain(id, parserResults);
        }
    }

    #endregion

    #region Yesterday

    [Fact]
    public async Task Yesterday_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now-1d/d TO now-1d/d]
        // Fake now: Feb 15, 2024 → yesterday = all of Feb 14
        // gte: round down → Feb 14 00:00:00
        // lte: round up   → Feb 14 23:59:59.999
        var fakeNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-15||-1d/d",
            lte: "2024-02-15||-1d/d");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1d/d TO now-1d/d]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-15||-1d/d TO 2024-02-15||-1d/d]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["feb-14"],
            expectedAbsent: ["feb-01", "feb-29", "jan-31", "mar-01"],
            expectedCount: 1);
    }

    [Fact]
    public async Task Yesterday_AlternativeExclusiveUpper_MatchesElasticsearch()
    {
        // Alternative: [now-1d/d TO now/d}
        // Fake now: Feb 15, 2024
        // gte: Feb 14 00:00:00, lt: Feb 15 00:00:00
        var fakeNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-15||-1d/d",
            lt: "2024-02-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1d/d TO now/d}", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-15||-1d/d TO 2024-02-15||/d}");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["feb-14"],
            expectedAbsent: ["feb-01", "feb-29", "jan-31"],
            expectedCount: 1);
    }

    #endregion

    #region Last Week

    [Fact]
    public async Task LastWeek_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now-1w/w TO now-1w/w]
        // Fake now: Jan 15, 2024 (Monday, ISO week 3)
        // Last week = ISO week 2: Mon Jan 8 – Sun Jan 14
        var fakeNow = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||-1w/w",
            lte: "2024-01-15||-1w/w");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1w/w TO now-1w/w]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||-1w/w TO 2024-01-15||-1w/w]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-08-mon", "jan-10-wed", "jan-14-sun"],
            expectedAbsent: ["jan-07-sun", "jan-15-mon", "jan-01"]);
    }

    [Fact]
    public async Task LastWeek_AlternativeExclusiveUpper_MatchesElasticsearch()
    {
        // Alternative: [now-1w/w TO now/w}
        // Fake now: Jan 15, 2024
        var fakeNow = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||-1w/w",
            lt: "2024-01-15||/w");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1w/w TO now/w}", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||-1w/w TO 2024-01-15||/w}");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-08-mon", "jan-10-wed", "jan-14-sun"],
            expectedAbsent: ["jan-07-sun", "jan-15-mon"]);
    }

    #endregion

    #region This Week

    [Fact]
    public async Task ThisWeek_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now/w TO now/w]
        // Fake now: Jan 15, 2024 (Monday, ISO week 3)
        // This week = Mon Jan 15 – Sun Jan 21
        var fakeNow = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/w",
            lte: "2024-01-15||/w");

        var parserResults = await QueryWithParser(
            "timestamp:[now/w TO now/w]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/w TO 2024-01-15||/w]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-15-mon", "jan-21-sun"],
            expectedAbsent: ["jan-14-sun", "jan-31"]);
    }

    [Fact]
    public async Task ThisWeek_MidWeekAnchor_MatchesElasticsearch()
    {
        // Fake now: Jan 10, 2024 (Wednesday, ISO week 2)
        // This week = Mon Jan 8 – Sun Jan 14
        var fakeNow = new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-10||/w",
            lte: "2024-01-10||/w");

        var parserResults = await QueryWithParser(
            "timestamp:[now/w TO now/w]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-10||/w TO 2024-01-10||/w]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-08-mon", "jan-10-wed", "jan-14-sun"],
            expectedAbsent: ["jan-07-sun", "jan-15-mon"]);
    }

    #endregion

    #region Last Month

    [Fact]
    public async Task LastMonth_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now-1M/M TO now-1M/M]
        // This is the key pattern from the original question!
        // Fake now: Feb 15, 2024 → last month = all of January
        var fakeNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-15||-1M/M",
            lte: "2024-02-15||-1M/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1M/M TO now-1M/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-15||-1M/M TO 2024-02-15||-1M/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "jan-07-sun", "jan-08-mon", "jan-10-wed",
                              "jan-14-sun", "jan-15-mon", "jan-21-sun", "jan-31"],
            expectedAbsent: ["2023-end", "feb-01", "feb-14"],
            expectedCount: 8);
    }

    [Fact]
    public async Task LastMonth_AlternativeExclusiveUpper_MatchesElasticsearch()
    {
        // Alternative pattern: [now-1M/M TO now/M}
        // Fake now: Feb 15, 2024
        var fakeNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-15||-1M/M",
            lt: "2024-02-15||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1M/M TO now/M}", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-15||-1M/M TO 2024-02-15||/M}");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "jan-31"],
            expectedAbsent: ["2023-end", "feb-01"],
            expectedCount: 8);
    }

    [Fact]
    public async Task LastMonth_FromMarch_LeapYearFebruary_MatchesElasticsearch()
    {
        // Fake now: Mar 15, 2024 → last month = all of February 2024 (leap year, 29 days!)
        var fakeNow = new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-03-15||-1M/M",
            lte: "2024-03-15||-1M/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1M/M TO now-1M/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-03-15||-1M/M TO 2024-03-15||-1M/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["feb-01", "feb-14", "feb-29"],
            expectedAbsent: ["jan-31", "mar-01"],
            expectedCount: 3);
    }

    #endregion

    #region This Month

    [Fact]
    public async Task ThisMonth_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now/M TO now/M]
        // Fake now: Feb 15, 2024 → this month = all of February (leap year)
        var fakeNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-15||/M",
            lte: "2024-02-15||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now/M TO now/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-15||/M TO 2024-02-15||/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["feb-01", "feb-14", "feb-29"],
            expectedAbsent: ["jan-31", "mar-01"],
            expectedCount: 3);
    }

    [Fact]
    public async Task ThisMonth_January_MatchesElasticsearch()
    {
        // Fake now: Jan 10, 2024 → this month = all of January
        var fakeNow = new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-10||/M",
            lte: "2024-01-10||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now/M TO now/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-10||/M TO 2024-01-10||/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "jan-07-sun", "jan-08-mon", "jan-10-wed",
                              "jan-14-sun", "jan-15-mon", "jan-21-sun", "jan-31"],
            expectedAbsent: ["2023-end", "feb-01"],
            expectedCount: 8);
    }

    #endregion

    #region Last Year

    [Fact]
    public async Task LastYear_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now-1y/y TO now-1y/y]
        // Fake now: Jun 15, 2024 → last year = all of 2023
        var fakeNow = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-06-15||-1y/y",
            lte: "2024-06-15||-1y/y");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1y/y TO now-1y/y]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-06-15||-1y/y TO 2024-06-15||-1y/y]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["2023-mid", "2023-end"],
            expectedAbsent: ["jan-01", "2025-start"],
            expectedCount: 2);
    }

    [Fact]
    public async Task LastYear_AlternativeExclusiveUpper_MatchesElasticsearch()
    {
        // Alternative: [now-1y/y TO now/y}
        // Fake now: Jun 15, 2024
        var fakeNow = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-06-15||-1y/y",
            lt: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1y/y TO now/y}", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-06-15||-1y/y TO 2024-06-15||/y}");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["2023-mid", "2023-end"],
            expectedAbsent: ["jan-01"],
            expectedCount: 2);
    }

    #endregion

    #region This Year (Full Year)

    [Fact]
    public async Task ThisYear_InclusiveRange_MatchesElasticsearch()
    {
        // Pattern: [now/y TO now/y]
        // Fake now: Jun 15, 2024 → this year = all of 2024 (16 docs)
        var fakeNow = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-06-15||/y",
            lte: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:[now/y TO now/y]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-06-15||/y TO 2024-06-15||/y]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "mar-15", "jun-15", "sep-30", "dec-31"],
            expectedAbsent: ["2023-mid", "2023-end", "2025-start", "2025-mid", "2025-end"],
            expectedCount: 16);
    }

    [Fact]
    public async Task ThisYear_2025_MatchesElasticsearch()
    {
        // Fake now: Jun 15, 2025 → all of 2025
        var fakeNow = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2025-06-15||/y",
            lte: "2025-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:[now/y TO now/y]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2025-06-15||/y TO 2025-06-15||/y]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["2025-start", "2025-mid", "2025-end"],
            expectedAbsent: ["dec-31", "2023-end"],
            expectedCount: 3);
    }

    #endregion

    #region Year to Date

    [Fact]
    public async Task YearToDate_MatchesElasticsearch()
    {
        // Pattern: [now/y TO now/d]
        // Fake now: Mar 15, 2024 → from start of 2024 through end of Mar 15
        // Matches: 8 Jan + 3 Feb + mar-01 + mar-15 = 13 docs
        var fakeNow = new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-03-15||/y",
            lte: "2024-03-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[now/y TO now/d]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-03-15||/y TO 2024-03-15||/d]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "jan-31", "feb-01", "feb-29", "mar-01", "mar-15"],
            expectedAbsent: ["2023-end", "jun-15", "sep-30", "dec-31"],
            expectedCount: 13);
    }

    [Fact]
    public async Task YearToDate_MonthGranularity_MatchesElasticsearch()
    {
        // Pattern: [now/y TO now/M]
        // Fake now: Feb 14, 2024 → from start of 2024 through end of February
        // Matches: 8 Jan + 3 Feb = 11 docs
        var fakeNow = new DateTimeOffset(2024, 2, 14, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-02-14||/y",
            lte: "2024-02-14||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now/y TO now/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-02-14||/y TO 2024-02-14||/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["jan-01", "jan-31", "feb-01", "feb-14", "feb-29"],
            expectedAbsent: ["2023-end", "mar-01"],
            expectedCount: 11);
    }

    #endregion

    #region Two-Year Span and Cross-Year Ranges

    [Fact]
    public async Task LastTwoYears_MatchesElasticsearch()
    {
        // [now-2y/y TO now-1y/y] — the year before last and last year
        // Fake now: Jun 15, 2025 → 2 years ago = 2023, 1 year ago = 2024
        // Matches: 2 docs in 2023 + 16 docs in 2024 = 18 docs
        var fakeNow = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2025-06-15||-2y/y",
            lte: "2025-06-15||-1y/y");

        var parserResults = await QueryWithParser(
            "timestamp:[now-2y/y TO now-1y/y]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2025-06-15||-2y/y TO 2025-06-15||-1y/y]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["2023-mid", "2023-end", "jan-01", "dec-31"],
            expectedAbsent: ["2025-start", "2025-mid", "2025-end"],
            expectedCount: 18);
    }

    [Fact]
    public async Task CrossMonthRange_MatchesElasticsearch()
    {
        // [now-1M/M TO now+1M/M] — Dec 2023 through Feb 2024
        // Fake now: Jan 15, 2024
        var fakeNow = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||-1M/M",
            lte: "2024-01-15||+1M/M");

        var parserResults = await QueryWithParser(
            "timestamp:[now-1M/M TO now+1M/M]", fakeNow);

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||-1M/M TO 2024-01-15||+1M/M]");

        AssertAllMatch(nativeResults, parserResults, queryStringResults,
            expectedPresent: ["2023-end", "jan-01", "jan-31", "feb-01", "feb-29"],
            expectedAbsent: ["2023-mid", "mar-01"]);
    }

    #endregion

    #region Now-Based Live Equivalence Tests

    // These tests use the real system clock 'now' and verify all three query methods
    // return identical results. No specific document assertions since results depend
    // on when the test runs — the important thing is that all three methods agree.

    [Fact]
    public async Task Now_Yesterday_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-1d/d", lte: "now-1d/d");
        var parserResults = await QueryWithParser("timestamp:[now-1d/d TO now-1d/d]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-1d/d TO now-1d/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_LastWeek_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-1w/w", lte: "now-1w/w");
        var parserResults = await QueryWithParser("timestamp:[now-1w/w TO now-1w/w]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-1w/w TO now-1w/w]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_ThisWeek_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now/w", lte: "now/w");
        var parserResults = await QueryWithParser("timestamp:[now/w TO now/w]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now/w TO now/w]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_LastMonth_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-1M/M", lte: "now-1M/M");
        var parserResults = await QueryWithParser("timestamp:[now-1M/M TO now-1M/M]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-1M/M TO now-1M/M]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_LastMonth_AlternativeExclusiveUpper_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-1M/M", lt: "now/M");
        var parserResults = await QueryWithParser("timestamp:[now-1M/M TO now/M}");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-1M/M TO now/M}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_ThisMonth_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now/M", lte: "now/M");
        var parserResults = await QueryWithParser("timestamp:[now/M TO now/M]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now/M TO now/M]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_LastYear_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-1y/y", lte: "now-1y/y");
        var parserResults = await QueryWithParser("timestamp:[now-1y/y TO now-1y/y]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-1y/y TO now-1y/y]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_ThisYear_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now/y", lte: "now/y");
        var parserResults = await QueryWithParser("timestamp:[now/y TO now/y]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now/y TO now/y]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_YearToDate_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now/y", lte: "now/d");
        var parserResults = await QueryWithParser("timestamp:[now/y TO now/d]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now/y TO now/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_Last30Days_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-30d/d", lte: "now/d");
        var parserResults = await QueryWithParser("timestamp:[now-30d/d TO now/d]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-30d/d TO now/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_Last90Days_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-90d/d", lte: "now/d");
        var parserResults = await QueryWithParser("timestamp:[now-90d/d TO now/d]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-90d/d TO now/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task Now_Last12Months_AllMethodsAgree()
    {
        var nativeResults = await QueryNativeDateRange(gte: "now-12M/M", lte: "now/M");
        var parserResults = await QueryWithParser("timestamp:[now-12M/M TO now/M]");
        var queryStringResults = await QueryWithQueryString("timestamp:[now-12M/M TO now/M]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion
}
