using System.Collections.Concurrent;

namespace Foundatio.Lucene;

/// <summary>
/// Thread-safe cache for per-tenant query options.
/// Optimizes multi-tenant scenarios by caching pre-configured options per tenant.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <typeparam name="TOptions">The options type (must inherit from QueryOptionsBase).</typeparam>
public class TenantOptionsCache<TKey, TOptions>
    where TKey : notnull
    where TOptions : QueryOptionsBase
{
    private readonly ConcurrentDictionary<TKey, TOptions> _cache = new();
    private readonly Func<TKey, TOptions> _optionsFactory;

    /// <summary>
    /// Creates a new tenant options cache with the specified factory function.
    /// </summary>
    /// <param name="optionsFactory">Factory function that creates options for a given tenant.</param>
    public TenantOptionsCache(Func<TKey, TOptions> optionsFactory)
    {
        _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
    }

    /// <summary>
    /// Gets or creates options for the specified tenant.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>The cached or newly created options for the tenant.</returns>
    public TOptions GetOptions(TKey tenantKey)
    {
        return _cache.GetOrAdd(tenantKey, _optionsFactory);
    }

    /// <summary>
    /// Tries to get cached options for the specified tenant.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="options">The cached options if found.</param>
    /// <returns>True if options were found in the cache.</returns>
    public bool TryGetOptions(TKey tenantKey, out TOptions? options)
    {
        return _cache.TryGetValue(tenantKey, out options);
    }

    /// <summary>
    /// Sets options for the specified tenant, replacing any existing cached options.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="options">The options to cache.</param>
    public void SetOptions(TKey tenantKey, TOptions options)
    {
        _cache[tenantKey] = options;
    }

    /// <summary>
    /// Removes cached options for the specified tenant.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions(TKey tenantKey)
    {
        return _cache.TryRemove(tenantKey, out _);
    }

    /// <summary>
    /// Clears all cached options.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of cached tenant options.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets all cached tenant keys.
    /// </summary>
    public IEnumerable<TKey> CachedTenants => _cache.Keys;

    /// <summary>
    /// Refreshes options for the specified tenant by invoking the factory function.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>The refreshed options.</returns>
    public TOptions RefreshOptions(TKey tenantKey)
    {
        var newOptions = _optionsFactory(tenantKey);
        _cache[tenantKey] = newOptions;
        return newOptions;
    }

    /// <summary>
    /// Refreshes options for all cached tenants by invoking the factory function.
    /// </summary>
    public void RefreshAll()
    {
        foreach (var key in _cache.Keys.ToArray())
        {
            var newOptions = _optionsFactory(key);
            _cache[key] = newOptions;
        }
    }
}

/// <summary>
/// Thread-safe cache for query options keyed by entity type.
/// Useful when different entity types require different query configurations.
/// </summary>
/// <typeparam name="TOptions">The options type (must inherit from QueryOptionsBase).</typeparam>
public class EntityOptionsCache<TOptions>
    where TOptions : QueryOptionsBase
{
    private readonly ConcurrentDictionary<Type, TOptions> _cache = new();
    private readonly Func<Type, TOptions> _optionsFactory;

    /// <summary>
    /// Creates a new entity options cache with the specified factory function.
    /// </summary>
    /// <param name="optionsFactory">Factory function that creates options for a given entity type.</param>
    public EntityOptionsCache(Func<Type, TOptions> optionsFactory)
    {
        _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
    }

    /// <summary>
    /// Gets or creates options for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>The cached or newly created options for the entity type.</returns>
    public TOptions GetOptions<TEntity>()
    {
        return GetOptions(typeof(TEntity));
    }

    /// <summary>
    /// Gets or creates options for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The cached or newly created options for the entity type.</returns>
    public TOptions GetOptions(Type entityType)
    {
        return _cache.GetOrAdd(entityType, _optionsFactory);
    }

    /// <summary>
    /// Sets options for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="options">The options to cache.</param>
    public void SetOptions<TEntity>(TOptions options)
    {
        SetOptions(typeof(TEntity), options);
    }

    /// <summary>
    /// Sets options for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="options">The options to cache.</param>
    public void SetOptions(Type entityType, TOptions options)
    {
        _cache[entityType] = options;
    }

    /// <summary>
    /// Removes cached options for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions<TEntity>()
    {
        return RemoveOptions(typeof(TEntity));
    }

    /// <summary>
    /// Removes cached options for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions(Type entityType)
    {
        return _cache.TryRemove(entityType, out _);
    }

    /// <summary>
    /// Clears all cached options.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of cached entity type options.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Refreshes options for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>The refreshed options.</returns>
    public TOptions RefreshOptions<TEntity>()
    {
        return RefreshOptions(typeof(TEntity));
    }

    /// <summary>
    /// Refreshes options for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The refreshed options.</returns>
    public TOptions RefreshOptions(Type entityType)
    {
        var newOptions = _optionsFactory(entityType);
        _cache[entityType] = newOptions;
        return newOptions;
    }
}

