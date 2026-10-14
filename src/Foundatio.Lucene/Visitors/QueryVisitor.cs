using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Generic abstract base class for query visitors with a typed context.
/// </summary>
/// <typeparam name="TContext">The type of visitor context.</typeparam>
public abstract class QueryVisitor<TContext> : IQueryVisitor<TContext>
    where TContext : IQueryVisitorContext
{
    /// <summary>
    /// Entry point for accepting a node. Dispatches to the appropriate typed Visit method.
    /// </summary>
    public virtual QueryNode Accept(QueryNode node, TContext context)
    {
        return node switch
        {
            QueryDocument doc => Visit(doc, context),
            GroupNode group => Visit(group, context),
            BooleanQueryNode boolQuery => Visit(boolQuery, context),
            FieldQueryNode fieldQuery => Visit(fieldQuery, context),
            TermNode term => Visit(term, context),
            PhraseNode phrase => Visit(phrase, context),
            RegexNode regex => Visit(regex, context),
            RangeNode range => Visit(range, context),
            NotNode not => Visit(not, context),
            ExistsNode exists => Visit(exists, context),
            MissingNode missing => Visit(missing, context),
            MatchAllNode matchAll => Visit(matchAll, context),
            _ => node
        };
    }

    /// <summary>
    /// Visits a QueryDocument node.
    /// </summary>
    protected virtual QueryNode Visit(QueryDocument node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a GroupNode.
    /// </summary>
    protected virtual QueryNode Visit(GroupNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a BooleanQueryNode.
    /// </summary>
    protected virtual QueryNode Visit(BooleanQueryNode node, TContext context)
    {
        foreach (var clause in node.Clauses)
        {
            if (clause.Query is not null)
                clause.Query = Accept(clause.Query, context);
        }
        return node;
    }

    /// <summary>
    /// Visits a FieldQueryNode.
    /// </summary>
    protected virtual QueryNode Visit(FieldQueryNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a TermNode.
    /// </summary>
    protected virtual QueryNode Visit(TermNode node, TContext context) => node;

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    protected virtual QueryNode Visit(PhraseNode node, TContext context) => node;

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    protected virtual QueryNode Visit(RegexNode node, TContext context) => node;

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    protected virtual QueryNode Visit(RangeNode node, TContext context) => node;

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    protected virtual QueryNode Visit(NotNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    protected virtual QueryNode Visit(ExistsNode node, TContext context) => node;

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    protected virtual QueryNode Visit(MissingNode node, TContext context) => node;

    /// <summary>
    /// Visits a MatchAllNode.
    /// </summary>
    protected virtual QueryNode Visit(MatchAllNode node, TContext context) => node;

}

/// <summary>
/// Non-generic abstract base class for query visitors that work with any context.
/// </summary>
public abstract class QueryVisitor : QueryVisitor<IQueryVisitorContext>, IQueryVisitor;
