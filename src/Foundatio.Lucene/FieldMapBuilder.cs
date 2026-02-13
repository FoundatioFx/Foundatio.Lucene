namespace Foundatio.Lucene;

/// <summary>
/// Fluent builder for creating and configuring field maps.
/// </summary>
public class FieldMapBuilder
{
    private readonly FieldMap _fieldMap = new();

    /// <summary>
    /// Creates a new field map builder.
    /// </summary>
    public static FieldMapBuilder Create() => new();

    /// <summary>
    /// Creates a new field map builder starting with an existing field map.
    /// </summary>
    public static FieldMapBuilder From(FieldMap fieldMap)
    {
        var builder = new FieldMapBuilder();
        foreach (var kvp in fieldMap)
        {
            builder._fieldMap[kvp.Key] = kvp.Value;
        }
        builder._fieldMap.ResolutionMode = fieldMap.ResolutionMode;
        builder._fieldMap.ReportUnmappedFields = fieldMap.ReportUnmappedFields;
        builder._fieldMap.ResultPrefix = fieldMap.ResultPrefix;
        return builder;
    }

    /// <summary>
    /// Maps an alias to a target field name.
    /// </summary>
    /// <param name="alias">The field alias used in queries.</param>
    /// <param name="target">The actual field name to map to.</param>
    public FieldMapBuilder Map(string alias, string target)
    {
        _fieldMap[alias] = target;
        return this;
    }

    /// <summary>
    /// Maps multiple aliases to the same target field.
    /// </summary>
    /// <param name="target">The actual field name to map to.</param>
    /// <param name="aliases">The field aliases used in queries.</param>
    public FieldMapBuilder MapMany(string target, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            _fieldMap[alias] = target;
        }
        return this;
    }

    /// <summary>
    /// Maps aliases from a dictionary.
    /// </summary>
    public FieldMapBuilder MapFrom(IDictionary<string, string> mappings)
    {
        foreach (var kvp in mappings)
        {
            _fieldMap[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Maps a namespace prefix to a target prefix.
    /// Useful for mapping all fields under a common prefix.
    /// </summary>
    /// <param name="sourcePrefix">The source namespace prefix (e.g., "user").</param>
    /// <param name="targetPrefix">The target namespace prefix (e.g., "document.author").</param>
    public FieldMapBuilder MapNamespace(string sourcePrefix, string targetPrefix)
    {
        _fieldMap[sourcePrefix] = targetPrefix;
        return this;
    }

    /// <summary>
    /// Sets the resolution mode for the field map.
    /// </summary>
    /// <param name="mode">The resolution mode to use.</param>
    public FieldMapBuilder WithResolutionMode(FieldResolutionMode mode)
    {
        _fieldMap.ResolutionMode = mode;
        return this;
    }

    /// <summary>
    /// Enables hierarchical resolution mode.
    /// Nested paths are resolved by finding the longest matching prefix.
    /// </summary>
    public FieldMapBuilder UseHierarchicalResolution()
    {
        _fieldMap.ResolutionMode = FieldResolutionMode.Hierarchical;
        return this;
    }

    /// <summary>
    /// Enables direct resolution mode.
    /// Only exact matches are resolved; unmapped fields pass through or return null.
    /// </summary>
    public FieldMapBuilder UseDirectResolution()
    {
        _fieldMap.ResolutionMode = FieldResolutionMode.Direct;
        return this;
    }

    /// <summary>
    /// Configures unmapped fields to be reported as unresolved.
    /// When enabled, unmapped fields are added to UnresolvedFields in validation.
    /// </summary>
    public FieldMapBuilder ReportUnmapped()
    {
        _fieldMap.ReportUnmappedFields = true;
        return this;
    }

    /// <summary>
    /// Configures unmapped fields to pass through unchanged (default behavior).
    /// </summary>
    public FieldMapBuilder AllowUnmapped()
    {
        _fieldMap.ReportUnmappedFields = false;
        return this;
    }

    /// <summary>
    /// Sets a prefix to add to all resolved field names.
    /// </summary>
    /// <param name="prefix">The prefix to add to resolved field names.</param>
    public FieldMapBuilder WithResultPrefix(string prefix)
    {
        _fieldMap.ResultPrefix = prefix;
        return this;
    }

    /// <summary>
    /// Clears any previously set result prefix.
    /// </summary>
    public FieldMapBuilder WithoutResultPrefix()
    {
        _fieldMap.ResultPrefix = null;
        return this;
    }

    /// <summary>
    /// Builds the configured field map.
    /// </summary>
    public FieldMap Build()
    {
        // Return a copy to prevent mutation after building
        var result = new FieldMap
        {
            ResolutionMode = _fieldMap.ResolutionMode,
            ReportUnmappedFields = _fieldMap.ReportUnmappedFields,
            ResultPrefix = _fieldMap.ResultPrefix
        };
        foreach (var kvp in _fieldMap)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }
}

/// <summary>
/// Extension methods for field map building.
/// </summary>
public static class FieldMapBuilderExtensions
{
    /// <summary>
    /// Creates a builder from this field map for further configuration.
    /// </summary>
    public static FieldMapBuilder ToBuilder(this FieldMap fieldMap)
    {
        return FieldMapBuilder.From(fieldMap);
    }
}
