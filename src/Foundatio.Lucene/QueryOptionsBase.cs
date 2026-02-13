namespace Foundatio.Lucene;

/// <summary>
/// Base record for per-request query options.
/// These options are merged with the global parser configuration and can be cached by consuming applications (e.g., per-tenant).
/// </summary>
public abstract record QueryOptionsBase
{
    /// <summary>
    /// Field alias mappings to apply for this request. Overrides global FieldMap if provided.
    /// </summary>
    public FieldMap? FieldMap { get; init; }

    /// <summary>
    /// Pre-resolved includes dictionary mapping include names to their query content.
    /// Overrides global Includes if provided.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Includes { get; init; }

    /// <summary>
    /// Validation options for this request. Overrides global ValidationOptions if provided.
    /// </summary>
    public QueryValidationOptions? ValidationOptions { get; init; }

    /// <summary>
    /// Default fields to search when no field is specified. Overrides global DefaultFields if provided.
    /// </summary>
    public string[]? DefaultFields { get; init; }
}
