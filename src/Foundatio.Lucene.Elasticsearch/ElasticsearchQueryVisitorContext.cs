using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Boolean operator used when combining queries with implicit AND/OR.
/// </summary>
public enum QueryOperator
{
    Or,
    And
}

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
    QueryOperator DefaultOperator { get; set; }

    /// <summary>
    /// Function to check if a field is a geo_point field.
    /// </summary>
    Func<string, bool>? IsGeoPointField { get; set; }

    /// <summary>
    /// Function to check if a field is a date field.
    /// </summary>
    Func<string, bool>? IsDateField { get; set; }

    /// <summary>
    /// Default timezone for date range queries.
    /// </summary>
    string? DefaultTimeZone { get; set; }

    /// <summary>
    /// Function to resolve location strings to coordinates.
    /// </summary>
    Func<string, Task<string?>>? GeoLocationResolver { get; set; }
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
    public QueryOperator DefaultOperator { get; set; }

    /// <inheritdoc />
    public Func<string, bool>? IsGeoPointField { get; set; }

    /// <inheritdoc />
    public Func<string, bool>? IsDateField { get; set; }

    /// <inheritdoc />
    public string? DefaultTimeZone { get; set; }

    /// <inheritdoc />
    public Func<string, Task<string?>>? GeoLocationResolver { get; set; }
}
