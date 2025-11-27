using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser.Visitors;

/// <summary>
/// A visitor that evaluates DateMath expressions in query terms and replaces them with their resolved values.
/// This visitor processes TermNode and RangeNode values, converting expressions like "now-1d" or "2024-01-01||+1M"
/// into their evaluated ISO 8601 date strings.
/// </summary>
public class DateMathEvaluatorVisitor : QueryNodeVisitor
{
    private readonly DateTimeOffset _relativeBaseTime;
    private readonly TimeZoneInfo? _timeZone;
    private readonly string _dateFormat;

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor with the specified base time.
    /// </summary>
    /// <param name="relativeBaseTime">The base time to use for relative calculations (e.g., 'now')</param>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(DateTimeOffset relativeBaseTime, string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
    {
        _relativeBaseTime = relativeBaseTime;
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor with the specified timezone.
    /// </summary>
    /// <param name="timeZone">The timezone to use for 'now' calculations and dates without explicit timezone information</param>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(TimeZoneInfo timeZone, string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
    {
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        _relativeBaseTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Creates a new DateMathEvaluatorVisitor using the current time in UTC.
    /// </summary>
    /// <param name="dateFormat">The format to use when outputting evaluated dates. Defaults to ISO 8601 with timezone.</param>
    public DateMathEvaluatorVisitor(string dateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz")
        : this(DateTimeOffset.UtcNow, dateFormat)
    {
    }

    /// <summary>
    /// Visits a TermNode and evaluates any DateMath expression in its term value.
    /// </summary>
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
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

        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a RangeNode and evaluates any DateMath expressions in its min/max values.
    /// </summary>
    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        // Evaluate min value (not an upper limit, use start of period for rounding)
        if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
        {
            if (TryEvaluateDateMath(node.Min, isUpperLimit: false, out var evaluatedMin))
            {
                node.Min = evaluatedMin;
            }
        }

        // Evaluate max value (upper limit, use end of period for rounding)
        if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
        {
            if (TryEvaluateDateMath(node.Max, isUpperLimit: true, out var evaluatedMax))
            {
                node.Max = evaluatedMax;
            }
        }

        // Handle short-form operators (>, >=, <, <=)
        // For < and <= operators, the value acts as an upper limit
        if (node.Operator.HasValue)
        {
            var value = node.Min ?? node.Max;
            if (!string.IsNullOrEmpty(value) && value != "*")
            {
                bool isUpperLimit = node.Operator.Value is RangeOperator.LessThan or RangeOperator.LessThanOrEqual;
                if (TryEvaluateDateMath(value, isUpperLimit, out var evaluatedValue))
                {
                    if (node.Min != null)
                        node.Min = evaluatedValue;
                    else
                        node.Max = evaluatedValue;
                }
            }
        }

        return Task.FromResult<QueryNode>(node);
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

        if (_timeZone != null)
        {
            success = DateMath.TryParse(expression, _timeZone, isUpperLimit, out evaluated);
        }
        else
        {
            success = DateMath.TryParse(expression, _relativeBaseTime, isUpperLimit, out evaluated);
        }

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
    public async Task<QueryNode> EvaluateAsync(QueryNode query, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        return await AcceptAsync(query, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a visitor and evaluates all DateMath expressions in the given query using UTC now as the base time.
    /// </summary>
    /// <param name="query">The query to process</param>
    /// <param name="context">The visitor context</param>
    /// <returns>The processed query with DateMath expressions evaluated</returns>
    public static Task<QueryNode> EvaluateAsync(QueryNode query, IQueryVisitorContext? context, DateTimeOffset relativeBaseTime)
    {
        var visitor = new DateMathEvaluatorVisitor(relativeBaseTime);
        return visitor.EvaluateAsync(query, context);
    }

    /// <summary>
    /// Creates a visitor and evaluates all DateMath expressions in the given query using the specified timezone.
    /// </summary>
    /// <param name="query">The query to process</param>
    /// <param name="context">The visitor context</param>
    /// <param name="timeZone">The timezone to use for 'now' calculations</param>
    /// <returns>The processed query with DateMath expressions evaluated</returns>
    public static Task<QueryNode> EvaluateAsync(QueryNode query, IQueryVisitorContext? context, TimeZoneInfo timeZone)
    {
        var visitor = new DateMathEvaluatorVisitor(timeZone);
        return visitor.EvaluateAsync(query, context);
    }

    /// <summary>
    /// Quick check if expression looks like a date followed by date math operations.
    /// Pattern: date-like string followed by +/-/digit/time-unit (e.g., 2024-01-01+1M or 2024-06-15-7d/d)
    /// </summary>
    private static bool LooksLikeDateMathWithOperations(string expression)
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
}
