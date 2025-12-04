namespace Foundatio.Lucene.Elasticsearch.Tests;

/// <summary>
/// Integration tests that verify Lucene queries work correctly against a real Elasticsearch instance.
/// </summary>
[Collection("Elasticsearch")]
public class ElasticsearchIntegrationTests
{
    private readonly ElasticsearchFixture _fixture;

    public ElasticsearchIntegrationTests(ElasticsearchFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanSearchWithSimpleTerm()
    {
        var query = await _fixture.Parser.BuildQueryAsync("elasticsearch");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("1", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchWithFieldQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("author:\"John Smith\"");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.Equal("John Smith", d.Author));
    }

    [Fact]
    public async Task CanSearchWithBooleanAnd()
    {
        var query = await _fixture.Parser.BuildQueryAsync("author:\"John Smith\" AND category:technology");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("1", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchWithBooleanOr()
    {
        var query = await _fixture.Parser.BuildQueryAsync("category:database OR category:draft");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithNotQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("category:technology AND NOT author:\"John Smith\"");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.NotEqual("John Smith", d.Author));
    }

    [Fact]
    public async Task CanSearchWithPhraseQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("content:\"search and analytics\"");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);
        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("1", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchWithPrefixQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("title:elast*");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("1", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchWithWildcardQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("title:*learning*");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("5", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchWithNumericRangeQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("price:[40 TO 60]");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.InRange(d.Price, 40, 60));
    }

    [Fact]
    public async Task CanSearchWithOpenEndedRangeQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("price:[50 TO *]");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.True(response.Documents.First().Price >= 50);
    }

    [Fact]
    public async Task CanSearchWithYearQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("year:2023");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.Equal(2023, d.Year));
    }

    [Fact]
    public async Task CanSearchWithYearRangeQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("year:[2023 TO 2024]");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(4, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithExistsQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("_exists_:publishedDate");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(4, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.NotNull(d.PublishedDate));
    }

    [Fact]
    public async Task CanSearchWithMissingQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("_missing_:publishedDate");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Null(response.Documents.First().PublishedDate);
    }

    [Fact]
    public async Task CanSearchWithBooleanField()
    {
        var query = await _fixture.Parser.BuildQueryAsync("isPublished:false");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.False(response.Documents.First().IsPublished);
    }

    [Fact]
    public async Task CanSearchWithMatchAll()
    {
        var query = await _fixture.Parser.BuildQueryAsync("*:*");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(5, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithRequiredTerm()
    {
        var query = await _fixture.Parser.BuildQueryAsync("+category:technology +year:2023");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithProhibitedTerm()
    {
        var query = await _fixture.Parser.BuildQueryAsync("category:technology -author:\"John Smith\"");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.NotEqual("John Smith", d.Author));
    }

    [Fact]
    public async Task CanSearchWithGroupedQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("(category:technology OR category:database) AND year:2023");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithFuzzyQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync("title:elastcsearch~");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Single(response.Documents);
        Assert.Equal("1", response.Documents.First().Id);
    }

    [Fact]
    public async Task CanSearchMultipleTermsDefaultOr()
    {
        var query = await _fixture.Parser.BuildQueryAsync("lucene machine");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithFieldMapping()
    {
        var fieldMap = new FieldMap { { "name", "author" } };
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseScoring = true;
            c.FieldMap = fieldMap;
        });

        var query = await parser.BuildQueryAsync("name:\"John Smith\"");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
        Assert.All(response.Documents, d => Assert.Equal("John Smith", d.Author));
    }

    [Fact]
    public async Task EmptyQueryReturnsAllDocuments()
    {
        var query = await _fixture.Parser.BuildQueryAsync("");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(5, response.Documents.Count);
    }

    [Fact]
    public async Task CanSearchWithComplexNestedQuery()
    {
        var query = await _fixture.Parser.BuildQueryAsync(
            "(author:\"John Smith\" OR author:\"Jane Doe\") AND category:technology AND year:[2022 TO 2023]");
        var response = await _fixture.Client.SearchAsync<TestDocument>(s => s
            .Indices(ElasticsearchFixture.TestIndexName)
            .Query(query), TestContext.Current.CancellationToken);

        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(2, response.Documents.Count);
    }
}
