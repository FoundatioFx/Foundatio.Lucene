using System.Linq.Expressions;
using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

/// <summary>The shape of merged properties returned to consumers, for every client property type.</summary>
public class MappingMergeTests(ITestOutputHelper output) : MappingTestBase(output)
{
    private static readonly Type[] _objectLikeTypes = [typeof(ObjectProperty), typeof(NestedProperty), typeof(PassthroughObjectProperty)];

    public static TheoryData<string> MergeablePropertyTypes => new(typeof(IProperty).Assembly.GetExportedTypes()
        .Where(type => !type.IsAbstract && type != typeof(FieldAliasProperty) && typeof(IProperty).IsAssignableFrom(type)
            && type.GetConstructor(Type.EmptyTypes) is not null && GetChildPropertyInfo(type) is not null)
        .Select(type => type.FullName!));

    public static TheoryData<string> PropertyTypesWithFields => new(typeof(IProperty).Assembly.GetExportedTypes()
        .Where(type => !type.IsAbstract && typeof(IProperty).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null
            && type.GetProperty("Fields")?.PropertyType == typeof(Properties))
        .Select(type => type.FullName!));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetMappingProperty_WithMergedChildren_PreservesPublicShape(bool text)
    {
        // Arrange
        IProperty code = text
            ? new TextProperty { Fields = new Properties { { "local", new KeywordProperty() } } }
            : new ObjectProperty { Properties = new Properties { { "local", new KeywordProperty() } } };
        IProperty server = text
            ? new TextProperty { Fields = new Properties { { "remote", new KeywordProperty() } } }
            : new ObjectProperty { Properties = new Properties { { "remote", new KeywordProperty() } } };
        using var resolver = new ElasticMappingResolver(new TypeMapping { Properties = new Properties { { "name", code } } }, Inferrer,
            () => new TypeMapping { Properties = new Properties { { "name", server } } });

        // Act
        var property = resolver.GetMappingProperty("name");
        var children = property is TextProperty t ? t.Fields : ((ObjectProperty)property!).Properties;

        // Assert
        Assert.True(resolver.GetMapping("name.local").Found);
        Assert.Contains(children!, child => child.Key.Name == "local");
        Assert.Contains(children!, child => child.Key.Name == "remote");
    }

    [Theory]
    [MemberData(nameof(MergeablePropertyTypes))]
    public void GetMappingProperty_WithCodeChildren_PreservesClientPropertyTypes(string typeName)
    {
        // Arrange
        var type = typeof(IProperty).Assembly.GetType(typeName)!;
        var code = (IProperty)Activator.CreateInstance(type)!;
        var server = (IProperty)Activator.CreateInstance(type)!;
        var childProperty = GetChildPropertyInfo(type)!;
        childProperty.SetValue(code, new Properties { { "local", new KeywordProperty() } });
        childProperty.SetValue(server, new Properties { { "remote", new KeywordProperty() } });
        using var resolver = new ElasticMappingResolver(new TypeMapping { Properties = new Properties { { "name", code } } }, Inferrer,
            () => new TypeMapping { Properties = new Properties { { "name", server } } });
        resolver.SetPropertyMetadataValue(code, "custom", "value");

        // Act
        var merged = resolver.GetMappingProperty("name")!;
        var children = (Properties)childProperty.GetValue(merged)!;

        // Assert
        Assert.Equal(type, merged.GetType());
        Assert.NotSame(server, merged);
        Assert.Contains(children, child => child.Key.Name == "local");
        Assert.Contains(children, child => child.Key.Name == "remote");
        Assert.Single((Properties)childProperty.GetValue(server)!);
        Assert.Single((Properties)childProperty.GetValue(code)!);
        Assert.Same(children.Single(child => child.Key.Name == "local").Value, resolver.GetMappingProperty("name.local"));
        Assert.Equal("value", resolver.GetPropertyMetadataValue<string>(merged, "custom"));
    }

