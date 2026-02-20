using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// A visitor that evaluates DateMath expressions in query terms and replaces them with their resolved values.
/// This visitor processes TermNode and RangeNode values, converting expressions like "now-1d" or "2024-01-01||+1M"
/// into their evaluated ISO 8601 date strings.
/// </summary>
public class DateMathEvaluatorVisitor : QueryVisitor
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo? _timeZone;
    private readonly string _dateFormat;

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor with the specified base time.
    /// When a fixed base time is provided, 'now' always resolves to this value.
    /// </summary>
    /// <param name="relativeBaseTime">The base time to use for relative calculations (e.g., 'now')</param>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(DateTimeOffset relativeBaseTime, string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
    {
        _timeProvider = new FixedTimeProvider(relativeBaseTime);
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor with the specified timezone.
    /// 'now' is resolved fresh at evaluation time using the given timezone.
    /// </summary>
    /// <param name="timeZone">The timezone to use for 'now' calculations and dates without explicit timezone information</param>
    /// <param name="timeProvider">Optional time provider for controlling 'now'. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(TimeZoneInfo timeZone, TimeProvider? timeProvider = null, string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
    {
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor using the specified time provider.
    /// 'now' is resolved fresh at evaluation time via the time provider, so this instance can be safely reused as a singleton.
    /// </summary>
    /// <param name="timeProvider">Optional time provider for controlling 'now'. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(TimeProvider? timeProvider = null, string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Visits a TermNode and evaluates any DateMath expression in its term value.
    /// </summary>
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        var term = node.Term;
        if (!string.IsNullOrEmpty(term) && TryEvaluateDateMath(term, isUpperLimit: false, out var evaluated))
        {
            node.Term = evaluated;
            // Also update unescaped term if it was set
            if (!string.IsNullOrEmpty(node.UnescapedTerm))
            {
                node.UnescapedTerm = evaluated;
            }
        }

        return node;
    }

    /// <summary>
    /// Visits a RangeNode and evaluates any DateMath expressions in its min/max values.
    /// </summary>
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        // Evaluate min value
        // Inclusive min ([): round down (start of period) — ">= start"
        // Exclusive min ({): round up (end of period) — "> end"
        if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
        {
            bool isUpperLimit = !node.MinInclusive;
            if (TryEvaluateDateMath(node.Min, isUpperLimit, out var evaluatedMin))
            {
                node.Min = evaluatedMin;
            }
        }

        // Evaluate max value
        // Inclusive max (]): round up (end of period) — "<= end"
        // Exclusive max (}): round down (start of period) — "< start"
        if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
        {
            bool isUpperLimit = node.MaxInclusive;
            if (TryEvaluateDateMath(node.Max, isUpperLimit, out var evaluatedMax))
            {
                node.Max = evaluatedMax;
            }
        }

        return node;
    }

    /// <summary>
    /// Tries to evaluate a DateMath expression and returns the formatted result.
    /// </summary>
    /// <param name="expression">The expression to evaluate</param>
    /// <param name="isUpperLimit">Whether this is for an upper limit (affects rounding behavior)</param>
    /// <param name="result">The formatted date string if successful</param>
    /// <returns>True if the expression was a valid DateMath expression and was evaluated</returns>
    private bool TryEvaluateDateMath(string expression, bool isUpperLimit, out string result)
    {
        result = expression;

        // Quick check: DateMath expressions either start with "now", contain "||",
        // or look like a date with operations (e.g., 2024-01-01+1M/d)
        if (!expression.StartsWith("now", StringComparison.OrdinalIgnoreCase) &&
            !expression.Contains("||") &&
            !LooksLikeDateMathWithOperations(expression))
        {
            return false;
        }

        bool success;
        DateTimeOffset evaluated;

        var now = _timeProvider.GetUtcNow();
        var baseTime = _timeZone is not null
            ? TimeZoneInfo.ConvertTime(now, _timeZone)
            : now;

        success = DateMath.TryParse(expression, baseTime, isUpperLimit, out evaluated);

        if (success)
        {
            result = evaluated.ToString(_dateFormat);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates all DateMath expressions in the given query.
    /// </summary>
    /// <param name="query">The query to process</param>
    /// <param name="context">The visitor context</param>
    /// <returns>The processed query with DateMath expressions evaluated</returns>
    public QueryNode Evaluate(QueryNode query, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        return Accept(query, context);
    }

    /// <summary>
    /// Creates a visitor and evaluates all DateMath expressions in the given query using UTC now as the base time.
    /// </summary>
    /// <param name="query">The query to process</param>
    /// <param name="context">The visitor context</param>
    /// <param name="relativeBaseTime">The base time to use for relative date calculations</param>
    /// <returns>The processed query with DateMath expressions evaluated</returns>
    public static QueryNode Evaluate(QueryNode query, IQueryVisitorContext? context, DateTimeOffset relativeBaseTime)
    {
        var visitor = new DateMathEvaluatorVisitor(relativeBaseTime);
        return visitor.Evaluate(query, context);
    }

    /// <summary>
    /// Creates a visitor and evaluates all DateMath expressions in the given query using the specified timezone.
    /// </summary>
    /// <param name="query">The query to process</param>
    /// <param name="context">The visitor context</param>
    /// <param name="timeZone">The timezone to use for 'now' calculations</param>
    /// <returns>The processed query with DateMath expressions evaluated</returns>
    public static QueryNode Evaluate(QueryNode query, IQueryVisitorContext? context, TimeZoneInfo timeZone)
    {
        var visitor = new DateMathEvaluatorVisitor(timeZone);
        return visitor.Evaluate(query, context);
    }

    /// <summary>
    /// Quick check if expression looks like a date followed by date math operations.
    /// Pattern: date-like string followed by +/-/digit/time-unit (e.g., 2024-01-01+1M or 2024-06-15-7d/d)
    /// </summary>
    private static bool LooksLikeDateMathWithOperations(ReadOnlySpan<char> expression)
    {
        // Must start with a date-like pattern (4 digits)
        if (expression.Length < 12 || !char.IsDigit(expression[0]))
            return false;

        // Look for date math operation pattern: [+-/] followed by optional digits and a time unit letter
        // Time units: y, M, w, d, h, H, m, s
        for (int i = 8; i < expression.Length - 1; i++)
        {
            char c = expression[i];
            if (c is '+' or '-' or '/')
            {
                // Check if followed by optional digits and a time unit
                int j = i + 1;
                while (j < expression.Length && char.IsDigit(expression[j]))
                    j++;

                if (j < expression.Length)
                {
                    char unit = expression[j];
                    if (unit is 'y' or 'M' or 'w' or 'd' or 'h' or 'H' or 'm' or 's')
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A simple TimeProvider that always returns a fixed time. Used when a specific
    /// base time is provided via the <see cref="DateMathEvaluatorVisitor(DateTimeOffset, string)"/> constructor.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }
}
