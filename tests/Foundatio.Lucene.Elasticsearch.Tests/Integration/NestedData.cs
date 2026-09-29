using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Nested documents whose results differ depending on whether clauses must match the same nested object.
/// </summary>
public static class NestedData
{
    public const string MultiLevelIndex = "nested-multilevel";
    public const string SiblingIndex = "nested-siblings";
    public const string ExclusionIndex = "nested-exclusion";
    public const string ResellerIndex = "nested-resellers";
    public const string ItemIndex = "nested-items";

    public static TypeMapping MultiLevelMapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            {
                "parent", new NestedProperty
                {
                    Properties = new Properties
                    {
                        { "name", new KeywordProperty() },
                        { "child", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() } } } }
                    }
                }
            }
        }
    };

    public static TypeMapping SiblingMapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "title", new KeywordProperty() },
            {
                "parent", new NestedProperty
                {
                    Properties = new Properties
                    {
                        { "childA", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() } } } },
                        { "childB", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() } } } }
                    }
                }
            }
        }
    };

    public static TypeMapping ResellerMapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "resellers", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() }, { "price", new DoubleNumberProperty() }, { "stock", new IntegerNumberProperty() } } } }
        }
    };

    public static TypeMapping ItemMapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "items", new NestedProperty { Properties = new Properties { { "status", new KeywordProperty() }, { "priority", new KeywordProperty() }, { "type", new KeywordProperty() } } } }
        }
    };

    public static Task SeedAsync(ElasticsearchFixture fixture)
    {
        MultiLevelDocument[] multiLevel =
        [
            new("docA", [new("Bob", [new("Charlie")]), new("Sue", [new("Alice")])]),
            new("docB", [new("Bob", [new("Alice")])])
        ];

        SiblingDocument[] siblings =
        [
            new("docA", "keep", [new([new("Alice")], [new("NotBob")]), new([new("NotAlice")], [new("Bob")])]),
            new("docB", "keep", [new([new("Alice")], [new("Bob")])])
        ];

        SiblingParent clean = new([new("NotAlice")], [new("NotBob")]);
        SiblingParent alice = new([new("Alice")], []);
        SiblingParent bob = new([], [new("Bob")]);
        SiblingDocument[] exclusions =
        [
            new("clean", "keep", [clean]),
            new("none", "keep", []),
            new("alice", "keep", [alice]),
            new("bob", "keep", [bob]),
            new("clean-alice", "keep", [clean, alice]),
            new("clean-bob", "keep", [clean, bob]),
            new("other", "other", [clean])
        ];

        ResellerDocument[] resellers =
        [
            new("p1", [new("Official", 10, 0), new("Other", 8, 5)]),
            new("p2", [new("Official", 10, 5)]),
            new("p3", [new("Other", 10, 5)]),
            new("p4", [new("Official", 10, 0), new("Official", 8, 5)])
        ];

        ItemDocument[] items =
        [
            new("doc1", [new("active", null, "priority_filter"), new(null, "active", "priority_filter")]),
            new("doc2", [new("active", null, "status_filter")]),
            new("doc3", [new("active", null, "priority_filter")])
        ];

        return Task.WhenAll(
            fixture.CreateIndexAsync(MultiLevelIndex, MultiLevelMapping, multiLevel),
            fixture.CreateIndexAsync(SiblingIndex, SiblingMapping, siblings),
            fixture.CreateIndexAsync(ExclusionIndex, SiblingMapping, exclusions),
            fixture.CreateIndexAsync(ResellerIndex, ResellerMapping, resellers),
            fixture.CreateIndexAsync(ItemIndex, ItemMapping, items));
    }

    public sealed record MultiLevelDocument(string Id, MultiLevelParent[] Parent);

    public sealed record MultiLevelParent(string Name, NamedChild[] Child);

    public sealed record SiblingDocument(string Id, string Title, SiblingParent[] Parent);

    public sealed record SiblingParent(NamedChild[] ChildA, NamedChild[] ChildB);

    public sealed record NamedChild(string Name);

    public sealed record ResellerDocument(string Id, Reseller[] Resellers);

    public sealed record Reseller(string Name, double Price, int Stock);

    public sealed record ItemDocument(string Id, Item[] Items);

    public sealed record Item(string? Status, string? Priority, string Type);
}
