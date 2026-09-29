using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Lets custom visitors supply the Elasticsearch query for a node instead of the default translation.
/// </summary>
public static class ElasticsearchNodeExtensions
{
    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding a custom query for a node.
    /// </summary>
    public const string QueryKey = "@Query";

    /// <summary>
    /// Sets the query to use for a node. The builder uses it as-is (wrapping it in a nested query when the node's
    /// field is under a nested path).
    /// </summary>
    public static void SetQuery(this QueryNode node, Query? query) => node.SetData(QueryKey, query);

    /// <summary>
    /// Gets the custom query set for a node, or null.
    /// </summary>
    public static Query? GetQuery(this QueryNode node) => node.GetData<Query>(QueryKey);
}
