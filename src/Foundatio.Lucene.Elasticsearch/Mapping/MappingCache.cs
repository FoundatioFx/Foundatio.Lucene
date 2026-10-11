using System.Collections.Concurrent;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Loads the server mapping and publishes it as an immutable <see cref="MappingSnapshot"/>. Owns only when to
/// load and how concurrent loads are coalesced; deriving the merged property tree is supplied by the resolver.
/// </summary>
/// <remarks>
/// <para>At most one registered load runs at a time and every caller joins it. Asynchronous callers await the
/// shared load without blocking a thread. Synchronous callers block only on a load that is itself running a
/// synchronous loader; they never wait for an asynchronous loader. When a synchronous caller needs a mapping
/// while an asynchronous load is in flight, it fetches independently with the synchronous loader if the mapping
/// must be (re)loaded, and otherwise continues with the current snapshot.</para>
/// <para>Loaders receive the cache lifetime token rather than a caller token, so a caller that stops waiting
/// never cancels work other callers share.</para>
/// </remarks>
internal sealed class MappingCache : IDisposable
{
    private readonly Func<TypeMapping?>? _loadServerMapping;
    private readonly Func<CancellationToken, Task<TypeMapping?>>? _loadServerMappingAsync;
    private readonly Func<TypeMapping?, MergedProperties?> _merge;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _lifetime = new();

    private MappingSnapshot _snapshot;
    private LoadOperation? _inFlightLoad;
    private long _snapshotVersion;
    private long _generation;
    private long _lastLoadAttemptTimestamp;
    private bool _throttleArmed;
    private bool _disposed;

    public MappingCache(Func<TypeMapping?>? loadServerMapping, Func<CancellationToken, Task<TypeMapping?>>? loadServerMappingAsync,
        Func<TypeMapping?, MergedProperties?> merge, TimeProvider timeProvider, ILogger logger)
    {
        _loadServerMapping = loadServerMapping;
        _loadServerMappingAsync = loadServerMappingAsync;
        _merge = merge;
        _timeProvider = timeProvider;
        _logger = logger;

        var initialKind = HasServerLoader ? SnapshotKind.Initial : SnapshotKind.Loaded;
        _snapshot = new MappingSnapshot(NextVersion(), initialKind, hasServerMapping: false, merge, null, CurrentUtc(), null);
    }

    public TimeSpan UnmappedFieldRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a caller waits to join a load that another caller owns.</summary>
    public TimeSpan RefreshWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public Func<TypeMapping, string?>? ServerMappingRevisionResolver { get; set; }

    public bool HasServerLoader => _loadServerMapping is not null || _loadServerMappingAsync is not null;

    public bool HasSynchronousLoader => _loadServerMapping is not null;

    /// <summary>The currently published mapping. Never null; read without locking.</summary>
    public MappingSnapshot Current => Volatile.Read(ref _snapshot);

    /// <summary>
    /// Marks the current mapping as requiring a reload that bypasses the unmapped field throttle, and supersedes
    /// any load in flight so its result cannot be published. The previous mapping stays readable until the reload
    /// completes, but it is no longer reused by revision and a failed reload discards it.
    /// </summary>
    public void Invalidate()
    {
        if (!HasServerLoader)
            return;

        LoadOperation? supersededLoad;
        lock (_stateLock)
        {
            supersededLoad = _inFlightLoad;
            _inFlightLoad = null;
            _throttleArmed = false;
            _generation++;
            Volatile.Write(ref _snapshot, Current.Invalidate(NextVersion()));
        }

        supersededLoad?.SetResult(LoadResult.Superseded);
    }

    /// <summary>
    /// Arms the unmapped field throttle without loading. Used when a lookup that just loaded or joined a load
    /// still cannot resolve its field: that load already served as the field's refresh attempt.
    /// </summary>
    public void ArmThrottle()
    {
        lock (_stateLock)
            RecordLoadAttempt();
    }

