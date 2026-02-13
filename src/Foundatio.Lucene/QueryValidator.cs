using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Static class for validating Lucene queries.
/// </summary>
public static class QueryValidator
{
    /// <summary>
    /// Validates a query string.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult ValidateQuery(string query, QueryValidationOptions? options = null)
    {
        var context = new QueryVisitorContext();
        if (options is not null)
            context.SetValidationOptions(options);

        return InternalValidate(query, context);
    }

    /// <summary>
    /// Validates a query string and throws an exception if invalid.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="QueryValidationException">Thrown when the query is invalid.</exception>
    public static QueryValidationResult ValidateQueryAndThrow(string query, QueryValidationOptions? options = null)
    {
        options ??= new QueryValidationOptions();
        options.ShouldThrow = true;
        return ValidateQuery(query, options);
    }

    /// <summary>
    /// Validates a query string with a list of allowed fields.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="allowedFields">The fields that are allowed in the query.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult ValidateQuery(string query, IEnumerable<string> allowedFields)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return ValidateQuery(query, options);
    }

    /// <summary>
    /// Validates a parsed query document.
    /// </summary>
    /// <param name="document">The parsed query document.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Validate(QueryDocument document, QueryValidationOptions? options = null)
    {
        return ValidationVisitor.Run(document, options ?? new QueryValidationOptions());
    }

    /// <summary>
    /// Validates a query node.
    /// </summary>
    /// <param name="node">The query node to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Validate(QueryNode node, QueryValidationOptions? options = null)
    {
        return ValidationVisitor.Run(node, options ?? new QueryValidationOptions());
    }

    private static QueryValidationResult InternalValidate(string query, IQueryVisitorContext context)
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
                visitor.Accept(parseResult.Document, context);
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
    /// Validates the query document.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Validate(this QueryDocument document, QueryValidationOptions? options = null)
    {
        return QueryValidator.Validate(document, options);
    }

    /// <summary>
    /// Validates the query document with allowed fields.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="allowedFields">The fields that are allowed.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Validate(this QueryDocument document, IEnumerable<string> allowedFields)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return document.Validate(options);
    }

    /// <summary>
    /// Validates the query document and throws if invalid.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="QueryValidationException">Thrown when validation fails.</exception>
    public static QueryValidationResult ValidateAndThrow(this QueryDocument document, QueryValidationOptions? options = null)
    {
        options ??= new QueryValidationOptions();
        options.ShouldThrow = true;
        return document.Validate(options);
    }

    /// <summary>
    /// Validates the parse result.
    /// </summary>
    /// <param name="result">The parse result to validate.</param>
    /// <param name="options">Optional validation options.</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Validate(this LuceneParseResult result, QueryValidationOptions? options = null)
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
            visitor.Accept(result.Document, context);
            visitor.ApplyRestrictions(context);
        }

        return context.GetValidationResult();
    }
}
