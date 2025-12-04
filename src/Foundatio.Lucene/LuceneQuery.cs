using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Main entry point for parsing Lucene queries with Elasticsearch extensions.
/// </summary>
public static class LuceneQuery
{
    /// <summary>
    /// Parses a Lucene query string and returns a parse result containing the AST and any errors.
    /// The parser is resilient and will continue parsing after errors, returning a partial AST.
    /// </summary>
    /// <param name="query">The Lucene query string to parse.</param>
    /// <param name="defaultOperator">The default operator to use (OR or AND). Defaults to OR.</param>
    /// <param name="splitOnWhitespace">Whether to split terms on whitespace in groups. 
    /// When false, consecutive terms in groups are combined into MultiTermNode. Defaults to true.</param>
    /// <returns>A LuceneParseResult containing the parsed document (possibly partial) and any errors.</returns>
    public static LuceneParseResult Parse(string query, BooleanOperator defaultOperator = BooleanOperator.Or, bool splitOnWhitespace = true)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new LuceneParseResult();
        }

        try
        {
            var lexer = new LuceneLexer(query);
            var tokens = lexer.Tokenize();

            var parser = new LuceneParser(tokens)
            {
                DefaultOperator = defaultOperator,
                SplitOnWhitespace = splitOnWhitespace
            };
            var document = parser.Parse();

            // Only combine errors if there are any
            var lexerErrors = lexer.Errors;
            var parserErrors = parser.Errors;
            
            if (lexerErrors.Count == 0 && parserErrors.Count == 0)
            {
                return LuceneParseResult.Success(document);
            }
            
            // Combine lexer and parser errors
            var allErrors = new List<ParseError>(lexerErrors.Count + parserErrors.Count);
            allErrors.AddRange(lexerErrors);
            allErrors.AddRange(parserErrors);

            return LuceneParseResult.Partial(document, allErrors);
        }
        catch (Exception ex)
        {
            return LuceneParseResult.Failure(new ParseError($"Unexpected error: {ex.Message}", 0, 0, 1, 1));
        }
    }

    /// <summary>
    /// Tokenizes a Lucene query string and returns all tokens.
    /// </summary>
    /// <param name="query">The Lucene query string to tokenize.</param>
    /// <returns>A list of tokens.</returns>
    public static List<Token> Tokenize(string query)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var lexer = new LuceneLexer(query);
        return lexer.Tokenize();
    }

    /// <summary>
    /// Validates a Lucene query string and returns the parse result with errors.
    /// Returns true if parsing completed (even with errors), false only on catastrophic failures.
    /// </summary>
    /// <param name="query">The Lucene query string to validate.</param>
    /// <param name="result">Output parameter for the parse result containing document and errors.</param>
    /// <param name="defaultOperator">The default operator to use (OR or AND). Defaults to OR.</param>
    /// <param name="splitOnWhitespace">Whether to split terms on whitespace in groups. Defaults to true.</param>
    /// <returns>True if parsing completed, false only on catastrophic exceptions.</returns>
    public static bool TryParse(string query, out LuceneParseResult result, BooleanOperator defaultOperator = BooleanOperator.Or, bool splitOnWhitespace = true)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            result = new LuceneParseResult();
            return true;
        }

        try
        {
            result = Parse(query, defaultOperator, splitOnWhitespace);
            return true;
        }
        catch (Exception ex)
        {
            result = LuceneParseResult.Failure(new ParseError($"Catastrophic error: {ex.Message}", 0, 0, 1, 1));
            return false;
        }
    }
}