    /// <summary>
    /// Loads the mapping synchronously. A snapshot that requires a load is (re)loaded; a loaded snapshot is
    /// reloaded because a field could not be resolved from it, which is rate limited by the unmapped field
    /// throttle. Requires a synchronous loader.
    /// </summary>
    public LoadResult Load(MappingSnapshot observed)
    {
        var begin = TryBegin(observed, synchronous: true, out var operation, out var immediateResult);
        switch (begin)
        {
            case BeginResult.Immediate:
                return immediateResult;
            case BeginResult.Owner:
            case BeginResult.Independent:
                Fetch(operation!);
                return MapResult(operation!.Task.GetAwaiter().GetResult(), observed);
            default:
                if (!operation!.Wait(RefreshWaitTimeout))
                    return HasNewerMapping(observed.Version) ? LoadResult.Updated : LoadResult.WaitTimedOut;

                return MapResult(operation.Task.GetAwaiter().GetResult(), observed);
        }
    }

    /// <summary>Asynchronous counterpart of <see cref="Load"/>. Cancellation stops only this caller's wait.</summary>
    public ValueTask<LoadResult> LoadAsync(MappingSnapshot observed, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<LoadResult>(cancellationToken);

        var begin = TryBegin(observed, synchronous: false, out var operation, out var immediateResult);
        if (begin == BeginResult.Immediate)
            return ValueTask.FromResult(immediateResult);

        bool isOwner = begin == BeginResult.Owner;
        if (isOwner)
            StartLoad(operation!);

        if (operation!.Task.IsCompletedSuccessfully)
            return ValueTask.FromResult(MapResult(operation.Task.Result, observed));

        return AwaitLoadAsync(operation, isOwner, observed, cancellationToken);
    }

