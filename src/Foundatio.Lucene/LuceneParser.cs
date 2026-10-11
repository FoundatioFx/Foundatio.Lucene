using System.Runtime.CompilerServices;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Recursive-descent parser for the Lucene query language with Elasticsearch extensions.
/// </summary>
/// <remarks>
/// Operator precedence is NOT, then AND, then OR; juxtaposed clauses use <see cref="LuceneParserOptions.DefaultOperator"/>.
/// <c>+x</c> makes a clause required and <c>-x</c> prohibits it within its boolean level (Lucene semantics).
/// <c>NOT x</c> prohibits like <c>-x</c>, except when it is an alternative of an explicit OR (<c>a OR NOT b</c>),
/// where it is a boolean negation.
/// </remarks>
internal sealed class LuceneParser
{
    private readonly List<Token> _tokens;
    private readonly LuceneParserOptions _options;
    private int _position;
    private int _depth;
    private int _lastEnd;
    private List<ParseError>? _errors;

    public LuceneParser(List<Token> tokens, LuceneParserOptions options)
    {
        _tokens = tokens;
        _options = options;
    }

    public List<ParseError>? Errors => _errors;

    public QueryDocument Parse()
    {
        var document = new QueryDocument { StartLine = 1, StartColumn = 1 };

        var query = ParseOr();
        while (!IsAtEnd)
        {
            ReportUnexpected(Current);
            Advance();
            query = Combine(query, ParseOr());
        }

        document.Query = query;
        if (query is not null)
        {
            document.StartPosition = query.StartPosition;
            document.EndPosition = query.EndPosition;
            document.StartLine = query.StartLine;
            document.StartColumn = query.StartColumn;
        }

        return document;
    }

    private readonly record struct Clause(QueryNode Node, ClauseModifier Modifier, BooleanOperator Operator);

    private QueryNode? ParseOr()
    {
        var first = ParseAnd();
        if (first is null)
            return null;

        List<Clause>? clauses = null;
        while (!IsAtEnd)
        {
            BooleanOperator op;
            if (Current.Type == TokenType.Or)
            {
                var orToken = Current;
                Advance();
                op = BooleanOperator.Or;
                var next = ParseAnd();
                if (next is null)
                {
                    AddError($"Expected a query after '{orToken.GetString()}'", orToken, QueryErrorCode.UnexpectedToken);
                    break;
                }

                (clauses ??= [first.Value]).Add(next.Value with { Operator = op });
            }
            else if (_options.DefaultOperator == BooleanOperator.Or && CanStartClause(Current.Type))
            {
                var next = ParseAnd();
                if (next is null)
                    break;

                (clauses ??= [first.Value]).Add(next.Value with { Operator = BooleanOperator.Implicit });
            }
            else
            {
                break;
            }
        }

        if (clauses is null)
            return Unwrap(first.Value);

        var node = new BooleanQueryNode { Clauses = new List<BooleanClause>(clauses.Count) };
        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            bool orAdjacent = clause.Operator == BooleanOperator.Or
                || i + 1 < clauses.Count && clauses[i + 1].Operator == BooleanOperator.Or;

            var booleanClause = clause.Modifier switch
            {
                ClauseModifier.Plus => new BooleanClause(clause.Node, Occur.Must),
                ClauseModifier.Minus => new BooleanClause(clause.Node, Occur.MustNot),
                ClauseModifier.Not or ClauseModifier.Bang when orAdjacent => new BooleanClause(Negate(clause.Node), Occur.Should),
                ClauseModifier.Not or ClauseModifier.Bang => new BooleanClause(clause.Node, Occur.MustNot),
                _ => new BooleanClause(clause.Node, Occur.Should)
            };

            booleanClause.Operator = clause.Operator;
            if (booleanClause.Query == clause.Node)
                booleanClause.Modifier = clause.Modifier;

            node.Clauses.Add(booleanClause);
        }

