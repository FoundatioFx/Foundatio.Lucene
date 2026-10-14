using System.Runtime.CompilerServices;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>Reads and replaces the child properties of client mapping properties without mutating them.</summary>
internal static class MappingProperty
{
    /// <summary>
    /// Returns the properties reachable through a field name below <paramref name="property"/>: the
    /// <c>properties</c> of object-like types and the <c>fields</c> (multi-fields) of every other type.
    /// </summary>
    public static Properties? GetChildren(IProperty property) => property switch
    {
        ObjectProperty p => p.Properties,
        NestedProperty p => p.Properties,
        PassthroughObjectProperty p => p.Properties,
        _ => property.GetFields()
    };

    /// <summary>
    /// Returns <paramref name="property"/> when its children already match <paramref name="children"/>, otherwise
    /// a shallow copy whose child collection is replaced. The original property is never modified, so loader-owned
    /// mapping instances stay safe to cache and share.
    /// </summary>
    public static IProperty WithChildren(IProperty property, MergedProperties? children)
    {
        if (children is null)
            return property;

        var original = GetChildren(property);
        if (original is not null && HasSameChildren(original, children))
            return property;

        var properties = new Properties();
        foreach (var child in children.Nodes)
            properties.Add(child.Name, child.Property);

        // Client properties have no copy constructor. A shallow copy preserves every setting and field
        // expression; only the child collection is replaced.
        var copy = ShallowClone(property);
        if (!TrySetChildren(copy, properties))
            return property;

        return copy;
    }

    private static bool HasSameChildren(Properties original, MergedProperties children)
    {
        int count = 0;
        foreach (var pair in original)
        {
            count++;
            if (pair.Key.Name is not { } name
                || !children.TryGetNode(name, out var child)
                || child.Name != name
                || !ReferenceEquals(pair.Value, child.Property))
                return false;
        }

        return count == children.Count;
    }

    private static bool TrySetChildren(IProperty property, Properties children)
    {
        switch (property)
        {
            case ObjectProperty p: p.Properties = children; break;
            case NestedProperty p: p.Properties = children; break;
            case PassthroughObjectProperty p: p.Properties = children; break;
            case AggregateMetricDoubleProperty p: p.Fields = children; break;
            case BinaryProperty p: p.Fields = children; break;
            case BooleanProperty p: p.Fields = children; break;
            case ByteNumberProperty p: p.Fields = children; break;
            case CompletionProperty p: p.Fields = children; break;
            case ConstantKeywordProperty p: p.Fields = children; break;
            case CountedKeywordProperty p: p.Fields = children; break;
            case DateNanosProperty p: p.Fields = children; break;
            case DateProperty p: p.Fields = children; break;
            case DateRangeProperty p: p.Fields = children; break;
            case DenseVectorProperty p: p.Fields = children; break;
            case DoubleNumberProperty p: p.Fields = children; break;
            case DoubleRangeProperty p: p.Fields = children; break;
            case DynamicProperty p: p.Fields = children; break;
            case ExponentialHistogramProperty p: p.Fields = children; break;
            case FieldAliasProperty p: p.Fields = children; break;
            case FlattenedProperty p: p.Fields = children; break;
            case FloatNumberProperty p: p.Fields = children; break;
            case FloatRangeProperty p: p.Fields = children; break;
            case GeoPointProperty p: p.Fields = children; break;
            case GeoShapeProperty p: p.Fields = children; break;
            case HalfFloatNumberProperty p: p.Fields = children; break;
            case HistogramProperty p: p.Fields = children; break;
            case IcuCollationProperty p: p.Fields = children; break;
            case IntegerNumberProperty p: p.Fields = children; break;
            case IntegerRangeProperty p: p.Fields = children; break;
            case IpProperty p: p.Fields = children; break;
            case IpRangeProperty p: p.Fields = children; break;
            case JoinProperty p: p.Fields = children; break;
            case KeywordProperty p: p.Fields = children; break;
            case LongNumberProperty p: p.Fields = children; break;
            case LongRangeProperty p: p.Fields = children; break;
            case MatchOnlyTextProperty p: p.Fields = children; break;
            case Murmur3HashProperty p: p.Fields = children; break;
            case PercolatorProperty p: p.Fields = children; break;
            case PointProperty p: p.Fields = children; break;
            case RankFeatureProperty p: p.Fields = children; break;
            case RankFeaturesProperty p: p.Fields = children; break;
            case RankVectorProperty p: p.Fields = children; break;
            case ScaledFloatNumberProperty p: p.Fields = children; break;
            case SearchAsYouTypeProperty p: p.Fields = children; break;
            case SemanticTextProperty p: p.Fields = children; break;
            case ShapeProperty p: p.Fields = children; break;
            case ShortNumberProperty p: p.Fields = children; break;
            case SparseVectorProperty p: p.Fields = children; break;
            case TextProperty p: p.Fields = children; break;
            case TokenCountProperty p: p.Fields = children; break;
            case UnsignedLongNumberProperty p: p.Fields = children; break;
            case VersionProperty p: p.Fields = children; break;
            case WildcardProperty p: p.Fields = children; break;
            default: return false;
        }

        return true;
    }

    /// <summary>Returns a memberwise copy of a client mapping object, which has no copy constructor.</summary>
    public static T ShallowClone<T>(T instance) where T : class => (T)MemberwiseClone(instance);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "MemberwiseClone")]
    private static extern object MemberwiseClone(object instance);
}
