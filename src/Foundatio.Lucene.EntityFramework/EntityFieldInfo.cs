using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Represents metadata about an entity field discovered from Entity Framework.
/// </summary>
[DebuggerDisplay("{FullName} IsNumber: {IsNumber} IsDate: {IsDate} IsBoolean: {IsBoolean} IsCollection: {IsCollection} IsNavigation: {IsNavigation}")]
public class EntityFieldInfo
{
    /// <summary>
    /// The simple name of the field.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full path name of the field (e.g., "Company.Address.City").
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// The CLR type of the field.
    /// </summary>
    public Type? ClrType { get; set; }

    /// <summary>
    /// Whether the field is a numeric type.
    /// </summary>
    public bool IsNumber { get; set; }

    /// <summary>
    /// Whether the field is a money/decimal type with specific precision.
    /// </summary>
    public bool IsMoney { get; set; }

    /// <summary>
    /// Whether the field is a DateTime type.
    /// </summary>
    public bool IsDate { get; set; }

    /// <summary>
    /// Whether the field is a DateOnly type.
    /// </summary>
    public bool IsDateOnly { get; set; }

    /// <summary>
    /// Whether the field is a boolean type.
    /// </summary>
    public bool IsBoolean { get; set; }

    /// <summary>
    /// Whether the field is a string type.
    /// </summary>
    public bool IsString { get; set; }

    /// <summary>
    /// Whether the field is a collection (e.g., ICollection&lt;T&gt;).
    /// </summary>
    public bool IsCollection { get; set; }

    /// <summary>
    /// Whether the field is a navigation property.
    /// </summary>
    public bool IsNavigation { get; set; }

    /// <summary>
    /// Whether the field has a full-text search index.
    /// When true, queries will use EF.Functions.Contains() for full-text search.
    /// </summary>
    public bool IsFullTextIndexed { get; set; }

    /// <summary>
    /// The name of the CLR type that declares this field (e.g., "Employee" for Employee.Name).
    /// Used for full-text field configuration matching.
    /// </summary>
    public string? DeclaringTypeName { get; set; }

    /// <summary>
    /// The parent field info for nested fields.
    /// </summary>
    public EntityFieldInfo? Parent { get; set; }

    /// <summary>
    /// Additional custom data associated with this field.
    /// </summary>
    public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// The EF property metadata if this is a scalar property.
    /// </summary>
    public IProperty? Property { get; set; }

    /// <summary>
    /// The EF navigation metadata if this is a navigation property.
    /// </summary>
    public INavigationBase? Navigation { get; set; }

    /// <summary>
    /// Gets the navigation prefix for building expressions with nested collections.
    /// </summary>
    public string GetNavigationPrefix()
    {
        if (!IsNavigation || Parent is null)
            return string.Empty;

        var parts = new List<string>();
        var current = this;
        while (current != null && current.IsNavigation)
        {
            parts.Insert(0, current.Name);
            current = current.Parent;
        }

        return parts.Count > 0 ? string.Join(".", parts) + "." : string.Empty;
    }

    /// <inheritdoc />
    protected bool Equals(EntityFieldInfo other) => Name == other.Name;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((EntityFieldInfo)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode();
}