/// <summary>
/// Thread-safe cache for query options keyed by both tenant and entity type.
/// Supports scenarios where each tenant may have different configurations per entity type.
/// </summary>
/// <typeparam name="TTenantKey">The tenant identifier type.</typeparam>
/// <typeparam name="TOptions">The options type (must inherit from QueryOptionsBase).</typeparam>
public class TenantEntityOptionsCache<TTenantKey, TOptions>
    where TTenantKey : notnull
    where TOptions : QueryOptionsBase
{
    private readonly ConcurrentDictionary<(TTenantKey, Type), TOptions> _cache = new();
    private readonly Func<TTenantKey, Type, TOptions> _optionsFactory;

    /// <summary>
    /// Creates a new tenant-entity options cache with the specified factory function.
    /// </summary>
    /// <param name="optionsFactory">Factory function that creates options for a given tenant and entity type.</param>
    public TenantEntityOptionsCache(Func<TTenantKey, Type, TOptions> optionsFactory)
    {
        _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
    }

    /// <summary>
    /// Gets or creates options for the specified tenant and entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>The cached or newly created options.</returns>
    public TOptions GetOptions<TEntity>(TTenantKey tenantKey)
    {
        return GetOptions(tenantKey, typeof(TEntity));
    }

    /// <summary>
    /// Gets or creates options for the specified tenant and entity type.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The cached or newly created options.</returns>
    public TOptions GetOptions(TTenantKey tenantKey, Type entityType)
    {
        return _cache.GetOrAdd((tenantKey, entityType), key => _optionsFactory(key.Item1, key.Item2));
    }

    /// <summary>
    /// Tries to get cached options for the specified tenant and entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="options">The cached options if found.</param>
    /// <returns>True if options were found in the cache.</returns>
    public bool TryGetOptions<TEntity>(TTenantKey tenantKey, out TOptions? options)
    {
        return TryGetOptions(tenantKey, typeof(TEntity), out options);
    }

    /// <summary>
    /// Tries to get cached options for the specified tenant and entity type.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="options">The cached options if found.</param>
    /// <returns>True if options were found in the cache.</returns>
    public bool TryGetOptions(TTenantKey tenantKey, Type entityType, out TOptions? options)
    {
        return _cache.TryGetValue((tenantKey, entityType), out options);
    }

    /// <summary>
    /// Sets options for the specified tenant and entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="options">The options to cache.</param>
    public void SetOptions<TEntity>(TTenantKey tenantKey, TOptions options)
    {
        SetOptions(tenantKey, typeof(TEntity), options);
    }

    /// <summary>
    /// Sets options for the specified tenant and entity type.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="options">The options to cache.</param>
    public void SetOptions(TTenantKey tenantKey, Type entityType, TOptions options)
    {
        _cache[(tenantKey, entityType)] = options;
    }

    /// <summary>
    /// Removes cached options for the specified tenant and entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions<TEntity>(TTenantKey tenantKey)
    {
        return RemoveOptions(tenantKey, typeof(TEntity));
    }

    /// <summary>
    /// Removes cached options for the specified tenant and entity type.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="entityType">The entity type.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions(TTenantKey tenantKey, Type entityType)
    {
        return _cache.TryRemove((tenantKey, entityType), out _);
    }

    /// <summary>
    /// Removes all cached options for the specified tenant.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>The number of options removed.</returns>
    public int RemoveTenant(TTenantKey tenantKey)
    {
        var keysToRemove = _cache.Keys.Where(k => EqualityComparer<TTenantKey>.Default.Equals(k.Item1, tenantKey)).ToList();
        int removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out _))
                removed++;
        }
        return removed;
    }

    /// <summary>
    /// Removes all cached options for the specified entity type across all tenants.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>The number of options removed.</returns>
    public int RemoveEntityType<TEntity>()
    {
        return RemoveEntityType(typeof(TEntity));
    }

    /// <summary>
    /// Removes all cached options for the specified entity type across all tenants.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The number of options removed.</returns>
    public int RemoveEntityType(Type entityType)
    {
        var keysToRemove = _cache.Keys.Where(k => k.Item2 == entityType).ToList();
        int removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out _))
                removed++;
        }
        return removed;
    }

    /// <summary>
    /// Clears all cached options.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of cached options.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets all cached tenant keys.
    /// </summary>
    public IEnumerable<TTenantKey> CachedTenants => _cache.Keys.Select(k => k.Item1).Distinct();

    /// <summary>
    /// Gets all cached entity types.
    /// </summary>
    public IEnumerable<Type> CachedEntityTypes => _cache.Keys.Select(k => k.Item2).Distinct();

    /// <summary>
    /// Refreshes options for the specified tenant and entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <returns>The refreshed options.</returns>
    public TOptions RefreshOptions<TEntity>(TTenantKey tenantKey)
    {
        return RefreshOptions(tenantKey, typeof(TEntity));
    }

    /// <summary>
    /// Refreshes options for the specified tenant and entity type.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The refreshed options.</returns>
    public TOptions RefreshOptions(TTenantKey tenantKey, Type entityType)
    {
        var newOptions = _optionsFactory(tenantKey, entityType);
        _cache[(tenantKey, entityType)] = newOptions;
        return newOptions;
    }

    /// <summary>
    /// Refreshes all options for the specified tenant.
    /// </summary>
    /// <param name="tenantKey">The tenant identifier.</param>
    public void RefreshTenant(TTenantKey tenantKey)
    {
        var keysToRefresh = _cache.Keys.Where(k => EqualityComparer<TTenantKey>.Default.Equals(k.Item1, tenantKey)).ToList();
        foreach (var key in keysToRefresh)
        {
            var newOptions = _optionsFactory(key.Item1, key.Item2);
            _cache[key] = newOptions;
        }
    }
}

