using System.Globalization;

namespace Foundatio.Lucene.Tests;

public class DateMathTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 15, 14, 30, 45, 123, TimeSpan.Zero);
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Theory]
    [InlineData("now", "2024-06-15T14:30:45.123+00:00")]
    [InlineData("now+1h", "2024-06-15T15:30:45.123+00:00")]
    [InlineData("now+1H", "2024-06-15T15:30:45.123+00:00")]
    [InlineData("now-2d", "2024-06-13T14:30:45.123+00:00")]
    [InlineData("now+d", "2024-06-16T14:30:45.123+00:00")]
    [InlineData("now+1M", "2024-07-15T14:30:45.123+00:00")]
    [InlineData("now-1y", "2023-06-15T14:30:45.123+00:00")]
    [InlineData("now+1w", "2024-06-22T14:30:45.123+00:00")]
    [InlineData("now+30m", "2024-06-15T15:00:45.123+00:00")]
    [InlineData("now-15s", "2024-06-15T14:30:30.123+00:00")]
    [InlineData("now+1d+2h-30m", "2024-06-16T16:00:45.123+00:00")]
    [InlineData("now-1d/d", "2024-06-14T00:00:00+00:00")]
    [InlineData("now/y", "2024-01-01T00:00:00+00:00")]
    [InlineData("now/M", "2024-06-01T00:00:00+00:00")]
    [InlineData("now/w", "2024-06-10T00:00:00+00:00")]
    [InlineData("now/d", "2024-06-15T00:00:00+00:00")]
    [InlineData("now/h", "2024-06-15T14:00:00+00:00")]
    [InlineData("now/H", "2024-06-15T14:00:00+00:00")]
    [InlineData("now/m", "2024-06-15T14:30:00+00:00")]
    [InlineData("now/s", "2024-06-15T14:30:45+00:00")]
    public void Parse_RelativeToNow_ReturnsExpectedDate(string expression, string expected)
    {
        var result = DateMath.Parse(expression, Now);

        AssertDate(expected, result);
    }

    [Theory]
    [InlineData("now", "2024-06-15T14:30:45.123+00:00")]
    [InlineData("now/y", "2024-12-31T23:59:59.9999999+00:00")]
    [InlineData("now/M", "2024-06-30T23:59:59.9999999+00:00")]
    [InlineData("now/w", "2024-06-16T23:59:59.9999999+00:00")]
    [InlineData("now/d", "2024-06-15T23:59:59.9999999+00:00")]
    [InlineData("now-1d/d", "2024-06-14T23:59:59.9999999+00:00")]
    [InlineData("now/h", "2024-06-15T14:59:59.9999999+00:00")]
    [InlineData("now/m", "2024-06-15T14:30:59.9999999+00:00")]
    [InlineData("now/s", "2024-06-15T14:30:45.9999999+00:00")]
    [InlineData("now+1d", "2024-06-16T14:30:45.123+00:00")]
    public void Parse_UpperLimit_RoundsToEndOfPeriod(string expression, string expected)
    {
        var result = DateMath.Parse(expression, Now, isUpperLimit: true);

        AssertDate(expected, result);
    }

    [Theory]
    [InlineData("2024-06-16T10:00:00Z", "2024-06-10T00:00:00+00:00")]
    [InlineData("2024-06-10T10:00:00Z", "2024-06-10T00:00:00+00:00")]
    [InlineData("2024-06-09T10:00:00Z", "2024-06-03T00:00:00+00:00")]
    public void Parse_WeekRounding_RoundsToMonday(string now, string expected)
    {
        var result = DateMath.Parse("now/w", DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));

        Assert.Equal(DayOfWeek.Monday, result.DayOfWeek);
        AssertDate(expected, result);
    }

    [Fact]
    public void Parse_BaseTimeWithOffset_UsesThatOffset()
    {
        var now = new DateTimeOffset(2024, 6, 15, 1, 0, 0, TimeSpan.FromHours(2));

        AssertDate("2024-06-15T00:00:00+02:00", DateMath.Parse("now/d", now));
        AssertDate("2024-01-15T00:00:00+02:00", DateMath.Parse("2024-01-15", now));
    }

    [Theory]
    [InlineData("2024-01-15", false, "2024-01-15T00:00:00+00:00")]
    [InlineData("2024-01-15", true, "2024-01-15T23:59:59.9999999+00:00")]
    [InlineData("2024-01", false, "2024-01-01T00:00:00+00:00")]
    [InlineData("2024-01", true, "2024-01-31T23:59:59.9999999+00:00")]
    [InlineData("2024-02", true, "2024-02-29T23:59:59.9999999+00:00")]
    [InlineData("2024", false, "2024-01-01T00:00:00+00:00")]
    [InlineData("2024", true, "2024-12-31T23:59:59.9999999+00:00")]
    [InlineData("2024-01-15T10", true, "2024-01-15T10:59:59.9999999+00:00")]
    [InlineData("2024-01-15T10:30", true, "2024-01-15T10:30:59.9999999+00:00")]
    [InlineData("2024-01-15T10:30:45", true, "2024-01-15T10:30:45.9999999+00:00")]
    [InlineData("2024-01-15T10:30:45.5", true, "2024-01-15T10:30:45.5+00:00")]
    [InlineData("2024-01-15T10:30:45,25", false, "2024-01-15T10:30:45.25+00:00")]
    [InlineData("2024-01-15T10:30:45.123456789", false, "2024-01-15T10:30:45.1234567+00:00")]
    [InlineData("2024-01-15 10:30:45", false, "2024-01-15T10:30:45+00:00")]
    [InlineData("20240115", false, "2024-01-15T00:00:00+00:00")]
    [InlineData("2024-01-15||", true, "2024-01-15T23:59:59.9999999+00:00")]
    [InlineData("2024-01-15||+1M/d", false, "2024-02-15T00:00:00+00:00")]
    [InlineData("2024-01-15||+1d", true, "2024-01-16T00:00:00+00:00")]
    [InlineData("2024-01-15||/M", true, "2024-01-31T23:59:59.9999999+00:00")]
    [InlineData("2024-01-15T10:30:00Z", false, "2024-01-15T10:30:00+00:00")]
    [InlineData("2024-01-15T10:30:00+05:30", false, "2024-01-15T10:30:00+05:30")]
    [InlineData("2024-01-15T10:30:00-0800", false, "2024-01-15T10:30:00-08:00")]
    [InlineData("2024-01-15T10:30:00+05", false, "2024-01-15T10:30:00+05:00")]
    [InlineData("2024-01-15T10:30:00+05:30||+1d/d", false, "2024-01-16T00:00:00+05:30")]
    [InlineData("2024-01-31||+1M", false, "2024-02-29T00:00:00+00:00")]
    [InlineData("2024-02-29||+1y", false, "2025-02-28T00:00:00+00:00")]
    [InlineData("2024-02-29||-4y", false, "2020-02-29T00:00:00+00:00")]
    [InlineData("2024-12-31||+1d", false, "2025-01-01T00:00:00+00:00")]
    public void Parse_ExplicitDate_ReturnsExpectedDate(string expression, bool isUpperLimit, string expected)
    {
        var result = DateMath.Parse(expression, Now, isUpperLimit);

        AssertDate(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("now+")]
    [InlineData("now+1x")]
    [InlineData("now/d+1h")]
    [InlineData("now/1d")]
    [InlineData("now+1D")]
    [InlineData("now+1Y")]
    [InlineData("now-1W")]
    [InlineData("now+1S")]
    [InlineData("2024-13-01")]
    [InlineData("2024-00-10")]
    [InlineData("2024-02-30")]
    [InlineData("2023-02-29")]
    [InlineData("2024-01-15T25:00")]
    [InlineData("2024-01-15T10:60")]
    [InlineData("2024-01-15T10:30:00+19:00")]
    [InlineData("Jan 15")]
    [InlineData("12345")]
    public void TryParse_InvalidExpression_ReturnsFalse(string expression)
    {
        Assert.False(DateMath.TryParse(expression, Now, false, out _));
        Assert.False(DateMath.TryParse(expression, Now, true, out _));
        Assert.False(DateMath.TryParse(expression, Now, Chicago, false, out _));
        Assert.False(DateMath.IsValidExpression(expression));
    }

    [Theory]
    [InlineData("now+999999999y")]
    [InlineData("now-999999999y")]
    [InlineData("now+999999999M")]
    [InlineData("now+999999999w")]
    [InlineData("now-999999999d")]
    [InlineData("now+999999999h")]
    [InlineData("9999-12-31||+1d")]
    [InlineData("0001-01-01||-1d")]
    public void TryParse_ResultOutOfRange_ReturnsFalse(string expression)
    {
        Assert.False(DateMath.TryParse(expression, Now, false, out _));
        Assert.False(DateMath.TryParse(expression, Now, NewYork, false, out _));
    }

    [Fact]
    public void Parse_InvalidExpression_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => DateMath.Parse("now+1x", Now));

        Assert.Contains("now+1x", exception.Message);
        Assert.Throws<ArgumentException>(() => DateMath.Parse("bogus", Now, Chicago));
    }

    [Fact]
    public void TryParse_NullExpression_ReturnsFalse()
    {
        Assert.False(DateMath.TryParse(null!, Now, false, out _));
        Assert.False(DateMath.IsValidExpression(null));
    }

    [Fact]
    public void TryParse_NullTimeZone_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DateMath.TryParse("now", Now, null!, false, out _));
        Assert.Throws<ArgumentNullException>(() => DateMath.Parse("now", Now, (TimeZoneInfo)null!));
    }

    [Fact]
    public void Parse_UppercaseAndLowercaseM_AreMonthsAndMinutes()
    {
        AssertDate("2024-07-15T14:30:45.123+00:00", DateMath.Parse("now+1M", Now));
        AssertDate("2024-06-15T14:31:45.123+00:00", DateMath.Parse("now+1m", Now));
    }

    [Theory]
    [InlineData("01/15/2024", false, "2024-01-15T00:00:00+00:00")]
    [InlineData("01/15/2024", true, "2024-01-15T23:59:59.9999999+00:00")]
    [InlineData("01/15/2024 10:30", true, "2024-01-15T10:30:00+00:00")]
    [InlineData("January 15, 2024", false, "2024-01-15T00:00:00+00:00")]
    [InlineData("15 Jan 2024", false, "2024-01-15T00:00:00+00:00")]
    [InlineData("Mon, 15 Jan 2024 10:30:00 GMT", false, "2024-01-15T10:30:00+00:00")]
    [InlineData("2024-01-15 10:30:00 -05:00", false, "2024-01-15T10:30:00-05:00")]
    [InlineData("2024-01-15 00:00:00 -05:00", true, "2024-01-15T23:59:59.9999999-05:00")]
    public void Parse_OtherDateFormats_UsesInvariantFallback(string expression, bool isUpperLimit, string expected)
    {
        var result = DateMath.Parse(expression, Now, isUpperLimit);

        AssertDate(expected, result);
    }

    [Fact]
    public void Parse_FallbackWithTimeZone_UsesZoneOffsetForThatDate()
    {
        AssertDate("2024-01-15T00:00:00-06:00", DateMath.Parse("01/15/2024", Now, Chicago));
        AssertDate("2024-07-15T00:00:00-05:00", DateMath.Parse("07/15/2024", Now, Chicago));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void Parse_NonInvariantCurrentCulture_IsCultureInvariant(string culture)
    {
        // Arrange
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            // Act
            var iso = DateMath.Parse("2024-01-15||+1M/d", Now);
            var fallback = DateMath.Parse("01/02/2024", Now);

            // Assert
            AssertDate("2024-02-15T00:00:00+00:00", iso);
            AssertDate("2024-01-02T00:00:00+00:00", fallback);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("now", "2024-06-15T09:30:45.123-05:00")]
    [InlineData("now/d", "2024-06-15T00:00:00-05:00")]
    [InlineData("now-1d/d", "2024-06-14T00:00:00-05:00")]
    [InlineData("now/M", "2024-06-01T00:00:00-05:00")]
    [InlineData("now-6M/M", "2023-12-01T00:00:00-06:00")]
    [InlineData("2024-01-15", "2024-01-15T00:00:00-06:00")]
    [InlineData("2024-07-15T12:00", "2024-07-15T12:00:00-05:00")]
    [InlineData("2024-01-15T10:00:00Z", "2024-01-15T10:00:00+00:00")]
    [InlineData("2024-01-15T10:00:00+01:00||/d", "2024-01-15T00:00:00+01:00")]
    public void Parse_TimeZone_UsesWallClockAndPerDateOffsets(string expression, string expected)
    {
        var result = DateMath.Parse(expression, Now, Chicago);

        AssertDate(expected, result);
    }

    [Fact]
    public void Parse_TimeZoneUpperLimit_RoundsToEndOfLocalPeriod()
    {
        AssertDate("2024-06-15T23:59:59.9999999-05:00", DateMath.Parse("now/d", Now, Chicago, isUpperLimit: true));
        AssertDate("2024-01-31T23:59:59.9999999-06:00", DateMath.Parse("2024-01", Now, Chicago, isUpperLimit: true));
    }

    [Fact]
    public void Parse_HalfHourTimeZone_RoundsToLocalMidnight()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var now = new DateTimeOffset(2024, 6, 15, 20, 0, 0, TimeSpan.Zero);

        AssertDate("2024-06-16T00:00:00+05:30", DateMath.Parse("now/d", now, kolkata));
    }

    [Theory]
    [InlineData("2024-03-10", false, "2024-03-10T00:00:00-05:00")]
    [InlineData("2024-03-10", true, "2024-03-10T23:59:59.9999999-04:00")]
    [InlineData("2024-03-10T01:59", false, "2024-03-10T01:59:00-05:00")]
    [InlineData("2024-03-10T03:00", false, "2024-03-10T03:00:00-04:00")]
    [InlineData("2024-03-10T12:00", false, "2024-03-10T12:00:00-04:00")]
    [InlineData("2024-03-10T02:30", false, "2024-03-10T03:30:00-04:00")]
    [InlineData("2024-03-09T12:00||+1d", false, "2024-03-10T12:00:00-04:00")]
    [InlineData("2024-03-09T02:30||+1d", false, "2024-03-10T03:30:00-04:00")]
    [InlineData("2024-03-11T12:00||-1d", false, "2024-03-10T12:00:00-04:00")]
    [InlineData("2024-03-10T12:00||-1d", false, "2024-03-09T12:00:00-05:00")]
    [InlineData("2024-03-10T12:00||/d", false, "2024-03-10T00:00:00-05:00")]
    [InlineData("2024-11-03", false, "2024-11-03T00:00:00-04:00")]
    [InlineData("2024-11-03", true, "2024-11-03T23:59:59.9999999-05:00")]
    [InlineData("2024-11-03T00:59", false, "2024-11-03T00:59:00-04:00")]
    [InlineData("2024-11-03T01:30", false, "2024-11-03T01:30:00-04:00")]
    [InlineData("2024-11-03T02:00", false, "2024-11-03T02:00:00-05:00")]
    [InlineData("2024-11-03T12:00", false, "2024-11-03T12:00:00-05:00")]
    [InlineData("2024-11-02T12:00||+1d", false, "2024-11-03T12:00:00-05:00")]
    [InlineData("2024-11-02T01:30||+1d", false, "2024-11-03T01:30:00-04:00")]
    [InlineData("2024-11-03T12:00||/d", false, "2024-11-03T00:00:00-04:00")]
    public void Parse_NewYorkDaylightSavingBoundaries_UsesCorrectOffsets(string expression, bool isUpperLimit, string expected)
    {
        var result = DateMath.Parse(expression, Now, NewYork, isUpperLimit);

        AssertDate(expected, result);
    }

    [Theory]
    [InlineData("2024-03-10T16:00:00Z", "now", "2024-03-10T12:00:00-04:00")]
    [InlineData("2024-03-10T16:00:00Z", "now/d", "2024-03-10T00:00:00-05:00")]
    [InlineData("2024-03-10T16:00:00Z", "now-1d", "2024-03-09T12:00:00-05:00")]
    [InlineData("2024-03-10T16:00:00Z", "now-12h", "2024-03-09T23:00:00-05:00")]
    [InlineData("2024-03-10T06:30:00Z", "now+1h", "2024-03-10T03:30:00-04:00")]
    [InlineData("2024-03-10T06:30:00Z", "now+30m", "2024-03-10T03:00:00-04:00")]
    [InlineData("2024-03-10T06:30:00Z", "now+1d", "2024-03-11T01:30:00-04:00")]
    [InlineData("2024-11-03T05:30:00Z", "now", "2024-11-03T01:30:00-04:00")]
    [InlineData("2024-11-03T05:30:00Z", "now+1h", "2024-11-03T01:30:00-05:00")]
    [InlineData("2024-11-03T05:30:00Z", "now/h", "2024-11-03T01:00:00-04:00")]
    [InlineData("2024-11-03T05:30:00Z", "now/d", "2024-11-03T00:00:00-04:00")]
    [InlineData("2024-11-03T06:30:00Z", "now", "2024-11-03T01:30:00-05:00")]
    [InlineData("2024-11-03T06:30:00Z", "now/h", "2024-11-03T01:00:00-05:00")]
    [InlineData("2024-11-03T06:30:00Z", "now-1h", "2024-11-03T01:30:00-04:00")]
    [InlineData("2024-11-02T05:30:00Z", "now+1d", "2024-11-03T01:30:00-04:00")]
    [InlineData("2024-11-03T17:00:00Z", "now-1d", "2024-11-02T12:00:00-04:00")]
    public void Parse_NewYorkDaylightSavingBoundariesRelativeToNow_UsesCorrectOffsets(string now, string expression, string expected)
    {
        var result = DateMath.Parse(expression, DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), NewYork);

        AssertDate(expected, result);
    }

    [Fact]
    public void Parse_NowInTimeZone_IsSameInstantAsNow()
    {
        foreach (string id in new[] { "America/New_York", "America/Chicago", "Europe/London", "Australia/Sydney", "Asia/Kolkata" })
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            for (var instant = new DateTimeOffset(2024, 1, 1, 0, 30, 0, TimeSpan.Zero); instant.Year == 2024; instant = instant.AddHours(7))
            {
                var result = DateMath.Parse("now", instant, zone);

                Assert.Equal(instant, result);
                Assert.Equal(zone.GetUtcOffset(instant), result.Offset);
            }
        }
    }

    [Fact]
    public void Parse_DaylightSavingStartsAtMidnight_RoundsToFirstInstantOfDay()
    {
        // Arrange
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var now = new DateTimeOffset(2018, 11, 4, 15, 0, 0, TimeSpan.Zero);

        // Act
        var start = DateMath.Parse("now/d", now, saoPaulo);
        var previousEnd = DateMath.Parse("now-1d/d", now, saoPaulo, isUpperLimit: true);

        // Assert
        AssertDate("2018-11-04T01:00:00-02:00", start);
        AssertDate("2018-11-03T23:59:59.9999999-03:00", previousEnd);
        Assert.Equal(TimeSpan.FromTicks(1), start - previousEnd);
    }

    [Fact]
    public void Parse_TimeZoneWithSystemClock_UsesCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;

        var result = DateMath.Parse("now", Chicago);

        Assert.InRange(result, before, DateTimeOffset.UtcNow);
        Assert.Equal(Chicago.GetUtcOffset(result), result.Offset);
        Assert.True(DateMath.TryParse("now/d", Chicago, false, out var rounded));
        Assert.Equal(TimeSpan.Zero, rounded.TimeOfDay);
    }

    [Theory]
    [InlineData("now", true)]
    [InlineData("now-1d", true)]
    [InlineData("now/d", true)]
    [InlineData("2024-01-01||+1d", true)]
    [InlineData("2024-01-01||", true)]
    [InlineData("2024-01-01+1d", true)]
    [InlineData("2024-01-01/M", true)]
    [InlineData("2024-01-01", false)]
    [InlineData("2024-01-01T10:00:00Z", false)]
    [InlineData("nowhere", false)]
    [InlineData("now+1x", false)]
    [InlineData("hello", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDateMath_Expression_ReturnsExpected(string? expression, bool expected)
    {
        Assert.Equal(expected, DateMath.IsDateMath(expression));
    }

    [Theory]
    [InlineData("now", true)]
    [InlineData("now-1d/d", true)]
    [InlineData("2024-01-01", true)]
    [InlineData("January 1, 2024", true)]
    [InlineData("now+1x", false)]
    [InlineData("nowhere", false)]
    public void IsValidExpression_Expression_ReturnsExpected(string expression, bool expected)
    {
        Assert.Equal(expected, DateMath.IsValidExpression(expression));
    }

    [Fact]
    public void ApplyOperations_Operations_AppliesInDateOffset()
    {
        var date = new DateTimeOffset(2024, 1, 31, 10, 0, 0, TimeSpan.FromHours(-6));

        AssertDate("2024-02-29T00:00:00-06:00", DateMath.ApplyOperations(date, "+1M/d"));
        AssertDate("2024-02-29T23:59:59.9999999-06:00", DateMath.ApplyOperations(date, "+1M/d", isUpperLimit: true));
        Assert.Equal(date, DateMath.ApplyOperations(date, ""));
        Assert.Throws<ArgumentException>(() => DateMath.ApplyOperations(date, "+1x"));
        Assert.Throws<ArgumentException>(() => DateMath.ApplyOperations(date, "/d+1h"));
        Assert.Throws<ArgumentNullException>(() => DateMath.ApplyOperations(date, null!));
    }

    [Fact]
    public void AddTimeUnit_AndRoundToUnit_UseDateOffset()
    {
        var date = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(2));

        AssertDate("2024-06-15T16:30:00+02:00", DateMath.AddTimeUnit(date, 2, "h"));
        AssertDate("2024-06-01T00:00:00+02:00", DateMath.RoundToUnit(date, "M"));
        AssertDate("2024-06-30T23:59:59.9999999+02:00", DateMath.RoundToUnit(date, "M", isUpperLimit: true));
        Assert.Throws<ArgumentException>(() => DateMath.AddTimeUnit(date, 1, "x"));
        Assert.Throws<ArgumentException>(() => DateMath.AddTimeUnit(DateTimeOffset.MaxValue, 1, "d"));
        Assert.Throws<ArgumentException>(() => DateMath.RoundToUnit(date, "q"));
    }

    [Fact]
    public void TryParse_ConcurrentCalls_ProduceConsistentResults()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var now = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i);
            if (!DateMath.TryParse("now-1d/d", now, NewYork, false, out var result) || result != DateMath.Parse("now-1d/d", now, NewYork))
                failures.Add(now.ToString("o", CultureInfo.InvariantCulture));
        });

        Assert.Empty(failures);
    }

    private static void AssertDate(string expected, DateTimeOffset actual)
    {
        var expectedDate = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);
        Assert.Equal(expectedDate.ToString("o", CultureInfo.InvariantCulture), actual.ToString("o", CultureInfo.InvariantCulture));
    }
}
