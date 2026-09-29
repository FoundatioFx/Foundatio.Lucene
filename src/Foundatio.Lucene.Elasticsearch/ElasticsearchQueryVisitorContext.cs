using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// The visitor context used by <see cref="ElasticsearchQueryParser"/>. After a build it also exposes the runtime
/// fields the query needs.
/// </summary>
public class ElasticsearchQueryVisitorContext : QueryVisitorContext
{
    private List<ElasticRuntimeField>? _runtimeFields;
    private Dictionary<string, string>? _geoLocations;
    private Dictionary<(string Path, string Field), Query?>? _nestedFilters;

    /// <summary>
    /// Whether the query runs in scoring context.
    /// </summary>
    public bool UseScoring { get; set; }

    /// <summary>
    /// The index mapping, or null when none is configured.
    /// </summary>
    public ElasticMappingResolver? MappingResolver { get; set; }

    /// <summary>
    /// The time zone applied to date ranges and date histograms that do not specify one.
    /// </summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary>
    /// Whether nested fields are queried with nested queries.
    /// </summary>
    public bool UseNested { get; set; } = true;

    /// <summary>
    /// Resolves location text in geo distance queries.
    /// </summary>
    public GeoLocationResolver? GeoLocationResolver { get; set; }

    /// <summary>
    /// Provides runtime field definitions for unmapped fields.
    /// </summary>
    public RuntimeFieldResolver? RuntimeFieldResolver { get; set; }

    /// <summary>
    /// Provides extra filters for nested queries, sorts, and aggregations.
    /// </summary>
    public NestedFilterResolver? NestedFilterResolver { get; set; }

    /// <summary>
    /// The start of the time range being queried, used for automatic date histogram intervals.
    /// </summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>
    /// The end of the time range being queried, used for automatic date histogram intervals.
    /// </summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Runtime fields the expression uses. Add them to the search request's <c>runtime_mappings</c>.
    /// </summary>
    public IReadOnlyList<ElasticRuntimeField> RuntimeFields => _runtimeFields ?? (IReadOnlyList<ElasticRuntimeField>)[];

    /// <summary>
    /// Adds a runtime field definition.
    /// </summary>
    public void AddRuntimeField(ElasticRuntimeField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _runtimeFields ??= [];
        if (!_runtimeFields.Exists(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
            _runtimeFields.Add(field);
    }

    internal ElasticRuntimeField? GetRuntimeField(string field) =>
        _runtimeFields?.Find(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));

    internal void SetGeoLocation(string text, string location) => (_geoLocations ??= new(StringComparer.Ordinal))[text] = location;

    internal string? GetGeoLocation(string text) => _geoLocations is not null && _geoLocations.TryGetValue(text, out string? location) ? location : null;

    internal void SetNestedFilter(string path, string field, Query? filter) => (_nestedFilters ??= [])[(path, field)] = filter;

    internal Query? GetNestedFilter(string path, string field) =>
        _nestedFilters is not null && _nestedFilters.TryGetValue((path, field), out var filter) ? filter : null;
}
