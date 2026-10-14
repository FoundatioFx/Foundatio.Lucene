using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Lets custom visitors supply the Elasticsearch query, sort, or aggregation for a node instead of the default
/// translation.
/// </summary>
public static class ElasticsearchNodeExtensions
{
    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding a custom query for a node.
    /// </summary>
    public const string QueryKey = "@Query";

    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding a custom sort for a node in a sort expression.
    /// </summary>
    public const string SortKey = "@Sort";

    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding a custom aggregation for a node in an aggregation expression.
    /// </summary>
    public const string AggregationKey = "@Aggregation";

    /// <summary>
    /// Sets the query to use for a node. The builder uses it as-is (wrapping it in a nested query when the node's
    /// field is under a nested path).
    /// </summary>
    public static void SetQuery(this QueryNode node, Query? query) => node.SetData(QueryKey, query);

    /// <summary>
    /// Gets the custom query set for a node, or null.
    /// </summary>
    public static Query? GetQuery(this QueryNode node) => node.GetData<Query>(QueryKey);

    /// <summary>
    /// Sets the sort to use for a field in a sort expression (for example a geo distance or script sort). Any
    /// <c>field:value</c> node can carry one; the value is left to the visitor.
    /// </summary>
    public static void SetSort(this QueryNode node, SortOptions? sort) => node.SetData(SortKey, sort);

    /// <summary>
    /// Gets the custom sort set for a node, or null.
    /// </summary>
    public static SortOptions? GetSort(this QueryNode node) => node.GetData<SortOptions>(SortKey);

    /// <summary>
    /// Sets the aggregation to use for a <c>type:field</c> node in an aggregation expression. The type may be one the
    /// provider doesn't know. The aggregation is named <c>{type}_{field}</c>, wrapped in nested aggregations when the
    /// field is under a nested path, and receives the node's sub-aggregations.
    /// </summary>
    public static void SetAggregation(this QueryNode node, Aggregation? aggregation) => node.SetData(AggregationKey, aggregation);

    /// <summary>
    /// Gets the custom aggregation set for a node, or null.
    /// </summary>
    public static Aggregation? GetAggregation(this QueryNode node) => node.GetData<Aggregation>(AggregationKey);
}
