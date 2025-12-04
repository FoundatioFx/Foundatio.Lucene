namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Provides context that is passed between visitors during query traversal.
/// Allows visitors to share state and communicate with each other.
/// </summary>
public class QueryVisitorContext : IQueryVisitorContext
{
    /// <summary>
    /// A dictionary for storing arbitrary data that visitors can use to share state.
    /// </summary>
    public IDictionary<string, object?> Data { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets a value from the context data.
    /// </summary>
    /// <typeparam name="T">The type of value to retrieve.</typeparam>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value if found and of the correct type; otherwise, default.</returns>
    public T? GetValue<T>(string key)
    {
        if (Data.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    /// <summary>
    /// Sets a value in the context data.
    /// </summary>
    /// <param name="key">The key to store the value under.</param>
    /// <param name="value">The value to store.</param>
    public void SetValue(string key, object? value)
    {
        Data[key] = value;
    }

    /// <summary>
    /// Gets or creates a list of items in the context data.
    /// </summary>
    /// <typeparam name="T">The type of items in the list.</typeparam>
    /// <param name="key">The key for the list.</param>
    /// <returns>The existing or newly created list.</returns>
    public IList<T> GetOrCreateList<T>(string key)
    {
        if (Data.TryGetValue(key, out var value) && value is IList<T> list)
            return list;
        
        var newList = new List<T>();
        Data[key] = newList;
        return newList;
    }
}

/// <summary>
/// Interface for query visitor context.
/// </summary>
public interface IQueryVisitorContext
{
    /// <summary>
    /// A dictionary for storing arbitrary data that visitors can use to share state.
    /// </summary>
    IDictionary<string, object?> Data { get; }

    /// <summary>
    /// Gets a value from the context data.
    /// </summary>
    T? GetValue<T>(string key);

    /// <summary>
    /// Sets a value in the context data.
    /// </summary>
    void SetValue(string key, object? value);

    /// <summary>
    /// Gets or creates a list of items in the context data.
    /// </summary>
    IList<T> GetOrCreateList<T>(string key);
}
