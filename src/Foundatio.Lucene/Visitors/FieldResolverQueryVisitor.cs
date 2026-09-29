using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Replaces field names with the names returned by the context's <see cref="IQueryVisitorContext.FieldResolver"/>
/// and <see cref="IQueryVisitorContext.FieldMap"/>, recording the original name on each node
/// (see <see cref="QueryNodeExtensions.GetOriginalField{T}"/>). Fields that cannot be resolved are added to
/// <see cref="QueryValidationResult.UnresolvedFields"/>. Fields starting with <c>@</c> are left alone.
/// </summary>
public class FieldResolverQueryVisitor : QueryVisitor
{
    /// <summary>
    /// A shared instance. The visitor is stateless.
    /// </summary>
    public static FieldResolverQueryVisitor Instance { get; } = new();

    /// <inheritdoc/>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        Resolve(node, context);
        return base.Visit(node, context);
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context)
    {
        Resolve(node, context);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context)
    {
        Resolve(node, context);
        return node;
    }

    private static void Resolve<T>(T node, IQueryVisitorContext context) where T : QueryNode, IFieldNode
    {
        string field = node.Field;
        if (field.Length == 0 || field[0] == '@' || node.Data.ContainsKey(QueryNodeExtensions.OriginalFieldKey))
            return;

        if (!TryResolveField(field, context, out string resolved))
        {
            context.ValidationResult.UnresolvedFields.Add(field);
            return;
        }

        if (!string.Equals(resolved, field, StringComparison.Ordinal))
        {
            node.SetOriginalField(field);
            node.Field = resolved;
        }
    }

    /// <summary>
    /// Resolves a field name using the context's field resolver, then its field map. When neither is configured the
    /// field resolves to itself.
    /// </summary>
    /// <returns>False when a resolver is configured but none could resolve the field.</returns>
    public static bool TryResolveField(string field, IQueryVisitorContext context, out string resolved)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(context);

        if (context.FieldResolver is null && context.FieldMap is null)
        {
            resolved = field;
            return true;
        }

        string? result;
        try
        {
            result = context.FieldResolver?.Invoke(field, context);
        }
        catch (Exception ex)
        {
            context.ValidationResult.AddError($"Error in field resolver callback when resolving field ({field}): {ex.Message}", code: QueryErrorCode.UnresolvedField);
            resolved = field;
            return false;
        }

        result ??= context.FieldMap?.ResolveField(field);
        resolved = result ?? field;
        return result is not null;
    }

    /// <summary>
    /// Resolves field aliases in a document using the specified field map.
    /// </summary>
    public static QueryDocument Run(QueryDocument document, FieldMap fieldMap, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fieldMap);

        context ??= new QueryVisitorContext();
        context.FieldMap = fieldMap;
        return (QueryDocument)Instance.Accept(document, context);
    }

    /// <summary>
    /// Resolves field names in a document using the specified resolver.
    /// </summary>
    public static QueryDocument Run(QueryDocument document, QueryFieldResolver resolver, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);

        context ??= new QueryVisitorContext();
        context.FieldResolver = resolver;
        return (QueryDocument)Instance.Accept(document, context);
    }
}
