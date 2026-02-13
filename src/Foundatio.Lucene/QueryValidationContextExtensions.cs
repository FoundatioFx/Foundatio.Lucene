using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Delegate for determining whether an include should be skipped.
/// </summary>
/// <param name="node">The FieldQueryNode representing the include.</param>
/// <param name="context">The visitor context.</param>
/// <returns>True to skip the include, false to process it.</returns>
public delegate bool ShouldSkipIncludeFunc(FieldQueryNode node, IQueryVisitorContext context);

/// <summary>
/// Extension methods for query validation on IQueryVisitorContext.
/// </summary>
public static class QueryValidationContextExtensions
{
    private const string ValidationOptionsKey = "@ValidationOptions";
    private const string ValidationResultKey = "@ValidationResult";
    private const string IncludesKey = "@Includes";
    private const string ShouldSkipIncludeFuncKey = "@ShouldSkipIncludeFunc";
    private const string IncludeStackKey = "@IncludeStack";
    private const string FieldMapKey = "@FieldMap";
    private const string OriginalFieldKey = "@OriginalField";

    #region Validation Extensions

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

    #endregion

    #region Field Map Extensions

    /// <summary>
    /// Gets the field map from the context.
    /// </summary>
    public static FieldMap? GetFieldMap(this IQueryVisitorContext context)
    {
        return context.GetValue<FieldMap>(FieldMapKey);
    }

    /// <summary>
    /// Sets the field map in the context.
    /// </summary>
    public static T SetFieldMap<T>(this T context, FieldMap? fieldMap) where T : IQueryVisitorContext
    {
        context.SetValue(FieldMapKey, fieldMap);
        return context;
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

    #region Include Extensions

    /// <summary>
    /// Gets the pre-resolved includes dictionary from the context.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? GetIncludes(this IQueryVisitorContext context)
    {
        return context.GetValue<IReadOnlyDictionary<string, string>>(IncludesKey);
    }

    /// <summary>
    /// Sets the pre-resolved includes dictionary in the context.
    /// </summary>
    public static T SetIncludes<T>(this T context, IReadOnlyDictionary<string, string>? includes) where T : IQueryVisitorContext
    {
        context.SetValue(IncludesKey, includes);
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
}
