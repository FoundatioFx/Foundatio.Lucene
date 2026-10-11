using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Extensions;

/// <summary>
/// Extension methods for query nodes.
/// </summary>
public static class QueryNodeExtensions
{
    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding the field name as written before alias resolution.
    /// </summary>
    public const string OriginalFieldKey = "@OriginalField";

    /// <summary>
    /// Gets the field name as written in the query, before alias resolution. Falls back to the current field.
    /// </summary>
    public static string GetOriginalField<T>(this T node) where T : QueryNode, IFieldNode
    {
        return node.GetData<string>(OriginalFieldKey) ?? node.Field;
    }

    /// <summary>
    /// Records the field name as written in the query, before alias resolution.
    /// </summary>
    public static void SetOriginalField<T>(this T node, string originalField) where T : QueryNode, IFieldNode
    {
        node.SetData(OriginalFieldKey, originalField);
    }

    /// <summary>
    /// Gets the names of all fields referenced by the node and its descendants.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <param name="includeSpecialFields">Whether to include fields that start with <c>@</c> (such as <c>@include</c>).</param>
    public static ISet<string> GetReferencedFields(this QueryNode node, bool includeSpecialFields = false)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GatherReferencedFields(node, fields, includeSpecialFields);
        return fields;
    }

    private static void GatherReferencedFields(QueryNode? node, HashSet<string> fields, bool includeSpecialFields)
    {
        switch (node)
        {
            case QueryDocument document:
                GatherReferencedFields(document.Query, fields, includeSpecialFields);
                break;
            case GroupNode group:
                GatherReferencedFields(group.Query, fields, includeSpecialFields);
                break;
            case NotNode not:
                GatherReferencedFields(not.Query, fields, includeSpecialFields);
                break;
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                    GatherReferencedFields(clause.Query, fields, includeSpecialFields);
                break;
            case FieldQueryNode field:
                AddField(fields, field.Field, includeSpecialFields);
                GatherReferencedFields(field.Query, fields, includeSpecialFields);
                break;
            case IFieldNode fieldNode:
                AddField(fields, fieldNode.Field, includeSpecialFields);
                break;
        }
    }

    private static void AddField(HashSet<string> fields, string field, bool includeSpecialFields)
    {
        if (field.Length > 0 && (includeSpecialFields || field[0] != '@'))
            fields.Add(field);
    }

    /// <summary>
    /// Visits the node and all of its descendants, depth first, calling <paramref name="action"/> for each.
    /// Iterative, so it is safe on arbitrarily deep trees.
    /// </summary>
    public static void Walk(this QueryNode node, Action<QueryNode> action)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(action);

        var stack = new Stack<QueryNode>();
        stack.Push(node);
        while (stack.TryPop(out var current))
        {
            action(current);
            switch (current)
            {
                case QueryDocument { Query: { } query }:
                    stack.Push(query);
                    break;
                case GroupNode { Query: { } query }:
                    stack.Push(query);
                    break;
                case NotNode { Query: { } query }:
                    stack.Push(query);
                    break;
                case FieldQueryNode { Query: { } query }:
                    stack.Push(query);
                    break;
                case BooleanQueryNode boolean:
                    for (int i = boolean.Clauses.Count - 1; i >= 0; i--)
                    {
                        if (boolean.Clauses[i].Query is { } clauseQuery)
                            stack.Push(clauseQuery);
                    }
                    break;
            }
        }
    }
}
