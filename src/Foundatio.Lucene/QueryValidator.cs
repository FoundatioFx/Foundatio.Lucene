using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Static class for validating Lucene queries.
/// </summary>
public static class QueryValidator
{
    /// <summary>
    /// Validates a query string asynchronously.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateQueryAsync(string query, QueryValidationOptions? options = null)
    {
        var context = new QueryVisitorContext();
        if (options is not null)
            context.SetValidationOptions(options);

        return InternalValidateAsync(query, context);
    }

    /// <summary>
    /// Validates a query string asynchronously and throws an exception if invalid.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="QueryValidationException">Thrown when the query is invalid.</exception>
    public static Task<QueryValidationResult> ValidateQueryAndThrowAsync(string query, QueryValidationOptions? options = null)
    {
        options ??= new QueryValidationOptions();
        options.ShouldThrow = true;
        return ValidateQueryAsync(query, options);
    }

    /// <summary>
    /// Validates a query string asynchronously with a list of allowed fields.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="allowedFields">The fields that are allowed in the query.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateQueryAsync(string query, IEnumerable<string> allowedFields)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return ValidateQueryAsync(query, options);
    }

    /// <summary>
    /// Validates a parsed query document asynchronously.
    /// </summary>
    /// <param name="document">The parsed query document.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateAsync(QueryDocument document, QueryValidationOptions? options = null)
    {
        return ValidationVisitor.RunAsync(document, options ?? new QueryValidationOptions());
    }

    /// <summary>
    /// Validates a query node asynchronously.
    /// </summary>
    /// <param name="node">The query node to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateAsync(QueryNode node, QueryValidationOptions? options = null)
    {
        return ValidationVisitor.RunAsync(node, options ?? new QueryValidationOptions());
    }

    private static async Task<QueryValidationResult> InternalValidateAsync(string query, IQueryVisitorContext context)
    {
        try
        {
            var parseResult = LuceneQuery.Parse(query);

            if (!parseResult.IsSuccess)
            {
                foreach (var error in parseResult.Errors)
                {
                    context.AddValidationError(error.Message, error.Position);
                }
                return context.GetValidationResult();
            }

            if (parseResult.Document is not null)
            {
                var visitor = new ValidationVisitor();
                await visitor.AcceptAsync(parseResult.Document, context).ConfigureAwait(false);
                visitor.ApplyRestrictions(context);
            }

            return context.GetValidationResult();
        }
        catch (Exception ex)
        {
            context.AddValidationError(ex.Message);

            var options = context.GetValidationOptions();
            if (options.ShouldThrow)
            {
                throw new QueryValidationException(ex.Message, context.GetValidationResult(), ex);
            }

            return context.GetValidationResult();
        }
    }
}

/// <summary>
/// Extension methods for query validation.
/// </summary>
public static class QueryValidationExtensions
{
    /// <summary>
    /// Validates the query document asynchronously.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateAsync(this QueryDocument document, QueryValidationOptions? options = null)
    {
        return QueryValidator.ValidateAsync(document, options);
    }

    /// <summary>
    /// Validates the query document asynchronously with allowed fields.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="allowedFields">The fields that are allowed.</param>
    /// <returns>The validation result.</returns>
    public static Task<QueryValidationResult> ValidateAsync(this QueryDocument document, IEnumerable<string> allowedFields)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return document.ValidateAsync(options);
    }

    /// <summary>
    /// Validates the query document asynchronously and throws if invalid.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="QueryValidationException">Thrown when validation fails.</exception>
    public static Task<QueryValidationResult> ValidateAndThrowAsync(this QueryDocument document, QueryValidationOptions? options = null)
    {
        options ??= new QueryValidationOptions();
        options.ShouldThrow = true;
        return document.ValidateAsync(options);
    }

    /// <summary>
    /// Validates the parse result asynchronously.
    /// </summary>
    /// <param name="result">The parse result to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static async Task<QueryValidationResult> ValidateAsync(this LuceneParseResult result, QueryValidationOptions? options = null)
    {
        var context = new QueryVisitorContext();

        if (options is not null)
            context.SetValidationOptions(options);

        // Add parse errors as validation errors
        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                context.AddValidationError(error.Message, error.Position);
            }
        }

        // Validate the document if it exists
        if (result.Document is not null)
        {
            var visitor = new ValidationVisitor();
            await visitor.AcceptAsync(result.Document, context).ConfigureAwait(false);
            visitor.ApplyRestrictions(context);
        }

        return context.GetValidationResult();
    }
}
