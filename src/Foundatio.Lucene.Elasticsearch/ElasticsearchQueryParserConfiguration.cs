using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Resolves a place name or other location text (for example <c>"London"</c> or a postal code) to a location
/// Elasticsearch understands (<c>"lat,lon"</c> or a geohash). Returns null to use the text as written.
/// </summary>
public delegate ValueTask<string?> GeoLocationResolver(string location, IQueryVisitorContext context, CancellationToken cancellationToken);

/// <summary>
/// Provides a runtime field definition for a field that is not in the index mapping, or null when there is none.
/// </summary>
public delegate ValueTask<ElasticRuntimeField?> RuntimeFieldResolver(string field, IQueryVisitorContext context, CancellationToken cancellationToken);

/// <summary>
/// Provides an extra filter applied inside the nested query, sort, or aggregation for a field under a nested path
/// (for example to restrict nested documents to the current tenant), or null for none.
/// </summary>
public delegate ValueTask<Query?> NestedFilterResolver(NestedFilterContext filterContext, IQueryVisitorContext context, CancellationToken cancellationToken);

/// <summary>
/// Describes the nested field a <see cref="NestedFilterResolver"/> is asked about.
/// </summary>
/// <param name="NestedPath">The deepest nested path containing the field.</param>
/// <param name="OriginalField">The field as written in the expression.</param>
/// <param name="ResolvedField">The resolved field name.</param>
public readonly record struct NestedFilterContext(string NestedPath, string OriginalField, string ResolvedField);

/// <summary>
/// Configuration for <see cref="ElasticsearchQueryParser"/>.
/// </summary>
public class ElasticsearchQueryParserConfiguration : QueryParserConfiguration
{
    /// <summary>
    /// Whether queries run in scoring (query) context. When false (the default) the query is wrapped in a
    /// <c>bool.filter</c>, which is cacheable and does not compute scores.
    /// </summary>
    public bool UseScoring { get; set; }

    /// <summary>
    /// The index mapping used to choose query types (for example <c>match</c> for text fields and <c>term</c> for
    /// keyword fields), keyword and sort sub-fields, nested paths, date and geo fields, and field name casing.
    /// Without a mapping every field is treated as an unmapped keyword field.
    /// </summary>
    public ElasticMappingResolver? MappingResolver { get; set; }

    /// <summary>
    /// The time zone applied to date ranges and date histograms that do not specify one (for example
    /// <c>America/Chicago</c> or <c>-05:00</c>).
    /// </summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary>
    /// Whether fields under nested mappings are queried with <c>nested</c> queries, sorts, and aggregations.
    /// Defaults to true; querying nested fields without nested queries matches incorrectly.
    /// </summary>
    public bool UseNested { get; set; } = true;

    /// <summary>
    /// Resolves location text in geo distance queries (<c>location:"London"~5km</c>). Runs in the resolution phase.
    /// </summary>
    public GeoLocationResolver? GeoLocationResolver { get; set; }

    /// <summary>
    /// Provides runtime field definitions for fields missing from the mapping. Runs in the resolution phase; the
    /// definitions are collected in <see cref="ElasticsearchQueryVisitorContext.RuntimeFields"/> for the caller to
    /// add to the search request's <c>runtime_mappings</c>.
    /// </summary>
    public RuntimeFieldResolver? RuntimeFieldResolver { get; set; }

    /// <summary>
    /// Provides extra filters for nested queries, sorts, and aggregations. Runs in the resolution phase.
    /// </summary>
    public NestedFilterResolver? NestedFilterResolver { get; set; }

    /// <summary>
    /// Configures a mapping from the index mapping defined in code.
    /// </summary>
    public ElasticsearchQueryParserConfiguration UseMappings(Elastic.Clients.Elasticsearch.Mapping.TypeMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        MappingResolver = ElasticMappingResolver.Create(mapping);
        return this;
    }

    /// <summary>
    /// Configures a mapping resolver.
    /// </summary>
    public ElasticsearchQueryParserConfiguration UseMappings(ElasticMappingResolver resolver)
    {
        MappingResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    /// <summary>
    /// Switches to search-box behavior: scoring, with OR as the default operator.
    /// </summary>
    public ElasticsearchQueryParserConfiguration UseSearchMode()
    {
        UseScoring = true;
        DefaultOperator = Ast.BooleanOperator.Or;
        return this;
    }
}
