namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a quoted phrase.
/// </summary>
public class PhraseNode : QueryNode, IBoostable, IProximityModifiable
{
    private TextValue _phrase;

    /// <summary>
    /// The phrase text (without quotes, escape sequences processed) as a memory slice.
    /// </summary>
    public ReadOnlyMemory<char> PhraseMemory
    {
        get => _phrase.Memory;
        set => _phrase.Memory = value;
    }

    /// <summary>
    /// The phrase text without quotes, with escape sequences processed.
    /// </summary>
    public string Phrase
    {
        get => _phrase.GetString();
        set => _phrase.SetString(value);
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
    public string? ProximityText { get; set; }

    /// <summary>
    /// The phrase slop for proximity queries (<c>"a b"~N</c>), or null when not specified or not an integer.
    /// </summary>
    public int? Slop
    {
        get => Modifiers.ParseInt(ProximityText);
        set => ProximityText = Modifiers.FormatInt(value);
    }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new PhraseNode { PhraseMemory = PhraseMemory, BoostText = BoostText, ProximityText = ProximityText });
}
