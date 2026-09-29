using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Supplies values to expression trees through a field of a holder object, which EF Core turns into a SQL parameter
/// (so query plans are reused) instead of inlining a literal.
/// </summary>
internal static class QueryParameter
{
    private static readonly ConcurrentDictionary<Type, Func<object?, Expression>> Factories = new();

    private static readonly MethodInfo CreateTypedMethod =
        typeof(QueryParameter).GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Expression Create(object? value, Type type)
    {
        var factory = Factories.GetOrAdd(type, static t => CreateTypedMethod.MakeGenericMethod(t).CreateDelegate<Func<object?, Expression>>());
        return factory(value);
    }

    private static Expression CreateTyped<T>(object? value)
    {
        return Expression.Field(Expression.Constant(new Holder<T>((T)value!)), Holder<T>.ValueField);
    }

    private sealed class Holder<T>(T value)
    {
        public static readonly FieldInfo ValueField = typeof(Holder<T>).GetField(nameof(Value))!;

        public readonly T Value = value;
    }
}

/// <summary>
/// Cached reflection and small expression-building helpers.
/// </summary>
internal static class ExpressionHelpers
{
    public static readonly ConstantExpression True = Expression.Constant(true);
    public static readonly ConstantExpression False = Expression.Constant(false);
    public static readonly ConstantExpression LikeEscapeCharacter = Expression.Constant(LikePattern.EscapeCharacter.ToString());

    public static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    public static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    public static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo EnumerableAny = GetEnumerableAny(parameters: 1);
    private static readonly MethodInfo EnumerableAnyWithPredicate = GetEnumerableAny(parameters: 2);
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;
    private static readonly MemberExpression EfFunctions = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);

    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly Lazy<MethodInfo?> FullTextContainsMethod = new(() => Type
        .GetType("Microsoft.EntityFrameworkCore.SqlServerDbFunctionsExtensions, Microsoft.EntityFrameworkCore.SqlServer")
        ?.GetMethod("Contains", [typeof(DbFunctions), typeof(object), typeof(string)]));

    private static readonly ConcurrentDictionary<Type, MethodInfo> AnyMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> AnyWithPredicateMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> EfPropertyMethods = new();

    private static MethodInfo GetEnumerableAny(int parameters)
    {
        return typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == parameters);
    }

    public static bool IsTrue(Expression expression) => expression is ConstantExpression { Value: true };

    public static bool IsFalse(Expression expression) => expression is ConstantExpression { Value: false };

    public static Expression AndAlso(Expression? left, Expression right)
    {
        if (left is null || IsTrue(left))
            return right;
        if (IsTrue(right))
            return left;
        if (IsFalse(left) || IsFalse(right))
            return False;

        return Expression.AndAlso(left, right);
    }

    public static Expression OrElse(Expression? left, Expression right)
    {
        if (left is null || IsFalse(left))
            return right;
        if (IsFalse(right))
            return left;
        if (IsTrue(left) || IsTrue(right))
            return True;

        return Expression.OrElse(left, right);
    }

    public static Expression Not(Expression expression)
    {
        if (IsTrue(expression))
            return False;
        if (IsFalse(expression))
            return True;
        if (expression is UnaryExpression { NodeType: ExpressionType.Not } not)
            return not.Operand;

        return Expression.Not(NullSafeComparisons.Instance.Visit(expression));
    }

    public static Expression Any(Expression collection, LambdaExpression predicate)
    {
        var elementType = predicate.Parameters[0].Type;
        if (IsTrue(predicate.Body))
            return Any(collection, elementType);

        var method = AnyWithPredicateMethods.GetOrAdd(elementType, static t => EnumerableAnyWithPredicate.MakeGenericMethod(t));
        return Expression.Call(method, collection, predicate);
    }

    public static Expression Any(Expression collection, Type elementType)
    {
        var method = AnyMethods.GetOrAdd(elementType, static t => EnumerableAny.MakeGenericMethod(t));
        return Expression.Call(method, collection);
    }

    public static Expression Like(Expression member, string pattern)
    {
        return Expression.Call(LikeMethod, EfFunctions, member, QueryParameter.Create(pattern, typeof(string)), LikeEscapeCharacter);
    }

    public static Expression? FullTextContains(Expression member, string searchCondition)
    {
        var method = FullTextContainsMethod.Value;
        return method is null ? null : Expression.Call(method, EfFunctions, member, QueryParameter.Create(searchCondition, typeof(string)));
    }

    /// <summary>
    /// Accesses a model member. Shadow and indexer properties are read with <c>EF.Property</c>.
    /// </summary>
    public static Expression Access(Expression instance, EntityFieldInfo field)
    {
        var metadata = field.Metadata ?? throw new InvalidOperationException($"Field {field.FullName} has no model member.");
        if (metadata.IsShadowProperty() || metadata.IsIndexerProperty() || (metadata.PropertyInfo is null && metadata.FieldInfo is null))
        {
            var method = EfPropertyMethods.GetOrAdd(metadata.ClrType, static t => EfProperty.MakeGenericMethod(t));
            return Expression.Call(method, instance, Expression.Constant(metadata.Name));
        }

        return metadata.PropertyInfo is { } property
            ? Expression.Property(instance, property)
            : Expression.Field(instance, metadata.FieldInfo!);
    }

    /// <summary>
    /// Replaces the parameter of <paramref name="lambda"/> with <paramref name="instance"/>.
    /// </summary>
    public static Expression Inline(LambdaExpression lambda, Expression instance)
    {
        return new ParameterReplacer(lambda.Parameters[0], instance).Visit(lambda.Body);
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
    }

    /// <summary>
    /// Guards relational comparisons with a null check before they are negated. SQL evaluates <c>NULL &gt; 5</c> as
    /// unknown, and <c>NOT</c> of unknown is still unknown, so <c>NOT (x &gt; 5)</c> would drop rows where <c>x</c> is
    /// null (EF Core 8 doesn't compensate for this). With the guard the comparison is false for those rows, so
    /// negating it includes them, as in Lucene and C#. Lambdas are skipped: <c>EXISTS</c> is never unknown.
    /// </summary>
    private sealed class NullSafeComparisons : ExpressionVisitor
    {
        public static readonly NullSafeComparisons Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual))
                return base.VisitBinary(node);

            Expression result = node;
            foreach (var operand in (ReadOnlySpan<Expression>)[node.Right, node.Left])
            {
                if (GetNullableSource(operand) is { } source)
                    result = Expression.AndAlso(Expression.NotEqual(source, Expression.Constant(null, source.Type)), result);
            }

            return result;
        }

        protected override Expression VisitLambda<T>(Expression<T> node) => node;

        private static Expression? GetNullableSource(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                expression = convert.Operand;

            if (expression is MethodCallExpression compare && compare.Method == StringCompare)
                expression = compare.Arguments[0];

            var instance = expression switch
            {
                MemberExpression member => member.Expression,
                MethodCallExpression { Method.IsGenericMethod: true } call when call.Method.GetGenericMethodDefinition() == EfProperty => call.Arguments[0],
                _ => null
            };

            if (instance is null or ConstantExpression)
                return null;

            if (!expression.Type.IsValueType || Nullable.GetUnderlyingType(expression.Type) is not null)
                return expression;

            return instance is ParameterExpression || instance.Type.IsValueType ? null : instance;
        }
    }
}
