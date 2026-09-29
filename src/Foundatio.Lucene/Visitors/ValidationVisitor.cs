using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Collects the fields, operations, and nesting depth a query uses into
/// <see cref="IQueryVisitorContext.ValidationResult"/>. When it visits a <see cref="QueryDocument"/> it then applies
/// the context's <see cref="QueryValidationOptions"/> (see <see cref="ApplyRestrictions"/>).
/// </summary>
public class ValidationVisitor : QueryVisitor
{
    private const string FieldStackKey = "@ValidationFieldStack";

    /// <summary>
    /// A shared instance. The visitor is stateless.
    /// </summary>
    public static ValidationVisitor Instance { get; } = new();

    /// <inheritdoc/>
    public override QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        var result = base.Accept(node, context);
        if (node is QueryDocument)
            ApplyRestrictions(context);

        return result;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(GroupNode node, IQueryVisitorContext context)
    {
        var result = context.ValidationResult;
        result.CurrentNodeDepth++;
        try
        {
            return base.Visit(node, context);
        }
        finally
        {
            result.CurrentNodeDepth--;
        }
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        RecordField(node, context);

        var fields = GetFieldStack(context);
        fields.Push(node.Field);
        try
        {
            return base.Visit(node, context);
        }
        finally
        {
            fields.Pop();
        }
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        string operation = node.IsPrefix ? QueryOperations.Prefix
            : node.IsWildcard ? QueryOperations.Wildcard
            : node.IsFuzzy ? QueryOperations.Fuzzy
            : QueryOperations.Term;
        AddOperation(operation, context);

        var options = context.ValidationOptions;
        if (options is { AllowLeadingWildcards: false } && node.IsWildcard && StartsWithWildcard(node.TermMemory.Span))
            context.ValidationResult.AddError($"Terms must not start with a wildcard: {node.Term}", node.StartPosition, QueryErrorCode.LeadingWildcardNotAllowed);

        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(PhraseNode node, IQueryVisitorContext context)
    {
        AddOperation(QueryOperations.Phrase, context);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(RegexNode node, IQueryVisitorContext context)
    {
        AddOperation(QueryOperations.Regex, context);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        AddOperation(QueryOperations.Range, context);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(MatchAllNode node, IQueryVisitorContext context)
    {
        AddOperation(QueryOperations.MatchAll, context);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context)
    {
        RecordField(node, context);
        context.ValidationResult.AddOperation(QueryOperations.Exists, node.Field);
        return node;
    }

    /// <inheritdoc/>
    protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context)
    {
        RecordField(node, context);
        context.ValidationResult.AddOperation(QueryOperations.Missing, node.Field);
        return node;
    }

    private static void RecordField<T>(T node, IQueryVisitorContext context) where T : QueryNode, IFieldNode
    {
        string field = node.Field;
        if (field.Length == 0 || field[0] == '@')
            return;

        var result = context.ValidationResult;
        result.ReferencedFields.Add(node.GetOriginalField());
        result.ResolvedFields.Add(field);
    }

    private static void AddOperation(string operation, IQueryVisitorContext context)
    {
        var fields = context.GetValue<Stack<string>>(FieldStackKey);
        string? field = fields is { Count: > 0 } ? fields.Peek() : null;
        if (field is { Length: > 0 } && field[0] == '@')
            return;

        if (field is null && operation != QueryOperations.MatchAll)
            RecordDefaultFields(context);

        context.ValidationResult.AddOperation(operation, field);
    }

    private static void RecordDefaultFields(IQueryVisitorContext context)
    {
        if (context.DefaultFields is not { Length: > 0 } defaultFields)
            return;

        var result = context.ValidationResult;
        foreach (string defaultField in defaultFields)
        {
            if (string.IsNullOrEmpty(defaultField) || !result.ReferencedFields.Add(defaultField))
                continue;

            FieldResolverQueryVisitor.TryResolveField(defaultField, context, out string resolved);
            result.ResolvedFields.Add(resolved);
        }
    }

    private static Stack<string> GetFieldStack(IQueryVisitorContext context)
    {
        var stack = context.GetValue<Stack<string>>(FieldStackKey);
        if (stack is null)
        {
            stack = new Stack<string>();
            context.SetValue(FieldStackKey, stack);
        }

        return stack;
    }

    private static bool StartsWithWildcard(ReadOnlySpan<char> term)
    {
        return term.Length > 0 && term[0] is '*' or '?';
    }

    /// <summary>
    /// Applies the context's <see cref="QueryValidationOptions"/> to the information collected so far, adding
    /// errors to <see cref="IQueryVisitorContext.ValidationResult"/>.
    /// </summary>
    public static void ApplyRestrictions(IQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.ValidationOptions ?? QueryValidationOptions.Default;
        var result = context.ValidationResult;

        bool hasFieldRules = options.AllowedFields.Count > 0 || options.RestrictedFields.Count > 0;
        if (hasFieldRules)
        {
            var wildcardFields = result.ReferencedFields.Where(f => f.AsSpan().IndexOfAny('*', '?') >= 0).ToList();
            if (wildcardFields.Count > 0)
                result.AddError($"Query uses wildcard field name(s) ({string.Join(", ", wildcardFields)}) which are not allowed when field restrictions are configured.", code: QueryErrorCode.FieldNotAllowed);
        }

        if (options.RestrictedFields.Count > 0)
        {
            var restricted = result.ReferencedFields.Concat(result.ResolvedFields)
                .Where(f => MatchesAny(f, options.RestrictedFields))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (restricted.Count > 0)
                result.AddError($"Query uses field(s) ({string.Join(", ", restricted)}) that are restricted from use.", code: QueryErrorCode.FieldRestricted);
        }

        if (options.AllowedFields.Count > 0)
        {
            var notAllowed = result.ReferencedFields.Where(f => !MatchesAny(f, options.AllowedFields)).ToList();
            if (notAllowed.Count > 0)
                result.AddError($"Query uses field(s) ({string.Join(", ", notAllowed)}) that are not allowed to be used.", code: QueryErrorCode.FieldNotAllowed);
        }

        if (!options.AllowUnresolvedFields && result.UnresolvedFields.Count > 0)
            result.AddError($"Query uses field(s) ({string.Join(", ", result.UnresolvedFields)}) that can't be resolved.", code: QueryErrorCode.UnresolvedField);

        if (!options.AllowUnresolvedIncludes && result.UnresolvedIncludes.Count > 0)
            result.AddError($"Query uses include(s) ({string.Join(", ", result.UnresolvedIncludes)}) that can't be resolved.", code: QueryErrorCode.UnresolvedInclude);

        if (options.AllowedOperations.Count > 0)
        {
            var notAllowed = result.Operations.Keys.Where(op => !options.AllowedOperations.Contains(op)).ToList();
            if (notAllowed.Count > 0)
                result.AddError($"Query uses operation(s) ({string.Join(", ", notAllowed)}) that are not allowed to be used.", code: QueryErrorCode.OperationNotAllowed);
        }

        if (options.RestrictedOperations.Count > 0)
        {
            var restricted = result.Operations.Keys.Where(options.RestrictedOperations.Contains).ToList();
            if (restricted.Count > 0)
                result.AddError($"Query uses operation(s) ({string.Join(", ", restricted)}) that are restricted from use.", code: QueryErrorCode.OperationRestricted);
        }

        if (options.AllowedMaxNodeDepth > 0 && result.MaxNodeDepth > options.AllowedMaxNodeDepth)
            result.AddError($"Query has a node depth {result.MaxNodeDepth} greater than the allowed maximum {options.AllowedMaxNodeDepth}.", code: QueryErrorCode.MaxDepthExceeded);
    }

    /// <summary>
    /// Whether <paramref name="field"/> is one of <paramref name="fields"/> or a sub-field of one.
    /// </summary>
    internal static bool MatchesAny(string field, ICollection<string> fields)
    {
        if (fields.Contains(field))
            return true;

        int dot = field.IndexOf('.');
        while (dot > 0)
        {
            if (fields.Contains(field[..dot]))
                return true;
            dot = field.IndexOf('.', dot + 1);
        }

        return false;
    }

    /// <summary>
    /// Validates a node using the context's options and returns the result.
    /// </summary>
    public static QueryValidationResult Run(QueryNode node, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        context ??= new QueryVisitorContext();
        Instance.Accept(node, context);
        if (node is not QueryDocument)
            ApplyRestrictions(context);

        return context.ValidationResult;
    }

    /// <summary>
    /// Validates a node using the specified options and returns the result.
    /// </summary>
    public static QueryValidationResult Run(QueryNode node, QueryValidationOptions options, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        context ??= new QueryVisitorContext();
        context.ValidationOptions = options;
        return Run(node, context);
    }
}
