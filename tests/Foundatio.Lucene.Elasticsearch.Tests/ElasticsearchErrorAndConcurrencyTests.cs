namespace Foundatio.Lucene.Elasticsearch.Tests;

public class ElasticsearchErrorAndConcurrencyTests
{
    [Fact]
    public void TryBuildQuery_MalformedQuery_ReturnsFailureNotThrow()
    {
        var parser = new ElasticsearchQueryParser();

        var result = parser.TryBuildQuery("(unbalanced AND");

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryBuildQuery_ValidQuery_ReturnsSuccess()
    {
        var parser = new ElasticsearchQueryParser();

        var result = parser.TryBuildQuery("status:active");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task BuildQuery_WithConcurrentOptionRegistrationAndRemoval_IsThreadSafe()
    {
        // Stresses the parser's internal registered-options ConcurrentDictionary: register, query,
        // and remove per-index options across many threads while building queries on a shared parser.
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            int n = i;
            var index = $"index-{n % 8}";
            tasks.Add(Task.Run(() => parser.SetOptions(index, new ElasticsearchQueryOptions
            {
                FieldMap = new FieldMap { { "user", $"tenant{n % 8}.userName" } }
            })));
            tasks.Add(Task.Run(() => parser.BuildQuery("user:john", index, null)));
            tasks.Add(Task.Run(() => parser.RemoveOptions(index)));
        }

        // Must complete without throwing despite concurrent mutation + reads.
        await Task.WhenAll(tasks);
        Assert.True(tasks.All(t => t.IsCompletedSuccessfully));
    }
}
