using System.Globalization;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Documents for every combination of organization, status, deletion flag, and description.
/// </summary>
public static class InvertData
{
    public const string Index = "invert";

    public static TypeMapping Mapping => new()
    {
        Properties = new Properties
        {
            { "id", new KeywordProperty() },
            { "organizationId", new KeywordProperty() },
            { "status", new KeywordProperty() },
            { "isDeleted", new BooleanProperty() },
            { "description", new TextProperty() }
        }
    };

    public static Task SeedAsync(ElasticsearchFixture fixture)
    {
        string[] organizations = ["1", "2"];
        string[] statuses = ["open", "regressed", "ignored", "fixed"];
        bool[] deleted = [false, true];
        string[] descriptions = ["alpha report", "beta report", "gamma"];

        int id = 0;
        var documents = (
            from organization in organizations
            from status in statuses
            from isDeleted in deleted
            from description in descriptions
            select new Document((++id).ToString(CultureInfo.InvariantCulture), organization, status, isDeleted, description)).ToList();

        return fixture.CreateIndexAsync(Index, Mapping, documents);
    }

    public sealed record Document(string Id, string OrganizationId, string Status, bool IsDeleted, string Description);
}
