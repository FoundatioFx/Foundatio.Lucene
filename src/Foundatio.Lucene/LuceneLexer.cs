using System.Runtime.CompilerServices;

namespace Foundatio.Lucene;

/// <summary>
/// Lexical analyzer for the Lucene query language with Elasticsearch extensions.
/// Token values are zero-copy slices of the source text; escape sequences are left in place
/// so they are processed exactly once, by the consumer that needs the unescaped value.
/// </summary>
internal sealed class LuceneLexer
{
    private readonly ReadOnlyMemory<char> _source;
    private int _position;
    private int _line = 1;
    private int _lineStart;
    private List<ParseError>? _errors;

    private bool _inRange;
    private bool _inValue;
    private bool _expectModifierValue;
    private bool _expectComparisonValue;

    public LuceneLexer(ReadOnlyMemory<char> source)
    {
        _source = source;
    }

    private ReadOnlySpan<char> Source => _source.Span;

    public List<ParseError> Errors => _errors ??= [];

    public bool HasErrors => _errors is { Count: > 0 };

    /// <summary>
    /// Tokenizes the entire source. The last token is always <see cref="TokenType.EndOfFile"/>.
    /// </summary>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>(Math.Max(8, _source.Length / 3));
        Token token;
        do
        {
            token = NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        return tokens;
    }

    public Token NextToken()
    {
        bool leadingWhitespace = SkipWhitespace();
        var flags = leadingWhitespace ? TokenFlags.LeadingWhitespace : TokenFlags.None;

        if (_position >= Source.Length)
            return new Token(TokenType.EndOfFile, ReadOnlyMemory<char>.Empty, _line, Column, _position, 0, flags);

        if (_expectModifierValue)
        {
            _expectModifierValue = false;
            if (!leadingWhitespace && IsModifierValueStart(Source[_position]))
                return ConsumeModifierValue(flags);
        }

        if (_expectComparisonValue)
        {
            _expectComparisonValue = false;
            _inValue = true;
            if (IsComparisonTermChar(Source[_position], _position))
                return ConsumeTerm(flags, TermMode.Comparison);
        }

        return _inRange ? NextRangeToken(flags) : NextQueryToken(flags);
    }

    private Token NextQueryToken(TokenFlags flags)
    {
        char current = Source[_position];
        switch (current)
        {
            case '"':
                return ConsumeQuotedString(flags);
            case '/':
                return ConsumeRegex(flags);
            case ':':
                _inValue = true;
                return ConsumeSingleChar(TokenType.Colon, flags);
            case '(':
                _inValue = false;
                return ConsumeSingleChar(TokenType.LeftParen, flags);
            case ')':
                _inValue = false;
                return ConsumeSingleChar(TokenType.RightParen, flags);
            case '[':
                _inRange = true;
                return ConsumeSingleChar(TokenType.LeftBracket, flags);
            case '{':
                _inRange = true;
                return ConsumeSingleChar(TokenType.LeftBrace, flags);
            case ']':
                return ConsumeSingleChar(TokenType.RightBracket, flags);
            case '}':
                return ConsumeSingleChar(TokenType.RightBrace, flags);
            case '^':
                _expectModifierValue = true;
                return ConsumeSingleChar(TokenType.Caret, flags);
            case '~':
                _expectModifierValue = true;
                return ConsumeSingleChar(TokenType.Tilde, flags);
            case '>':
            case '<':
                return ConsumeComparisonOperator(flags);
        }

        if (current is '+' or '-' or '!' && IsOperatorPrefix(_position + 1))
        {
            _inValue = false;
            return ConsumeSingleChar(current switch
            {
                '+' => TokenType.Plus,
                '-' => TokenType.Minus,
                _ => TokenType.Not
            }, flags);
        }

        if (current is '&' or '|' && Peek(1) == current && IsBoundary(_position + 2))
        {
            _inValue = false;
            return ConsumeChars(current == '&' ? TokenType.And : TokenType.Or, 2, flags);
        }

        return ConsumeTerm(flags, _inValue ? TermMode.Value : TermMode.Query);
    }

