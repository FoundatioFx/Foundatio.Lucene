namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a regular expression query (<c>/pattern/</c>).
/// </summary>
public class RegexNode : QueryNode, IBoostable
{
    private TextValue _pattern;

    /// <summary>
    /// The regex pattern (without the enclosing slashes) as a memory slice.
    /// </summary>
    public ReadOnlyMemory<char> PatternMemory
    {
        get => _pattern.Memory;
        set => _pattern.Memory = value;
    }

    /// <summary>
    /// The regex pattern without the enclosing slashes. Escape sequences are preserved.
    /// </summary>
    public string Pattern
    {
        get => _pattern.GetString();
        set => _pattern.SetString(value);
    }

    /// <inheritdoc/>
    public string? BoostText { get; set; }

    /// <inheritdoc/>
    public float? Boost
    {
        get => Modifiers.ParseBoost(BoostText);
        set => BoostText = Modifiers.FormatBoost(value);
    }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new RegexNode { PatternMemory = PatternMemory, BoostText = BoostText });
}
