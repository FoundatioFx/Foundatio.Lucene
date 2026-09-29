using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Merges two property trees into a name-keyed tree. The primary tree is authoritative: a secondary property
/// only contributes its children when it has the same client property type as the primary property with the same
/// name, and secondary-only properties are added. Neither input is modified.
/// </summary>
/// <remarks>
/// Keying children by name rather than by <see cref="IProperty"/> instance is what makes merging work for every
/// property type, and is also why reusing one property instance for several fields cannot leak one field's
/// children into another's.
/// </remarks>
internal sealed class MappingMerger
{
    private readonly Inferrer? _inferrer;
    private readonly Action<IProperty, IProperty>? _copyMetadata;
    private readonly Action<string, IProperty, IProperty>? _onTypeConflict;

    /// <param name="inferrer">Resolves secondary property names and alias paths declared with expressions.</param>
    /// <param name="copyMetadata">Copies resolver metadata from a source property to the merged property.</param>
    /// <param name="onTypeConflict">Invoked with the field path, primary, and secondary property when their types differ.</param>
    public MappingMerger(Inferrer? inferrer = null, Action<IProperty, IProperty>? copyMetadata = null,
        Action<string, IProperty, IProperty>? onTypeConflict = null)
    {
        _inferrer = inferrer;
        _copyMetadata = copyMetadata;
        _onTypeConflict = onTypeConflict;
    }

    public MergedProperties? Merge(Properties? secondary, Properties? primary) => Merge(secondary, primary, null);

    /// <summary>Merges and flattens the result back into a client <see cref="Properties"/> collection.</summary>
    public Properties? MergeToProperties(Properties? secondary, Properties? primary)
    {
        var merged = Merge(secondary, primary);
        if (merged is null)
            return null;

        var properties = new Properties();
        foreach (var node in merged.Nodes)
            properties.Add(node.Name, node.Property);

        return properties;
    }

    private MergedProperties? Merge(Properties? secondary, Properties? primary, string? parentPath)
    {
        if (secondary is null && primary is null)
            return null;

        var nodes = new List<MergedNode>();
        var secondaryByName = IndexSecondaryProperties(secondary);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (primary is not null)
        {
            foreach (var pair in primary)
            {
                string? name = ResolvePropertyName(pair.Key);
                if (name is null || !seen.Add(name))
                    continue;

                Properties? secondaryChildren = null;
                if (secondaryByName.TryGetValue(name, out var secondaryProperty))
                {
                    if (secondaryProperty.GetType() == pair.Value.GetType())
                        secondaryChildren = MappingProperty.GetChildren(secondaryProperty);
                    else
                        _onTypeConflict?.Invoke(CombinePath(parentPath, name), pair.Value, secondaryProperty);
                }

                var children = Merge(secondaryChildren, MappingProperty.GetChildren(pair.Value), ChildPath(parentPath, name));
                var property = MappingProperty.WithChildren(pair.Value, children);
                CopyMetadata(pair.Value, property);
                if (secondaryProperty is not null)
                    CopyMetadata(secondaryProperty, property);

                nodes.Add(new MergedNode(name, property, children));
            }
        }

        foreach (var (name, property) in secondaryByName)
        {
            if (!seen.Add(name))
                continue;

            var children = Merge(MappingProperty.GetChildren(property), null, ChildPath(parentPath, name));
            var mergedProperty = MappingProperty.WithChildren(property, children);
            CopyMetadata(property, mergedProperty);
            nodes.Add(new MergedNode(name, mergedProperty, children));
        }

        return nodes.Count > 0 ? new MergedProperties(nodes) : null;
    }

    /// <summary>
    /// Resolves secondary property names and alias paths through the inferrer, so a code mapping declared with
    /// property expressions is keyed by the same names the server reports.
    /// </summary>
    private Dictionary<string, IProperty> IndexSecondaryProperties(Properties? properties)
    {
        var indexed = new Dictionary<string, IProperty>(StringComparer.Ordinal);
        if (properties is null)
            return indexed;

        foreach (var pair in properties)
        {
            string? name = ResolvePropertyName(pair.Key);
            if (name is null)
                continue;

            var property = pair.Value;
            if (_inferrer is not null && property is FieldAliasProperty { Path: not null } alias)
            {
                var resolvedAlias = new FieldAliasProperty { Path = _inferrer.Field(alias.Path) ?? alias.Path, Fields = alias.Fields, Meta = alias.Meta };
                CopyMetadata(alias, resolvedAlias);
                property = resolvedAlias;
            }

            indexed[name] = property;
        }

        return indexed;
    }

    private string? ResolvePropertyName(PropertyName? key)
    {
        if (key is null)
            return null;

        // A property expression has no literal name until the inferrer resolves it.
        if (_inferrer is not null)
            return _inferrer.PropertyName(key);

        return key.Name;
    }

    private void CopyMetadata(IProperty source, IProperty target)
    {
        if (_copyMetadata is not null && !ReferenceEquals(source, target))
            _copyMetadata(source, target);
    }

    private string? ChildPath(string? parentPath, string name) => _onTypeConflict is null ? null : CombinePath(parentPath, name);

    private static string CombinePath(string? parentPath, string name) => parentPath is null ? name : $"{parentPath}.{name}";
}
