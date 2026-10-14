namespace Foundatio.Lucene;

/// <summary>
/// Defines all token types in the Lucene query language including Elasticsearch extensions.
/// </summary>
public enum TokenType
{
    /// <summary>A term. The token value is the raw (still escaped) text.</summary>
    Term,
    /// <summary>A quoted phrase. The token value is the raw text between the quotes.</summary>
    QuotedString,
    /// <summary>A regular expression. The token value is the raw text between the slashes.</summary>
    Regex,

    /// <summary>AND or &amp;&amp; operator.</summary>
    And,
    /// <summary>OR or || operator.</summary>
    Or,
    /// <summary>NOT keyword or ! prefix.</summary>
    Not,

    /// <summary>Plus sign (+) prefix marking a required clause.</summary>
    Plus,
    /// <summary>Minus sign (-) prefix marking a prohibited clause.</summary>
    Minus,
    /// <summary>Tilde (~) introducing a fuzzy, proximity, or interval modifier.</summary>
    Tilde,
    /// <summary>Caret (^) introducing a boost (or time zone) modifier.</summary>
    Caret,
    /// <summary>The value of a <see cref="Tilde"/> or <see cref="Caret"/> modifier.</summary>
    ModifierValue,

    /// <summary>Colon (:) field separator.</summary>
    Colon,
    /// <summary>Left parenthesis.</summary>
    LeftParen,
    /// <summary>Right parenthesis.</summary>
    RightParen,
    /// <summary>Left bracket, opening an inclusive range.</summary>
    LeftBracket,
    /// <summary>Right bracket, closing an inclusive range.</summary>
    RightBracket,
    /// <summary>Left brace, opening an exclusive range.</summary>
    LeftBrace,
    /// <summary>Right brace, closing an exclusive range.</summary>
    RightBrace,

    /// <summary>TO keyword inside a range.</summary>
    To,
    /// <summary>The <c>..</c> delimiter inside a range.</summary>
    RangeDots,
    /// <summary>Greater than (&gt;).</summary>
    GreaterThan,
    /// <summary>Greater than or equal (&gt;=).</summary>
    GreaterThanOrEqual,
    /// <summary>Less than (&lt;).</summary>
    LessThan,
    /// <summary>Less than or equal (&lt;=).</summary>
    LessThanOrEqual,

    /// <summary>End of input.</summary>
    EndOfFile,
    /// <summary>An unrecognized character.</summary>
    Invalid
}

/// <summary>
/// Additional information about a token's raw text.
/// </summary>
[Flags]
public enum TokenFlags
{
    /// <summary>No flags.</summary>
    None = 0,
    /// <summary>The token contains backslash escape sequences.</summary>
    HasEscapes = 1,
    /// <summary>The token contains an unescaped <c>*</c> or <c>?</c> wildcard.</summary>
    HasWildcard = 2,
    /// <summary>The token is a quoted string or regex that is missing its closing delimiter.</summary>
    Unterminated = 4,
    /// <summary>The token was preceded by whitespace.</summary>
    LeadingWhitespace = 8
}
