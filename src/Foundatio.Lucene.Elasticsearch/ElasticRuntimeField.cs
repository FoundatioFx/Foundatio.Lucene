using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// A runtime field definition returned by a <see cref="RuntimeFieldResolver"/>.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="Type">The runtime field type. Defaults to keyword.</param>
/// <param name="Script">The Painless script that computes the value, or null to read the value from <c>_source</c>.</param>
public sealed record ElasticRuntimeField(string Name, RuntimeFieldType Type = RuntimeFieldType.Keyword, string? Script = null)
{
    /// <summary>
    /// Converts the definition to the client's runtime field type for <c>runtime_mappings</c>.
    /// </summary>
    public RuntimeField ToRuntimeField()
    {
        var field = new RuntimeField(Type);
        if (Script is not null)
            field.Script = new Elastic.Clients.Elasticsearch.Script { Source = Script };

        return field;
    }
}
