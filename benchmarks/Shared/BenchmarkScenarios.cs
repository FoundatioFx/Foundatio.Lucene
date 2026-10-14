using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Benchmarks;

/// <summary>
/// Scenarios shared by the Foundatio.Lucene and Foundatio.Parsers benchmark projects so their results are comparable.
/// The file is linked into both projects; it only uses the Elastic.Clients.Elasticsearch mapping types that exist in
/// both the 8.x (Foundatio.Parsers) and 9.x (Foundatio.Lucene) clients.
/// </summary>
public static class BenchmarkScenarios
{
    public const string SimpleQuery = "status:open";

    public const string TypicalQuery = "type:error AND (status:open OR status:regressed) AND created:[now-7d TO now] -tags:ignore";

    public const string ComplexQuery = "(message:\"connection timeout\" OR message:refused*) AND source:checkout AND (count:>=10 OR level:critical) AND NOT tags:(test OR debug) AND _exists_:user";

    public const string NestedQuery = "children.name:x AND children.num:[1 TO 5]";

    public const string Aggregations = "terms:(status~10 max:created min:created) date:created~1d cardinality:user";

    public const string Sort = "-created +title count";

    public static TypeMapping CreateMapping()
    {
        return new TypeMapping
        {
            Properties = new Properties
            {
                { "status", new KeywordProperty() },
                { "type", new KeywordProperty() },
                { "created", new DateProperty() },
                { "tags", new KeywordProperty() },
                { "message", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() } } } },
                { "source", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() } } } },
                { "count", new IntegerNumberProperty() },
                { "level", new KeywordProperty() },
                { "user", new KeywordProperty() },
                { "title", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() }, { "sort", new KeywordProperty() } } } },
                {
                    "children", new NestedProperty
                    {
                        Properties = new Properties { { "name", new KeywordProperty() }, { "num", new IntegerNumberProperty() } }
                    }
                }
            }
        };
    }
}