    private Token NextRangeToken(TokenFlags flags)
    {
        char current = Source[_position];
        switch (current)
        {
            case '"':
                return ConsumeQuotedString(flags);
            case ']':
                _inRange = false;
                _inValue = false;
                return ConsumeSingleChar(TokenType.RightBracket, flags);
            case '}':
                _inRange = false;
                _inValue = false;
                return ConsumeSingleChar(TokenType.RightBrace, flags);
            case '[':
                return ConsumeSingleChar(TokenType.LeftBracket, flags);
            case '{':
                return ConsumeSingleChar(TokenType.LeftBrace, flags);
            case '.' when Peek(1) == '.':
                return ConsumeChars(TokenType.RangeDots, 2, flags);
            case '(':
                _inRange = false;
                return ConsumeSingleChar(TokenType.LeftParen, flags);
            case ')':
                // An unclosed range must not swallow the rest of the query.
                _inRange = false;
                _inValue = false;
                return ConsumeSingleChar(TokenType.RightParen, flags);
        }

        var token = ConsumeTerm(flags, TermMode.Range);
        if (token.Span.SequenceEqual("TO") && !token.HasEscapes)
            return token with { Type = TokenType.To };

        return token;
    }

    private enum TermMode
    {
        Query,
        Value,
        Range,
        Comparison
    }

    private Token ConsumeTerm(TokenFlags flags, TermMode mode)
    {
        int start = _position;
        int line = _line;
        int column = Column;
        var source = Source;

        while (_position < source.Length)
        {
            char c = source[_position];

            if (c == '\\')
            {
                flags |= TokenFlags.HasEscapes;
                if (_position + 1 >= source.Length)
                {
                    AddError("Dangling escape character '\\' at end of input", _position, 1, QueryErrorCode.UnexpectedToken);
                    _position++;
                    break;
                }

                if (source[_position + 1] == '\n')
                    NewLine(_position + 1);

                _position += 2;
                continue;
            }

            if (c is '*' or '?')
            {
                flags |= TokenFlags.HasWildcard;
                _position++;
                continue;
            }

            bool isTermChar = mode switch
            {
                TermMode.Range => IsRangeTermChar(c, _position),
                TermMode.Comparison => IsComparisonTermChar(c, _position),
                TermMode.Value => IsTermChar(c) || c == ':' && IsTimeColon(_position),
                _ => IsTermChar(c)
            };

            if (!isTermChar)
                break;

            _position++;
        }

        if (_position == start)
        {
            // Not a term character; consume it so the parser can report it without looping.
            _position++;
            AddError($"Unexpected character '{source[start]}'", start, 1, QueryErrorCode.UnexpectedToken);
            return new Token(TokenType.Invalid, _source.Slice(start, 1), line, column, start, 1, flags);
        }

        int length = _position - start;
        var value = _source.Slice(start, length);

        if (mode is TermMode.Query or TermMode.Value && (flags & TokenFlags.HasEscapes) == 0)
        {
            var span = value.Span;
            if (span.SequenceEqual("AND"))
                return MakeOperator(TokenType.And);
            if (span.SequenceEqual("OR"))
                return MakeOperator(TokenType.Or);
            if (span.SequenceEqual("NOT"))
                return MakeOperator(TokenType.Not);
        }

        if (mode != TermMode.Range)
            _inValue = false;

        return new Token(TokenType.Term, value, line, column, start, length, flags);

        Token MakeOperator(TokenType type)
        {
            _inValue = false;
            return new Token(type, value, line, column, start, length, flags);
        }
    }

    private Token ConsumeQuotedString(TokenFlags flags)
    {
        int start = _position;
        int line = _line;
        int column = Column;
        var source = Source;

        _position++;
        int contentStart = _position;

        while (_position < source.Length)
        {
            char c = source[_position];
            if (c == '\\' && _position + 1 < source.Length)
            {
                flags |= TokenFlags.HasEscapes;
                if (source[_position + 1] == '\n')
                    NewLine(_position + 1);
                _position += 2;
                continue;
            }

            if (c == '"')
                break;

            if (c == '\n')
                NewLine(_position);

            _position++;
        }

        int contentEnd = Math.Min(_position, source.Length);
        if (_position < source.Length)
        {
            _position++;
        }
        else
        {
            flags |= TokenFlags.Unterminated;
            AddError("Unterminated quoted string", start, source.Length - start, QueryErrorCode.UnmatchedBracket, line, column);
        }

        if (!_inRange)
            _inValue = false;

        return new Token(TokenType.QuotedString, _source.Slice(contentStart, contentEnd - contentStart), line, column, start, _position - start, flags);
    }

