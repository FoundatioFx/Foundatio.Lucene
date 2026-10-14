namespace Foundatio.Lucene.Extensions;

/// <summary>
/// String helpers for Lucene query text.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Removes backslash escapes. A trailing lone backslash is preserved.
    /// </summary>
    public static string? Unescape(this string? input) => input is null ? null : QueryText.Unescape(input);

    /// <summary>
    /// Escapes characters that have special meaning in an unquoted Lucene term.
    /// </summary>
    public static string? Escape(this string? input) => input is null ? null : QueryText.Escape(input);
}
