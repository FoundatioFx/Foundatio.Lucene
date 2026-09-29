using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Resolves field names against a merged view of a code mapping and the server mapping of an Elasticsearch
/// index, and answers type questions about them (analyzed, nested, geo, numeric, and so on).
/// </summary>
/// <remarks>
/// <para><b>Two phases.</b> Loading is asynchronous and explicit: await <see cref="EnsureLoadedAsync"/> or
/// <see cref="EnsureFieldsAsync"/> before building a query. Every lookup (<see cref="GetMapping(string, bool)"/>
/// and the helpers built on it) is synchronous and lock-free against the current immutable mapping snapshot and
/// never blocks on asynchronous work.</para>
/// <para><b>Synchronous loading.</b> When the mapping has not been loaded, a lookup loads it with the synchronous
/// loader if the resolver has one (for example the client's synchronous <c>GetMapping</c> API); otherwise it throws
/// <see cref="InvalidOperationException"/>. Check <see cref="CanResolveSynchronously"/> to decide whether callers
/// must use the asynchronous phase first.</para>
/// <para><b>Unmapped fields.</b> A field missing from a loaded mapping is the strongest available signal that the
/// mapping changed (fields created by dynamic templates only exist after the first document that uses them is
/// indexed), so it triggers a reload, at most once per <see cref="UnmappedFieldRefreshInterval"/>. Asynchronous
/// reloads happen in <see cref="EnsureFieldsAsync"/>; synchronous lookups only reload through a synchronous
/// loader. A load that finds a requested field missing counts as that field's reload attempt.</para>
/// <para><b>Merging.</b> The server mapping is authoritative. A code property contributes its children
/// (multi-fields or object properties) only when the server property has the same client property type, and
/// code-only properties are included. Neither mapping is modified. Dynamic templates are not simulated.</para>
/// <para><b>Lookup.</b> Dotted names are walked part by part, matching each part exactly first and then ignoring
/// case, and the result uses the canonical mapped spelling. Multi-fields and object or nested properties are
/// both children.</para>
/// <para>Configure the resolver before sharing it. Loaders must enforce their own finite timeout; they receive the
/// resolver lifetime token, which <see cref="Dispose"/> cancels.</para>
/// </remarks>
public sealed class ElasticMappingResolver : IDisposable
{
    private static readonly TimeSpan _suppressedRefreshWarningInterval = TimeSpan.FromMinutes(1);

    private readonly TypeMapping? _codeMapping;
    private readonly Inferrer? _inferrer;
    private readonly MappingCache _cache;
    private readonly ConditionalWeakTable<IProperty, ConcurrentDictionary<string, object>> _propertyMetadata = new();
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private string _keywordSubFieldName = ElasticMappingExtensions.KeywordFieldName;
    private string _sortSubFieldName = ElasticMappingExtensions.SortFieldName;
    private long _lastSuppressedRefreshWarningTimestamp;

    /// <summary>
    /// Creates a resolver backed by a synchronous mapping loader, which is called lazily and again when a field
    /// cannot be resolved (throttled by <see cref="UnmappedFieldRefreshInterval"/>).
    /// </summary>
    /// <param name="getMapping">Returns the server mapping, or <see langword="null"/> when it is unavailable.</param>
    /// <param name="inferrer">Resolves property expressions and <see cref="Field"/> names.</param>
    /// <param name="timeProvider">The time provider used for refresh throttling.</param>
    /// <param name="logger">The logger.</param>
    public ElasticMappingResolver(Func<TypeMapping?> getMapping, Inferrer? inferrer = null, TimeProvider? timeProvider = null, ILogger? logger = null)
        : this(null, inferrer, getMapping ?? throw new ArgumentNullException(nameof(getMapping)), null, timeProvider, logger)
    {
    }

    /// <summary>Creates a resolver for a code mapping, optionally merged with a synchronously loaded server mapping.</summary>
    /// <param name="codeMapping">The mapping declared in code.</param>
    /// <param name="inferrer">Resolves property expressions in <paramref name="codeMapping"/> and <see cref="Field"/> names.</param>
    /// <param name="getMapping">Returns the server mapping, or <see langword="null"/> when it is unavailable. When omitted only the code mapping is used.</param>
    /// <param name="timeProvider">The time provider used for refresh throttling.</param>
    /// <param name="logger">The logger.</param>
    public ElasticMappingResolver(TypeMapping codeMapping, Inferrer? inferrer = null, Func<TypeMapping?>? getMapping = null,
        TimeProvider? timeProvider = null, ILogger? logger = null)
        : this(codeMapping ?? throw new ArgumentNullException(nameof(codeMapping)), inferrer, getMapping, null, timeProvider, logger)
    {
    }

    private ElasticMappingResolver(TypeMapping? codeMapping, Inferrer? inferrer, Func<TypeMapping?>? getMapping,
        Func<CancellationToken, Task<TypeMapping?>>? getMappingAsync, TimeProvider? timeProvider, ILogger? logger)
    {
        _codeMapping = codeMapping;
        _inferrer = inferrer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;

        var merger = new MappingMerger(inferrer, CopyPropertyMetadata);
        _cache = new MappingCache(getMapping, getMappingAsync, serverMapping => merger.Merge(_codeMapping?.Properties, serverMapping?.Properties),
            _timeProvider, _logger);
    }

