using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>Helpers for declaring and inspecting Elasticsearch mappings used by the query builders.</summary>
public static class ElasticMappingExtensions
{
    /// <summary>The default name of the non-analyzed <c>keyword</c> sub-field used for aggregations.</summary>
    public const string KeywordFieldName = "keyword";

    /// <summary>The default name of the normalized <c>keyword</c> sub-field used for sorting.</summary>
    public const string SortFieldName = "sort";

    /// <summary>The name of the normalizer registered by <see cref="AddSortNormalizer"/>.</summary>
    public const string SortNormalizerName = "sort";

    private const int DefaultIgnoreAbove = 256;

    /// <summary>Adds a <see cref="KeywordFieldName"/> keyword sub-field with <c>ignore_above: 256</c>.</summary>
    /// <remarks>Replaces any sub-fields declared earlier on the descriptor. Use <see cref="AddKeywordAndSortFields{T}(TextPropertyDescriptor{T}, string, string?)"/> to add both sub-fields.</remarks>
    public static TextPropertyDescriptor<T> AddKeywordField<T>(this TextPropertyDescriptor<T> descriptor, string? normalizer = null)
    {
        return descriptor.Fields(f => f.Keyword(KeywordFieldName, k => k.Normalizer(normalizer!).IgnoreAbove(DefaultIgnoreAbove)));
    }

    /// <summary>Adds a <see cref="KeywordFieldName"/> keyword sub-field, optionally using the built-in <c>lowercase</c> normalizer.</summary>
    /// <remarks>Replaces any sub-fields declared earlier on the descriptor.</remarks>
    public static TextPropertyDescriptor<T> AddKeywordField<T>(this TextPropertyDescriptor<T> descriptor, bool lowercase)
    {
        return descriptor.AddKeywordField(lowercase ? "lowercase" : null);
    }

    /// <summary>Adds a <see cref="SortFieldName"/> keyword sub-field normalized for sorting, with <c>ignore_above: 256</c>.</summary>
    /// <remarks>
    /// Replaces any sub-fields declared earlier on the descriptor. The default normalizer must be registered on the
    /// index with <see cref="AddSortNormalizer"/>.
    /// </remarks>
    public static TextPropertyDescriptor<T> AddSortField<T>(this TextPropertyDescriptor<T> descriptor, string normalizer = SortNormalizerName)
    {
        return descriptor.Fields(f => f.Keyword(SortFieldName, k => k.Normalizer(normalizer).IgnoreAbove(DefaultIgnoreAbove)));
    }

    /// <summary>Adds both the <see cref="KeywordFieldName"/> and <see cref="SortFieldName"/> keyword sub-fields.</summary>
    /// <remarks>Replaces any sub-fields declared earlier on the descriptor.</remarks>
    public static TextPropertyDescriptor<T> AddKeywordAndSortFields<T>(this TextPropertyDescriptor<T> descriptor,
        string sortNormalizer = SortNormalizerName, string? keywordNormalizer = null)
    {
        return descriptor.Fields(f => f
            .Keyword(KeywordFieldName, k => k.Normalizer(keywordNormalizer!).IgnoreAbove(DefaultIgnoreAbove))
            .Keyword(SortFieldName, k => k.Normalizer(sortNormalizer).IgnoreAbove(DefaultIgnoreAbove)));
    }

    /// <summary>Adds both keyword sub-fields, optionally using the built-in <c>lowercase</c> normalizer for the keyword sub-field.</summary>
    /// <remarks>Replaces any sub-fields declared earlier on the descriptor.</remarks>
    public static TextPropertyDescriptor<T> AddKeywordAndSortFields<T>(this TextPropertyDescriptor<T> descriptor, bool keywordLowercase)
    {
        return descriptor.AddKeywordAndSortFields(keywordNormalizer: keywordLowercase ? "lowercase" : null);
    }

    /// <summary>Registers the <see cref="SortNormalizerName"/> custom normalizer (<c>lowercase</c> and <c>asciifolding</c>).</summary>
    public static IndexSettingsAnalysisDescriptor AddSortNormalizer(this IndexSettingsAnalysisDescriptor descriptor)
    {
        return descriptor.Normalizers(n => n.Custom(SortNormalizerName, c => c.Filter("lowercase", "asciifolding")));
    }

    /// <summary>Returns the multi-fields (<c>fields</c>) of a property, or <see langword="null"/> when it has none.</summary>
    /// <remarks>
    /// The client has no common accessor for multi-fields, so each property type is matched explicitly. Object-like
    /// properties report their <c>fields</c> here, not their <c>properties</c>.
    /// </remarks>
    public static Properties? GetFields(this IProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property switch
        {
            AggregateMetricDoubleProperty p => p.Fields,
            BinaryProperty p => p.Fields,
            BooleanProperty p => p.Fields,
            ByteNumberProperty p => p.Fields,
            CompletionProperty p => p.Fields,
            ConstantKeywordProperty p => p.Fields,
            CountedKeywordProperty p => p.Fields,
            DateNanosProperty p => p.Fields,
            DateProperty p => p.Fields,
            DateRangeProperty p => p.Fields,
            DenseVectorProperty p => p.Fields,
            DoubleNumberProperty p => p.Fields,
            DoubleRangeProperty p => p.Fields,
            DynamicProperty p => p.Fields,
            ExponentialHistogramProperty p => p.Fields,
            FieldAliasProperty p => p.Fields,
            FlattenedProperty p => p.Fields,
            FloatNumberProperty p => p.Fields,
            FloatRangeProperty p => p.Fields,
            GeoPointProperty p => p.Fields,
            GeoShapeProperty p => p.Fields,
            HalfFloatNumberProperty p => p.Fields,
            HistogramProperty p => p.Fields,
            IcuCollationProperty p => p.Fields,
            IntegerNumberProperty p => p.Fields,
            IntegerRangeProperty p => p.Fields,
            IpProperty p => p.Fields,
            IpRangeProperty p => p.Fields,
            JoinProperty p => p.Fields,
            KeywordProperty p => p.Fields,
            LongNumberProperty p => p.Fields,
            LongRangeProperty p => p.Fields,
            MatchOnlyTextProperty p => p.Fields,
            Murmur3HashProperty p => p.Fields,
            NestedProperty p => p.Fields,
            ObjectProperty p => p.Fields,
            PassthroughObjectProperty p => p.Fields,
            PercolatorProperty p => p.Fields,
            PointProperty p => p.Fields,
            RankFeatureProperty p => p.Fields,
            RankFeaturesProperty p => p.Fields,
            RankVectorProperty p => p.Fields,
            ScaledFloatNumberProperty p => p.Fields,
            SearchAsYouTypeProperty p => p.Fields,
            SemanticTextProperty p => p.Fields,
            ShapeProperty p => p.Fields,
            ShortNumberProperty p => p.Fields,
            SparseVectorProperty p => p.Fields,
            TextProperty p => p.Fields,
            TokenCountProperty p => p.Fields,
            UnsignedLongNumberProperty p => p.Fields,
            VersionProperty p => p.Fields,
            WildcardProperty p => p.Fields,
            _ => null
        };
    }
}
