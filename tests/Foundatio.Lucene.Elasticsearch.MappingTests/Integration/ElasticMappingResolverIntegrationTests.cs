using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lucene.Elasticsearch.MappingTests.Integration;

[Collection(ElasticsearchMappingCollection.Name)]
[Trait("TestType", "Integration")]
public class ElasticMappingResolverIntegrationTests(ElasticsearchMappingFixture fixture, ITestOutputHelper output)
{
    private readonly ILogger _logger = new TestOutputLogger(output);

    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    private ElasticsearchClient Client => fixture.Client;

    [Fact]
    public async Task GetMappingProperty_WithCodeMappedAndDynamicProperties_ResolvesServerMapping()
    {
        // Arrange
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapMyNestedType);
        await fixture.IndexAndRefreshAsync(index, new MyNestedType { Field1 = "value1", Payload = "test payload" });
        using var resolver = ElasticMappingResolver.Create<MyNestedType>(MapMyNestedType, Client, index, _logger);

        // Act
        var payloadProperty = resolver.GetMappingProperty("payload");

        // Assert
        Assert.IsType<TextProperty>(payloadProperty);
        Assert.True(resolver.IsLoaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithCodeAndServerMapping_ResolvesCanonicalFieldsAndSubFields(bool asynchronous)
    {
        // Arrange
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapMyNestedType);
        await fixture.IndexAndRefreshAsync(index, new MyNestedType
        {
            Field1 = "value1",
            Field2 = "value2",
            Nested =
            [
                new MyType
                {
                    Id = "nested-id-1",
                    Field1 = "banana",
                    Field4 = 5,
                    Field5 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Data = { { "number-0001", 23 }, { "text-0001", "Hey" }, { "spaced field", "hey" } }
                }
            ]
        });
        using var resolver = ElasticMappingResolver.Create<MyNestedType>(MapMyNestedType, Client, index, _logger);
        if (asynchronous)
            await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Act & Assert - dynamic template and dynamically mapped fields.
        Assert.Equal("nested.data.text-0001.keyword", resolver.GetAggregationsFieldName("nested.data.text-0001"));
        Assert.Equal("nested.data.text-0001.sort", resolver.GetSortFieldName("nested.data.text-0001"));
        Assert.Equal("nested.data.spaced field.keyword", resolver.GetAggregationsFieldName("nested.data.spaced field"));
        Assert.Equal("nested.data.spaced field.keyword", resolver.GetSortFieldName("nested.data.spaced field"));
        Assert.Equal("nested.data.spaced field", resolver.GetResolvedField("nested.data.spaced field"));

        // Code mapped fields, canonical casing, and unmapped paths.
        Assert.IsType<TextProperty>(resolver.GetMappingProperty("Field1"));
        Assert.Equal("field5.keyword", resolver.GetAggregationsFieldName("Field5"));
        Assert.Null(resolver.GetMappingProperty("UnknowN.test.doesNotExist"));
        Assert.Equal("field1", resolver.GetResolvedField("FielD1"));
        Assert.Equal(" ", resolver.GetResolvedField(" "));
        Assert.Equal("UnknowN.test.doesNotExist", resolver.GetResolvedField("UnknowN.test.doesNotExist"));
        Assert.Equal("unknown", resolver.GetResolvedField("unknown"));

        // Field expressions and aliases.
        var field4Property = resolver.GetMappingProperty("Field4");
        Assert.IsType<TextProperty>(field4Property);
        Assert.IsType<TextProperty>(resolver.GetMappingProperty(Infer.Field<MyNestedType>(p => p.Field4)));
        var field4AliasMapping = resolver.GetMapping("Field4Alias", followAlias: true);
        Assert.Same(field4Property, field4AliasMapping.Property);
        Assert.Equal("field4.sort", resolver.GetSortFieldName("Field4Alias"));
        Assert.Equal("field4.keyword", resolver.GetAggregationsFieldName("Field4Alias"));

        // Object properties below a dynamically mapped array of objects.
        Assert.IsType<TextProperty>(resolver.GetMappingProperty("Nested.Id"));
        Assert.Equal("nested.id", resolver.GetResolvedField("Nested.Id"));
        Assert.IsType<TextProperty>(resolver.GetMappingProperty("nEsted.fieLD1"));
        Assert.IsType<LongNumberProperty>(resolver.GetMappingProperty("Nested.Field4"));
        Assert.IsType<DateProperty>(resolver.GetMappingProperty("Nested.Field5"));
        Assert.IsType<ObjectProperty>(resolver.GetMappingProperty("Nested.Data"));
        Assert.IsType<LongNumberProperty>(resolver.GetMappingProperty("nested.data.number-0001"));
    }

