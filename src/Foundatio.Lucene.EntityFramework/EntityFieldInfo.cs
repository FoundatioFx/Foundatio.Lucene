using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// The kind of member an <see cref="EntityFieldInfo"/> describes.
/// </summary>
public enum EntityFieldKind
{
    /// <summary>A field registered by the application (for example a dynamic or EAV field). Queries on it need a
    /// <see cref="CustomFieldExpressionBuilder"/>.</summary>
    Custom,
    /// <summary>A scalar property of the EF Core model (including primitive collections).</summary>
    Property,
    /// <summary>A reference or collection navigation (including owned types).</summary>
    Navigation,
    /// <summary>A many-to-many skip navigation.</summary>
    SkipNavigation,
    /// <summary>A complex property (EF Core complex type).</summary>
    ComplexProperty
}

/// <summary>
/// Describes a queryable field: a property or navigation discovered from the EF Core model, or a custom field
/// registered with <see cref="EntityFrameworkQueryOptions.AdditionalFields"/>. Instances are immutable and shared
/// between requests.
/// </summary>
[DebuggerDisplay("{FullName} ({Kind}, {ClrType.Name})")]
public sealed class EntityFieldInfo
{
    private readonly string? _fullName;
    private SortKey? _sortKey;

    /// <summary>
    /// The member name (for a nested field, the last path segment).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The dotted path from the queried entity, for example <c>Company.Address.City</c>. Defaults to <see cref="Name"/>.
    /// </summary>
    public string FullName
    {
        get => _fullName ?? Name;
        init => _fullName = value;
    }

    /// <summary>
    /// The CLR type of the member. For collections this is the collection type; see <see cref="ElementType"/>.
    /// </summary>
    public required Type ClrType { get; init; }

    /// <summary>
    /// The element type of a collection navigation or primitive collection, otherwise null.
    /// </summary>
    public Type? ElementType { get; init; }

    /// <summary>
    /// What the field describes. Defaults to <see cref="EntityFieldKind.Custom"/> for fields created by the application.
    /// </summary>
    public EntityFieldKind Kind { get; init; } = EntityFieldKind.Custom;

    /// <summary>
    /// Whether the member is a collection (a collection navigation or a primitive collection).
    /// </summary>
    public bool IsCollection => ElementType is not null;

    /// <summary>
    /// Whether the member is a navigation or skip navigation.
    /// </summary>
    public bool IsNavigation => Kind is EntityFieldKind.Navigation or EntityFieldKind.SkipNavigation;

    /// <summary>
    /// Whether the field has a full-text index, so terms are matched with <c>EF.Functions.Contains</c>.
    /// </summary>
    public bool IsFullTextSearch { get; init; }

    /// <summary>
    /// The field that declares this one (for <c>Company.Name</c>, the <c>Company</c> navigation), or null for fields
    /// of the queried entity.
    /// </summary>
    public EntityFieldInfo? Parent { get; init; }

    /// <summary>
    /// The EF Core metadata (<see cref="IProperty"/>, <see cref="INavigationBase"/>, or <see cref="IComplexProperty"/>),
    /// or null for custom fields.
    /// </summary>
    public IPropertyBase? Metadata { get; init; }

    /// <summary>
    /// Application data for custom field expression builders (for example a data definition id).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; } = EmptyData;

    /// <summary>
    /// The scalar value type of the field: the element type for collections, otherwise <see cref="ClrType"/>, with
    /// <see cref="Nullable{T}"/> removed.
    /// </summary>
    public Type ValueType => Nullable.GetUnderlyingType(ElementType ?? ClrType) ?? ElementType ?? ClrType;

    /// <summary>
    /// Whether the value type is <see cref="string"/>.
    /// </summary>
    public bool IsString => ValueType == typeof(string);

    /// <summary>
    /// Whether the value type is numeric.
    /// </summary>
    public bool IsNumber => QueryValueParser.IsNumeric(ValueType);

    /// <summary>
    /// Whether the value type is <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, or <see cref="DateOnly"/>.
    /// </summary>
    public bool IsDate => QueryValueParser.IsDate(ValueType);

    /// <summary>
    /// Whether the value type is <see cref="bool"/>.
    /// </summary>
    public bool IsBoolean => ValueType == typeof(bool);

    internal bool IsScalar => Kind is EntityFieldKind.Property or EntityFieldKind.Custom;

    internal ITypeBase? TargetType { get; init; }

    internal SortKey? SortKey
    {
        get => Volatile.Read(ref _sortKey);
        set => Volatile.Write(ref _sortKey, value);
    }

    internal static IReadOnlyDictionary<string, object?> EmptyData { get; } = FrozenDictionary<string, object?>.Empty;

    /// <inheritdoc />
    public override string ToString() => FullName;
}
