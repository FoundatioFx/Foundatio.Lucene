using System.Text;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Converts a query AST back into Lucene query text. The output parses (with the same default operator) back into
/// a semantically equivalent tree; the operators and prefixes the user wrote are preserved where they are still valid.
/// </summary>
public sealed class QueryStringBuilder
{
    private readonly StringBuilder _builder;
    private readonly BooleanOperator _defaultOperator;

    /// <summary>
    /// Creates a builder that assumes the default parser options.
    /// </summary>
    public QueryStringBuilder() : this(LuceneParserOptions.Default.DefaultOperator)
    {
    }

    /// <summary>
    /// Creates a builder for text that will be parsed with the specified default operator.
    /// </summary>
    public QueryStringBuilder(BooleanOperator defaultOperator, int capacity = 256)
    {
        _defaultOperator = defaultOperator == BooleanOperator.Implicit ? LuceneParserOptions.Default.DefaultOperator : defaultOperator;
        _builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Converts a node to query text, assuming the default parser options.
    /// </summary>
    public static string ToQueryString(QueryNode node) => new QueryStringBuilder().Build(node);

    /// <summary>
    /// Converts a node to query text that will be parsed with the specified default operator.
    /// </summary>
    public static string ToQueryString(QueryNode node, BooleanOperator defaultOperator) => new QueryStringBuilder(defaultOperator).Build(node);

    /// <summary>
    /// Converts a node to query text.
    /// </summary>
    public string Build(QueryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _builder.Clear();
        Append(node);
        return _builder.ToString();
    }

    private void Append(QueryNode? node)
    {
        switch (node)
        {
            case null:
                break;
            case QueryDocument document:
                Append(document.Query);
                break;
            case GroupNode group:
                _builder.Append('(');
                Append(group.Query);
                _builder.Append(')');
                AppendBoost(group.BoostText);
                break;
            case BooleanQueryNode boolean:
                AppendBoolean(boolean);
                break;
            case NotNode not:
                _builder.Append("NOT ");
                AppendOperand(not.Query);
                break;
            case FieldQueryNode field:
                AppendFieldName(field.Field);
                _builder.Append(':');
                if (field.Query is BooleanQueryNode or NotNode)
                {
                    _builder.Append('(');
                    Append(field.Query);
                    _builder.Append(')');
                }
                else
                {
                    Append(field.Query);
                }
                break;
            case TermNode term:
                // A lone +, -, or ! is literal text only when followed by whitespace; escape it so modifiers
                // written after it cannot turn it into an operator.
                if (term.Term is "+" or "-" or "!" or "&&" or "||" && !term.IsPrefix)
                    _builder.Append('\\');
                _builder.Append(term.Term);
                if (term.IsPrefix)
                    _builder.Append('*');
                AppendProximity(term.ProximityText);
                AppendBoost(term.BoostText);
                break;
            case PhraseNode phrase:
                _builder.Append('"').Append(QueryText.EscapePhrase(phrase.Phrase)).Append('"');
                AppendProximity(phrase.ProximityText);
                AppendBoost(phrase.BoostText);
                break;
            case RegexNode regex:
                _builder.Append('/').Append(regex.Pattern).Append('/');
                AppendBoost(regex.BoostText);
                break;
            case RangeNode range:
                AppendRange(range);
                break;
            case ExistsNode exists when exists.IsExistsSyntax:
                _builder.Append("_exists_:");
                AppendFieldName(exists.Field);
                break;
            case ExistsNode exists:
                AppendFieldName(exists.Field);
                _builder.Append(":*");
                break;
            case MissingNode missing:
                _builder.Append("_missing_:");
                AppendFieldName(missing.Field);
                break;
            case MatchAllNode:
                _builder.Append("*:*");
                break;
            default:
                throw new NotSupportedException($"Cannot convert node type '{node.GetType().Name}' to query text.");
        }
    }

    private void AppendBoolean(BooleanQueryNode node)
    {
        var clauses = node.Clauses;
        if (clauses.Count == 0)
            return;

        // A node without should clauses has AND semantics; any should clause means it is an OR level.
        bool isAndLevel = true;
        foreach (var clause in clauses)
        {
            if (clause.Occur == Occur.Should)
            {
                isAndLevel = false;
                break;
            }
        }

        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            if (i > 0)
                AppendOperator(clause, isAndLevel);

            AppendClause(clause, isAndLevel);
        }
    }

    private void AppendOperator(BooleanClause clause, bool isAndLevel)
    {
        var levelOperator = isAndLevel ? BooleanOperator.And : BooleanOperator.Or;
        if (clause.Operator == BooleanOperator.Implicit && _defaultOperator == levelOperator)
            _builder.Append(' ');
        else
            _builder.Append(isAndLevel ? " AND " : " OR ");
    }

