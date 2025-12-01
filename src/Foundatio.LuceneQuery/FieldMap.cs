namespace Foundatio.LuceneQuery;

/// <summary>
/// A dictionary mapping field aliases to their resolved field names.
/// </summary>
public class FieldMap : Dictionary<string, string>
{
    /// <summary>
    /// Creates a new empty field map.
    /// </summary>
    public FieldMap() : base(StringComparer.OrdinalIgnoreCase) { }

    /// <summary>
    /// Creates a new field map with the specified mappings.
    /// </summary>
    public FieldMap(IDictionary<string, string> dictionary) : base(dictionary, StringComparer.OrdinalIgnoreCase) { }
}

/// <summary>
/// Extension methods for field maps.
/// </summary>
public static class FieldMapExtensions
{
    /// <summary>
    /// Gets the value for a key, or null if not found.
    /// </summary>
    public static string? GetValueOrNull(this IDictionary<string, string> map, string? field)
    {
        if (map is null || field is null)
            return null;

        return map.TryGetValue(field, out var value) ? value : null;
    }

    /// <summary>
    /// Converts a field map to a hierarchical field resolver.
    /// This resolver handles nested field paths like "data.field" by finding
    /// the longest matching prefix in the map and appending the remaining path.
    /// </summary>
    /// <param name="map">The field map to convert.</param>
    /// <param name="resultPrefix">Optional prefix to add to all resolved field names.</param>
    /// <returns>A QueryFieldResolver that resolves fields hierarchically.</returns>
    /// <remarks>
    /// For example, if the map contains {"data": "resolved"}, then:
    /// - "data" resolves to "resolved"
    /// - "data.subfield" resolves to "resolved.subfield"
    /// - "data.nested.field" resolves to "resolved.nested.field"
    /// </remarks>
    public static QueryFieldResolver ToHierarchicalFieldResolver(this IDictionary<string, string> map, string? resultPrefix = null)
    {
        return (field, _) =>
        {
            if (field is null)
                return Task.FromResult<string?>(null);

            // Direct match
            if (map.TryGetValue(field, out var result))
                return Task.FromResult<string?>($"{resultPrefix}{result}");

            // Start at the longest path and go backwards until we find a match
            int currentPart = field.LastIndexOf('.');
            while (currentPart > 0)
            {
                string currentName = field[..currentPart];
                if (map.TryGetValue(currentName, out var currentResult))
                    return Task.FromResult<string?>($"{resultPrefix}{currentResult}{field[currentPart..]}");

                currentPart = field.LastIndexOf('.', currentPart - 1);
            }

            // No match found, return the original field
            return Task.FromResult<string?>(field);
        };
    }

    /// <summary>
    /// Converts a field map to a simple field resolver that only does direct lookups.
    /// Returns null for fields not in the map (which will be added to UnresolvedFields).
    /// </summary>
    /// <param name="map">The field map to convert.</param>
    /// <returns>A QueryFieldResolver that resolves fields by direct lookup.</returns>
    public static QueryFieldResolver ToFieldResolver(this IDictionary<string, string> map)
    {
        return (field, _) =>
        {
            if (field is null)
                return Task.FromResult<string?>(null);

            return Task.FromResult(map.TryGetValue(field, out var result) ? result : null);
        };
    }
}