    private Token ConsumeRegex(TokenFlags flags)
    {
        int start = _position;
        int line = _line;
        int column = Column;
        var source = Source;

        _position++;
        int contentStart = _position;

        while (_position < source.Length)
        {
            char c = source[_position];
            if (c == '\\' && _position + 1 < source.Length)
            {
                _position += 2;
                continue;
            }

            if (c == '/')
                break;

            if (c == '\n')
                NewLine(_position);

            _position++;
        }

        int contentEnd = Math.Min(_position, source.Length);
        if (_position < source.Length)
        {
            _position++;
        }
        else
        {
            flags |= TokenFlags.Unterminated;
            AddError("Unterminated regular expression", start, source.Length - start, QueryErrorCode.UnmatchedBracket, line, column);
        }

        _inValue = false;
        return new Token(TokenType.Regex, _source.Slice(contentStart, contentEnd - contentStart), line, column, start, _position - start, flags);
    }

    private Token ConsumeModifierValue(TokenFlags flags)
    {
        if (Source[_position] == '"')
        {
            var quoted = ConsumeQuotedString(flags);
            return quoted with { Type = TokenType.ModifierValue };
        }

        int start = _position;
        int column = Column;
        var source = Source;
        while (_position < source.Length && IsModifierValueChar(source[_position]))
            _position++;

        return new Token(TokenType.ModifierValue, _source.Slice(start, _position - start), _line, column, start, _position - start, flags);
    }

    private Token ConsumeComparisonOperator(TokenFlags flags)
    {
        char current = Source[_position];
        bool orEqual = Peek(1) == '=';
        var type = current == '>'
            ? orEqual ? TokenType.GreaterThanOrEqual : TokenType.GreaterThan
            : orEqual ? TokenType.LessThanOrEqual : TokenType.LessThan;

        _expectComparisonValue = true;
        return ConsumeChars(type, orEqual ? 2 : 1, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ConsumeSingleChar(TokenType type, TokenFlags flags) => ConsumeChars(type, 1, flags);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ConsumeChars(TokenType type, int count, TokenFlags flags)
    {
        int start = _position;
        int column = Column;
        _position += count;
        return new Token(type, _source.Slice(start, count), _line, column, start, count, flags);
    }

    private bool SkipWhitespace()
    {
        int start = _position;
        var source = Source;
        while (_position < source.Length && char.IsWhiteSpace(source[_position]))
        {
            if (source[_position] == '\n')
                NewLine(_position);
            _position++;
        }

        return _position > start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NewLine(int newLinePosition)
    {
        _line++;
        _lineStart = newLinePosition + 1;
    }

    private int Column => _position - _lineStart + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset)
    {
        int pos = _position + offset;
        return pos < Source.Length ? Source[pos] : '\0';
    }

    /// <summary>
    /// A +, -, or ! is an operator only when it is immediately followed by the clause it modifies.
    /// Followed by whitespace (or nothing) it is literal term text, matching Foundatio.Parsers.
    /// </summary>
    private bool IsOperatorPrefix(int nextPosition)
    {
        return nextPosition < Source.Length && !char.IsWhiteSpace(Source[nextPosition]);
    }

    private bool IsBoundary(int position)
    {
        return position >= Source.Length || char.IsWhiteSpace(Source[position]) || Source[position] is '(' or ')';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTermChar(char c)
    {
        return c switch
        {
            ':' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '^' or '~' => false,
            _ => !char.IsWhiteSpace(c)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsRangeTermChar(char c, int position)
    {
        return c switch
        {
            '[' or ']' or '{' or '}' or '(' or ')' or '"' or '^' or '~' => false,
            '.' => position + 1 >= Source.Length || Source[position + 1] != '.',
            _ => !char.IsWhiteSpace(c)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsComparisonTermChar(char c, int position)
    {
        return c == ':' ? IsTimeColon(position) : IsRangeTermChar(c, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsModifierValueStart(char c)
    {
        return c == '"' || IsModifierValueChar(c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsModifierValueChar(char c)
    {
        return c switch
        {
            ':' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '^' or '~' => false,
            _ => !char.IsWhiteSpace(c)
        };
    }

    /// <summary>
    /// A colon between two digits inside a value (e.g., 2024-01-01T12:30:00) belongs to the value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTimeColon(int position)
    {
        return position > 0 && position + 1 < Source.Length
            && char.IsAsciiDigit(Source[position - 1]) && char.IsAsciiDigit(Source[position + 1]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddError(string message, int position, int length, QueryErrorCode code, int? line = null, int? column = null)
    {
        Errors.Add(new ParseError(message, position, length, line ?? _line, column ?? position - _lineStart + 1, code));
    }
}
