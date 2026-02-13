namespace Foundatio.Lucene;

/// <summary>
/// A dictionary mapping field aliases to their resolved field names.
/// Supports both direct lookups and hierarchical resolution for nested field paths.
/// </summary>
public class FieldMap : Dictionary<string, string>
{
    /// <summary>
    /// Gets or sets the resolution mode. Default is Hierarchical.
    /// </summary>
    public FieldResolutionMode ResolutionMode { get; set; } = FieldResolutionMode.Hierarchical;

    /// <summary>
    /// Gets or sets whether unmapped fields should be reported as unresolved.
    /// When false (default), unmapped fields pass through unchanged.
    /// When true, unmapped fields are added to UnresolvedFields in validation.
    /// </summary>
    public bool ReportUnmappedFields { get; set; }

    /// <summary>
    /// Optional prefix to add to all resolved field names.
    /// </summary>
    public string? ResultPrefix { get; set; }

    /// <summary>
    /// Creates a new empty field map.
    /// </summary>
    public FieldMap() : base(StringComparer.OrdinalIgnoreCase) { }

    /// <summary>
    /// Creates a new field map with the specified mappings.
    /// </summary>
    public FieldMap(IDictionary<string, string> dictionary) : base(dictionary, StringComparer.OrdinalIgnoreCase) { }

    /// <summary>
    /// Resolves a field name using this field map.
    /// Returns the resolved field name, or null if not resolved (when ReportUnmappedFields is true).
    /// Returns the original field if not mapped and ReportUnmappedFields is false.
    /// </summary>
    /// <param name="field">The field name to resolve.</param>
    /// <returns>The resolved field name.</returns>
    public string? ResolveField(string? field)
    {
        if (field is null)
            return null;

        // Direct match
        if (TryGetValue(field, out var result))
            return $"{ResultPrefix}{result}";

        // If hierarchical resolution is enabled, try prefix matching
        if (ResolutionMode == FieldResolutionMode.Hierarchical)
        {
            // Start at the longest path and go backwards until we find a match
            int currentPart = field.LastIndexOf('.');
            while (currentPart > 0)
            {
                string currentName = field[..currentPart];
                if (TryGetValue(currentName, out var currentResult))
                    return $"{ResultPrefix}{currentResult}{field[currentPart..]}";

                currentPart = field.LastIndexOf('.', currentPart - 1);
            }
        }

        // No match found
        if (ReportUnmappedFields)
            return null; // Will be added to UnresolvedFields

        // Pass through unchanged
        return field;
    }

    /// <summary>
    /// Adds a field mapping and returns this instance for fluent chaining.
    /// </summary>
    public new FieldMap Add(string alias, string target)
    {
        this[alias] = target;
        return this;
    }
}

/// <summary>
/// Specifies how field names are resolved.
/// </summary>
public enum FieldResolutionMode
{
    /// <summary>
    /// Only exact matches are resolved. Unmapped fields pass through or return null.
    /// </summary>
    Direct,

    /// <summary>
    /// Nested paths are resolved by finding the longest matching prefix.
    /// For example, if "data" maps to "resolved", then "data.subfield" becomes "resolved.subfield".
    /// </summary>
    Hierarchical
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
}
