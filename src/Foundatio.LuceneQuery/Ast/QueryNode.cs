namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Base class for all AST nodes in the Lucene query tree.
/// </summary>
public abstract class QueryNode
{
    /// <summary>
    /// The start position in the source text (0-based).
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// The end position in the source text (exclusive).
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// The line number where this node starts (1-based).
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// The column number where this node starts (1-based).
    /// </summary>
    public int StartColumn { get; set; }
}