/// <summary>
/// Builder for creating tenant options caches with fluent configuration.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <typeparam name="TOptions">The options type (must inherit from QueryOptionsBase).</typeparam>
public class TenantOptionsCacheBuilder<TKey, TOptions>
    where TKey : notnull
    where TOptions : QueryOptionsBase
{
    private Func<TKey, TOptions>? _optionsFactory;
    private readonly Dictionary<TKey, TOptions> _preloadedOptions = new();

    /// <summary>
    /// Sets the factory function for creating tenant options.
    /// </summary>
    public TenantOptionsCacheBuilder<TKey, TOptions> WithFactory(Func<TKey, TOptions> factory)
    {
        _optionsFactory = factory;
        return this;
    }

    /// <summary>
    /// Preloads options for a specific tenant.
    /// </summary>
    public TenantOptionsCacheBuilder<TKey, TOptions> WithTenant(TKey tenantKey, TOptions options)
    {
        _preloadedOptions[tenantKey] = options;
        return this;
    }

    /// <summary>
    /// Preloads options for a specific tenant using a configuration action.
    /// </summary>
    public TenantOptionsCacheBuilder<TKey, TOptions> WithTenant(TKey tenantKey, Func<TOptions> optionsFactory)
    {
        _preloadedOptions[tenantKey] = optionsFactory();
        return this;
    }

    /// <summary>
    /// Builds the tenant options cache.
    /// </summary>
    public TenantOptionsCache<TKey, TOptions> Build()
    {
        if (_optionsFactory is null)
            throw new InvalidOperationException("Options factory must be configured using WithFactory().");

        var cache = new TenantOptionsCache<TKey, TOptions>(_optionsFactory);

        // Preload any configured tenant options
        foreach (var kvp in _preloadedOptions)
        {
            cache.SetOptions(kvp.Key, kvp.Value);
        }

        return cache;
    }
}

/// <summary>
/// Static helper methods for creating options caches.
/// </summary>
public static class TenantOptionsCache
{
    /// <summary>
    /// Creates a new tenant options cache builder.
    /// </summary>
    public static TenantOptionsCacheBuilder<TKey, TOptions> Create<TKey, TOptions>()
        where TKey : notnull
        where TOptions : QueryOptionsBase
    {
        return new TenantOptionsCacheBuilder<TKey, TOptions>();
    }

    /// <summary>
    /// Creates a new tenant options cache with the specified factory.
    /// </summary>
    public static TenantOptionsCache<TKey, TOptions> Create<TKey, TOptions>(Func<TKey, TOptions> optionsFactory)
        where TKey : notnull
        where TOptions : QueryOptionsBase
    {
        return new TenantOptionsCache<TKey, TOptions>(optionsFactory);
    }

    /// <summary>
    /// Creates a new entity options cache with the specified factory.
    /// </summary>
    public static EntityOptionsCache<TOptions> CreateByEntityType<TOptions>(Func<Type, TOptions> optionsFactory)
        where TOptions : QueryOptionsBase
    {
        return new EntityOptionsCache<TOptions>(optionsFactory);
    }

    /// <summary>
    /// Creates a new tenant-entity options cache with the specified factory.
    /// </summary>
    public static TenantEntityOptionsCache<TTenantKey, TOptions> CreateByTenantAndEntityType<TTenantKey, TOptions>(
        Func<TTenantKey, Type, TOptions> optionsFactory)
        where TTenantKey : notnull
        where TOptions : QueryOptionsBase
    {
        return new TenantEntityOptionsCache<TTenantKey, TOptions>(optionsFactory);
    }
}
