namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Configuration for the Elasticsearch query parser.
/// </summary>
public class ElasticsearchQueryParserConfiguration : QueryParserConfigurationBase
{
    /// <summary>
    /// Whether to use scoring queries (match) vs filter queries (term).
    /// </summary>
    public bool UseScoring { get; set; }

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
}