        return SetSpan(node, node.Clauses[0].Query!, clauses[^1].Node);
    }

    private Clause? ParseAnd()
    {
        var first = ParseUnary();
        if (first is null)
            return null;

        List<Clause>? clauses = null;
        while (!IsAtEnd)
        {
            if (Current.Type == TokenType.And)
            {
                var andToken = Current;
                Advance();
                var next = ParseUnary();
                if (next is null)
                {
                    AddError($"Expected a query after '{andToken.GetString()}'", andToken, QueryErrorCode.UnexpectedToken);
                    break;
                }

                (clauses ??= [first.Value]).Add(next.Value with { Operator = BooleanOperator.And });
            }
            else if (_options.DefaultOperator == BooleanOperator.And && CanStartClause(Current.Type))
            {
                var next = ParseUnary();
                if (next is null)
                    break;

                (clauses ??= [first.Value]).Add(next.Value with { Operator = BooleanOperator.Implicit });
            }
            else
            {
                break;
            }
        }

        if (clauses is null)
            return first;

        var node = new BooleanQueryNode { Clauses = new List<BooleanClause>(clauses.Count) };
        foreach (var clause in clauses)
        {
            var occur = clause.Modifier is ClauseModifier.Minus or ClauseModifier.Not or ClauseModifier.Bang ? Occur.MustNot : Occur.Must;
            node.Clauses.Add(new BooleanClause(clause.Node, occur) { Operator = clause.Operator, Modifier = clause.Modifier });
        }

        SetSpan(node, clauses[0].Node, clauses[^1].Node);
        return new Clause(node, ClauseModifier.None, BooleanOperator.Implicit);
    }

    private Clause? ParseUnary()
    {
        var startToken = Current;
        var modifier = ClauseModifier.None;

        if (startToken.Type is TokenType.Plus or TokenType.Minus or TokenType.Not)
        {
            modifier = startToken.Type switch
            {
                TokenType.Plus => ClauseModifier.Plus,
                TokenType.Minus => ClauseModifier.Minus,
                _ => startToken.Span.SequenceEqual("!") ? ClauseModifier.Bang : ClauseModifier.Not
            };
            Advance();

            while (Current.Type is TokenType.Plus or TokenType.Minus or TokenType.Not)
            {
                AddError($"Unexpected operator '{Current.GetString()}'", Current, QueryErrorCode.UnexpectedToken);
                Advance();
            }
        }

        var node = ParsePrimary();
        if (node is null)
        {
            if (modifier != ClauseModifier.None)
                AddError($"Expected a query after '{startToken.GetString()}'", startToken, QueryErrorCode.UnexpectedToken);

            return null;
        }

        if (modifier != ClauseModifier.None)
        {
            node.StartPosition = startToken.Position;
            node.StartLine = startToken.Line;
            node.StartColumn = startToken.Column;
        }

        return new Clause(node, modifier, BooleanOperator.Implicit);
    }

    private QueryNode? ParsePrimary()
    {
        while (Current.Type == TokenType.Invalid)
            Advance();

        return Current.Type switch
        {
            TokenType.LeftParen => ParseGroup(),
            TokenType.LeftBracket or TokenType.LeftBrace => ParseRange(),
            TokenType.GreaterThan or TokenType.GreaterThanOrEqual or TokenType.LessThan or TokenType.LessThanOrEqual => ParseShortRange(),
            TokenType.QuotedString => ParsePhrase(),
            TokenType.Regex => ParseRegex(),
            TokenType.Term => Peek(1).Type == TokenType.Colon ? ParseFieldQuery() : ParseTerm(),
            _ => null
        };
    }

    private GroupNode ParseGroup()
    {
        var startToken = Current;
        Advance();

        if (++_depth > _options.MaxDepth)
        {
            AddError($"Query nesting depth exceeds the maximum of {_options.MaxDepth}", startToken, QueryErrorCode.MaxDepthExceeded);
            SkipBalancedGroup();
            _depth--;
            return SetSpan(new GroupNode(), startToken);
        }

        var inner = ParseOr();
        while (!IsAtEnd && Current.Type != TokenType.RightParen)
        {
            ReportUnexpected(Current);
            Advance();
            inner = Combine(inner, ParseOr());
        }

        if (Current.Type == TokenType.RightParen)
        {
            if (inner is null)
                AddError("Empty group '()'", startToken, QueryErrorCode.UnexpectedToken);
            Advance();
        }
        else
        {
            AddError("Missing closing ')' for group", startToken, QueryErrorCode.UnmatchedBracket);
        }

        _depth--;

        var group = SetSpan(new GroupNode { Query = inner }, startToken);
        ParseModifiers(group);
        return group;
    }

    private QueryNode ParseFieldQuery()
    {
        var fieldToken = Current;
        Advance(); // field
        var colonToken = Current;
        Advance(); // :

        var field = fieldToken.HasEscapes ? QueryText.UnescapeMemory(fieldToken.Value) : fieldToken.Value;
        var fieldSpan = field.Span;

        bool isExists = fieldSpan.Equals("_exists_", StringComparison.OrdinalIgnoreCase);
        if (isExists || fieldSpan.Equals("_missing_", StringComparison.OrdinalIgnoreCase))
            return ParseExistsOrMissing(fieldToken, isExists);

        if (Current.Type is TokenType.Plus or TokenType.Minus or TokenType.Not)
        {
            AddError("Place '+', '-', '!', or 'NOT' before the field name, not after ':'. Quote or escape literal values.", Current, QueryErrorCode.UnexpectedToken);
            Advance();
        }

        QueryNode? value;
        switch (Current.Type)
        {
            case TokenType.Term when IsStar(Current):
                var starToken = Current;
                Advance();
                if (fieldSpan is "*")
                    return SetSpan(new MatchAllNode(), fieldToken);

                var exists = SetSpan(new ExistsNode { FieldMemory = field }, fieldToken);
                exists.EndPosition = starToken.EndPosition;
                return exists;
            case TokenType.LeftParen:
                value = ParseGroup();
                break;
            case TokenType.LeftBracket or TokenType.LeftBrace:
                value = ParseRange();
                break;
            case TokenType.GreaterThan or TokenType.GreaterThanOrEqual or TokenType.LessThan or TokenType.LessThanOrEqual:
                value = ParseShortRange();
                break;
            case TokenType.QuotedString:
                value = ParsePhrase();
                break;
            case TokenType.Regex:
                value = ParseRegex();
                break;
            case TokenType.Term:
                value = ParseTerm();
                break;
            default:
                AddError($"Expected a value after '{fieldToken.GetString()}:'", IsAtEnd ? colonToken : Current, QueryErrorCode.UnexpectedToken);
                value = null;
                break;
        }

        var node = SetSpan(new FieldQueryNode { FieldMemory = field, Query = value }, fieldToken);
        if (value is null)
            node.EndPosition = colonToken.EndPosition;

        return node;
    }

    private QueryNode ParseExistsOrMissing(Token fieldToken, bool isExists)
    {
        var nameToken = Current;
        ReadOnlyMemory<char> name = default;
        if (nameToken.Type is TokenType.Term or TokenType.QuotedString)
        {
            name = nameToken.HasEscapes ? QueryText.UnescapeMemory(nameToken.Value) : nameToken.Value;
            Advance();
        }
        else
        {
            AddError($"Expected a field name after '{fieldToken.GetString()}:'", IsAtEnd ? fieldToken : nameToken, QueryErrorCode.InvalidFieldName);
        }

        QueryNode node = isExists
            ? new ExistsNode { FieldMemory = name, IsExistsSyntax = true }
            : new MissingNode { FieldMemory = name };

        return SetSpan(node, fieldToken);
    }

    private QueryNode ParseTerm()
    {
        var token = Current;
        Advance();

        if (IsStar(token))
        {
            var matchAll = SetSpan(new MatchAllNode(), token);
            ParseModifiers(matchAll);
            return matchAll;
        }

        var value = token.Value;
        bool isPrefix = false, isWildcard = false;
        if (token.HasWildcard)
        {
            if (IsSimplePrefix(value.Span))
            {
                isPrefix = true;
                value = value[..^1];
            }
            else
            {
                isWildcard = true;
            }
        }

        var node = SetSpan(new TermNode { TermMemory = value, IsPrefix = isPrefix, IsWildcard = isWildcard }, token);
        ParseModifiers(node);
        return node;
    }

    private PhraseNode ParsePhrase()
    {
        var token = Current;
        Advance();

        var phrase = token.HasEscapes ? QueryText.UnescapeMemory(token.Value) : token.Value;
        var node = SetSpan(new PhraseNode { PhraseMemory = phrase }, token);
        ParseModifiers(node);
        return node;
    }

    private RegexNode ParseRegex()
    {
        var token = Current;
        Advance();

        var node = SetSpan(new RegexNode { PatternMemory = token.Value }, token);
        ParseModifiers(node);
        return node;
    }

    private RangeNode ParseRange()
    {
        var startToken = Current;
        Advance();

        var node = new RangeNode { MinInclusive = startToken.Type == TokenType.LeftBracket };

        if (Current.Type is TokenType.To or TokenType.RangeDots)
            AddError("Expected a lower bound for the range (use '*' for an unbounded range)", Current, QueryErrorCode.InvalidRange);
        else if (TryReadRangeBound(out var min))
            node.MinMemory = min;

        if (Current.Type is TokenType.To or TokenType.RangeDots)
        {
            Advance();
        }
        else
        {
            AddError("Expected 'TO' in range", IsAtEnd ? startToken : Current, QueryErrorCode.InvalidRange);
        }

        if (Current.Type is TokenType.RightBracket or TokenType.RightBrace)
            AddError("Expected an upper bound for the range (use '*' for an unbounded range)", Current, QueryErrorCode.InvalidRange);
        else if (TryReadRangeBound(out var max))
            node.MaxMemory = max;

        if (Current.Type is TokenType.RightBracket or TokenType.RightBrace)
        {
            node.MaxInclusive = Current.Type == TokenType.RightBracket;
            Advance();
        }
        else
        {
            AddError("Missing closing ']' or '}' for range", startToken, QueryErrorCode.UnmatchedBracket);
        }

        SetSpan(node, startToken);
        ParseModifiers(node);
        return node;
    }

    private bool TryReadRangeBound(out ReadOnlyMemory<char> value)
    {
        var token = Current;
        if (token.Type is TokenType.Term)
        {
            Advance();
            value = IsStar(token) ? default : token.HasEscapes ? QueryText.UnescapeMemory(token.Value) : token.Value;
            return true;
        }

        if (token.Type is TokenType.QuotedString)
        {
            Advance();
            value = token.HasEscapes ? QueryText.UnescapeMemory(token.Value) : token.Value;
            return true;
        }

        AddError("Expected a range bound", token, QueryErrorCode.InvalidRange);
        value = default;
        return false;
    }

    private RangeNode ParseShortRange()
    {
        var opToken = Current;
        Advance();

        var op = opToken.Type switch
        {
            TokenType.GreaterThan => RangeOperator.GreaterThan,
            TokenType.GreaterThanOrEqual => RangeOperator.GreaterThanOrEqual,
            TokenType.LessThan => RangeOperator.LessThan,
            _ => RangeOperator.LessThanOrEqual
        };

        var node = new RangeNode { Operator = op };
        ReadOnlyMemory<char> value = default;
        if (Current.Type is TokenType.Term or TokenType.QuotedString)
        {
            var token = Current;
            Advance();
            value = token.HasEscapes ? QueryText.UnescapeMemory(token.Value) : token.Value;
        }
        else
        {
            AddError($"Expected a value after '{opToken.GetString()}'", opToken, QueryErrorCode.InvalidRange);
        }

        if (op is RangeOperator.GreaterThan or RangeOperator.GreaterThanOrEqual)
        {
            node.MinMemory = value;
            node.MinInclusive = op == RangeOperator.GreaterThanOrEqual;
        }
        else
        {
            node.MaxMemory = value;
            node.MaxInclusive = op == RangeOperator.LessThanOrEqual;
        }

        SetSpan(node, opToken);
        ParseModifiers(node);
        return node;
    }

    private void ParseModifiers(QueryNode node)
    {
        while (Current.Type is TokenType.Tilde or TokenType.Caret)
        {
            var modifierToken = Current;
            Advance();

            string? text = null;
            if (Current.Type == TokenType.ModifierValue)
            {
                text = Current.HasEscapes ? QueryText.Unescape(Current.Span) : Current.GetString();
                Advance();
            }

            if (modifierToken.Type == TokenType.Tilde)
            {
                if (node is not IProximityModifiable proximity)
                    AddError($"'~' is not supported on this kind of query", modifierToken, QueryErrorCode.UnexpectedToken);
                else if (proximity.ProximityText is not null)
                    AddError("Duplicate '~' modifier", modifierToken, QueryErrorCode.UnexpectedToken);
                else
                    proximity.ProximityText = text ?? string.Empty;
            }
            else if (text is null)
            {
                AddError("Expected a value after '^'", modifierToken, QueryErrorCode.UnexpectedToken);
            }
            else if (node is not IBoostable boostable)
            {
                AddError($"'^' is not supported on this kind of query", modifierToken, QueryErrorCode.UnexpectedToken);
            }
            else if (boostable.BoostText is not null)
            {
                AddError("Duplicate '^' modifier", modifierToken, QueryErrorCode.UnexpectedToken);
            }
            else
            {
                boostable.BoostText = text;
            }

            node.EndPosition = _lastEnd;
        }
    }

    private static NotNode Negate(QueryNode node)
    {
        return new NotNode
        {
            Query = node,
            StartPosition = node.StartPosition,
            EndPosition = node.EndPosition,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
    }

    private QueryNode Unwrap(Clause clause)
    {
        if (clause.Modifier == ClauseModifier.None)
            return clause.Node;

        var occur = clause.Modifier == ClauseModifier.Plus ? Occur.Must : Occur.MustNot;
        var node = new BooleanQueryNode { Clauses = [new BooleanClause(clause.Node, occur) { Modifier = clause.Modifier }] };
        return SetSpan(node, clause.Node, clause.Node);
    }

    private QueryNode? Combine(QueryNode? left, QueryNode? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;

        var occur = _options.DefaultOperator == BooleanOperator.And ? Occur.Must : Occur.Should;
        var node = new BooleanQueryNode { Clauses = [new BooleanClause(left, occur), new BooleanClause(right, occur)] };
        return SetSpan(node, left, right);
    }

    private void SkipBalancedGroup()
    {
        int balance = 1;
        while (!IsAtEnd && balance > 0)
        {
            var type = Current.Type;
            Advance();
            if (type == TokenType.LeftParen)
                balance++;
            else if (type == TokenType.RightParen)
                balance--;
        }
    }

    private static bool CanStartClause(TokenType type)
    {
        return type is TokenType.Term or TokenType.QuotedString or TokenType.Regex
            or TokenType.Plus or TokenType.Minus or TokenType.Not
            or TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace
            or TokenType.GreaterThan or TokenType.GreaterThanOrEqual or TokenType.LessThan or TokenType.LessThanOrEqual
            or TokenType.Invalid;
    }

    /// <summary>
    /// A term is a simple prefix when its only unescaped wildcard is a single trailing <c>*</c>.
    /// </summary>
    private static bool IsSimplePrefix(ReadOnlySpan<char> value)
    {
        if (value.Length < 2 || value[^1] != '*')
            return false;

        for (int i = 0; i < value.Length - 1; i++)
        {
            char c = value[i];
            if (c == '\\')
            {
                i++;
                if (i == value.Length - 1)
                    return false; // the trailing * is escaped
                continue;
            }

            if (c is '*' or '?')
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStar(Token token) => token.Type == TokenType.Term && token.Length == 1 && token.Span[0] == '*';

    private void ReportUnexpected(Token token)
    {
        string text = token.Type == TokenType.EndOfFile ? "end of query" : $"'{token.GetString()}'";
        AddError($"Unexpected {text}", token, QueryErrorCode.UnexpectedToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddError(string message, Token token, QueryErrorCode code)
    {
        (_errors ??= []).Add(new ParseError(message, token.Position, token.Length, token.Line, token.Column, code));
    }

    private T SetSpan<T>(T node, Token startToken) where T : QueryNode
    {
        node.StartPosition = startToken.Position;
        node.StartLine = startToken.Line;
        node.StartColumn = startToken.Column;
        node.EndPosition = Math.Max(_lastEnd, startToken.EndPosition);
        return node;
    }

    private static T SetSpan<T>(T node, QueryNode first, QueryNode last) where T : QueryNode
    {
        node.StartPosition = first.StartPosition;
        node.StartLine = first.StartLine;
        node.StartColumn = first.StartColumn;
        node.EndPosition = last.EndPosition;
        return node;
    }

    private Token Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tokens[_position];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Peek(int offset)
    {
        int index = _position + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private bool IsAtEnd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tokens[_position].Type == TokenType.EndOfFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance()
    {
        if (_position < _tokens.Count - 1)
        {
            _lastEnd = _tokens[_position].EndPosition;
            _position++;
        }
    }
}
