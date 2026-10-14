using System.Text;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Extensions;

/// <summary>
/// Produces a compact, unambiguous description of a query tree for debugging and tests.
/// </summary>
public static class QueryDebugExtensions
{
    /// <summary>
    /// Describes the tree as nested S-expressions, for example <c>(and a (not b))</c>.
    /// </summary>
    public static string ToDebugString(this QueryNode? node)
    {
        var builder = new StringBuilder();
        Append(builder, node);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, QueryNode? node)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case QueryDocument document:
                Append(builder, document.Query);
                break;
            case GroupNode group:
                builder.Append("(group ");
                Append(builder, group.Query);
                AppendModifier(builder, '^', group.BoostText);
                builder.Append(')');
                break;
            case BooleanQueryNode boolean:
                builder.Append("(bool");
                foreach (var clause in boolean.Clauses)
                {
                    builder.Append(clause.Occur switch
                    {
                        Occur.Must => " +",
                        Occur.MustNot => " -",
                        _ => " ?"
                    });
                    Append(builder, clause.Query);
                }
                builder.Append(')');
                break;
            case NotNode not:
                builder.Append("(not ");
                Append(builder, not.Query);
                builder.Append(')');
                break;
            case FieldQueryNode field:
                builder.Append(field.Field).Append(':');
                Append(builder, field.Query);
                break;
            case TermNode term:
                builder.Append(term.UnescapedTerm);
                if (term.IsPrefix)
                    builder.Append("*(prefix)");
                if (term.IsWildcard)
                    builder.Append("(wildcard)");
                AppendModifier(builder, '~', term.ProximityText);
                AppendModifier(builder, '^', term.BoostText);
                break;
            case PhraseNode phrase:
                builder.Append('"').Append(phrase.Phrase).Append('"');
                AppendModifier(builder, '~', phrase.ProximityText);
                AppendModifier(builder, '^', phrase.BoostText);
                break;
            case RegexNode regex:
                builder.Append('/').Append(regex.Pattern).Append('/');
                AppendModifier(builder, '^', regex.BoostText);
                break;
            case RangeNode range:
                builder.Append(range.MinInclusive ? '[' : '{')
                    .Append(range.Min ?? "*")
                    .Append(" TO ")
                    .Append(range.Max ?? "*")
                    .Append(range.MaxInclusive ? ']' : '}');
                AppendModifier(builder, '~', range.ProximityText);
                AppendModifier(builder, '^', range.BoostText);
                break;
            case ExistsNode exists:
                builder.Append("(exists ").Append(exists.Field).Append(')');
                break;
            case MissingNode missing:
                builder.Append("(missing ").Append(missing.Field).Append(')');
                break;
            case MatchAllNode:
                builder.Append("(all)");
                break;
            default:
                builder.Append('(').Append(node.GetType().Name).Append(')');
                break;
        }
    }

    private static void AppendModifier(StringBuilder builder, char prefix, string? value)
    {
        if (value is not null)
            builder.Append(prefix).Append(value);
    }
}
