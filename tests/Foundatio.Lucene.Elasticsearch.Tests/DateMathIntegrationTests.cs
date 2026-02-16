using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Foundatio.Lucene.Elasticsearch.Tests;

/// <summary>
/// Integration tests that verify the library's date math evaluation produces identical results
/// to Elasticsearch's native date math handling, particularly for inclusive/exclusive range
/// boundaries with date rounding.
///
/// The approach: for each scenario, query ES three ways —
/// 1. With native DateRangeQuery (ES handles rounding and gt/gte/lt/lte)
/// 2. With the library's ElasticsearchQueryParser (pre-evaluates date math to concrete dates)
/// 3. With ES's QueryStringQuery (ES parses Lucene syntax and handles date math natively)
/// Then assert all three return the exact same document set.
/// </summary>
[Collection("Elasticsearch")]
public class DateMathIntegrationTests : IAsyncLifetime
{
    private readonly ElasticsearchFixture _fixture;

    private const string DateMathIndex = "test-datemath";

    public DateMathIntegrationTests(ElasticsearchFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // Delete the index if it exists from a previous run
        await _fixture.Client.Indices.DeleteAsync(DateMathIndex);

        // Create index with a date field
        var createResponse = await _fixture.Client.Indices.CreateAsync<DateMathDocument>(DateMathIndex, c => c
            .Mappings(m => m
                .Properties(p => p
                    .Date(d => d.Timestamp)
                    .Keyword(d => d.Label)
                )
            )
        );

        if (!createResponse.IsValidResponse)
            throw new InvalidOperationException($"Failed to create date math index: {createResponse.DebugInformation}");

        var documents = new List<DateMathDocument>
        {
            new() { Id = "A", Label = "start-of-day",     Timestamp = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "B", Label = "noon",              Timestamp = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc) },
            new() { Id = "C", Label = "end-of-day",        Timestamp = new DateTime(2024, 1, 15, 23, 59, 59, DateTimeKind.Utc) },
            new() { Id = "D", Label = "start-of-next-day", Timestamp = new DateTime(2024, 1, 16, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "E", Label = "end-of-prev-day",   Timestamp = new DateTime(2024, 1, 14, 23, 59, 59, DateTimeKind.Utc) },
            new() { Id = "F", Label = "start-of-feb",      Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = "G", Label = "end-of-jan",        Timestamp = new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc) },
        };

        var bulkResponse = await _fixture.Client.BulkAsync(b => b
            .Index(DateMathIndex)
            .IndexMany(documents)
            .Refresh(Refresh.True)
        );

