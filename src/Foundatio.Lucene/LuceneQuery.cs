using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Main entry point for parsing Lucene queries with Elasticsearch extensions.
/// </summary>
public static class LuceneQuery
{
    /// <summary>
    /// Parses a Lucene query. Parsing never throws for malformed input: errors are reported on the result and the
    /// document contains everything that could be parsed.
    /// </summary>
    /// <param name="query">The query text.</param>
    /// <param name="options">Parser options, or null for <see cref="LuceneParserOptions.Default"/>.</param>
    public static LuceneParseResult Parse(string query, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Parse(query.AsMemory(), options);
    }

    /// <summary>
    /// Parses a Lucene query using the specified default operator.
    /// </summary>
    public static LuceneParseResult Parse(string query, BooleanOperator defaultOperator)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Parse(query.AsMemory(), defaultOperator == LuceneParserOptions.Default.DefaultOperator
            ? LuceneParserOptions.Default
            : new LuceneParserOptions { DefaultOperator = defaultOperator });
    }

    /// <summary>
    /// Parses a Lucene query held in memory. Node values are zero-copy slices of <paramref name="query"/>,
    /// so the memory must not be modified while the document is in use.
    /// </summary>
    public static LuceneParseResult Parse(ReadOnlyMemory<char> query, LuceneParserOptions? options = null)
    {
        options ??= LuceneParserOptions.Default;

        var lexer = new LuceneLexer(query);
        var tokens = lexer.Tokenize();
        var parser = new LuceneParser(tokens, options);
        var document = parser.Parse();

        var lexerErrors = lexer.HasErrors ? lexer.Errors : null;
        var parserErrors = parser.Errors;
        if (lexerErrors is null && parserErrors is null)
            return new LuceneParseResult(document);

        var errors = new List<ParseError>((lexerErrors?.Count ?? 0) + (parserErrors?.Count ?? 0));
        if (lexerErrors is not null)
            errors.AddRange(lexerErrors);
        if (parserErrors is not null)
            errors.AddRange(parserErrors);

        errors.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        return new LuceneParseResult(document, errors);
    }

    /// <summary>
    /// Parses a Lucene query and reports whether it parsed without errors.
    /// </summary>
    public static bool TryParse(string query, out LuceneParseResult result, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        result = Parse(query, options);
        return result.IsSuccess;
    }

    /// <summary>
    /// Tokenizes a Lucene query. Useful for syntax highlighting. The last token is always <see cref="TokenType.EndOfFile"/>.
    /// </summary>
    public static IReadOnlyList<Token> Tokenize(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new LuceneLexer(query.AsMemory()).Tokenize();
    }
}
