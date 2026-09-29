using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Validates query, sort, and aggregation expressions without building them. Includes and field aliases on the
/// context are applied first, so the result reflects what would actually be queried.
/// </summary>
public static class QueryValidator
{
    /// <summary>
    /// Validates a query.
    /// </summary>
    /// <param name="query">The query text.</param>
    /// <param name="options">Validation rules, or null for the defaults (or the context's options).</param>
    /// <param name="context">Optional context carrying includes, a field map or resolver, and parser options.</param>
    public static QueryValidationResult ValidateQuery(string query, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        context = Prepare(context, options, QueryType.Query);

        var parsed = LuceneQuery.Parse(query, context.ParserOptions);
        AddParseErrors(parsed, context);
        if (context.ValidationResult.IsValid)
            Validate(parsed.Document, context);

        return Finish(context);
    }

    /// <summary>
    /// Validates a query, throwing a <see cref="QueryValidationException"/> when it is invalid.
    /// </summary>
    public static QueryValidationResult ValidateQueryAndThrow(string query, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        var result = ValidateQuery(query, options, context);
        result.ThrowIfInvalid();
        return result;
    }

    /// <summary>
    /// Validates a parsed query. The document is not modified.
    /// </summary>
    public static QueryValidationResult Validate(QueryDocument document, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        context = Prepare(context, options, QueryType.Query);
        Validate(document.CloneDocument(), context);
        return Finish(context);
    }

    /// <summary>
    /// Validates a sort expression such as <c>-created +name</c>.
    /// </summary>
    public static QueryValidationResult ValidateSort(string sort, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(sort);
        context = Prepare(context, options, QueryType.Sort);
        var result = context.ValidationResult;

        var parsed = LuceneQuery.Parse(sort, context.ParserOptions);
        AddParseErrors(parsed, context);
        if (result.IsValid)
        {
            var document = (QueryDocument)IncludeVisitor.Instance.Accept(parsed.Document, context);
            var fields = SortExpression.FromDocument(document, result);
            foreach (var field in fields)
                RecordField(field.OriginalField, context);

            if (options is { AllowedMaxSortFields: > 0 } && fields.Count > options.AllowedMaxSortFields)
                result.AddError($"Sort has {fields.Count} fields which exceeds the allowed maximum of {options.AllowedMaxSortFields}.");

            ValidationVisitor.ApplyRestrictions(context);
        }

        return Finish(context);
    }

    /// <summary>
    /// Validates an aggregation expression such as <c>terms:(status min:created)</c>.
    /// </summary>
    public static QueryValidationResult ValidateAggregations(string aggregations, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        context = Prepare(context, options, QueryType.Aggregation);
        var result = context.ValidationResult;

        var parsed = LuceneQuery.Parse(aggregations, context.ParserOptions);
        AddParseErrors(parsed, context);
        if (result.IsValid)
        {
            var document = (QueryDocument)IncludeVisitor.TopLevelInstance.Accept(parsed.Document, context);
            RecordAggregations(AggregationExpressionParser.FromDocument(document, result), context);
            ValidationVisitor.ApplyRestrictions(context);
        }

        return Finish(context);
    }

    private static void RecordAggregations(List<AggregationExpression> aggregations, IQueryVisitorContext context)
    {
        foreach (var aggregation in aggregations)
        {
            context.ValidationResult.AddOperation(aggregation.Type, aggregation.OriginalField);
            if (aggregation.OriginalField is { Length: > 0 } and not "_")
                RecordField(aggregation.OriginalField, context);

            RecordAggregations(aggregation.Aggregations, context);
        }
    }

    private static void RecordField(string field, IQueryVisitorContext context)
    {
        var result = context.ValidationResult;
        if (!FieldResolverQueryVisitor.TryResolveField(field, context, out string resolved))
            result.UnresolvedFields.Add(field);

        result.ReferencedFields.Add(field);
        result.ResolvedFields.Add(resolved);
    }

    private static void Validate(QueryDocument document, IQueryVisitorContext context)
    {
        var processed = IncludeVisitor.Instance.Accept(document, context);
        processed = FieldResolverQueryVisitor.Instance.Accept(processed, context);
        ValidationVisitor.Instance.Accept(processed, context);
    }

    private static IQueryVisitorContext Prepare(IQueryVisitorContext? context, QueryValidationOptions? options, QueryType type)
    {
        context ??= new QueryVisitorContext();
        if (options is not null)
            context.ValidationOptions = options;

        context.QueryType = type;
        context.ValidationResult.QueryType = type;
        return context;
    }

    private static void AddParseErrors(LuceneParseResult parsed, IQueryVisitorContext context)
    {
        foreach (var error in parsed.Errors)
            context.ValidationResult.AddError(error.Message, error.Position, error.Code);
    }

    private static QueryValidationResult Finish(IQueryVisitorContext context)
    {
        var result = context.ValidationResult;
        if (context.ValidationOptions is { ShouldThrow: true })
            result.ThrowIfInvalid();

        return result;
    }
}

/// <summary>
/// Validation extension methods.
/// </summary>
public static class QueryValidationExtensions
{
    /// <summary>
    /// Validates a parsed query. The document is not modified.
    /// </summary>
    public static QueryValidationResult Validate(this QueryDocument document, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        return QueryValidator.Validate(document, options, context);
    }

    /// <summary>
    /// Validates a parse result, including its syntax errors.
    /// </summary>
    public static QueryValidationResult Validate(this LuceneParseResult result, QueryValidationOptions? options = null, IQueryVisitorContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            return QueryValidator.Validate(result.Document, options, context);

        context ??= new QueryVisitorContext();
        if (options is not null)
            context.ValidationOptions = options;

        foreach (var error in result.Errors)
            context.ValidationResult.AddError(error.Message, error.Position, error.Code);

        return context.ValidationResult;
    }
}
