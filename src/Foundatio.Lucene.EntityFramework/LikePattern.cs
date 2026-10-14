using System.Text;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// How a wildcard term is translated.
/// </summary>
internal enum WildcardKind
{
    /// <summary>A literal followed only by <c>*</c>: <c>StartsWith(literal)</c>.</summary>
    Prefix,
    /// <summary><c>*literal*</c>: <c>Contains(literal)</c>.</summary>
    Contains,
    /// <summary>Anything else: <c>LIKE pattern</c>.</summary>
    Like
}

/// <summary>
/// A wildcard term analyzed from its raw text. For <see cref="WildcardKind.Prefix"/> and
/// <see cref="WildcardKind.Contains"/> <see cref="Value"/> is the literal text; for <see cref="WildcardKind.Like"/> it
/// is an escaped <c>LIKE</c> pattern.
/// </summary>
internal readonly record struct WildcardPattern(WildcardKind Kind, string Value);

/// <summary>
/// Translates Lucene wildcard terms to SQL <c>LIKE</c> patterns and SQL Server full-text search conditions.
/// </summary>
internal static class LikePattern
{
    public const char EscapeCharacter = '\\';

    /// <summary>
    /// Analyzes a raw term (escape sequences preserved), where <c>\*</c> and <c>\?</c> are literal characters and
    /// unescaped <c>*</c> and <c>?</c> are wildcards.
    /// </summary>
    public static WildcardPattern Analyze(string raw)
    {
        var literal = new StringBuilder(raw.Length);
        var like = new StringBuilder(raw.Length + 4);
        int leadingStars = 0;
        int trailingStars = 0;
        int innerWildcards = 0;
        int literals = 0;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            bool escaped = false;
            if (c == '\\' && i + 1 < raw.Length)
            {
                c = raw[++i];
                escaped = true;
            }

            if (!escaped && c is '*' or '?')
            {
                like.Append(c == '*' ? '%' : '_');
                if (c == '*' && literals == 0)
                {
                    leadingStars++;
                }
                else if (c == '*')
                {
                    trailingStars++;
                }
                else
                {
                    innerWildcards += trailingStars + 1;
                    trailingStars = 0;
                }

                continue;
            }

            innerWildcards += trailingStars;
            trailingStars = 0;
            literals++;
            literal.Append(c);
            if (c is '%' or '_' or '[' or EscapeCharacter)
                like.Append(EscapeCharacter);
            like.Append(c);
        }

        if (literals > 0 && innerWildcards == 0 && leadingStars == 0 && trailingStars > 0)
            return new WildcardPattern(WildcardKind.Prefix, literal.ToString());

        if (literals > 0 && innerWildcards == 0 && leadingStars == 1 && trailingStars == 1)
            return new WildcardPattern(WildcardKind.Contains, literal.ToString());

        return new WildcardPattern(WildcardKind.Like, like.ToString());
    }

    /// <summary>
    /// Creates a SQL Server full-text search condition that matches <paramref name="text"/> as a word or phrase
    /// (or, with <paramref name="prefix"/>, words starting with it), or returns null when the text has nothing to
    /// search for. The condition is a single quoted phrase. A phrase has no escape for <c>"</c>, so double quotes and
    /// <c>*</c> in the text become word separators (the word breaker ignores them anyway); the text therefore cannot
    /// close the phrase or add operators such as <c>OR</c> or <c>NEAR</c>.
    /// </summary>
    public static string? ToFullTextCondition(string text, bool prefix)
    {
        var builder = new StringBuilder(text.Length + 4);
        builder.Append('"');
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (c is '"' or '*' || char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 1;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');

            pendingSpace = false;
            builder.Append(c);
        }

        if (builder.Length == 1)
            return null;

        if (prefix)
            builder.Append('*');

        return builder.Append('"').ToString();
    }
}
