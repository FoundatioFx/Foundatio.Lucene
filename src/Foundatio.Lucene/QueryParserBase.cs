using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// The processing engine shared by the query parsers.
/// </summary>
/// <remarks>
/// A query is processed in three phases:
/// <list type="number">
/// <item><b>Parse</b> (synchronous): the text becomes a <see cref="QueryDocument"/>.</item>
/// <item><b>Resolve</b> (asynchronous, optional): lookups that need I/O, such as include text, field names, or
/// provider data like index mappings, are fetched and stored on the context. Only the <c>Async</c> methods run it.</item>
/// <item><b>Process and build</b> (synchronous): the visitor pipeline expands includes, resolves fields, and
/// validates, and the provider builds its output from the processed tree.</item>
/// </list>
/// </remarks>
/// <typeparam name="TContext">The provider's visitor context type.</typeparam>
public abstract class QueryParserBase<TContext> where TContext : QueryVisitorContext
{
    /// <summary>
    /// Creates the engine for the specified configuration.
    /// </summary>
    protected QueryParserBase(QueryParserConfiguration configuration)
    {
        BaseConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// The shared configuration.
    /// </summary>
    protected QueryParserConfiguration BaseConfiguration { get; }

    /// <summary>
    /// Applies the configuration, then the registered (per-scope) options, then the per-request options to a context.
    /// Later sources win.
    /// </summary>
    protected void ApplyOptions(TContext context, QueryOptionsBase? registered, QueryOptionsBase? options)
    {
        var config = BaseConfiguration;
        var defaultOperator = options?.DefaultOperator ?? registered?.DefaultOperator;
        context.ParserOptions = defaultOperator is { } op && op != config.ParserOptions.DefaultOperator
            ? config.ParserOptions with { DefaultOperator = op }
            : config.ParserOptions;
        context.DefaultFields = options?.DefaultFields ?? registered?.DefaultFields ?? config.DefaultFields;
        context.FieldMap = options?.FieldMap ?? registered?.FieldMap ?? config.FieldMap;
        context.FieldResolver = options?.FieldResolver ?? registered?.FieldResolver ?? config.FieldResolver;
        context.AsyncFieldResolver = options?.AsyncFieldResolver ?? registered?.AsyncFieldResolver ?? config.AsyncFieldResolver;
        context.Includes = options?.Includes ?? registered?.Includes ?? config.Includes;
        context.IncludeResolver = options?.IncludeResolver ?? registered?.IncludeResolver ?? config.IncludeResolver;
        context.ShouldSkipInclude = config.ShouldSkipInclude;
        context.ValidationOptions = options?.ValidationOptions ?? registered?.ValidationOptions ?? config.ValidationOptions;
        context.TimeProvider = config.TimeProvider;
    }

    /// <summary>
    /// Parses query text. Syntax errors are added to the context's validation result (with their positions and
    /// parse error codes (100-199)) and thrown as a <see cref="QueryValidationException"/>, like
    /// every other problem with the query, so callers handle all invalid input with one exception type.
    /// </summary>
    protected static QueryDocument ParseQuery(string query, TContext context)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parsed = LuceneQuery.Parse(query, context.ParserOptions);
        if (parsed.IsSuccess)
            return parsed.Document;

        var result = BeginExpression(context, QueryType.Query);
        foreach (var error in parsed.Errors)
            result.AddError(error.Message, error.Position, error.Code);

        result.ThrowIfInvalid();
        return parsed.Document;
    }

    /// <summary>
    /// Runs the query visitor pipeline on a document and throws a <see cref="QueryValidationException"/> when the
    /// query is invalid. The caller's document is never modified: it is cloned when <paramref name="clone"/> is true,
    /// which callers pass for documents they did not parse themselves.
    /// </summary>
    protected QueryDocument ProcessQuery(QueryDocument document, TContext context, bool clone)
    {
        ArgumentNullException.ThrowIfNull(document);
        BeginExpression(context, QueryType.Query);
        EnsureResolved(context);

        if (clone)
            document = document.CloneDocument();

        var processed = BaseConfiguration.QueryVisitor.Accept(document, context) as QueryDocument
            ?? throw new InvalidOperationException("A query visitor replaced the query document with a different node type.");

        context.ValidationResult.ThrowIfInvalid();
        return processed;
    }

