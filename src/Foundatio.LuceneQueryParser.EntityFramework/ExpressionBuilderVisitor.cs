using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

namespace Foundatio.LuceneQueryParser.EntityFramework;

/// <summary>
/// Visitor that converts Lucene AST nodes into LINQ Expression trees.
/// </summary>
public class ExpressionBuilderVisitor : QueryNodeVisitor
{
    private ParameterExpression _parameter = null!;
    private Type _entityType = null!;
    private string? _currentField;
    private readonly Stack<Expression> _expressionStack = new();
    private IEntityFrameworkQueryVisitorContext _efContext = null!;
    private EntityFrameworkQueryParserConfiguration? _configuration;

    private static readonly MethodInfo StringContainsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StringEndsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo StringEqualsMethod = typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(StringComparison)])!;
    private static readonly MethodInfo RegexIsMatchMethod = typeof(Regex).GetMethod(nameof(Regex.IsMatch), [typeof(string), typeof(string)])!;

    /// <summary>
    /// Builds a predicate expression from a query node.
    /// </summary>
    public Expression<Func<T, bool>> BuildExpression<T>(QueryNode node, IEntityFrameworkQueryVisitorContext context, EntityFrameworkQueryParserConfiguration? configuration = null)
    {
        _entityType = typeof(T);
        _parameter = Expression.Parameter(_entityType, "e");
        _efContext = context;
        _configuration = configuration;
        _expressionStack.Clear();
        _currentField = null;

        // Visit the node tree
        AcceptAsync(node, context).GetAwaiter().GetResult();

        // Get result from stack
        var body = _expressionStack.Count > 0 ? _expressionStack.Pop() : Expression.Constant(true);

        return Expression.Lambda<Func<T, bool>>(body, _parameter);
    }

    /// <summary>
    /// Builds a predicate expression from a query node with a specific entity type.
    /// </summary>
    public LambdaExpression BuildExpression(Type entityType, QueryNode node, IEntityFrameworkQueryVisitorContext context, EntityFrameworkQueryParserConfiguration? configuration = null)
    {
        _entityType = entityType;
        _parameter = Expression.Parameter(_entityType, "e");
        _efContext = context;
        _configuration = configuration;
        _expressionStack.Clear();
        _currentField = null;

        // Visit the node tree
        AcceptAsync(node, context).GetAwaiter().GetResult();

        // Get result from stack
        var body = _expressionStack.Count > 0 ? _expressionStack.Pop() : Expression.Constant(true);

        var delegateType = typeof(Func<,>).MakeGenericType(entityType, typeof(bool));
        return Expression.Lambda(delegateType, body, _parameter);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
    {
        if (node.Query != null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
        }
        else
        {
            _expressionStack.Push(Expression.Constant(true));
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context)
    {
        if (node.Query != null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(BooleanQueryNode node, IQueryVisitorContext context)
    {
        if (node.Clauses.Count == 0)
        {
            _expressionStack.Push(Expression.Constant(true));
            return Task.FromResult<QueryNode>(node);
        }

        Expression? result = null;

        foreach (var clause in node.Clauses)
        {
            if (clause.Query == null)
                continue;

            // Determine the effective Occur from the clause
            // If the clause query is a BooleanQueryNode with a single Must/MustNot clause (from +/- prefix),
            // use that inner occur instead, but don't apply negation since the inner visit already did
            var effectiveOccur = clause.Occur;
            var isInnerBooleanWithOccur = false;
            if (clause.Query is BooleanQueryNode innerBoolean && 
                innerBoolean.Clauses.Count == 1 && 
                innerBoolean.Clauses[0].Occur != Occur.Should)
            {
                effectiveOccur = innerBoolean.Clauses[0].Occur;
                isInnerBooleanWithOccur = true;
            }

            AcceptAsync(clause.Query, context).GetAwaiter().GetResult();
            
            if (_expressionStack.Count == 0)
                continue;

            var clauseExpr = _expressionStack.Pop();

            // Handle MUST_NOT (negate the expression) - but only if not already handled by inner BooleanQueryNode
            if (clause.Occur == Occur.MustNot && !isInnerBooleanWithOccur)
            {
                clauseExpr = Expression.Not(clauseExpr);
            }

            if (result == null)
            {
                result = clauseExpr;
            }
            else
            {
                // Determine how to combine based on operator and effective occur
                result = clause.Operator switch
                {
                    BooleanOperator.And => Expression.AndAlso(result, clauseExpr),
                    BooleanOperator.Or => Expression.OrElse(result, clauseExpr),
                    // For implicit operator, use AND for Must/MustNot clauses, OR otherwise
                    _ => effectiveOccur != Occur.Should ? Expression.AndAlso(result, clauseExpr) : Expression.OrElse(result, clauseExpr)
                };
            }
        }

        _expressionStack.Push(result ?? Expression.Constant(true));
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        var previousField = _currentField;
        _currentField = node.Field;

        if (node.Query != null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
        }

        _currentField = previousField;
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        var field = _currentField;
        var term = node.UnescapedTerm;

        if (string.IsNullOrEmpty(field))
        {
            // Search default fields
            var defaultFields = _efContext.DefaultFields;
            if (defaultFields != null && defaultFields.Length > 0)
            {
                var fieldExpressions = defaultFields
                    .Select(f => BuildTermExpression(f, term, node.IsPrefix, node.IsWildcard))
                    .Where(e => e != null)
                    .ToList();

                if (fieldExpressions.Count > 0)
                {
                    var combined = fieldExpressions.Cast<Expression>().Aggregate(Expression.OrElse);
                    _expressionStack.Push(combined);
                }
                else
                {
                    _expressionStack.Push(Expression.Constant(false));
                }
            }
            else
            {
                _expressionStack.Push(Expression.Constant(false));
            }
        }
        else
        {
            var expr = BuildTermExpression(field, term, node.IsPrefix, node.IsWildcard);
            _expressionStack.Push(expr ?? Expression.Constant(false));
        }

        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
    {
        var field = _currentField;
        var phrase = node.Phrase;

        if (string.IsNullOrEmpty(field))
        {
            var defaultFields = _efContext.DefaultFields;
            if (defaultFields != null && defaultFields.Length > 0)
            {
                var fieldExpressions = defaultFields
                    .Select(f => BuildPhraseExpression(f, phrase))
                    .Where(e => e != null)
                    .ToList();

                if (fieldExpressions.Count > 0)
                {
                    var combined = fieldExpressions.Cast<Expression>().Aggregate(Expression.OrElse);
                    _expressionStack.Push(combined);
                }
                else
                {
                    _expressionStack.Push(Expression.Constant(false));
                }
            }
            else
            {
                _expressionStack.Push(Expression.Constant(false));
            }
        }
        else
        {
            var expr = BuildPhraseExpression(field, phrase);
            _expressionStack.Push(expr ?? Expression.Constant(false));
        }

        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        var field = _currentField ?? node.Field;

        if (string.IsNullOrEmpty(field))
        {
            _expressionStack.Push(Expression.Constant(false));
            return Task.FromResult<QueryNode>(node);
        }

        var expr = BuildRangeExpression(field, node);
        _expressionStack.Push(expr ?? Expression.Constant(false));

        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context)
    {
        if (node.Query != null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
            if (_expressionStack.Count > 0)
            {
                var inner = _expressionStack.Pop();
                _expressionStack.Push(Expression.Not(inner));
            }
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context)
    {
        var field = node.Field;
        var expr = BuildExistsExpression(field);
        _expressionStack.Push(expr ?? Expression.Constant(false));
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context)
    {
        var field = node.Field;
        var expr = BuildExistsExpression(field);
        if (expr != null)
        {
            _expressionStack.Push(Expression.Not(expr));
        }
        else
        {
            _expressionStack.Push(Expression.Constant(false));
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(MatchAllNode node, IQueryVisitorContext context)
    {
        _expressionStack.Push(Expression.Constant(true));
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context)
    {
        var field = _currentField;

        if (string.IsNullOrEmpty(field))
        {
            _expressionStack.Push(Expression.Constant(false));
            return Task.FromResult<QueryNode>(node);
        }

        var expr = BuildRegexExpression(field, node.Pattern);
        _expressionStack.Push(expr ?? Expression.Constant(false));

        return Task.FromResult<QueryNode>(node);
    }

    private Expression? BuildTermExpression(string fieldPath, string term, bool isPrefix, bool isWildcard)
    {
        // Check for custom field expression builder first
        var customExpr = TryBuildCustomFieldExpression(fieldPath, term, isPrefix, isWildcard, rangeNode: null);
        if (customExpr != null)
            return customExpr;

        // Check for collection navigation first (e.g., Employees.Name)
        if (HasCollectionInPath(fieldPath))
        {
            return BuildCollectionExpression(fieldPath, term, isPrefix, isWildcard);
        }

        var (memberExpr, fieldInfo) = GetMemberExpression(fieldPath);
        if (memberExpr == null)
            return null;

        var fieldType = memberExpr.Type;
        var underlyingType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        // Handle collection field (e.g., Companies where field is marked as collection)
        if (fieldInfo?.IsCollection == true)
        {
            return BuildCollectionExpression(fieldPath, term, isPrefix, isWildcard);
        }

        // Handle string fields
        if (underlyingType == typeof(string))
        {
            return BuildStringComparison(memberExpr, term, isPrefix, isWildcard);
        }

        // Handle numeric fields
        if (IsNumericType(underlyingType))
        {
            var value = ConvertToNumeric(term, underlyingType);
            if (value == null) return null;
            var constant = Expression.Constant(value, fieldType);
            return Expression.Equal(memberExpr, constant);
        }

        // Handle boolean fields
        if (underlyingType == typeof(bool))
        {
            if (bool.TryParse(term, out var boolValue))
            {
                var constant = Expression.Constant(boolValue, fieldType);
                return Expression.Equal(memberExpr, constant);
            }
            return null;
        }

        // Handle DateTime fields
        if (underlyingType == typeof(DateTime))
        {
            var dateValue = _efContext.DateTimeParser?.Invoke(term);
            if (dateValue == null) return null;
            var constant = Expression.Constant(dateValue, fieldType);
            return Expression.Equal(memberExpr, constant);
        }

        // Handle DateOnly fields
        if (underlyingType == typeof(DateOnly))
        {
            var dateValue = _efContext.DateOnlyParser?.Invoke(term);
            if (dateValue == null) return null;
            var constant = Expression.Constant(dateValue, fieldType);
            return Expression.Equal(memberExpr, constant);
        }

        // Handle Guid fields
        if (underlyingType == typeof(Guid))
        {
            if (Guid.TryParse(term, out var guidValue))
            {
                var constant = Expression.Constant(guidValue, fieldType);
                return Expression.Equal(memberExpr, constant);
            }
            return null;
        }

        // Handle enum fields
        if (underlyingType.IsEnum)
        {
            if (Enum.TryParse(underlyingType, term, ignoreCase: true, out var enumValue))
            {
                var constant = Expression.Constant(enumValue, fieldType);
                return Expression.Equal(memberExpr, constant);
            }
            return null;
        }

        // Default: try string comparison via ToString
        return BuildStringComparison(memberExpr, term, isPrefix, isWildcard);
    }

    private Expression BuildStringComparison(Expression memberExpr, string term, bool isPrefix, bool isWildcard)
    {
        // Ensure we're comparing non-null values
        var nullCheck = Expression.NotEqual(memberExpr, Expression.Constant(null, memberExpr.Type));

        Expression comparison;
        
        if (isWildcard && !isPrefix)
        {
            // Handle wildcards (* and ?)
            // Convert to LIKE pattern: * -> %, ? -> _
            comparison = BuildWildcardExpression(memberExpr, term);
        }
        else if (isPrefix)
        {
            // StartsWith comparison - use ToLower for case-insensitive
            var lowerMember = Expression.Call(memberExpr, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
            comparison = Expression.Call(lowerMember, StringStartsWithMethod, Expression.Constant(term.TrimEnd('*').ToLowerInvariant()));
        }
        else
        {
            // Default: use Contains for better search experience (case-insensitive)
            var lowerMember = Expression.Call(memberExpr, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
            comparison = Expression.Call(lowerMember, StringContainsMethod, Expression.Constant(term.ToLowerInvariant()));
        }

        return Expression.AndAlso(nullCheck, comparison);
    }

    private Expression BuildWildcardExpression(Expression memberExpr, string term)
    {
        // Simple wildcard handling - convert to Contains/StartsWith/EndsWith
        var startsWithWildcard = term.StartsWith('*') || term.StartsWith('?');
        var endsWithWildcard = term.EndsWith('*') || term.EndsWith('?');
        var cleanTerm = term.Trim('*', '?');

        if (startsWithWildcard && endsWithWildcard)
        {
            // Contains
            return Expression.Call(memberExpr, StringContainsMethod, Expression.Constant(cleanTerm));
        }
        else if (startsWithWildcard)
        {
            // EndsWith
            return Expression.Call(memberExpr, StringEndsWithMethod, Expression.Constant(cleanTerm));
        }
        else if (endsWithWildcard)
        {
            // StartsWith
            return Expression.Call(memberExpr, StringStartsWithMethod, Expression.Constant(cleanTerm));
        }
        else
        {
            // Contains (for internal wildcards)
            return Expression.Call(memberExpr, StringContainsMethod, Expression.Constant(cleanTerm));
        }
    }

    private Expression? TryBuildCustomFieldExpression(string fieldPath, string? value, bool isPrefix, bool isWildcard, RangeNode? rangeNode)
    {
        if (_configuration?.CustomFieldExpressionBuilder == null)
            return null;

        var fieldInfo = _efContext.GetField(fieldPath);
        if (fieldInfo == null)
            return null;

        // Only invoke custom builder if field has custom data
        if (fieldInfo.Data.Count == 0)
            return null;

        var context = new CustomFieldContext
        {
            Field = fieldInfo,
            Parameter = _parameter,
            Term = value,
            IsPrefix = isPrefix,
            IsWildcard = isWildcard,
            RangeNode = rangeNode,
            Context = _efContext
        };

        return _configuration.CustomFieldExpressionBuilder(context);
    }

    private Expression? BuildPhraseExpression(string fieldPath, string phrase)
    {
        // Check for custom field expression builder first
        var customExpr = TryBuildCustomFieldExpression(fieldPath, phrase, isPrefix: false, isWildcard: false, rangeNode: null);
        if (customExpr != null)
            return customExpr;

        // Check for collection navigation first
        if (HasCollectionInPath(fieldPath))
        {
            return BuildCollectionExpression(fieldPath, phrase, isPrefix: false, isWildcard: false);
        }

        var (memberExpr, fieldInfo) = GetMemberExpression(fieldPath);
        if (memberExpr == null)
            return null;

        // Handle collection field
        if (fieldInfo?.IsCollection == true)
        {
            return BuildCollectionExpression(fieldPath, phrase, isPrefix: false, isWildcard: false);
        }

        var fieldType = memberExpr.Type;
        var underlyingType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        // For non-string types (like DateTime), treat phrase as exact value match
        if (underlyingType != typeof(string))
        {
            // Use the same logic as term expression for non-string types
            return BuildTermExpression(fieldPath, phrase, isPrefix: false, isWildcard: false);
        }

        // String contains for phrase (case-insensitive)
        var nullCheck = Expression.NotEqual(memberExpr, Expression.Constant(null, memberExpr.Type));
        var lowerMember = Expression.Call(memberExpr, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
        var contains = Expression.Call(lowerMember, StringContainsMethod, Expression.Constant(phrase.ToLowerInvariant()));
        return Expression.AndAlso(nullCheck, contains);
    }

    private Expression? BuildRangeExpression(string fieldPath, RangeNode node)
    {
        // Check for custom field expression builder first
        var customExpr = TryBuildCustomFieldExpression(fieldPath, value: null, isPrefix: false, isWildcard: false, rangeNode: node);
        if (customExpr != null)
            return customExpr;

        // Check for collection navigation first
        if (HasCollectionInPath(fieldPath))
        {
            return BuildCollectionRangeExpression(fieldPath, node);
        }

        var (memberExpr, fieldInfo) = GetMemberExpression(fieldPath);
        if (memberExpr == null)
            return null;

        // Handle collection field
        if (fieldInfo?.IsCollection == true)
        {
            return BuildCollectionRangeExpression(fieldPath, node);
        }

        var fieldType = memberExpr.Type;
        var underlyingType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        Expression? result = null;

        // Handle short-form operators (>, >=, <, <=)
        if (node.Operator.HasValue)
        {
            var value = node.Min ?? node.Max;
            if (value == null) return null;

            // For < and <= operators, use end-of-day for date-only values
            var isMaxBound = node.Operator.Value is RangeOperator.LessThan or RangeOperator.LessThanOrEqual;
            var constant = ConvertToConstant(value, fieldType, underlyingType, isRangeMax: isMaxBound);
            if (constant == null) return null;

            result = node.Operator.Value switch
            {
                RangeOperator.GreaterThan => Expression.GreaterThan(memberExpr, constant),
                RangeOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(memberExpr, constant),
                RangeOperator.LessThan => Expression.LessThan(memberExpr, constant),
                RangeOperator.LessThanOrEqual => Expression.LessThanOrEqual(memberExpr, constant),
                _ => null
            };
        }
        else
        {
            // Handle range expression [min TO max]
            Expression? minExpr = null;
            Expression? maxExpr = null;

            if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
            {
                var minConstant = ConvertToConstant(node.Min, fieldType, underlyingType, isRangeMax: false);
                if (minConstant != null)
                {
                    minExpr = node.MinInclusive
                        ? Expression.GreaterThanOrEqual(memberExpr, minConstant)
                        : Expression.GreaterThan(memberExpr, minConstant);
                }
            }

            if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
            {
                var maxConstant = ConvertToConstant(node.Max, fieldType, underlyingType, isRangeMax: true);
                if (maxConstant != null)
                {
                    maxExpr = node.MaxInclusive
                        ? Expression.LessThanOrEqual(memberExpr, maxConstant)
                        : Expression.LessThan(memberExpr, maxConstant);
                }
            }

            if (minExpr != null && maxExpr != null)
            {
                result = Expression.AndAlso(minExpr, maxExpr);
            }
            else
            {
                result = minExpr ?? maxExpr;
            }
        }

        return result;
    }

    private Expression? BuildExistsExpression(string fieldPath)
    {
        var (memberExpr, _) = GetMemberExpression(fieldPath);
        if (memberExpr == null)
            return null;

        // For nullable types, check if not null
        if (!memberExpr.Type.IsValueType || Nullable.GetUnderlyingType(memberExpr.Type) != null)
        {
            return Expression.NotEqual(memberExpr, Expression.Constant(null, memberExpr.Type));
        }

        // For non-nullable value types, always true
        return Expression.Constant(true);
    }

    private Expression? BuildRegexExpression(string fieldPath, string pattern)
    {
        var (memberExpr, _) = GetMemberExpression(fieldPath);
        if (memberExpr == null || memberExpr.Type != typeof(string))
            return null;

        // Note: Regex.IsMatch may not be directly supported by EF Core
        // This will work for in-memory queries and some EF providers
        var nullCheck = Expression.NotEqual(memberExpr, Expression.Constant(null, memberExpr.Type));
        var isMatch = Expression.Call(RegexIsMatchMethod, memberExpr, Expression.Constant(pattern));
        return Expression.AndAlso(nullCheck, isMatch);
    }

    private Expression? BuildCollectionExpression(string fieldPath, string term, bool isPrefix, bool isWildcard)
    {
        // Split the path to find the collection
        var parts = fieldPath.Split('.');
        
        Expression current = _parameter;
        Type currentType = _entityType;
        int collectionIndex = -1;

        // Find the collection in the path
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var propertyInfo = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return null;

            current = Expression.Property(current, propertyInfo);
            currentType = propertyInfo.PropertyType;

            // Check if this is a collection
            if (IsCollectionType(currentType))
            {
                collectionIndex = i;
                break;
            }
        }

        if (collectionIndex < 0 || collectionIndex >= parts.Length - 1)
            return null;

        // Get the element type of the collection
        var elementType = GetCollectionElementType(currentType);
        if (elementType == null)
            return null;

        // Build the inner expression for .Any()
        var innerParam = Expression.Parameter(elementType, "inner");
        Expression innerExpr = innerParam;

        // Navigate remaining path within the collection element
        for (int i = collectionIndex + 1; i < parts.Length; i++)
        {
            var part = parts[i];
            var propertyInfo = innerExpr.Type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return null;
            innerExpr = Expression.Property(innerExpr, propertyInfo);
        }

        // Build the comparison for the inner property
        Expression comparison;
        if (innerExpr.Type == typeof(string))
        {
            var nullCheck = Expression.NotEqual(innerExpr, Expression.Constant(null, typeof(string)));
            Expression stringComparison;
            
            if (isWildcard && !isPrefix)
            {
                stringComparison = BuildWildcardExpression(innerExpr, term);
            }
            else if (isPrefix)
            {
                var lowerInner = Expression.Call(innerExpr, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
                stringComparison = Expression.Call(lowerInner, StringStartsWithMethod, Expression.Constant(term.TrimEnd('*').ToLowerInvariant()));
            }
            else
            {
                var lowerInner = Expression.Call(innerExpr, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
                stringComparison = Expression.Call(lowerInner, StringContainsMethod, Expression.Constant(term.ToLowerInvariant()));
            }
            comparison = Expression.AndAlso(nullCheck, stringComparison);
        }
        else
        {
            var underlyingType = Nullable.GetUnderlyingType(innerExpr.Type) ?? innerExpr.Type;
            var constant = ConvertToConstant(term, innerExpr.Type, underlyingType);
            if (constant == null)
                return null;
            comparison = Expression.Equal(innerExpr, constant);
        }

        // Build the lambda for Any
        var lambda = Expression.Lambda(comparison, innerParam);

        // Call Enumerable.Any<T>(collection, lambda)
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType);

        return Expression.Call(anyMethod, current, lambda);
    }

    private Expression? BuildCollectionRangeExpression(string fieldPath, RangeNode node)
    {
        // Similar to BuildCollectionExpression but for ranges
        var parts = fieldPath.Split('.');
        
        Expression current = _parameter;
        Type currentType = _entityType;
        int collectionIndex = -1;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var propertyInfo = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return null;

            current = Expression.Property(current, propertyInfo);
            currentType = propertyInfo.PropertyType;

            if (IsCollectionType(currentType))
            {
                collectionIndex = i;
                break;
            }
        }

        if (collectionIndex < 0 || collectionIndex >= parts.Length - 1)
            return null;

        var elementType = GetCollectionElementType(currentType);
        if (elementType == null)
            return null;

        var innerParam = Expression.Parameter(elementType, "inner");
        Expression innerExpr = innerParam;

        for (int i = collectionIndex + 1; i < parts.Length; i++)
        {
            var part = parts[i];
            var propertyInfo = innerExpr.Type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return null;
            innerExpr = Expression.Property(innerExpr, propertyInfo);
        }

        // Build range comparison
        var fieldType = innerExpr.Type;
        var underlyingType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        Expression? comparison = null;

        if (node.Operator.HasValue)
        {
            var value = node.Min ?? node.Max;
            if (value == null) return null;

            // For < and <= operators, use end-of-day for date-only values
            var isMaxBound = node.Operator.Value is RangeOperator.LessThan or RangeOperator.LessThanOrEqual;
            var constant = ConvertToConstant(value, fieldType, underlyingType, isRangeMax: isMaxBound);
            if (constant == null) return null;

            comparison = node.Operator.Value switch
            {
                RangeOperator.GreaterThan => Expression.GreaterThan(innerExpr, constant),
                RangeOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(innerExpr, constant),
                RangeOperator.LessThan => Expression.LessThan(innerExpr, constant),
                RangeOperator.LessThanOrEqual => Expression.LessThanOrEqual(innerExpr, constant),
                _ => null
            };
        }
        else
        {
            Expression? minExpr = null;
            Expression? maxExpr = null;

            if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
            {
                var minConstant = ConvertToConstant(node.Min, fieldType, underlyingType, isRangeMax: false);
                if (minConstant != null)
                {
                    minExpr = node.MinInclusive
                        ? Expression.GreaterThanOrEqual(innerExpr, minConstant)
                        : Expression.GreaterThan(innerExpr, minConstant);
                }
            }

            if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
            {
                var maxConstant = ConvertToConstant(node.Max, fieldType, underlyingType, isRangeMax: true);
                if (maxConstant != null)
                {
                    maxExpr = node.MaxInclusive
                        ? Expression.LessThanOrEqual(innerExpr, maxConstant)
                        : Expression.LessThan(innerExpr, maxConstant);
                }
            }

            if (minExpr != null && maxExpr != null)
            {
                comparison = Expression.AndAlso(minExpr, maxExpr);
            }
            else
            {
                comparison = minExpr ?? maxExpr;
            }
        }

        if (comparison == null)
            return null;

        var lambda = Expression.Lambda(comparison, innerParam);

        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType);

        return Expression.Call(anyMethod, current, lambda);
    }

    private (MemberExpression? Expression, EntityFieldInfo? FieldInfo) GetMemberExpression(string fieldPath)
    {
        // First check if we have field info for this path
        EntityFieldInfo? fieldInfo = null;
        if (_efContext is EntityFrameworkQueryVisitorContext efContext)
        {
            fieldInfo = efContext.GetField(fieldPath);
        }

        var parts = fieldPath.Split('.');
        Expression current = _parameter;

        foreach (var part in parts)
        {
            var propertyInfo = current.Type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return (null, fieldInfo);

            current = Expression.Property(current, propertyInfo);
        }

        return (current as MemberExpression, fieldInfo);
    }

    private bool HasCollectionInPath(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        Type currentType = _entityType;

        foreach (var part in parts)
        {
            var propertyInfo = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return false;

            if (IsCollectionType(propertyInfo.PropertyType))
                return true;

            currentType = propertyInfo.PropertyType;
        }

        return false;
    }

    private static bool IsCollectionType(Type type)
    {
        if (type == typeof(string))
            return false;

        return type.IsGenericType && 
               (type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                type.GetGenericTypeDefinition() == typeof(IList<>) ||
                type.GetGenericTypeDefinition() == typeof(List<>) ||
                type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)));
    }

    private static Type? GetCollectionElementType(Type collectionType)
    {
        if (collectionType.IsGenericType)
        {
            return collectionType.GetGenericArguments().FirstOrDefault();
        }

        var enumerableInterface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }

    private ConstantExpression? ConvertToConstant(string value, Type targetType, Type underlyingType)
    {
        return ConvertToConstant(value, targetType, underlyingType, isRangeMax: false);
    }

    private ConstantExpression? ConvertToConstant(string value, Type targetType, Type underlyingType, bool isRangeMax)
    {
        object? converted;

        if (IsNumericType(underlyingType))
        {
            converted = ConvertToNumeric(value, underlyingType);
        }
        else if (underlyingType == typeof(DateTime))
        {
            converted = _efContext.DateTimeParser?.Invoke(value);
            // For range max bounds with date-only values, use end of day (23:59:59)
            if (isRangeMax && converted is DateTime dt && !HasTimeComponent(value))
            {
                converted = dt.Date.AddDays(1).AddTicks(-1); // 23:59:59.9999999
            }
        }
        else if (underlyingType == typeof(DateOnly))
        {
            converted = _efContext.DateOnlyParser?.Invoke(value);
        }
        else if (underlyingType == typeof(bool))
        {
            converted = bool.TryParse(value, out var b) ? b : null;
        }
        else if (underlyingType == typeof(Guid))
        {
            converted = Guid.TryParse(value, out var g) ? g : null;
        }
        else if (underlyingType.IsEnum)
        {
            converted = Enum.TryParse(underlyingType, value, ignoreCase: true, out var e) ? e : null;
        }
        else
        {
            converted = value;
        }

        if (converted == null)
            return null;

        // Handle nullable types
        if (targetType != underlyingType && converted.GetType() == underlyingType)
        {
            return Expression.Constant(converted, targetType);
        }

        return Expression.Constant(converted, targetType);
    }

    /// <summary>
    /// Checks if a date string contains a time component.
    /// </summary>
    private static bool HasTimeComponent(string value)
    {
        // Check for common time indicators: T separator, colons for time, or "now"
        if (string.Equals(value, "now", StringComparison.OrdinalIgnoreCase))
            return true;

        // ISO8601 with time: 2019-06-01T10:30:00
        if (value.Contains('T', StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if there's a time portion (contains colon after date part)
        // Formats like "2019-06-01 10:30:00" or just has hour:minute
        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0)
            return true;

        return false;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(decimal) || type == typeof(double) ||
               type == typeof(float) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }

    private static object? ConvertToNumeric(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(int)) return int.TryParse(value, out var i) ? i : null;
            if (targetType == typeof(long)) return long.TryParse(value, out var l) ? l : null;
            if (targetType == typeof(short)) return short.TryParse(value, out var s) ? s : null;
            if (targetType == typeof(byte)) return byte.TryParse(value, out var b) ? b : null;
            if (targetType == typeof(decimal)) return decimal.TryParse(value, out var d) ? d : null;
            if (targetType == typeof(double)) return double.TryParse(value, out var db) ? db : null;
            if (targetType == typeof(float)) return float.TryParse(value, out var f) ? f : null;
            if (targetType == typeof(uint)) return uint.TryParse(value, out var ui) ? ui : null;
            if (targetType == typeof(ulong)) return ulong.TryParse(value, out var ul) ? ul : null;
            if (targetType == typeof(ushort)) return ushort.TryParse(value, out var us) ? us : null;
            if (targetType == typeof(sbyte)) return sbyte.TryParse(value, out var sb) ? sb : null;
        }
        catch
        {
            // Ignore conversion errors
        }
        return null;
    }
}
