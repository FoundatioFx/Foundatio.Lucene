namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Per-request (or per-index, per-tenant) settings for <see cref="ElasticsearchQueryParser"/>. Immutable, so an
/// instance can be cached and shared.
/// </summary>
public sealed record ElasticsearchQueryOptions : QueryOptionsBase
{
    /// <summary>
    /// The name of options registered with <see cref="ElasticsearchQueryParser.SetOptions(string, ElasticsearchQueryOptions)"/>
    /// (typically an index name) to apply beneath these options.
    /// </summary>
    public string? Index { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.UseScoring"/>.
    /// </summary>
    public bool? UseScoring { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.MappingResolver"/> (for example per index).
    /// </summary>
    public ElasticMappingResolver? MappingResolver { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.DefaultTimeZone"/>.
    /// </summary>
    public string? DefaultTimeZone { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.GeoLocationResolver"/>.
    /// </summary>
    public GeoLocationResolver? GeoLocationResolver { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.RuntimeFieldResolver"/>.
    /// </summary>
    public RuntimeFieldResolver? RuntimeFieldResolver { get; init; }

    /// <summary>
    /// Set to <see langword="false"/> to skip the runtime field resolver for a request (for example on endpoints
    /// that shouldn't pay for runtime fields), which also lets the synchronous methods run without it.
    /// </summary>
    public bool? EnableRuntimeFieldResolver { get; init; }

    /// <summary>
    /// Overrides <see cref="ElasticsearchQueryParserConfiguration.NestedFilterResolver"/>.
    /// </summary>
    public NestedFilterResolver? NestedFilterResolver { get; init; }

    /// <summary>
    /// The start of the time range being queried. With <see cref="EndDate"/>, date histograms without an explicit
    /// interval choose one that yields about 100 buckets and set <c>extended_bounds</c>.
    /// </summary>
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// The end of the time range being queried. See <see cref="StartDate"/>.
    /// </summary>
    public DateTimeOffset? EndDate { get; init; }
}
