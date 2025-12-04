using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Delegate for resolving field names to their actual field names.
/// </summary>
/// <param name="field">The field name to resolve.</param>
/// <param name="context">The visitor context.</param>
/// <returns>The resolved field name, or null if not resolved.</returns>
public delegate Task<string?> QueryFieldResolver(string field, IQueryVisitorContext context);

/// <summary>
/// Delegate for resolving an include by name.
/// </summary>
/// <param name="name">The name of the include to resolve.</param>
/// <returns>The query string for the include, or null if not found.</returns>
public delegate Task<string?> IncludeResolver(string name);

/// <summary>
/// Delegate for determining whether an include should be skipped.
/// </summary>
/// <param name="node">The FieldQueryNode representing the include.</param>
/// <param name="context">The visitor context.</param>
/// <returns>True to skip the include, false to process it.</returns>
public delegate bool ShouldSkipIncludeFunc(FieldQueryNode node, IQueryVisitorContext context);

/// <summary>
/// Interface for contexts that support include resolution.
/// </summary>
public interface IQueryVisitorContextWithIncludeResolver : IQueryVisitorContext
{
    /// <summary>
    /// The resolver used to look up include definitions.
    /// </summary>
    IncludeResolver? IncludeResolver { get; set; }
}

/// <summary>
/// Interface for contexts that support field resolution.
/// </summary>
public interface IQueryVisitorContextWithFieldResolver : IQueryVisitorContext
{
    /// <summary>
    /// The resolver used to resolve field names.
    /// </summary>
    QueryFieldResolver? FieldResolver { get; set; }
}

/// <summary>
/// Extension methods for query validation on IQueryVisitorContext.
/// </summary>
public static class QueryValidationContextExtensions
{
    private const string ValidationOptionsKey = "@ValidationOptions";
    private const string ValidationResultKey = "@ValidationResult";
    private const string IncludeResolverKey = "@IncludeResolver";
    private const string ShouldSkipIncludeFuncKey = "@ShouldSkipIncludeFunc";
    private const string IncludeStackKey = "@IncludeStack";

    /// <summary>
    /// Gets or creates the validation options from the context.
    /// </summary>
    public static QueryValidationOptions GetValidationOptions(this IQueryVisitorContext context)
    {
        var options = context.GetValue<QueryValidationOptions>(ValidationOptionsKey);
        if (options is null)
        {
            options = new QueryValidationOptions();
            context.SetValue(ValidationOptionsKey, options);
        }
        return options;
    }

    /// <summary>
    /// Sets the validation options in the context.
    /// </summary>
    public static T SetValidationOptions<T>(this T context, QueryValidationOptions options) where T : IQueryVisitorContext
    {
        context.SetValue(ValidationOptionsKey, options);
        return context;
    }

    /// <summary>
    /// Checks if validation options have been set.
    /// </summary>
    public static bool HasValidationOptions(this IQueryVisitorContext context)
    {
        return context.Data.ContainsKey(ValidationOptionsKey);
    }

    /// <summary>
    /// Gets or creates the validation result from the context.
    /// </summary>
    public static QueryValidationResult GetValidationResult(this IQueryVisitorContext context)
    {
        var result = context.GetValue<QueryValidationResult>(ValidationResultKey);
        if (result is null)
        {
            result = new QueryValidationResult();
            context.SetValue(ValidationResultKey, result);
        }
        return result;
    }

    /// <summary>
    /// Adds a validation error to the context.
    /// </summary>
    public static void AddValidationError(this IQueryVisitorContext context, string message, int index = -1)
    {
        context.GetValidationResult().ValidationErrors.Add(new QueryValidationError(message, index));
    }

    /// <summary>
    /// Checks if the validation result is valid.
    /// </summary>
    public static bool IsValid(this IQueryVisitorContext context)
    {
        return context.GetValidationResult().IsValid;
    }

    /// <summary>
    /// Gets all validation errors from the context.
    /// </summary>
    public static ICollection<QueryValidationError> GetValidationErrors(this IQueryVisitorContext context)
    {
        return context.GetValidationResult().ValidationErrors;
    }

    /// <summary>
    /// Gets the validation message from the context.
    /// </summary>
    public static string GetValidationMessage(this IQueryVisitorContext context)
    {
        return context.GetValidationResult().Message;
    }

    /// <summary>
    /// Throws a QueryValidationException if the validation result is invalid.
    /// </summary>
    public static void ThrowIfInvalid(this IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        if (!result.IsValid)
            throw new QueryValidationException($"Invalid query: {result.Message}", result);
    }

    #region Include Resolver Extensions

    /// <summary>
    /// Gets the include resolver from the context.
    /// </summary>
    public static IncludeResolver? GetIncludeResolver(this IQueryVisitorContext context)
    {
        if (context is IQueryVisitorContextWithIncludeResolver typedContext)
            return typedContext.IncludeResolver;

        return context.GetValue<IncludeResolver>(IncludeResolverKey);
    }

    /// <summary>
    /// Sets the include resolver in the context.
    /// </summary>
    public static T SetIncludeResolver<T>(this T context, IncludeResolver? resolver) where T : IQueryVisitorContext
    {
        if (context is IQueryVisitorContextWithIncludeResolver typedContext)
            typedContext.IncludeResolver = resolver;
        else
            context.SetValue(IncludeResolverKey, resolver);

        return context;
    }

