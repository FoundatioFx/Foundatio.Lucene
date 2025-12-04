using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Configuration for the Elasticsearch query parser.
/// </summary>
public class ElasticsearchQueryParserConfiguration
{
    /// <summary>
    /// Whether to use scoring queries (match) vs filter queries (term).
    /// </summary>
    public bool UseScoring { get; set; }

    /// <summary>
    /// Default fields to search when no field is specified.
    /// </summary>
    public string[]? DefaultFields { get; set; }

    /// <summary>
    /// Default boolean operator for implicit combinations.
    /// </summary>
    public QueryOperator DefaultOperator { get; set; }

    /// <summary>
    /// Function to check if a field is a geo_point field.
    /// </summary>
    public Func<string, bool>? IsGeoPointField { get; set; }

    /// <summary>
    /// Function to check if a field is a date field.
    /// </summary>
    public Func<string, bool>? IsDateField { get; set; }

    /// <summary>
    /// Default timezone for date range queries.
    /// </summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary>
    /// Function to resolve location strings to coordinates.
    /// </summary>
    public Func<string, Task<string?>>? GeoLocationResolver { get; set; }

    /// <summary>
    /// Field map for resolving field aliases.
    /// </summary>
    public FieldMap? FieldMap { get; set; }

    /// <summary>
    /// Include resolver for @include syntax.
    /// </summary>
    public IncludeResolver? IncludeResolver { get; set; }

    /// <summary>
    /// Query validation options.
    /// </summary>
    public QueryValidationOptions? ValidationOptions { get; set; }

    /// <summary>
    /// Additional visitors to run before building the query.
    /// </summary>
    internal List<QueryNodeVisitor> Visitors { get; } = [];
}
