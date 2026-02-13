using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// A visitor that resolves field names using a FieldMap.
/// This allows using field aliases that are mapped to their actual field names.
/// </summary>
public class FieldResolverQueryVisitor : QueryVisitor
{
    private readonly FieldMap? _fieldMap;

    /// <summary>
    /// Creates a new FieldResolverQueryVisitor with no field map.
    /// A FieldMap can be set on the context instead.
    /// </summary>
    public FieldResolverQueryVisitor()
    {
    }

    /// <summary>
    /// Creates a new FieldResolverQueryVisitor with the specified field map.
    /// </summary>
    /// <param name="fieldMap">The field map to use when resolving field names.</param>
    public FieldResolverQueryVisitor(FieldMap? fieldMap)
    {
        _fieldMap = fieldMap;
    }

    /// <summary>
    /// Visits a FieldQueryNode and resolves the field name.
    /// </summary>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // First visit children
        base.Visit(node, context);

        // Then resolve the field
        ResolveField(node, context);

        return node;
    }

    /// <summary>
    /// Visits an ExistsNode and resolves the field name.
    /// </summary>
    protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context)
    {
        ResolveExistsField(node, context);
        return node;
    }

    /// <summary>
    /// Visits a MissingNode and resolves the field name.
    /// </summary>
    protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context)
    {
        ResolveMissingField(node, context);
        return node;
    }

    /// <summary>
    /// Visits a RangeNode and resolves the field name.
    /// </summary>
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        ResolveRangeField(node, context);
        return node;
    }

    private FieldMap? GetEffectiveFieldMap(IQueryVisitorContext context)
    {
        // Context field map takes precedence
        var contextFieldMap = context.GetFieldMap();
        return contextFieldMap ?? _fieldMap;
    }

    private void ResolveField(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var fieldMap = GetEffectiveFieldMap(context);
        if (fieldMap is null)
            return;

        var resolvedField = fieldMap.ResolveField(node.Field);
        if (resolvedField is null)
        {
            // Add to unresolved fields list
            context.GetValidationResult().UnresolvedFields.Add(node.Field);
            return;
        }

        if (!resolvedField.Equals(node.Field, StringComparison.Ordinal))
        {
            node.SetOriginalField(context, node.Field);
            node.Field = resolvedField;
        }
    }

    private void ResolveExistsField(ExistsNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var fieldMap = GetEffectiveFieldMap(context);
        if (fieldMap is null)
            return;

        var resolvedField = fieldMap.ResolveField(node.Field);
        if (resolvedField is null)
        {
            context.GetValidationResult().UnresolvedFields.Add(node.Field);
            return;
        }

        if (!resolvedField.Equals(node.Field, StringComparison.Ordinal))
        {
            node.SetOriginalField(context, node.Field);
            node.Field = resolvedField;
        }
    }

    private void ResolveMissingField(MissingNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var fieldMap = GetEffectiveFieldMap(context);
        if (fieldMap is null)
            return;

        var resolvedField = fieldMap.ResolveField(node.Field);
        if (resolvedField is null)
        {
            context.GetValidationResult().UnresolvedFields.Add(node.Field);
            return;
        }

        if (!resolvedField.Equals(node.Field, StringComparison.Ordinal))
        {
            node.SetOriginalField(context, node.Field);
            node.Field = resolvedField;
        }
    }

    private void ResolveRangeField(RangeNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var fieldMap = GetEffectiveFieldMap(context);
        if (fieldMap is null)
            return;

        var resolvedField = fieldMap.ResolveField(node.Field);
        if (resolvedField is null)
        {
            context.GetValidationResult().UnresolvedFields.Add(node.Field);
            return;
        }

        if (!resolvedField.Equals(node.Field, StringComparison.Ordinal))
        {
            node.SetOriginalField(context, node.Field);
            node.Field = resolvedField;
        }
    }

    #region Static Run Methods

    /// <summary>
    /// Runs the field resolver visitor on a query document using the specified field map.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="fieldMap">The field map to use for resolution.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document.</returns>
    public static QueryDocument Run(QueryDocument document, FieldMap fieldMap, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetFieldMap(fieldMap);
        return new FieldResolverQueryVisitor().Run(document, context);
    }

    /// <summary>
    /// Runs the field resolver visitor on a query document using a dictionary as field map.
    /// Uses hierarchical field resolution for nested field paths.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="map">The field map dictionary to use for resolution.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document.</returns>
    public static QueryDocument Run(QueryDocument document, IDictionary<string, string> map, IQueryVisitorContext? context = null)
    {
        var fieldMap = new FieldMap(map);
        return Run(document, fieldMap, context);
    }

    #endregion
}
