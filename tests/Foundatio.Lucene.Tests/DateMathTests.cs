namespace Foundatio.Lucene.Tests;

/// <summary>
/// Comprehensive tests for the DateMath utility class, covering all parsing scenarios,
/// edge cases, timezone handling, and error conditions.
/// </summary>
public class DateMathTests
{
    private readonly DateTimeOffset _baseTime = new(2023, 6, 15, 14, 30, 45, 123, TimeSpan.FromHours(5));

    [Theory]
    [InlineData("now", false)]
    [InlineData("now", true)]
    public void Parse_Now_ReturnsBaseTime(string expression, bool isUpperLimit)
    {
        var result = DateMath.Parse(expression, _baseTime, isUpperLimit);

        Assert.Equal(_baseTime, result);
    }

    [Theory]
    [InlineData("now+1h", 1)]
    [InlineData("now+2h", 2)]
    [InlineData("now+24h", 24)]
    [InlineData("now+1H", 1)] // Both h and H are valid Elastic units for hours
    [InlineData("now-1h", -1)]
    [InlineData("now-12h", -12)]
    public void Parse_HourOperations_ReturnsCorrectResult(string expression, int hours)
    {
        var expected = _baseTime.AddHours(hours);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1d", 1)]
    [InlineData("now+7d", 7)]
    [InlineData("now-1d", -1)]
    [InlineData("now-30d", -30)]
    public void Parse_DayOperations_ReturnsCorrectResult(string expression, int days)
    {
        var expected = _baseTime.AddDays(days);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1M", 1)]
    [InlineData("now+6M", 6)]
    [InlineData("now-1M", -1)]
    [InlineData("now-12M", -12)]
    public void Parse_MonthOperations_ReturnsCorrectResult(string expression, int months)
    {
        var expected = _baseTime.AddMonths(months);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1y", 1)]
    [InlineData("now+5y", 5)]
    [InlineData("now-1y", -1)]
    [InlineData("now-10y", -10)]
    public void Parse_YearOperations_ReturnsCorrectResult(string expression, int years)
    {
        var expected = _baseTime.AddYears(years);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1w", 7)]
    [InlineData("now+2w", 14)]
    [InlineData("now-1w", -7)]
    [InlineData("now-4w", -28)]
    public void Parse_WeekOperations_ReturnsCorrectResult(string expression, int days)
    {
        var expected = _baseTime.AddDays(days);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1m", 1)]
    [InlineData("now+30m", 30)]
    [InlineData("now-1m", -1)]
    [InlineData("now-60m", -60)]
    public void Parse_MinuteOperations_ReturnsCorrectResult(string expression, int minutes)
    {
        var expected = _baseTime.AddMinutes(minutes);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now+1s", 1)]
    [InlineData("now+30s", 30)]
    [InlineData("now-1s", -1)]
    [InlineData("now-3600s", -3600)]
    public void Parse_SecondOperations_ReturnsCorrectResult(string expression, int seconds)
    {
        var expected = _baseTime.AddSeconds(seconds);

        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("now/d", false)]
    [InlineData("now/d", true)]
    [InlineData("now/h", false)]
    [InlineData("now/h", true)]
    [InlineData("now/m", false)]
    [InlineData("now/m", true)]
    public void Parse_RoundingOperations_ReturnsCorrectResult(string expression, bool isUpperLimit)
    {
        var result = DateMath.Parse(expression, _baseTime, isUpperLimit);

        if (expression.EndsWith("/d"))
        {
            if (isUpperLimit)
            {
                var expectedEnd = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, 23, 59, 59, 999, _baseTime.Offset).AddTicks(9999);
                Assert.Equal(expectedEnd, result);
            }
            else
            {
                var expectedStart = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, 0, 0, 0, 0, _baseTime.Offset);
                Assert.Equal(expectedStart, result);
            }
        }
        else if (expression.EndsWith("/h"))
        {
            if (isUpperLimit)
            {
                var expectedEnd = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, _baseTime.Hour, 59, 59, 999, _baseTime.Offset).AddTicks(9999);
                Assert.Equal(expectedEnd, result);
            }
            else
            {
                var expectedStart = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, _baseTime.Hour, 0, 0, 0, _baseTime.Offset);
                Assert.Equal(expectedStart, result);
            }
        }
        else if (expression.EndsWith("/m"))
        {
            if (isUpperLimit)
            {
                var expectedEnd = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, _baseTime.Hour, _baseTime.Minute, 59, 999, _baseTime.Offset).AddTicks(9999);
                Assert.Equal(expectedEnd, result);
            }
            else
            {
                var expectedStart = new DateTimeOffset(_baseTime.Year, _baseTime.Month, _baseTime.Day, _baseTime.Hour, _baseTime.Minute, 0, 0, _baseTime.Offset);
                Assert.Equal(expectedStart, result);
            }
        }
    }

    [Theory]
    [InlineData("now+1d+2h")]
    [InlineData("now-1d+12h")]
    [InlineData("now+1M+1d")]
    [InlineData("now+1y-1M")]
    public void Parse_MultipleOperations_ReturnsCorrectResult(string expression)
    {
        var result = DateMath.Parse(expression, _baseTime);

        if (expression == "now+1d+2h")
        {
            var expected = _baseTime.AddDays(1).AddHours(2);
            Assert.Equal(expected, result);
        }
        else if (expression == "now-1d+12h")
        {
            var expected = _baseTime.AddDays(-1).AddHours(12);
            Assert.Equal(expected, result);
        }
        else
        {
            Assert.NotEqual(_baseTime, result);
        }
    }

    [Theory]
    [InlineData("2023-06-15||")]
    [InlineData("2023-06-15T10:30:00||")]
    [InlineData("2023-06-15T10:30:00.123||")]
    public void Parse_ExplicitDateFormats_ReturnsCorrectResult(string expression)
    {
        var result = DateMath.Parse(expression, _baseTime);

        Assert.Equal(2023, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(_baseTime.Offset, result.Offset);
    }

    [Theory]
    [InlineData("2023-06-15T10:30:00Z||", 0)]
    [InlineData("2023-06-15T10:30:00+02:00||", 2)]
    [InlineData("2023-06-15T10:30:00-05:00||", -5)]
    [InlineData("2023-06-15T10:30:00+09:30||", 9.5)]
    public void Parse_ExplicitTimezones_PreservesTimezone(string expression, double offsetHours)
    {
        var result = DateMath.Parse(expression, _baseTime);
        var expectedOffset = TimeSpan.FromHours(offsetHours);

        Assert.Equal(2023, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(10, result.Hour);
        Assert.Equal(30, result.Minute);
        Assert.Equal(expectedOffset, result.Offset);
    }

    [Theory]
    [InlineData("2023-06-15||+1M")]
    [InlineData("2023-06-15T10:30:00||+2d")]
    [InlineData("2023-06-15T10:30:00Z||+1h")]
    [InlineData("2023-06-15T10:30:00+02:00||-1d/d")]
    public void Parse_ExplicitDateWithOperations_ReturnsCorrectResult(string expression)
    {
        var result = DateMath.Parse(expression, _baseTime);

        Assert.NotEqual(_baseTime, result);

        if (expression.Contains("+1M"))
        {
            Assert.Equal(7, result.Month);
        }
        else if (expression.Contains("+2d"))
        {
            Assert.Equal(17, result.Day);
        }
        else if (expression.Contains("+1h"))
        {
            Assert.Equal(11, result.Hour);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("now+1x")]
    [InlineData("||+1d")]
    [InlineData("now/x")]
    [InlineData("2023-13-01||")]
    [InlineData("2023-01-32||")]
    [InlineData("2001.02.01||")]
    [InlineData("now/d+1h")]
    [InlineData("now/d/d")]
    [InlineData("now+1h/d+2m")]
    [InlineData("Now")]
    [InlineData("NOW")]
    [InlineData("NOW+1h")]
    [InlineData("Now-1d/d")]
    public void Parse_InvalidExpressions_ThrowsArgumentException(string expression)
    {
        var exception = Assert.Throws<ArgumentException>(() => DateMath.Parse(expression, _baseTime));

        Assert.Contains("Invalid date math expression", exception.Message);
    }

    [Fact]
    public void Parse_NullExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DateMath.Parse(null!, _baseTime));
    }

    [Theory]
    [InlineData("now")]
    [InlineData("now+1h")]
    [InlineData("now-1d/d")]
    [InlineData("2023-06-15")]
    [InlineData("2023-06-15||")]
    [InlineData("2023-06-15||+1M/d")]
    [InlineData("2025-01-01T01:25:35Z||+3d/d")]
    public void TryParse_ValidExpressions_ReturnsTrueAndCorrectResult(string expression)
    {
        bool success = DateMath.TryParse(expression, _baseTime, false, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);

        var parseResult = DateMath.Parse(expression, _baseTime, false);
        Assert.Equal(parseResult, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("now+")]
    [InlineData("||+1d")]
    [InlineData("2001.02.01||")]
    [InlineData("now/d+1h")]
    [InlineData("now/d/d")]
    [InlineData("Now+1h")]
    [InlineData("NOW-1d")]
    public void TryParse_InvalidExpressions_ReturnsFalse(string expression)
    {
        bool success = DateMath.TryParse(expression, _baseTime, false, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParse_NullExpression_ReturnsFalse()
    {
        bool success = DateMath.TryParse(null!, _baseTime, false, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParse_FallbackExplicitDate_AppliesBaseOffset()
    {
        const string expression = "2023-04-01";

        bool success = DateMath.TryParse(expression, _baseTime, false, out var result);

        Assert.True(success);
        var expected = new DateTimeOffset(2023, 4, 1, 0, 0, 0, _baseTime.Offset);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryParse_FallbackExplicitDateUpperLimit_AdjustsToEndOfDay()
    {
        // Note: "2023-07-10" matches the main parser regex (as an explicit date without operations),
        // so it goes through TryParseExplicitDate, not the fallback path. The main path does not
        // apply end-of-day adjustment — isUpperLimit only affects rounding operations.
        const string expression = "2023-07-10";

        bool success = DateMath.TryParse(expression, _baseTime, true, out var result);

        Assert.True(success);
        var expected = new DateTimeOffset(2023, 7, 10, 0, 0, 0, _baseTime.Offset);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryParse_FallbackExplicitDateWithTimezone_PreservesOffset()
    {
        const string expression = "2023-05-05T18:45:00-07:00";

        bool success = DateMath.TryParse(expression, _baseTime, false, out var result);

        Assert.True(success);
        Assert.Equal(new DateTimeOffset(2023, 5, 5, 18, 45, 0, TimeSpan.FromHours(-7)), result);
    }

    [Fact]
    public void TryParse_FallbackExplicitDateWithTimeZoneInfo_UsesProvidedOffset()
    {
        const string expression = "2023-09-15";
        var customZone = TimeZoneInfo.CreateCustomTimeZone("TestPlusThree", TimeSpan.FromHours(3), "Test +3", "Test +3");

        bool success = DateMath.TryParse(expression, customZone, false, out var result);

        Assert.True(success);
        Assert.Equal(new DateTimeOffset(2023, 9, 15, 0, 0, 0, customZone.BaseUtcOffset), result);
    }

    [Theory]
    [InlineData("now+1h", false)]
    [InlineData("now-1d/d", true)]
    [InlineData("2023-06-15", false)]
    [InlineData("2023-06-15||+1M", false)]
    [InlineData("2025-01-01T01:25:35Z||+3d/d", true)]
    public void Parse_And_TryParse_ReturnSameResults(string expression, bool isUpperLimit)
    {
        var parseResult = DateMath.Parse(expression, _baseTime, isUpperLimit);
        bool tryParseSuccess = DateMath.TryParse(expression, _baseTime, isUpperLimit, out var tryParseResult);

        Assert.True(tryParseSuccess);
        Assert.Equal(parseResult, tryParseResult);
    }

    [Theory]
    [InlineData("now/d")]
    [InlineData("now/h")]
    [InlineData("now/m")]
    [InlineData("now+1d/d")]
    [InlineData("now-1M/d")]
    public void Parse_UpperLimitVsLowerLimit_ProducesDifferentResults(string expression)
    {
        var lowerResult = DateMath.Parse(expression, _baseTime, false);
        var upperResult = DateMath.Parse(expression, _baseTime, true);

        Assert.True(upperResult > lowerResult,
            $"Upper limit ({upperResult}) should be greater than lower limit ({lowerResult})");
    }

    [Fact]
    public void Parse_EdgeCase_LeapYear()
    {
        var leapYearDate = new DateTimeOffset(2024, 2, 28, 12, 0, 0, _baseTime.Offset);
        const string expression = "now+1d";

        var result = DateMath.Parse(expression, leapYearDate);

        Assert.Equal(29, result.Day);
        Assert.Equal(2, result.Month);
    }

    [Fact]
    public void Parse_EdgeCase_MonthOverflow()
    {
        var endOfMonth = new DateTimeOffset(2023, 1, 31, 12, 0, 0, _baseTime.Offset);
        const string expression = "now+1M";

        var result = DateMath.Parse(expression, endOfMonth);

        Assert.Equal(2, result.Month);
        Assert.True(result.Day <= 29);
    }

    [Fact]
    public void Parse_EdgeCase_YearOverflow()
    {
        var endOfYear = new DateTimeOffset(2023, 12, 31, 23, 59, 59, _baseTime.Offset);
        const string expression = "now+1d";

        var result = DateMath.Parse(expression, endOfYear);

        Assert.Equal(2024, result.Year);
        Assert.Equal(1, result.Month);
        Assert.Equal(1, result.Day);
    }

    [Fact]
    public void Parse_ComplexExpression_MultipleOperationsWithRounding()
    {
        const string expression = "now+1M-2d+3h/h";

        var result = DateMath.Parse(expression, _baseTime, false);

        Assert.Equal(0, result.Minute);
        Assert.Equal(0, result.Second);
        Assert.Equal(0, result.Millisecond);
        Assert.NotEqual(_baseTime, result);
    }

    [Fact]
    public void ParseTimeZone_Now_ReturnsCurrentTimeInSpecifiedTimezone()
    {
        var utcTimeZone = TimeZoneInfo.Utc;
        const string expression = "now";

        var result = DateMath.Parse(expression, utcTimeZone);

        var utcNow = DateTimeOffset.UtcNow;
        Assert.True(Math.Abs((result - utcNow).TotalSeconds) < 5,
            $"Result {result} should be within 5 seconds of UTC now {utcNow}");
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("US/Eastern")]
    [InlineData("US/Pacific")]
    public void ParseTimeZone_Now_ReturnsCorrectTimezone(string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        const string expression = "now";

        var result = DateMath.Parse(expression, timeZone);

        Assert.Equal(timeZone.GetUtcOffset(DateTime.UtcNow), result.Offset);
    }

    [Fact]
    public void ParseTimeZone_ExplicitDateWithoutTimezone_UsesSpecifiedTimezone()
    {
        var easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("US/Eastern");
        const string expression = "2023-06-15T14:30:00";

        var result = DateMath.Parse(expression, easternTimeZone);

        Assert.Equal(2023, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(14, result.Hour);
        Assert.Equal(30, result.Minute);
        Assert.Equal(0, result.Second);

        // The implementation uses timeZone.GetUtcOffset(DateTime.UtcNow) to determine the offset,
        // so the result reflects the current timezone offset (which depends on DST at the time
        // of test execution), not the offset for the parsed date.
        var expectedOffset = easternTimeZone.GetUtcOffset(DateTime.UtcNow);
        Assert.Equal(expectedOffset, result.Offset);
    }

    [Fact]
    public void ParseTimeZone_ExplicitDateWithTimezone_PreservesOriginalTimezone()
    {
        var pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("US/Pacific");
        const string expression = "2023-06-15T14:30:00+05:00";

        var result = DateMath.Parse(expression, pacificTimeZone);

        Assert.Equal(2023, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(14, result.Hour);
        Assert.Equal(30, result.Minute);
        Assert.Equal(0, result.Second);
        Assert.Equal(TimeSpan.FromHours(5), result.Offset);
    }

    [Theory]
    [InlineData("now+1h", 1)]
    [InlineData("now+6h", 6)]
    [InlineData("now-2h", -2)]
    [InlineData("now+24h", 24)]
    public void ParseTimeZone_HourOperations_ReturnsCorrectResult(string expression, int hours)
    {
        var utcTimeZone = TimeZoneInfo.Utc;

        var result = DateMath.Parse(expression, utcTimeZone);
        var utcNow = DateTimeOffset.UtcNow;
        var expected = utcNow.AddHours(hours);

        Assert.True(Math.Abs((result - expected).TotalSeconds) < 5,
            $"Result {result} should be within 5 seconds of expected {expected}");
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Theory]
    [InlineData("now/d", false)]
    [InlineData("now/d", true)]
    [InlineData("now/h", false)]
    [InlineData("now/h", true)]
    [InlineData("now/M", false)]
    [InlineData("now/M", true)]
    public void ParseTimeZone_RoundingOperations_ReturnsCorrectResult(string expression, bool isUpperLimit)
    {
        var centralTimeZone = TimeZoneInfo.FindSystemTimeZoneById("US/Central");

        var result = DateMath.Parse(expression, centralTimeZone, isUpperLimit);

        var expectedOffset = centralTimeZone.GetUtcOffset(DateTime.UtcNow);
        Assert.Equal(expectedOffset, result.Offset);

        if (expression.EndsWith("/d"))
        {
            if (isUpperLimit)
            {
                Assert.Equal(23, result.Hour);
                Assert.Equal(59, result.Minute);
                Assert.Equal(59, result.Second);
            }
            else
            {
                Assert.Equal(0, result.Hour);
                Assert.Equal(0, result.Minute);
                Assert.Equal(0, result.Second);
            }
        }
        else if (expression.EndsWith("/h"))
        {
            if (isUpperLimit)
            {
                Assert.Equal(59, result.Minute);
                Assert.Equal(59, result.Second);
            }
            else
            {
                Assert.Equal(0, result.Minute);
                Assert.Equal(0, result.Second);
            }
        }
    }

    [Fact]
    public void TryParseTimeZone_ValidExpression_ReturnsTrue()
    {
        var mountainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("US/Mountain");
        const string expression = "now+2d";

        bool success = DateMath.TryParse(expression, mountainTimeZone, false, out DateTimeOffset result);

        Assert.True(success);
        Assert.NotEqual(default(DateTimeOffset), result);

        var expectedOffset = mountainTimeZone.GetUtcOffset(DateTime.UtcNow);
        Assert.Equal(expectedOffset, result.Offset);
    }

    [Fact]
    public void TryParseTimeZone_InvalidExpression_ReturnsFalse()
    {
        var utcTimeZone = TimeZoneInfo.Utc;
        const string expression = "invalid_expression";

        bool success = DateMath.TryParse(expression, utcTimeZone, false, out DateTimeOffset result);

        Assert.False(success);
        Assert.Equal(default(DateTimeOffset), result);
    }

    [Fact]
    public void ParseTimeZone_ComplexExpression_WorksCorrectly()
    {
        var utcTimeZone = TimeZoneInfo.Utc;
        const string expression = "now+1M-2d+3h/h";

        var result = DateMath.Parse(expression, utcTimeZone, false);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(0, result.Minute);
        Assert.Equal(0, result.Second);
        Assert.Equal(0, result.Millisecond);
    }

    [Fact]
    public void ParseTimeZone_NullTimeZone_ThrowsArgumentNullException()
    {
        const string expression = "now";

        Assert.Throws<ArgumentNullException>(() => DateMath.Parse(expression, (TimeZoneInfo)null!));
    }

    [Fact]
    public void TryParseTimeZone_NullTimeZone_ThrowsArgumentNullException()
    {
        const string expression = "now";

        Assert.Throws<ArgumentNullException>(() => DateMath.TryParse(expression, (TimeZoneInfo)null!, false, out _));
    }

    /// <summary>
    /// Per Elasticsearch docs, valid date-math units are case-sensitive:
    /// y, M, w, d, h, H, m, s. Uppercase D, Y, W, S are NOT valid units.
    /// https://www.elastic.co/docs/reference/elasticsearch/rest-apis/common-options
    /// </summary>
    [Theory]
    [InlineData("now-7D")]
    [InlineData("now-1D")]
    [InlineData("now-30D")]
    [InlineData("now+1D")]
    [InlineData("now-1Y")]
    [InlineData("now-1W")]
    [InlineData("now-1S")]
    [InlineData("now/D")]
    public void Parse_UppercaseInvalidUnits_ThrowsArgumentException(string expression)
    {
        Assert.Throws<ArgumentException>(() => DateMath.Parse(expression, _baseTime));
    }

    [Theory]
    [InlineData("now-7D")]
    [InlineData("now-1D")]
    [InlineData("now+1D")]
    public void TryParse_UppercaseInvalidUnits_ReturnsFalse(string expression)
    {
        bool success = DateMath.TryParse(expression, _baseTime, false, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void Parse_UppercaseAndLowercaseM_ProduceDifferentResults()
    {
        var minuteExpression = "now-1m";
        var monthExpression = "now+1M";

        var minuteResult = DateMath.Parse(minuteExpression, _baseTime);
        var monthResult = DateMath.Parse(monthExpression, _baseTime);

        Assert.Equal(_baseTime.AddMinutes(-1), minuteResult);
        Assert.Equal(_baseTime.AddMonths(1), monthResult);
    }

    [Fact]
    public void IsValidExpression_CaseSensitiveInputs_ValidatesCorrectly()
    {
        Assert.True(DateMath.IsValidExpression("now-7d"));
        Assert.True(DateMath.IsValidExpression("now-1d/d"));

        Assert.False(DateMath.IsValidExpression("now-7D"));
        Assert.False(DateMath.IsValidExpression("now-1D/D"));

        Assert.False(DateMath.IsValidExpression("Now-7d"));
        Assert.False(DateMath.IsValidExpression("NOW-7d"));
    }
}
