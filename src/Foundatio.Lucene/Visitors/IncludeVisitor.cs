using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// A visitor that expands @include:name references by replacing them
/// with their resolved query content.
/// </summary>
public class IncludeVisitor : QueryNodeVisitor
{
    /// <summary>
    /// Maximum depth for nested includes to prevent infinite recursion.
    /// </summary>
    public const int MaxIncludeDepth = 50;

    private readonly IncludeResolver? _globalResolver;

    /// <summary>
    /// Creates a new IncludeVisitor with no global resolver.
    /// A resolver can be set on the context instead.
    /// </summary>
    public IncludeVisitor()
    {
    }

    /// <summary>
    /// Creates a new IncludeVisitor with the specified global resolver.
    /// </summary>
    /// <param name="resolver">The resolver used to look up include definitions.</param>
    public IncludeVisitor(IncludeResolver? resolver)
    {
        _globalResolver = resolver;
    }

    /// <summary>
    /// Visits a FieldQueryNode and expands @include references.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Check if this is an @include field
        if (!IsIncludeField(node))
        {
            // Not an include, visit children normally
            return await base.VisitAsync(node, context).ConfigureAwait(false);
        }

        // Get the include name from the query
        var includeName = GetIncludeName(node);
        if (string.IsNullOrEmpty(includeName))
        {
            context.AddValidationError($"Invalid @include syntax: missing include name");
            return node;
        }

        // Track for validation
        context.GetValidationResult().ReferencedIncludes.Add(includeName);

        // Check skip function
        var shouldSkip = context.GetShouldSkipIncludeFunc();
        if (shouldSkip?.Invoke(node, context) == true)
            return node;

        // Check for circular references
        if (context.IsIncludeInStack(includeName))
        {
            context.AddValidationError($"Circular @include reference detected: {includeName}");
            return node;
        }

        // Check max depth
        var stack = context.GetIncludeStack();
        if (stack.Count >= MaxIncludeDepth)
        {
            context.AddValidationError($"Maximum include depth ({MaxIncludeDepth}) exceeded at: {includeName}");
            return node;
        }

        // Resolve the include
        var resolver = context.GetIncludeResolver() ?? _globalResolver;
        if (resolver is null)
        {
            context.GetValidationResult().UnresolvedIncludes.Add(includeName);
            return node;
        }

        string? includeContent;
        try
        {
            includeContent = await resolver(includeName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.AddValidationError($"Error in include resolver callback when expanding @include:{includeName}: {ex.Message}");
            return node;
        }

        if (string.IsNullOrWhiteSpace(includeContent))
        {
            context.GetValidationResult().UnresolvedIncludes.Add(includeName);
            return node;
        }

        // Parse the include content
        var parseResult = LuceneQuery.Parse(includeContent);
        if (!parseResult.IsSuccess || parseResult.Document?.Query is null)
        {
            var errorMessage = parseResult.Errors.Count > 0 ? parseResult.Errors[0].Message : "Unknown error";
            context.AddValidationError($"Invalid query in @include:{includeName}: {errorMessage}");
            return node;
        }

        // Push onto stack for circular reference detection
        context.PushInclude(includeName);

        try
        {
            // Recursively expand any nested includes
            var expandedNode = await AcceptAsync(parseResult.Document.Query, context).ConfigureAwait(false);

            // Handle prefix/suffix/boost from the original node
            var resultNode = WrapExpandedNode(expandedNode, node);

            return resultNode;
        }
        finally
        {
            context.PopInclude();
        }
    }

    private static bool IsIncludeField(FieldQueryNode node)
    {
        return string.Equals(node.Field, "@include", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetIncludeName(FieldQueryNode node)
    {
        // The include name can be in Query (if parsed as term) or in the field query
        if (node.Query is TermNode termNode)
            return termNode.Term;

        if (node.Query is PhraseNode phraseNode)
            return phraseNode.Phrase;

        return null;
    }

    private static QueryNode WrapExpandedNode(QueryNode expandedNode, FieldQueryNode originalNode)
    {
        // Wrap in a group to preserve precedence
        var groupNode = new GroupNode { Query = expandedNode };
        return groupNode;
    }

    #region Static RunAsync Methods

    /// <summary>
    /// Expands includes in a query document asynchronously using the specified resolver.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="resolver">The include resolver to use.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static Task<QueryDocument> ExpandIncludesAsync(QueryDocument document, IncludeResolver resolver, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetIncludeResolver(resolver);
        return new IncludeVisitor().RunAsync(document, context);
    }

    /// <summary>
    /// Expands includes in a query document asynchronously using a dictionary of includes.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="includes">Dictionary mapping include names to their query content.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static Task<QueryDocument> ExpandIncludesAsync(QueryDocument document, IDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        IncludeResolver resolver = name =>
        {
            includes.TryGetValue(name, out var value);
            return Task.FromResult(value);
        };

        return ExpandIncludesAsync(document, resolver, context);
    }

    #endregion
}

/// <summary>
/// Extension methods for include expansion.
/// </summary>
public static class IncludeExtensions
{
    /// <summary>
    /// Expands includes in a query document asynchronously using the specified resolver.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="resolver">The include resolver to use.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static Task<QueryDocument> ExpandIncludesAsync(this QueryDocument document, IncludeResolver resolver, IQueryVisitorContext? context = null)
    {
        return IncludeVisitor.ExpandIncludesAsync(document, resolver, context);
    }

    /// <summary>
    /// Expands includes in a query document asynchronously using a dictionary of includes.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="includes">Dictionary mapping include names to their query content.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static Task<QueryDocument> ExpandIncludesAsync(this QueryDocument document, IDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        return IncludeVisitor.ExpandIncludesAsync(document, includes, context);
    }

    /// <summary>
    /// Expands includes in a query document asynchronously using the resolver from the context.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="context">The context containing the include resolver.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static Task<QueryDocument> ExpandIncludesAsync(this QueryDocument document, IQueryVisitorContext context)
    {
        return new IncludeVisitor().RunAsync(document, context);
    }
}
