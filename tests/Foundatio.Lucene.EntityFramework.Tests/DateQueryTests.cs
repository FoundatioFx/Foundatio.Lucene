namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Dates follow Elasticsearch rounding: a date that omits components covers the whole period, inclusive upper and
/// exclusive lower bounds round up to the end of the period, and date math is evaluated with the configured clock and
/// time zone.
/// </summary>
public class DateQueryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2024, 2, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private readonly SampleContext _db = SampleData.CreateInMemory();
    private readonly EntityFrameworkQueryParser _parser = new(c => c.SetTimeProvider(new FixedTimeProvider(Now)));

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("datetime:[2024-01-01 TO 2024-02-01}", new[] { 1, 2 })]
    [InlineData("datetime:[2024-01-01 TO 2024-02-01]", new[] { 1, 2, 3 })]
    [InlineData("datetime:<=2024-01-31", new[] { 1, 2 })]
    [InlineData("datetime:<2024-01-31", new[] { 1 })]
    [InlineData("datetime:>2024-01-31", new[] { 3 })]
    [InlineData("datetime:{2024-01-31 TO *]", new[] { 3 })]
    [InlineData("datetime:>=2024-01-31", new[] { 2, 3 })]
    [InlineData("datetime:2024-01-31", new[] { 2 })]
    [InlineData("datetime:2024-01", new[] { 1, 2 })]
    [InlineData("datetime:2024", new[] { 1, 2, 3 })]
    [InlineData("datetime:2024-01-15T10:30:00Z", new[] { 1 })]
    [InlineData("datetime:\"2024-01-15T10:30:00.000Z\"", new[] { 1 })]
    [InlineData("datetime:[2024-01-15T10:00:00 TO 2024-01-15T11:00:00]", new[] { 1 })]
    [InlineData("datetime:[* TO 2024-01-15T10:30:00Z}", new int[0])]
    [InlineData("nullabledatetime:[2024-01-01 TO *]", new[] { 2, 3 })]
    public void BuildFilter_WithDateTimeValues_UsesElasticsearchRounding(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("datetime:>=now/d", new[] { 3 })]
    [InlineData("datetime:now/d", new[] { 3 })]
    [InlineData("datetime:[now-1d/d TO now/d}", new[] { 2 })]
    [InlineData("datetime:<now-1M", new int[0])]
    [InlineData("datetime:>now-17d", new[] { 2, 3 })]
    [InlineData("datetime:2024-01-31||+1d", new[] { 3 })]
    [InlineData("datetime:[2024-01-01||+14d/d TO 2024-01-01||+1M}", new[] { 1, 2 })]
    [InlineData("datetimeoffset:>now-1w", new[] { 2, 3 })]
    [InlineData("dateonly:>=now/d", new[] { 3 })]
    [InlineData("dateonly:now-1d", new[] { 2 })]
    [InlineData("dateonly:[now-17d TO now]", new[] { 1, 2, 3 })]
    [InlineData("dateonly:[now-16d TO now]", new[] { 2, 3 })]
    [InlineData("timeonly:<now", new[] { 1 })]
    public void BuildFilter_WithDateMath_EvaluatesRelativeToTimeProvider(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("dateonly:[2024-01-01 TO 2024-02-01}", new[] { 1, 2 })]
    [InlineData("dateonly:[2024-01-01 TO 2024-02-01]", new[] { 1, 2, 3 })]
    [InlineData("dateonly:<=2024-01-31", new[] { 1, 2 })]
    [InlineData("dateonly:<2024-01-31", new[] { 1 })]
    [InlineData("dateonly:>2024-01-31", new[] { 3 })]
    [InlineData("dateonly:>=2024-01-31", new[] { 2, 3 })]
    [InlineData("dateonly:2024-01-31", new[] { 2 })]
    [InlineData("dateonly:2024-01", new[] { 1, 2 })]
    [InlineData("dateonly:2024-01-15T10:00:00", new[] { 1 })]
    [InlineData("dateonly:[2024-01-15T10:00:00 TO *]", new[] { 1, 2, 3 })]
    [InlineData("dateonly:{2024-01-15T10:00:00 TO *]", new[] { 2, 3 })]
    public void BuildFilter_WithDateOnlyValues_ComparesCalendarDatesOfRoundedBounds(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("datetimeoffset:2024-02-01", new[] { 2 })]
    [InlineData("datetimeoffset:<2024-02-01T06:00:00Z", new[] { 1 })]
    [InlineData("datetimeoffset:<=2024-02-01T06:00:00Z", new[] { 1, 2 })]
    [InlineData("datetimeoffset:\"2024-02-01T00:00:00-06:00\"", new[] { 2 })]
    [InlineData("datetimeoffset:[2024-01-15 TO 2024-01-15]", new[] { 1 })]
    public void BuildFilter_WithDateTimeOffsetValues_ComparesInstants(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("datetime:2024-01-31", new[] { 2, 3 })]
    [InlineData("datetime:[2024-01-01 TO 2024-01-31]", new[] { 1, 2, 3 })]
    [InlineData("datetime:<2024-01-31", new[] { 1 })]
    [InlineData("datetimeoffset:[2024-01-15T04:30 TO 2024-01-15T04:30]", new[] { 1 })]
    [InlineData("datetimeoffset:2024-01-31", new int[0])]
    [InlineData("dateonly:2024-01-31", new[] { 2 })]
    public void BuildFilter_WithConfiguredTimeZone_InterpretsDatesInThatZone(string query, int[] expected)
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetTimeProvider(new FixedTimeProvider(Now)).SetTimeZone(Chicago));

        Assert.Equal(expected, _db.TypeSamples.Where(query, parser).Ids());
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser, new EntityFrameworkQueryOptions { TimeZone = Chicago }).Ids());
    }

    [Fact]
    public void BuildFilter_WithTimeZoneModifierOnRange_UsesThatZoneForTheRange()
    {
        Assert.Equal([2], _db.TypeSamples.Where("datetime:[2024-01-31 TO 2024-01-31]", _parser).Ids());
        Assert.Equal([2, 3], _db.TypeSamples.Where("datetime:[2024-01-31 TO 2024-01-31]^\"America/Chicago\"", _parser).Ids());

        var ex = Assert.Throws<QueryValidationException>(() => _db.TypeSamples.Where("datetime:[2024-01-31 TO *]^\"Mars/Olympus\"", _parser));
        Assert.Contains("Mars/Olympus", ex.Message);
    }

    [Theory]
    [InlineData("nullabledatetime:2024-03-10", new[] { 3 })]
    [InlineData("nullabledatetime:[2024-03-10T01:00 TO 2024-03-10T03:30]", new[] { 3 })]
    [InlineData("nullabledatetime:[2024-03-10T01:00 TO 2024-03-10T03:30}", new int[0])]
    [InlineData("nullabledatetime:[2024-03-10T01:00 TO 2024-03-10T03:29]", new int[0])]
    [InlineData("nullabledatetime:>=2024-03-10T03:30", new[] { 3 })]
    public void BuildFilter_AcrossDaylightSavingTransition_UsesOffsetInEffectOnEachBound(string query, int[] expected)
    {
        // 2024-03-10 is 23 hours long in Chicago: 02:00 CST jumps to 03:00 CDT, so 03:30 local is 08:30Z.
        var parser = new EntityFrameworkQueryParser(c => c.SetTimeZone(Chicago));

        Assert.Equal(expected, _db.TypeSamples.Where(query, parser).Ids());
    }

    [Fact]
    public void BuildFilter_WithDateTimeStorageTimeZone_ComparesStoredWallClockValues()
    {
        var localStorage = new EntityFrameworkQueryParser(c => c.SetDateTimeStorageTimeZone(Chicago));
        var localEverywhere = new EntityFrameworkQueryParser(c => c.SetDateTimeStorageTimeZone(Chicago).SetTimeZone(Chicago));

        Assert.Empty(_db.TypeSamples.Where("datetime:2024-01-31", localStorage).Ids());
        Assert.Equal([2], _db.TypeSamples.Where("datetime:2024-01-31", localEverywhere).Ids());
    }

    [Theory]
    [InlineData("datetime:now-1x")]
    [InlineData("datetime:[now/d TO now+]")]
    [InlineData("datetime:2024-02-30")]
    public void BuildFilter_WithInvalidDate_ThrowsValidationError(string query)
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.TypeSamples.Where(query, _parser));

        Assert.Equal(QueryErrorCode.TypeConversionError, Assert.Single(ex.Errors).Code);
    }

    [Fact]
    public void BuildFilter_WithRoundedUpperBound_ComparesAgainstStartOfNextPeriod()
    {
        var filter = _parser.BuildFilter<TypeSample>("datetime:<=2024-01-31", new EntityFrameworkQueryOptions { Model = _db.Model });
        var value = CapturedValues(filter).OfType<DateTime>().Single();

        Assert.Equal(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Contains(" < ", filter.Body.ToString());
    }

    internal static IEnumerable<object?> CapturedValues(System.Linq.Expressions.LambdaExpression filter)
    {
        var values = new List<object?>();
        new CapturedValueCollector(values).Visit(filter);
        return values;
    }

    private sealed class CapturedValueCollector(List<object?> values) : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitMember(System.Linq.Expressions.MemberExpression node)
        {
            if (node.Expression is System.Linq.Expressions.ConstantExpression { Value: { } holder } && node.Member is System.Reflection.FieldInfo field)
                values.Add(field.GetValue(holder));

            return base.VisitMember(node);
        }
    }
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
