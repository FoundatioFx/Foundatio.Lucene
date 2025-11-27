using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser.Visitors;

/// <summary>
/// A visitor that resolves field names using a field resolver.
/// This allows using field aliases that are mapped to their actual field names.
/// </summary>
public class FieldResolverQueryVisitor : QueryNodeVisitor
{
    private readonly QueryFieldResolver? _globalResolver;

    /// <summary>
    /// Creates a new FieldResolverQueryVisitor with no global resolver.
    /// A resolver can be set on the context instead.
    /// </summary>
    public FieldResolverQueryVisitor()
    {
    }

    /// <summary>
    /// Creates a new FieldResolverQueryVisitor with the specified global resolver.
    /// </summary>
    /// <param name="globalResolver">The resolver to use when resolving field names.</param>
    public FieldResolverQueryVisitor(QueryFieldResolver? globalResolver)
    {
        _globalResolver = globalResolver;
    }

    /// <summary>
    /// Visits a FieldQueryNode and resolves the field name.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // First visit children
        await base.VisitAsync(node, context).ConfigureAwait(false);

        // Then resolve the field
        await ResolveFieldAsync(node, context).ConfigureAwait(false);

        return node;
    }

    /// <summary>
    /// Visits an ExistsNode and resolves the field name.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context)
    {
        await ResolveExistsFieldAsync(node, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits a MissingNode and resolves the field name.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context)
    {
        await ResolveMissingFieldAsync(node, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits a RangeNode and resolves the field name.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        await ResolveRangeFieldAsync(node, context).ConfigureAwait(false);
        return node;
    }

    private async Task ResolveFieldAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var contextResolver = context.GetFieldResolver();
        if (_globalResolver is null && contextResolver is null)
            return;

        try
        {
            string? resolvedField = null;

            // Try context resolver first
            if (contextResolver is not null)
                resolvedField = await contextResolver(node.Field, context).ConfigureAwait(false);

            // Fall back to global resolver
            if (resolvedField is null && _globalResolver is not null)
                resolvedField = await _globalResolver(node.Field, context).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            context.AddValidationError($"Error in field resolver callback when resolving field ({node.Field}): {ex.Message}");
        }
    }

    private async Task ResolveExistsFieldAsync(ExistsNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var contextResolver = context.GetFieldResolver();
        if (_globalResolver is null && contextResolver is null)
            return;

        try
        {
            string? resolvedField = null;

            if (contextResolver is not null)
                resolvedField = await contextResolver(node.Field, context).ConfigureAwait(false);

            if (resolvedField is null && _globalResolver is not null)
                resolvedField = await _globalResolver(node.Field, context).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            context.AddValidationError($"Error in field resolver callback when resolving field ({node.Field}): {ex.Message}");
        }
    }

    private async Task ResolveMissingFieldAsync(MissingNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var contextResolver = context.GetFieldResolver();
        if (_globalResolver is null && contextResolver is null)
            return;

        try
        {
            string? resolvedField = null;

            if (contextResolver is not null)
                resolvedField = await contextResolver(node.Field, context).ConfigureAwait(false);

            if (resolvedField is null && _globalResolver is not null)
                resolvedField = await _globalResolver(node.Field, context).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            context.AddValidationError($"Error in field resolver callback when resolving field ({node.Field}): {ex.Message}");
        }
    }

    private async Task ResolveRangeFieldAsync(RangeNode node, IQueryVisitorContext context)
    {
        if (string.IsNullOrEmpty(node.Field))
            return;

        var contextResolver = context.GetFieldResolver();
        if (_globalResolver is null && contextResolver is null)
            return;

        try
        {
            string? resolvedField = null;

            if (contextResolver is not null)
                resolvedField = await contextResolver(node.Field, context).ConfigureAwait(false);

            if (resolvedField is null && _globalResolver is not null)
                resolvedField = await _globalResolver(node.Field, context).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            context.AddValidationError($"Error in field resolver callback when resolving field ({node.Field}): {ex.Message}");
        }
    }

    #region Static RunAsync Methods

    /// <summary>
    /// Runs the field resolver visitor on a query document asynchronously using the specified resolver.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="resolver">The field resolver to use.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document.</returns>
    public static async Task<QueryDocument> RunAsync(QueryDocument document, QueryFieldResolver resolver, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetFieldResolver(resolver);
        return await new FieldResolverQueryVisitor().RunAsync(document, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the field resolver visitor on a query document asynchronously using a synchronous resolver.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="resolver">The synchronous field resolver to use.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document.</returns>
    public static async Task<QueryDocument> RunAsync(QueryDocument document, Func<string, string?> resolver, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetFieldResolver(resolver);
        return await new FieldResolverQueryVisitor().RunAsync(document, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the field resolver visitor on a query document asynchronously using a field map.
    /// Uses hierarchical field resolution for nested field paths.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="map">The field map to use for resolution.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document.</returns>
    public static async Task<QueryDocument> RunAsync(QueryDocument document, IDictionary<string, string> map, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetFieldResolver(map.ToHierarchicalFieldResolver());
        return await new FieldResolverQueryVisitor().RunAsync(document, context).ConfigureAwait(false);
    }

    #endregion
}
