using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// A small catalog used to execute generated aggregations, sorts, and complete search requests.
/// </summary>
public static class SearchData
{
    public const string Index = "search";

    public static TypeMapping Mapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "title", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() } } } },
            { "category", new KeywordProperty() },
            { "cat", new FieldAliasProperty { Path = "category" } },
            { "price", new DoubleNumberProperty() },
            { "created", new DateProperty() },
            { "location", new GeoPointProperty() },
            { "tags", new KeywordProperty() },
            { "resellers", new NestedProperty { Properties = new Properties { { "name", new KeywordProperty() }, { "price", new DoubleNumberProperty() } } } }
        }
    };

    public static Task SeedAsync(ElasticsearchFixture fixture)
    {
        Document[] documents =
        [
            new("1", "Introduction to Elasticsearch", "technology", 29.99, new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc), GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = 40.7128, Lon = -74.0060 }), ["search", "database"], [new("Official", 25), new("Other", 20)]),
            new("2", "Advanced Lucene Queries", "technology", 39.99, new DateTime(2024, 1, 20, 10, 0, 0, DateTimeKind.Utc), GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = 34.0522, Lon = -118.2437 }), ["lucene", "search"], [new("Official", 35)]),
            new("3", "Database Design Patterns", "database", 49.99, new DateTime(2024, 2, 10, 10, 0, 0, DateTimeKind.Utc), GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = 51.5074, Lon = -0.1278 }), ["database"], [new("Other", 45)]),
            new("4", "Unpublished Draft", "draft", 0, null, null, [], []),
            new("5", "Machine Learning Basics", "technology", 59.99, new DateTime(2024, 3, 5, 10, 0, 0, DateTimeKind.Utc), GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = 37.7749, Lon = -122.4194 }), ["ml"], [new("Official", 55), new("Other", 50)])
        ];

        return fixture.CreateIndexAsync(Index, Mapping, documents);
    }

    public sealed record Document(string Id, string Title, string Category, double Price, DateTime? Created, GeoLocation? Location, string[] Tags, Reseller[] Resellers);

    public sealed record Reseller(string Name, double Price);
}