        if (!bulkResponse.IsValidResponse)
            throw new InvalidOperationException($"Failed to index date math documents: {bulkResponse.DebugInformation}");
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.Client.Indices.DeleteAsync(DateMathIndex);
    }

    private ElasticsearchQueryParser CreateParser()
    {
        return new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = false;
            c.UseDateFields(f => f == "timestamp");
        });
    }

    /// <summary>
    /// Queries ES with native date math (ES handles rounding and gt/gte/lt/lte).
    /// </summary>
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
            .Indices(DateMathIndex)
            .Size(100)
            .Query(new BoolQuery { Filter = [dateRange] }),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// Queries ES using the library's parser (date math is pre-evaluated to concrete dates).
    /// </summary>
    private async Task<List<string>> QueryWithParser(string luceneQuery)
    {
        var parser = CreateParser();
        var query = parser.BuildQuery(luceneQuery);

        var response = await _fixture.Client.SearchAsync<DateMathDocument>(s => s
            .Indices(DateMathIndex)
            .Size(100)
            .Query(query),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// Queries ES using Elasticsearch's query_string query (ES parses Lucene syntax and handles date math natively).
    /// </summary>
    private async Task<List<string>> QueryWithQueryString(string luceneQuery)
    {
        var response = await _fixture.Client.SearchAsync<DateMathDocument>(s => s
            .Indices(DateMathIndex)
            .Size(100)
            .Query(new BoolQuery
            {
                Filter = [new QueryStringQuery(luceneQuery)]
            }),
            TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        return response.Documents.Select(d => d.Id).OrderBy(id => id).ToList();
    }

    #region Inclusive Range [] with Day Rounding

    [Fact]
    public async Task InclusiveRange_DayRounding_MatchesElasticsearch()
    {
        // [2024-01-15||/d TO 2024-01-15||/d] — inclusive both sides, day rounding
        // ES native: gte=2024-01-15||/d, lte=2024-01-15||/d
        // Should match everything on Jan 15 (A, B, C)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/d",
            lte: "2024-01-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15||/d TO 2024-01-15||/d]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/d TO 2024-01-15||/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("D", parserResults);
        Assert.DoesNotContain("E", parserResults);
    }

    #endregion

    #region Inclusive/Exclusive Range [} with Day Rounding

    [Fact]
    public async Task InclusiveExclusiveRange_DayRounding_MatchesElasticsearch()
    {
        // [2024-01-15||/d TO 2024-01-16||/d} — inclusive min, exclusive max, day rounding
        // ES native: gte=2024-01-15||/d, lt=2024-01-16||/d
        // Should match everything on Jan 15 (A, B, C)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/d",
            lt: "2024-01-16||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15||/d TO 2024-01-16||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/d TO 2024-01-16||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("D", parserResults);
    }

    [Fact]
    public async Task InclusiveExclusiveRange_SameDay_DayRounding_MatchesElasticsearch()
    {
        // [2024-01-15||/d TO 2024-01-15||/d} — inclusive min, exclusive max, same day
        // ES native: gte=2024-01-15||/d, lt=2024-01-15||/d
        // Min rounds to start of day, Max rounds to start of day — nothing matches (start >= start AND < start)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/d",
            lt: "2024-01-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15||/d TO 2024-01-15||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/d TO 2024-01-15||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Exclusive/Inclusive Range {] with Day Rounding

    [Fact]
    public async Task ExclusiveInclusiveRange_DayRounding_MatchesElasticsearch()
    {
        // {2024-01-14||/d TO 2024-01-15||/d] — exclusive min, inclusive max, day rounding
        // ES native: gt=2024-01-14||/d, lte=2024-01-15||/d
        // Should match everything on Jan 15 (A, B, C) but not end of Jan 14 (E)
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-14||/d",
            lte: "2024-01-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-14||/d TO 2024-01-15||/d]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-14||/d TO 2024-01-15||/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("E", parserResults); // End of Jan 14 excluded
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_SameDay_DayRounding_MatchesElasticsearch()
    {
        // {2024-01-15||/d TO 2024-01-15||/d] — exclusive min, inclusive max, same day
        // ES native: gt=2024-01-15||/d, lte=2024-01-15||/d
        // Min rounds to end of day, Max rounds to end of day — nothing matches (> end AND <= end)
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15||/d",
            lte: "2024-01-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15||/d TO 2024-01-15||/d]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15||/d TO 2024-01-15||/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Exclusive Range {} with Day Rounding

    [Fact]
    public async Task ExclusiveRange_DayRounding_MatchesElasticsearch()
    {
        // {2024-01-14||/d TO 2024-01-16||/d} — exclusive both sides, day rounding
        // ES native: gt=2024-01-14||/d, lt=2024-01-16||/d
        // Should match everything on Jan 15 (A, B, C)
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-14||/d",
            lt: "2024-01-16||/d");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-14||/d TO 2024-01-16||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-14||/d TO 2024-01-16||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("D", parserResults);
        Assert.DoesNotContain("E", parserResults);
    }

    [Fact]
    public async Task ExclusiveRange_SameDay_DayRounding_MatchesElasticsearch()
    {
        // {2024-01-15||/d TO 2024-01-15||/d} — exclusive both sides, same day
        // ES native: gt=2024-01-15||/d, lt=2024-01-15||/d
        // Min rounds to end of day, Max rounds to start of day — empty
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15||/d",
            lt: "2024-01-15||/d");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15||/d TO 2024-01-15||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15||/d TO 2024-01-15||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Month Rounding

    [Fact]
    public async Task InclusiveRange_MonthRounding_MatchesElasticsearch()
    {
        // [2024-01-15||/M TO 2024-01-15||/M] — inclusive both sides, month rounding
        // ES native: gte=2024-01-15||/M, lte=2024-01-15||/M
        // Should match everything in January (A, B, C, D, E, G)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/M",
            lte: "2024-01-15||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15||/M TO 2024-01-15||/M]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/M TO 2024-01-15||/M]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults); // Jan 15 start
        Assert.Contains("G", parserResults); // Jan 31 end
        Assert.Contains("E", parserResults); // Jan 14 end
        Assert.DoesNotContain("F", parserResults); // Feb 1 start
    }

    [Fact]
    public async Task InclusiveExclusiveRange_MonthRounding_MatchesElasticsearch()
    {
        // [2024-01-15||/M TO 2024-02-01||/M} — inclusive min, exclusive max, month rounding
        // ES native: gte=2024-01-15||/M, lt=2024-02-01||/M
        // Min rounds to start of January, Max rounds to start of February
        // Should match everything in January (A, B, C, D, E, G) but not Feb (F)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15||/M",
            lt: "2024-02-01||/M");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15||/M TO 2024-02-01||/M}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15||/M TO 2024-02-01||/M}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("F", parserResults); // Feb 1 excluded
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_MonthRounding_MatchesElasticsearch()
    {
        // {2024-01-15||/M TO 2024-02-01||/M] — exclusive min, inclusive max, month rounding
        // ES native: gt=2024-01-15||/M, lte=2024-02-01||/M
        // Min rounds to end of January (gt end-of-jan), Max rounds to end of February
        // Should match Feb 1 (F) and anything after Jan 31 23:59:59.999
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15||/M",
            lte: "2024-02-01||/M");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15||/M TO 2024-02-01||/M]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15||/M TO 2024-02-01||/M]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("F", parserResults); // Feb 1 included
    }

    [Fact]
    public async Task ExclusiveRange_MonthRounding_MatchesElasticsearch()
    {
        // {2024-01-15||/M TO 2024-02-01||/M} — exclusive both sides, month rounding
        // ES native: gt=2024-01-15||/M, lt=2024-02-01||/M
        // Min rounds to end of January (gt), Max rounds to start of February (lt)
        // Nothing between end-of-jan and start-of-feb → empty
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15||/M",
            lt: "2024-02-01||/M");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15||/M TO 2024-02-01||/M}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15||/M TO 2024-02-01||/M}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Hour Rounding

    [Fact]
    public async Task InclusiveRange_HourRounding_MatchesElasticsearch()
    {
        // [2024-01-15T12:30:00Z||/h TO 2024-01-15T12:30:00Z||/h] — inclusive, hour rounding
        // ES native: gte=2024-01-15T12:30:00Z||/h, lte=2024-01-15T12:30:00Z||/h
        // Rounds to the 12:00 hour, should match Doc B at 12:00:00
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15T12:30:00Z||/h",
            lte: "2024-01-15T12:30:00Z||/h");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15T12:30:00Z||/h TO 2024-01-15T12:30:00Z||/h]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15T12:30:00Z||/h TO 2024-01-15T12:30:00Z||/h]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("B", parserResults); // Noon document
    }

    [Fact]
    public async Task InclusiveExclusiveRange_HourRounding_MatchesElasticsearch()
    {
        // [2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h} — inclusive min, exclusive max, hour rounding
        // ES native: gte=2024-01-15T00:00:00Z||/h, lt=2024-01-15T12:00:00Z||/h
        // Should match Doc A (00:00) but not Doc B (12:00)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-15T00:00:00Z||/h",
            lt: "2024-01-15T12:00:00Z||/h");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults); // Start of day
        Assert.DoesNotContain("B", parserResults); // Noon excluded
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_HourRounding_MatchesElasticsearch()
    {
        // {2024-01-15T00:00:00Z||/h TO 2024-01-15T12:30:00Z||/h] — exclusive min, inclusive max, hour rounding
        // ES native: gt=2024-01-15T00:00:00Z||/h, lte=2024-01-15T12:30:00Z||/h
        // Min rounds to end of 00:xx hour (gt 00:59:59), Max rounds to end of 12:xx hour
        // Should match Doc A (00:00? no — gt end of hour 0 means > 00:59:59) and Doc B (12:00)
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15T00:00:00Z||/h",
            lte: "2024-01-15T12:30:00Z||/h");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:30:00Z||/h]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:30:00Z||/h]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("B", parserResults); // Noon within 12:xx hour
    }

    [Fact]
    public async Task ExclusiveRange_HourRounding_MatchesElasticsearch()
    {
        // {2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h} — exclusive both sides, hour rounding
        // ES native: gt=2024-01-15T00:00:00Z||/h, lt=2024-01-15T12:00:00Z||/h
        // Min rounds to end of 00:xx hour (gt 00:59:59), Max rounds to start of 12:xx hour (lt 12:00:00)
        // Should match anything between 01:00:00 and 11:59:59
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-15T00:00:00Z||/h",
            lt: "2024-01-15T12:00:00Z||/h");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-15T00:00:00Z||/h TO 2024-01-15T12:00:00Z||/h}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("A", parserResults); // 00:00 excluded (gt end of hour 0)
        Assert.DoesNotContain("B", parserResults); // 12:00 excluded (lt start of hour 12)
    }

    #endregion

    #region Date Math with Operations and Rounding

    [Fact]
    public async Task InclusiveRange_DateMathWithAddAndRounding_MatchesElasticsearch()
    {
        // [2024-01-14||+1d/d TO 2024-01-14||+2d/d] — add days then round
        // ES native: gte=2024-01-14||+1d/d, lte=2024-01-14||+2d/d
        // Min: Jan 14 + 1 day = Jan 15, rounded to start → Jan 15 00:00:00
        // Max: Jan 14 + 2 days = Jan 16, rounded to end → Jan 16 23:59:59
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-14||+1d/d",
            lte: "2024-01-14||+2d/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-14||+1d/d TO 2024-01-14||+2d/d]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-14||+1d/d TO 2024-01-14||+2d/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults); // Jan 15
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.Contains("D", parserResults); // Jan 16
    }

    [Fact]
    public async Task InclusiveExclusiveRange_DateMathWithSubtractAndRounding_MatchesElasticsearch()
    {
        // [2024-01-16||-1d/d TO 2024-01-16||/d} — subtract then round, exclusive upper
        // ES native: gte=2024-01-16||-1d/d, lt=2024-01-16||/d
        // Min: Jan 16 - 1 day = Jan 15, rounded to start → Jan 15 00:00:00
        // Max: Jan 16 rounded to start of day → Jan 16 00:00:00 (exclusive)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-16||-1d/d",
            lt: "2024-01-16||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-16||-1d/d TO 2024-01-16||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-16||-1d/d TO 2024-01-16||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("D", parserResults); // Jan 16 excluded
    }

    #endregion

    #region Mixed Inclusive/Exclusive with Multiple Day Spans

    [Fact]
    public async Task InclusiveRange_MultiDaySpan_MatchesElasticsearch()
    {
        // [2024-01-14||/d TO 2024-01-16||/d] — inclusive, all three days
        // Should match all docs from Jan 14 through Jan 16
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-01-14||/d",
            lte: "2024-01-16||/d");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-01-14||/d TO 2024-01-16||/d]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-01-14||/d TO 2024-01-16||/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("E", parserResults); // Jan 14
        Assert.Contains("A", parserResults); // Jan 15
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.Contains("D", parserResults); // Jan 16
    }

    [Fact]
    public async Task ExclusiveRange_MultiDaySpan_MatchesElasticsearch()
    {
        // {2024-01-14||/d TO 2024-01-16||/d} — exclusive, skip boundary days
        // Min: > end of Jan 14, Max: < start of Jan 16
        // Should match only docs on Jan 15
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-01-14||/d",
            lt: "2024-01-16||/d");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-01-14||/d TO 2024-01-16||/d}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-01-14||/d TO 2024-01-16||/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("E", parserResults); // Jan 14 excluded
        Assert.Contains("A", parserResults); // Jan 15
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.DoesNotContain("D", parserResults); // Jan 16 excluded
    }

    #endregion

    #region Now-based Date Math with Rounding

    [Fact]
    public async Task InclusiveRange_NowBasedDateMath_MatchesElasticsearch()
    {
        // All documents are in the past, so [* TO now/d] should match all of them
        // This verifies "now" handling is consistent
        var nativeResults = await QueryNativeDateRange(lte: "now/d");
        var parserResults = await QueryWithParser("timestamp:[* TO now/d]");
        var queryStringResults = await QueryWithQueryString("timestamp:[* TO now/d]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Equal(7, parserResults.Count); // All documents
    }

    [Fact]
    public async Task ExclusiveRange_NowBasedDateMath_MatchesElasticsearch()
    {
        // All documents are in the past, so {* TO now/d} should also match all
        // (since now/d exclusive floors to start of today, and all docs are before today)
        var nativeResults = await QueryNativeDateRange(lt: "now/d");
        var parserResults = await QueryWithParser("timestamp:{* TO now/d}");
        var queryStringResults = await QueryWithQueryString("timestamp:{* TO now/d}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Year Rounding

    [Fact]
    public async Task InclusiveRange_YearRounding_MatchesElasticsearch()
    {
        // [2024-06-15||/y TO 2024-06-15||/y] — all of year 2024
        // Should match all documents (all are in 2024)
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-06-15||/y",
            lte: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-06-15||/y TO 2024-06-15||/y]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-06-15||/y TO 2024-06-15||/y]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Equal(7, parserResults.Count);
    }

    [Fact]
    public async Task InclusiveExclusiveRange_YearRounding_MatchesElasticsearch()
    {
        // [2024-06-15||/y TO 2024-06-15||/y} — inclusive min, exclusive max, year rounding
        // Min: start of 2024, Max: start of 2024 (exclusive) → empty
        var nativeResults = await QueryNativeDateRange(
            gte: "2024-06-15||/y",
            lt: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:[2024-06-15||/y TO 2024-06-15||/y}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:[2024-06-15||/y TO 2024-06-15||/y}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_YearRounding_MatchesElasticsearch()
    {
        // {2023-06-15||/y TO 2024-06-15||/y] — exclusive min, inclusive max, year rounding
        // ES native: gt=2023-06-15||/y, lte=2024-06-15||/y
        // Min rounds to end of 2023 (gt), Max rounds to end of 2024
        // Should match all docs (all in 2024, which is after end of 2023 and before end of 2024)
        var nativeResults = await QueryNativeDateRange(
            gt: "2023-06-15||/y",
            lte: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:{2023-06-15||/y TO 2024-06-15||/y]");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2023-06-15||/y TO 2024-06-15||/y]");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Equal(7, parserResults.Count);
    }

    [Fact]
    public async Task ExclusiveRange_YearRounding_MatchesElasticsearch()
    {
        // {2024-06-15||/y TO 2024-06-15||/y} — exclusive both sides, same year
        // ES native: gt=2024-06-15||/y, lt=2024-06-15||/y
        // Min rounds to end of 2024, Max rounds to start of 2024 → empty
        var nativeResults = await QueryNativeDateRange(
            gt: "2024-06-15||/y",
            lt: "2024-06-15||/y");

        var parserResults = await QueryWithParser(
            "timestamp:{2024-06-15||/y TO 2024-06-15||/y}");

        var queryStringResults = await QueryWithQueryString(
            "timestamp:{2024-06-15||/y TO 2024-06-15||/y}");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
    }

    #endregion

    #region Short-Form Operators (>, >=, <, <=) with Date Math

    [Fact]
    public async Task GreaterThan_DayRounding_MatchesElasticsearch()
    {
        // >2024-01-15||/d — greater than end of Jan 15
        // ES native: gt=2024-01-15||/d
        // Should match D (Jan 16), F (Feb 1), G (Jan 31) but not A/B/C (Jan 15) or E (Jan 14)
        var nativeResults = await QueryNativeDateRange(gt: "2024-01-15||/d");
        var parserResults = await QueryWithParser("timestamp:>2024-01-15||/d");
        // ES query_string requires escaping / in short-form operators to avoid regex interpretation
        var queryStringResults = await QueryWithQueryString("timestamp:>2024-01-15||\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("A", parserResults);
        Assert.DoesNotContain("B", parserResults);
        Assert.DoesNotContain("C", parserResults);
        Assert.Contains("D", parserResults);
        Assert.DoesNotContain("E", parserResults);
    }

    [Fact]
    public async Task GreaterThanOrEqual_DayRounding_MatchesElasticsearch()
    {
        // >=2024-01-15||/d — greater than or equal to start of Jan 15
        // ES native: gte=2024-01-15||/d
        // Should match A/B/C (Jan 15), D (Jan 16), F (Feb 1), G (Jan 31) but not E (Jan 14)
        var nativeResults = await QueryNativeDateRange(gte: "2024-01-15||/d");
        var parserResults = await QueryWithParser("timestamp:>=2024-01-15||/d");
        var queryStringResults = await QueryWithQueryString("timestamp:>=2024-01-15||\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.Contains("D", parserResults);
        Assert.DoesNotContain("E", parserResults);
    }

    [Fact]
    public async Task LessThan_DayRounding_MatchesElasticsearch()
    {
        // <2024-01-15||/d — less than start of Jan 15
        // ES native: lt=2024-01-15||/d
        // Should match E (Jan 14) but not A/B/C (Jan 15) or D/F/G
        var nativeResults = await QueryNativeDateRange(lt: "2024-01-15||/d");
        var parserResults = await QueryWithParser("timestamp:<2024-01-15||/d");
        var queryStringResults = await QueryWithQueryString("timestamp:<2024-01-15||\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("A", parserResults);
        Assert.DoesNotContain("B", parserResults);
        Assert.DoesNotContain("C", parserResults);
        Assert.Contains("E", parserResults);
    }

    [Fact]
    public async Task LessThanOrEqual_DayRounding_MatchesElasticsearch()
    {
        // <=2024-01-15||/d — less than or equal to end of Jan 15
        // ES native: lte=2024-01-15||/d
        // Should match A/B/C (Jan 15) and E (Jan 14) but not D (Jan 16), F, G
        var nativeResults = await QueryNativeDateRange(lte: "2024-01-15||/d");
        var parserResults = await QueryWithParser("timestamp:<=2024-01-15||/d");
        var queryStringResults = await QueryWithQueryString("timestamp:<=2024-01-15||\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.Contains("E", parserResults);
        Assert.DoesNotContain("D", parserResults);
    }

    [Fact]
    public async Task GreaterThan_MonthRounding_MatchesElasticsearch()
    {
        // >2024-01-15||/M — greater than end of January
        // ES native: gt=2024-01-15||/M
        // Should match F (Feb 1) only
        var nativeResults = await QueryNativeDateRange(gt: "2024-01-15||/M");
        var parserResults = await QueryWithParser("timestamp:>2024-01-15||/M");
        var queryStringResults = await QueryWithQueryString("timestamp:>2024-01-15||\\/M");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("F", parserResults);
        Assert.DoesNotContain("A", parserResults);
        Assert.DoesNotContain("G", parserResults); // End of Jan excluded
    }

    [Fact]
    public async Task LessThanOrEqual_MonthRounding_MatchesElasticsearch()
    {
        // <=2024-01-15||/M — less than or equal to end of January
        // ES native: lte=2024-01-15||/M
        // Should match all January docs (A, B, C, D, E, G) but not F (Feb)
        var nativeResults = await QueryNativeDateRange(lte: "2024-01-15||/M");
        var parserResults = await QueryWithParser("timestamp:<=2024-01-15||/M");
        var queryStringResults = await QueryWithQueryString("timestamp:<=2024-01-15||\\/M");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("F", parserResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("G", parserResults);
    }

    [Fact]
    public async Task GreaterThan_NowDayRounding_MatchesElasticsearch()
    {
        // >now/d — greater than end of today
        // All test documents are from 2024, well in the past, so nothing matches
        var nativeResults = await QueryNativeDateRange(gt: "now/d");
        var parserResults = await QueryWithParser("timestamp:>now/d");
        var queryStringResults = await QueryWithQueryString("timestamp:>now\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Empty(parserResults);
    }

    [Fact]
    public async Task LessThan_NowDayRounding_MatchesElasticsearch()
    {
        // <now/d — less than start of today
        // All test documents are from 2024, well before today, so all match
        var nativeResults = await QueryNativeDateRange(lt: "now/d");
        var parserResults = await QueryWithParser("timestamp:<now/d");
        var queryStringResults = await QueryWithQueryString("timestamp:<now\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Equal(7, parserResults.Count);
    }

    [Fact]
    public async Task GreaterThanOrEqual_DateMathWithOperations_MatchesElasticsearch()
    {
        // >=2024-01-14||+1d/d — add 1 day then round, inclusive
        // Equivalent to >= start of Jan 15
        var nativeResults = await QueryNativeDateRange(gte: "2024-01-14||+1d/d");
        var parserResults = await QueryWithParser("timestamp:>=2024-01-14||+1d/d");
        var queryStringResults = await QueryWithQueryString("timestamp:>=2024-01-14||+1d\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("C", parserResults);
        Assert.Contains("D", parserResults);
        Assert.DoesNotContain("E", parserResults);
    }

    [Fact]
    public async Task LessThan_DateMathWithOperations_MatchesElasticsearch()
    {
        // <2024-01-16||-1d/d — subtract 1 day then round, exclusive
        // Equivalent to < start of Jan 15
        var nativeResults = await QueryNativeDateRange(lt: "2024-01-16||-1d/d");
        var parserResults = await QueryWithParser("timestamp:<2024-01-16||-1d/d");
        var queryStringResults = await QueryWithQueryString("timestamp:<2024-01-16||-1d\\/d");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("E", parserResults);
        Assert.DoesNotContain("A", parserResults);
    }

    [Fact]
    public async Task GreaterThan_HourRounding_MatchesElasticsearch()
    {
        // >2024-01-15T12:30:00Z||/h — greater than end of the 12:xx hour
        // ES native: gt=2024-01-15T12:30:00Z||/h
        // Hour 12 rounds to 12:59:59 → matches anything after that (C at 23:59:59, D, F, G)
        var nativeResults = await QueryNativeDateRange(gt: "2024-01-15T12:30:00Z||/h");
        var parserResults = await QueryWithParser("timestamp:>2024-01-15T12:30:00Z||/h");
        // ES query_string requires escaping : in timestamps and / in rounding for short-form operators
        var queryStringResults = await QueryWithQueryString("timestamp:>2024-01-15T12\\:30\\:00Z||\\/h");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.DoesNotContain("A", parserResults);
        Assert.DoesNotContain("B", parserResults); // 12:00 is within the excluded hour
        Assert.Contains("C", parserResults); // 23:59:59 is after hour 12
    }

    [Fact]
    public async Task LessThanOrEqual_HourRounding_MatchesElasticsearch()
    {
        // <=2024-01-15T12:30:00Z||/h — less than or equal to end of the 12:xx hour
        // ES native: lte=2024-01-15T12:30:00Z||/h
        // Hour 12 rounds to 12:59:59 → matches A (00:00), B (12:00), E (prev day)
        var nativeResults = await QueryNativeDateRange(lte: "2024-01-15T12:30:00Z||/h");
        var parserResults = await QueryWithParser("timestamp:<=2024-01-15T12:30:00Z||/h");
        var queryStringResults = await QueryWithQueryString("timestamp:<=2024-01-15T12\\:30\\:00Z||\\/h");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Contains("A", parserResults);
        Assert.Contains("B", parserResults);
        Assert.Contains("E", parserResults);
        Assert.DoesNotContain("C", parserResults); // 23:59:59 is after hour 12
    }

    [Fact]
    public async Task GreaterThanOrEqual_YearRounding_MatchesElasticsearch()
    {
        // >=2024-06-15||/y — greater than or equal to start of 2024
        // ES native: gte=2024-06-15||/y
        // All docs are in 2024, so all match
        var nativeResults = await QueryNativeDateRange(gte: "2024-06-15||/y");
        var parserResults = await QueryWithParser("timestamp:>=2024-06-15||/y");
        var queryStringResults = await QueryWithQueryString("timestamp:>=2024-06-15||\\/y");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Equal(7, parserResults.Count);
    }

    [Fact]
    public async Task LessThan_YearRounding_MatchesElasticsearch()
    {
        // <2024-06-15||/y — less than start of 2024
        // ES native: lt=2024-06-15||/y
        // All docs are in 2024, so nothing matches
        var nativeResults = await QueryNativeDateRange(lt: "2024-06-15||/y");
        var parserResults = await QueryWithParser("timestamp:<2024-06-15||/y");
        var queryStringResults = await QueryWithQueryString("timestamp:<2024-06-15||\\/y");

        Assert.Equal(nativeResults, parserResults);
        Assert.Equal(nativeResults, queryStringResults);
        Assert.Empty(parserResults);
    }

    #endregion
}

public class DateMathDocument
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
}
