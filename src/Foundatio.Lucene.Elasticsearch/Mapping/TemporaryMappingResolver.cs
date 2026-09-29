// TEMPORARY: minimal stand-in with the agreed API, replaced by the full port of Foundatio.Parsers'
// ElasticMappingResolver before merge.
#pragma warning disable CS1591
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

public sealed class FieldMapping
{
    public required string FullPath { get; init; }
    public IProperty? Property { get; init; }
    public bool Found => Property is not null;
    public IReadOnlyList<string> NestedPathChain { get; init; } = [];
    public string? NestedPath => NestedPathChain.Count > 0 ? NestedPathChain[^1] : null;
}

public sealed class ElasticMappingResolver : IDisposable
{
    private readonly Func<TypeMapping?> _getMapping;
    private TypeMapping? _mapping;
    private bool _loaded;

    private ElasticMappingResolver(Func<TypeMapping?> getMapping) => _getMapping = getMapping;

    public static ElasticMappingResolver NullInstance { get; } = new(() => null);

    public static ElasticMappingResolver Create(Func<TypeMapping?> getMapping) => new(getMapping);

    public static ElasticMappingResolver Create(TypeMapping mapping) => new(() => mapping);

    public bool IsLoaded => _loaded;

    public bool CanResolveSynchronously => true;

    public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        Load();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> EnsureFieldsAsync(IEnumerable<string> fields, CancellationToken cancellationToken = default)
    {
        Load();
        return ValueTask.FromResult(fields.All(f => GetMapping(f).Found));
    }

    private void Load()
    {
        if (_loaded)
            return;
        _mapping = _getMapping();
        _loaded = true;
    }

    public FieldMapping GetMapping(string field, bool followAlias = false)
    {
        Load();
        var properties = _mapping?.Properties;
        var parts = field.Split('.');
        var path = new List<string>();
        var nested = new List<string>();
        IProperty? property = null;
        for (int i = 0; i < parts.Length; i++)
        {
            if (properties is null)
            {
                path.AddRange(parts[i..]);
                property = null;
                break;
            }

            var match = properties.FirstOrDefault(p => string.Equals(p.Key.Name, parts[i], StringComparison.Ordinal));
            if (match.Value is null)
                match = properties.FirstOrDefault(p => string.Equals(p.Key.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (match.Value is null)
            {
                path.AddRange(parts[i..]);
                property = null;
                break;
            }

            path.Add(match.Key.Name!);
            property = match.Value;
            if (property is NestedProperty)
                nested.Add(string.Join('.', path));
            properties = property switch
            {
                ObjectProperty o => o.Properties,
                NestedProperty n => n.Properties,
                TextProperty t => t.Fields,
                KeywordProperty k => k.Fields,
                _ => null
            };
        }

        if (followAlias && property is FieldAliasProperty alias && alias.Path is not null)
            return GetMapping(alias.Path.Name!, followAlias: false);

        return new FieldMapping { FullPath = string.Join('.', path), Property = property, NestedPathChain = nested };
    }

    public string GetResolvedField(string field) => GetMapping(field).FullPath;

    public string GetSortFieldName(string field) => GetNonAnalyzedFieldName(field, "sort");

    public string GetAggregationsFieldName(string field) => GetNonAnalyzedFieldName(field, "keyword");

    public string GetNonAnalyzedFieldName(string field, string? preferredSubField = null)
    {
        var mapping = GetMapping(field);
        if (!mapping.Found || !IsPropertyAnalyzed(mapping.Property))
            return mapping.Found ? mapping.FullPath : field;

        var fields = (mapping.Property as TextProperty)?.Fields;
        if (fields is null)
            return mapping.FullPath;

        if (preferredSubField is not null && fields.FirstOrDefault(f => f.Key.Name == preferredSubField).Value is { } preferred && !IsPropertyAnalyzed(preferred))
            return $"{mapping.FullPath}.{preferredSubField}";

        var first = fields.FirstOrDefault(f => !IsPropertyAnalyzed(f.Value));
        return first.Value is null ? mapping.FullPath : $"{mapping.FullPath}.{first.Key.Name}";
    }

    public bool IsPropertyAnalyzed(IProperty? property) => property is TextProperty { Index: null or true };

    public static FieldType GetFieldType(IProperty? property)
    {
        if (property?.Type is not { } type)
            return FieldType.None;

        return type switch
        {
            "text" => FieldType.Text,
            "keyword" => FieldType.Keyword,
            "integer" => FieldType.Integer,
            "long" or "unsigned_long" => FieldType.Long,
            "short" => FieldType.Short,
            "byte" => FieldType.Byte,
            "double" => FieldType.Double,
            "float" => FieldType.Float,
            "half_float" => FieldType.HalfFloat,
            "scaled_float" => FieldType.ScaledFloat,
            "boolean" => FieldType.Boolean,
            "date" => FieldType.Date,
            "date_nanos" => FieldType.DateNanos,
            "geo_point" => FieldType.GeoPoint,
            "nested" => FieldType.Nested,
            "object" => FieldType.Object,
            "alias" => FieldType.Alias,
            "ip" => FieldType.Ip,
            "token_count" => FieldType.TokenCount,
            "match_only_text" => FieldType.MatchOnlyText,
            _ => FieldType.None
        };
    }

    public void Dispose()
    {
    }
}
