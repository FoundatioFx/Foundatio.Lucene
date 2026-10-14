using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundatio.Lucene;

/// <summary>
/// Evaluates Elasticsearch date math expressions such as <c>now-1d/d</c>, <c>2024-01-01||+1M/d</c>, or
/// <c>2024-01-01T10:30:00Z</c>.
/// </summary>
/// <remarks>
/// <para>An expression is an anchor (<c>now</c> or a date), an optional <c>||</c>, and zero or more operations:
/// <c>+N</c>/<c>-N</c> followed by a unit adds or subtracts, and a trailing <c>/unit</c> rounds. Units are
/// <c>y</c> (years), <c>M</c> (months), <c>w</c> (weeks), <c>d</c> (days), <c>h</c>/<c>H</c> (hours),
/// <c>m</c> (minutes), and <c>s</c> (seconds); they are case-sensitive.</para>
/// <para>Rounding and adding years, months, weeks, or days happen on the local wall clock of the time zone, so
/// <c>now/d</c> in America/Chicago is midnight in Chicago and adding days across a daylight-saving change keeps the
/// time of day. Hours, minutes, and seconds are added to the instant, as Elasticsearch does. Dates without an
/// offset are interpreted in the time zone using the offset in effect on that date. A wall-clock time skipped by a
/// daylight-saving change moves forward by the length of the gap, and a repeated wall-clock time keeps the current
/// offset when it can and otherwise uses the earlier one.</para>
/// <para>For upper limits (<c>isUpperLimit</c>) rounding goes to the end of the period, and a date that
/// omits components is rounded up the same way Elasticsearch does: <c>2024-01</c> becomes the last instant of
/// January.</para>
/// </remarks>
public static class DateMath
{
    private static readonly Regex ExpressionRegex = new(
        @"^(?:(?<now>now)|(?<y>\d{4})(?:-?(?<mo>\d{2})(?:-?(?<d>\d{2}))?)?(?:[T ](?<h>\d{1,2})(?::?(?<mi>\d{2})(?::?(?<s>\d{2})(?:[.,](?<f>\d{1,9}))?)?)?)?(?<off>Z|[+-]\d{2}(?::?\d{2})?)?)(?:\|\|)?(?<ops>(?:[+-]\d{0,9}[yMwdhHms]|/[yMwdhHms])*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));

    private static readonly Regex OperationRegex = new(@"([+\-/])(\d{0,9})([yMwdhHms])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private enum Precision
    {
        Instant,
        Year,
        Month,
        Day,
        Hour,
        Minute,
        Second
    }

    /// <summary>
    /// Evaluates an expression using <paramref name="relativeBaseTime"/> as <c>now</c>. Dates without an offset use the
    /// offset of <paramref name="relativeBaseTime"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid date math.</exception>
    public static DateTimeOffset Parse(string expression, DateTimeOffset relativeBaseTime, bool isUpperLimit = false)
    {
        return TryParse(expression, relativeBaseTime, isUpperLimit, out var result)
            ? result
            : throw new ArgumentException($"Invalid date math expression: {expression}", nameof(expression));
    }

    /// <summary>
    /// Evaluates an expression using <paramref name="relativeBaseTime"/> as <c>now</c>. Dates without an offset use the
    /// offset of <paramref name="relativeBaseTime"/>.
    /// </summary>
    public static bool TryParse(string expression, DateTimeOffset relativeBaseTime, bool isUpperLimit, out DateTimeOffset result)
    {
        return TryEvaluate(expression, relativeBaseTime, timeZone: null, isUpperLimit, out result);
    }

    /// <summary>
    /// Evaluates an expression in <paramref name="timeZone"/> using the system clock for <c>now</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid date math.</exception>
    public static DateTimeOffset Parse(string expression, TimeZoneInfo timeZone, bool isUpperLimit = false)
    {
        return Parse(expression, DateTimeOffset.UtcNow, timeZone, isUpperLimit);
    }

    /// <summary>
    /// Evaluates an expression in <paramref name="timeZone"/> using the system clock for <c>now</c>.
    /// </summary>
    public static bool TryParse(string expression, TimeZoneInfo timeZone, bool isUpperLimit, out DateTimeOffset result)
    {
        return TryParse(expression, DateTimeOffset.UtcNow, timeZone, isUpperLimit, out result);
    }

    /// <summary>
    /// Evaluates an expression in <paramref name="timeZone"/>, using <paramref name="now"/> as the current instant.
    /// </summary>
    /// <exception cref="ArgumentException">The expression is not valid date math.</exception>
    public static DateTimeOffset Parse(string expression, DateTimeOffset now, TimeZoneInfo timeZone, bool isUpperLimit = false)
    {
        return TryParse(expression, now, timeZone, isUpperLimit, out var result)
            ? result
            : throw new ArgumentException($"Invalid date math expression: {expression}", nameof(expression));
    }

    /// <summary>
    /// Evaluates an expression in <paramref name="timeZone"/>, using <paramref name="now"/> as the current instant.
    /// </summary>
    public static bool TryParse(string expression, DateTimeOffset now, TimeZoneInfo timeZone, bool isUpperLimit, out DateTimeOffset result)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return TryEvaluate(expression, now, timeZone, isUpperLimit, out result);
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is a date math expression or a date.
    /// </summary>
    public static bool IsValidExpression(string? expression)
    {
        return expression is not null && TryEvaluate(expression, DateTimeOffset.UtcNow, timeZone: null, isUpperLimit: false, out _);
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is relative to the current time (starts with <c>now</c>) or contains
    /// date math operations.
    /// </summary>
    public static bool IsDateMath(string? expression)
    {
        if (string.IsNullOrEmpty(expression))
            return false;

        var match = MatchExpression(expression);
        return match is { Success: true }
            && (match.Groups["now"].Success || match.Groups["ops"].Length > 0 || expression.Contains("||", StringComparison.Ordinal));
    }

    private static Match? MatchExpression(string expression)
    {
        if (expression.Length is 0 or > 64)
            return null;

        try
        {
            return ExpressionRegex.Match(expression);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static bool TryEvaluate(string expression, DateTimeOffset now, TimeZoneInfo? timeZone, bool isUpperLimit, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var match = MatchExpression(expression);
        if (match is not { Success: true })
            return TryParseFallback(expression, now, timeZone, isUpperLimit, out result);

        Clock clock;
        DateTime local = default;
        var precision = Precision.Instant;
        bool isNow = match.Groups["now"].Success;

        if (isNow)
        {
            clock = new Clock(timeZone, now.Offset);
        }
        else
        {
            if (!TryCreateDate(match, out local, out precision))
                return false;

            var offsetGroup = match.Groups["off"];
            if (offsetGroup.Success)
            {
                if (!TryParseOffset(offsetGroup.Value, out var fixedOffset))
                    return false;
                clock = new Clock(null, fixedOffset);
            }
            else
            {
                clock = new Clock(timeZone, now.Offset);
            }
        }

        string operations = match.Groups["ops"].Value;
        try
        {
            if (operations.Length == 0 && isUpperLimit && precision != Precision.Instant)
                local = RoundUp(local, precision);

            var anchor = isNow ? clock.FromInstant(now) : clock.FromLocal(local);
            result = ApplyOperations(anchor, operations, isUpperLimit, clock);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryCreateDate(Match match, out DateTime date, out Precision precision)
    {
        date = default;
        precision = Precision.Year;

        int year = ParseInt(match.Groups["y"].Value);
        int month = 1, day = 1, hour = 0, minute = 0, second = 0;
        long fractionTicks = 0;

        if (match.Groups["mo"].Success)
        {
            month = ParseInt(match.Groups["mo"].Value);
            precision = Precision.Month;
        }

        if (match.Groups["d"].Success)
        {
            day = ParseInt(match.Groups["d"].Value);
            precision = Precision.Day;
        }

        if (match.Groups["h"].Success)
        {
            hour = ParseInt(match.Groups["h"].Value);
            precision = Precision.Hour;
        }

        if (match.Groups["mi"].Success)
        {
            minute = ParseInt(match.Groups["mi"].Value);
            precision = Precision.Minute;
        }

        if (match.Groups["s"].Success)
        {
            second = ParseInt(match.Groups["s"].Value);
            precision = Precision.Second;
        }

        if (match.Groups["f"].Success)
        {
            string fraction = match.Groups["f"].Value;
            fractionTicks = ParseInt(fraction.Length > 7 ? fraction[..7] : fraction.PadRight(7, '0'));
            precision = Precision.Instant;
        }

        if (month is < 1 or > 12 || year < 1 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return false;

        date = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionTicks);
        return true;
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool TryParseOffset(string value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (value == "Z")
            return true;

        int sign = value[0] == '-' ? -1 : 1;
        string digits = value[1..].Replace(":", "", StringComparison.Ordinal);
        int hours = ParseInt(digits[..2]);
        int minutes = digits.Length >= 4 ? ParseInt(digits[2..4]) : 0;
        if (hours > 18 || minutes > 59)
            return false;

        offset = new TimeSpan(sign * hours, sign * minutes, 0);
        return true;
    }

    private static bool TryParseFallback(string expression, DateTimeOffset now, TimeZoneInfo? timeZone, bool isUpperLimit, out DateTimeOffset result)
    {
        result = default;
        if (!ContainsYear(expression))
            return false;

        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;
        try
        {
            if (HasExplicitOffset(expression) && DateTimeOffset.TryParse(expression, CultureInfo.InvariantCulture, styles, out result))
            {
                if (isUpperLimit && result.TimeOfDay == TimeSpan.Zero)
                    result = new DateTimeOffset(RoundUp(result.DateTime, Precision.Day), result.Offset);
                return true;
            }

            if (!DateTime.TryParse(expression, CultureInfo.InvariantCulture, styles, out var local))
                return false;

            if (isUpperLimit && local.TimeOfDay == TimeSpan.Zero)
                local = RoundUp(local, Precision.Day);

            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            result = new Clock(timeZone, now.Offset).FromLocal(local);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
    }

    private static bool ContainsYear(string expression)
    {
        int run = 0;
        foreach (char c in expression)
        {
            run = char.IsAsciiDigit(c) ? run + 1 : 0;
            if (run == 4)
                return true;
        }

        return false;
    }

    private static bool HasExplicitOffset(string expression)
    {
        return expression.EndsWith('Z') || expression.EndsWith("GMT", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(expression, @"[+-]\d{2}:?\d{2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Applies date math operations (for example <c>+1d/d</c>) to a date. Arithmetic happens in the date's own offset.
    /// </summary>
    /// <exception cref="ArgumentException">The operations are invalid or the result is out of range.</exception>
    public static DateTimeOffset ApplyOperations(DateTimeOffset baseTime, string operations, bool isUpperLimit = false)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return ApplyOperations(baseTime, operations, isUpperLimit, new Clock(null, baseTime.Offset));
    }

    private static DateTimeOffset ApplyOperations(DateTimeOffset baseTime, string operations, bool isUpperLimit, Clock clock)
    {
        if (operations.Length == 0)
            return baseTime;

        var matches = OperationRegex.Matches(operations);
        int matched = 0;
        foreach (Match match in matches)
            matched += match.Length;

        if (matched != operations.Length)
            throw new ArgumentException($"Invalid date math operations: {operations}", nameof(operations));

        var result = baseTime;
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string operation = match.Groups[1].Value;
            string amountText = match.Groups[2].Value;
            string unit = match.Groups[3].Value;

            if (operation == "/")
            {
                if (i != matches.Count - 1)
                    throw new ArgumentException("Rounding must be the final date math operation.", nameof(operations));
                if (amountText.Length > 0)
                    throw new ArgumentException("Rounding does not take an amount.", nameof(operations));

                result = clock.FromLocal(isUpperLimit ? RoundUp(result.DateTime, unit) : RoundDown(result.DateTime, unit), result.Offset);
                continue;
            }

            int amount = amountText.Length == 0 ? 1 : ParseInt(amountText);
            result = Add(result, operation == "-" ? -amount : amount, unit, clock);
        }

        return result;
    }

    private static DateTimeOffset Add(DateTimeOffset date, long amount, string unit, Clock clock)
    {
        if (unit is not ("h" or "H" or "m" or "s"))
            return clock.FromLocal(Add(date.DateTime, amount, unit), date.Offset);

        try
        {
            var instant = unit == "s" ? date.AddSeconds(amount) : unit == "m" ? date.AddMinutes(amount) : date.AddHours(amount);
            return clock.FromInstant(instant);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException($"Date math result is out of range: {amount}{unit}", nameof(amount), ex);
        }
    }

    /// <summary>
    /// Adds an amount of a date math unit to a date.
    /// </summary>
    /// <exception cref="ArgumentException">The unit is invalid or the result is out of range.</exception>
    public static DateTimeOffset AddTimeUnit(DateTimeOffset dateTime, int amount, string unit)
    {
        return new DateTimeOffset(Add(dateTime.DateTime, amount, unit), dateTime.Offset);
    }

    /// <summary>
    /// Rounds a date to the start (or, for an upper limit, the end) of a date math unit.
    /// </summary>
    /// <exception cref="ArgumentException">The unit is invalid.</exception>
    public static DateTimeOffset RoundToUnit(DateTimeOffset dateTime, string unit, bool isUpperLimit = false)
    {
        var local = isUpperLimit ? RoundUp(dateTime.DateTime, unit) : RoundDown(dateTime.DateTime, unit);
        return new DateTimeOffset(local, dateTime.Offset);
    }

    private static DateTime Add(DateTime date, long amount, string unit)
    {
        try
        {
            return unit switch
            {
                "y" => date.AddYears(checked((int)amount)),
                "M" => date.AddMonths(checked((int)amount)),
                "w" => date.AddDays(checked(amount * 7)),
                "d" => date.AddDays(amount),
                "h" or "H" => date.AddHours(amount),
                "m" => date.AddMinutes(amount),
                "s" => date.AddSeconds(amount),
                _ => throw new ArgumentException($"Invalid date math unit: {unit}", nameof(unit))
            };
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            throw new ArgumentException($"Date math result is out of range: {amount}{unit}", nameof(amount), ex);
        }
    }

    private static DateTime RoundDown(DateTime date, string unit)
    {
        return unit switch
        {
            "y" => new DateTime(date.Year, 1, 1),
            "M" => new DateTime(date.Year, date.Month, 1),
            "w" => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            "d" => date.Date,
            "h" or "H" => new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0),
            "m" => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0),
            "s" => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second),
            _ => throw new ArgumentException($"Invalid date math rounding unit: {unit}", nameof(unit))
        };
    }

    private static DateTime RoundUp(DateTime date, string unit)
    {
        var start = RoundDown(date, unit);
        var next = unit switch
        {
            "y" => start.AddYears(1),
            "M" => start.AddMonths(1),
            "w" => start.AddDays(7),
            "d" => start.AddDays(1),
            "h" or "H" => start.AddHours(1),
            "m" => start.AddMinutes(1),
            _ => start.AddSeconds(1)
        };

        // The last instant of the period: Elasticsearch rounds up to the last millisecond; ticks keep full precision.
        return next.AddTicks(-1);
    }

    private static DateTime RoundUp(DateTime date, Precision precision)
    {
        return precision switch
        {
            Precision.Year => RoundUp(date, "y"),
            Precision.Month => RoundUp(date, "M"),
            Precision.Day => RoundUp(date, "d"),
            Precision.Hour => RoundUp(date, "h"),
            Precision.Minute => RoundUp(date, "m"),
            Precision.Second => RoundUp(date, "s"),
            _ => date
        };
    }

    /// <summary>
    /// Converts between local wall-clock times and instants in a time zone, or at a fixed offset when there is none.
    /// </summary>
    private readonly struct Clock(TimeZoneInfo? timeZone, TimeSpan fixedOffset)
    {
        public DateTimeOffset FromInstant(DateTimeOffset instant)
        {
            return timeZone is null ? instant.ToOffset(fixedOffset) : TimeZoneInfo.ConvertTime(instant, timeZone);
        }

        public DateTimeOffset FromLocal(DateTime local, TimeSpan? preferredOffset = null)
        {
            if (timeZone is null)
                return new DateTimeOffset(local, fixedOffset);

            if (timeZone.IsAmbiguousTime(local))
            {
                var offsets = timeZone.GetAmbiguousTimeOffsets(local);
                return new DateTimeOffset(local, preferredOffset is { } preferred && offsets.Contains(preferred) ? preferred : offsets.Max());
            }

            if (timeZone.IsInvalidTime(local))
            {
                var beforeGap = local;
                do
                    beforeGap = beforeGap.AddMinutes(-15);
                while (timeZone.IsInvalidTime(beforeGap));

                return TimeZoneInfo.ConvertTime(new DateTimeOffset(local, timeZone.GetUtcOffset(beforeGap)), timeZone);
            }

            return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        }
    }
}
