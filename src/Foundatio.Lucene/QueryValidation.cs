using System.Diagnostics;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Rules that restrict what a query, sort, or aggregation expression may use.
/// Configure an instance once and share it; do not modify it while it is in use.
/// </summary>
public class QueryValidationOptions
{
    /// <summary>
    /// Whether the <c>Validate</c> APIs throw a <see cref="QueryValidationException"/> for invalid input.
    /// Building a query always throws for invalid input.
    /// </summary>
    public bool ShouldThrow { get; set; }

    /// <summary>
    /// Field names (as written in queries, before alias resolution) that may be used. Empty allows all fields.
    /// A name also allows its sub-fields, so <c>data</c> allows <c>data.age</c>.
    /// </summary>
    public ICollection<string> AllowedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Field names that may not be used, checked against both the name as written and the resolved name.
    /// A name also restricts its sub-fields, so <c>secret</c> restricts <c>secret.keyword</c>.
    /// </summary>
    public ICollection<string> RestrictedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether terms may start with a wildcard (<c>*foo</c>, <c>?oo</c>). Leading wildcards can be expensive.
    /// Defaults to true.
    /// </summary>
    public bool AllowLeadingWildcards { get; set; } = true;

    /// <summary>
    /// Whether fields that a field resolver could not resolve are allowed. Defaults to true.
    /// </summary>
    public bool AllowUnresolvedFields { get; set; } = true;

    /// <summary>
    /// Whether <c>@include</c> references that could not be resolved are allowed. Defaults to false.
    /// </summary>
    public bool AllowUnresolvedIncludes { get; set; }

    /// <summary>
    /// Operations that may be used. Empty allows all operations. Query operations are
    /// <see cref="QueryOperations"/> names; aggregation operations are aggregation types such as <c>terms</c>.
    /// </summary>
    public ICollection<string> AllowedOperations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Operations that may not be used.
    /// </summary>
    public ICollection<string> RestrictedOperations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The maximum nesting depth of parenthesized groups, or 0 for no limit.
    /// </summary>
    public int AllowedMaxNodeDepth { get; set; }

    /// <summary>
    /// The maximum number of fields a sort expression may contain, or 0 for no limit.
    /// </summary>
    public int AllowedMaxSortFields { get; set; }

    /// <summary>
    /// The maximum depth of nested <c>@include</c> references. Defaults to 10.
    /// </summary>
    public int MaxIncludeDepth { get; set; } = 10;

    /// <summary>
    /// The maximum number of <c>@include</c> expansions in one query. Bounds the size of the expanded query.
    /// Defaults to 100.
    /// </summary>
    public int MaxIncludeExpansions { get; set; } = 100;

    internal static QueryValidationOptions Default { get; } = new();
}

/// <summary>
/// Names of the operations recorded for query expressions.
/// </summary>
public static class QueryOperations
{
    /// <summary>A term match.</summary>
    public const string Term = "term";
    /// <summary>A quoted phrase.</summary>
    public const string Phrase = "phrase";
    /// <summary>A prefix match (<c>foo*</c>).</summary>
    public const string Prefix = "prefix";
    /// <summary>A wildcard match (<c>f?o*bar</c>).</summary>
    public const string Wildcard = "wildcard";
    /// <summary>A fuzzy match (<c>foo~</c>).</summary>
    public const string Fuzzy = "fuzzy";
    /// <summary>A regular expression (<c>/re/</c>).</summary>
    public const string Regex = "regex";
    /// <summary>A range.</summary>
    public const string Range = "range";
    /// <summary>A field-exists check.</summary>
    public const string Exists = "exists";
    /// <summary>A field-missing check.</summary>
    public const string Missing = "missing";
    /// <summary>Match all documents.</summary>
    public const string MatchAll = "match_all";
}

