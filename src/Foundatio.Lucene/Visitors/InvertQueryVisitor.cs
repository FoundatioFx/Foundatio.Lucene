using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Inverts a query so it matches the documents the original did not, while keeping clauses on
/// <see cref="NonInvertedFields"/> (typically scope filters such as a tenant or project id) as they are.
/// </summary>
/// <remarks>
/// The top-level AND clauses that only use non-inverted fields are kept, and the remaining clauses are negated as a
/// single unit, so the result is the exact complement of the query within the scope:
/// <c>organization:1 status:open type:error</c> becomes <c>organization:1 (NOT (status:open type:error))</c>.
/// When <see cref="AlternateInvertedCriteria"/> is set, the negated part becomes <c>(alternate OR (NOT ...))</c>.
/// Queries parsed with OR as the default operator cannot be inverted, because their juxtaposed clauses are
/// alternatives rather than scope filters.
/// </remarks>
public class InvertQueryVisitor : IQueryVisitor
{
    /// <summary>
    /// Creates the visitor.
    /// </summary>
    /// <param name="nonInvertedFields">Fields whose clauses are kept as they are.</param>
    /// <param name="alternateInvertedCriteria">A query OR-ed with the inverted part, for example <c>is_deleted:true</c>.</param>
    public InvertQueryVisitor(IEnumerable<string>? nonInvertedFields = null, QueryNode? alternateInvertedCriteria = null)
    {
        if (nonInvertedFields is not null)
        {
            foreach (string field in nonInvertedFields)
                NonInvertedFields.Add(field);
        }

        AlternateInvertedCriteria = alternateInvertedCriteria is QueryDocument document ? document.Query : alternateInvertedCriteria;
    }

    /// <summary>
    /// Fields whose clauses are kept as they are.
    /// </summary>
    public ISet<string> NonInvertedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A query OR-ed with the inverted part.
    /// </summary>
    public QueryNode? AlternateInvertedCriteria { get; }

    /// <summary>
    /// Inverts a query. The input is not modified.
    /// </summary>
    /// <exception cref="ArgumentException">The context's default operator is OR.</exception>
    public QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ParserOptions.DefaultOperator == BooleanOperator.Or)
            throw new ArgumentException("Queries using OR as the default operator can not be inverted.", nameof(context));

        if (node is QueryDocument document)
        {
            var inverted = document.CloneDocument();
            if (inverted.Query is not null)
                inverted.Query = Invert(inverted.Query);
            return inverted;
        }

        return Invert(node.Clone());
    }

    private QueryNode Invert(QueryNode node)
    {
        if (IsScopeOnly(node))
            return node;

        var conjuncts = new List<BooleanClause>();
        if (!TryFlattenConjunction(node, conjuncts))
            return Negate(node);

        int firstInverted = -1;
        var kept = new List<BooleanClause>();
        var inverted = new List<BooleanClause>();
        foreach (var clause in conjuncts)
        {
            if (IsScopeOnly(clause.Query!))
            {
                kept.Add(clause);
            }
            else
            {
                if (firstInverted < 0)
                    firstInverted = kept.Count;
                inverted.Add(clause);
            }
        }

        if (inverted.Count == 0)
            return node;

        QueryNode rest = inverted.Count == 1 && inverted[0].Occur == Occur.Must
            ? inverted[0].Query!
            : new BooleanQueryNode { Clauses = inverted.Select(c => new BooleanClause(c.Query, c.Occur) { Modifier = c.Modifier }).ToList() };

        var negated = Negate(rest);
        if (kept.Count == 0)
            return negated;

        var result = new BooleanQueryNode();
        result.Clauses.AddRange(kept.Take(firstInverted).Select(Copy));
        result.Clauses.Add(new BooleanClause(negated, Occur.Must));
        result.Clauses.AddRange(kept.Skip(firstInverted).Select(Copy));
        return result;
    }

    private static BooleanClause Copy(BooleanClause clause) => new(clause.Query, clause.Occur) { Modifier = clause.Modifier };

    /// <summary>
    /// Collects the AND-ed clauses of a node, flattening nested AND levels and unboosted groups.
    /// </summary>
    private bool TryFlattenConjunction(QueryNode node, List<BooleanClause> conjuncts)
    {
        switch (node)
        {
            case GroupNode { BoostText: null, Query: { } inner }:
                return TryFlattenConjunction(inner, conjuncts);
            case BooleanQueryNode boolean when boolean.Clauses.Count > 1 && boolean.Clauses.All(c => c.Occur != Occur.Should && c.Query is not null):
                foreach (var clause in boolean.Clauses)
                {
                    var nested = new List<BooleanClause>();
                    if (clause.Occur == Occur.Must && !IsScopeOnly(clause.Query!) && TryFlattenConjunction(clause.Query!, nested))
                        conjuncts.AddRange(nested);
                    else
                        conjuncts.Add(clause);
                }
                return true;
            default:
                return false;
        }
    }

    private QueryNode Negate(QueryNode node)
    {
        QueryNode negated = node switch
        {
            BooleanQueryNode { Clauses: [{ Occur: Occur.MustNot, Query: { } inner }] } => inner,
            GroupNode { BoostText: null, Query: BooleanQueryNode { Clauses: [{ Occur: Occur.MustNot, Query: { } inner }] } } => inner,
            NotNode { Query: { } inner } => inner,
            _ => new GroupNode { Query = new BooleanQueryNode { Clauses = [new BooleanClause(node, Occur.MustNot) { Modifier = ClauseModifier.Not }] } }
        };

        if (AlternateInvertedCriteria is null)
            return negated;

        return new GroupNode
        {
            Query = new BooleanQueryNode
            {
                Clauses =
                [
                    new BooleanClause(AlternateInvertedCriteria.Clone(), Occur.Should),
                    new BooleanClause(negated, Occur.Should) { Operator = BooleanOperator.Or }
                ]
            }
        };
    }

    private bool IsScopeOnly(QueryNode node)
    {
        if (NonInvertedFields.Count == 0 || HasFieldlessLeaf(node, insideField: false))
            return false;

        var fields = node.GetReferencedFields();
        return fields.Count > 0 && fields.All(NonInvertedFields.Contains);
    }

    private static bool HasFieldlessLeaf(QueryNode? node, bool insideField)
    {
        return node switch
        {
            null => false,
            QueryDocument document => HasFieldlessLeaf(document.Query, insideField),
            GroupNode group => HasFieldlessLeaf(group.Query, insideField),
            NotNode not => HasFieldlessLeaf(not.Query, insideField),
            BooleanQueryNode boolean => boolean.Clauses.Any(c => HasFieldlessLeaf(c.Query, insideField)),
            FieldQueryNode field => HasFieldlessLeaf(field.Query, insideField: true),
            IFieldNode => false,
            _ => !insideField
        };
    }

    /// <summary>
    /// Inverts a query and returns the result as query text.
    /// </summary>
    public static string Run(string query, IEnumerable<string>? nonInvertedFields = null, string? alternateInvertedCriteria = null, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= LuceneParserOptions.Default;

        var document = LuceneQuery.Parse(query, options).GetDocumentOrThrow();
        var alternate = alternateInvertedCriteria is null ? null : LuceneQuery.Parse(alternateInvertedCriteria, options).GetDocumentOrThrow();
        var visitor = new InvertQueryVisitor(nonInvertedFields, alternate);
        var inverted = visitor.Accept(document, new QueryVisitorContext { ParserOptions = options });
        return QueryStringBuilder.ToQueryString(inverted, options.DefaultOperator);
    }
}
