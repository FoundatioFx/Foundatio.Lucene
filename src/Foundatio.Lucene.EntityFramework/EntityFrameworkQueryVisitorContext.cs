using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Interface for Entity Framework specific query visitor context.
/// </summary>
public interface IEntityFrameworkQueryVisitorContext : IQueryVisitorContext
{
    /// <summary>
    /// The list of fields discovered from the entity type.
    /// </summary>
    List<EntityFieldInfo> Fields { get; set; }

    /// <summary>
    /// The EF entity type being queried.
    /// </summary>
    IEntityType? EntityType { get; set; }

    /// <summary>
    /// The default fields to search when no field is specified.
    /// </summary>
    string[]? DefaultFields { get; set; }

    /// <summary>
    /// Function to parse date/time strings into comparable values.
    /// </summary>
    Func<string, object?>? DateTimeParser { get; set; }

    /// <summary>
    /// Function to parse date-only strings into comparable values.
    /// </summary>
    Func<string, object?>? DateOnlyParser { get; set; }

    /// <summary>
    /// Gets a field info by name (case-insensitive).
    /// </summary>
    EntityFieldInfo? GetField(string fieldName);
}

/// <summary>
/// Entity Framework specific query visitor context implementation.
/// </summary>
public class EntityFrameworkQueryVisitorContext : QueryVisitorContext, IEntityFrameworkQueryVisitorContext
{
    /// <inheritdoc />
    public List<EntityFieldInfo> Fields { get; set; } = [];

    /// <inheritdoc />
    public IEntityType? EntityType { get; set; }

    /// <inheritdoc />
    public string[]? DefaultFields { get; set; }

    /// <inheritdoc />
    public Func<string, object?>? DateTimeParser { get; set; } = DefaultDateTimeParser;

    /// <inheritdoc />
    public Func<string, object?>? DateOnlyParser { get; set; } = DefaultDateOnlyParser;

    /// <summary>
    /// Default DateTime parser implementation.
    /// </summary>
    public static object? DefaultDateTimeParser(string value)
    {
        if (string.Equals(value, "now", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow;

        if (DateTime.TryParse(value, out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Default DateOnly parser implementation.
    /// </summary>
    public static object? DefaultDateOnlyParser(string value)
    {
        if (string.Equals(value, "now", StringComparison.OrdinalIgnoreCase))
            return DateOnly.FromDateTime(DateTime.UtcNow);

        if (DateOnly.TryParse(value, out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Gets a field info by name (case-insensitive).
    /// </summary>
    public EntityFieldInfo? GetField(string fieldName)
    {
        return Fields.FirstOrDefault(f => 
            string.Equals(f.FullName, fieldName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
    }
}
