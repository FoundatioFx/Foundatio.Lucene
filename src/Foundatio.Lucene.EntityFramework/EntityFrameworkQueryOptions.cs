using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Per-request (or per-scope, such as per-tenant) options for <see cref="EntityFrameworkQueryParser"/>. They override
/// the options registered for the entity type with <see cref="EntityFrameworkQueryParser.SetOptions{TEntity}(EntityFrameworkQueryOptions)"/>,
/// which override the parser configuration. Instances are immutable and can be cached and shared.
/// </summary>
public sealed record EntityFrameworkQueryOptions : QueryOptionsBase
{
    /// <summary>
    /// Options that override nothing.
    /// </summary>
    public static EntityFrameworkQueryOptions Empty { get; } = new();

    /// <summary>
    /// The EF Core model used to discover fields. The <c>DbSet</c> and <c>IQueryable</c> extension methods set it
    /// automatically.
    /// </summary>
    public IModel? Model { get; init; }

    /// <summary>
    /// Overrides how terms without a field are matched against string default fields.
    /// </summary>
    public SearchOperator? DefaultSearchOperator { get; init; }

    /// <summary>
    /// Overrides the time zone used for <c>now</c>, date math rounding, and dates written without an offset.
    /// </summary>
    public TimeZoneInfo? DefaultTimeZone { get; init; }

    /// <summary>
    /// Custom fields (for example dynamic or EAV fields) that can be queried in addition to the model's fields. A
    /// custom field with the same name as a model field replaces it. Queries on custom fields need a
    /// <see cref="CustomFieldExpressionBuilder"/>. Registered and per-request fields are combined.
    /// </summary>
    public IReadOnlyList<EntityFieldInfo>? AdditionalFields { get; init; }

    /// <summary>
    /// Overrides the builder for custom field expressions.
    /// </summary>
    public CustomFieldExpressionBuilder? CustomFieldExpressionBuilder { get; init; }

    /// <summary>
    /// Creates a builder for options.
    /// </summary>
    public static EntityFrameworkQueryOptionsBuilder CreateBuilder() => new();
}

/// <summary>
/// Fluent builder for <see cref="EntityFrameworkQueryOptions"/>.
/// </summary>
public class EntityFrameworkQueryOptionsBuilder
{
    private EntityFrameworkQueryOptions _options = EntityFrameworkQueryOptions.Empty;
    private List<EntityFieldInfo>? _additionalFields;

    /// <summary>
    /// Sets the field aliases.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldMap(FieldMap fieldMap)
    {
        _options = _options with { FieldMap = fieldMap };
        return this;
    }

    /// <summary>
    /// Sets the field aliases using a <see cref="FieldMapBuilder"/>.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldMap(Action<FieldMapBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = FieldMapBuilder.Create();
        configure(builder);
        return WithFieldMap(builder.Build());
    }

    /// <summary>
    /// Sets the synchronous field resolver.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithFieldResolver(QueryFieldResolver resolver)
    {
        _options = _options with { FieldResolver = resolver };
        return this;
    }

    /// <summary>
    /// Sets the asynchronous field resolver (requires the <c>Async</c> build methods).
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithAsyncFieldResolver(AsyncQueryFieldResolver resolver)
    {
        _options = _options with { AsyncFieldResolver = resolver };
        return this;
    }

    /// <summary>
    /// Sets the query text for <c>@include:name</c> references.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithIncludes(IReadOnlyDictionary<string, string> includes)
    {
        _options = _options with { Includes = includes };
        return this;
    }

    /// <summary>
    /// Sets the asynchronous include resolver (requires the <c>Async</c> build methods).
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithIncludeResolver(IncludeResolver resolver)
    {
        _options = _options with { IncludeResolver = resolver };
        return this;
    }

    /// <summary>
    /// Sets the validation rules.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithValidationOptions(QueryValidationOptions options)
    {
        _options = _options with { ValidationOptions = options };
        return this;
    }

    /// <summary>
    /// Sets the validation rules using a configuration action.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithValidationOptions(Action<QueryValidationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new QueryValidationOptions();
        configure(options);
        return WithValidationOptions(options);
    }

    /// <summary>
    /// Sets the operator used between clauses written without AND or OR.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDefaultOperator(BooleanOperator op)
    {
        _options = _options with { DefaultOperator = op };
        return this;
    }

    /// <summary>
    /// Sets the fields searched by terms that have no field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDefaultFields(params string[] fields)
    {
        _options = _options with { DefaultFields = fields };
        return this;
    }

    /// <summary>
    /// Sets how terms without a field are matched against string default fields.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDefaultSearchOperator(SearchOperator searchOperator)
    {
        _options = _options with { DefaultSearchOperator = searchOperator };
        return this;
    }

    /// <summary>
    /// Sets the time zone used for <c>now</c>, date math rounding, and dates written without an offset.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDefaultTimeZone(TimeZoneInfo timeZone)
    {
        _options = _options with { DefaultTimeZone = timeZone };
        return this;
    }

    /// <summary>
    /// Sets the EF Core model used to discover fields.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithModel(IModel model)
    {
        _options = _options with { Model = model };
        return this;
    }

    /// <summary>
    /// Sets the builder for custom field expressions.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithCustomFieldExpressionBuilder(CustomFieldExpressionBuilder builder)
    {
        _options = _options with { CustomFieldExpressionBuilder = builder };
        return this;
    }

    /// <summary>
    /// Adds custom fields.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithAdditionalFields(params IEnumerable<EntityFieldInfo> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _additionalFields ??= [];
        _additionalFields.AddRange(fields);
        return this;
    }

    /// <summary>
    /// Adds a custom field.
    /// </summary>
    /// <param name="name">The field name used in queries.</param>
    /// <param name="clrType">The type of the field's values, used to parse query values.</param>
    /// <param name="data">Application data for the custom field expression builder.</param>
    public EntityFrameworkQueryOptionsBuilder WithAdditionalField(string name, Type clrType, IReadOnlyDictionary<string, object?>? data = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(clrType);
        return WithAdditionalFields(new EntityFieldInfo
        {
            Name = name,
            ClrType = clrType,
            Data = data ?? EntityFieldInfo.EmptyData
        });
    }

    /// <summary>
    /// Adds a custom string field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithStringField(string name, IReadOnlyDictionary<string, object?>? data = null) => WithAdditionalField(name, typeof(string), data);

    /// <summary>
    /// Adds a custom integer field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithIntField(string name, IReadOnlyDictionary<string, object?>? data = null) => WithAdditionalField(name, typeof(int), data);

    /// <summary>
    /// Adds a custom decimal field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDecimalField(string name, IReadOnlyDictionary<string, object?>? data = null) => WithAdditionalField(name, typeof(decimal), data);

    /// <summary>
    /// Adds a custom boolean field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithBooleanField(string name, IReadOnlyDictionary<string, object?>? data = null) => WithAdditionalField(name, typeof(bool), data);

    /// <summary>
    /// Adds a custom date and time field.
    /// </summary>
    public EntityFrameworkQueryOptionsBuilder WithDateTimeField(string name, IReadOnlyDictionary<string, object?>? data = null) => WithAdditionalField(name, typeof(DateTime), data);

    /// <summary>
    /// Builds the options.
    /// </summary>
    public EntityFrameworkQueryOptions Build()
    {
        return _additionalFields is null ? _options : _options with { AdditionalFields = [.. _additionalFields] };
    }
}
