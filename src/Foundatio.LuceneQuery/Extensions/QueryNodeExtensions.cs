using Foundatio.LuceneQuery.Ast;

namespace Foundatio.LuceneQuery.Extensions;

/// <summary>
/// Extension methods for getting referenced fields from query nodes.
/// </summary>
public static class QueryNodeExtensions
{
    /// <summary>
    /// Gets all field names referenced in the query node and its descendants.
    /// </summary>
    /// <typeparam name="T">The type of query node.</typeparam>
    /// <param name="node">The query node to analyze.</param>
    /// <returns>A set of all field names referenced in the query.</returns>
    public static ISet<string> GetReferencedFields<T>(this T node) where T : QueryNode
    {
        // For QueryDocument, delegate to the inner Query
        if (node is QueryDocument doc && doc.Query != null)
        {
            return doc.Query.GetReferencedFields();
        }

        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GatherReferencedFields(node, fields);
        return fields;
    }

    private static void GatherReferencedFields(QueryNode? node, HashSet<string> fields)
    {
        if (node is null)
            return;

        switch (node)
        {
            case QueryDocument doc:
                GatherReferencedFields(doc.Query, fields);
                break;

            case FieldQueryNode fieldNode:
                if (!string.IsNullOrEmpty(fieldNode.Field))
                {
                    fields.Add(fieldNode.Field);
                }
                GatherReferencedFields(fieldNode.Query, fields);
                break;

            case GroupNode group:
                GatherReferencedFields(group.Query, fields);
                break;

            case BooleanQueryNode boolQuery:
                foreach (var clause in boolQuery.Clauses)
                {
                    GatherReferencedFields(clause.Query, fields);
                }
                break;

            case NotNode notNode:
                GatherReferencedFields(notNode.Query, fields);
                break;

            case ExistsNode existsNode:
                if (!string.IsNullOrEmpty(existsNode.Field))
                {
                    fields.Add(existsNode.Field);
                }
                break;

            case MissingNode missingNode:
                if (!string.IsNullOrEmpty(missingNode.Field))
                {
                    fields.Add(missingNode.Field);
                }
                break;

            // Terminal nodes that don't have fields or children:
            // TermNode, PhraseNode, RegexNode, RangeNode, MatchAllNode, MultiTermNode
            // These are handled by their parent FieldQueryNode
        }
    }
}

