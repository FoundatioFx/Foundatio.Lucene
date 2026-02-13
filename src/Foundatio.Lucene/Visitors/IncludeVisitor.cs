using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// A visitor that expands @include:name references by replacing them
/// with their resolved query content from a pre-resolved dictionary.
/// </summary>
public class IncludeVisitor : QueryVisitor
{
    /// <summary>
    /// Maximum depth for nested includes to prevent infinite recursion.
    /// </summary>
    public const int MaxIncludeDepth = 50;

    private readonly IReadOnlyDictionary<string, string>? _includes;

    /// <summary>
    /// Creates a new IncludeVisitor with no includes.
    /// Includes can be set on the context instead.
    /// </summary>
    public IncludeVisitor()
    {
    }

    /// <summary>
    /// Creates a new IncludeVisitor with the specified pre-resolved includes.
    /// </summary>
    /// <param name="includes">Dictionary mapping include names to their query content.</param>
    public IncludeVisitor(IReadOnlyDictionary<string, string>? includes)
    {
        _includes = includes;
    }

    /// <summary>
    /// Visits a FieldQueryNode and expands @include references.
    /// </summary>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Check if this is an @include field
        if (!IsIncludeField(node))
        {
            // Not an include, visit children normally
            return base.Visit(node, context);
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

        // Resolve the include from context or constructor-provided includes
        var includes = context.GetIncludes() ?? _includes;
        if (includes is null || !includes.TryGetValue(includeName, out var includeContent))
        {
            context.GetValidationResult().UnresolvedIncludes.Add(includeName);
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
            var expandedNode = Accept(parseResult.Document.Query, context);

            // Wrap in a group to preserve precedence
            return new GroupNode { Query = expandedNode };
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

    #region Static Run Methods

    /// <summary>
    /// Expands includes in a query document using the specified includes dictionary.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="includes">Dictionary mapping include names to their query content.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static QueryDocument ExpandIncludes(QueryDocument document, IReadOnlyDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetIncludes(includes);
        return new IncludeVisitor().Run(document, context);
    }

    #endregion
}

/// <summary>
/// Extension methods for include expansion.
/// </summary>
public static class IncludeExtensions
{
    /// <summary>
    /// Expands includes in a query document using the specified includes dictionary.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="includes">Dictionary mapping include names to their query content.</param>
    /// <param name="context">Optional context. If null, a new context is created.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static QueryDocument ExpandIncludes(this QueryDocument document, IReadOnlyDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        return IncludeVisitor.ExpandIncludes(document, includes, context);
    }

    /// <summary>
    /// Expands includes in a query document using the includes from the context.
    /// </summary>
    /// <param name="document">The query document to process.</param>
    /// <param name="context">The context containing the includes.</param>
    /// <returns>The processed query document with includes expanded.</returns>
    public static QueryDocument ExpandIncludes(this QueryDocument document, IQueryVisitorContext context)
    {
        return new IncludeVisitor().Run(document, context);
    }
}
