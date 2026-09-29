using Elastic.Clients.Elasticsearch.Mapping;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>The result of resolving a field name against an Elasticsearch mapping.</summary>
public sealed class FieldMapping
{
    /// <summary>Creates a field mapping.</summary>
    /// <param name="fullPath">The canonical field path.</param>
    /// <param name="property">The mapped property, or <see langword="null"/> when the field is not mapped.</param>
    /// <param name="nestedPathChain">The canonical paths of the <c>nested</c> ancestors of the field, outermost first.</param>
    public FieldMapping(string fullPath, IProperty? property, IReadOnlyList<string>? nestedPathChain = null)
        : this(fullPath, property, null, nestedPathChain)
    {
    }

    internal FieldMapping(string fullPath, IProperty? property, MergedProperties? children, IReadOnlyList<string>? nestedPathChain)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        FullPath = fullPath;
        Property = property;
        Children = children;
        NestedPathChain = nestedPathChain ?? [];
    }

    /// <summary>
    /// The canonical field path. Every part that exists in the mapping uses its mapped spelling; parts below the
    /// deepest mapped one are kept exactly as requested.
    /// </summary>
    public string FullPath { get; }

    /// <summary>The mapped property, or <see langword="null"/> when the field is not mapped.</summary>
    public IProperty? Property { get; }

    /// <summary>Whether the field exists in the mapping.</summary>
    public bool Found => Property is not null;

    /// <summary>
    /// The canonical paths of the <c>nested</c> properties on the path to this field, outermost first, including
    /// the field itself when it is nested. Empty when the field is not below a nested property. Known nested
    /// ancestors are reported even when the field itself is not mapped.
    /// </summary>
    public IReadOnlyList<string> NestedPathChain { get; }

    /// <summary>The deepest nested path of <see cref="NestedPathChain"/>, or <see langword="null"/> when the field is not nested.</summary>
    public string? NestedPath => NestedPathChain.Count > 0 ? NestedPathChain[^1] : null;

    /// <summary>
    /// Merged sub-objects and multi-fields of this field, captured during resolution so sub-field lookups do not
    /// have to walk the mapping again.
    /// </summary>
    internal MergedProperties? Children { get; }
}

/// <summary>A name-indexed merged view of the properties at one mapping level.</summary>
internal sealed class MergedProperties
{
    private readonly Dictionary<string, MergedNode> _byExactName;
    private readonly Dictionary<string, MergedNode> _byIgnoreCaseName;

    public MergedProperties(IReadOnlyList<MergedNode> nodes)
    {
        Nodes = nodes;
        _byExactName = new Dictionary<string, MergedNode>(nodes.Count, StringComparer.Ordinal);
        _byIgnoreCaseName = new Dictionary<string, MergedNode>(nodes.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            _byExactName[node.Name] = node;
            _byIgnoreCaseName.TryAdd(node.Name, node);
        }
    }

    public int Count => Nodes.Count;

    public IReadOnlyList<MergedNode> Nodes { get; }

    public bool TryGetNode(string name, out MergedNode node)
    {
        return _byExactName.TryGetValue(name, out node!) || _byIgnoreCaseName.TryGetValue(name, out node!);
    }
}

/// <summary>A canonical property and the merged children reachable through its field name.</summary>
internal sealed class MergedNode(string name, IProperty property, MergedProperties? children)
{
    public string Name { get; } = name;

    public IProperty Property { get; } = property;

    public MergedProperties? Children { get; } = children;
}
