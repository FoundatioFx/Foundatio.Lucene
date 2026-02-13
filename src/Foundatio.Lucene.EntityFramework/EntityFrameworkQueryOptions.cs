namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Represents per-request options for building Entity Framework filter expressions.
/// These options are merged with the global parser configuration and can be cached by consuming applications (e.g., per-tenant).
/// </summary>
public sealed record EntityFrameworkQueryOptions : QueryOptionsBase
{
    /// <summary>
    /// Additional fields to include for this request, merged with global field discovery.
    /// </summary>
    public IReadOnlyList<EntityFieldInfo>? AdditionalFields { get; init; }

    /// <summary>
    /// Custom data to attach to specific fields for this request.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? FieldData { get; init; }

    /// <summary>
    /// Custom field expression builder for this request. Overrides global builder if provided.
    /// </summary>
    public CustomFieldExpressionBuilder? CustomFieldExpressionBuilder { get; init; }

    /// <summary>
    /// Creates a new instance of EntityFrameworkQueryOptions.
    /// </summary>
    public static EntityFrameworkQueryOptions Empty { get; } = new();
}

/// <summary>
/// Builder for creating EntityFrameworkQueryOptions with fluent configuration.
/// </summary>
public class EntityFrameworkQueryOptionsBuilder
{
    private FieldMap? _fieldMap;
    private IReadOnlyDictionary<string, string>? _includes;
    private QueryValidationOptions? _validationOptions;
    private string[]? _defaultFields;
    private List<EntityFieldInfo>? _additionalFields;
    private Dictionary<string, object?>? _fieldData;
    private CustomFieldExpressionBuilder? _customFieldExpressionBuilder;

    /// <summary>
    /// Sets the field map for alias resolution.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldMap(FieldMap fieldMap)
    {
        _fieldMap = fieldMap;
        return this;
    }

    /// <summary>
    /// Sets the field map using a builder action.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldMap(Action<FieldMapBuilder> configure)
    {
        var builder = FieldMapBuilder.Create();
        configure(builder);
        _fieldMap = builder.Build();
        return this;
    }

    /// <summary>
    /// Sets the includes dictionary.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithIncludes(IReadOnlyDictionary<string, string> includes)
    {
        _includes = includes;
        return this;
    }

    /// <summary>
    /// Sets the validation options.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithValidationOptions(QueryValidationOptions options)
    {
        _validationOptions = options;
        return this;
    }

    /// <summary>
    /// Sets the validation options using a configuration action.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithValidationOptions(Action<QueryValidationOptions> configure)
    {
        _validationOptions = new QueryValidationOptions();
        configure(_validationOptions);
        return this;
    }

    /// <summary>
    /// Sets the default fields to search when no field is specified.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDefaultFields(params string[] fields)
    {
        _defaultFields = fields;
        return this;
    }

    /// <summary>
    /// Adds additional fields for this entity type.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithAdditionalFields(params EntityFieldInfo[] fields)
    {
        _additionalFields ??= [];
        _additionalFields.AddRange(fields);
        return this;
    }

    /// <summary>
    /// Adds additional fields for this entity type.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithAdditionalFields(IEnumerable<EntityFieldInfo> fields)
    {
        _additionalFields ??= [];
        _additionalFields.AddRange(fields);
        return this;
    }

    /// <summary>
    /// Adds a custom field with the specified properties.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithAdditionalField(string name, Type clrType, Action<EntityFieldInfo>? configure = null)
    {
        _additionalFields ??= [];
        var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var field = new EntityFieldInfo
        {
            Name = name,
            FullName = name,
            ClrType = clrType,
            IsNumber = EntityFrameworkQueryParser.IsNumericType(underlyingType),
            IsDate = underlyingType == typeof(DateTime),
            IsDateOnly = underlyingType == typeof(DateOnly),
            IsBoolean = underlyingType == typeof(bool),
            IsString = underlyingType == typeof(string)
        };
        configure?.Invoke(field);
        _additionalFields.Add(field);
        return this;
    }

    /// <summary>
    /// Adds a string field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithStringField(string name, Action<EntityFieldInfo>? configure = null)
    {
        return WithAdditionalField(name, typeof(string), configure);
    }

    /// <summary>
    /// Adds an integer field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithIntField(string name, Action<EntityFieldInfo>? configure = null)
    {
        return WithAdditionalField(name, typeof(int), configure);
    }

    /// <summary>
    /// Adds a decimal field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDecimalField(string name, Action<EntityFieldInfo>? configure = null)
    {
        return WithAdditionalField(name, typeof(decimal), configure);
    }

    /// <summary>
    /// Adds a boolean field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithBooleanField(string name, Action<EntityFieldInfo>? configure = null)
    {
        return WithAdditionalField(name, typeof(bool), configure);
    }

    /// <summary>
    /// Adds a DateTime field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDateTimeField(string name, Action<EntityFieldInfo>? configure = null)
    {
        return WithAdditionalField(name, typeof(DateTime), configure);
    }

    /// <summary>
    /// Attaches custom data to a field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldData(string fieldName, string key, object value)
    {
        _fieldData ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!_fieldData.TryGetValue(fieldName, out var existingData) || existingData is not Dictionary<string, object?> dict)
        {
            dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _fieldData[fieldName] = dict;
        }
        dict[key] = value;
        return this;
    }

    /// <summary>
    /// Sets a custom field expression builder.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithCustomFieldExpressionBuilder(CustomFieldExpressionBuilder builder)
    {
        _customFieldExpressionBuilder = builder;
        return this;
    }

    /// <summary>
    /// Builds the options.
    /// </summary>
    public EntityFrameworkQueryOptions Build()
    {
        return new EntityFrameworkQueryOptions
        {
            FieldMap = _fieldMap,
            Includes = _includes,
            ValidationOptions = _validationOptions,
            DefaultFields = _defaultFields,
            AdditionalFields = _additionalFields?.ToList(),
            FieldData = _fieldData,
            CustomFieldExpressionBuilder = _customFieldExpressionBuilder
        };
    }
}