    /// <summary>
    /// Validates query text using the full pipeline (includes and field resolution included) without building it.
    /// </summary>
    protected QueryValidationResult ValidateQuery(string query, TContext context)
    {
        ArgumentNullException.ThrowIfNull(query);
        BeginExpression(context, QueryType.Query);
        EnsureResolved(context);

        var parsed = LuceneQuery.Parse(query, context.ParserOptions);
        foreach (var error in parsed.Errors)
            context.ValidationResult.AddError(error.Message, error.Position, error.Code);

        if (context.ValidationResult.IsValid)
            BaseConfiguration.QueryVisitor.Accept(parsed.Document, context);

        return FinishValidation(context);
    }

    /// <summary>
    /// Asynchronously validates query text, running the resolution phase first.
    /// </summary>
    protected async ValueTask<QueryValidationResult> ValidateQueryAsync(string query, TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parsed = LuceneQuery.Parse(query, context.ParserOptions);
        if (parsed.IsSuccess)
            await ResolveQueryAsync(parsed.Document, context, cancellationToken).ConfigureAwait(false);

        context.IsResolved = true;
        return ValidateQuery(query, context);
    }

    private static QueryValidationResult FinishValidation(TContext context)
    {
        var result = context.ValidationResult;
        if ((context.ValidationOptions?.ShouldThrow ?? false) && !result.IsValid)
            result.ThrowIfInvalid();

        return result;
    }

    /// <summary>
    /// Parses and processes a sort expression, returning the fields to sort by. Throws when the expression is invalid.
    /// </summary>
    protected List<SortField> ProcessSort(string sort, TContext context)
    {
        ArgumentNullException.ThrowIfNull(sort);
        var result = BeginExpression(context, QueryType.Sort);
        EnsureResolved(context);

        var document = ParseExpression(sort, context);
        var fields = SortExpression.FromDocument(document, result);
        foreach (var field in fields)
        {
            field.Field = ResolveField(field.OriginalField, context);
            result.ReferencedFields.Add(field.OriginalField);
            result.ResolvedFields.Add(field.Field);
        }

        ValidationVisitor.ApplyRestrictions(context);
        var options = context.ValidationOptions;
        if (options is { AllowedMaxSortFields: > 0 } && fields.Count > options.AllowedMaxSortFields)
            result.AddError($"Sort has {fields.Count} fields which exceeds the allowed maximum of {options.AllowedMaxSortFields}.");

        result.ThrowIfInvalid();
        return fields;
    }

    /// <summary>
    /// Asynchronously parses and processes a sort expression, running the resolution phase first.
    /// </summary>
    protected async ValueTask<List<SortField>> ProcessSortAsync(string sort, TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sort);
        BeginExpression(context, QueryType.Sort);
        var parsed = LuceneQuery.Parse(sort, context.ParserOptions);
        if (parsed.IsSuccess)
        {
            var includeDocuments = await ResolveIncludesAsync(parsed.Document, QueryType.Sort, context, cancellationToken).ConfigureAwait(false);
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in includeDocuments.Prepend(parsed.Document))
                foreach (var field in SortExpression.FromDocument(document, new QueryValidationResult()))
                    fields.Add(field.OriginalField);

