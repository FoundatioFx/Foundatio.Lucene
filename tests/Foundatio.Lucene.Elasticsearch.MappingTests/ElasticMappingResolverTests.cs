using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

public class ElasticMappingResolverTests(ITestOutputHelper output) : MappingTestBase(output)
{
    [Fact]
    public void GetNonAnalyzedFieldName_WithTextPropertyAndKeywordSubField_ReturnsKeywordPath()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordMapping("title"), Inferrer, logger: Logger);

        // Act
        string result = resolver.GetNonAnalyzedFieldName("title", "keyword");

        // Assert
        Assert.Equal("title.keyword", result);
    }

    [Fact]
    public void GetAggregationsFieldName_WithTextPropertyAndKeywordSubField_ReturnsKeywordPath()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordMapping("title"), Inferrer, logger: Logger);

        // Act
        string result = resolver.GetAggregationsFieldName("title");

        // Assert
        Assert.Equal("title.keyword", result);
    }

    [Fact]
    public void GetSortFieldName_WithTextPropertyAndSortSubField_ReturnsSortPath()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordAndSortMapping("title"), Inferrer, logger: Logger);

        // Act
        string result = resolver.GetSortFieldName("title");

        // Assert
        Assert.Equal("title.sort", result);
    }

    [Fact]
    public void GetSortFieldName_WithoutSortSubField_FallsBackToFirstNonAnalyzedSubField()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordMapping("title"), Inferrer, logger: Logger);

        // Act
        string result = resolver.GetSortFieldName("title");

        // Assert
        Assert.Equal("title.keyword", result);
    }

    [Fact]
    public void GetSortFieldName_WithOnlyAnalyzedSubFields_ReturnsAnalyzedField()
    {
        // Arrange
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(("title", new TextProperty { Fields = CreateProperties(("english", new TextProperty { Analyzer = "english" })) }))
        };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger);

        // Act
        string result = resolver.GetSortFieldName("title");

        // Assert
        Assert.Equal("title", result);
    }

    [Fact]
    public void SubFieldNames_WhenCustomized_AreUsedByNameHelpers()
    {
        // Arrange
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(("title", new TextProperty
            {
                Fields = CreateProperties(("keyword", new KeywordProperty()), ("raw", new KeywordProperty()), ("lower", new KeywordProperty()))
            }))
        };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger)
        {
            KeywordSubFieldName = "raw",
            SortSubFieldName = "lower"
        };

        // Act
        string aggregation = resolver.GetAggregationsFieldName("title");
        string sort = resolver.GetSortFieldName("title");

        // Assert
        Assert.Equal("title.raw", aggregation);
        Assert.Equal("title.lower", sort);
        Assert.Throws<ArgumentException>(() => resolver.SortSubFieldName = " ");
    }

    [Fact]
    public void GetNonAnalyzedFieldName_WithKeywordProperty_ReturnsBareFieldName()
    {
        // Arrange
        var mapping = new TypeMapping { Properties = CreateProperties(("status", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger);

        // Act
        string result = resolver.GetNonAnalyzedFieldName("status", "keyword");

        // Assert
        Assert.Equal("status", result);
    }

    [Fact]
    public void GetNonAnalyzedFieldName_WithTextPropertyWithoutSubFields_ReturnsBareFieldName()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextOnlyMapping("body"), Inferrer, logger: Logger);

        // Act
        string result = resolver.GetNonAnalyzedFieldName("body", "keyword");

        // Assert
        Assert.Equal("body", result);
    }

    [Theory]
    [InlineData("title", "sort", "title.sort")]
    [InlineData("TITLE", "aggregation", "title.keyword")]
    [InlineData("alias", "non-analyzed", "title.keyword")]
    [InlineData("status-alias", "sort", "status-alias")]
    [InlineData("STATUS", "aggregation", "status")]
    [InlineData("title.missing", "non-analyzed", "title.missing")]
    [InlineData("Missing.Field", "sort", "Missing.Field")]
    public void GetFieldNameHelpers_WithStringAndTypedField_ReturnCanonicalNamesAndSubFields(string field, string helper, string expected)
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTypedFieldMapping, Inferrer, logger: Logger);

        // Act
        string fromString = GetFieldName(resolver, field, helper);
        string fromField = GetFieldName(resolver, new Field(field), helper);

        // Assert
        Assert.Equal(expected, fromString);
        Assert.Equal(expected, fromField);
    }

    [Fact]
    public void GetSortFieldName_WithExpressionField_UsesConfiguredInferrer()
    {
        // Arrange
        using var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"));
        settings.DefaultFieldNameInferrer(name => name.ToUpperInvariant());
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(("TITLE", new TextProperty { Fields = CreateProperties(("keyword", new KeywordProperty())) }))
        };
        using var resolver = new ElasticMappingResolver(mapping, new Inferrer(settings), logger: Logger);

        // Act
        string field = resolver.GetSortFieldName(Infer.Field<Document>(d => d.Title!));

        // Assert
        Assert.Equal("TITLE.keyword", field);
    }

    [Fact]
    public void GetMapping_WithExpressionFieldAndNoInferrer_ThrowsInvalidOperationException()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create(CreateTextOnlyMapping("title"));

        // Act
        var exception = Record.Exception(() => resolver.GetMapping(Infer.Field<Document>(d => d.Title!)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.True(resolver.GetMapping(new Field("title")).Found);
    }

    [Fact]
    public void GetMapping_WithAliasMissingPath_ReturnsUnmappedAliasPathConsistently()
    {
        // Arrange
        var properties = CreateProperties(("alias", new FieldAliasProperty()));
        using var resolver = new ElasticMappingResolver(() => new TypeMapping { Properties = properties }, Inferrer, logger: Logger);

        // Act
        var first = resolver.GetMapping("alias", followAlias: true);
        var cached = resolver.GetMapping("alias", followAlias: true);

        // Assert
        Assert.False(first.Found);
        Assert.False(cached.Found);
        Assert.Equal("alias", first.FullPath);
        Assert.Equal("alias", resolver.GetResolvedField("alias"));
        Assert.IsType<FieldAliasProperty>(resolver.GetMappingProperty("alias"));
    }

    [Fact]
    public void GetMapping_WithAlias_FollowsToTargetOnFreshAndCachedLookups()
    {
        // Arrange
        var serverMapping = new TypeMapping
        {
            Properties = CreateProperties(("alias", new FieldAliasProperty { Path = "target" }), ("target", new KeywordProperty()))
        };
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, logger: Logger);

        // Act
        var followed = resolver.GetMapping("alias", followAlias: true);
        var cachedFollowed = resolver.GetMapping("alias", followAlias: true);
        var unfollowed = resolver.GetMapping("alias");

        // Assert
        Assert.True(followed.Found);
        Assert.Equal("target", followed.FullPath);
        Assert.IsType<KeywordProperty>(followed.Property);
        Assert.Same(followed, cachedFollowed);
        Assert.True(unfollowed.Found);
        Assert.Equal("alias", unfollowed.FullPath);
        Assert.IsType<FieldAliasProperty>(unfollowed.Property);
        Assert.Equal("target", resolver.GetResolvedField("ALIAS"));
    }

    [Fact]
    public void GetMapping_WithCodeAliasDeclaredWithExpression_FollowsInferredTarget()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create<Document>(m => m.Properties(p => p
            .Text(d => d.Title!)
            .FieldAlias("heading", a => a.Path(d => d.Title!))), Inferrer, logger: Logger);

        // Act
        var mapping = resolver.GetMapping("heading", followAlias: true);

        // Assert
        Assert.True(mapping.Found);
        Assert.Equal("title", mapping.FullPath);
        Assert.IsType<TextProperty>(mapping.Property);
    }

    [Theory]
    [InlineData("keyword")]
    [InlineData("date")]
    [InlineData("long")]
    [InlineData("boolean")]
    [InlineData("ip")]
    public void GetMapping_WithMultiFieldOnNonTextProperty_ResolvesSubField(string propertyType)
    {
        // Arrange - every property type can carry multi-fields, not just text.
        var property = CreateProperty(propertyType);
        SetMultiFields(property, CreateProperties(("sort", new KeywordProperty())));
        var properties = CreateProperties(("code", property));
        using var resolver = new ElasticMappingResolver(() => new TypeMapping { Properties = properties }, Inferrer, logger: Logger);

        // Act
        var parent = resolver.GetMapping("code");
        var subField = resolver.GetMapping("code.sort");

        // Assert
        Assert.True(parent.Found);
        Assert.True(subField.Found);
        Assert.Equal("code.sort", subField.FullPath);
        Assert.IsType<KeywordProperty>(subField.Property);
    }

    [Theory]
    [InlineData("keyword")]
    [InlineData("date")]
    [InlineData("long")]
    [InlineData("boolean")]
    [InlineData("ip")]
    public void GetMapping_WithCodeDeclaredMultiFieldOnNonTextServerProperty_ResolvesSubField(string propertyType)
    {
        // Arrange - the server reports the property without the multi-field the code mapping declares.
        var codeProperty = CreateProperty(propertyType);
        SetMultiFields(codeProperty, CreateProperties(("sort", new KeywordProperty())));
        var codeMapping = new TypeMapping { Properties = CreateProperties(("code", codeProperty)) };
        var serverMapping = new TypeMapping { Properties = CreateProperties(("code", CreateProperty(propertyType))) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        var subField = resolver.GetMapping("code.sort");

        // Assert
        Assert.True(subField.Found);
        Assert.Equal("code.sort", subField.FullPath);
        Assert.IsType<KeywordProperty>(subField.Property);
    }

    [Fact]
    public void GetMapping_WithCodeAndServerMappings_DoesNotMutateEitherMapping()
    {
        // Arrange - the merged view must not be written back into the mapping the loader returned.
        var codeProperty = new KeywordProperty { Fields = CreateProperties(("sort", new KeywordProperty())) };
        var codeMapping = new TypeMapping { Properties = CreateProperties(("code", codeProperty)) };
        var serverProperty = new KeywordProperty();
        var serverMapping = new TypeMapping { Properties = CreateProperties(("code", serverProperty)) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        var subField = resolver.GetMapping("code.sort");

        // Assert
        Assert.True(subField.Found);
        Assert.Null(serverProperty.Fields);
        Assert.Single(codeProperty.Fields!);
        Assert.NotSame(serverProperty, resolver.GetMappingProperty("code"));
    }

    [Fact]
    public void GetMapping_WithIncompatibleCodeAndServerParentTypes_DoesNotFabricateChildField()
    {
        // Arrange - the server mapping is authoritative about whether a field is a container or a leaf.
        var codeMapping = new TypeMapping
        {
            Properties = CreateProperties(("value", new ObjectProperty { Properties = CreateProperties(("child", new KeywordProperty())) }))
        };
        var serverMapping = new TypeMapping { Properties = CreateProperties(("value", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        var mapping = resolver.GetMapping("value.child");

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal("value.child", mapping.FullPath);
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("value"));
    }

    [Fact]
    public void GetMapping_WithIncompatibleCodeAndServerScalarTypes_DoesNotFabricateChildField()
    {
        // Arrange - a server scalar with a different type must not inherit code-only multi-fields.
        var codeMapping = new TypeMapping
        {
            Properties = CreateProperties(("value", new TextProperty { Fields = CreateProperties(("keyword", new KeywordProperty())) }))
        };
        var serverMapping = new TypeMapping { Properties = CreateProperties(("value", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        var mapping = resolver.GetMapping("value.keyword");

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal("value.keyword", mapping.FullPath);
    }

    [Fact]
    public void GetMapping_WithCodeOnlyProperty_IncludesItAlongsideServerProperties()
    {
        // Arrange
        var codeMapping = new TypeMapping { Properties = CreateProperties(("code_only", new DateProperty())) };
        var serverMapping = new TypeMapping { Properties = CreateProperties(("server_only", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act & Assert
        Assert.IsType<DateProperty>(resolver.GetMappingProperty("code_only"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("server_only"));
    }

    [Fact]
    public void GetNonAnalyzedFieldName_WithCodeDeclaredKeywordSubField_UsesCodeDeclaredSubField()
    {
        // Arrange - the sub-field may only be declared in code.
        var codeMapping = new TypeMapping
        {
            Properties = CreateProperties(("name", new TextProperty { Fields = CreateProperties(("keyword", new KeywordProperty())) }))
        };
        var serverMapping = new TypeMapping { Properties = CreateProperties(("name", new TextProperty())) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        string resolved = resolver.GetNonAnalyzedFieldName("name");

        // Assert
        Assert.Equal("name.keyword", resolved);
    }

    [Fact]
    public void GetMapping_WithSharedServerPropertyInstanceAndDistinctCodeSubFields_DoesNotLeakSubFieldsAcrossFields()
    {
        // Arrange - reusing one property instance for several fields is legal, so merged children are keyed by name.
        var alphaCode = new KeywordProperty { Fields = CreateProperties(("alpha_only", new KeywordProperty())) };
        var betaCode = new KeywordProperty { Fields = CreateProperties(("beta_only", new KeywordProperty())) };
        var codeMapping = new TypeMapping { Properties = CreateProperties(("alpha", alphaCode), ("beta", betaCode)) };
        var shared = new KeywordProperty();
        var serverMapping = new TypeMapping { Properties = CreateProperties(("alpha", shared), ("beta", shared)) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act & Assert
        Assert.True(resolver.GetMapping("alpha.alpha_only").Found);
        Assert.True(resolver.GetMapping("beta.beta_only").Found);
        Assert.False(resolver.GetMapping("alpha.beta_only").Found);
        Assert.False(resolver.GetMapping("beta.alpha_only").Found);
    }

    [Fact]
    public void GetMapping_WithCodeSubPropertyUnderServerNestedProperty_ResolvesCodeSubProperty()
    {
        // Arrange - the server only knows sub fields that have been indexed, so code sub properties are merged in.
        var codeMapping = new TypeMapping
        {
            Properties = CreateProperties(("items", new NestedProperty { Properties = CreateProperties(("code_only", new KeywordProperty())) }))
        };
        var serverMapping = new TypeMapping
        {
            Properties = CreateProperties(("items", new NestedProperty { Properties = CreateProperties(("server_only", new KeywordProperty())) }))
        };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () => serverMapping, logger: Logger);

        // Act
        var codeOnly = resolver.GetMapping("items.code_only");
        var serverOnly = resolver.GetMapping("items.server_only");

        // Assert
        Assert.True(codeOnly.Found);
        Assert.IsType<KeywordProperty>(codeOnly.Property);
        Assert.True(serverOnly.Found);
        Assert.True(resolver.IsNestedPropertyType("items"));
    }

    [Fact]
    public void GetResolvedField_WithPropertyInstanceSharedByMultipleFields_ResolvesEachFieldName()
    {
        // Arrange
        var shared = new KeywordProperty();
        var serverMapping = new TypeMapping { Properties = CreateProperties(("alpha", shared), ("beta", shared)) };
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, logger: Logger);

        // Act & Assert
        Assert.Equal("alpha", resolver.GetResolvedField("alpha"));
        Assert.Equal("beta", resolver.GetResolvedField("beta"));
    }

    [Fact]
    public void GetResolvedField_WithMissingTextSubField_PreservesOriginalPath()
    {
        // Arrange
        var serverMapping = new TypeMapping { Properties = CreateProperties(("Body", new TextProperty())) };
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, logger: Logger);

        // Act
        string resolved = resolver.GetResolvedField("body.Keyword");

        // Assert - the mapped part is canonicalized and the unmapped remainder is kept as requested.
        Assert.Equal("Body.Keyword", resolved);
    }

    [Fact]
    public void GetMapping_WithDifferentFieldCasing_ResolvesCanonicalNameFromEverySpelling()
    {
        // Arrange
        var serverMapping = new TypeMapping { Properties = CreateProperties(("MixedCase", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, logger: Logger);

        // Act
        var exact = resolver.GetMapping("MixedCase");
        var lower = resolver.GetMapping("mixedcase");
        var upper = resolver.GetMapping("MIXEDCASE");

        // Assert
        Assert.Equal("MixedCase", exact.FullPath);
        Assert.Equal("MixedCase", lower.FullPath);
        Assert.Equal("MixedCase", upper.FullPath);
        Assert.IsType<KeywordProperty>(upper.Property);
    }

    [Fact]
    public void GetMapping_WithFieldsDifferingOnlyByCase_PrefersExactMatch()
    {
        // Arrange
        var serverMapping = new TypeMapping { Properties = CreateProperties(("Name", new TextProperty()), ("name", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, logger: Logger);

        // Act & Assert
        Assert.IsType<TextProperty>(resolver.GetMappingProperty("Name"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void GetMapping_WithBlankField_ReturnsUnmappedFieldWithoutLoading(string field)
    {
        // Arrange
        int loads = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            loads++;
            return CreateTextOnlyMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var mapping = resolver.GetMapping(field);

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal(field, mapping.FullPath);
        Assert.Equal(field, resolver.GetResolvedField(field));
        Assert.Equal(field, resolver.GetSortFieldName(field));
        Assert.True(resolver.IsPropertyAnalyzed(field));
        Assert.False(resolver.IsNestedPropertyType(field));
        Assert.Equal(FieldType.None, resolver.GetFieldType(field));
        Assert.Equal(0, loads);
    }

    [Fact]
    public void GetMapping_WithNestedProperties_ReportsNestedPathChainOutermostFirst()
    {
        // Arrange
        var grand = new NestedProperty { Properties = CreateProperties(("value", new LongNumberProperty())) };
        var details = new ObjectProperty { Properties = CreateProperties(("Grand", grand)) };
        var children = new NestedProperty { Properties = CreateProperties(("name", new KeywordProperty()), ("details", details)) };
        var mapping = new TypeMapping { Properties = CreateProperties(("Children", children), ("plain", new KeywordProperty())) };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger);

        // Act
        var leaf = resolver.GetMapping("children.details.grand.value");
        var parent = resolver.GetMapping("children");
        var missingLeaf = resolver.GetMapping("children.details.grand.missing");
        var plain = resolver.GetMapping("plain");

        // Assert
        Assert.Equal("Children.details.Grand.value", leaf.FullPath);
        Assert.Equal(["Children", "Children.details.Grand"], leaf.NestedPathChain);
        Assert.Equal("Children.details.Grand", leaf.NestedPath);
        Assert.Equal(["Children"], parent.NestedPathChain);
        Assert.False(missingLeaf.Found);
        Assert.Equal("Children.details.Grand.missing", missingLeaf.FullPath);
        Assert.Equal("Children.details.Grand", missingLeaf.NestedPath);
        Assert.Empty(plain.NestedPathChain);
        Assert.Null(plain.NestedPath);
    }

    [Fact]
    public void NestedPathResolver_WithNestedField_ReturnsDeepestPathAndChain()
    {
        // Arrange
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(("children", new NestedProperty
            {
                Properties = CreateProperties(("grand", new NestedProperty { Properties = CreateProperties(("name", new KeywordProperty())) }))
            }), ("plain", new KeywordProperty()))
        };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger);

        // Act & Assert
        Assert.Equal("children.grand", NestedPathResolver.GetDeepestNestedPath("Children.Grand.name", resolver));
        Assert.Equal("children.grand", NestedPathResolver.GetDeepestNestedPath("children.grand.missing", resolver));
        Assert.Null(NestedPathResolver.GetDeepestNestedPath("plain", resolver));
        Assert.Equal(["children", "children.grand"], NestedPathResolver.GetNestedPathChain("children.grand", resolver));
        Assert.Equal(["unknown"], NestedPathResolver.GetNestedPathChain("unknown", resolver));
        Assert.Equal("nested_children", NestedPathResolver.GetNestedAggName("children"));
    }

    [Theory]
    [InlineData("text", true)]
    [InlineData("text-not-indexed", false)]
    [InlineData("match_only_text", false)]
    [InlineData("search_as_you_type", false)]
    [InlineData("keyword", false)]
    [InlineData("long", false)]
    [InlineData("date", false)]
    [InlineData("missing", false)]
    public void IsPropertyAnalyzed_ForPropertyType_ReportsWhetherFieldIsAnalyzed(string propertyType, bool expected)
    {
        // Arrange
        var properties = new Properties();
        IProperty? property = propertyType switch
        {
            "text" => new TextProperty(),
            "text-not-indexed" => new TextProperty { Index = false },
            "match_only_text" => new MatchOnlyTextProperty(),
            "search_as_you_type" => new SearchAsYouTypeProperty(),
            "missing" => null,
            _ => CreateProperty(propertyType)
        };
        if (property is not null)
            properties.Add("field", property);

        using var resolver = new ElasticMappingResolver(() => new TypeMapping { Properties = properties }, Inferrer, logger: Logger);

        // Act
        bool analyzed = resolver.IsPropertyAnalyzed("field");

        // Assert
        Assert.Equal(expected, analyzed);
        Assert.Equal(expected, resolver.IsPropertyAnalyzed(property));
    }

    [Fact]
    public void PropertyTypePredicates_ForMatchingAndNonMatchingFields_ReportPropertyCategory()
    {
        // Arrange
        var properties = CreateProperties(
            ("location", new GeoPointProperty()),
            ("count", new LongNumberProperty()),
            ("ratio", new ScaledFloatNumberProperty()),
            ("big", new UnsignedLongNumberProperty()),
            ("enabled", new BooleanProperty()),
            ("created", new DateProperty()),
            ("precise", new DateNanosProperty()),
            ("items", new NestedProperty()),
            ("name", new KeywordProperty()),
            ("count_alias", new FieldAliasProperty { Path = "count" }));
        using var resolver = new ElasticMappingResolver(() => new TypeMapping { Properties = properties }, Inferrer, logger: Logger);

        // Act & Assert
        Assert.True(resolver.IsGeoPropertyType("location"));
        Assert.False(resolver.IsGeoPropertyType("name"));
        Assert.True(resolver.IsNumericPropertyType("count"));
        Assert.True(resolver.IsNumericPropertyType("ratio"));
        Assert.True(resolver.IsNumericPropertyType("big"));
        Assert.True(resolver.IsNumericPropertyType("count_alias"));
        Assert.False(resolver.IsNumericPropertyType("name"));
        Assert.True(resolver.IsBooleanPropertyType("enabled"));
        Assert.False(resolver.IsBooleanPropertyType("name"));
        Assert.True(resolver.IsDatePropertyType("created"));
        Assert.True(resolver.IsDatePropertyType("precise"));
        Assert.False(resolver.IsDatePropertyType("name"));
        Assert.True(resolver.IsNestedPropertyType("items"));
        Assert.False(resolver.IsNestedPropertyType("name"));
        Assert.False(resolver.IsNestedPropertyType("missing"));
    }

    public static TheoryData<string, FieldType> FieldTypes => new()
    {
        { "counter", FieldType.Long },
        { "timestamp", FieldType.DateNanos },
        { "suggest", FieldType.SearchAsYouType },
        { "tenant", FieldType.ConstantKeyword },
        { "labels", FieldType.Flattened },
        { "relation", FieldType.Join },
        { "body", FieldType.MatchOnlyText },
        { "shape", FieldType.Shape },
        { "point", FieldType.Shape },
        { "alias", FieldType.Integer },
        { "vectors", FieldType.None },
        { "missing", FieldType.None }
    };

    [Theory]
    [MemberData(nameof(FieldTypes))]
    public void GetFieldType_ForPropertyType_ReturnsClientFieldType(string field, FieldType expected)
    {
        // Arrange
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(
                ("counter", new UnsignedLongNumberProperty()),
                ("timestamp", new DateNanosProperty()),
                ("suggest", new SearchAsYouTypeProperty()),
                ("tenant", new ConstantKeywordProperty()),
                ("labels", new FlattenedProperty()),
                ("relation", new JoinProperty()),
                ("body", new MatchOnlyTextProperty()),
                ("shape", new ShapeProperty()),
                ("point", new PointProperty()),
                ("number", new IntegerNumberProperty()),
                ("alias", new FieldAliasProperty { Path = "number" }),
                ("vectors", new RankVectorProperty()))
        };
        using var resolver = new ElasticMappingResolver(mapping, Inferrer, logger: Logger);

        // Act
        var result = resolver.GetFieldType(field);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFieldType_WithNullProperty_ReturnsNone()
    {
        Assert.Equal(FieldType.None, ElasticMappingResolver.GetFieldType(null));
        Assert.Equal(FieldType.Alias, ElasticMappingResolver.GetFieldType(new FieldAliasProperty()));
    }

    [Fact]
    public void NullInstance_WhenResolvingAnyField_ReportsFieldAsUnmapped()
    {
        // Arrange - the shared null resolver never has a mapping, so every field resolves to its own name.
        var resolver = ElasticMappingResolver.NullInstance;

        // Act
        var mapping = resolver.GetMapping("name");

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal("name", mapping.FullPath);
        Assert.Equal("name", resolver.GetSortFieldName("name"));
        Assert.True(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
    }

    [Fact]
    public async Task NullInstance_WhenDisposedOrRefreshed_RemainsUsable()
    {
        // Arrange - NullInstance is shared process wide, so disposing it must not poison it for other consumers.
        var resolver = ElasticMappingResolver.NullInstance;

        // Act
        resolver.Dispose();
        resolver.RefreshMapping();
        await resolver.RefreshAsync(TestCancellationToken);

        // Assert
        Assert.False(resolver.GetMapping("name").Found);
        Assert.False(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
    }

    [Fact]
    public void Constructors_WithNullMappingSource_ThrowArgumentNullException()
    {
        // A resolver without any mapping source is a configuration error and fails at construction.
        Assert.Throws<ArgumentNullException>(() => new ElasticMappingResolver((Func<TypeMapping?>)null!, Inferrer));
        Assert.Throws<ArgumentNullException>(() => new ElasticMappingResolver((TypeMapping)null!, Inferrer));
        Assert.Throws<ArgumentNullException>(() => ElasticMappingResolver.Create((TypeMapping)null!));
        Assert.Throws<ArgumentNullException>(() => ElasticMappingResolver.CreateWithAsyncLoader(null!));
        Assert.Throws<ArgumentNullException>(() => ElasticMappingResolver.Create((ElasticsearchClient)null!, "index"));
    }

    [Fact]
    public void Create_WithDescriptorAndMappingHelpers_ResolvesDeclaredSubFields()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create<Document>(m => m.Properties(p => p
            .Text(d => d.Title!, t => t.AddKeywordAndSortFields())
            .Text(d => d.Body!, t => t.AddKeywordField(lowercase: true))
            .Text(d => d.Summary!, t => t.AddSortField())), Inferrer, logger: Logger);

        // Act & Assert
        Assert.Equal("title.sort", resolver.GetSortFieldName("Title"));
        Assert.Equal("title.keyword", resolver.GetAggregationsFieldName("title"));
        Assert.Equal("lowercase", Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("body.keyword")).Normalizer);
        Assert.Equal("sort", Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("summary.sort")).Normalizer);
        Assert.Equal(256, Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("title.keyword")).IgnoreAbove);
        Assert.True(resolver.IsLoaded);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void UnmappedFieldRefreshInterval_WithNonPositiveValue_ThrowsArgumentOutOfRangeException(int seconds)
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(() => null, Inferrer, logger: Logger);

        // Act
        var exception = Record.Exception(() => resolver.UnmappedFieldRefreshInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        var argumentException = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal("value", argumentException.ParamName);
    }

    public static TheoryData<TimeSpan> InvalidWaitTimeouts => new() { TimeSpan.Zero, TimeSpan.FromSeconds(-1), Timeout.InfiniteTimeSpan, TimeSpan.FromDays(30) };

    [Theory]
    [MemberData(nameof(InvalidWaitTimeouts))]
    public void MappingRefreshWaitTimeout_WithUnsupportedValue_ThrowsArgumentOutOfRangeException(TimeSpan timeout)
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(() => null, Inferrer, logger: Logger);

        // Act
        var exception = Record.Exception(() => resolver.MappingRefreshWaitTimeout = timeout);

        // Assert
        var argumentException = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal("value", argumentException.ParamName);
    }

    [Fact]
    public void RefreshSettings_ByDefault_UseDocumentedValues()
    {
        // Arrange & Act
        using var resolver = new ElasticMappingResolver(() => CreateTextWithKeywordMapping("name"), Inferrer, logger: Logger);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), resolver.MappingRefreshWaitTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), resolver.UnmappedFieldRefreshInterval);
        Assert.Equal("keyword", resolver.KeywordSubFieldName);
        Assert.Equal("sort", resolver.SortSubFieldName);
    }

    [Fact]
    public async Task PropertyMetadata_WithConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create(new TypeMapping());
        var properties = Enumerable.Range(0, 100).Select(_ => new KeywordProperty()).ToArray();

        // Act
        await Task.WhenAll(properties.Select((property, index) => Task.Run(() =>
        {
            resolver.SetPropertyMetadataValue(property, "index", index);
            resolver.SetPropertyMetadataValue(property, "name", $"property_{index}");
        }, TestCancellationToken)));
        var results = await Task.WhenAll(properties.Select((property, index) => Task.Run(() =>
            (Index: index, ActualIndex: resolver.GetPropertyMetadataValue<int>(property, "index"), ActualName: resolver.GetPropertyMetadataValue<string>(property, "name")),
            TestCancellationToken)));

        // Assert
        foreach (var result in results)
        {
            Assert.Equal(result.Index, result.ActualIndex);
            Assert.Equal($"property_{result.Index}", result.ActualName);
        }
    }

    [Fact]
    public void PropertyMetadata_WhenCopiedOrConverted_PreservesValues()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create(new TypeMapping());
        var source = new KeywordProperty();
        resolver.SetPropertyMetadataValue(source, "key1", "value1");
        resolver.SetPropertyMetadataValue(source, "key2", 42);
        var target = new TextProperty();

        // Act
        resolver.CopyPropertyMetadata(source, target);

        // Assert
        Assert.Equal("value1", resolver.GetPropertyMetadataValue<string>(target, "key1"));
        Assert.Equal(42, resolver.GetPropertyMetadataValue<int>(target, "key2"));
        Assert.Equal(42L, resolver.GetPropertyMetadataValue<long>(target, "key2"));
        Assert.Equal(-1, resolver.GetPropertyMetadataValue(target, "key1", -1));
        Assert.Equal("fallback", resolver.GetPropertyMetadataValue(new KeywordProperty(), "key1", "fallback"));
        Assert.Equal(2, resolver.GetPropertyMetadata(target).Count);
    }

    private static TypeMapping CreateTypedFieldMapping() => new()
    {
        Properties = new Properties
        {
            { "title", new TextProperty { Fields = new Properties { { "keyword", new KeywordProperty() }, { "sort", new KeywordProperty() } } } },
            { "status", new KeywordProperty() },
            { "alias", new FieldAliasProperty { Path = "title" } },
            { "status-alias", new FieldAliasProperty { Path = "status" } }
        }
    };

    private static string GetFieldName(ElasticMappingResolver resolver, string field, string helper) => helper switch
    {
        "sort" => resolver.GetSortFieldName(field),
        "aggregation" => resolver.GetAggregationsFieldName(field),
        _ => resolver.GetNonAnalyzedFieldName(field)
    };

    private static string GetFieldName(ElasticMappingResolver resolver, Field field, string helper) => helper switch
    {
        "sort" => resolver.GetSortFieldName(field),
        "aggregation" => resolver.GetAggregationsFieldName(field),
        _ => resolver.GetNonAnalyzedFieldName(field)
    };

    private sealed class Document
    {
        public string? Title { get; set; }

        public string? Body { get; set; }

        public string? Summary { get; set; }
    }
}