    [Fact]
    public async Task EnsureFieldsAsync_WithCustomFieldCreatedAfterMappingWasLoaded_ResolvesFieldsThatExecute()
    {
        // Arrange - resolve an existing field first so the mapping is already loaded, like a long lived resolver.
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapDynamicCustomFieldType);
        using var resolver = ElasticMappingResolver.Create<MyNestedType>(MapDynamicCustomFieldType, Client, index, _logger);
        Assert.Equal("field1.sort", resolver.GetSortFieldName("field1"));

        await fixture.IndexAndRefreshAsync(index, new Dictionary<string, object>
        {
            { "field1", "value1" },
            {
                "idx", new Dictionary<string, object>
                {
                    { "nested-000001", new[] { new Dictionary<string, object> { { "value", "banana" } } } },
                    { "string-000001", "apple" }
                }
            }
        });

        // Act
        bool found = await resolver.EnsureFieldsAsync(["idx.nested-000001.value", "idx.string-000001"], TestCancellationToken);
        var valueMapping = resolver.GetMapping("idx.nested-000001.value");
        string sortField = resolver.GetSortFieldName("idx.string-000001");
        var response = await Client.SearchAsync<object>(s => s
            .Indices(index)
            .Query(q => q.Nested(n => n
                .Path(valueMapping.NestedPath!)
                .Query(nq => nq.Term(t => t.Field(valueMapping.FullPath).Value("banana")))))
            .Sort(so => so.Field(sortField)), TestCancellationToken);

