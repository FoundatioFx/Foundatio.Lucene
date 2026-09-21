namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Represents per-request options for building Elasticsearch queries.
/// These options are merged with the global parser configuration and can be cached by consuming applications (e.g., per-tenant).
/// </summary>
public sealed record ElasticsearchQueryOptions : QueryOptionsBase
{
    /// <summary>
    /// Whether to use scoring (query context) or filtering (filter context). Overrides global UseScoring if provided.
    /// </summary>
    public bool? UseScoring { get; init; }

    /// <summary>
    /// Default timezone for date range queries. Overrides global DefaultTimeZone if provided.
    /// </summary>
    public string? DefaultTimeZone { get; init; }

    /// <summary>
    /// Function to determine if a field is a date field. Overrides global IsDateField if provided.
    /// </summary>
    public Func<string, bool>? IsDateField { get; init; }

    /// <summary>
    /// Creates a new instance of ElasticsearchQueryOptions.
    /// </summary>
    public static ElasticsearchQueryOptions Empty { get; } = new();
}

/// <summary>
/// Builder for creating ElasticsearchQueryOptions with fluent configuration.
/// </summary>
public class ElasticsearchQueryOptionsBuilder
{
    private FieldMap? _fieldMap;
    private IReadOnlyDictionary<string, string>? _includes;
    private QueryValidationOptions? _validationOptions;
    private string[]? _defaultFields;
    private bool? _useScoring;
    private string? _defaultTimeZone;
    private Func<string, bool>? _isDateField;

    /// <summary>
    /// Sets the field map for alias resolution.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithFieldMap(FieldMap fieldMap)
    {
        _fieldMap = fieldMap;
        return this;
    }

    /// <summary>
    /// Sets the field map using a builder action.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithFieldMap(Action<FieldMapBuilder> configure)
    {
        var builder = FieldMapBuilder.Create();
        configure(builder);
        _fieldMap = builder.Build();
        return this;
    }

    /// <summary>
    /// Sets the includes dictionary.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithIncludes(IReadOnlyDictionary<string, string> includes)
    {
        _includes = includes;
        return this;
    }

    /// <summary>
    /// Sets the validation options.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithValidationOptions(QueryValidationOptions options)
    {
        _validationOptions = options;
        return this;
    }

    /// <summary>
    /// Sets the validation options using a configuration action.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithValidationOptions(Action<QueryValidationOptions> configure)
    {
        _validationOptions = new QueryValidationOptions();
        configure(_validationOptions);
        return this;
    }

    /// <summary>
    /// Sets the default fields to search when no field is specified.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithDefaultFields(params string[] fields)
    {
        _defaultFields = fields;
        return this;
    }

    /// <summary>
    /// Enables scoring (query context).
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithScoring()
    {
        _useScoring = true;
        return this;
    }

    /// <summary>
    /// Disables scoring (filter context).
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithoutScoring()
    {
        _useScoring = false;
        return this;
    }

    /// <summary>
    /// Sets whether to use scoring.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder UseScoring(bool useScoring)
    {
        _useScoring = useScoring;
        return this;
    }

    /// <summary>
    /// Sets the default timezone for date range queries.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithDefaultTimeZone(string timeZone)
    {
        _defaultTimeZone = timeZone;
        return this;
    }

    /// <summary>
    /// Sets the function to determine if a field is a date field.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithDateFields(Func<string, bool> isDateField)
    {
        _isDateField = isDateField;
        return this;
    }

    /// <summary>
    /// Sets the function to determine if a field is a date field using a set of field names.
    /// </summary>
    public ElasticsearchQueryOptionsBuilder WithDateFields(params string[] fieldNames)
    {
        var fieldSet = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);
        _isDateField = field => fieldSet.Contains(field);
        return this;
    }

    /// <summary>
    /// Builds the options.
    /// </summary>
    public ElasticsearchQueryOptions Build()
    {
        return new ElasticsearchQueryOptions
        {
            FieldMap = _fieldMap,
            Includes = _includes,
            ValidationOptions = _validationOptions,
            DefaultFields = _defaultFields,
            UseScoring = _useScoring,
            DefaultTimeZone = _defaultTimeZone,
            IsDateField = _isDateField
        };
    }
}