    private async ValueTask<LoadResult> AwaitLoadAsync(LoadOperation operation, bool isOwner, MappingSnapshot observed,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = isOwner
                ? await operation.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await operation.Task.WaitAsync(RefreshWaitTimeout, cancellationToken).ConfigureAwait(false);

            return MapResult(result, observed);
        }
        catch (TimeoutException)
        {
            return HasNewerMapping(observed.Version) ? LoadResult.Updated : LoadResult.WaitTimedOut;
        }
    }

    private LoadResult MapResult(LoadResult result, MappingSnapshot observed)
    {
        if (result != LoadResult.Superseded)
            return result;

        return HasNewerMapping(observed.Version) ? LoadResult.Updated : LoadResult.Unavailable;
    }

    private BeginResult TryBegin(MappingSnapshot observed, bool synchronous, out LoadOperation? operation, out LoadResult result)
    {
        lock (_stateLock)
        {
            operation = null;
            result = LoadResult.Unavailable;

            if (_disposed || !HasServerLoader || (synchronous && !HasSynchronousLoader))
                return BeginResult.Immediate;

            if (HasNewerMapping(observed.Version))
            {
                result = LoadResult.Updated;
                return BeginResult.Immediate;
            }

            var current = Current;
            if (_inFlightLoad is not null)
            {
                if (!synchronous || !_inFlightLoad.IsAsynchronous)
                {
                    operation = _inFlightLoad;
                    return BeginResult.Join;
                }

                if (!current.RequiresLoad)
                {
                    result = LoadResult.RefreshInFlight;
                    return BeginResult.Immediate;
                }

                operation = new LoadOperation(_generation, armThrottleOnSuccess: false, isAsynchronous: false);
                return BeginResult.Independent;
            }

            if (!IsLoadAllowed())
            {
                result = LoadResult.Throttled;
                return BeginResult.Immediate;
            }

            bool isAsynchronous = !synchronous && _loadServerMappingAsync is not null;
            operation = new LoadOperation(_generation, armThrottleOnSuccess: !current.RequiresLoad, isAsynchronous);
            _inFlightLoad = operation;
            return BeginResult.Owner;
        }
    }

    private void StartLoad(LoadOperation operation)
    {
        if (!operation.IsAsynchronous)
        {
            Fetch(operation);
            return;
        }

        // An asynchronous loader can capture the owner's context before it returns its task, which would make
        // every caller joining this shared load depend on that context. Isolate that boundary.
        if (SynchronizationContext.Current is not null || TaskScheduler.Current != TaskScheduler.Default)
            _ = Task.Run(() => FetchAsync(operation));
        else
            _ = FetchAsync(operation);
    }

    private void Fetch(LoadOperation operation)
    {
        TypeMapping? mapping;
        string? revision;
        try
        {
            mapping = _loadServerMapping!();
            revision = mapping is null ? null : ServerMappingRevisionResolver?.Invoke(mapping);
        }
        catch (Exception ex)
        {
            CompleteFailedLoad(operation, ex);
            return;
        }

        FinalizeLoad(operation, mapping, revision);
    }

    private async Task FetchAsync(LoadOperation operation)
    {
        TypeMapping? mapping;
        string? revision;
        try
        {
            mapping = await _loadServerMappingAsync!(_lifetime.Token).ConfigureAwait(false);
            revision = mapping is null ? null : ServerMappingRevisionResolver?.Invoke(mapping);
        }
        catch (Exception ex)
        {
            CompleteFailedLoad(operation, ex);
            return;
        }

        FinalizeLoad(operation, mapping, revision);
    }

    private void CompleteFailedLoad(LoadOperation operation, Exception exception)
    {
        if (exception is OutOfMemoryException)
        {
            lock (_stateLock)
            {
                operation.SetException(exception);
                if (ReferenceEquals(_inFlightLoad, operation))
                    _inFlightLoad = null;
            }

            return;
        }

        FinalizeLoad(operation, null, null);
        if (exception is not OperationCanceledException || !_lifetime.IsCancellationRequested)
            _logger.LogError(exception, "Error getting server mapping: {Message}", exception.Message);
    }

    private void FinalizeLoad(LoadOperation operation, TypeMapping? mapping, string? revision)
    {
        LoadResult result;

        lock (_stateLock)
        {
            var current = Current;
            if (_disposed || operation.Generation != _generation)
            {
                result = LoadResult.Superseded;
            }
            else if (mapping is null)
            {
                // A failed reload of a loaded mapping keeps the last known good one. A failed (re)load of a mapping
                // that must be reloaded, such as after an explicit refresh, falls back to the code mapping unless
                // another load that could still succeed is in flight.
                RecordLoadAttempt();
                if (current.RequiresLoad && (_inFlightLoad is null || ReferenceEquals(_inFlightLoad, operation)))
                    Volatile.Write(ref _snapshot, new MappingSnapshot(NextVersion(), SnapshotKind.Failed, hasServerMapping: false, _merge, null, CurrentUtc(), null));

                result = LoadResult.Unavailable;
            }
            else
            {
                Volatile.Write(ref _snapshot, CreateLoadedSnapshot(current, mapping, revision));
                if (operation.ArmThrottleOnSuccess)
                    RecordLoadAttempt();

                result = LoadResult.Updated;
            }

            operation.SetResult(result);
            if (ReferenceEquals(_inFlightLoad, operation))
                _inFlightLoad = null;
        }

        if (result == LoadResult.Updated)
            _logger.LogDebug("Loaded server mapping");
    }

    private MappingSnapshot CreateLoadedSnapshot(MappingSnapshot current, TypeMapping mapping, string? revision)
    {
        long version = NextVersion();
        if (!string.IsNullOrEmpty(revision) && current.IsLoaded && string.Equals(revision, current.ServerRevision, StringComparison.Ordinal))
            return current.WithVersion(version, CurrentUtc());

        return new MappingSnapshot(version, SnapshotKind.Loaded, hasServerMapping: true, _merge, mapping, CurrentUtc(), revision);
    }

    private bool HasNewerMapping(long version)
    {
        var snapshot = Current;
        return snapshot.Version != version && snapshot.IsLoaded;
    }

    private bool IsLoadAllowed() => !_throttleArmed || _timeProvider.GetElapsedTime(_lastLoadAttemptTimestamp) >= UnmappedFieldRefreshInterval;

    private void RecordLoadAttempt()
    {
        _lastLoadAttemptTimestamp = _timeProvider.GetTimestamp();
        _throttleArmed = true;
    }

    private long NextVersion() => Interlocked.Increment(ref _snapshotVersion);

    private DateTime CurrentUtc() => _timeProvider.GetUtcNow().UtcDateTime;

    public void Dispose()
    {
        lock (_stateLock)
            _disposed = true;

        _lifetime.Cancel();
    }

    private enum BeginResult
    {
        Immediate,
        Owner,
        Join,
        Independent
    }

    private sealed class LoadOperation(long generation, bool armThrottleOnSuccess, bool isAsynchronous)
    {
        private readonly TaskCompletionSource<LoadResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The invalidation generation the load started in; an explicit refresh supersedes it.</summary>
        public long Generation { get; } = generation;

        public bool ArmThrottleOnSuccess { get; } = armThrottleOnSuccess;

        /// <summary>Whether the load runs the asynchronous loader, which synchronous callers never wait for.</summary>
        public bool IsAsynchronous { get; } = isAsynchronous;

        public Task<LoadResult> Task => _completion.Task;

        public void SetResult(LoadResult result) => _completion.TrySetResult(result);

        public void SetException(Exception exception) => _completion.TrySetException(exception);

        /// <summary>Blocks until a synchronous load owned by another caller completes.</summary>
        public bool Wait(TimeSpan timeout)
        {
            try
            {
                return _completion.Task.Wait(timeout);
            }
            catch (AggregateException)
            {
                return true;
            }
        }
    }
}