    private void AppendClause(BooleanClause clause, bool isAndLevel)
    {
        switch (clause.Occur)
        {
            case Occur.Must when !isAndLevel || clause.Modifier == ClauseModifier.Plus:
                _builder.Append('+');
                break;
            case Occur.MustNot:
                _builder.Append(clause.Modifier switch
                {
                    ClauseModifier.Not => "NOT ",
                    ClauseModifier.Bang => "!",
                    _ => "-"
                });
                break;
        }

        // An OR level nested directly inside an AND level (or under a prefix) needs parentheses to keep its meaning.
        bool hasPrefix = clause.Occur == Occur.MustNot || clause.Occur == Occur.Must && (!isAndLevel || clause.Modifier == ClauseModifier.Plus);
        if (clause.Query is BooleanQueryNode child && (isAndLevel || hasPrefix || !IsAndLevel(child)))
        {
            _builder.Append('(');
            AppendBoolean(child);
            _builder.Append(')');
        }
        else if (clause.Query is NotNode && hasPrefix)
        {
            _builder.Append('(');
            Append(clause.Query);
            _builder.Append(')');
        }
        else
        {
            Append(clause.Query);
        }
    }

    private void AppendOperand(QueryNode? node)
    {
        if (node is BooleanQueryNode or NotNode)
        {
            _builder.Append('(');
            Append(node);
            _builder.Append(')');
        }
        else
        {
            Append(node);
        }
    }

    private static bool IsAndLevel(BooleanQueryNode node)
    {
        foreach (var clause in node.Clauses)
        {
            if (clause.Occur == Occur.Should)
                return false;
        }

        return true;
    }

    private void AppendRange(RangeNode range)
    {
        if (range.Operator is { } op && IsConsistentShortRange(range, op))
        {
            _builder.Append(op switch
            {
                RangeOperator.GreaterThan => ">",
                RangeOperator.GreaterThanOrEqual => ">=",
                RangeOperator.LessThan => "<",
                _ => "<="
            });
            AppendRangeValue(op is RangeOperator.GreaterThan or RangeOperator.GreaterThanOrEqual ? range.Min : range.Max, isShortRange: true);
        }
        else
        {
            _builder.Append(range.MinInclusive ? '[' : '{');
            AppendRangeValue(range.Min, isShortRange: false);
            _builder.Append(" TO ");
            AppendRangeValue(range.Max, isShortRange: false);
            _builder.Append(range.MaxInclusive ? ']' : '}');
        }

        AppendProximity(range.ProximityText);
        AppendBoost(range.BoostText);
    }

    private static bool IsConsistentShortRange(RangeNode range, RangeOperator op)
    {
        return op switch
        {
            RangeOperator.GreaterThan => range.Min is not null && range.Max is null && !range.MinInclusive,
            RangeOperator.GreaterThanOrEqual => range.Min is not null && range.Max is null && range.MinInclusive,
            RangeOperator.LessThan => range.Max is not null && range.Min is null && !range.MaxInclusive,
            _ => range.Max is not null && range.Min is null && range.MaxInclusive
        };
    }

    private void AppendRangeValue(string? value, bool isShortRange)
    {
        if (value is null)
        {
            _builder.Append('*');
            return;
        }

        if (NeedsQuotes(value, isShortRange))
            _builder.Append('"').Append(QueryText.EscapePhrase(value)).Append('"');
        else
            _builder.Append(value);
    }

    private static bool NeedsQuotes(string value, bool isShortRange)
    {
        if (value.Length == 0 || value == "*" || value == "TO" || value.Contains("..", StringComparison.Ordinal))
            return true;

        // A short range value starting with '=' would merge with the operator (<=, >=).
        if (isShortRange && value[0] == '=')
            return true;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '{' or '}' or '(' or ')' or '"' or '^' or '~' or '\\')
                return true;

            // Short range values end at a colon unless it separates digits (a time such as 10:30:00).
            if (isShortRange && c == ':' && (i == 0 || i == value.Length - 1 || !char.IsAsciiDigit(value[i - 1]) || !char.IsAsciiDigit(value[i + 1])))
                return true;
        }

        return false;
    }

    private void AppendFieldName(string field)
    {
        for (int i = 0; i < field.Length; i++)
        {
            char c = field[i];
            bool special = char.IsWhiteSpace(c) || c is ':' or '\\' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '^' or '~'
                || i == 0 && c is '+' or '-' or '!' or '/' or '>' or '<';
            if (special)
                _builder.Append('\\');
            _builder.Append(c);
        }
    }

    private void AppendProximity(string? proximity)
    {
        if (proximity is null)
            return;

        _builder.Append('~');
        if (proximity.Length > 0)
            AppendModifierValue(proximity);
    }

    private void AppendBoost(string? boost)
    {
        if (boost is null)
            return;

        _builder.Append('^');
        AppendModifierValue(boost);
    }

    private void AppendModifierValue(string value)
    {
        bool needsQuotes = value.Length == 0;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c is ':' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '^' or '~')
            {
                needsQuotes = true;
                break;
            }
        }

        if (needsQuotes)
            _builder.Append('"').Append(QueryText.EscapePhrase(value)).Append('"');
        else
            _builder.Append(value);
    }
}
