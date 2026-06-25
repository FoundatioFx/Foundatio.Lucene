using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Context interface for Elasticsearch query building.
/// </summary>
public interface IElasticsearchQueryVisitorContext : IQueryVisitorContext
{
    /// <summary>
    /// Whether to use scoring queries (match) vs filter queries (term).
    /// </summary>
    bool UseScoring { get; set; }

    /// <summary>
    /// Default fields to search when no field is specified.
    /// </summary>
    string[]? DefaultFields { get; set; }

    /// <summary>
    /// Default boolean operator for implicit combinations.
    /// </summary>
    BooleanOperator DefaultOperator { get; set; }

    /// <summary>
    /// Function to check if a field is a date field.
    /// </summary>
    Func<string, bool>? IsDateField { get; set; }

    /// <summary>
    /// Default timezone for date range queries.
    /// </summary>
    string? DefaultTimeZone { get; set; }

    /// <summary>
    /// Stack used during query building to accumulate Query objects.
    /// This is internal state for the stateless visitor pattern.
    /// </summary>
    Stack<Query> QueryStack { get; }

    /// <summary>
    /// Current field being processed during query building.
    /// This is internal state for the stateless visitor pattern.
    /// </summary>
    string? CurrentField { get; set; }
}

/// <summary>
/// Default implementation of the Elasticsearch query visitor context.
/// </summary>
public class ElasticsearchQueryVisitorContext : QueryVisitorContext, IElasticsearchQueryVisitorContext
{
    /// <inheritdoc />
    public bool UseScoring { get; set; }

    /// <inheritdoc />
    public string[]? DefaultFields { get; set; }

    /// <inheritdoc />
    public BooleanOperator DefaultOperator { get; set; } = BooleanOperator.Or;

    /// <inheritdoc />
    public Func<string, bool>? IsDateField { get; set; }

    /// <inheritdoc />
    public string? DefaultTimeZone { get; set; }

    /// <inheritdoc />
    public Stack<Query> QueryStack { get; } = new();

    /// <inheritdoc />
    public string? CurrentField { get; set; }
}
