using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Translates a processed query document into a predicate over the queried entity. Problems are reported as
/// validation errors on the context (with the position of the offending node) instead of silently matching nothing;
/// the caller throws when any were found.
/// </summary>
internal sealed class FilterExpressionBuilder
{
    private readonly DateTimeOffset _now;
    private int _parameterCount;

    public FilterExpressionBuilder(EntityFrameworkQueryVisitorContext context)
    {
        Context = context;
        Root = Expression.Parameter(context.EntityType.ClrType, "e");
        _now = context.TimeProvider.GetUtcNow();
    }

    public EntityFrameworkQueryVisitorContext Context { get; }

    public ParameterExpression Root { get; }

    private QueryValidationResult Result => Context.ValidationResult;

    private string EntityName => Context.EntityType.ClrType.Name;

    public LambdaExpression Build(QueryDocument document)
    {
        var body = document.Query is null ? ExpressionHelpers.True : Visit(document.Query, scope: null) ?? ExpressionHelpers.False;
        return Expression.Lambda(body, Root);
    }

    private Expression? Visit(QueryNode node, FieldScope? scope)
    {
        if (node.HasData && node.GetFilterExpression() is { } filter)
            return InlineFilter(filter, node);

        switch (node)
        {
            case BooleanQueryNode boolean:
                return VisitBoolean(boolean, scope);
            case GroupNode group:
                if (group.BoostText is not null)
                    return Fail(group, "Boosts (^) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return group.Query is null ? ExpressionHelpers.True : Visit(group.Query, scope);
            case NotNode not:
                if (not.Query is null)
                    return Fail(not, "NOT must be followed by a query.", QueryErrorCode.ValidationError);
                var negated = Visit(not.Query, scope);
                return negated is null ? null : ExpressionHelpers.Not(negated);
            case FieldQueryNode field:
                return VisitField(field);
            case MatchAllNode when scope is { } fieldScope:
                // In a field group (name:(*)) a bare * means the field exists, as in Lucene.
                return BuildExists(node, fieldScope, missing: false);
            case MatchAllNode:
                return ExpressionHelpers.True;
            case ExistsNode exists:
                return BuildExists(exists, new FieldScope(exists.Field, exists.GetOriginalField()), missing: false);
            case MissingNode missing:
                return BuildExists(missing, new FieldScope(missing.Field, missing.GetOriginalField()), missing: true);
            case QueryDocument document:
                return document.Query is null ? ExpressionHelpers.True : Visit(document.Query, scope);
            case TermNode or PhraseNode or RangeNode or RegexNode:
                return BuildLeaf(node, scope);
            default:
                return Fail(node, $"Query node type {node.GetType().Name} is not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
        }
    }

    private Expression? VisitBoolean(BooleanQueryNode node, FieldScope? scope)
    {
        Expression? must = null;
        Expression? should = null;
        bool hasMust = false;
        bool failed = false;

        foreach (var clause in node.Clauses)
        {
            if (clause.Query is null)
                continue;

            var expression = Visit(clause.Query, scope);
            if (expression is null)
            {
                failed = true;
                continue;
            }

            switch (clause.Occur)
            {
                case Occur.Must:
                    hasMust = true;
                    must = ExpressionHelpers.AndAlso(must, expression);
                    break;
                case Occur.MustNot:
                    must = ExpressionHelpers.AndAlso(must, ExpressionHelpers.Not(expression));
                    break;
                default:
                    should = ExpressionHelpers.OrElse(should, expression);
                    break;
            }
        }

        if (failed)
            return null;

        if (!hasMust && should is not null)
            must = ExpressionHelpers.AndAlso(must, should);

        return must ?? ExpressionHelpers.True;
    }

    private Expression? VisitField(FieldQueryNode node)
    {
        string field = node.Field;
        if (IncludeVisitor.IsInclude(node))
            return Fail(node, $"The include ({IncludeVisitor.GetIncludeName(node)}) could not be expanded.", QueryErrorCode.UnresolvedInclude);

        if (field.Length > 0 && field[0] == '@')
            return Fail(node, $"The special field ({field}) is not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);

        if (node.IsWildcardField)
            return Fail(node, $"Wildcard field names ({node.GetOriginalField()}) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);

        if (node.Query is null)
            return Fail(node, $"Field ({node.GetOriginalField()}) must be followed by a query.", QueryErrorCode.ValidationError);

        return Visit(node.Query, new FieldScope(field, node.GetOriginalField()));
    }

    private Expression? InlineFilter(LambdaExpression filter, QueryNode node)
    {
        if (filter.Parameters.Count != 1 || !filter.Parameters[0].Type.IsAssignableFrom(Root.Type) || filter.ReturnType != typeof(bool))
            return Fail(node, $"The filter expression attached to the query node must be a predicate over {EntityName}.", QueryErrorCode.ExpressionBuildError);

        var parameter = filter.Parameters[0];
        return ExpressionHelpers.Inline(filter, parameter.Type == Root.Type ? Root : Expression.Convert(Root, parameter.Type));
    }

    private Expression? BuildLeaf(QueryNode node, FieldScope? scope)
    {
        if (!ValidateModifiers(node))
            return null;

        if (scope is not { } field)
            return BuildDefaultFields(node);

        if (!TryGetField(field, node, out var info))
            return null;

        return BuildFieldPredicate(info, node, searchOperator: null, lenient: false);
    }

    private bool ValidateModifiers(QueryNode node)
    {
        switch (node)
        {
            case TermNode { IsFuzzy: true } term:
                Fail(term, $"Fuzzy queries ({term.Term}~) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            case PhraseNode { ProximityText: not null } phrase:
                Fail(phrase, $"Proximity queries (\"{phrase.Phrase}\"~) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            case RangeNode { ProximityText: not null } range:
                Fail(range, "Proximity (~) is not supported on range queries by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            case RangeNode { BoostText: not null, Boost: not null } range:
                Fail(range, "Boosts (^) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            case RegexNode regex when Context.Configuration.RegexExpressionBuilder is null:
                Fail(regex, $"Regular expression queries (/{regex.Pattern}/) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            case RangeNode:
                return true;
            case IBoostable { BoostText: not null }:
                Fail(node, "Boosts (^) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);
                return false;
            default:
                return true;
        }
    }

    private Expression? BuildDefaultFields(QueryNode node)
    {
        var defaultFields = Context.DefaultFields;
        if (defaultFields is not { Length: > 0 })
            return Fail(node, $"Terms without a field ({QueryStringBuilder.ToQueryString(node)}) require default fields to be configured.", QueryErrorCode.UnresolvedField);

        int errors = Result.ValidationErrors.Count;
        Expression? result = null;
        bool matched = false;

        foreach (string defaultField in defaultFields)
        {
            FieldResolverQueryVisitor.TryResolveField(defaultField, Context, out string resolved);
            if (!TryGetField(new FieldScope(resolved, defaultField), node, out var info))
                continue;

            var predicate = BuildFieldPredicate(info, node, Context.DefaultSearchOperator, lenient: true);
            if (predicate is null)
                continue;

            matched = true;
            result = ExpressionHelpers.OrElse(result, predicate);
        }

        if (Result.ValidationErrors.Count > errors)
            return null;

        if (!matched)
            return Fail(node, $"The query ({QueryStringBuilder.ToQueryString(node)}) is not valid for any of the default fields ({string.Join(", ", defaultFields)}).", QueryErrorCode.TypeConversionError);

        return result;
    }

    private bool TryGetField(FieldScope scope, QueryNode node, [NotNullWhen(true)] out EntityFieldInfo? field)
    {
        if (Context.TryGetField(scope.Field, out field))
            return true;

        Result.UnresolvedFields.Add(scope.Original);
        Fail(node, $"Field ({scope.Original}) is not a queryable field of {EntityName}.", QueryErrorCode.UnresolvedField);
        return false;
    }

    private Expression? BuildFieldPredicate(EntityFieldInfo field, QueryNode node, SearchOperator? searchOperator, bool lenient)
    {
        if (!field.IsScalar)
            return Fail(lenient, node, $"Field ({field.FullName}) is a navigation; query one of its fields instead.", QueryErrorCode.UnsupportedQueryType);

        return BuildPath(field, instance => BuildMemberPredicate(instance, field, node, searchOperator, lenient));
    }

    /// <summary>
    /// Walks from the queried entity to the object declaring <paramref name="field"/>, accessing reference
    /// navigations and wrapping collection navigations in <c>Any</c>, then builds the predicate at the declaring object.
    /// </summary>
    private Expression? BuildPath(EntityFieldInfo field, Func<Expression, Expression?> buildAtDeclaringInstance)
    {
        if (field.Parent is null)
            return buildAtDeclaringInstance(Root);

        var chain = new List<EntityFieldInfo>();
        for (var parent = field.Parent; parent is not null; parent = parent.Parent)
            chain.Add(parent);
        chain.Reverse();

        return BuildPath(chain, 0, Root, buildAtDeclaringInstance);
    }

    private Expression? BuildPath(List<EntityFieldInfo> chain, int index, Expression instance, Func<Expression, Expression?> buildAtDeclaringInstance)
    {
        if (index == chain.Count)
            return buildAtDeclaringInstance(instance);

        var segment = chain[index];
        var member = ExpressionHelpers.Access(instance, segment);
        if (!segment.IsCollection)
            return BuildPath(chain, index + 1, member, buildAtDeclaringInstance);

        var element = CreateParameter(segment.ElementType!);
        var body = BuildPath(chain, index + 1, element, buildAtDeclaringInstance);
        return body is null ? null : Any(member, element, body);
    }

    private Expression? BuildMemberPredicate(Expression instance, EntityFieldInfo field, QueryNode node, SearchOperator? searchOperator, bool lenient)
    {
        if (TryBuildCustom(instance, field, node, searchOperator, out var custom))
            return custom;

        if (field.Kind == EntityFieldKind.Custom)
            return Fail(node, $"Field ({field.FullName}) is a custom field; configure a CustomFieldExpressionBuilder that handles it.", QueryErrorCode.ExpressionBuildError);

        var member = ExpressionHelpers.Access(instance, field);
        if (!field.IsCollection)
            return BuildValuePredicate(member, field, node, searchOperator, lenient);

        var element = CreateParameter(field.ElementType!);
        var predicate = BuildValuePredicate(element, field, node, searchOperator, lenient);
        return predicate is null ? null : Any(member, element, predicate);
    }

    private bool TryBuildCustom(Expression instance, EntityFieldInfo field, QueryNode node, SearchOperator? searchOperator, out Expression? expression)
    {
        expression = null;
        var builder = Context.CustomFieldExpressionBuilder;
        if (builder is null)
            return false;

        expression = builder(new CustomFieldContext(this, field, node, instance, searchOperator));
        if (expression is null)
            return false;

        if (expression.Type != typeof(bool))
            expression = Fail(node, $"The custom field expression for ({field.FullName}) must return a boolean predicate.", QueryErrorCode.ExpressionBuildError);

        return true;
    }

    internal Expression BuildDefaultForMember(Expression member, EntityFieldInfo field, QueryNode node, SearchOperator? searchOperator)
    {
        var predicate = node switch
        {
            ExistsNode => Exists(member),
            MissingNode => ExpressionHelpers.Not(Exists(member)),
            _ => BuildValuePredicate(member, field, node, searchOperator, lenient: false)
        };

        return predicate ?? ExpressionHelpers.False;
    }

    private Expression? BuildExists(QueryNode node, FieldScope scope, bool missing)
    {
        if (scope.Field.Length > 0 && scope.Field[0] == '@')
            return Fail(node, $"The special field ({scope.Field}) is not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);

        if (scope.Field.AsSpan().IndexOfAny('*', '?') >= 0)
            return Fail(node, $"Wildcard field names ({scope.Original}) are not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType);

        if (!TryGetField(scope, node, out var field))
            return null;

        bool handledByCustomBuilder = false;
        var exists = BuildPath(field, instance =>
        {
            if (TryBuildCustom(instance, field, node, searchOperator: null, out var custom))
            {
                handledByCustomBuilder = true;
                return custom;
            }

            if (field.Kind == EntityFieldKind.Custom)
                return Fail(node, $"Field ({field.FullName}) is a custom field; configure a CustomFieldExpressionBuilder that handles it.", QueryErrorCode.ExpressionBuildError);

            var member = ExpressionHelpers.Access(instance, field);
            return field.IsCollection ? ExpressionHelpers.Any(member, field.ElementType!) : Exists(member);
        });

        if (exists is null || handledByCustomBuilder || !missing)
            return exists;

        return ExpressionHelpers.Not(exists);
    }

    private static Expression Exists(Expression member)
    {
        var type = member.Type;
        if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            return ExpressionHelpers.True;

        return Expression.NotEqual(member, Expression.Constant(null, type));
    }

    private Expression? BuildValuePredicate(Expression member, EntityFieldInfo field, QueryNode node, SearchOperator? searchOperator, bool lenient)
    {
        var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        return node switch
        {
            TermNode term when term.IsPrefix || term.IsWildcard => BuildWildcard(member, type, field, term, lenient),
            TermNode term => BuildEquals(member, type, field, term.UnescapedTerm, node, searchOperator, lenient),
            PhraseNode phrase => BuildEquals(member, type, field, phrase.Phrase, node, searchOperator, lenient),
            RangeNode range => BuildRange(member, type, field, range, lenient),
            RegexNode regex => BuildRegex(member, type, field, regex, lenient),
            ExistsNode => Exists(member),
            MissingNode => ExpressionHelpers.Not(Exists(member)),
            _ => Fail(node, $"Query node type {node.GetType().Name} is not supported by the Entity Framework provider.", QueryErrorCode.UnsupportedQueryType)
        };
    }

    private Expression? BuildEquals(Expression member, Type type, EntityFieldInfo field, string text, QueryNode node, SearchOperator? searchOperator, bool lenient)
    {
        if (type == typeof(string))
            return BuildStringMatch(member, field, text, node, searchOperator);

        if (QueryValueParser.IsDate(type))
            return BuildDateEquals(member, type, field, text, node, Context.DefaultTimeZone, lenient);

        if (type == typeof(TimeOnly))
        {
            if (!TryParseTime(text, Context.DefaultTimeZone, out var time))
                return InvalidValue(lenient, node, field, text, type);

            return Expression.Equal(member, QueryParameter.Create(time, member.Type));
        }

        if (!QueryValueParser.IsSupported(type))
            return Fail(lenient, node, $"Field ({field.FullName}) of type {type.Name} does not support value queries.", QueryErrorCode.UnsupportedQueryType);

        if (!QueryValueParser.TryParse(text, type, out object? value))
            return InvalidValue(lenient, node, field, text, type);

        return Expression.Equal(member, QueryParameter.Create(value, member.Type));
    }

    private Expression? BuildStringMatch(Expression member, EntityFieldInfo field, string text, QueryNode node, SearchOperator? searchOperator)
    {
        if (searchOperator is not { } op || Context.Configuration.SearchTokenizer is not { } tokenizer)
            return BuildStringValue(member, field, text, searchOperator ?? SearchOperator.Equals, node);

        var term = new SearchTerm { Field = field, Term = text, Operator = op };
        tokenizer(term);
        if (term.Tokens is null)
            return BuildStringValue(member, field, text, term.Operator, node);

        Expression? result = null;
        foreach (string token in term.Tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var predicate = BuildStringValue(member, field, token, term.Operator, node);
            if (predicate is null)
                return null;

            result = ExpressionHelpers.OrElse(result, predicate);
        }

        return result ?? ExpressionHelpers.False;
    }

    private Expression? BuildStringValue(Expression member, EntityFieldInfo field, string text, SearchOperator op, QueryNode node)
    {
        if (field.IsFullTextSearch)
            return BuildFullText(member, field, text, prefix: op != SearchOperator.Equals, node);

        var value = QueryParameter.Create(text, typeof(string));
        return op switch
        {
            SearchOperator.StartsWith => Expression.Call(member, ExpressionHelpers.StringStartsWith, value),
            SearchOperator.Contains => Expression.Call(member, ExpressionHelpers.StringContains, value),
            _ => Expression.Equal(member, value)
        };
    }

    private Expression? BuildFullText(Expression member, EntityFieldInfo field, string text, bool prefix, QueryNode node)
    {
        if (LikePattern.ToFullTextCondition(text, prefix) is not { } condition)
            return Fail(node, $"Full-text searches on field ({field.FullName}) need text to search for.", QueryErrorCode.ValidationError);

        return ExpressionHelpers.FullTextContains(member, condition)
            ?? Fail(node, $"Field ({field.FullName}) is full-text indexed, which requires the Microsoft.EntityFrameworkCore.SqlServer provider.", QueryErrorCode.ExpressionBuildError);
    }

    private Expression? BuildWildcard(Expression member, Type type, EntityFieldInfo field, TermNode term, bool lenient)
    {
        if (type != typeof(string))
            return Fail(lenient, term, $"Wildcard queries ({term.Term}) require a string field; field ({field.FullName}) is {Describe(type)}.", QueryErrorCode.UnsupportedQueryType);

        var pattern = term.IsPrefix ? new WildcardPattern(WildcardKind.Prefix, term.UnescapedTerm) : LikePattern.Analyze(term.Term);
        switch (pattern.Kind)
        {
            case WildcardKind.Prefix when field.IsFullTextSearch:
                return BuildFullText(member, field, pattern.Value, prefix: true, term);
            case WildcardKind.Prefix:
                return Expression.Call(member, ExpressionHelpers.StringStartsWith, QueryParameter.Create(pattern.Value, typeof(string)));
            case WildcardKind.Contains:
                return Expression.Call(member, ExpressionHelpers.StringContains, QueryParameter.Create(pattern.Value, typeof(string)));
            default:
                return ExpressionHelpers.Like(member, pattern.Value);
        }
    }

    private Expression? BuildRegex(Expression member, Type type, EntityFieldInfo field, RegexNode regex, bool lenient)
    {
        if (type != typeof(string))
            return Fail(lenient, regex, $"Regular expression queries require a string field; field ({field.FullName}) is {Describe(type)}.", QueryErrorCode.UnsupportedQueryType);

        var builder = Context.Configuration.RegexExpressionBuilder!;
        var expression = builder(member, QueryParameter.Create(regex.Pattern, typeof(string)));
        return expression.Type == typeof(bool)
            ? expression
            : Fail(regex, "The regular expression builder must return a boolean predicate.", QueryErrorCode.ExpressionBuildError);
    }

    private Expression? BuildRange(Expression member, Type type, EntityFieldInfo field, RangeNode range, bool lenient)
    {
        var timeZone = Context.DefaultTimeZone;
        if (range.BoostText is { } caret)
        {
            if (!QueryValueParser.IsDate(type) && type != typeof(TimeOnly))
                return Fail(lenient, range, $"The time zone modifier (^{caret}) only applies to date fields; field ({field.FullName}) is {Describe(type)}.", QueryErrorCode.UnsupportedQueryType);

            if (!TryFindTimeZone(caret, out timeZone))
                return Fail(range, $"The time zone ({caret}) is not a known time zone.", QueryErrorCode.ValidationError);
        }

        if (range.Min is null && range.Max is null)
            return Exists(member);

        if (type == typeof(bool) || type == typeof(Guid))
            return Fail(lenient, range, $"Range queries are not supported on field ({field.FullName}) of type {type.Name}.", QueryErrorCode.UnsupportedQueryType);

        if (QueryValueParser.IsDate(type))
            return BuildDateRange(member, type, field, range, timeZone, lenient);

        if (type.IsEnum && field.Metadata is IProperty property && IsStoredAsText(property))
            return Fail(lenient, range, $"Range queries are not supported on field ({field.FullName}) because its values are stored as text.", QueryErrorCode.UnsupportedQueryType);

        if (type != typeof(TimeOnly) && !QueryValueParser.IsSupported(type))
            return Fail(lenient, range, $"Field ({field.FullName}) of type {type.Name} does not support range queries.", QueryErrorCode.UnsupportedQueryType);

        var comparand = type.IsEnum ? ConvertEnum(member, type) : member;
        Expression? result = null;
        if (range.Min is { } min)
        {
            var bound = BuildBound(comparand, member, type, field, min, range.MinInclusive ? ExpressionType.GreaterThanOrEqual : ExpressionType.GreaterThan, range, timeZone, lenient);
            if (bound is null)
                return null;
            result = ExpressionHelpers.AndAlso(result, bound);
        }

        if (range.Max is { } max)
        {
            var bound = BuildBound(comparand, member, type, field, max, range.MaxInclusive ? ExpressionType.LessThanOrEqual : ExpressionType.LessThan, range, timeZone, lenient);
            if (bound is null)
                return null;
            result = ExpressionHelpers.AndAlso(result, bound);
        }

        return result;
    }

    private Expression? BuildBound(Expression comparand, Expression member, Type type, EntityFieldInfo field, string text, ExpressionType op, RangeNode range, TimeZoneInfo timeZone, bool lenient)
    {
        if (type == typeof(string))
        {
            var compare = Expression.Call(ExpressionHelpers.StringCompare, member, QueryParameter.Create(text, typeof(string)));
            return Expression.MakeBinary(op, compare, Expression.Constant(0));
        }

        object? value;
        if (type == typeof(TimeOnly))
        {
            if (!TryParseTime(text, timeZone, out var time))
                return InvalidValue(lenient, range, field, text, type);
            value = time;
        }
        else if (!QueryValueParser.TryParse(text, type, out value))
        {
            return InvalidValue(lenient, range, field, text, type);
        }

        if (type.IsEnum)
            value = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);

        return Expression.MakeBinary(op, comparand, QueryParameter.Create(value, comparand.Type));
    }

    private static bool IsStoredAsText(IProperty property)
    {
        var providerType = property.GetProviderClrType() ?? (property.GetValueConverter() ?? property.FindTypeMapping()?.Converter)?.ProviderClrType;
        return providerType == typeof(string);
    }

    private static Expression ConvertEnum(Expression member, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        return Expression.Convert(member, Nullable.GetUnderlyingType(member.Type) is null ? underlying : typeof(Nullable<>).MakeGenericType(underlying));
    }

    private Expression? BuildDateEquals(Expression member, Type type, EntityFieldInfo field, string text, QueryNode node, TimeZoneInfo timeZone, bool lenient)
    {
        if (!TryEvaluateDate(text, isUpperLimit: false, timeZone, out var lower) || !TryEvaluateDate(text, isUpperLimit: true, timeZone, out var upper))
            return InvalidValue(lenient, node, field, text, type);

        if (type == typeof(DateOnly))
        {
            var first = ToDate(lower, timeZone);
            var last = ToDate(upper, timeZone);
            if (first == last)
                return Expression.Equal(member, QueryParameter.Create(first, member.Type));
        }
        else if (lower == upper)
        {
            return Expression.Equal(member, DateValue(lower, member.Type));
        }

        return ExpressionHelpers.AndAlso(
            CompareDate(member, type, lower, ExpressionType.GreaterThanOrEqual, timeZone),
            CompareDate(member, type, upper, ExpressionType.LessThanOrEqual, timeZone));
    }

    private Expression? BuildDateRange(Expression member, Type type, EntityFieldInfo field, RangeNode range, TimeZoneInfo timeZone, bool lenient)
    {
        Expression? result = null;
        if (range.Min is { } min)
        {
            if (!TryEvaluateDate(min, isUpperLimit: !range.MinInclusive, timeZone, out var lower))
                return InvalidValue(lenient, range, field, min, type);

            result = CompareDate(member, type, lower, range.MinInclusive ? ExpressionType.GreaterThanOrEqual : ExpressionType.GreaterThan, timeZone);
        }

        if (range.Max is { } max)
        {
            if (!TryEvaluateDate(max, isUpperLimit: range.MaxInclusive, timeZone, out var upper))
                return InvalidValue(lenient, range, field, max, type);

            result = ExpressionHelpers.AndAlso(result, CompareDate(member, type, upper, range.MaxInclusive ? ExpressionType.LessThanOrEqual : ExpressionType.LessThan, timeZone));
        }

        return result;
    }

    /// <summary>
    /// Compares a date member with an evaluated bound. A bound rounded up to the last tick of a period is compared
    /// against the start of the next period instead (<c>&lt;= 23:59:59.9999999</c> becomes <c>&lt; 00:00</c>), which
    /// is exact and avoids precision loss in columns that store fewer fractional digits.
    /// </summary>
    private Expression CompareDate(Expression member, Type type, DateTimeOffset value, ExpressionType op, TimeZoneInfo timeZone)
    {
        if (type == typeof(DateOnly))
            return Expression.MakeBinary(op, member, QueryParameter.Create(ToDate(value, timeZone), member.Type));

        if ((value.Ticks + 1) % TimeSpan.TicksPerSecond == 0 && value.Ticks < DateTimeOffset.MaxValue.Ticks)
        {
            if (op == ExpressionType.LessThanOrEqual)
                (value, op) = (value.AddTicks(1), ExpressionType.LessThan);
            else if (op == ExpressionType.GreaterThan)
                (value, op) = (value.AddTicks(1), ExpressionType.GreaterThanOrEqual);
        }

        return Expression.MakeBinary(op, member, DateValue(value, member.Type));
    }

    /// <summary>
    /// The calendar date of an instant in the query's time zone. <see cref="DateOnly"/> fields compare against the
    /// date of each (already rounded) bound.
    /// </summary>
    private static DateOnly ToDate(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, timeZone).DateTime);
    }

    private Expression DateValue(DateTimeOffset value, Type memberType)
    {
        var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (type == typeof(DateTimeOffset))
            return QueryParameter.Create(value, memberType);

        var storageZone = Context.Configuration.DateTimeStorageTimeZone;
        var stored = storageZone == TimeZoneInfo.Utc
            ? value.UtcDateTime
            : DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(value, storageZone).DateTime, DateTimeKind.Unspecified);

        return QueryParameter.Create(stored, memberType);
    }

    private bool TryEvaluateDate(string text, bool isUpperLimit, TimeZoneInfo timeZone, out DateTimeOffset value)
    {
        return DateMath.TryParse(text, _now, timeZone, isUpperLimit, out value);
    }

    private bool TryParseTime(string text, TimeZoneInfo timeZone, out TimeOnly value)
    {
        if (text.StartsWith("now", StringComparison.Ordinal) && TryEvaluateDate(text, isUpperLimit: false, timeZone, out var now))
        {
            value = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(now, timeZone).TimeOfDay);
            return true;
        }

        return QueryValueParser.TryParseTime(text, out value);
    }

    private static bool TryFindTimeZone(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private ParameterExpression CreateParameter(Type type) => Expression.Parameter(type, "x" + ++_parameterCount);

    private static Expression Any(Expression collection, ParameterExpression element, Expression body)
    {
        return ExpressionHelpers.IsFalse(body) ? ExpressionHelpers.False : ExpressionHelpers.Any(collection, Expression.Lambda(body, element));
    }

    private Expression? InvalidValue(bool lenient, QueryNode node, EntityFieldInfo field, string text, Type type)
    {
        return Fail(lenient, node, $"Value ({text}) is not a valid {Describe(type)} for field ({field.FullName}).", QueryErrorCode.TypeConversionError);
    }

    private static string Describe(Type type)
    {
        if (type.IsEnum)
            return $"{type.Name} value ({string.Join(", ", Enum.GetNames(type))})";
        if (QueryValueParser.IsNumeric(type))
            return Type.GetTypeCode(type) is TypeCode.Single or TypeCode.Double or TypeCode.Decimal ? "number" : $"whole number ({type.Name})";
        if (type == typeof(bool))
            return "boolean (true or false)";
        if (type == typeof(Guid))
            return "GUID";
        if (type == typeof(char))
            return "single character";
        if (type == typeof(TimeOnly))
            return "time of day";
        if (type == typeof(TimeSpan))
            return "time span";
        if (QueryValueParser.IsDate(type))
            return "date";
        return type.Name;
    }

    private Expression? Fail(bool lenient, QueryNode node, string message, QueryErrorCode code)
    {
        return lenient ? null : Fail(node, message, code);
    }

    private Expression? Fail(QueryNode node, string message, QueryErrorCode code)
    {
        Result.AddError(message, node.StartPosition, code);
        return null;
    }

    private readonly record struct FieldScope(string Field, string Original);
}