internal enum SnapshotKind
{
    /// <summary>No load attempt has completed yet; only the code mapping is available.</summary>
    Initial,

    /// <summary>The server mapping was loaded, or the resolver has no server loader.</summary>
    Loaded,

    /// <summary>The last load attempt failed; only the code mapping is available.</summary>
    Failed
}

/// <summary>
/// A point-in-time view of the mapping, its merged property tree, and successful field resolutions made against
/// it. Publishing a new snapshot atomically invalidates everything derived from the old mapping.
/// </summary>
internal sealed class MappingSnapshot
{
    private readonly Lazy<MergedProperties?> _properties;
    private readonly ConcurrentDictionary<string, ElasticFieldMapping> _fields;

    public MappingSnapshot(long version, SnapshotKind kind, bool hasServerMapping, Func<TypeMapping?, MergedProperties?> merge,
        TypeMapping? serverMapping, DateTime createdUtc, string? serverRevision)
    {
        Version = version;
        Kind = kind;
        HasServerMapping = hasServerMapping;
        CreatedUtc = createdUtc;
        ServerRevision = serverRevision;
        _properties = new Lazy<MergedProperties?>(() => merge(serverMapping), LazyThreadSafetyMode.ExecutionAndPublication);
        _fields = new ConcurrentDictionary<string, ElasticFieldMapping>(StringComparer.Ordinal);
    }

    private MappingSnapshot(MappingSnapshot previous, long version, DateTime createdUtc, bool isInvalidated, string? serverRevision)
    {
        Version = version;
        Kind = previous.Kind;
        HasServerMapping = previous.HasServerMapping;
        CreatedUtc = createdUtc;
        IsInvalidated = isInvalidated;
        ServerRevision = serverRevision;
        _properties = previous._properties;
        _fields = previous._fields;
    }

    public long Version { get; }

    public SnapshotKind Kind { get; }

    public bool HasServerMapping { get; }

    /// <summary>Whether an explicit refresh requested a reload of this mapping.</summary>
    public bool IsInvalidated { get; }

    public DateTime CreatedUtc { get; }

    public string? ServerRevision { get; }

    public bool IsLoaded => Kind == SnapshotKind.Loaded && !IsInvalidated;

    public bool RequiresLoad => !IsLoaded;

    public MergedProperties? Properties => _properties.Value;

    public MappingSnapshot WithVersion(long version, DateTime createdUtc) => new(this, version, createdUtc, IsInvalidated, ServerRevision);

    public MappingSnapshot Invalidate(long version) => new(this, version, CreatedUtc, isInvalidated: true, serverRevision: null);

    public bool TryGetField(string field, out ElasticFieldMapping mapping) => _fields.TryGetValue(field, out mapping!);

    /// <summary>
    /// Memoizes successful resolutions by canonical path, which keeps the cache bounded by the mapping itself no
    /// matter how many distinct spellings callers ask for. Unknown names are caller controlled and stay uncached.
    /// </summary>
    public void CacheField(ElasticFieldMapping mapping)
    {
        if (mapping.Found)
            _fields.TryAdd(mapping.FullPath, mapping);
    }
}

internal enum LoadResult
{
    Unavailable,
    Throttled,
    WaitTimedOut,
    Superseded,
    RefreshInFlight,
    Updated
}
