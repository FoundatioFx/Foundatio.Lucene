using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// The parts of a search request built by <see cref="ElasticsearchQueryParser.BuildSearch"/>: the query, aggregations,
/// sort, and any runtime fields they use.
/// </summary>
public sealed class ElasticsearchSearch
{
    /// <summary>
    /// The query, or null when no query expression was given.
    /// </summary>
    public Query? Query { get; init; }

    /// <summary>
    /// The aggregations, or null when no aggregation expression was given.
    /// </summary>
    public IDictionary<string, Aggregation>? Aggregations { get; init; }

    /// <summary>
    /// The sort, or null when no sort expression was given.
    /// </summary>
    public ICollection<SortOptions>? Sort { get; init; }

    /// <summary>
    /// Runtime fields used by the query, aggregations, or sort. Empty when none are needed.
    /// </summary>
    public IReadOnlyList<ElasticRuntimeField> RuntimeFields { get; init; } = [];

    /// <summary>
    /// The runtime fields in the shape of a search request's <c>runtime_mappings</c>, or null when there are none.
    /// </summary>
    public IDictionary<Field, RuntimeField>? GetRuntimeMappings()
    {
        if (RuntimeFields.Count == 0)
            return null;

        return RuntimeFields.ToDictionary(f => (Field)f.Name, f => f.ToRuntimeField());
    }

    /// <summary>
    /// Sets the query, aggregations, sort, and runtime mappings on a search request. Parts that were not built are
    /// left unchanged.
    /// </summary>
    public SearchRequest ApplyTo(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Query is not null)
            request.Query = Query;
        if (Aggregations is not null)
            request.Aggregations = Aggregations;
        if (Sort is not null)
            request.Sort = Sort;
        if (GetRuntimeMappings() is { } runtimeMappings)
            request.RuntimeMappings = runtimeMappings;

        return request;
    }

    /// <summary>
    /// Sets the query, aggregations, sort, and runtime mappings on a search request descriptor. Parts that were not
    /// built are left unchanged.
    /// </summary>
    public SearchRequestDescriptor<TDocument> ApplyTo<TDocument>(SearchRequestDescriptor<TDocument> descriptor)
    {
        if (Query is not null)
            descriptor.Query(Query);
        if (Aggregations is not null)
            descriptor.Aggregations(Aggregations);
        if (Sort is not null)
            descriptor.Sort(Sort);
        if (GetRuntimeMappings() is { } runtimeMappings)
            descriptor.RuntimeMappings(runtimeMappings);

        return descriptor;
    }
}

/// <summary>
/// Extensions for applying an <see cref="ElasticsearchSearch"/> to search requests.
/// </summary>
public static class ElasticsearchSearchExtensions
{
    /// <summary>
    /// Sets the query, aggregations, sort, and runtime mappings built by <see cref="ElasticsearchQueryParser.BuildSearch"/>.
    /// </summary>
    public static SearchRequestDescriptor<TDocument> Apply<TDocument>(this SearchRequestDescriptor<TDocument> descriptor, ElasticsearchSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return search.ApplyTo(descriptor);
    }
}
