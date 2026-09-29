using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Expands <c>@include:name</c> references with query text from <see cref="IQueryVisitorContext.Includes"/>.
/// Each expansion is wrapped in a group so it keeps its meaning, and it takes the place of the reference, so a
/// prefix such as <c>-@include:name</c> applies to the whole expansion. Includes may reference other includes;
/// recursion, depth, and the total number of expansions are bounded by <see cref="QueryValidationOptions"/>.
/// </summary>
public class IncludeVisitor : QueryVisitor
{
    /// <summary>
    /// The field name that marks an include reference.
    /// </summary>
    public const string IncludeField = "@include";

    /// <summary>
    /// The <see cref="QueryNode.Data"/> key set on each expansion's group, holding the include name.
    /// </summary>
    public const string IncludeNameKey = "@IncludeName";

    private const string StateKey = "@IncludeState";

    /// <summary>
    /// A shared instance. The visitor is stateless.
    /// </summary>
    public static IncludeVisitor Instance { get; } = new();

    /// <inheritdoc/>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (!IsInclude(node))
            return base.Visit(node, context);

        string? name = GetIncludeName(node);
        var result = context.ValidationResult;
        if (string.IsNullOrEmpty(name))
        {
            result.AddError($"Invalid {IncludeField}: missing include name", node.StartPosition, QueryErrorCode.UnresolvedInclude);
            return node;
        }

        result.ReferencedIncludes.Add(name);
        if (context.ShouldSkipInclude?.Invoke(node, context) == true)
            return node;

        var state = GetState(context);
        var options = context.ValidationOptions ?? QueryValidationOptions.Default;
        if (state.Stack.Contains(name))
        {
            result.AddError($"Recursive {IncludeField} ({name})", node.StartPosition, QueryErrorCode.UnresolvedInclude);
            return node;
        }

        if (state.Stack.Count >= options.MaxIncludeDepth)
        {
            result.AddError($"Maximum {IncludeField} depth of {options.MaxIncludeDepth} exceeded at ({name})", node.StartPosition, QueryErrorCode.MaxDepthExceeded);
            return node;
        }

        if (++state.Expansions > options.MaxIncludeExpansions)
        {
            if (state.Expansions == options.MaxIncludeExpansions + 1)
                result.AddError($"Maximum number of {IncludeField} expansions ({options.MaxIncludeExpansions}) exceeded", node.StartPosition, QueryErrorCode.MaxDepthExceeded);
            return node;
        }

        var parsed = state.GetParsed(name, context);
        if (parsed is null)
        {
            result.UnresolvedIncludes.Add(name);
            return node;
        }

        if (!parsed.IsSuccess)
        {
            result.AddError($"Invalid query in {IncludeField}:{name}: {parsed.Errors[0].Message}", node.StartPosition, QueryErrorCode.UnresolvedInclude);
            return node;
        }

        state.Stack.Add(name);
        QueryNode? expanded;
        try
        {
            expanded = parsed.Document.Query is { } query ? Accept(query.Clone(), context) : null;
        }
        finally
        {
            state.Stack.RemoveAt(state.Stack.Count - 1);
        }

        var group = new GroupNode
        {
            Query = expanded,
            BoostText = (node.Query as IBoostable)?.BoostText,
            StartPosition = node.StartPosition,
            EndPosition = node.EndPosition,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
        group.SetData(IncludeNameKey, name);
        return group;
    }

    /// <summary>
    /// Whether the node is an <c>@include:name</c> reference.
    /// </summary>
    public static bool IsInclude(FieldQueryNode node) => node.FieldMemory.Span.Equals(IncludeField, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the include name referenced by an <c>@include:name</c> node.
    /// </summary>
    public static string? GetIncludeName(FieldQueryNode node)
    {
        return node.Query switch
        {
            TermNode term => term.UnescapedTerm,
            PhraseNode phrase => phrase.Phrase,
            _ => null
        };
    }

    private static IncludeState GetState(IQueryVisitorContext context)
    {
        var state = context.GetValue<IncludeState>(StateKey);
        if (state is null)
        {
            state = new IncludeState();
            context.SetValue(StateKey, state);
        }

        return state;
    }

    /// <summary>
    /// Expands includes in a document using the specified includes.
    /// </summary>
    public static QueryDocument ExpandIncludes(QueryDocument document, IReadOnlyDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(includes);

        context ??= new QueryVisitorContext();
        context.Includes = includes;
        return (QueryDocument)Instance.Accept(document, context);
    }

    private sealed class IncludeState
    {
        private Dictionary<string, LuceneParseResult?>? _parsed;

        public List<string> Stack { get; } = [];

        public int Expansions { get; set; }

        public LuceneParseResult? GetParsed(string name, IQueryVisitorContext context)
        {
            _parsed ??= new Dictionary<string, LuceneParseResult?>(StringComparer.OrdinalIgnoreCase);
            if (_parsed.TryGetValue(name, out var result))
                return result;

            result = context.Includes is { } includes && TryGetInclude(includes, name, out string? text) && !string.IsNullOrWhiteSpace(text)
                ? LuceneQuery.Parse(text, context.ParserOptions)
                : null;

            _parsed[name] = result;
            return result;
        }

        private static bool TryGetInclude(IReadOnlyDictionary<string, string> includes, string name, out string? text)
        {
            if (includes.TryGetValue(name, out text))
                return true;

            foreach (var include in includes)
            {
                if (string.Equals(include.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    text = include.Value;
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Extension methods for include expansion.
/// </summary>
public static class IncludeExtensions
{
    /// <summary>
    /// Expands includes in a document using the specified includes.
    /// </summary>
    public static QueryDocument ExpandIncludes(this QueryDocument document, IReadOnlyDictionary<string, string> includes, IQueryVisitorContext? context = null)
    {
        return IncludeVisitor.ExpandIncludes(document, includes, context);
    }
}