/// <summary>
/// The outcome of validating a query along with statistics about what it uses.
/// </summary>
[DebuggerDisplay("IsValid: {IsValid} Message: {Message}")]
public class QueryValidationResult
{
    private int _currentNodeDepth = 1;
    private List<QueryValidationError>? _validationErrors;
    private HashSet<string>? _referencedFields;
    private HashSet<string>? _resolvedFields;
    private HashSet<string>? _referencedIncludes;
    private HashSet<string>? _unresolvedFields;
    private HashSet<string>? _unresolvedIncludes;
    private Dictionary<string, ISet<string>>? _operations;

    /// <summary>
    /// The kind of expression that was validated.
    /// </summary>
    public QueryType QueryType { get; set; }

    /// <summary>
    /// Whether no validation errors were found.
    /// </summary>
    public bool IsValid => _validationErrors is not { Count: > 0 };

    /// <summary>
    /// The validation errors.
    /// </summary>
    public List<QueryValidationError> ValidationErrors => _validationErrors ??= [];

    /// <summary>
    /// A description of the errors, or an empty string when valid.
    /// </summary>
    public string Message => (_validationErrors?.Count ?? 0) switch
    {
        0 => string.Empty,
        1 => ValidationErrors[0].Message,
        _ => string.Join(Environment.NewLine, ValidationErrors.Select(e => e.ToString()))
    };

    /// <summary>
    /// Fields referenced by the query, as written (before alias resolution). Terms without a field reference
    /// the default fields.
    /// </summary>
    public ISet<string> ReferencedFields => _referencedFields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fields referenced by the query after alias resolution.
    /// </summary>
    public ISet<string> ResolvedFields => _resolvedFields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names of the <c>@include</c> references used by the query.
    /// </summary>
    public ISet<string> ReferencedIncludes => _referencedIncludes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fields that a field resolver could not resolve.
    /// </summary>
    public ISet<string> UnresolvedFields => _unresolvedFields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>@include</c> references that could not be resolved.
    /// </summary>
    public ISet<string> UnresolvedIncludes => _unresolvedIncludes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The operations used, each with the set of fields it was used on.
    /// </summary>
    public IDictionary<string, ISet<string>> Operations => _operations ??= new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);

    internal bool HasUnresolvedFields => _unresolvedFields is { Count: > 0 };

    internal bool HasUnresolvedIncludes => _unresolvedIncludes is { Count: > 0 };

    internal bool HasOperations => _operations is { Count: > 0 };

    /// <summary>
    /// The deepest nesting of parenthesized groups (1 when there are none).
    /// </summary>
    public int MaxNodeDepth { get; set; } = 1;

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
    /// Records that an operation was used on a field.
    /// </summary>
    public void AddOperation(string operation, string? field)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        if (!Operations.TryGetValue(operation, out var fields))
        {
            fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Operations[operation] = fields;
        }

        fields.Add(field ?? string.Empty);
    }

    /// <summary>
    /// Adds a validation error.
    /// </summary>
    public void AddError(string message, int position = -1, QueryErrorCode code = QueryErrorCode.ValidationError)
    {
        ValidationErrors.Add(new QueryValidationError(message, position, code));
    }

    /// <summary>
    /// Throws a <see cref="QueryValidationException"/> when there are validation errors.
    /// </summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new QueryValidationException($"Invalid {QueryType.ToString().ToLowerInvariant()}: {Message}", this);
    }

    /// <summary>
    /// Converts the result to a boolean indicating validity.
    /// </summary>
    public static implicit operator bool(QueryValidationResult result) => result.IsValid;
}

/// <summary>
/// A single validation error.
/// </summary>
/// <param name="Message">A description of the problem.</param>
/// <param name="Position">The 0-based position in the source text, or -1 when unknown.</param>
/// <param name="Code">The error classification.</param>
public sealed record QueryValidationError(string Message, int Position = -1, QueryErrorCode Code = QueryErrorCode.ValidationError)
{
    /// <inheritdoc/>
    public override string ToString() => Position >= 0 ? $"[{Position}] {Message}" : Message;
}
