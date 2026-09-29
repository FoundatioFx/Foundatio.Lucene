namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// How a term without a field is matched against a string default field.
/// </summary>
public enum SearchOperator
{
    /// <summary>The field equals the term.</summary>
    Equals,
    /// <summary>The field contains the term.</summary>
    Contains,
    /// <summary>The field starts with the term.</summary>
    StartsWith
}

/// <summary>
/// A term being searched against one default field, passed to a <see cref="SearchTokenizer"/>.
/// </summary>
public sealed class SearchTerm
{
    /// <summary>
    /// The default field being searched.
    /// </summary>
    public required EntityFieldInfo Field { get; init; }

    /// <summary>
    /// The term as the user wrote it, with escape sequences processed.
    /// </summary>
    public required string Term { get; init; }

    /// <summary>
    /// The alternatives to search for. The field matches when it matches any token. Null searches for
    /// <see cref="Term"/>; blank tokens never match.
    /// </summary>
    public IList<string>? Tokens { get; set; }

    /// <summary>
    /// How the tokens are matched against the field.
    /// </summary>
    public SearchOperator Operator { get; set; }
}

/// <summary>
/// Adjusts how a term is searched against a default field, for example by normalizing a phone number.
/// </summary>
public delegate void SearchTokenizer(SearchTerm term);
