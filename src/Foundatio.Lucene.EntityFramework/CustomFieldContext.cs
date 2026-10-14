using System.Linq.Expressions;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Builds the predicate for one field comparison, for example to query a dynamic field stored in an
/// entity-attribute-value table. Return null to use the default expression.
/// </summary>
public delegate Expression? CustomFieldExpressionBuilder(CustomFieldContext context);

/// <summary>
/// Describes a field comparison for a <see cref="CustomFieldExpressionBuilder"/>.
/// </summary>
public sealed class CustomFieldContext
{
    private readonly FilterExpressionBuilder _builder;
    private readonly SearchOperator? _searchOperator;

    internal CustomFieldContext(FilterExpressionBuilder builder, EntityFieldInfo field, QueryNode node, Expression instance, SearchOperator? searchOperator)
    {
        _builder = builder;
        _searchOperator = searchOperator;
        Field = field;
        Node = node;
        Instance = instance;
    }

    /// <summary>
    /// The field being compared.
    /// </summary>
    public EntityFieldInfo Field { get; }

    /// <summary>
    /// The query node being translated: a <see cref="TermNode"/>, <see cref="PhraseNode"/>, <see cref="RangeNode"/>,
    /// <see cref="RegexNode"/>, <see cref="ExistsNode"/>, or <see cref="MissingNode"/>. The returned predicate must
    /// implement its meaning; negation and boolean logic are applied by the caller.
    /// </summary>
    public QueryNode Node { get; }

    /// <summary>
    /// The object that declares the field: the queried entity, or the element of a collection the field is reached
    /// through.
    /// </summary>
    public Expression Instance { get; }

    /// <summary>
    /// The lambda parameter for the queried entity.
    /// </summary>
    public ParameterExpression Parameter => _builder.Root;

    /// <summary>
    /// The context of the query being built.
    /// </summary>
    public EntityFrameworkQueryVisitorContext Context => _builder.Context;

    /// <summary>
    /// The value of a term or phrase with escape sequences processed (for a prefix term, without the trailing
    /// <c>*</c>), or null for other nodes.
    /// </summary>
    public string? Term => Node switch
    {
        TermNode term => term.UnescapedTerm,
        PhraseNode phrase => phrase.Phrase,
        _ => null
    };

    /// <summary>
    /// Whether the node is a prefix term such as <c>jo*</c>.
    /// </summary>
    public bool IsPrefix => Node is TermNode { IsPrefix: true };

    /// <summary>
    /// Whether the node is a wildcard term such as <c>j?n*</c>.
    /// </summary>
    public bool IsWildcard => Node is TermNode { IsWildcard: true };

    /// <summary>
    /// The range being compared, or null when the node is not a range.
    /// </summary>
    public RangeNode? Range => Node as RangeNode;

    /// <summary>
    /// Builds the predicate the parser would build if <paramref name="member"/> were the field, using the member's
    /// type to convert values. Use it to apply the query to the column that holds a custom field's value.
    /// </summary>
    public Expression BuildDefault(Expression member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return _builder.BuildDefaultForMember(member, Field, Node, _searchOperator);
    }

    /// <summary>
    /// Creates an expression that supplies <paramref name="value"/> as a query parameter.
    /// </summary>
    public Expression Parameterize<T>(T value) => QueryParameter.Create(value, typeof(T));

    /// <summary>
    /// Creates an expression of type <paramref name="type"/> that supplies <paramref name="value"/> as a query parameter.
    /// </summary>
    public Expression Parameterize(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return QueryParameter.Create(value, type);
    }

    /// <summary>
    /// Creates <c>collection.Any(predicate)</c>.
    /// </summary>
    public Expression Any(Expression collection, LambdaExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(predicate);
        return ExpressionHelpers.Any(collection, predicate);
    }
}
