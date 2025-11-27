using System.Collections.Concurrent;
using System.Diagnostics;

namespace Foundatio.LuceneQueryParser;

/// <summary>
/// Options for query validation.
/// </summary>
public class QueryValidationOptions
{
    /// <summary>
    /// Whether to throw an exception when validation fails.
    /// </summary>
    public bool ShouldThrow { get; set; }

    /// <summary>
    /// Fields that are allowed in the query. If empty, all fields are allowed.
    /// </summary>
    public ICollection<string> AllowedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fields that are restricted from use in the query.
    /// </summary>
    public ICollection<string> RestrictedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to allow leading wildcards (e.g., *value or ?value).
    /// Default is true.
    /// </summary>
    public bool AllowLeadingWildcards { get; set; } = true;

    /// <summary>
    /// Maximum allowed nesting depth for groups. 0 means unlimited.
    /// </summary>
    public int AllowedMaxNodeDepth { get; set; }

    /// <summary>
    /// Operations that are allowed. If empty, all operations are allowed.
    /// </summary>
    public ICollection<string> AllowedOperations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Operations that are restricted from use.
    /// </summary>
    public ICollection<string> RestrictedOperations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of query validation.
/// </summary>
[DebuggerDisplay("IsValid: {IsValid} Message: {Message}")]
public class QueryValidationResult
{
    private readonly ConcurrentDictionary<string, ICollection<string>> _operations = new(StringComparer.OrdinalIgnoreCase);
    private int _currentNodeDepth = 1;

    /// <summary>
    /// Whether the query is valid.
    /// </summary>
    public bool IsValid => ValidationErrors.Count == 0;

    /// <summary>
    /// Collection of validation errors.
    /// </summary>
    public ICollection<QueryValidationError> ValidationErrors { get; } = new List<QueryValidationError>();

    /// <summary>
    /// Combined message of all validation errors.
    /// </summary>
    public string Message
    {
        get
        {
            if (ValidationErrors.Count == 0)
                return string.Empty;

            if (ValidationErrors.Count > 1)
                return string.Join("\r\n", ValidationErrors.Select(e => e.ToString()));

            return ValidationErrors.First().Message;
        }
    }

    /// <summary>
    /// All fields referenced in the query.
    /// </summary>
    public ICollection<string> ReferencedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All includes referenced in the query (@include:name).
    /// </summary>
    public ICollection<string> ReferencedIncludes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Includes that could not be resolved.
    /// </summary>
    public ICollection<string> UnresolvedIncludes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fields that could not be resolved by the field resolver.
    /// </summary>
    public ICollection<string> UnresolvedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The maximum nesting depth found in the query.
    /// </summary>
    public int MaxNodeDepth { get; set; } = 1;

    /// <summary>
    /// Current node depth during traversal (internal use).
    /// </summary>
    internal int CurrentNodeDepth
    {
        get => _currentNodeDepth;
        set
        {
            _currentNodeDepth = value;
            if (_currentNodeDepth > MaxNodeDepth)
                MaxNodeDepth = _currentNodeDepth;
        }
    }

    /// <summary>
    /// Operations used in the query, mapped to the fields they operate on.
    /// </summary>
    public IDictionary<string, ICollection<string>> Operations => _operations;

    /// <summary>
    /// Adds an operation to the result.
    /// </summary>
    internal void AddOperation(string operation, string? field)
    {
        if (string.IsNullOrEmpty(operation))
            return;

        _operations.AddOrUpdate(operation,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { field ?? string.Empty },
            (_, collection) =>
            {
                collection.Add(field ?? string.Empty);
                return collection;
            }
        );
    }

    /// <summary>
    /// Implicit conversion to bool based on IsValid.
    /// </summary>
    public static implicit operator bool(QueryValidationResult result) => result.IsValid;
}

/// <summary>
/// Represents a single validation error.
/// </summary>
public class QueryValidationError
{
    public QueryValidationError(string message, int index = -1)
    {
        Message = message;
        Index = index;
    }

    /// <summary>
    /// The validation error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Index where the validation error occurs in the query string.
    /// </summary>
    public int Index { get; } = -1;

    public override string ToString()
    {
        if (Index > 0)
            return $"[{Index}] {Message}";

        return Message;
    }
}

/// <summary>
/// Exception thrown when query validation fails.
/// </summary>
public class QueryValidationException : Exception
{
    public QueryValidationException(string message, QueryValidationResult? result = null, Exception? inner = null)
        : base(message, inner)
    {
        Result = result ?? new QueryValidationResult();
    }

    /// <summary>
    /// The validation result containing details about the failure.
    /// </summary>
    public QueryValidationResult Result { get; }

    /// <summary>
    /// The validation errors.
    /// </summary>
    public ICollection<QueryValidationError> Errors => Result.ValidationErrors;
}
