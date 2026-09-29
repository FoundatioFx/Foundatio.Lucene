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