    /// <summary>
    /// Gets the should skip include function from the context.
    /// </summary>
    public static ShouldSkipIncludeFunc? GetShouldSkipIncludeFunc(this IQueryVisitorContext context)
    {
        return context.GetValue<ShouldSkipIncludeFunc>(ShouldSkipIncludeFuncKey);
    }

    /// <summary>
    /// Sets the should skip include function in the context.
    /// </summary>
    public static T SetShouldSkipIncludeFunc<T>(this T context, ShouldSkipIncludeFunc? func) where T : IQueryVisitorContext
    {
        context.SetValue(ShouldSkipIncludeFuncKey, func);
        return context;
    }

    /// <summary>
    /// Gets or creates the include stack for tracking recursive includes.
    /// </summary>
    public static Stack<string> GetIncludeStack(this IQueryVisitorContext context)
    {
        var stack = context.GetValue<Stack<string>>(IncludeStackKey);
        if (stack is null)
        {
            stack = new Stack<string>();
            context.SetValue(IncludeStackKey, stack);
        }
        return stack;
    }

    /// <summary>
    /// Checks if an include is already in the include stack (recursive).
    /// </summary>
    public static bool IsIncludeInStack(this IQueryVisitorContext context, string includeName)
    {
        return context.GetIncludeStack().Contains(includeName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pushes an include onto the stack.
    /// </summary>
    public static void PushInclude(this IQueryVisitorContext context, string includeName)
    {
        context.GetIncludeStack().Push(includeName);
    }

    /// <summary>
    /// Pops an include from the stack.
    /// </summary>
    public static void PopInclude(this IQueryVisitorContext context)
    {
        var stack = context.GetIncludeStack();
        if (stack.Count > 0)
            stack.Pop();
    }

    #endregion

    #region Field Resolver Extensions

    private const string FieldResolverKey = "@FieldResolver";
    private const string OriginalFieldKey = "@OriginalField";

    /// <summary>
    /// Gets the field resolver from the context.
    /// </summary>
    public static QueryFieldResolver? GetFieldResolver(this IQueryVisitorContext context)
    {
        if (context is IQueryVisitorContextWithFieldResolver typedContext)
            return typedContext.FieldResolver;

        return context.GetValue<QueryFieldResolver>(FieldResolverKey);
    }

    /// <summary>
    /// Sets the field resolver in the context.
    /// </summary>
    public static T SetFieldResolver<T>(this T context, QueryFieldResolver? resolver) where T : IQueryVisitorContext
    {
        if (context is IQueryVisitorContextWithFieldResolver typedContext)
            typedContext.FieldResolver = resolver;
        else
            context.SetValue(FieldResolverKey, resolver);

        return context;
    }

    /// <summary>
    /// Sets a synchronous field resolver in the context.
    /// </summary>
    public static T SetFieldResolver<T>(this T context, Func<string, string?>? resolver) where T : IQueryVisitorContext
    {
        if (resolver is null)
        {
            return context.SetFieldResolver((QueryFieldResolver?)null);
        }

        return context.SetFieldResolver((field, _) => Task.FromResult(resolver(field)));
    }

    /// <summary>
    /// Gets the original field name before resolution.
    /// </summary>
    public static string? GetOriginalField(this FieldQueryNode node, IQueryVisitorContext context)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        return context.GetValue<string>(key);
    }

    /// <summary>
    /// Sets the original field name before resolution.
    /// </summary>
    public static void SetOriginalField(this FieldQueryNode node, IQueryVisitorContext context, string originalField)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        context.SetValue(key, originalField);
    }

    /// <summary>
    /// Gets the original field name before resolution.
    /// </summary>
    public static string? GetOriginalField(this ExistsNode node, IQueryVisitorContext context)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        return context.GetValue<string>(key);
    }

    /// <summary>
    /// Sets the original field name before resolution.
    /// </summary>
    public static void SetOriginalField(this ExistsNode node, IQueryVisitorContext context, string originalField)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        context.SetValue(key, originalField);
    }

    /// <summary>
    /// Gets the original field name before resolution.
    /// </summary>
    public static string? GetOriginalField(this MissingNode node, IQueryVisitorContext context)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        return context.GetValue<string>(key);
    }

    /// <summary>
    /// Sets the original field name before resolution.
    /// </summary>
    public static void SetOriginalField(this MissingNode node, IQueryVisitorContext context, string originalField)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        context.SetValue(key, originalField);
    }

    /// <summary>
    /// Gets the original field name before resolution.
    /// </summary>
    public static string? GetOriginalField(this RangeNode node, IQueryVisitorContext context)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        return context.GetValue<string>(key);
    }

    /// <summary>
    /// Sets the original field name before resolution.
    /// </summary>
    public static void SetOriginalField(this RangeNode node, IQueryVisitorContext context, string originalField)
    {
        var key = $"{OriginalFieldKey}:{node.GetHashCode()}";
        context.SetValue(key, originalField);
    }

    #endregion
}
