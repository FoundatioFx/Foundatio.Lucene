using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// The direction of a sort.
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending order.</summary>
    Ascending,
    /// <summary>Descending order.</summary>
    Descending
}

/// <summary>
/// A field in a sort expression such as <c>-created +name</c>.
/// </summary>
public sealed class SortField
{
    private Dictionary<string, object?>? _data;

    /// <summary>
    /// Creates a sort field.
    /// </summary>
    public SortField(string field, SortDirection direction = SortDirection.Ascending)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        Field = field;
        OriginalField = field;
        Direction = direction;
    }

    /// <summary>
    /// The field to sort on, after alias resolution.
    /// </summary>
    public string Field { get; set; }

    /// <summary>
    /// The field as written in the sort expression.
    /// </summary>
    public string OriginalField { get; set; }

    /// <summary>
    /// The sort direction.
    /// </summary>
    public SortDirection Direction { get; set; }

    /// <summary>
    /// The 0-based position of the field in the sort expression.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Arbitrary metadata for providers.
    /// </summary>
    public IDictionary<string, object?> Data => _data ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => (Direction == SortDirection.Descending ? "-" : "") + Field;
}

/// <summary>
/// Converts sort expressions to <see cref="SortField"/> lists. Fields are separated by whitespace (or AND/OR);
/// <c>-field</c> sorts descending, <c>field</c> and <c>+field</c> ascending, <c>field:desc</c> and
/// <c>field:asc</c> are explicit, and a prefix on a group applies to the fields inside it
/// (<c>-(a b +c)</c> sorts <c>a</c> and <c>b</c> descending and <c>c</c> ascending).
/// </summary>
public static class SortExpression
{
    /// <summary>
    /// Parses a sort expression. Errors are added to <paramref name="result"/>.
    /// </summary>
    public static List<SortField> Parse(string sort, QueryValidationResult result, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(result);

        var parsed = LuceneQuery.Parse(sort, options);
        foreach (var error in parsed.Errors)
            result.AddError(error.Message, error.Position, error.Code);

        return FromDocument(parsed.Document, result);
    }

    /// <summary>
    /// Converts a parsed sort expression. Errors are added to <paramref name="result"/>.
    /// </summary>
    public static List<SortField> FromDocument(QueryDocument document, QueryValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);

        var fields = new List<SortField>();
        Collect(document.Query, SortDirection.Ascending, fields, result);
        return fields;
    }

    private static void Collect(QueryNode? node, SortDirection direction, List<SortField> fields, QueryValidationResult result)
    {
        switch (node)
        {
            case null:
                break;
            case GroupNode group:
                Collect(group.Query, direction, fields, result);
                break;
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                {
                    switch (clause.Modifier)
                    {
                        case ClauseModifier.Not or ClauseModifier.Bang:
                            AddOrderingError(clause.Query, "sort", result);
                            break;
                        case ClauseModifier.Minus:
                            Collect(clause.Query, SortDirection.Descending, fields, result);
                            break;
                        case ClauseModifier.Plus:
                            Collect(clause.Query, SortDirection.Ascending, fields, result);
                            break;
                        default:
                            Collect(clause.Query, direction, fields, result);
                            break;
                    }
                }
                break;
            case NotNode not:
                AddOrderingError(not.Query, "sort", result);
                break;
            case TermNode { IsPrefix: false, IsWildcard: false, IsFuzzy: false, BoostText: null } term:
                Add(term.UnescapedTerm, direction, term, fields);
                break;
            case PhraseNode { ProximityText: null, BoostText: null } phrase:
                Add(phrase.Phrase, direction, phrase, fields);
                break;
            case FieldQueryNode { Query: TermNode { IsPrefix: false, IsWildcard: false } value } field
                when value.UnescapedTerm.Equals("asc", StringComparison.OrdinalIgnoreCase) || value.UnescapedTerm.Equals("desc", StringComparison.OrdinalIgnoreCase):
                Add(field.Field, value.UnescapedTerm.Equals("desc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Descending : SortDirection.Ascending, field, fields);
                break;
            default:
                result.AddError($"Sort expressions only support field names, optionally prefixed with + or - or suffixed with :asc or :desc ({QueryStringBuilder.ToQueryString(node)}).", node.StartPosition);
                break;
        }
    }

    private static void Add(string field, SortDirection direction, QueryNode node, List<SortField> fields)
    {
        fields.Add(new SortField(field, direction) { Position = node.StartPosition });
    }

    internal static void AddOrderingError(QueryNode? node, string kind, QueryValidationResult result)
    {
        string suffix = node is null ? "" : $" for ({QueryStringBuilder.ToQueryString(node)})";
        result.AddError($"Boolean operator (NOT|!) is not supported in {kind} expressions{suffix}: use + for ascending or - for descending order.", node?.StartPosition ?? -1);
    }
}
