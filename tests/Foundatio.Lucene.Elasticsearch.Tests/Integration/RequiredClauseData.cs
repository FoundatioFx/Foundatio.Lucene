using System.Globalization;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Eight documents cover every combination of the tags a, b, and c (the id's bits); the nested values distinguish
/// matches within one child from matches spread across different children.
/// </summary>
public static class RequiredClauseData
{
    public const string Index = "required-clauses";

    public static TypeMapping Mapping => new()
    {
        Dynamic = DynamicMapping.Strict,
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "tags", new KeywordProperty() },
            { "children", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() }, { "value", new KeywordProperty() } } } }
        }
    };

    public static Task SeedAsync(ElasticsearchFixture fixture)
    {
        Child[][] children =
        [
            [],
            [new("a", "x"), new("b", "y")],
            [new("b", "y")],
            [new("a", "y"), new("b", "x")],
            [],
            [new("a", "x")],
            [new("b", "y")],
            [new("a", "y")]
        ];

        var documents = Enumerable.Range(0, 8).Select(bits => new Document
        {
            Id = bits.ToString(CultureInfo.InvariantCulture),
            Tags = new[] { "a", "b", "c" }.Where((_, bit) => (bits & (1 << bit)) != 0).ToArray(),
            Children = children[bits]
        });

        return fixture.CreateIndexAsync(Index, Mapping, documents);
    }

    public sealed record Document
    {
        public required string Id { get; init; }
        public string[] Tags { get; init; } = [];
        public Child[] Children { get; init; } = [];
    }

    public sealed record Child(string Name, string Value);
}
