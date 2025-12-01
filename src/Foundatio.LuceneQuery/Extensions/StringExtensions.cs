using System.Text;

namespace Foundatio.LuceneQuery.Extensions;

/// <summary>
/// String extension methods for working with escaped Lucene query strings.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Unescapes a Lucene query string by removing backslash escape characters.
    /// </summary>
    /// <param name="input">The escaped string to unescape.</param>
    /// <returns>The unescaped string.</returns>
    public static string? Unescape(this string? input)
    {
        if (input == null)
            return null;

        var sb = new StringBuilder();
        bool escaped = false;
        foreach (char ch in input)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else
            {
                sb.Append(ch);
            }
        }

        // If we ended with a backslash, preserve it
        if (escaped)
            sb.Append('\\');

        return sb.ToString();
    }

    /// <summary>
    /// Escapes special characters in a string for use in a Lucene query.
    /// Special characters: + - &amp;&amp; || ! ( ) { } [ ] ^ " ~ * ? : \ /
    /// </summary>
    /// <param name="input">The string to escape.</param>
    /// <returns>The escaped string.</returns>
    public static string? Escape(this string? input)
    {
        if (input == null)
            return null;

        var sb = new StringBuilder();
        foreach (char ch in input)
        {
            // Escape special characters
            if (ch is '+' or '-' or '!' or '(' or ')' or '{' or '}' or '[' or ']' 
                or '^' or '"' or '~' or '*' or '?' or ':' or '\\' or '/')
            {
                sb.Append('\\');
            }
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
