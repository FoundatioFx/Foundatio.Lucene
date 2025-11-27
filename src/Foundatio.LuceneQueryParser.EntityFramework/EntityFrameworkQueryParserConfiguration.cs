using System.Linq.Expressions;
using Foundatio.LuceneQueryParser.Ast;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.LuceneQueryParser.EntityFramework;

/// <summary>
/// Filter delegate for entity type properties.
/// </summary>
public delegate bool EntityTypePropertyFilter(IProperty property);

/// <summary>
/// Filter delegate for entity type navigations.
/// </summary>
public delegate bool EntityTypeNavigationFilter(INavigation navigation);

/// <summary>
/// Filter delegate for entity type skip navigations (many-to-many).
/// </summary>
public delegate bool EntityTypeSkipNavigationFilter(ISkipNavigation navigation);

/// <summary>
/// Context information for building custom field expressions.
/// </summary>
public class CustomFieldContext
{
    /// <summary>
    /// The field info for the custom field being queried.
    /// </summary>
    public required EntityFieldInfo Field { get; init; }
    
    /// <summary>
    /// The parameter expression for the entity (e.g., "e" in "e => ...").
    /// </summary>
    public required ParameterExpression Parameter { get; init; }
    
    /// <summary>
    /// The term value being searched (for term queries).
    /// </summary>
    public string? Term { get; init; }
    
    /// <summary>
    /// Whether this is a prefix query (e.g., "John*").
    /// </summary>
    public bool IsPrefix { get; init; }
    
    /// <summary>
    /// Whether this is a wildcard query.
    /// </summary>
    public bool IsWildcard { get; init; }
    
    /// <summary>
    /// The range node (for range queries).
    /// </summary>
    public RangeNode? RangeNode { get; init; }
    
    /// <summary>
    /// The visitor context.
    /// </summary>
    public required IEntityFrameworkQueryVisitorContext Context { get; init; }
}

/// <summary>
/// Delegate for building custom field expressions.
/// Return null to use the default expression builder.
/// </summary>
public delegate Expression? CustomFieldExpressionBuilder(CustomFieldContext context);

/// <summary>
/// Configuration options for the Entity Framework query parser.
/// </summary>
public class EntityFrameworkQueryParserConfiguration
{
    /// <summary>
    /// The default operator to use when no explicit operator is specified in the query.
    /// Default is OR.
    /// </summary>
    public BooleanOperator DefaultOperator { get; private set; } = BooleanOperator.Or;

    /// <summary>
    /// The default fields to search when no field is specified in the query.
    /// </summary>
    public string[]? DefaultFields { get; private set; }

    /// <summary>
    /// The maximum depth to traverse navigation properties when discovering fields.
    /// Default is 10.
    /// </summary>
    public int MaxFieldDepth { get; private set; } = 10;

    /// <summary>
    /// Function to parse date/time strings. Default handles common formats and "now".
    /// </summary>
    public Func<string, object?> DateTimeParser { get; private set; } = EntityFrameworkQueryVisitorContext.DefaultDateTimeParser;

    /// <summary>
    /// Function to parse date-only strings. Default handles common formats and "now".
    /// </summary>
    public Func<string, object?> DateOnlyParser { get; private set; } = EntityFrameworkQueryVisitorContext.DefaultDateOnlyParser;

    /// <summary>
    /// Filter to exclude specific properties from field discovery.
    /// Default includes all properties.
    /// </summary>
    public EntityTypePropertyFilter EntityTypePropertyFilter { get; private set; } = static _ => true;
    
    /// <summary>
    /// Whether a custom property filter has been set.
    /// </summary>
    public bool HasCustomPropertyFilter { get; private set; }

    /// <summary>
    /// Filter to exclude specific navigation properties from field discovery.
    /// Default includes all navigations.
    /// </summary>
    public EntityTypeNavigationFilter EntityTypeNavigationFilter { get; private set; } = static _ => true;
    
    /// <summary>
    /// Whether a custom navigation filter has been set.
    /// </summary>
    public bool HasCustomNavigationFilter { get; private set; }

    /// <summary>
    /// Filter to exclude specific skip navigation properties from field discovery.
    /// Default includes all skip navigations.
    /// </summary>
    public EntityTypeSkipNavigationFilter EntityTypeSkipNavigationFilter { get; private set; } = static _ => true;
    
    /// <summary>
    /// Whether a custom skip navigation filter has been set.
    /// </summary>
    public bool HasCustomSkipNavigationFilter { get; private set; }

    /// <summary>
    /// Custom expression builder for fields with custom data.
    /// Return null from the builder to use the default expression.
    /// </summary>
    public CustomFieldExpressionBuilder? CustomFieldExpressionBuilder { get; private set; }

    /// <summary>
    /// Sets the default operator for implicit boolean operations.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultOperator(BooleanOperator op)
    {
        DefaultOperator = op;
        return this;
    }

    /// <summary>
    /// Sets the default fields to search when no field is specified.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultFields(params string[] fields)
    {
        DefaultFields = fields;
        return this;
    }

    /// <summary>
    /// Sets the maximum depth for navigation property traversal.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetMaxFieldDepth(int maxDepth)
    {
        MaxFieldDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Sets the DateTime parser function.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDateTimeParser(Func<string, object?> parser)
    {
        DateTimeParser = parser;
        return this;
    }

    /// <summary>
    /// Sets the DateOnly parser function.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDateOnlyParser(Func<string, object?> parser)
    {
        DateOnlyParser = parser;
        return this;
    }

    /// <summary>
    /// Sets a filter for entity type properties.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypePropertyFilter(EntityTypePropertyFilter filter)
    {
        EntityTypePropertyFilter = filter;
        HasCustomPropertyFilter = true;
        return this;
    }

    /// <summary>
    /// Sets a filter for navigation properties.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypeNavigationFilter(EntityTypeNavigationFilter filter)
    {
        EntityTypeNavigationFilter = filter;
        HasCustomNavigationFilter = true;
        return this;
    }

    /// <summary>
    /// Sets a filter for skip navigation properties.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypeSkipNavigationFilter(EntityTypeSkipNavigationFilter filter)
    {
        EntityTypeSkipNavigationFilter = filter;
        HasCustomSkipNavigationFilter = true;
        return this;
    }

    /// <summary>
    /// Sets a custom expression builder for fields with custom data.
    /// The builder receives context about the field and query, and should return
    /// an Expression representing the filter, or null to use the default behavior.
    /// </summary>
    /// <example>
    /// <code>
    /// parser.Configuration.SetCustomFieldExpressionBuilder(ctx =>
    /// {
    ///     // Check if this is a custom field with metadata
    ///     if (!ctx.Field.Data.TryGetValue("DataDefinitionId", out var idObj) || idObj is not int definitionId)
    ///         return null; // Use default handling
    ///     
    ///     // Build expression: e.DataValues.Any(dv => dv.DataDefinitionId == X &amp;&amp; dv.NumberValue == Y)
    ///     var dataValuesProperty = Expression.Property(ctx.Parameter, "DataValues");
    ///     // ... build the Any() expression
    ///     return anyExpression;
    /// });
    /// </code>
    /// </example>
    public EntityFrameworkQueryParserConfiguration SetCustomFieldExpressionBuilder(CustomFieldExpressionBuilder builder)
    {
        CustomFieldExpressionBuilder = builder;
        return this;
    }
}