    [Theory]
    [MemberData(nameof(PropertyTypesWithFields))]
    public void GetFields_ForEveryClientPropertyType_ReturnsMultiFields(string typeName)
    {
        // Arrange - the client has no common multi-field accessor, so a new property type in a client upgrade
        // must be added to GetFields and to the merge.
        var type = typeof(IProperty).Assembly.GetType(typeName)!;
        var property = (IProperty)Activator.CreateInstance(type)!;
        var fields = new Properties { { "sub", new KeywordProperty() } };
        type.GetProperty("Fields")!.SetValue(property, fields);

        // Act
        var result = property.GetFields();

        // Assert
        Assert.Same(fields, result);
    }

    [Fact]
    public void GetMappingProperty_WithNestedMerges_PreservesSettingsAndSerialization()
    {
        // Arrange
        var local = new TextProperty { Fields = new Properties { { "local", new KeywordProperty() } } };
        var remote = new TextProperty
        {
            Analyzer = "english",
            SearchAnalyzer = "standard",
            Index = false,
            Norms = false,
            Fields = new Properties { { "remote", new KeywordProperty { IgnoreAbove = 256 } } }
        };
        using var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"));
        var code = new TypeMapping { Properties = new Properties { { "parent", new ObjectProperty { Properties = new Properties { { "name", local } } } } } };
        var server = new TypeMapping
        {
            Properties = new Properties { { "parent", new ObjectProperty { Dynamic = DynamicMapping.Strict, Properties = new Properties { { "name", remote } } } } }
        };
        using var resolver = new ElasticMappingResolver(code, new Inferrer(settings), () => server);

        // Act
        var parent = Assert.IsType<ObjectProperty>(resolver.GetMappingProperty("parent"));
        var merged = Assert.IsType<TextProperty>(parent.Properties!.Single().Value);
        var client = new ElasticsearchClient(settings);
        using var stream = new MemoryStream();
        client.RequestResponseSerializer.Serialize<IProperty>(parent, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        // Assert
        Assert.Equal(DynamicMapping.Strict, parent.Dynamic);
        Assert.Equal("english", merged.Analyzer);
        Assert.Equal("standard", merged.SearchAnalyzer);
        Assert.False(merged.Index);
        Assert.False(merged.Norms);
        Assert.Equal(2, merged.Fields!.Count());
        Assert.Contains("\"local\"", json);
        Assert.Contains("\"remote\"", json);
        Assert.Contains("\"ignore_above\":256", json);
        Assert.Single(remote.Fields!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMappingProperty_WithExpressionChild_PreservesInferredName(bool server)
    {
        // Arrange
        var name = new PropertyName((Expression<Func<string, object>>)(value => value.Length));
        var child = new KeywordProperty();
        var parent = new ObjectProperty { Properties = new Properties { { name, child } } };
        var mapping = new TypeMapping { Properties = new Properties { { "parent", parent } } };
        using var resolver = new ElasticMappingResolver(server ? new TypeMapping() : mapping, Inferrer, () => server ? mapping : null);

        // Act
        var merged = Assert.IsType<ObjectProperty>(resolver.GetMappingProperty("parent"));

        // Assert
        Assert.Same(child, resolver.GetMappingProperty("parent.length"));
        Assert.Equal("length", Assert.Single(merged.Properties!).Key.Name);
        Assert.Same(name, Assert.Single(parent.Properties!).Key);
    }

    [Fact]
    public void AddSortNormalizer_WhenSerialized_DeclaresLowercaseAsciiFoldingNormalizer()
    {
        // Arrange
        using var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"));
        var client = new ElasticsearchClient(settings);
        var analysis = new IndexSettingsAnalysis();

        // Act
        new IndexSettingsAnalysisDescriptor(analysis).AddSortNormalizer();
        using var stream = new MemoryStream();
        client.RequestResponseSerializer.Serialize(analysis, stream);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        // Assert
        Assert.Contains("\"sort\"", json);
        Assert.Contains("\"lowercase\"", json);
        Assert.Contains("\"asciifolding\"", json);
    }

    private static System.Reflection.PropertyInfo? GetChildPropertyInfo(Type type)
    {
        string name = _objectLikeTypes.Contains(type) ? "Properties" : "Fields";
        var property = type.GetProperty(name);
        return property?.PropertyType == typeof(Properties) ? property : null;
    }
}
