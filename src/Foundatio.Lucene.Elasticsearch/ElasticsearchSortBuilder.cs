using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Converts sort fields into Elasticsearch sort options.
/// </summary>
internal static class ElasticsearchSortBuilder
{
    public static List<SortOptions> Build(IReadOnlyList<SortField> fields, ElasticsearchQueryVisitorContext context)
    {
        var sorts = new List<SortOptions>(fields.Count);
        foreach (var field in fields)
        {
            if (field.GetData<SortOptions>(ElasticsearchNodeExtensions.SortKey) is { } custom)
            {
                sorts.Add(custom);
                continue;
            }

            var order = field.Direction == SortDirection.Descending ? SortOrder.Desc : SortOrder.Asc;
            switch (field.Field)
            {
                case "_score":
                    sorts.Add(new SortOptions { Score = new ScoreSort { Order = order } });
                    continue;
                case "_doc":
                    sorts.Add(new SortOptions { Doc = new ScoreSort { Order = order } });
                    continue;
            }

            if (field.Field.Contains('^'))
            {
                context.ValidationResult.AddError($"Field names cannot contain '^': {field.Field}", field.Position);
                continue;
            }

            var resolver = context.MappingResolver;
            string sortField = resolver?.GetSortFieldName(field.Field) ?? field.Field;
            var fieldType = resolver is null ? FieldType.None : ElasticMappingResolver.GetFieldType(resolver.GetMapping(sortField, followAlias: true).Property);
            if (fieldType == FieldType.None && context.GetRuntimeField(field.Field) is { } runtime)
                fieldType = runtime.Type switch
                {
                    RuntimeFieldType.Boolean => FieldType.Boolean,
                    RuntimeFieldType.Date => FieldType.Date,
                    RuntimeFieldType.Double => FieldType.Double,
                    RuntimeFieldType.Long => FieldType.Long,
                    _ => FieldType.Keyword
                };

            if (field.Value is { } value)
            {
                if (fieldType == FieldType.GeoPoint)
                {
                    if (ParseLocation(value, field, context) is { } location)
                        sorts.Add(new SortOptions { GeoDistance = new GeoDistanceSort { Field = sortField, Location = [location], Order = order, DistanceType = GeoDistanceType.Arc } });
                }
                else
                    context.ValidationResult.AddError($"Sort values are only supported on geo_point fields, where they sort by distance ({field.OriginalField}:{value}).", field.Position, QueryErrorCode.UnsupportedQueryType);

                continue;
            }

            var fieldSort = new FieldSort(sortField)
            {
                Order = order,
                UnmappedType = fieldType == FieldType.None ? FieldType.Keyword : fieldType
            };

            if (context.UseNested && resolver?.GetMapping(field.Field, followAlias: true) is { NestedPath: { } nestedPath } mapping)
                fieldSort.Nested = BuildNestedSort(mapping.NestedPathChain, context.GetNestedFilter(nestedPath, field.Field));

            sorts.Add(new SortOptions { Field = fieldSort });
        }

        context.ValidationResult.ThrowIfInvalid();
        return sorts;
    }

    /// <summary>
    /// Reads a <c>lat,lon</c> point; anything else (a geohash) is passed to Elasticsearch as text.
    /// </summary>
    private static GeoLocation? ParseLocation(string value, SortField field, ElasticsearchQueryVisitorContext context)
    {
        string[] parts = value.Split(',');
        if (parts.Length == 2
            && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180)
            {
                context.ValidationResult.AddError($"Invalid geo sort coordinates '{value}'; latitude must be between -90 and 90 and longitude between -180 and 180, both finite.", field.Position);
                return null;
            }

            return GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = lat, Lon = lon });
        }

        return GeoLocation.Text(value);
    }

    private static NestedSortValue BuildNestedSort(IReadOnlyList<string> chain, Elastic.Clients.Elasticsearch.QueryDsl.Query? filter)
    {
        NestedSortValue? inner = null;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var nested = new NestedSortValue { Path = chain[i], Nested = inner };
            if (i == chain.Count - 1 && filter is not null)
                nested.Filter = filter;

            inner = nested;
        }

        return inner!;
    }
}