        // Assert - executing the nested query and the sort proves the resolved names are the real ones.
        Assert.True(found);
        Assert.True(response.IsValidResponse, response.DebugInformation);
        Assert.Equal(1, response.Total);
        Assert.Equal("idx.nested-000001", valueMapping.NestedPath);
        Assert.True(resolver.IsNestedPropertyType("idx.nested-000001"));
        Assert.Equal("idx.string-000001.sort", sortField);
        Assert.Equal("idx.string-000001.keyword", resolver.GetAggregationsFieldName("idx.string-000001"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithCustomFieldQueriedBeforeItWasCreated_ResolvesNewField(bool asynchronous)
    {
        // Arrange - the field is queried before it exists in the index mapping.
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapDynamicCustomFieldType);
        using var resolver = ElasticMappingResolver.Create<MyNestedType>(MapDynamicCustomFieldType, Client, index, _logger);
        resolver.UnmappedFieldRefreshInterval = TimeSpan.FromMilliseconds(1);
        Assert.False(asynchronous
            ? await resolver.EnsureFieldsAsync(["idx.nested-000001"], TestCancellationToken)
            : resolver.IsNestedPropertyType("idx.nested-000001"));

        // Act
        await fixture.IndexAndRefreshAsync(index, new Dictionary<string, object>
        {
            { "field1", "value1" },
            { "idx", new Dictionary<string, object> { { "nested-000001", new[] { new Dictionary<string, object> { { "value", "banana" } } } } } }
        });
        await Task.Delay(10, TestCancellationToken);
        if (asynchronous)
            Assert.True(await resolver.EnsureFieldsAsync(["idx.nested-000001"], TestCancellationToken));

        // Assert
        Assert.True(resolver.IsNestedPropertyType("idx.nested-000001"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithAliasOrPatternSpanningIndices_MergesTheirMappings(bool pattern)
    {
        // Arrange
        string token = Guid.NewGuid().ToString("N")[..8];
        string alias = $"merged-{token}";
        await fixture.CreateIndexAsync<object>(m => m.Properties(p => p
            .Keyword("old_only")
            .Text("shared")
            .Object("obj", o => o.Properties(op => op.Keyword("old_child")))), $"merge{token}-2024.01", alias);
        await fixture.CreateIndexAsync<object>(m => m.Properties(p => p
            .LongNumber("new_only")
            .Keyword("shared")
            .Object("obj", o => o.Properties(op => op.Date("new_child")))), $"merge{token}-2024.02", alias);
        using var resolver = ElasticMappingResolver.Create(Client, pattern ? $"merge{token}-*" : alias, _logger);

        // Act
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert - fields of every index are included and the newest index wins type conflicts.
        Assert.True(resolver.IsLoaded);
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("old_only"));
        Assert.IsType<LongNumberProperty>(resolver.GetMappingProperty("new_only"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("shared"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("obj.old_child"));
        Assert.IsType<DateProperty>(resolver.GetMappingProperty("obj.new_child"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithMissingIndex_TreatsFieldsAsUnmapped(bool asynchronous)
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create(Client, $"missing-{Guid.NewGuid():N}", _logger);

        // Act
        if (asynchronous)
            await resolver.EnsureLoadedAsync(TestCancellationToken);

        var mapping = resolver.GetMapping("field1");

        // Assert
        Assert.False(mapping.Found);
        Assert.False(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
    }

    [Fact]
    public async Task Create_WithInferredIndexName_ResolvesServerMapping()
    {
        // Arrange
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapMyNestedType);
        var client = fixture.CreateClient(settings => settings.DefaultMappingFor<MyNestedType>(m => m.IndexName(index)));
        using var resolver = ElasticMappingResolver.Create<MyNestedType>(client, _logger);
        using var mergedResolver = ElasticMappingResolver.Create<MyNestedType>(m => m.Properties(p => p.Keyword("code_only")), client, _logger);

        // Act
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.Equal("field1.sort", resolver.GetSortFieldName("field1"));
        Assert.Equal("field1.sort", mergedResolver.GetSortFieldName("field1"));
        Assert.IsType<KeywordProperty>(mergedResolver.GetMappingProperty("code_only"));
    }

    [Fact]
    public async Task CreateWithAsyncLoader_WithClientLoader_ResolvesAfterEnsureLoaded()
    {
        // Arrange
        string index = await fixture.CreateIndexAsync<MyNestedType>(MapMyNestedType);
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            var response = await Client.Indices.GetMappingAsync(index, cancellationToken);
            return response.IsValidResponse ? response.Mappings.Values.Single().Mappings : null;
        }, Client.Infer, logger: _logger);

        // Act
        bool canResolveBeforeLoad = resolver.CanResolveSynchronously;
        bool found = await resolver.EnsureFieldsAsync(["field1", "Field4Alias"], TestCancellationToken);

        // Assert
        Assert.False(canResolveBeforeLoad);
        Assert.True(found);
        Assert.Equal("field4.sort", resolver.GetSortFieldName("field4alias"));
    }

    private static void MapMyNestedType(TypeMappingDescriptor<MyNestedType> m) => m
        .Dynamic(DynamicMapping.True)
        .DynamicTemplates(dt => dt.Add("idx_text", d => d.Match("text*").Mapping(p => p.Text(t => t.AddKeywordAndSortFields()))))
        .Properties(p => p
            .Text(d => d.Field1!, t => t.AddKeywordAndSortFields())
            .Text(d => d.Field4, t => t.AddKeywordAndSortFields())
            .FieldAlias("field4alias", a => a.Path(d => d.Field4))
            .Text(d => d.Field5!, t => t.AddKeywordAndSortFields(true)));

    private static void MapDynamicCustomFieldType(TypeMappingDescriptor<MyNestedType> m) => m
        // Mirrors how Foundatio.Repositories maps custom fields: the idx.<type>-<slot> fields only exist in the index
        // mapping after the first document that uses them has been indexed.
        .Dynamic(DynamicMapping.True)
        .DynamicTemplates(dt => dt
            .Add("idx_nested", d => d.PathMatch("idx.*").Match("nested-*").Mapping(p => p.Nested(n => n.Dynamic(DynamicMapping.True))))
            .Add("idx_string", d => d.PathMatch("idx.*").Match("string-*").Mapping(p => p.Text(t => t.AddKeywordAndSortFields()))))
        .Properties(p => p.Text(d => d.Field1!, t => t.AddKeywordAndSortFields()));

    public sealed class MyNestedType
    {
        public string? Field1 { get; set; }

        public string? Field2 { get; set; }

        public string? Field3 { get; set; }

        public int Field4 { get; set; }

        public string? Field5 { get; set; }

        public string? Payload { get; set; }

        public IList<MyType> Nested { get; set; } = [];
    }

    public sealed class MyType
    {
        public string? Id { get; set; }

        public string? Field1 { get; set; }

        public int Field4 { get; set; }

        public DateTime Field5 { get; set; }

        public Dictionary<string, object> Data { get; set; } = [];
    }
}
