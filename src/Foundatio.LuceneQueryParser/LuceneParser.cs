using System.Runtime.CompilerServices;
using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser;

/// <summary>
/// Parser for Lucene query language with Elasticsearch extensions.
/// Converts tokens into an Abstract Syntax Tree (AST).
/// </summary>
public class LuceneParser
{
    private readonly List<Token> _tokens;
    private int _position;
    private List<ParseError>? _errors;

    /// <summary>
    /// The default operator to use when no explicit operator is specified.
    /// </summary>
    public BooleanOperator DefaultOperator { get; set; } = BooleanOperator.Or;

    /// <summary>
    /// Whether to split on whitespace when parsing groups.
    /// When false, consecutive terms in a group are combined into a MultiTermNode.
    /// When true, terms are parsed as separate clauses with implicit operators.
    /// Default is true for backward compatibility.
    /// </summary>
    public bool SplitOnWhitespace { get; set; } = true;

    public LuceneParser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _position = 0;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpanEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        => a.SequenceEqual(b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpanEqualsIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStar(ReadOnlySpan<char> span)
        => span.Length == 1 && span[0] == '*';

    /// <summary>
    /// Gets the list of errors encountered during parsing.
    /// </summary>
    public List<ParseError> Errors => _errors ??= [];

    /// <summary>
    /// Parses the tokens into a query document AST.
    /// </summary>
    public QueryDocument Parse()
    {
        SkipWhitespace();

        var document = new QueryDocument();

        if (IsAtEnd())
        {
            return document;
        }

        try
        {
            document.Query = ParseQuery();
            document.StartPosition = document.Query?.StartPosition ?? 0;
            document.EndPosition = document.Query?.EndPosition ?? 0;
            document.StartLine = document.Query?.StartLine ?? 1;
            document.StartColumn = document.Query?.StartColumn ?? 1;
        }
        catch (Exception ex)
        {
            Errors.Add(new ParseError(ex.Message, CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
        }

        return document;
    }

    /// <summary>
    /// Parses a query expression (handles OR at the lowest precedence).
    /// </summary>
    private QueryNode? ParseQuery()
    {
        // When SplitOnWhitespace is false, try to parse as MultiTerm first
        if (!SplitOnWhitespace)
        {
            var multiTerm = TryParseMultiTerm();
            if (multiTerm != null)
            {
                return multiTerm;
            }
        }
        
        return ParseOrQuery();
    }

    /// <summary>
    /// Parses OR expressions.
    /// </summary>
    private QueryNode? ParseOrQuery()
    {
        var left = ParseAndQuery();
        if (left == null) return null;

        var clauses = new List<BooleanClause>
        {
            new() { Query = left, Occur = Occur.Should, Operator = BooleanOperator.Implicit }
        };

        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd()) break;

            int positionBeforeParse = _position;

            // Check for explicit OR
            if (CurrentToken.Type == TokenType.Or)
            {
                Advance(); // Skip OR
                SkipWhitespace();

                var right = ParseAndQuery();
                if (right != null)
                {
                    clauses.Add(new BooleanClause { Query = right, Occur = Occur.Should, Operator = BooleanOperator.Or });
                }
                else if (_position == positionBeforeParse + 1)
                {
                    // No progress after OR, break to avoid infinite loop
                    break;
                }
            }
            // Check for implicit OR (next clause without explicit operator)
            else if (DefaultOperator == BooleanOperator.Or &&
                     !IsAtEndOfClause() &&
                     CurrentToken.Type != TokenType.And &&
                     CurrentToken.Type != TokenType.RightParen)
            {
                var right = ParseAndQuery();
                if (right != null)
                {
                    clauses.Add(new BooleanClause { Query = right, Occur = Occur.Should, Operator = BooleanOperator.Implicit });
                }
                
                // If no progress was made, break to avoid infinite loop
                if (_position == positionBeforeParse)
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        if (clauses.Count == 1)
        {
            return left;
        }

        return new BooleanQueryNode
        {
            Clauses = clauses,
            StartPosition = left.StartPosition,
            EndPosition = clauses[^1].Query?.EndPosition ?? left.EndPosition,
            StartLine = left.StartLine,
            StartColumn = left.StartColumn
        };
    }

    /// <summary>
    /// Parses AND expressions.
    /// </summary>
    private QueryNode? ParseAndQuery()
    {
        var left = ParseClause();
        if (left == null) return null;

        var clauses = new List<BooleanClause>
        {
            new() { Query = left, Occur = Occur.Must, Operator = BooleanOperator.Implicit }
        };

        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd()) break;

            int positionBeforeParse = _position;

            // Check for explicit AND
            if (CurrentToken.Type == TokenType.And)
            {
                Advance(); // Skip AND
                SkipWhitespace();

                var right = ParseClause();
                if (right != null)
                {
                    clauses.Add(new BooleanClause { Query = right, Occur = Occur.Must, Operator = BooleanOperator.And });
                }
                else if (_position == positionBeforeParse + 1)
                {
                    // No progress after AND, break to avoid infinite loop
                    break;
                }
            }
            // Check for implicit AND when default operator is AND
            else if (DefaultOperator == BooleanOperator.And &&
                     !IsAtEndOfClause() &&
                     CurrentToken.Type != TokenType.Or &&
                     CurrentToken.Type != TokenType.RightParen)
            {
                var right = ParseClause();
                if (right != null)
                {
                    clauses.Add(new BooleanClause { Query = right, Occur = Occur.Must, Operator = BooleanOperator.Implicit });
                }
                
                // If no progress was made, break to avoid infinite loop
                if (_position == positionBeforeParse)
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        if (clauses.Count == 1)
        {
            // Single clause in AND mode - adjust occur to Should for proper handling
            clauses[0].Occur = Occur.Should;
            return left;
        }

        return new BooleanQueryNode
        {
            Clauses = clauses,
            StartPosition = left.StartPosition,
            EndPosition = clauses[^1].Query?.EndPosition ?? left.EndPosition,
            StartLine = left.StartLine,
            StartColumn = left.StartColumn
        };
    }

    /// <summary>
    /// Parses a single clause (with optional modifiers + - NOT).
    /// </summary>
    private QueryNode? ParseClause()
    {
        SkipWhitespace();

        if (IsAtEnd()) return null;

        var startToken = CurrentToken;
        Occur occur = Occur.Should;
        bool isNot = false;

        // Check for +/- modifiers
        if (CurrentToken.Type == TokenType.Plus)
        {
            occur = Occur.Must;
            Advance();
            SkipWhitespace();
        }
        else if (CurrentToken.Type == TokenType.Minus)
        {
            occur = Occur.MustNot;
            Advance();
            SkipWhitespace();
        }
        else if (CurrentToken.Type == TokenType.Not)
        {
            isNot = true;
            Advance();
            SkipWhitespace();
        }

        var query = ParsePrimary();

        if (query == null) return null;

        // Handle NOT wrapping
        if (isNot)
        {
            query = new NotNode
            {
                Query = query,
                StartPosition = startToken.Position,
                EndPosition = query.EndPosition,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
        }

        // If we have a modifier, wrap in a boolean query if needed
        if (occur != Occur.Should && query is not BooleanQueryNode)
        {
            return new BooleanQueryNode
            {
                Clauses = [new BooleanClause { Query = query, Occur = occur, Operator = BooleanOperator.Implicit }],
                StartPosition = startToken.Position,
                EndPosition = query.EndPosition,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
        }

        return query;
    }

    /// <summary>
    /// Parses a primary expression (groups, fields, terms, etc.).
    /// </summary>
    private QueryNode? ParsePrimary()
    {
        if (IsAtEnd()) return null;

        // Check for grouping
        if (CurrentToken.Type == TokenType.LeftParen)
        {
            return ParseGroup();
        }

        // Check for range query
        if (CurrentToken.Type == TokenType.LeftBracket || CurrentToken.Type == TokenType.LeftBrace)
        {
            return ParseRange();
        }

        // Check for short-form range (>, >=, <, <=)
        if (CurrentToken.Type == TokenType.GreaterThan ||
            CurrentToken.Type == TokenType.GreaterThanOrEqual ||
            CurrentToken.Type == TokenType.LessThan ||
            CurrentToken.Type == TokenType.LessThanOrEqual)
        {
            return ParseShortRange();
        }

        // Check for term with potential field prefix
        return ParseFieldOrTerm();
    }

    /// <summary>
    /// Parses a grouped expression.
    /// When SplitOnWhitespace is false, consecutive simple terms are combined into a MultiTermNode.
    /// </summary>
    private GroupNode ParseGroup()
    {
        var startToken = CurrentToken;
        Advance(); // Skip (

        SkipWhitespace();

        QueryNode? innerQuery;
        
        if (!SplitOnWhitespace)
        {
            // Try to parse as MultiTerm first (consecutive simple terms without operators)
            innerQuery = TryParseMultiTerm();
            if (innerQuery == null)
            {
                // Fall back to normal query parsing
                innerQuery = ParseQuery();
            }
        }
        else
        {
            innerQuery = ParseQuery();
        }

        SkipWhitespace();

        if (CurrentToken.Type == TokenType.RightParen)
        {
            Advance(); // Skip )
        }
        else
        {
            Errors.Add(new ParseError("Expected ')'", CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
        }

        var group = new GroupNode
        {
            Query = innerQuery,
            StartPosition = startToken.Position,
            EndPosition = CurrentToken.Position,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            group.Boost = ParseBoost();
        }

        return group;
    }

    /// <summary>
    /// Tries to parse consecutive simple terms as a MultiTermNode.
    /// Returns null if the content is not suitable for MultiTerm (contains operators, ranges, etc.).
    /// </summary>
    private MultiTermNode? TryParseMultiTerm()
    {
        int savedPosition = _position;
        var startToken = CurrentToken;
        var terms = new List<ReadOnlyMemory<char>>();

        while (!IsAtEnd() && CurrentToken.Type != TokenType.RightParen)
        {
            SkipWhitespace();
            if (IsAtEnd() || CurrentToken.Type == TokenType.RightParen)
                break;

            // If we encounter an operator or modifier, this isn't a simple multi-term
            // Boost (^) and fuzzy (~) on individual terms means we should not combine into MultiTerm
            if (CurrentToken.Type == TokenType.And ||
                CurrentToken.Type == TokenType.Or ||
                CurrentToken.Type == TokenType.Not ||
                CurrentToken.Type == TokenType.Plus ||
                CurrentToken.Type == TokenType.Minus ||
                CurrentToken.Type == TokenType.LeftParen ||
                CurrentToken.Type == TokenType.LeftBracket ||
                CurrentToken.Type == TokenType.LeftBrace ||
                CurrentToken.Type == TokenType.Colon ||
                CurrentToken.Type == TokenType.Caret ||
                CurrentToken.Type == TokenType.Tilde ||
                CurrentToken.Type == TokenType.GreaterThan ||
                CurrentToken.Type == TokenType.GreaterThanOrEqual ||
                CurrentToken.Type == TokenType.LessThan ||
                CurrentToken.Type == TokenType.LessThanOrEqual)
            {
                // Not a simple multi-term, backtrack
                _position = savedPosition;
                return null;
            }

            // Accept simple terms
            if (CurrentToken.Type == TokenType.Term)
            {
                terms.Add(CurrentToken.Value);
                Advance();
            }
            else
            {
                // Unexpected token type, backtrack
                _position = savedPosition;
                return null;
            }

            SkipWhitespace();
        }

        if (terms.Count == 0)
        {
            _position = savedPosition;
            return null;
        }

        // Build combined text - this requires allocation but only for MultiTermNode
        var combinedText = BuildCombinedText(terms);

        // If only one term, still create MultiTerm for consistency when SplitOnWhitespace is false
        return new MultiTermNode
        {
            TermsMemory = terms,
            CombinedTextMemory = combinedText,
            StartPosition = startToken.Position,
            EndPosition = CurrentToken.Position,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<char> BuildCombinedText(List<ReadOnlyMemory<char>> terms)
    {
        if (terms.Count == 1)
            return terms[0];
            
        int totalLength = terms.Count - 1; // spaces
        foreach (var term in terms)
            totalLength += term.Length;
            
        var chars = new char[totalLength];
        int pos = 0;
        for (int i = 0; i < terms.Count; i++)
        {
            if (i > 0)
                chars[pos++] = ' ';
            terms[i].Span.CopyTo(chars.AsSpan(pos));
            pos += terms[i].Length;
        }
        return chars.AsMemory();
    }

    /// <summary>
    /// Parses a field:value expression or a simple term.
    /// </summary>
    private QueryNode? ParseFieldOrTerm()
    {
        var startToken = CurrentToken;

        // Check if this is a field:value pattern
        if (CurrentToken.Type == TokenType.Term ||
            CurrentToken.Type == TokenType.Wildcard ||
            CurrentToken.Type == TokenType.Prefix)
        {
            var potentialField = CurrentToken.Value;
            int savedPosition = _position;

            Advance();
            SkipWhitespace();

            if (CurrentToken.Type == TokenType.Colon)
            {
                // This is a field query
                Advance(); // Skip :
                SkipWhitespace();

                return ParseFieldValue(potentialField, startToken);
            }
            else
            {
                // Backtrack - this is just a term
                _position = savedPosition;
                return ParseTerm();
            }
        }

        return ParseTerm();
    }

    /// <summary>
    /// Parses the value part of a field:value expression.
    /// </summary>
    private QueryNode ParseFieldValue(ReadOnlyMemory<char> field, Token startToken)
    {
        QueryNode? valueQuery;
        var fieldSpan = field.Span;

        // Check for match-all query (*:*)
        if (IsStar(fieldSpan) && CurrentToken.Type == TokenType.Prefix && IsStar(CurrentToken.Span))
        {
            Advance();
            return new MatchAllNode
            {
                StartPosition = startToken.Position,
                EndPosition = CurrentToken.Position,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
        }

        // Check for _exists_:fieldname syntax
        if (SpanEqualsIgnoreCase(fieldSpan, "_exists_"))
        {
            var fieldNameToken = CurrentToken;
            if (fieldNameToken.Type == TokenType.Term || fieldNameToken.Type == TokenType.Wildcard || fieldNameToken.Type == TokenType.Prefix)
            {
                Advance();
                return new ExistsNode
                {
                    FieldMemory = fieldNameToken.Value,
                    IsExistsSyntax = true,
                    StartPosition = startToken.Position,
                    EndPosition = CurrentToken.Position,
                    StartLine = startToken.Line,
                    StartColumn = startToken.Column
                };
            }
        }

        // Check for _missing_:fieldname syntax
        if (SpanEqualsIgnoreCase(fieldSpan, "_missing_"))
        {
            var fieldNameToken = CurrentToken;
            if (fieldNameToken.Type == TokenType.Term || fieldNameToken.Type == TokenType.Wildcard || fieldNameToken.Type == TokenType.Prefix)
            {
                Advance();
                return new MissingNode
                {
                    FieldMemory = fieldNameToken.Value,
                    StartPosition = startToken.Position,
                    EndPosition = CurrentToken.Position,
                    StartLine = startToken.Line,
                    StartColumn = startToken.Column
                };
            }
        }

        // Check for exists query (field:*)
        if (CurrentToken.Type == TokenType.Prefix && IsStar(CurrentToken.Span))
        {
            Advance();
            var existsNode = new ExistsNode
            {
                FieldMemory = field,
                IsExistsSyntax = false,
                StartPosition = startToken.Position,
                EndPosition = CurrentToken.Position,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
            return existsNode;
        }

        // Check for grouping within field
        if (CurrentToken.Type == TokenType.LeftParen)
        {
            valueQuery = ParseGroup();
        }
        // Check for range query
        else if (CurrentToken.Type == TokenType.LeftBracket || CurrentToken.Type == TokenType.LeftBrace)
        {
            valueQuery = ParseRange();
        }
        // Check for short-form range
        else if (CurrentToken.Type == TokenType.GreaterThan ||
                 CurrentToken.Type == TokenType.GreaterThanOrEqual ||
                 CurrentToken.Type == TokenType.LessThan ||
                 CurrentToken.Type == TokenType.LessThanOrEqual)
        {
            valueQuery = ParseShortRange();
        }
        else
        {
            valueQuery = ParseTerm();
        }

        if (valueQuery == null)
        {
            Errors.Add(new ParseError($"Expected value after field '{field.Span.ToString()}:'", CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
            return new TermNode
            {
                TermMemory = ReadOnlyMemory<char>.Empty,
                StartPosition = startToken.Position,
                EndPosition = CurrentToken.Position,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
        }

        return new FieldQueryNode
        {
            FieldMemory = field,
            Query = valueQuery,
            StartPosition = startToken.Position,
            EndPosition = valueQuery.EndPosition,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };
    }

    /// <summary>
    /// Parses a term, phrase, regex, or wildcard.
    /// </summary>
    private QueryNode? ParseTerm()
    {
        if (IsAtEnd()) return null;

        var startToken = CurrentToken;

        // Check for match all
        if (CurrentToken.Type == TokenType.Prefix && IsStar(CurrentToken.Span))
        {
            Advance();
            return new MatchAllNode
            {
                StartPosition = startToken.Position,
                EndPosition = startToken.Position + startToken.Length,
                StartLine = startToken.Line,
                StartColumn = startToken.Column
            };
        }

        // Check for quoted phrase
        if (CurrentToken.Type == TokenType.QuotedString)
        {
            return ParsePhrase();
        }

        // Check for regex
        if (CurrentToken.Type == TokenType.Regex)
        {
            return ParseRegex();
        }

        // Regular term (may include wildcards)
        if (CurrentToken.Type == TokenType.Term ||
            CurrentToken.Type == TokenType.Wildcard ||
            CurrentToken.Type == TokenType.Prefix)
        {
            return ParseTermNode();
        }

        return null;
    }

    /// <summary>
    /// Parses a term node with optional modifiers.
    /// </summary>
    private TermNode ParseTermNode()
    {
        var startToken = CurrentToken;
        var term = CurrentToken.Value;
        var isWildcard = CurrentToken.Type == TokenType.Wildcard;
        var isPrefix = CurrentToken.Type == TokenType.Prefix;

        // For prefix terms, strip the trailing *
        if (isPrefix && term.Span.EndsWith("*".AsSpan()))
        {
            term = term.Slice(0, term.Length - 1);
        }

        Advance();

        var node = new TermNode
        {
            TermMemory = term,
            UnescapedTermMemory = UnescapeTerm(term),
            IsWildcard = isWildcard,
            IsPrefix = isPrefix,
            StartPosition = startToken.Position,
            EndPosition = startToken.Position + startToken.Length,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Check for fuzzy modifier
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Tilde)
        {
            Advance();
            node.FuzzyDistance = ParseFuzzyDistance();
            node.EndPosition = CurrentToken.Position;
        }

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a number as a term.
    /// </summary>
    private TermNode ParseNumberTerm()
    {
        var startToken = CurrentToken;
        var term = CurrentToken.Value;

        Advance();

        var node = new TermNode
        {
            TermMemory = term,
            UnescapedTermMemory = term,
            StartPosition = startToken.Position,
            EndPosition = startToken.Position + startToken.Length,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a quoted phrase.
    /// </summary>
    private PhraseNode ParsePhrase()
    {
        var startToken = CurrentToken;
        var phrase = CurrentToken.Value;

        Advance();

        var node = new PhraseNode
        {
            PhraseMemory = phrase,
            StartPosition = startToken.Position,
            EndPosition = startToken.Position + startToken.Length,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Check for proximity/slop modifier
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Tilde)
        {
            Advance();
            node.Slop = ParseFuzzyDistance();
            node.EndPosition = CurrentToken.Position;
        }

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a regex pattern.
    /// </summary>
    private RegexNode ParseRegex()
    {
        var startToken = CurrentToken;
        var pattern = CurrentToken.Value;

        Advance();

        var node = new RegexNode
        {
            PatternMemory = pattern,
            StartPosition = startToken.Position,
            EndPosition = startToken.Position + startToken.Length,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a range query [min TO max] or {min TO max}.
    /// </summary>
    private RangeNode ParseRange()
    {
        var startToken = CurrentToken;
        bool minInclusive = CurrentToken.Type == TokenType.LeftBracket;

        Advance(); // Skip [ or {
        SkipWhitespace();

        // Parse min value
        ReadOnlyMemory<char>? min = null;
        if (CurrentToken.Type != TokenType.To)
        {
            min = ParseRangeValue();
        }

        SkipWhitespace();

        // Expect TO
        if (CurrentToken.Type != TokenType.To)
        {
            Errors.Add(new ParseError("Expected 'TO' in range query", CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
        }
        else
        {
            Advance(); // Skip TO
        }

        SkipWhitespace();

        // Parse max value
        ReadOnlyMemory<char>? max = null;
        if (CurrentToken.Type != TokenType.RightBracket && CurrentToken.Type != TokenType.RightBrace)
        {
            max = ParseRangeValue();
        }

        SkipWhitespace();

        // Expect ] or }
        bool maxInclusive = CurrentToken.Type == TokenType.RightBracket;
        if (CurrentToken.Type == TokenType.RightBracket || CurrentToken.Type == TokenType.RightBrace)
        {
            Advance();
        }
        else
        {
            Errors.Add(new ParseError("Expected ']' or '}' to close range query", CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
        }

        var node = new RangeNode
        {
            MinInclusive = minInclusive,
            MaxInclusive = maxInclusive,
            StartPosition = startToken.Position,
            EndPosition = CurrentToken.Position,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };
        
        // Set min/max, treating * as unbounded
        if (min.HasValue && !IsStar(min.Value.Span))
            node.MinMemory = min.Value;
        if (max.HasValue && !IsStar(max.Value.Span))
            node.MaxMemory = max.Value;

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a value in a range query.
    /// This method handles compound values like dates (2020-01-01) by collecting
    /// adjacent number tokens (since -01 is tokenized as a negative number).
    /// </summary>
    private ReadOnlyMemory<char>? ParseRangeValue()
    {
        if (CurrentToken.Type == TokenType.Term ||
            CurrentToken.Type == TokenType.QuotedString ||
            CurrentToken.Type == TokenType.Wildcard ||
            CurrentToken.Type == TokenType.Prefix)
        {
            var value = CurrentToken.Value;
            Advance();
            return value;
        }

        return null;
    }

    /// <summary>
    /// Parses a short-form range query (>, >=, <, <=).
    /// </summary>
    private RangeNode ParseShortRange()
    {
        var startToken = CurrentToken;
        RangeOperator op;
        bool inclusive = false;

        switch (CurrentToken.Type)
        {
            case TokenType.GreaterThan:
                op = RangeOperator.GreaterThan;
                break;
            case TokenType.GreaterThanOrEqual:
                op = RangeOperator.GreaterThanOrEqual;
                inclusive = true;
                break;
            case TokenType.LessThan:
                op = RangeOperator.LessThan;
                break;
            case TokenType.LessThanOrEqual:
                op = RangeOperator.LessThanOrEqual;
                inclusive = true;
                break;
            default:
                throw new InvalidOperationException("Unexpected operator type");
        }

        Advance();
        SkipWhitespace();

        var value = ParseRangeValue();

        var node = new RangeNode
        {
            Operator = op,
            StartPosition = startToken.Position,
            EndPosition = CurrentToken.Position,
            StartLine = startToken.Line,
            StartColumn = startToken.Column
        };

        // Set min/max based on operator
        switch (op)
        {
            case RangeOperator.GreaterThan:
            case RangeOperator.GreaterThanOrEqual:
                if (value.HasValue)
                    node.MinMemory = value.Value;
                node.MinInclusive = inclusive;
                node.MaxInclusive = true;
                break;
            case RangeOperator.LessThan:
            case RangeOperator.LessThanOrEqual:
                node.MinInclusive = true;
                if (value.HasValue)
                    node.MaxMemory = value.Value;
                node.MaxInclusive = inclusive;
                break;
        }

        // Check for boost
        SkipWhitespace();
        if (CurrentToken.Type == TokenType.Caret)
        {
            node.Boost = ParseBoost();
            node.EndPosition = CurrentToken.Position;
        }

        return node;
    }

    /// <summary>
    /// Parses a boost value after ^.
    /// </summary>
    private float ParseBoost()
    {
        Advance(); // Skip ^
        SkipWhitespace();

        if (CurrentToken.Type == TokenType.Term)
        {
            if (float.TryParse(CurrentToken.Span, out float boost))
            {
                Advance();
                return boost;
            }
        }

        Errors.Add(new ParseError("Expected numeric boost value after '^'", CurrentToken.Position, CurrentToken.Length, CurrentToken.Line, CurrentToken.Column));
        return 1.0f;
    }

    /// <summary>
    /// Parses a fuzzy distance value after ~.
    /// </summary>
    private int ParseFuzzyDistance()
    {
        SkipWhitespace();

        if (CurrentToken.Type == TokenType.Term)
        {
            if (int.TryParse(CurrentToken.Span, out int distance))
            {
                Advance();
                return distance;
            }
        }

        // Default fuzzy distance
        return 2;
    }

    /// <summary>
    /// Unescapes special characters in a term.
    /// </summary>
    private static ReadOnlyMemory<char> UnescapeTerm(ReadOnlyMemory<char> term)
    {
        var span = term.Span;
        
        // Fast path: no backslash means no escaping needed
        if (span.IndexOf('\\') < 0)
            return term;

        var sb = new System.Text.StringBuilder(span.Length);
        bool escaped = false;

        foreach (char c in span)
        {
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString().AsMemory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipWhitespace()
    {
        while (!IsAtEnd() && CurrentToken.Type == TokenType.Whitespace)
        {
            Advance();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAtEndOfClause()
    {
        return IsAtEnd() ||
               CurrentToken.Type == TokenType.RightParen ||
               CurrentToken.Type == TokenType.And ||
               CurrentToken.Type == TokenType.Or;
    }

    private Token CurrentToken
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAtEnd() => _position >= _tokens.Count || CurrentToken.Type == TokenType.EndOfFile;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance() => _position++;
}
