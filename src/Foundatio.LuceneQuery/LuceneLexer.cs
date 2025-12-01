using System.Runtime.CompilerServices;

namespace Foundatio.LuceneQuery;

/// <summary>
/// Lexical analyzer for Lucene query language with Elasticsearch extensions.
/// Uses zero-copy memory slices for token values.
/// </summary>
public class LuceneLexer
{
    private readonly ReadOnlyMemory<char> _sourceMemory;
    private int _position;
    private int _line;
    private int _column;
    private List<ParseError>? _errors;

    // Cached span for hot path access
    private ReadOnlySpan<char> Source => _sourceMemory.Span;

    public LuceneLexer(string source)
        : this((source ?? throw new ArgumentNullException(nameof(source))).AsMemory())
    {
    }

    public LuceneLexer(ReadOnlyMemory<char> source)
    {
        _sourceMemory = source;
        _position = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>
    /// Gets the list of errors encountered during tokenization.
    /// </summary>
    public List<ParseError> Errors => _errors ??= [];

    /// <summary>
    /// Tokenizes the entire source and returns all tokens.
    /// </summary>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>(Math.Max(8, _sourceMemory.Length / 4));
        Token token;

        while ((token = NextToken()).Type != TokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        tokens.Add(token);
        return tokens;
    }

    /// <summary>
    /// Gets the next token from the source.
    /// </summary>
    public Token NextToken()
    {
        if (_position < _sourceMemory.Length && char.IsWhiteSpace(Source[_position]))
        {
            return ConsumeWhitespace();
        }

        if (_position >= _sourceMemory.Length)
        {
            return new Token(TokenType.EndOfFile, ReadOnlyMemory<char>.Empty, _line, _column, _position, 0);
        }

        char current = Source[_position];

        if (current == '/')
            return ConsumeRegex();

        if (current == '"')
            return ConsumeQuotedString();

        if (current == '>' || current == '<')
            return ConsumeComparisonOperator();

        switch (current)
        {
            case ':':
                return ConsumeSingleChar(TokenType.Colon);
            case '(':
                return ConsumeSingleChar(TokenType.LeftParen);
            case ')':
                return ConsumeSingleChar(TokenType.RightParen);
            case '[':
                return ConsumeSingleChar(TokenType.LeftBracket);
            case ']':
                return ConsumeSingleChar(TokenType.RightBracket);
            case '{':
                return ConsumeSingleChar(TokenType.LeftBrace);
            case '}':
                return ConsumeSingleChar(TokenType.RightBrace);
            case '+':
                return ConsumeSingleChar(TokenType.Plus);
            case '-':
                return ConsumeSingleChar(TokenType.Minus);
            case '~':
                return ConsumeSingleChar(TokenType.Tilde);
            case '^':
                return ConsumeSingleChar(TokenType.Caret);
        }

        if (current == '&' && Peek() == '&')
            return ConsumeTwoChars(TokenType.And);
        if (current == '|' && Peek() == '|')
            return ConsumeTwoChars(TokenType.Or);
        if (current == '!')
            return ConsumeSingleChar(TokenType.Not);

        if (IsTermStartChar(current))
            return ConsumeTermOrKeyword();

        AddError(current);
        return ConsumeSingleChar(TokenType.Invalid);
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Keep error path out of hot path
    private void AddError(char c)
    {
        _errors ??= new List<ParseError>(4);
        _errors.Add(new ParseError(string.Concat("Unexpected character: '", c.ToString(), "'"), _position, 1, _line, _column));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlyMemory<char> Slice(int start, int length) => _sourceMemory.Slice(start, length);

    private Token ConsumeWhitespace()
    {
        int start = _position;
        int startColumn = _column;

        while (_position < _sourceMemory.Length && char.IsWhiteSpace(Source[_position]))
        {
            if (Source[_position] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }

        int length = _position - start;
        
        return new Token(TokenType.Whitespace, Slice(start, length), _line, startColumn, start, length);
    }

    // Buffer for processing escaped strings - reused to avoid allocations
    [ThreadStatic]
    private static char[]? _escapeBuffer;
    
    private Token ConsumeQuotedString()
    {
        int start = _position;
        int startColumn = _column;
        int startLine = _line;

        _position++;
        _column++;

        int contentStart = _position;
        bool hasEscapes = false;

        while (_position < _sourceMemory.Length)
        {
            char c = Source[_position];
            if (c == '\\')
            {
                hasEscapes = true;
                _position += 2;
                _column += 2;
            }
            else if (c == '"')
            {
                break;
            }
            else
            {
                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
        }

        int contentLength = _position - contentStart;
        ReadOnlyMemory<char> content;
        
        if (!hasEscapes)
        {
            // Zero-copy slice
            content = Slice(contentStart, contentLength);
        }
        else
        {
            // Must process escapes - allocates a new string
            content = ProcessEscapes(Source.Slice(contentStart, contentLength)).AsMemory();
        }

        if (_position < _sourceMemory.Length && Source[_position] == '"')
        {
            _position++;
            _column++;
        }

        int fullLength = _position - start;
        return new Token(TokenType.QuotedString, content, startLine, startColumn, start, fullLength);
    }

    private static string ProcessEscapes(ReadOnlySpan<char> input)
    {
        // Ensure buffer is large enough
        _escapeBuffer ??= new char[256];
        if (_escapeBuffer.Length < input.Length)
        {
            _escapeBuffer = new char[input.Length];
        }
        
        int writePos = 0;
        bool escaped = false;
        
        foreach (char c in input)
        {
            if (escaped)
            {
                _escapeBuffer[writePos++] = c;
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else
            {
                _escapeBuffer[writePos++] = c;
            }
        }
        
        return new string(_escapeBuffer, 0, writePos);
    }

    private Token ConsumeRegex()
    {
        int start = _position;
        int startColumn = _column;
        int startLine = _line;

        _position++;
        _column++;

        int contentStart = _position;

        while (_position < _sourceMemory.Length)
        {
            char c = Source[_position];
            if (c == '\\')
            {
                _position += 2;
                _column += 2;
            }
            else if (c == '/')
            {
                break;
            }
            else
            {
                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
        }

        int contentLength = _position - contentStart;
        var content = Slice(contentStart, contentLength);

        if (_position < _sourceMemory.Length && Source[_position] == '/')
        {
            _position++;
            _column++;
        }

        int fullLength = _position - start;
        return new Token(TokenType.Regex, content, startLine, startColumn, start, fullLength);
    }

    private Token ConsumeComparisonOperator()
    {
        int start = _position;
        int startColumn = _column;
        char current = Source[_position];

        if (_position + 1 < _sourceMemory.Length && Source[_position + 1] == '=')
        {
            _position += 2;
            _column += 2;
            return new Token(
                current == '>' ? TokenType.GreaterThanOrEqual : TokenType.LessThanOrEqual,
                Slice(start, 2),
                _line, startColumn, start, 2);
        }

        _position++;
        _column++;
        return new Token(
            current == '>' ? TokenType.GreaterThan : TokenType.LessThan,
            Slice(start, 1),
            _line, startColumn, start, 1);
    }

    private Token ConsumeTermOrKeyword()
    {
        int start = _position;
        int startColumn = _column;

        bool hasWildcard = false;
        bool escaped = false;

        while (_position < _sourceMemory.Length)
        {
            char c = Source[_position];

            if (escaped)
            {
                escaped = false;
                _position++;
                _column++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                _position++;
                _column++;
                continue;
            }

            if (c == '*' || c == '?')
            {
                hasWildcard = true;
                _position++;
                _column++;
                continue;
            }

            // Special case: allow colon in time values (e.g., 12:30:00)
            if (c == ':' && IsTimeColon(_position))
            {
                _position++;
                _column++;
                continue;
            }

            if (!IsTermChar(c))
                break;

            _position++;
            _column++;
        }

        int length = _position - start;
        var valueSpan = Source.Slice(start, length);
        var valueMemory = Slice(start, length);

        TokenType type;
        
        if (valueSpan.SequenceEqual("AND"))
            type = TokenType.And;
        else if (valueSpan.SequenceEqual("OR"))
            type = TokenType.Or;
        else if (valueSpan.SequenceEqual("NOT"))
            type = TokenType.Not;
        else if (valueSpan.SequenceEqual("TO"))
            type = TokenType.To;
        else if (hasWildcard && valueSpan.EndsWith("*") && !valueSpan.Contains('?') && CountChar(valueSpan, '*') == 1)
            type = TokenType.Prefix;
        else if (hasWildcard)
            type = TokenType.Wildcard;
        else
            type = TokenType.Term;

        return new Token(type, valueMemory, _line, startColumn, start, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountChar(ReadOnlySpan<char> span, char c)
    {
        int count = 0;
        foreach (char ch in span)
            if (ch == c) count++;
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ConsumeSingleChar(TokenType type)
    {
        int start = _position;
        int startColumn = _column;
        _position++;
        _column++;
        return new Token(type, Slice(start, 1), _line, startColumn, start, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ConsumeTwoChars(TokenType type)
    {
        int start = _position;
        int startColumn = _column;
        _position += 2;
        _column += 2;
        return new Token(type, Slice(start, 2), _line, startColumn, start, 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset = 1)
    {
        int pos = _position + offset;
        return pos < _sourceMemory.Length ? Source[pos] : '\0';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTermStartChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '*' || c == '?' || c == '\\' || c == '@';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTermChar(char c)
    {
        if (char.IsWhiteSpace(c)) return false;
        return c switch
        {
            ':' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '^' or '~' or '>' or '<' or '=' => false,
            _ => true
        };
    }

    /// <summary>
    /// Checks if a colon at the current position is part of a time value (e.g., 12:30:00)
    /// rather than a field separator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTimeColon(int pos)
    {
        // A colon is part of a time if:
        // 1. Previous char is a digit
        // 2. Next char is a digit
        if (pos <= 0 || pos + 1 >= _sourceMemory.Length)
            return false;
        
        return char.IsDigit(Source[pos - 1]) && char.IsDigit(Source[pos + 1]);
    }
}
