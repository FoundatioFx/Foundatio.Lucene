namespace Foundatio.Lucene;

/// <summary>
/// Defines all token types in the Lucene query language including Elasticsearch extensions.
/// </summary>
public enum TokenType
{
    // Literals
    /// <summary>A term (word or phrase without quotes)</summary>
    Term,
    /// <summary>A quoted phrase (e.g., "quick brown fox")</summary>
    QuotedString,
    /// <summary>A wildcard term (contains * or ?)</summary>
    Wildcard,
    /// <summary>A prefix term (ends with *)</summary>
    Prefix,
    /// <summary>A regular expression term (enclosed in /)</summary>
    Regex,

    // Boolean operators
    /// <summary>AND operator</summary>
    And,
    /// <summary>OR operator</summary>
    Or,
    /// <summary>NOT operator</summary>
    Not,

    // Modifiers
    /// <summary>Plus sign (+) - required term</summary>
    Plus,
    /// <summary>Minus sign (-) - excluded term</summary>
    Minus,
    /// <summary>Tilde (~) - fuzzy or proximity</summary>
    Tilde,
    /// <summary>Caret (^) - boost</summary>
    Caret,

    // Structural tokens
    /// <summary>Colon (:) - field separator</summary>
    Colon,
    /// <summary>Left parenthesis (</summary>
    LeftParen,
    /// <summary>Right parenthesis )</summary>
    RightParen,
    /// <summary>Left bracket [</summary>
    LeftBracket,
    /// <summary>Right bracket ]</summary>
    RightBracket,
    /// <summary>Left brace {</summary>
    LeftBrace,
    /// <summary>Right brace }</summary>
    RightBrace,

    // Range tokens
    /// <summary>TO keyword in ranges</summary>
    To,
    /// <summary>Greater than (&gt;)</summary>
    GreaterThan,
    /// <summary>Greater than or equal (&gt;=)</summary>
    GreaterThanOrEqual,
    /// <summary>Less than (&lt;)</summary>
    LessThan,
    /// <summary>Less than or equal (&lt;=)</summary>
    LessThanOrEqual,

    // Special tokens
    /// <summary>Whitespace</summary>
    Whitespace,
    /// <summary>End of input</summary>
    EndOfFile,
    /// <summary>Invalid/unrecognized character</summary>
    Invalid
}