    /// <summary>A shared resolver with an empty mapping: every field is unmapped and no I/O is ever performed.</summary>
    public static ElasticMappingResolver NullInstance { get; } = new(new TypeMapping());

    /// <summary>
    /// Minimum interval between reloads triggered by fields that cannot be resolved from the loaded mapping.
    /// Defaults to 5 seconds.
    /// </summary>
    /// <remarks>
    /// A reload walks and merges the whole property tree, so this trades staleness against that cost as well as
    /// against request load on the cluster. Failed loads also wait for this interval before retrying.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or zero.</exception>
    public TimeSpan UnmappedFieldRefreshInterval
    {
        get => _cache.UnmappedFieldRefreshInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _cache.UnmappedFieldRefreshInterval = value;
        }
    }

    /// <summary>
    /// Maximum time a caller waits to join a mapping load that another caller started. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// Only one load runs at a time, so concurrent callers wait for it instead of issuing their own. This must
    /// comfortably exceed the latency of the mapping fetch; giving up early treats fields as unmapped even though a
    /// load that could have resolved them is running. The loader must enforce a shorter timeout.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive or exceeds <see cref="int.MaxValue"/> milliseconds.</exception>
    public TimeSpan MappingRefreshWaitTimeout
    {
        get => _cache.RefreshWaitTimeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, TimeSpan.FromMilliseconds(int.MaxValue));
            _cache.RefreshWaitTimeout = value;
        }
    }

    /// <summary>
    /// Optionally identifies a server mapping revision so reloads of an unchanged revision reuse the merged mapping
    /// and resolved fields. Null or empty revisions disable reuse for that load.
    /// </summary>
    /// <remarks>
    /// The revision must identify the complete mapping, including dynamically added fields and the concrete index.
    /// The callback must be cheap, must not mutate the mapping, and must not call this resolver. An exception from
    /// it counts as a failed load. <see cref="RefreshMapping"/> always discards the cached mapping regardless of revision.
    /// </remarks>
    public Func<TypeMapping, string?>? ServerMappingRevisionResolver
    {
        get => _cache.ServerMappingRevisionResolver;
        set => _cache.ServerMappingRevisionResolver = value;
    }

    /// <summary>The preferred sub-field returned by <see cref="GetAggregationsFieldName(string)"/>. Defaults to <c>keyword</c>.</summary>
    public string KeywordSubFieldName
    {
        get => _keywordSubFieldName;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _keywordSubFieldName = value;
        }
    }

    /// <summary>The preferred sub-field returned by <see cref="GetSortFieldName(string)"/>. Defaults to <c>sort</c>.</summary>
    public string SortSubFieldName
    {
        get => _sortSubFieldName;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _sortSubFieldName = value;
        }
    }

    /// <summary>
    /// Whether a current mapping is loaded, so <see cref="EnsureLoadedAsync"/> has nothing to do. Always
    /// <see langword="true"/> for a resolver without a server loader. <see langword="false"/> after a failed load
    /// or <see cref="RefreshMapping"/> until the next successful load.
    /// </summary>
    public bool IsLoaded => _cache.Current.IsLoaded;

    /// <summary>
    /// Whether synchronous lookups can be served without throwing: the resolver has a synchronous loader, has no
    /// server loader, or has completed a load attempt. When <see langword="false"/>, await
    /// <see cref="EnsureLoadedAsync"/> or <see cref="EnsureFieldsAsync"/> first.
    /// </summary>
    public bool CanResolveSynchronously => _cache.HasSynchronousLoader || _cache.Current.Kind != SnapshotKind.Initial;

    /// <summary>Loads the mapping if it has not been loaded or was invalidated by <see cref="RefreshMapping"/>.</summary>
    /// <remarks>
    /// Concurrent callers share one load; cancellation stops only this caller's wait. A load that fails is logged and
    /// leaves only the code mapping available (and <see cref="IsLoaded"/> false); it is retried after
    /// <see cref="UnmappedFieldRefreshInterval"/>. On return <see cref="CanResolveSynchronously"/> is <see langword="true"/>.
    /// </remarks>
    /// <exception cref="TimeoutException">No mapping has ever been loaded and joining a load another caller started exceeded <see cref="MappingRefreshWaitTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _cache.Current;
        if (!snapshot.RequiresLoad)
            return ValueTask.CompletedTask;

        var load = LoadRequiredMappingAsync(snapshot, cancellationToken);
        if (load.IsCompletedSuccessfully)
            return ValueTask.CompletedTask;

        return AwaitLoadAsync(load);

        static async ValueTask AwaitLoadAsync(ValueTask<LoadResult> load) => await load.ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the mapping if needed and checks that every field (and the target of every alias) is mapped. When a
    /// field is missing from a mapping that was already loaded, the mapping is reloaded once, subject to the
    /// <see cref="UnmappedFieldRefreshInterval"/> throttle, and the fields are checked again.
    /// </summary>
    /// <param name="fields">The field names that the following synchronous lookups will use. Blank names are ignored.</param>
    /// <param name="cancellationToken">Cancels this caller's wait, not a shared load.</param>
    /// <returns>Whether every field was found.</returns>
    /// <exception cref="TimeoutException">No mapping has ever been loaded and joining a load another caller started exceeded <see cref="MappingRefreshWaitTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async ValueTask<bool> EnsureFieldsAsync(IEnumerable<string> fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        string[] requested = fields.Where(field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.Ordinal).ToArray();

        var snapshot = _cache.Current;
        var loadResult = LoadResult.Unavailable;
        bool loaded = false;
        if (snapshot.RequiresLoad)
        {
            loadResult = await LoadRequiredMappingAsync(snapshot, cancellationToken).ConfigureAwait(false);
            snapshot = _cache.Current;
            loaded = true;
        }

        var missing = FindMissingFields(requested, snapshot);
        if (missing is null)
            return true;

        if (loaded)
        {
            if (loadResult == LoadResult.Updated)
                _cache.ArmThrottle();
        }
        else if (snapshot.IsLoaded && _cache.HasServerLoader)
        {
            loadResult = await _cache.LoadAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (loadResult == LoadResult.Updated)
            {
                snapshot = _cache.Current;
                missing = FindMissingFields(missing, snapshot);
                if (missing is null)
                    return true;
            }
        }

        foreach (string field in missing)
            LogUnresolvedField(field, loadResult, snapshot);

        return false;
    }

    /// <summary>
    /// Discards the loaded mapping and reloads it, bypassing the unmapped field throttle. A load in flight is
    /// superseded and its result is not published.
    /// </summary>
    /// <remarks>Equivalent to <see cref="RefreshMapping"/> followed by <see cref="EnsureLoadedAsync"/>.</remarks>
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshMapping();
        return EnsureLoadedAsync(cancellationToken);
    }

    /// <summary>
    /// Invalidates the loaded mapping so the next load bypasses the unmapped field throttle and fetches it again. A
    /// load in flight is superseded and its result is not published.
    /// </summary>
    /// <remarks>
    /// With a synchronous loader the next lookup reloads. Otherwise synchronous lookups keep using the previous
    /// mapping until <see cref="EnsureLoadedAsync"/> or <see cref="EnsureFieldsAsync"/> reloads it. If that reload
    /// fails, the previous mapping is discarded and only the code mapping remains.
    /// </remarks>
    public void RefreshMapping()
    {
        if (!_cache.HasServerLoader)
            return;

        _cache.Invalidate();
        _logger.LogInformation("Mapping refresh triggered");
    }

    /// <summary>
    /// Resolves a field name, loading the mapping synchronously if needed and possible, and reloading it (through a
    /// synchronous loader only, throttled) when the field is missing.
    /// </summary>
    /// <param name="field">The field name, using dots for sub-fields and object properties.</param>
    /// <param name="followAlias">Whether to return the target of a field alias instead of the alias itself.</param>
    /// <returns>The mapping; <see cref="FieldMapping.Found"/> is <see langword="false"/> when the field is not mapped.</returns>
    /// <exception cref="InvalidOperationException">The mapping has not been loaded and the resolver has no synchronous loader.</exception>
    public FieldMapping GetMapping(string field, bool followAlias = false)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(field))
            return new FieldMapping(field, null);

        var snapshot = _cache.Current;
        if (!snapshot.RequiresLoad && snapshot.TryGetField(field, out var cached))
            return FollowAlias(cached, followAlias);

        return ResolveSynchronously(field, followAlias, snapshot);
    }

    /// <inheritdoc cref="GetMapping(string, bool)"/>
    public FieldMapping GetMapping(Field field, bool followAlias = false) => GetMapping(InferFieldName(field), followAlias);

    /// <summary>
    /// Ensures the field is loaded as <see cref="EnsureFieldsAsync"/> does and resolves it against the resulting
    /// mapping. Completes synchronously when the field is already resolvable.
    /// </summary>
    public ValueTask<FieldMapping> GetMappingAsync(string field, bool followAlias = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(field))
            return ValueTask.FromResult(new FieldMapping(field, null));

        var snapshot = _cache.Current;
        if (!snapshot.RequiresLoad)
        {
            var mapping = LookupInSnapshot(field, followAlias, snapshot);
            if (mapping.Found)
                return ValueTask.FromResult(mapping);
        }

        return LoadAndGetMappingAsync(field, followAlias, cancellationToken);
    }

    private async ValueTask<FieldMapping> LoadAndGetMappingAsync(string field, bool followAlias, CancellationToken cancellationToken)
    {
        await EnsureFieldsAsync([field], cancellationToken).ConfigureAwait(false);
        return LookupInSnapshot(field, followAlias, _cache.Current);
    }

    /// <summary>Returns the mapped property of a field, or <see langword="null"/> when it is not mapped.</summary>
    /// <inheritdoc cref="GetMapping(string, bool)"/>
    public IProperty? GetMappingProperty(string field, bool followAlias = false) => GetMapping(field, followAlias).Property;

    /// <inheritdoc cref="GetMappingProperty(string, bool)"/>
    public IProperty? GetMappingProperty(Field field, bool followAlias = false) => GetMapping(field, followAlias).Property;

    /// <summary>
    /// Returns the canonical path of a field, following aliases to their target. Unmapped parts are kept as
    /// requested. Blank names are returned unchanged.
    /// </summary>
    /// <remarks>Use <see cref="GetMapping(string, bool)"/> without following aliases for the canonical path of the alias itself.</remarks>
    /// <inheritdoc cref="GetMapping(string, bool)" path="/exception"/>
    public string GetResolvedField(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(field))
            return field;

        var mapping = GetMapping(field);
        return FollowAlias(mapping, followAlias: true) is { Found: true } target ? target.FullPath : mapping.FullPath;
    }

    /// <inheritdoc cref="GetResolvedField(string)"/>
    public string GetResolvedField(Field field) => GetResolvedField(InferFieldName(field));

    /// <summary>Returns the field to sort on: a non-analyzed field, preferring the <see cref="SortSubFieldName"/> sub-field.</summary>
    /// <inheritdoc cref="GetNonAnalyzedFieldName(string, string?)" path="/remarks"/>
    public string GetSortFieldName(string field) => GetNonAnalyzedFieldName(field, SortSubFieldName);

    /// <inheritdoc cref="GetSortFieldName(string)"/>
    public string GetSortFieldName(Field field) => GetSortFieldName(InferFieldName(field));

    /// <summary>Returns the field to aggregate on: a non-analyzed field, preferring the <see cref="KeywordSubFieldName"/> sub-field.</summary>
    /// <inheritdoc cref="GetNonAnalyzedFieldName(string, string?)" path="/remarks"/>
    public string GetAggregationsFieldName(string field) => GetNonAnalyzedFieldName(field, KeywordSubFieldName);

    /// <inheritdoc cref="GetAggregationsFieldName(string)"/>
    public string GetAggregationsFieldName(Field field) => GetAggregationsFieldName(InferFieldName(field));

    /// <summary>Returns a non-analyzed field for exact matching, sorting, or aggregating on <paramref name="field"/>.</summary>
    /// <param name="field">The field name.</param>
    /// <param name="preferredSubField">The sub-field to prefer when the field is analyzed.</param>
    /// <remarks>
    /// A field that is unmapped or not analyzed is returned as its canonical path (an alias keeps its own path). An
    /// analyzed field (see <see cref="IsPropertyAnalyzed(IProperty?)"/>) resolves through aliases and returns its
    /// non-analyzed sub-field named <paramref name="preferredSubField"/>, else its first non-analyzed sub-field,
    /// else the analyzed field itself.
    /// </remarks>
    public string GetNonAnalyzedFieldName(string field, string? preferredSubField = null)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(field))
            return field;

        var mapping = GetMapping(field);
        var target = FollowAlias(mapping, followAlias: true);
        if (!target.Found || !IsPropertyAnalyzed(target.Property))
            return mapping.FullPath;

        var children = target.Children;
        if (children is null || children.Count == 0)
            return target.FullPath;

        MergedNode? nonAnalyzed = null;
        foreach (var child in children.Nodes)
        {
            if (!IsNonAnalyzed(child.Property))
                continue;

            if (child.Name == preferredSubField)
            {
                nonAnalyzed = child;
                break;
            }

            nonAnalyzed ??= child;
        }

        return nonAnalyzed is not null ? $"{target.FullPath}.{nonAnalyzed.Name}" : target.FullPath;
    }

    /// <inheritdoc cref="GetNonAnalyzedFieldName(string, string?)"/>
    public string GetNonAnalyzedFieldName(Field field, string? preferredSubField = null) => GetNonAnalyzedFieldName(InferFieldName(field), preferredSubField);

    private bool IsNonAnalyzed(IProperty property) => property is KeywordProperty || !IsPropertyAnalyzed(property);

    /// <summary>
    /// Whether a field (following aliases) is analyzed. Blank names are treated as analyzed and unmapped fields as not analyzed.
    /// </summary>
    /// <inheritdoc cref="IsPropertyAnalyzed(IProperty?)" path="/remarks"/>
    public bool IsPropertyAnalyzed(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrWhiteSpace(field))
            return true;

        return IsPropertyAnalyzed(GetMappingProperty(field, followAlias: true));
    }

    /// <summary>Whether a property is analyzed.</summary>
    /// <remarks>
    /// Only <c>text</c> properties with <c>index</c> unset or <see langword="true"/> are analyzed. Types such as
    /// <c>match_only_text</c>, <c>search_as_you_type</c>, and <c>semantic_text</c> count as not analyzed.
    /// </remarks>
    public bool IsPropertyAnalyzed(IProperty? property) => property is TextProperty text && (text.Index ?? true);

    /// <summary>Whether a field (following aliases) is mapped as <c>nested</c>.</summary>
    public bool IsNestedPropertyType(string field) => GetPropertyOrNull(field) is NestedProperty;

    /// <summary>Whether a field (following aliases) is mapped as <c>geo_point</c>.</summary>
    public bool IsGeoPropertyType(string field) => GetPropertyOrNull(field) is GeoPointProperty;

    /// <summary>
    /// Whether a field (following aliases) is mapped as a number: <c>byte</c>, <c>double</c>, <c>float</c>,
    /// <c>half_float</c>, <c>integer</c>, <c>long</c>, <c>scaled_float</c>, <c>short</c>, or <c>unsigned_long</c>.
    /// </summary>
    public bool IsNumericPropertyType(string field) => GetPropertyOrNull(field) is ByteNumberProperty
        or DoubleNumberProperty
        or FloatNumberProperty
        or HalfFloatNumberProperty
        or IntegerNumberProperty
        or LongNumberProperty
        or ScaledFloatNumberProperty
        or ShortNumberProperty
        or UnsignedLongNumberProperty;

    /// <summary>Whether a field (following aliases) is mapped as <c>boolean</c>.</summary>
    public bool IsBooleanPropertyType(string field) => GetPropertyOrNull(field) is BooleanProperty;

    /// <summary>Whether a field (following aliases) is mapped as <c>date</c> or <c>date_nanos</c>.</summary>
    public bool IsDatePropertyType(string field) => GetPropertyOrNull(field) is DateProperty or DateNanosProperty;

    /// <summary>Returns the client field type of a field (following aliases), or <see cref="FieldType.None"/> when it is unmapped.</summary>
    public FieldType GetFieldType(string field) => GetFieldType(GetPropertyOrNull(field));

    /// <summary>
    /// Maps a property's Elasticsearch type to the client <see cref="FieldType"/>, or <see cref="FieldType.None"/>
    /// for <see langword="null"/> and types the enumeration does not define. <c>unsigned_long</c> maps to
    /// <see cref="FieldType.Long"/>, and <c>point</c> to <see cref="FieldType.Shape"/>.
    /// </summary>
    public static FieldType GetFieldType(IProperty? property) => property?.Type switch
    {
        "aggregate_metric_double" => FieldType.AggregateMetricDouble,
        "alias" => FieldType.Alias,
        "binary" => FieldType.Binary,
        "boolean" => FieldType.Boolean,
        "byte" => FieldType.Byte,
        "completion" => FieldType.Completion,
        "constant_keyword" => FieldType.ConstantKeyword,
        "counted_keyword" => FieldType.CountedKeyword,
        "date" => FieldType.Date,
        "date_nanos" => FieldType.DateNanos,
        "date_range" => FieldType.DateRange,
        "dense_vector" => FieldType.DenseVector,
        "double" => FieldType.Double,
        "double_range" => FieldType.DoubleRange,
        "flattened" => FieldType.Flattened,
        "float" => FieldType.Float,
        "float_range" => FieldType.FloatRange,
        "geo_point" => FieldType.GeoPoint,
        "geo_shape" => FieldType.GeoShape,
        "half_float" => FieldType.HalfFloat,
        "histogram" => FieldType.Histogram,
        "icu_collation_keyword" => FieldType.IcuCollationKeyword,
        "integer" => FieldType.Integer,
        "integer_range" => FieldType.IntegerRange,
        "ip" => FieldType.Ip,
        "ip_range" => FieldType.IpRange,
        "join" => FieldType.Join,
        "keyword" => FieldType.Keyword,
        "long" or "unsigned_long" => FieldType.Long,
        "long_range" => FieldType.LongRange,
        "match_only_text" => FieldType.MatchOnlyText,
        "murmur3" => FieldType.Murmur3,
        "nested" => FieldType.Nested,
        "object" => FieldType.Object,
        "passthrough" => FieldType.Passthrough,
        "percolator" => FieldType.Percolator,
        "point" or "shape" => FieldType.Shape,
        "rank_feature" => FieldType.RankFeature,
        "rank_features" => FieldType.RankFeatures,
        "scaled_float" => FieldType.ScaledFloat,
        "search_as_you_type" => FieldType.SearchAsYouType,
        "semantic_text" => FieldType.SemanticText,
        "short" => FieldType.Short,
        "sparse_vector" => FieldType.SparseVector,
        "text" => FieldType.Text,
        "token_count" => FieldType.TokenCount,
        "version" => FieldType.Version,
        "wildcard" => FieldType.Wildcard,
        _ => FieldType.None
    };

    /// <summary>Returns the metadata bag this resolver keeps for a property instance, creating it if needed.</summary>
    /// <remarks>Metadata is copied onto the merged properties the resolver returns, so it can be set on code or server properties.</remarks>
    public IDictionary<string, object> GetPropertyMetadata(IProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return _propertyMetadata.GetOrCreateValue(property);
    }

    /// <summary>Returns a metadata value for a property, converted to <typeparamref name="T"/>, or <paramref name="defaultValue"/>.</summary>
    public T? GetPropertyMetadataValue<T>(IProperty property, string key, T? defaultValue = default)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(key);

        if (!_propertyMetadata.TryGetValue(property, out var metadata) || !metadata.TryGetValue(key, out object? value))
            return defaultValue;

        if (value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return defaultValue;
        }
    }

    /// <summary>Sets a metadata value for a property.</summary>
    public void SetPropertyMetadataValue(IProperty property, string key, object value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        _propertyMetadata.GetOrCreateValue(property)[key] = value;
    }

    /// <summary>Copies every metadata value of <paramref name="source"/> to <paramref name="target"/>.</summary>
    public void CopyPropertyMetadata(IProperty source, IProperty target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (!_propertyMetadata.TryGetValue(source, out var sourceMetadata))
            return;

        var targetMetadata = _propertyMetadata.GetOrCreateValue(target);
        foreach (var pair in sourceMetadata)
            targetMetadata[pair.Key] = pair.Value;
    }

    /// <summary>Cancels the loader lifetime token and stops further loads. A no-op for <see cref="NullInstance"/>.</summary>
    public void Dispose()
    {
        // The shared null instance is process wide; disposing it must not affect other consumers.
        if (ReferenceEquals(this, NullInstance))
            return;

        _cache.Dispose();
    }

    private FieldMapping ResolveSynchronously(string field, bool followAlias, MappingSnapshot snapshot)
    {
        var loadResult = LoadResult.Unavailable;
        bool loaded = false;
        if (snapshot.RequiresLoad)
        {
            if (_cache.HasSynchronousLoader)
            {
                loadResult = _cache.Load(snapshot);
                snapshot = _cache.Current;
                loaded = true;
            }
            else if (snapshot.Kind == SnapshotKind.Initial)
            {
                throw new InvalidOperationException(
                    $"The Elasticsearch mapping has not been loaded and the resolver has no synchronous loader. Await {nameof(EnsureLoadedAsync)} or {nameof(EnsureFieldsAsync)} (or use the asynchronous build methods) before resolving fields synchronously.");
            }
        }

        var mapping = Lookup(field, snapshot);
        if (!mapping.Found)
        {
            if (loaded)
            {
                if (loadResult == LoadResult.Updated)
                    _cache.ArmThrottle();
            }
            else if (snapshot.IsLoaded && _cache.HasSynchronousLoader)
            {
                loadResult = _cache.Load(snapshot);
                if (loadResult == LoadResult.Updated)
                {
                    snapshot = _cache.Current;
                    mapping = Lookup(field, snapshot);
                }
            }
        }

        if (!mapping.Found)
        {
            LogUnresolvedField(field, loadResult, snapshot);
            return mapping;
        }

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Resolved mapping: {Field}={FieldPath}:{FieldType}", field, mapping.FullPath, mapping.Property?.Type);

        return FollowAlias(mapping, followAlias);
    }

    private FieldMapping FollowAlias(FieldMapping mapping, bool followAlias)
    {
        if (!followAlias || mapping.Property is not FieldAliasProperty alias)
            return mapping;

        string? target = GetAliasTarget(alias);
        return string.IsNullOrWhiteSpace(target) ? CreateUnmapped(mapping) : GetMapping(target);
    }

    private IProperty? GetPropertyOrNull(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return string.IsNullOrWhiteSpace(field) ? null : GetMapping(field, followAlias: true).Property;
    }

    private async ValueTask<LoadResult> LoadRequiredMappingAsync(MappingSnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = await _cache.LoadAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (result == LoadResult.WaitTimedOut && _cache.Current.Kind == SnapshotKind.Initial)
        {
            throw new TimeoutException(
                $"The Elasticsearch mapping load started by another caller did not complete within {MappingRefreshWaitTimeout}. Increase {nameof(MappingRefreshWaitTimeout)} if the mapping fetch is expected to take longer.");
        }

        return result;
    }

    /// <summary>Returns the fields that are not mapped in <paramref name="snapshot"/>, or <see langword="null"/> when all are.</summary>
    private List<string>? FindMissingFields(IReadOnlyCollection<string> fields, MappingSnapshot snapshot)
    {
        List<string>? missing = null;
        foreach (string field in fields)
        {
            if (!LookupInSnapshot(field, followAlias: true, snapshot).Found)
                (missing ??= []).Add(field);
        }

        return missing;
    }

    /// <summary>Resolves a field against one snapshot without loading.</summary>
    private FieldMapping LookupInSnapshot(string field, bool followAlias, MappingSnapshot snapshot)
    {
        var mapping = Lookup(field, snapshot);
        if (!followAlias || mapping.Property is not FieldAliasProperty alias)
            return mapping;

        string? target = GetAliasTarget(alias);
        return string.IsNullOrWhiteSpace(target) ? CreateUnmapped(mapping) : Lookup(target, snapshot);
    }

    private static FieldMapping Lookup(string field, MappingSnapshot snapshot)
    {
        if (snapshot.TryGetField(field, out var cached))
            return cached;

        var resolved = Resolve(field, snapshot.Properties);
        snapshot.CacheField(resolved);
        return resolved;
    }

    /// <summary>
    /// Walks a dotted field name through the merged property tree, resolving each part to its canonical name. Parts
    /// past the deepest resolvable one are appended unchanged so callers still get a usable path for an unmapped field.
    /// </summary>
    private static FieldMapping Resolve(string field, MergedProperties? properties)
    {
        int start = 0;
        StringBuilder? resolvedName = null;
        List<string>? nestedPathChain = null;

        while (true)
        {
            int separator = field.IndexOf('.', start);
            string part = separator < 0 ? field[start..] : field[start..separator];

            if (properties is null || !properties.TryGetNode(part, out var matched))
            {
                string remainder = field[start..];
                if (resolvedName is null)
                    return new FieldMapping(remainder, null);

                return new FieldMapping(resolvedName.Append('.').Append(remainder).ToString(), null, null, nestedPathChain);
            }

            if (resolvedName is null)
                resolvedName = new StringBuilder(matched.Name);
            else
                resolvedName.Append('.').Append(matched.Name);

            if (matched.Property is NestedProperty)
                (nestedPathChain ??= []).Add(resolvedName.ToString());

            if (separator < 0)
                return new FieldMapping(resolvedName.ToString(), matched.Property, matched.Children, nestedPathChain);

            properties = matched.Children;
            start = separator + 1;
        }
    }

    private static FieldMapping CreateUnmapped(FieldMapping alias) => new(alias.FullPath, null, null, alias.NestedPathChain);

    private string? GetAliasTarget(FieldAliasProperty alias)
    {
        if (alias.Path is null)
            return null;

        return alias.Path.Name ?? _inferrer?.Field(alias.Path);
    }

    private string InferFieldName(Field field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (_inferrer is not null)
            return _inferrer.Field(field);

        return field.Name ?? throw new InvalidOperationException("Unable to resolve a Field expression without an inferrer.");
    }

    private void LogUnresolvedField(string field, LoadResult loadResult, MappingSnapshot snapshot)
    {
        if (loadResult == LoadResult.WaitTimedOut && ShouldLogSuppressedRefresh())
        {
            _logger.LogWarning("Unable to resolve mapping for field {Field}. A server mapping reload was already in flight but did not complete within {WaitTimeout}, so this field is being treated as unmapped. Increase {Property} if the mapping fetch is expected to take longer than this",
                field, MappingRefreshWaitTimeout, nameof(MappingRefreshWaitTimeout));
        }
        else if (loadResult == LoadResult.Throttled && snapshot.HasServerMapping && ShouldLogSuppressedRefresh())
        {
            _logger.LogWarning("Unable to resolve mapping for field {Field}. The loaded server mapping is {MappingAge} old and a reload was suppressed by the {RefreshInterval} unmapped field refresh throttle, so this field is being treated as unmapped",
                field, _timeProvider.GetUtcNow().UtcDateTime - snapshot.CreatedUtc, UnmappedFieldRefreshInterval);
        }
        else if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Mapping not found: {Field} ({LoadResult})", field, loadResult);
        }
    }

    /// <summary>Rate limits warnings about suppressed reloads so a flood of queries against missing fields cannot flood the log.</summary>
    private bool ShouldLogSuppressedRefresh()
    {
        if (!_logger.IsEnabled(LogLevel.Warning))
            return false;

        long last = Interlocked.Read(ref _lastSuppressedRefreshWarningTimestamp);
        if (last != 0 && _timeProvider.GetElapsedTime(last) < _suppressedRefreshWarningInterval)
            return false;

        long timestamp = _timeProvider.GetTimestamp();
        return Interlocked.CompareExchange(ref _lastSuppressedRefreshWarningTimestamp, timestamp == 0 ? 1 : timestamp, last) == last;
    }

    /// <summary>Creates a resolver for a code mapping only; no I/O is ever performed.</summary>
    /// <param name="codeMapping">The mapping.</param>
    /// <param name="inferrer">Resolves property expressions in <paramref name="codeMapping"/> and <see cref="Field"/> names.</param>
    /// <param name="logger">The logger.</param>
    public static ElasticMappingResolver Create(TypeMapping codeMapping, Inferrer? inferrer = null, ILogger? logger = null)
    {
        return new ElasticMappingResolver(codeMapping, inferrer, logger: logger);
    }

    /// <summary>Creates a resolver backed by a synchronous mapping loader.</summary>
    /// <inheritdoc cref="ElasticMappingResolver(Func{TypeMapping}, Inferrer, TimeProvider, ILogger)"/>
    public static ElasticMappingResolver Create(Func<TypeMapping?> getMapping, Inferrer? inferrer = null, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        return new ElasticMappingResolver(getMapping, inferrer, timeProvider, logger);
    }

    /// <summary>Creates a resolver for a code mapping built with a descriptor, optionally merged with a synchronously loaded server mapping.</summary>
    /// <param name="mappingBuilder">Builds the code mapping.</param>
    /// <param name="inferrer">Resolves the property expressions of the descriptor.</param>
    /// <param name="getMapping">Returns the server mapping, or <see langword="null"/> when it is unavailable. When omitted only the code mapping is used.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">The time provider used for refresh throttling.</param>
    public static ElasticMappingResolver Create<T>(Action<TypeMappingDescriptor<T>> mappingBuilder, Inferrer inferrer, Func<TypeMapping?>? getMapping = null,
        ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inferrer);

        return new ElasticMappingResolver(BuildMapping(mappingBuilder), inferrer, getMapping, timeProvider, logger);
    }

    /// <summary>
    /// Creates a resolver for the server mapping of <paramref name="index"/>, which may be an index name, alias, or
    /// pattern. Both the synchronous and the asynchronous client APIs are used, so synchronous lookups never throw.
    /// </summary>
    /// <remarks>When the target resolves to several indices their mappings are merged; the newest index by descending name wins type conflicts.</remarks>
    public static ElasticMappingResolver Create(ElasticsearchClient client, string index, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(index);

        return CreateServerResolver(client, index, null, logger, timeProvider);
    }

    /// <summary>Creates a resolver for the server mapping of the index inferred for <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="Create(ElasticsearchClient, string, ILogger, TimeProvider)" path="/remarks"/>
    public static ElasticMappingResolver Create<T>(ElasticsearchClient client, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return CreateServerResolver(client, Indices.Index<T>(), null, logger, timeProvider);
    }

    /// <summary>Creates a resolver that merges a code mapping with the server mapping of the index inferred for <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="Create(ElasticsearchClient, string, ILogger, TimeProvider)" path="/remarks"/>
    public static ElasticMappingResolver Create<T>(Action<TypeMappingDescriptor<T>> mappingBuilder, ElasticsearchClient client, ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return CreateServerResolver(client, Indices.Index<T>(), BuildMapping(mappingBuilder), logger, timeProvider);
    }

    /// <summary>Creates a resolver that merges a code mapping with the server mapping of <paramref name="index"/>.</summary>
    /// <inheritdoc cref="Create(ElasticsearchClient, string, ILogger, TimeProvider)" path="/remarks"/>
    public static ElasticMappingResolver Create<T>(Action<TypeMappingDescriptor<T>> mappingBuilder, ElasticsearchClient client, string index,
        ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(index);

        return CreateServerResolver(client, index, BuildMapping(mappingBuilder), logger, timeProvider);
    }

    /// <summary>
    /// Creates a resolver backed only by an asynchronous mapping loader. Synchronous lookups throw until
    /// <see cref="EnsureLoadedAsync"/> or <see cref="EnsureFieldsAsync"/> has completed a load, and never reload.
    /// </summary>
    /// <param name="getMappingAsync">Returns the server mapping, or <see langword="null"/> when it is unavailable. Receives the resolver lifetime token.</param>
    /// <param name="inferrer">Resolves <see cref="Field"/> names.</param>
    /// <param name="timeProvider">The time provider used for refresh throttling.</param>
    /// <param name="logger">The logger.</param>
    public static ElasticMappingResolver CreateWithAsyncLoader(Func<CancellationToken, Task<TypeMapping?>> getMappingAsync, Inferrer? inferrer = null,
        TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(getMappingAsync);

        return new ElasticMappingResolver(null, inferrer, null, getMappingAsync, timeProvider, logger);
    }

    /// <summary>Creates a resolver that merges a code mapping built with a descriptor with an asynchronously loaded server mapping.</summary>
    /// <inheritdoc cref="CreateWithAsyncLoader(Func{CancellationToken, Task{TypeMapping}}, Inferrer, TimeProvider, ILogger)"/>
    public static ElasticMappingResolver CreateWithAsyncLoader<T>(Action<TypeMappingDescriptor<T>> mappingBuilder, Inferrer inferrer,
        Func<CancellationToken, Task<TypeMapping?>> getMappingAsync, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inferrer);
        ArgumentNullException.ThrowIfNull(getMappingAsync);

        return new ElasticMappingResolver(BuildMapping(mappingBuilder), inferrer, null, getMappingAsync, timeProvider, logger);
    }

    /// <summary>
    /// Creates a resolver with both a synchronous and an asynchronous loader for the same server mapping, optionally
    /// merged with a code mapping. Asynchronous loading uses <paramref name="getMappingAsync"/>; synchronous lookups
    /// use <paramref name="getMapping"/>.
    /// </summary>
    public static ElasticMappingResolver CreateWithLoaders(Func<TypeMapping?> getMapping, Func<CancellationToken, Task<TypeMapping?>> getMappingAsync,
        Inferrer? inferrer = null, TypeMapping? codeMapping = null, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(getMapping);
        ArgumentNullException.ThrowIfNull(getMappingAsync);

        return new ElasticMappingResolver(codeMapping, inferrer, getMapping, getMappingAsync, timeProvider, logger);
    }

    private static ElasticMappingResolver CreateServerResolver(ElasticsearchClient client, Indices indices, TypeMapping? codeMapping, ILogger? logger,
        TimeProvider? timeProvider)
    {
        logger ??= NullLogger.Instance;
        var loader = new ElasticsearchMappingLoader(client, indices, logger);
        return new ElasticMappingResolver(codeMapping, client.Infer, loader.Load, loader.LoadAsync, timeProvider, logger);
    }

    private static TypeMapping BuildMapping<T>(Action<TypeMappingDescriptor<T>> mappingBuilder)
    {
        ArgumentNullException.ThrowIfNull(mappingBuilder);

        var mapping = new TypeMapping();
        mappingBuilder(new TypeMappingDescriptor<T>(mapping));
        return mapping;
    }
}
