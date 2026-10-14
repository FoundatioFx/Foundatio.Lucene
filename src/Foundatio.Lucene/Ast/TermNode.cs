namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a single term, optionally with wildcards, a fuzzy modifier, or a boost.
/// </summary>
public class TermNode : QueryNode, IBoostable, IProximityModifiable
{
    /// <summary>
    /// Sentinel value returned by <see cref="FuzzyDistance"/> when <c>~</c> was specified without a distance.
    /// </summary>
    public const int DefaultFuzzyDistance = -1;

    /// <summary>
    /// The fuzzy distance used when <c>~</c> was specified without a distance (the Lucene default).
    /// </summary>
    public const int DefaultFuzzyDistanceValue = 2;

    private TextValue _term;
    private string? _unescapedTerm;

    /// <summary>
    /// The term as written, with escape sequences preserved, as a memory slice. For prefix terms the trailing
    /// <c>*</c> is not included.
    /// </summary>
    public ReadOnlyMemory<char> TermMemory
    {
        get => _term.Memory;
        set
        {
            _term.Memory = value;
            _unescapedTerm = null;
        }
    }

    /// <summary>
    /// The term as written, with escape sequences preserved. For prefix terms the trailing <c>*</c> is not
    /// included. Use this value when escaped wildcard characters must be distinguished from real ones.
    /// </summary>
    public string Term
    {
        get => _term.GetString();
        set
        {
            _term.SetString(value);
            _unescapedTerm = null;
        }
    }

    /// <summary>
    /// The term with escape sequences processed. This is the literal text to search for.
    /// </summary>
    public string UnescapedTerm => _unescapedTerm ??= QueryText.Unescape(_term.Span, Term);

    /// <summary>
    /// Whether the term contains escape sequences.
    /// </summary>
    public bool HasEscapes => _term.Span.IndexOf('\\') >= 0;

    /// <summary>
    /// Whether this is a prefix query (a single unescaped trailing <c>*</c>, which is not part of <see cref="Term"/>).
    /// </summary>
    public bool IsPrefix { get; set; }

    /// <summary>
    /// Whether this is a wildcard query (contains unescaped <c>*</c> or <c>?</c> other than a single trailing <c>*</c>).
    /// </summary>
    public bool IsWildcard { get; set; }

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
    /// Whether a fuzzy (<c>~</c>) modifier was specified.
    /// </summary>
    public bool IsFuzzy => ProximityText is not null;

    /// <summary>
    /// The fuzzy distance: null when no <c>~</c> modifier was specified or it is not an integer, and
    /// <see cref="DefaultFuzzyDistance"/> when <c>~</c> was specified without a value.
    /// </summary>
    public int? FuzzyDistance
    {
        get => ProximityText is null ? null : ProximityText.Length == 0 ? DefaultFuzzyDistance : Modifiers.ParseInt(ProximityText);
        set => ProximityText = value == DefaultFuzzyDistance ? string.Empty : Modifiers.FormatInt(value);
    }

    /// <summary>
    /// Gets the fuzzy distance to use, resolving <see cref="DefaultFuzzyDistance"/> to <see cref="DefaultFuzzyDistanceValue"/>.
    /// </summary>
    public int? GetEffectiveFuzzyDistance()
    {
        int? distance = FuzzyDistance;
        return distance == DefaultFuzzyDistance ? DefaultFuzzyDistanceValue : distance;
    }

    /// <inheritdoc/>
    public override QueryNode Clone()
    {
        return CopyCommonTo(new TermNode
        {
            TermMemory = TermMemory,
            IsPrefix = IsPrefix,
            IsWildcard = IsWildcard,
            BoostText = BoostText,
            ProximityText = ProximityText
        });
    }
}
