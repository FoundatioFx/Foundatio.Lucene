using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Foundatio.LuceneQuery.Ast;
using Foundatio.LuceneQuery.Visitors;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.LuceneQuery.EntityFramework;

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
    private static readonly PropertyInfo EfFunctionsProperty = typeof(EF).GetProperty(nameof(EF.Functions))!;

    /// <summary>
    /// Builds a predicate expression from a query node.
    /// </summary>
    public Expression<Func<T, bool>> BuildExpression<T>(QueryNode node, IEntityFrameworkQueryVisitorContext context, EntityFrameworkQueryParserConfiguration? configuration = null)
    {
        var body = BuildExpressionBody(typeof(T), node, context, configuration);
        return Expression.Lambda<Func<T, bool>>(body, _parameter);
    }

    /// <summary>
    /// Builds a predicate expression from a query node with a specific entity type.
    /// </summary>
    public LambdaExpression BuildExpression(Type entityType, QueryNode node, IEntityFrameworkQueryVisitorContext context, EntityFrameworkQueryParserConfiguration? configuration = null)
    {
        var body = BuildExpressionBody(entityType, node, context, configuration);
        var delegateType = typeof(Func<,>).MakeGenericType(entityType, typeof(bool));
        return Expression.Lambda(delegateType, body, _parameter);
    }

    private Expression BuildExpressionBody(Type entityType, QueryNode node, IEntityFrameworkQueryVisitorContext context, EntityFrameworkQueryParserConfiguration? configuration)
    {
        _entityType = entityType;
        _parameter = Expression.Parameter(_entityType, "e");
        _efContext = context;
        _configuration = configuration;
        _expressionStack.Clear();
        _currentField = null;

        AcceptAsync(node, context).GetAwaiter().GetResult();

        return _expressionStack.Count > 0 ? _expressionStack.Pop() : Expression.Constant(true);
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

        var expr = BuildExpressionForFieldOrDefaults(field, f => BuildTermExpression(f, term, node.IsPrefix, node.IsWildcard));
        _expressionStack.Push(expr);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
    {
        var field = _currentField;
        var phrase = node.Phrase;

        var expr = BuildExpressionForFieldOrDefaults(field, f => BuildPhraseExpression(f, phrase));
        _expressionStack.Push(expr);
        return Task.FromResult<QueryNode>(node);
    }

    private Expression BuildExpressionForFieldOrDefaults(string? field, Func<string, Expression?> buildExpression)
    {
        if (!string.IsNullOrEmpty(field))
            return buildExpression(field) ?? Expression.Constant(false);

        var defaultFields = _efContext.DefaultFields;
        if (defaultFields == null || defaultFields.Length == 0)
            return Expression.Constant(false);

        var fieldExpressions = defaultFields
            .Select(buildExpression)
            .Where(e => e != null)
            .Cast<Expression>()
            .ToList();

        if (fieldExpressions.Count == 0)
            return Expression.Constant(false);

        return fieldExpressions.Aggregate(Expression.OrElse);
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
            return BuildStringComparison(memberExpr, term, isPrefix, isWildcard, fieldInfo);
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
        return BuildStringComparison(memberExpr, term, isPrefix, isWildcard, fieldInfo);
    }

    private Expression BuildStringComparison(Expression memberExpr, string term, bool isPrefix, bool isWildcard, EntityFieldInfo? fieldInfo = null)
    {
        // Check if this field is full-text indexed
        if (fieldInfo?.IsFullTextIndexed == true || IsFullTextIndexedField(fieldInfo))
        {
            return BuildFullTextSearchExpression(memberExpr, term, isPrefix, isWildcard);
        }

        // Note: We don't add explicit null checks here because:
        // 1. SQL's LIKE with NULL returns NULL which evaluates to false in WHERE clauses
        // 2. This matches EF Core's behavior with direct LINQ Contains/StartsWith/EndsWith calls
        // 3. Adding null checks generates different SQL than equivalent LINQ expressions

        if (isWildcard && !isPrefix)
        {
            // Handle wildcards (* and ?)
            // Convert to LIKE pattern: * -> %, ? -> _
            return BuildWildcardExpression(memberExpr, term);
        }
        else if (isPrefix)
        {
            // StartsWith comparison - case sensitivity determined by database collation
            return Expression.Call(memberExpr, StringStartsWithMethod, Expression.Constant(term.TrimEnd('*')));
        }
        else
        {
            // Default: use Contains for better search experience - case sensitivity determined by database collation
            return Expression.Call(memberExpr, StringContainsMethod, Expression.Constant(term));
        }
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

    private bool IsFullTextIndexedField(EntityFieldInfo? fieldInfo)
    {
        if (fieldInfo == null || _configuration == null)
            return false;

        // Determine the entity type name from the field info
        // Priority order:
        // 1. DeclaringTypeName (set during field discovery for both EF and reflection)
        // 2. Property.DeclaringType (EF metadata)
        // 3. Fallback to root entity type
        string entityTypeName;
        if (!string.IsNullOrEmpty(fieldInfo.DeclaringTypeName))
        {
            entityTypeName = fieldInfo.DeclaringTypeName;
        }
        else if (fieldInfo.Property?.DeclaringType != null)
        {
            // Use the declaring type from EF metadata
            entityTypeName = fieldInfo.Property.DeclaringType.ClrType.Name;
        }
        else
        {
            // Fallback to the root entity type
            entityTypeName = _entityType.Name;
        }

        return _configuration.IsFullTextField(entityTypeName, fieldInfo.Name);
    }

    private Expression BuildFullTextSearchExpression(Expression memberExpr, string term, bool isPrefix, bool isWildcard)
    {
        // Build EF.Functions.Contains(property, searchTerm) for full-text search
        // Get EF.Functions instance
        var efFunctions = Expression.Property(null, EfFunctionsProperty);

        // Format the search term for SQL Server full-text search
        var searchTerm = FormatFullTextSearchTerm(term, isPrefix, isWildcard);
        var searchTermConstant = Expression.Constant(searchTerm, typeof(string));

        // Get the SqlServerDbFunctionsExtensions.Contains method
        // Method signature: Contains(DbFunctions, object propertyReference, string searchCondition)
        // Note: The 'object' parameter is used for the method signature, but EF Core's translator
        // needs to see the actual property expression to translate it correctly
        var sqlServerExtensionsType = Type.GetType("Microsoft.EntityFrameworkCore.SqlServerDbFunctionsExtensions, Microsoft.EntityFrameworkCore.SqlServer");
        if (sqlServerExtensionsType == null)
        {
            throw new InvalidOperationException(
                "Full-text search requires Microsoft.EntityFrameworkCore.SqlServer package. " +
                "Ensure it is installed and the field is properly configured.");
        }

        var containsMethod = sqlServerExtensionsType.GetMethod(
            "Contains",
            [typeof(DbFunctions), typeof(object), typeof(string)]);

        if (containsMethod == null)
        {
            throw new InvalidOperationException(
                "Could not find EF.Functions.Contains method. " +
                "Ensure Microsoft.EntityFrameworkCore.SqlServer package is installed.");
        }

        // Pass the member expression directly - EF Core's translator will handle the object conversion
        return Expression.Call(null, containsMethod, efFunctions, memberExpr, searchTermConstant);
    }

    private static string FormatFullTextSearchTerm(string term, bool isPrefix, bool isWildcard)
    {
        // Format the term for SQL Server full-text CONTAINS syntax
        // See: https://docs.microsoft.com/en-us/sql/relational-databases/search/query-with-full-text-search

        var cleanTerm = term.Trim('*', '?');

        if (isWildcard && !isPrefix)
        {
            // Handle wildcards
            var startsWithWildcard = term.StartsWith('*') || term.StartsWith('?');
            var endsWithWildcard = term.EndsWith('*') || term.EndsWith('?');

            if (endsWithWildcard && !startsWithWildcard)
            {
                // Prefix search: "term*" -> "\"term*\""
                return $"\"{cleanTerm}*\"";
            }
            // Full-text search doesn't support suffix wildcards well, use the term as-is
            return $"\"{cleanTerm}\"";
        }
        else if (isPrefix)
        {
            // Prefix search: "term*" -> "\"term*\""
            return $"\"{cleanTerm}*\"";
        }
        else
        {
            // Regular term - quote it for exact word matching
            return $"\"{cleanTerm}\"";
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

        // Check if this field is full-text indexed
        if (fieldInfo?.IsFullTextIndexed == true || IsFullTextIndexedField(fieldInfo))
        {
            return BuildFullTextSearchExpression(memberExpr, phrase, isPrefix: false, isWildcard: false);
        }

        // String contains for phrase - case sensitivity determined by database collation
        var nullCheck = Expression.NotEqual(memberExpr, Expression.Constant(null, memberExpr.Type));
        var contains = Expression.Call(memberExpr, StringContainsMethod, Expression.Constant(phrase));
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

        return BuildRangeComparisonExpression(memberExpr, node);
    }

    private Expression? BuildRangeComparisonExpression(Expression memberExpr, RangeNode node)
    {
        var fieldType = memberExpr.Type;
        var underlyingType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        // Handle short-form operators (>, >=, <, <=)
        if (node.Operator.HasValue)
        {
            var value = node.Min ?? node.Max;
            if (value == null) return null;

            // For < and <= operators, use end-of-day for date-only values
            var isMaxBound = node.Operator.Value is RangeOperator.LessThan or RangeOperator.LessThanOrEqual;
            var constant = ConvertToConstant(value, fieldType, underlyingType, isRangeMax: isMaxBound);
            if (constant == null) return null;

            return node.Operator.Value switch
            {
                RangeOperator.GreaterThan => Expression.GreaterThan(memberExpr, constant),
                RangeOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(memberExpr, constant),
                RangeOperator.LessThan => Expression.LessThan(memberExpr, constant),
                RangeOperator.LessThanOrEqual => Expression.LessThanOrEqual(memberExpr, constant),
                _ => null
            };
        }

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
            return Expression.AndAlso(minExpr, maxExpr);

        return minExpr ?? maxExpr;
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
        var traversal = TraverseCollectionPath(fieldPath);
        if (traversal == null)
            return null;

        var (collectionExpr, elementType, innerExpr, innerParam) = traversal.Value;

        // Get field info for full-text check
        EntityFieldInfo? fieldInfo = null;
        if (_efContext is EntityFrameworkQueryVisitorContext efContext)
        {
            fieldInfo = efContext.GetField(fieldPath);
        }

        // Build the comparison for the inner property
        Expression? comparison;
        if (innerExpr.Type == typeof(string))
        {
            comparison = BuildCollectionStringComparison(innerExpr, term, isPrefix, isWildcard, fieldInfo);
        }
        else
        {
            var underlyingType = Nullable.GetUnderlyingType(innerExpr.Type) ?? innerExpr.Type;
            var constant = ConvertToConstant(term, innerExpr.Type, underlyingType);
            if (constant == null)
                return null;
            comparison = Expression.Equal(innerExpr, constant);
        }

        return BuildAnyExpression(collectionExpr, elementType, comparison, innerParam);
    }

    private Expression BuildCollectionStringComparison(Expression innerExpr, string term, bool isPrefix, bool isWildcard, EntityFieldInfo? fieldInfo)
    {
        // Check if this field is full-text indexed
        if (fieldInfo?.IsFullTextIndexed == true || IsFullTextIndexedField(fieldInfo))
            return BuildFullTextSearchExpression(innerExpr, term, isPrefix, isWildcard);

        // Note: We don't add explicit null checks to match EF Core's LINQ behavior
        if (isWildcard && !isPrefix)
            return BuildWildcardExpression(innerExpr, term);

        if (isPrefix)
            return Expression.Call(innerExpr, StringStartsWithMethod, Expression.Constant(term.TrimEnd('*')));

        return Expression.Call(innerExpr, StringContainsMethod, Expression.Constant(term));
    }

    private (Expression CollectionExpr, Type ElementType, Expression InnerExpr, ParameterExpression InnerParam)? TraverseCollectionPath(string fieldPath)
    {
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

        // Navigate remaining path within the collection element
        for (int i = collectionIndex + 1; i < parts.Length; i++)
        {
            var part = parts[i];
            var propertyInfo = innerExpr.Type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
                return null;
            innerExpr = Expression.Property(innerExpr, propertyInfo);
        }

        return (current, elementType, innerExpr, innerParam);
    }

    private static Expression BuildAnyExpression(Expression collectionExpr, Type elementType, Expression comparison, ParameterExpression innerParam)
    {
        var lambda = Expression.Lambda(comparison, innerParam);
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType);
        return Expression.Call(anyMethod, collectionExpr, lambda);
    }

    private Expression? BuildCollectionRangeExpression(string fieldPath, RangeNode node)
    {
        var traversal = TraverseCollectionPath(fieldPath);
        if (traversal == null)
            return null;

        var (collectionExpr, elementType, innerExpr, innerParam) = traversal.Value;

        var comparison = BuildRangeComparisonExpression(innerExpr, node);
        if (comparison == null)
            return null;

        return BuildAnyExpression(collectionExpr, elementType, comparison, innerParam);
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

    private static bool IsCollectionType(Type type) => EntityFrameworkQueryParser.IsCollectionType(type);

    private static Type? GetCollectionElementType(Type collectionType) => EntityFrameworkQueryParser.GetCollectionElementType(collectionType);

    private ConstantExpression? ConvertToConstant(string value, Type targetType, Type underlyingType, bool isRangeMax = false)
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

        return converted == null ? null : Expression.Constant(converted, targetType);
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

    private static bool IsNumericType(Type type) => EntityFrameworkQueryParser.IsNumericType(type);

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
