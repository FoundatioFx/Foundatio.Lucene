using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Simplifies a query without changing its meaning: removes empty groups and clauses, collapses nested parentheses,
/// drops parentheses around single terms, and replaces single-clause boolean levels with their clause.
/// For example <c>((value))</c> becomes <c>value</c>, <c>test:((value))</c> becomes <c>test:(value)</c>, and
/// <c>NOT (status:fixed)</c> becomes <c>NOT status:fixed</c>.
/// </summary>
public class CleanupQueryVisitor : IQueryVisitor
{
    /// <summary>
    /// A shared instance. The visitor is stateless.
    /// </summary>
    public static CleanupQueryVisitor Instance { get; } = new();

    /// <summary>
    /// Cleans up a query. The input is not modified.
    /// </summary>
    public QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is QueryDocument document)
        {
            var result = document.CloneDocument();
            result.Query = result.Query is null ? null : Clean(result.Query, parentIsField: false);
            return result;
        }

        return Clean(node.Clone(), parentIsField: false) ?? new QueryDocument();
    }

    private static QueryNode? Clean(QueryNode node, bool parentIsField)
    {
        switch (node)
        {
            case GroupNode group:
                var inner = group.Query is null ? null : Clean(group.Query, parentIsField: false);
                while (inner is GroupNode { BoostText: null } nested)
                    inner = nested.Query;

                if (inner is null)
                    return null;

                group.Query = inner;
                if (group.BoostText is null && !parentIsField && IsLeaf(inner))
                    return inner;

                return group;
            case FieldQueryNode field:
                field.Query = field.Query is null ? null : Clean(field.Query, parentIsField: true);
                return field.Query is null ? null : field;
            case NotNode not:
                not.Query = not.Query is null ? null : Clean(not.Query, parentIsField: false);
                return not.Query is null ? null : not;
            case BooleanQueryNode boolean:
                boolean.Clauses = boolean.Clauses
                    .Select(c => { c.Query = c.Query is null ? null : Clean(c.Query, parentIsField: false); return c; })
                    .Where(c => c.Query is not null)
                    .ToList();

                if (boolean.Clauses.Count == 0)
                    return null;

                if (boolean.Clauses is [{ Occur: not Occur.MustNot, Modifier: ClauseModifier.None } single])
                    return single.Query;

                return boolean;
            default:
                return node;
        }
    }

    private static bool IsLeaf(QueryNode node) => node is not (BooleanQueryNode or GroupNode or NotNode);

    /// <summary>
    /// Cleans up a query and returns the result as query text.
    /// </summary>
    public static string Run(string query, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= LuceneParserOptions.Default;
        var document = LuceneQuery.Parse(query, options).GetDocumentOrThrow();
        return QueryStringBuilder.ToQueryString(Instance.Accept(document, new QueryVisitorContext { ParserOptions = options }), options.DefaultOperator);
    }
}
