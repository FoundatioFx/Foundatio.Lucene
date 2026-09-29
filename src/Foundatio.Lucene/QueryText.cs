using System.Buffers;

namespace Foundatio.Lucene;

/// <summary>
/// Escaping helpers for Lucene query text.
/// </summary>
public static class QueryText
{
    private static readonly SearchValues<char> SpecialCharacters = SearchValues.Create("+-!(){}[]^\"~*?:\\/&| \t\r\n");
    private static readonly SearchValues<char> PhraseSpecialCharacters = SearchValues.Create("\"\\");

    /// <summary>
    /// Removes backslash escapes from <paramref name="text"/>. A trailing lone backslash is preserved.
    /// </summary>
    public static string Unescape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Unescape(text.AsSpan(), text);
    }

    /// <summary>
    /// Removes backslash escapes from <paramref name="text"/>. When there is nothing to unescape,
    /// <paramref name="original"/> is returned (if provided) to avoid an allocation.
    /// </summary>
    internal static string Unescape(ReadOnlySpan<char> text, string? original = null)
    {
        if (text.IndexOf('\\') < 0)
            return original ?? text.ToString();

        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        return buffer[..UnescapeInto(text, buffer)].ToString();
    }

    private static int UnescapeInto(ReadOnlySpan<char> text, Span<char> buffer)
    {
        int written = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
                c = text[++i];

            buffer[written++] = c;
        }

        return written;
    }

    /// <summary>
    /// Unescapes into a new memory block, or returns <paramref name="text"/> unchanged when it has no escapes.
    /// </summary>
    internal static ReadOnlyMemory<char> UnescapeMemory(ReadOnlyMemory<char> text)
    {
        var span = text.Span;
        if (span.IndexOf('\\') < 0)
            return text;

        var buffer = new char[span.Length];
        return buffer.AsMemory(0, UnescapeInto(span, buffer));
    }

    /// <summary>
    /// Escapes all characters that have special meaning in an unquoted Lucene term, including whitespace.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return EscapeCore(text, SpecialCharacters);
    }

    /// <summary>
    /// Escapes the characters that have special meaning inside a quoted phrase (<c>"</c> and <c>\</c>).
    /// </summary>
    public static string EscapePhrase(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return EscapeCore(text, PhraseSpecialCharacters);
    }

    /// <summary>
    /// Escapes special characters but leaves <c>*</c> and <c>?</c> so they keep their wildcard meaning.
    /// </summary>
    internal static string EscapeExceptWildcards(string text)
    {
        if (text.AsSpan().IndexOfAny(SpecialCharacters) < 0)
            return text;

        var builder = new System.Text.StringBuilder(text.Length + 4);
        foreach (char c in text)
        {
            if (c is not '*' and not '?' && SpecialCharacters.Contains(c))
                builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string EscapeCore(string text, SearchValues<char> special)
    {
        if (text.AsSpan().IndexOfAny(special) < 0)
            return text;

        var builder = new System.Text.StringBuilder(text.Length + 4);
        foreach (char c in text)
        {
            if (special.Contains(c))
                builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }
}
