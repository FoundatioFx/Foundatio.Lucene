using System.Collections.Concurrent;

namespace Foundatio.Lucene;

/// <summary>
/// Simple object pool for reusing visitor instances across requests.
/// </summary>
public sealed class VisitorPool<T> where T : class, new()
{
    private readonly ConcurrentBag<T> _pool = new();
    private readonly int _maxPoolSize;

    /// <summary>
    /// Creates a new visitor pool with the specified maximum size.
    /// </summary>
    public VisitorPool(int maxPoolSize = 64)
    {
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Rents a visitor instance from the pool or creates a new one.
    /// </summary>
    public T Rent()
    {
        return _pool.TryTake(out var visitor) ? visitor : new T();
    }

    /// <summary>
    /// Returns a visitor instance to the pool for reuse.
    /// </summary>
    public void Return(T visitor)
    {
        if (_pool.Count < _maxPoolSize)
        {
            _pool.Add(visitor);
        }
    }
}
