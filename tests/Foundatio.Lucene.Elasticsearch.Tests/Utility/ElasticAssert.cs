using System.Text.Json.Nodes;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport.Extensions;

namespace Foundatio.Lucene.Elasticsearch.Tests.Utility;

/// <summary>
/// Compares client request objects with expected JSON structurally, so key order does not matter.
/// </summary>
public static class ElasticAssert
{
    private static readonly Elastic.Transport.Serializer Serializer = new ElasticsearchClient().RequestResponseSerializer;

    /// <summary>
    /// Serializes a client object with the client's own serializer.
    /// </summary>
    public static string Serialize<T>(T value) => Serializer.SerializeToString(value);

    /// <summary>
    /// Asserts that <paramref name="actual"/> serializes to the same JSON as <paramref name="expected"/>. Expected JSON
    /// without double quotes may use single quotes, which keeps inline test data readable.
    /// </summary>
    public static void Json<T>(string expected, T actual)
    {
        string expectedJson = expected.Contains('"') ? expected : expected.Replace('\'', '"');
        var expectedNode = JsonNode.Parse(expectedJson);
        string actualJson = Serialize(actual);
        var actualNode = JsonNode.Parse(actualJson);

        if (!JsonNode.DeepEquals(expectedNode, actualNode))
            Assert.Fail($"JSON mismatch.{Environment.NewLine}Expected: {expectedNode?.ToJsonString()}{Environment.NewLine}Actual:   {actualNode?.ToJsonString()}");
    }
}
