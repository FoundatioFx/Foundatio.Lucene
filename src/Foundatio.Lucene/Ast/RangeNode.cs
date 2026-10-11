namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a range query (<c>[min TO max]</c>, <c>{min TO max}</c>, or a short form such as <c>&gt;=min</c>).
/// </summary>
public class RangeNode : QueryNode, IBoostable, IProximityModifiable
{
    private TextValue _min;
    private TextValue _max;

    /// <summary>
    /// The lower bound (escape sequences processed) as a memory slice. Empty when unbounded.
    /// </summary>
    public ReadOnlyMemory<char> MinMemory
    {
        get => _min.Memory;
        set => _min.Memory = value;
    }

    /// <summary>
    /// The lower bound, or null when unbounded.
    /// </summary>
    public string? Min
    {
        get => _min.IsEmpty ? null : _min.GetString();
        set => _min.SetString(value);
    }

    /// <summary>
    /// The upper bound (escape sequences processed) as a memory slice. Empty when unbounded.
    /// </summary>
    public ReadOnlyMemory<char> MaxMemory
    {
        get => _max.Memory;
        set => _max.Memory = value;
    }

    /// <summary>
    /// The upper bound, or null when unbounded.
    /// </summary>
    public string? Max
    {
        get => _max.IsEmpty ? null : _max.GetString();
        set => _max.SetString(value);
    }

    /// <summary>
    /// Whether the lower bound is inclusive.
    /// </summary>
    public bool MinInclusive { get; set; } = true;

    /// <summary>
    /// Whether the upper bound is inclusive.
    /// </summary>
    public bool MaxInclusive { get; set; } = true;

    /// <summary>
    /// The operator used for short-form ranges (&gt;, &gt;=, &lt;, &lt;=), or null for bracketed ranges.
    /// </summary>
    public RangeOperator? Operator { get; set; }

    /// <inheritdoc/>
    public string? BoostText { get; set; }

    /// <inheritdoc/>
    public float? Boost
    {
        get => Modifiers.ParseBoost(BoostText);
        set => BoostText = Modifiers.FormatBoost(value);
    }

    /// <inheritdoc/>
    public string? ProximityText { get; set; }

    /// <inheritdoc/>
    public override QueryNode Clone()
    {
        return CopyCommonTo(new RangeNode
        {
            MinMemory = MinMemory,
            MaxMemory = MaxMemory,
            MinInclusive = MinInclusive,
            MaxInclusive = MaxInclusive,
            Operator = Operator,
            BoostText = BoostText,
            ProximityText = ProximityText
        });
    }
}

/// <summary>
/// Defines the operator for short-form range queries.
/// </summary>
public enum RangeOperator
{
    /// <summary>Greater than (&gt;)</summary>
    GreaterThan,
    /// <summary>Greater than or equal (&gt;=)</summary>
    GreaterThanOrEqual,
    /// <summary>Less than (&lt;)</summary>
    LessThan,
    /// <summary>Less than or equal (&lt;=)</summary>
    LessThanOrEqual
}
