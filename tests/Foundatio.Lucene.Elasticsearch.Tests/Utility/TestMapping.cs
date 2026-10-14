using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Utility;

/// <summary>
/// The index mapping shared by the unit tests. It mirrors the probe mapping used to document Foundatio.Parsers
/// behavior, plus sibling nested paths and a second nested object for nested filter tests.
/// </summary>
public static class TestMapping
{
    public static TypeMapping Create() => new()
    {
        Properties = new Properties
        {
            { "text", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() } } } },
            { "text2", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() }, { "sort", new KeywordProperty() } } } },
            { "plaintext", new TextProperty() },
            { "otherText", new TextProperty() },
            { "keyword", new KeywordProperty() },
            { "multiWord", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() } } } },
            { "number", new IntegerNumberProperty() },
            { "long", new LongNumberProperty() },
            { "double", new DoubleNumberProperty() },
            { "float", new FloatNumberProperty() },
            { "bool", new BooleanProperty() },
            { "date", new DateProperty() },
            { "dateNanos", new DateNanosProperty() },
            { "geo", new GeoPointProperty() },
            { "alias", new FieldAliasProperty { Path = "keyword" } },
            { "textAlias", new FieldAliasProperty { Path = "plaintext" } },
            { "dateAlias", new FieldAliasProperty { Path = "date" } },
            { "obj", new ObjectProperty { Properties = new Properties { { "name", new KeywordProperty() }, { "desc", new TextProperty() } } } },
            {
                "children", new NestedProperty
                {
                    Properties = new Properties
                    {
                        { "name", new KeywordProperty() },
                        { "text", new TextProperty() },
                        { "num", new IntegerNumberProperty() },
                        { "grand", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() } } } },
                        { "toys", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() } } } }
                    }
                }
            },
            { "resellers", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() }, { "price", new DoubleNumberProperty() } } } }
        }
    };

    /// <summary>
    /// Creates a parser that uses the shared mapping.
    /// </summary>
    public static ElasticsearchQueryParser CreateParser(Action<ElasticsearchQueryParserConfiguration>? configure = null)
    {
        return new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(Create());
            configure?.Invoke(c);
        });
    }

    /// <summary>
    /// Creates a scoring parser that uses the shared mapping, so queries are returned without the filter wrapper.
    /// </summary>
    public static ElasticsearchQueryParser CreateScoringParser(Action<ElasticsearchQueryParserConfiguration>? configure = null)
    {
        return CreateParser(c =>
        {
            c.UseScoring = true;
            configure?.Invoke(c);
        });
    }
}
