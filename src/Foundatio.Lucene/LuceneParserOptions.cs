using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Options that control how query text is parsed.
/// </summary>
public sealed record LuceneParserOptions
{
    private readonly BooleanOperator _defaultOperator = BooleanOperator.And;
    private readonly int _maxDepth = 100;

    /// <summary>
    /// The default options: implicit operator AND and a maximum nesting depth of 100.
    /// </summary>
    public static LuceneParserOptions Default { get; } = new();

    /// <summary>
    /// The operator used between clauses that are written next to each other without AND or OR.
    /// Defaults to <see cref="BooleanOperator.And"/>, matching Foundatio.Parsers. Use <see cref="BooleanOperator.Or"/>
    /// for Lucene / Elasticsearch <c>query_string</c> style search boxes.
    /// </summary>
    public BooleanOperator DefaultOperator
    {
        get => _defaultOperator;
        init => _defaultOperator = value == BooleanOperator.Implicit
            ? throw new ArgumentOutOfRangeException(nameof(DefaultOperator), value, "The default operator must be And or Or.")
            : value;
    }

    /// <summary>
    /// The maximum nesting depth of parenthesized groups. Deeper input produces a parse error instead of
    /// recursing, which protects against stack exhaustion from malicious queries. Defaults to 100.
    /// </summary>
    public int MaxDepth
    {
        get => _maxDepth;
        init => _maxDepth = value < 1
            ? throw new ArgumentOutOfRangeException(nameof(MaxDepth), value, "The maximum depth must be at least 1.")
            : value;
    }
}
