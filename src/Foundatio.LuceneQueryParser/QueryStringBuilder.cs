using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser;

/// <summary>
/// Converts a query AST back to a Lucene query string.
/// </summary>
public class QueryStringBuilder
{
    private readonly StringBuilder _builder;

    /// <summary>
    /// Creates a new QueryStringBuilder instance.
    /// </summary>
    public QueryStringBuilder()
    {
        _builder = new StringBuilder();
    }

    /// <summary>
    /// Creates a new QueryStringBuilder with pre-allocated capacity.
    /// </summary>
    /// <param name="capacity">Initial capacity for the string builder.</param>
    public QueryStringBuilder(int capacity)
    {
        _builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Converts a query node to its string representation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToQueryString(QueryNode node)
    {
        return new QueryStringBuilder().Visit(node);
    }

    /// <summary>
    /// Visits a node and returns its string representation.
    /// </summary>
    public string Visit(QueryNode node)
    {
        _builder.Clear();
        AppendNode(node);
        return _builder.ToString();
    }

    /// <summary>
    /// Appends a node to the internal buffer. Call ToString() or Visit() to get the result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendNode(QueryNode? node)
    {
        if (node is null)
            return;

        switch (node)
        {
            case QueryDocument doc:
                AppendNode(doc.Query);
                break;
            case GroupNode group:
                AppendGroup(group);
                break;
            case BooleanQueryNode boolQuery:
                AppendBooleanQuery(boolQuery);
                break;
            case FieldQueryNode fieldQuery:
                AppendFieldQuery(fieldQuery);
                break;
            case TermNode term:
                AppendTerm(term);
                break;
            case PhraseNode phrase:
                AppendPhrase(phrase);
                break;
            case RegexNode regex:
                AppendRegex(regex);
                break;
            case RangeNode range:
                AppendRange(range);
                break;
            case NotNode not:
                AppendNot(not);
                break;
            case ExistsNode exists:
                AppendExists(exists);
                break;
            case MissingNode missing:
                AppendMissing(missing);
                break;
            case MatchAllNode:
                _builder.Append("*:*");
                break;
            case MultiTermNode multiTerm:
                AppendMultiTerm(multiTerm);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendGroup(GroupNode node)
    {
        _builder.Append('(');
        AppendNode(node.Query);
        _builder.Append(')');
        AppendBoost(node.Boost);
    }

    private void AppendBooleanQuery(BooleanQueryNode node)
    {
        if (node.Clauses.Count == 0)
            return;

        bool isFirst = true;

        for (int i = 0; i < node.Clauses.Count; i++)
        {
            var clause = node.Clauses[i];
            if (clause.Query is null)
                continue;

            // Track position before appending to check if anything was written
            int positionBefore = _builder.Length;

            // Add operator before clause (except for first written clause)
            if (!isFirst)
            {
                switch (clause.Operator)
                {
                    case BooleanOperator.And:
                        _builder.Append(" AND ");
                        break;
                    case BooleanOperator.Or:
                        _builder.Append(" OR ");
                        break;
                    default:
                        _builder.Append(' ');
                        break;
                }
            }

            // Determine if we need a +/- prefix
            if (clause.Occur == Occur.MustNot)
            {
                _builder.Append('-');
            }
            else if (clause.Occur == Occur.Must && clause.Operator != BooleanOperator.And)
            {
                bool isPartOfAndChain = i + 1 < node.Clauses.Count &&
                                        node.Clauses[i + 1].Operator == BooleanOperator.And;
                if (!isPartOfAndChain)
                {
                    _builder.Append('+');
                }
            }

            int positionBeforeQuery = _builder.Length;
            AppendNode(clause.Query);

            // Check if query actually produced output
            if (_builder.Length > positionBeforeQuery)
            {
                isFirst = false;
            }
            else
            {
                // Nothing was written, revert any operator/prefix we added
                _builder.Length = positionBefore;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendFieldQuery(FieldQueryNode node)
    {
        _builder.Append(node.Field);
        _builder.Append(':');

        if (node.IsExists)
        {
            _builder.Append('*');
        }
        else
        {
            AppendNode(node.Query);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendTerm(TermNode node)
    {
        _builder.Append(node.Term);

        if (node.IsPrefix)
        {
            _builder.Append('*');
        }

        if (node.FuzzyDistance.HasValue)
        {
            _builder.Append('~');
            if (node.FuzzyDistance.Value != 2)
            {
                _builder.Append(node.FuzzyDistance.Value);
            }
        }

        AppendBoost(node.Boost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendPhrase(PhraseNode node)
    {
        _builder.Append('"');
        _builder.Append(node.Phrase);
        _builder.Append('"');

        if (node.Slop.HasValue)
        {
            _builder.Append('~');
            _builder.Append(node.Slop.Value);
        }

        AppendBoost(node.Boost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRegex(RegexNode node)
    {
        _builder.Append('/');
        _builder.Append(node.Pattern);
        _builder.Append('/');
        AppendBoost(node.Boost);
    }

    private void AppendRange(RangeNode node)
    {
        // Handle short-form ranges
        if (node.Operator.HasValue)
        {
            switch (node.Operator.Value)
            {
                case RangeOperator.GreaterThan:
                    _builder.Append('>');
                    _builder.Append(node.Min ?? "*");
                    break;
                case RangeOperator.GreaterThanOrEqual:
                    _builder.Append(">=");
                    _builder.Append(node.Min ?? "*");
                    break;
                case RangeOperator.LessThan:
                    _builder.Append('<');
                    _builder.Append(node.Max ?? "*");
                    break;
                case RangeOperator.LessThanOrEqual:
                    _builder.Append("<=");
                    _builder.Append(node.Max ?? "*");
                    break;
            }
        }
        else
        {
            // Standard range syntax
            _builder.Append(node.MinInclusive ? '[' : '{');
            _builder.Append(node.Min ?? "*");
            _builder.Append(" TO ");
            _builder.Append(node.Max ?? "*");
            _builder.Append(node.MaxInclusive ? ']' : '}');
        }

        AppendBoost(node.Boost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendNot(NotNode node)
    {
        _builder.Append("NOT ");
        AppendNode(node.Query);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendExists(ExistsNode node)
    {
        if (node.IsExistsSyntax)
        {
            _builder.Append("_exists_:");
            _builder.Append(node.Field);
        }
        else
        {
            _builder.Append(node.Field);
            _builder.Append(":*");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendMissing(MissingNode node)
    {
        _builder.Append("_missing_:");
        _builder.Append(node.Field);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendMultiTerm(MultiTermNode node)
    {
        _builder.Append(node.CombinedText);

        if (node.FuzzyDistance.HasValue)
        {
            _builder.Append('~');
            if (node.FuzzyDistance.Value != 2)
            {
                _builder.Append(node.FuzzyDistance.Value);
            }
        }

        AppendBoost(node.Boost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendBoost(float? boost)
    {
        if (boost.HasValue)
        {
            _builder.Append('^');
            AppendFloat(boost.Value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendFloat(float value)
    {
        // Fast path for common integer values
        if (value == (int)value && value >= 0 && value <= 99)
        {
            _builder.Append((int)value);
        }
        else
        {
            _builder.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }
}
