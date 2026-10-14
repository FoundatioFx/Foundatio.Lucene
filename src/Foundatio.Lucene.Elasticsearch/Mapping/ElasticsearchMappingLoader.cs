using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Fetches a server mapping with the Elasticsearch client, synchronously or asynchronously, and combines the
/// mappings of every index the target (an index name, alias, or pattern) resolves to.
/// </summary>
/// <remarks>
/// When the target resolves to several indices, their mappings are merged in descending ordinal order of index
/// name, so for date or rollover suffixed indices the newest index is authoritative. A field whose type differs
/// between indices keeps the type from the first index in that order and is logged; fields that exist in only
/// some indices are included.
/// </remarks>
internal sealed class ElasticsearchMappingLoader
{
    private static readonly MappingMerger _anonymousMerger = new();

    private readonly ElasticsearchClient _client;
    private readonly Indices _indices;
    private readonly ILogger _logger;
    private string? _lastConflictSignature;

    public ElasticsearchMappingLoader(ElasticsearchClient client, Indices indices, ILogger logger)
    {
        _client = client;
        _indices = indices;
        _logger = logger;
    }

    public TypeMapping? Load()
    {
        var response = _client.Indices.GetMapping(new GetMappingRequest(_indices));
        return GetMapping(response);
    }

    public async Task<TypeMapping?> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await _client.Indices.GetMappingAsync(new GetMappingRequest(_indices), cancellationToken).ConfigureAwait(false);
        return GetMapping(response);
    }

    private TypeMapping? GetMapping(GetMappingResponse response)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("GetMapping response for {Target}: {DebugInformation}", _indices, response.DebugInformation);

        if (!response.IsValidResponse)
        {
            response.TryGetOriginalException(out var exception);
            _logger.LogWarning(exception, "Unable to get server mapping for {Target}: [{StatusCode}] {Reason}",
                _indices, response.ApiCallDetails?.HttpStatusCode, response.ElasticsearchServerError?.Error?.Reason ?? exception?.Message);
            return null;
        }

        var indices = response.Mappings?
            .Where(pair => pair.Value?.Mappings is not null)
            .OrderByDescending(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        if (indices is null || indices.Count == 0)
        {
            _logger.LogWarning("Server mapping for {Target} did not contain any index mappings", _indices);
            return null;
        }

        if (indices.Count == 1)
            return indices[0].Value.Mappings;

        return MergeIndexMappings(indices);
    }

    private TypeMapping MergeIndexMappings(List<KeyValuePair<string, IndexMappingRecord>> indices)
    {
        List<string>? conflicts = null;
        var merger = _logger.IsEnabled(LogLevel.Warning)
            ? new MappingMerger(onTypeConflict: (path, primary, secondary) => (conflicts ??= []).Add($"{path} ({primary.Type} vs {secondary.Type})"))
            : _anonymousMerger;

        var primaryMapping = indices[0].Value.Mappings;
        var properties = primaryMapping.Properties;
        for (int i = 1; i < indices.Count; i++)
            properties = merger.MergeToProperties(indices[i].Value.Mappings.Properties, properties);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Merged server mappings of {IndexCount} indices for {Target}: {Indices}",
                indices.Count, _indices, string.Join(", ", indices.Select(pair => pair.Key)));

        LogConflicts(conflicts);

        // A shallow copy keeps every mapping level setting of the authoritative index without mutating the response.
        var merged = MappingProperty.ShallowClone(primaryMapping);
        merged.Properties = properties;
        return merged;
    }

    private void LogConflicts(List<string>? conflicts)
    {
        if (conflicts is null)
        {
            _lastConflictSignature = null;
            return;
        }

        // Reloads repeat the same conflicts; only report them when they change.
        string signature = string.Join(", ", conflicts);
        if (string.Equals(signature, Volatile.Read(ref _lastConflictSignature), StringComparison.Ordinal))
            return;

        Volatile.Write(ref _lastConflictSignature, signature);
        _logger.LogWarning("Server mappings for {Target} define {ConflictCount} fields with different types across indices; the newest index (by descending name) is used: {Conflicts}",
            _indices, conflicts.Count, signature);
    }
}
