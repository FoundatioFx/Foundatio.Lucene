using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Removes the clauses that use the specified fields, for example to strip a scope filter before showing a query to
/// a user. Groups and boolean levels left empty are removed; a level left with a single plain clause is replaced by it.
/// </summary>
public class RemoveFieldsQueryVisitor : IQueryVisitor
{
    /// <summary>
    /// Creates a visitor that removes the specified fields (case-insensitive).
    /// </summary>
    public RemoveFieldsQueryVisitor(IEnumerable<string> fieldsToRemove)
    {
        ArgumentNullException.ThrowIfNull(fieldsToRemove);
        var fields = new HashSet<string>(fieldsToRemove, StringComparer.OrdinalIgnoreCase);
        ShouldRemoveField = fields.Contains;
    }

    /// <summary>
    /// Creates a visitor that removes fields for which <paramref name="shouldRemoveField"/> returns true.
    /// </summary>
    public RemoveFieldsQueryVisitor(Func<string, bool> shouldRemoveField)
    {
        ShouldRemoveField = shouldRemoveField ?? throw new ArgumentNullException(nameof(shouldRemoveField));
    }

    /// <summary>
    /// Decides whether a field's clauses are removed.
    /// </summary>
    public Func<string, bool> ShouldRemoveField { get; }

    /// <summary>
    /// Removes the fields. The input is not modified.
    /// </summary>
    public QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is QueryDocument document)
        {
            var result = document.CloneDocument();
            result.Query = result.Query is null ? null : Remove(result.Query);
            return result;
        }

        return Remove(node.Clone()) ?? new QueryDocument();
    }

    private QueryNode? Remove(QueryNode node)
    {
        switch (node)
        {
            case IFieldNode field when ShouldRemoveField(field.Field):
                return null;
            case FieldQueryNode field:
                field.Query = field.Query is null ? null : Remove(field.Query);
                return field.Query is null ? null : field;
            case GroupNode group:
                group.Query = group.Query is null ? null : Remove(group.Query);
                return group.Query is null ? null : group;
            case NotNode not:
                not.Query = not.Query is null ? null : Remove(not.Query);
                return not.Query is null ? null : not;
            case BooleanQueryNode boolean:
                int count = boolean.Clauses.Count;
                boolean.Clauses = boolean.Clauses
                    .Select(c => { c.Query = c.Query is null ? null : Remove(c.Query); return c; })
                    .Where(c => c.Query is not null)
                    .ToList();

                if (boolean.Clauses.Count == 0)
                    return null;

                if (boolean.Clauses.Count < count && boolean.Clauses is [{ Occur: not Occur.MustNot, Modifier: ClauseModifier.None } single])
                    return single.Query;

                return boolean;
            default:
                return node;
        }
    }

    /// <summary>
    /// Removes fields from a query and returns the result as query text.
    /// </summary>
    public static string Run(string query, IEnumerable<string> fieldsToRemove, LuceneParserOptions? options = null)
    {
        return Run(query, new RemoveFieldsQueryVisitor(fieldsToRemove), options);
    }

    /// <summary>
    /// Removes fields matching a predicate from a query and returns the result as query text.
    /// </summary>
    public static string Run(string query, Func<string, bool> shouldRemoveField, LuceneParserOptions? options = null)
    {
        return Run(query, new RemoveFieldsQueryVisitor(shouldRemoveField), options);
    }

    private static string Run(string query, RemoveFieldsQueryVisitor visitor, LuceneParserOptions? options)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= LuceneParserOptions.Default;
        var document = LuceneQuery.Parse(query, options).GetDocumentOrThrow();
        return QueryStringBuilder.ToQueryString(visitor.Accept(document, new QueryVisitorContext { ParserOptions = options }), options.DefaultOperator);
    }
}