            await ResolveFieldsAsync(fields, context, cancellationToken).ConfigureAwait(false);
            await OnResolveAsync(QueryType.Sort, parsed.Document, ResolveFields(fields, context), context, cancellationToken).ConfigureAwait(false);
        }

        context.IsResolved = true;
        return ProcessSort(sort, context);
    }

    /// <summary>
    /// Parses and processes an aggregation expression. Throws when the expression is invalid.
    /// </summary>
    protected List<AggregationExpression> ProcessAggregations(string aggregations, TContext context)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        var result = BeginExpression(context, QueryType.Aggregation);
        EnsureResolved(context);

        var document = ParseExpression(aggregations, context);
        var expressions = AggregationExpressionParser.FromDocument(document, result);
        ResolveAggregationFields(expressions, context, result);
        ValidationVisitor.ApplyRestrictions(context);

        result.ThrowIfInvalid();
        return expressions;
    }

    /// <summary>
    /// Asynchronously parses and processes an aggregation expression, running the resolution phase first.
    /// </summary>
    protected async ValueTask<List<AggregationExpression>> ProcessAggregationsAsync(string aggregations, TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        BeginExpression(context, QueryType.Aggregation);
        var parsed = LuceneQuery.Parse(aggregations, context.ParserOptions);
        if (parsed.IsSuccess)
        {
            var includeDocuments = await ResolveIncludesAsync(parsed.Document, QueryType.Aggregation, context, cancellationToken).ConfigureAwait(false);
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in includeDocuments.Prepend(parsed.Document))
                CollectAggregationFields(AggregationExpressionParser.FromDocument(document, new QueryValidationResult()), fields);

            await ResolveFieldsAsync(fields, context, cancellationToken).ConfigureAwait(false);
            await OnResolveAsync(QueryType.Aggregation, parsed.Document, ResolveFields(fields, context), context, cancellationToken).ConfigureAwait(false);
        }

        context.IsResolved = true;
        return ProcessAggregations(aggregations, context);
    }

    private static QueryValidationResult BeginExpression(TContext context, QueryType type)
    {
        context.QueryType = type;
        var result = context.ValidationResult;
        result.QueryType = type;
        return result;
    }

    private QueryDocument ParseExpression(string text, TContext context)
    {
        var result = context.ValidationResult;
        var parsed = LuceneQuery.Parse(text, context.ParserOptions);
        foreach (var error in parsed.Errors)
            result.AddError(error.Message, error.Position, error.Code);

        result.ThrowIfInvalid();
        var document = IncludeVisitor.Instance.Accept(parsed.Document, context);
        var visitor = context.QueryType == QueryType.Sort ? BaseConfiguration.SortVisitor : BaseConfiguration.AggregationVisitor;
        return visitor.Accept(document, context) as QueryDocument
            ?? throw new InvalidOperationException("A visitor replaced the expression document with a different node type.");
    }

    private static void ResolveAggregationFields(List<AggregationExpression> aggregations, TContext context, QueryValidationResult result)
    {
        foreach (var aggregation in aggregations)
        {
            result.AddOperation(aggregation.Type, aggregation.OriginalField);
            if (aggregation.OriginalField.Length > 0 && aggregation.OriginalField != "_")
            {
                aggregation.Field = ResolveField(aggregation.OriginalField, context);
                result.ReferencedFields.Add(aggregation.OriginalField);
                result.ResolvedFields.Add(aggregation.Field);
            }

            ResolveAggregationFields(aggregation.Aggregations, context, result);
        }
    }

    private static void CollectAggregationFields(List<AggregationExpression> aggregations, HashSet<string> fields)
    {
        foreach (var aggregation in aggregations)
        {
            if (aggregation.OriginalField.Length > 0 && aggregation.OriginalField != "_")
                fields.Add(aggregation.OriginalField);
            CollectAggregationFields(aggregation.Aggregations, fields);
        }
    }

    private static string ResolveField(string field, TContext context)
    {
        if (!FieldResolverQueryVisitor.TryResolveField(field, context, out string resolved))
            context.ValidationResult.UnresolvedFields.Add(field);

        return resolved;
    }

    private static Dictionary<string, string> ResolveFields(IEnumerable<string> fields, TContext context)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in fields)
        {
            string? name;
            try
            {
                name = context.FieldResolver?.Invoke(field, context);
            }
            catch (Exception)
            {
                // Resolver failures are reported when the pipeline resolves the field.
                name = null;
            }

            resolved[field] = name ?? context.FieldMap?.ResolveField(field) ?? field;
        }

        return resolved;
    }

    /// <summary>
    /// Runs the asynchronous resolution phase for a query: fetches includes, resolves field names with the async
    /// field resolver, then calls <see cref="OnResolveAsync"/> with the resolved field names.
    /// </summary>
    protected async ValueTask ResolveQueryAsync(QueryDocument document, TContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        BeginExpression(context, QueryType.Query);

        var includeDocuments = await ResolveIncludesAsync(document, QueryType.Query, context, cancellationToken).ConfigureAwait(false);
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in includeDocuments.Prepend(document))
        {
            foreach (string field in root.GetReferencedFields())
                fields.Add(field);
        }

        int includeExpansions = 0;
        if (context.DefaultFields is { Length: > 0 } defaultFields && UsesDefaultFields(document, context, hasField: false, depth: 0, ref includeExpansions))
            fields.UnionWith(defaultFields);

        await ResolveFieldsAsync(fields, context, cancellationToken).ConfigureAwait(false);
        await OnResolveAsync(QueryType.Query, document, ResolveFields(fields, context), context, cancellationToken).ConfigureAwait(false);
        context.IsResolved = true;
    }

    /// <summary>
    /// Provider hook for asynchronous lookups (for example loading index mappings) that run after includes are
    /// fetched and field names are resolved. <see cref="IQueryVisitorContext.Includes"/> holds the fetched includes.
    /// </summary>
    /// <param name="type">The kind of expression being resolved.</param>
    /// <param name="document">The parsed expression, before includes are expanded. Do not modify it.</param>
    /// <param name="fields">The fields the expression and its includes reference, including used default fields, as written, mapped to their resolved names.</param>
    /// <param name="context">The context to store resolved data on.</param>
    /// <param name="cancellationToken">A token to cancel the lookups.</param>
    protected virtual ValueTask OnResolveAsync(QueryType type, QueryDocument document, IReadOnlyDictionary<string, string> fields, TContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Whether the synchronous build methods can be used with this context. Providers override this to require the
    /// resolution phase when they have asynchronous dependencies of their own; <see cref="IQueryVisitorContext.QueryType"/>
    /// is set to the kind of expression being built.
    /// </summary>
    protected virtual bool RequiresResolution(TContext context)
    {
        return context.IncludeResolver is not null || context.AsyncFieldResolver is not null;
    }

    private void EnsureResolved(TContext context)
    {
        if (!context.IsResolved && RequiresResolution(context))
            throw new InvalidOperationException("The parser is configured with asynchronous resolvers. Use the Async build methods so they can run before the query is built.");
    }

    private static async ValueTask<List<QueryDocument>> ResolveIncludesAsync(QueryDocument document, QueryType type, TContext context, CancellationToken cancellationToken)
    {
        var documents = new List<QueryDocument>();
        var resolver = context.IncludeResolver;
        if (resolver is null && context.Includes is null)
            return documents;

        var options = context.ValidationOptions ?? QueryValidationOptions.Default;
        var includes = context.Includes is { } existing
            ? new Dictionary<string, string>(existing.ToDictionary(p => p.Key, p => p.Value), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<string>();
        CollectIncludeNames(document, type, context, pending);

        int fetched = 0;
        for (int depth = 0; depth < options.MaxIncludeDepth && pending.Count > 0; depth++)
        {
            var level = pending.Where(seen.Add).ToList();
            pending = [];

            var missing = resolver is null ? [] : level.Where(name => !includes.ContainsKey(name)).ToList();
            fetched += missing.Count;
            if (fetched > options.MaxIncludeExpansions)
                break;

            if (resolver is not null && missing.Count > 0)
            {
                var results = await Task.WhenAll(missing.Select(name => ResolveIncludeAsync(resolver, name, context, cancellationToken))).ConfigureAwait(false);
                foreach (var (name, text, error) in results)
                {
                    if (error is not null)
                        context.ValidationResult.AddError(error, code: QueryErrorCode.UnresolvedInclude);
                    else if (text is not null)
                        includes[name] = text;
                }
            }

            foreach (string name in level)
            {
                if (!includes.TryGetValue(name, out string? text) || string.IsNullOrWhiteSpace(text))
                    continue;

                var parsed = LuceneQuery.Parse(text, context.ParserOptions);
                documents.Add(parsed.Document);
                CollectIncludeNames(parsed.Document, type, context, pending);
            }
        }

        context.Includes = includes;

        return documents;
    }

    private static async Task<(string Name, string? Text, string? Error)> ResolveIncludeAsync(IncludeResolver resolver, string name, TContext context, CancellationToken cancellationToken)
    {
        try
        {
            return (name, await resolver(name, context, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (name, null, $"Error in include resolver callback when resolving include ({name}): {ex.Message}");
        }
    }

    private static void CollectIncludeNames(QueryNode root, QueryType type, TContext context, List<string> names)
    {
        var stack = new Stack<QueryNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            switch (node)
            {
                case FieldQueryNode field when IncludeVisitor.IsInclude(field):
                    if (context.ShouldSkipInclude?.Invoke(field, context) != true && IncludeVisitor.GetIncludeName(field) is { Length: > 0 } name)
                        names.Add(name);
                    break;
                case FieldQueryNode { Query: { } query } when type != QueryType.Aggregation:
                    stack.Push(query);
                    break;
                case QueryDocument { Query: { } query }:
                    stack.Push(query);
                    break;
                case GroupNode { Query: { } query }:
                    stack.Push(query);
                    break;
                case NotNode { Query: { } query }:
                    stack.Push(query);
                    break;
                case BooleanQueryNode boolean:
                    for (int i = boolean.Clauses.Count - 1; i >= 0; i--)
                    {
                        if (boolean.Clauses[i].Query is { } clauseQuery)
                            stack.Push(clauseQuery);
                    }
                    break;
            }
        }
    }

    private static bool UsesDefaultFields(QueryNode? node, TContext context, bool hasField, int depth, ref int includeExpansions)
    {
        switch (node)
        {
            case QueryDocument document:
                return UsesDefaultFields(document.Query, context, hasField, depth, ref includeExpansions);
            case GroupNode group:
                return UsesDefaultFields(group.Query, context, hasField, depth, ref includeExpansions);
            case NotNode not:
                return UsesDefaultFields(not.Query, context, hasField, depth, ref includeExpansions);
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                {
                    if (UsesDefaultFields(clause.Query, context, hasField, depth, ref includeExpansions))
                        return true;
                }
                return false;
            case FieldQueryNode field when IncludeVisitor.IsInclude(field):
                if (context.ShouldSkipInclude?.Invoke(field, context) == true
                    || depth >= (context.ValidationOptions ?? QueryValidationOptions.Default).MaxIncludeDepth
                    || IncludeVisitor.GetIncludeName(field) is not { } name
                    || context.Includes?.TryGetValue(name, out string? text) != true
                    || string.IsNullOrWhiteSpace(text)
                    || ++includeExpansions > (context.ValidationOptions ?? QueryValidationOptions.Default).MaxIncludeExpansions)
                    return false;

                return UsesDefaultFields(LuceneQuery.Parse(text, context.ParserOptions).Document, context, hasField, depth + 1, ref includeExpansions);
            case FieldQueryNode field:
                return UsesDefaultFields(field.Query, context, hasField: true, depth, ref includeExpansions);
            case TermNode or PhraseNode or RegexNode or RangeNode:
                return !hasField;
            default:
                return false;
        }
    }

    private static async ValueTask ResolveFieldsAsync(IEnumerable<string> fields, TContext context, CancellationToken cancellationToken)
    {
        var resolver = context.AsyncFieldResolver;
        if (resolver is null)
            return;

        var names = fields.Where(f => f.Length > 0 && f[0] != '@').Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = await Task.WhenAll(names.Select(async name =>
        {
            try
            {
                return (Name: name, Resolved: await resolver(name, context, cancellationToken).ConfigureAwait(false), Error: (string?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (Name: name, Resolved: (string?)null, Error: $"Error in field resolver callback when resolving field ({name}): {ex.Message}");
            }
        })).ConfigureAwait(false);

        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value, error) in results)
        {
            if (error is not null)
                context.ValidationResult.AddError(error, code: QueryErrorCode.UnresolvedField);

            resolved[name] = value;
        }

        var previous = context.FieldResolver;
        context.FieldResolver = (field, ctx) => resolved.TryGetValue(field, out string? value) && value is not null
            ? value
            : previous?.Invoke(field, ctx);
    }
}
