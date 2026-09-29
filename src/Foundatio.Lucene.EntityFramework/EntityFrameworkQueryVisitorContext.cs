using System.Diagnostics.CodeAnalysis;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// The per-request state used while an Entity Framework query is processed. A new context is created for every
/// request, so custom visitors can store request state on it safely.
/// </summary>
public sealed class EntityFrameworkQueryVisitorContext : QueryVisitorContext
{
    private readonly EntityFieldResolver _fields;

    internal EntityFrameworkQueryVisitorContext(EntityFrameworkQueryParserConfiguration configuration, EntityFieldResolver fields, IEntityType entityType)
    {
        Configuration = configuration;
        _fields = fields;
        EntityType = entityType;
    }

    /// <summary>
    /// The EF Core entity type being queried.
    /// </summary>
    public IEntityType EntityType { get; }

    /// <summary>
    /// The parser configuration.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration Configuration { get; }

    /// <summary>
    /// The per-request options, if any.
    /// </summary>
    public EntityFrameworkQueryOptions? Options { get; internal set; }

    /// <summary>
    /// How terms without a field are matched against string default fields.
    /// </summary>
    public SearchOperator DefaultSearchOperator { get; set; }

    /// <summary>
    /// The time zone used for <c>now</c>, date math rounding, and dates written without an offset.
    /// </summary>
    public TimeZoneInfo DefaultTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Custom fields that can be queried in addition to the model's fields.
    /// </summary>
    public IReadOnlyList<EntityFieldInfo> AdditionalFields { get; set; } = [];

    /// <summary>
    /// The builder for custom field expressions.
    /// </summary>
    public CustomFieldExpressionBuilder? CustomFieldExpressionBuilder { get; set; }

    /// <summary>
    /// Looks up a queryable field by name (case-insensitive): a custom field, or a path of the entity type's model
    /// members such as <c>Company.Name</c>. Fields excluded by the configured filters are not found.
    /// </summary>
    public bool TryGetField(string name, [NotNullWhen(true)] out EntityFieldInfo? field)
    {
        ArgumentNullException.ThrowIfNull(name);

        var additional = AdditionalFields;
        for (int i = additional.Count - 1; i >= 0; i--)
        {
            if (string.Equals(additional[i].FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                field = additional[i];
                return true;
            }
        }

        field = _fields.Resolve(EntityType, name);
        return field is not null;
    }
}
