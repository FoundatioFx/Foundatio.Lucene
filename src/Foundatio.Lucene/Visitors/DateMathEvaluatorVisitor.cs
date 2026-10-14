using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Replaces date math expressions (for example <c>now-1d/d</c> or <c>2024-01-01||+1M</c>) in terms and range bounds
/// with the absolute dates they evaluate to. Useful for data stores that do not evaluate date math themselves.
/// </summary>
/// <remarks>
/// <c>now</c> is read once per document so every expression in a query uses the same instant. Range bounds are
/// rounded the way Elasticsearch rounds them: inclusive upper and exclusive lower bounds round up to the end of the
/// period, the others round down.
/// </remarks>
public class DateMathEvaluatorVisitor : QueryVisitor
{
    /// <summary>
    /// The default output format: ISO 8601 with milliseconds and offset.
    /// </summary>
    public const string DefaultDateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private const string StateKey = "@DateMathState";

    private readonly TimeProvider? _timeProvider;
    private readonly TimeZoneInfo? _timeZone;
    private readonly Func<string, bool>? _isDateField;
    private readonly string _dateFormat;

    /// <summary>
    /// Creates the visitor.
    /// </summary>
    /// <param name="timeProvider">The clock for <c>now</c>; defaults to the context's <see cref="IQueryVisitorContext.TimeProvider"/>.</param>
    /// <param name="timeZone">The time zone for <c>now</c> and dates without an offset; defaults to UTC.</param>
    /// <param name="isDateField">Limits evaluation to fields for which this returns true. When null, every field is
    /// evaluated, as are terms without a field.</param>
    /// <param name="dateFormat">The format of the evaluated dates.</param>
    public DateMathEvaluatorVisitor(TimeProvider? timeProvider = null, TimeZoneInfo? timeZone = null, Func<string, bool>? isDateField = null, string dateFormat = DefaultDateFormat)
    {
        ArgumentException.ThrowIfNullOrEmpty(dateFormat);
        _timeProvider = timeProvider;
        _timeZone = timeZone;
        _isDateField = isDateField;
        _dateFormat = dateFormat;
    }

    /// <summary>
    /// Creates a visitor that uses a fixed instant for <c>now</c>.
    /// </summary>
    public DateMathEvaluatorVisitor(DateTimeOffset now, TimeZoneInfo? timeZone = null, string dateFormat = DefaultDateFormat)
        : this(new FixedTimeProvider(now), timeZone, isDateField: null, dateFormat)
    {
    }

    /// <inheritdoc/>
    public override QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        if (node is not QueryDocument)
            return base.Accept(node, context);

        var previous = context.GetValue<State>(StateKey);
        context.SetValue(StateKey, new State((_timeProvider ?? context.TimeProvider).GetUtcNow()));
        try
        {
            return base.Accept(node, context);
        }
        finally
        {
            context.SetValue(StateKey, previous);
        }
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        var state = GetState(context);
        state.Fields.Push(node.Field);
        try
        {
            return base.Visit(node, context);
        }
        finally
        {
            state.Fields.Pop();
        }
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        if (node.IsWildcard || node.IsPrefix || !ShouldEvaluate(context))
            return node;

        if (TryEvaluate(node.UnescapedTerm, isUpperLimit: false, context, out string? evaluated))
            node.Term = QueryText.Escape(evaluated);

        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        if (!ShouldEvaluate(context))
            return node;

        if (node.Min is { } min && TryEvaluate(min, isUpperLimit: !node.MinInclusive, context, out string? evaluatedMin))
            node.Min = evaluatedMin;

        if (node.Max is { } max && TryEvaluate(max, isUpperLimit: node.MaxInclusive, context, out string? evaluatedMax))
            node.Max = evaluatedMax;

        return node;
    }

    private bool ShouldEvaluate(IQueryVisitorContext context)
    {
        if (_isDateField is null)
            return true;

        var fields = GetState(context).Fields;
        return fields.Count > 0 && _isDateField(fields.Peek());
    }

    private bool TryEvaluate(string expression, bool isUpperLimit, IQueryVisitorContext context, [NotNullWhen(true)] out string? result)
    {
        result = null;
        if (!DateMath.IsDateMath(expression))
            return false;

        var now = GetState(context).Now;
        bool success = _timeZone is null
            ? DateMath.TryParse(expression, now, isUpperLimit, out var evaluated)
            : DateMath.TryParse(expression, now, _timeZone, isUpperLimit, out evaluated);

        if (!success)
            return false;

        result = evaluated.ToString(_dateFormat, CultureInfo.InvariantCulture);
        return true;
    }

    private State GetState(IQueryVisitorContext context)
    {
        var state = context.GetValue<State>(StateKey);
        if (state is null)
        {
            state = new State((_timeProvider ?? context.TimeProvider).GetUtcNow());
            context.SetValue(StateKey, state);
        }

        return state;
    }

    /// <summary>
    /// Evaluates date math in a query using a fixed instant for <c>now</c>.
    /// </summary>
    public static QueryNode Evaluate(QueryNode query, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new DateMathEvaluatorVisitor(now, timeZone).Accept(query, new QueryVisitorContext());
    }

    private sealed class State(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; } = now;

        public Stack<string> Fields { get; } = new();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
