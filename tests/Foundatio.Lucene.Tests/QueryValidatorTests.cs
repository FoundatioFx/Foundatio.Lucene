using System.Collections.Concurrent;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class QueryValidatorTests
{
    [Fact]
    public void ValidateQuery_ValidQuery_ReturnsValidResultWithStatistics()
    {
        // Act
        var result = QueryValidator.ValidateQuery("title:hello AND (status:active OR _exists_:tags) AND price:[1 TO 5]");

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(QueryType.Query, result.QueryType);
        Assert.Equal(["price", "status", "tags", "title"], result.ReferencedFields.Order());
        Assert.Equal([QueryOperations.Exists, QueryOperations.Range, QueryOperations.Term], result.Operations.Keys.Order());
        Assert.Equal(2, result.MaxNodeDepth);
    }

    [Theory]
    [InlineData("title:(hello")]
    [InlineData("\"unterminated")]
    [InlineData("a AND")]
    [InlineData("price:[1 TO")]
    public void ValidateQuery_SyntaxError_ReturnsParseErrors(string query)
    {
        // Act
        var result = QueryValidator.ValidateQuery(query);

        // Assert
        Assert.False(result.IsValid);
        Assert.All(result.ValidationErrors, e => Assert.True(e.Position >= 0));
        Assert.Empty(result.ReferencedFields);
    }

    [Fact]
    public void ValidateQuery_SyntaxErrorWithRestrictions_DoesNotRunFurtherValidation()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        var result = QueryValidator.ValidateQuery("secret:(1", options);

        Assert.DoesNotContain(result.ValidationErrors, e => e.Code == QueryErrorCode.FieldRestricted);
    }

    [Fact]
    public void ValidateQueryAndThrow_InvalidQuery_ThrowsQueryValidationException()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("title");

        // Act
        var exception = Assert.Throws<QueryValidationException>(() => QueryValidator.ValidateQueryAndThrow("other:1", options));

        // Assert
        Assert.StartsWith("Invalid query:", exception.Message);
        Assert.Equal(QueryErrorCode.FieldNotAllowed, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void ValidateQueryAndThrow_ValidQuery_ReturnsResult()
    {
        var result = QueryValidator.ValidateQueryAndThrow("title:x");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldThrow_ThrowsForInvalidQuery()
    {
        var options = new QueryValidationOptions { ShouldThrow = true, AllowLeadingWildcards = false };

        Assert.Throws<QueryValidationException>(() => QueryValidator.ValidateQuery("*foo", options));
        Assert.True(QueryValidator.ValidateQuery("foo*", options).IsValid);
    }

    [Fact]
    public void ValidateQuery_OptionsOnContext_AreUsedWhenNoOptionsPassed()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext { ValidationOptions = options };

        // Act
        var result = QueryValidator.ValidateQuery("secret:1", context: context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Same(context.ValidationResult, result);
    }

    [Fact]
    public void ValidateQuery_ContextDefaultOperator_IsUsedForParsing()
    {
        var context = new QueryVisitorContext { DefaultOperator = BooleanOperator.Or };
        var options = new QueryValidationOptions { AllowedMaxNodeDepth = 1 };

        var result = QueryValidator.ValidateQuery("a b", options, context);

        Assert.True(result.IsValid);
        Assert.Equal(BooleanOperator.Or, context.ParserOptions.DefaultOperator);
    }

    [Theory]
    [InlineData("@include:hidden")]
    [InlineData("@include:indirect")]
    [InlineData("-@include:hidden")]
    [InlineData("x:1 OR NOT @include:indirect")]
    [InlineData("innocent:1")]
    [InlineData("INNOCENT:1")]
    [InlineData("nested.child:1")]
    [InlineData("secret.keyword:1")]
    [InlineData("Secret.Raw:1")]
    [InlineData("sec\\ret:1")]
    [InlineData("_exists_:secret")]
    [InlineData("_missing_:secret.keyword")]
    [InlineData("_exists_:innocent")]
    [InlineData("_exists_:secr*")]
    [InlineData("secr*:1")]
    [InlineData("s?cret:1")]
    [InlineData("@include:wildcard")]
    [InlineData("x:(y OR (z AND secret:1))")]
    public void ValidateQuery_RestrictedFieldBypassAttempts_AreRejected(string query)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext
        {
            Includes = new Dictionary<string, string>
            {
                ["hidden"] = "secret:1",
                ["indirect"] = "a:1 AND @include:hidden",
                ["wildcard"] = "_exists_:sec*"
            },
            FieldMap = new FieldMap { { "innocent", "secret" }, { "nested", "secret.inner" } }
        };

        // Act
        var result = QueryValidator.ValidateQuery(query, options, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.ValidationErrors, e => e.Code is QueryErrorCode.FieldRestricted or QueryErrorCode.FieldNotAllowed);
    }

    [Theory]
    [InlineData("account.user:x")]
    [InlineData("@include:other")]
    [InlineData("_exists_:acc*")]
    [InlineData("*:x")]
    public void ValidateQuery_AllowedFieldBypassAttempts_AreRejected(string query)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("user");
        var context = new QueryVisitorContext
        {
            Includes = new Dictionary<string, string> { ["other"] = "user:x OR account.password:y" },
            FieldMap = new FieldMap { { "user", "account.user" } }
        };

        // Act
        var result = QueryValidator.ValidateQuery(query, options, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.ValidationErrors, e => e.Code == QueryErrorCode.FieldNotAllowed);
    }

    [Fact]
    public void ValidateQuery_AllowedAlias_IsValid()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("user");
        var context = new QueryVisitorContext { FieldMap = new FieldMap { { "user", "account.user" } } };

        // Act
        var result = QueryValidator.ValidateQuery("user:x user.first:y", options, context);

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(["account.user", "account.user.first"], result.ResolvedFields.Order());
    }

    [Fact]
    public void ValidateQuery_UnresolvedInclude_IsInvalidByDefault()
    {
        var result = QueryValidator.ValidateQuery("@include:nope");

        Assert.False(result.IsValid);
        Assert.Equal(["nope"], result.UnresolvedIncludes);
        Assert.Equal(QueryErrorCode.UnresolvedInclude, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void ValidateQuery_IncludeFanOutBomb_IsRejectedQuickly()
    {
        // Arrange
        string fanOut(string next) => string.Join(" ", Enumerable.Repeat($"@include:{next}", 50));
        var context = new QueryVisitorContext
        {
            Includes = new Dictionary<string, string>
            {
                ["a"] = fanOut("b"),
                ["b"] = fanOut("c"),
                ["c"] = fanOut("d"),
                ["d"] = fanOut("e"),
                ["e"] = "x:1"
            }
        };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = QueryValidator.ValidateQuery("@include:a", context: context);

        // Assert
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(result.IsValid);
        Assert.Contains(result.ValidationErrors, e => e.Code == QueryErrorCode.MaxDepthExceeded);
    }

    [Fact]
    public void Validate_Document_DoesNotModifyDocument()
    {
        // Arrange
        var document = LuceneQuery.Parse("user:john @include:saved").Document;
        string before = document.ToDebugString();
        var context = new QueryVisitorContext
        {
            Includes = new Dictionary<string, string> { ["saved"] = "status:active" },
            FieldMap = new FieldMap { { "user", "account.user" }, { "status", "data.status" } }
        };

        // Act
        var result = document.Validate(context: context);

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(before, document.ToDebugString());
        bool hasData = false;
        document.Walk(n => hasData |= n.HasData);
        Assert.False(hasData);
        Assert.Equal(["account.user", "data.status"], result.ResolvedFields.Order());
    }

    [Fact]
    public void Validate_Options_AreNotModified()
    {
        // Arrange
        var options = new QueryValidationOptions { AllowUnresolvedFields = false, AllowedMaxNodeDepth = 3 };
        options.AllowedFields.Add("title");
        options.RestrictedFields.Add("secret");
        options.AllowedOperations.Add(QueryOperations.Term);

        // Act
        QueryValidator.ValidateQuery("title:x other:y secret:z (((a)))", options);
        QueryValidator.ValidateSort("-title other", options);
        QueryValidator.ValidateAggregations("terms:title min:other", options);

        // Assert
        Assert.Equal(["title"], options.AllowedFields);
        Assert.Equal(["secret"], options.RestrictedFields);
        Assert.Equal([QueryOperations.Term], options.AllowedOperations);
        Assert.Empty(options.RestrictedOperations);
        Assert.False(options.AllowUnresolvedFields);
        Assert.Equal(3, options.AllowedMaxNodeDepth);
    }

    [Fact]
    public void Validate_ParseResultWithErrors_ReturnsParseErrors()
    {
        var parsed = LuceneQuery.Parse("a:(b");

        var result = parsed.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(parsed.Errors.Count, result.ValidationErrors.Count);
    }

    [Fact]
    public void Validate_SuccessfulParseResult_ValidatesDocument()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        var result = LuceneQuery.Parse("secret:1").Validate(options);

        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void Validate_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QueryValidator.ValidateQuery(null!));
        Assert.Throws<ArgumentNullException>(() => QueryValidator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => QueryValidator.ValidateSort(null!));
        Assert.Throws<ArgumentNullException>(() => QueryValidator.ValidateAggregations(null!));
        Assert.Throws<ArgumentNullException>(() => ((LuceneParseResult)null!).Validate());
    }

    [Fact]
    public void ValidateSort_ValidSort_RecordsFields()
    {
        // Arrange
        var context = new QueryVisitorContext { FieldMap = new FieldMap { { "created", "meta.created" } } };

        // Act
        var result = QueryValidator.ValidateSort("-created +name", context: context);

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(QueryType.Sort, result.QueryType);
        Assert.Equal(["created", "name"], result.ReferencedFields.Order());
        Assert.Equal(["meta.created", "name"], result.ResolvedFields.Order());
    }

    [Theory]
    [InlineData("-secret")]
    [InlineData("secret.keyword:desc")]
    [InlineData("innocent")]
    [InlineData("@include:sneaky")]
    public void ValidateSort_RestrictedField_IsRejected(string sort)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext
        {
            FieldMap = new FieldMap { { "innocent", "secret" } },
            Includes = new Dictionary<string, string> { ["sneaky"] = "name -secret" }
        };

        // Act
        var result = QueryValidator.ValidateSort(sort, options, context);

        // Assert
        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void ValidateSort_TooManyFields_ReturnsError()
    {
        var options = new QueryValidationOptions { AllowedMaxSortFields = 2 };

        Assert.True(QueryValidator.ValidateSort("a b", options).IsValid);
        Assert.False(QueryValidator.ValidateSort("a b c", options).IsValid);
    }

    [Fact]
    public void ValidateSort_TooManyFieldsWithOptionsOnContext_ReturnsError()
    {
        var context = new QueryVisitorContext { ValidationOptions = new QueryValidationOptions { AllowedMaxSortFields = 1 } };

        var result = QueryValidator.ValidateSort("a b", context: context);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("NOT a")]
    [InlineData("a:foo")]
    [InlineData("a*")]
    [InlineData("(a")]
    public void ValidateSort_InvalidSort_ReturnsError(string sort)
    {
        var result = QueryValidator.ValidateSort(sort);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateAggregations_ValidExpression_RecordsTypesAndFields()
    {
        // Arrange
        var context = new QueryVisitorContext { FieldMap = new FieldMap { { "created", "meta.created" } } };

        // Act
        var result = QueryValidator.ValidateAggregations("terms:(status min:created) cardinality:user", context: context);

        // Assert
        Assert.True(result.IsValid, result.Message);
        Assert.Equal(QueryType.Aggregation, result.QueryType);
        Assert.Equal([AggregationTypes.Cardinality, AggregationTypes.Min, AggregationTypes.Terms], result.Operations.Keys.Order());
        Assert.Equal(["created", "status", "user"], result.ReferencedFields.Order());
        Assert.Contains("meta.created", result.ResolvedFields);
    }

    [Fact]
    public void ValidateAggregations_IncludeModifier_IsNotTreatedAsQueryInclude()
    {
        var result = QueryValidator.ValidateAggregations("terms:(status @include:active @exclude:/test.*/)");

        Assert.True(result.IsValid, result.Message);
        Assert.Empty(result.UnresolvedIncludes);
    }

    [Theory]
    [InlineData("terms:(status max:secret)")]
    [InlineData("terms:secret.keyword")]
    [InlineData("date:(created terms:(status min:SECRET))")]
    public void ValidateAggregations_RestrictedFieldAnywhere_IsRejected(string aggregations)
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        var result = QueryValidator.ValidateAggregations(aggregations, options);

        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void ValidateAggregations_AllowedOperations_RestrictsAggregationTypes()
    {
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add(AggregationTypes.Terms);

        Assert.True(QueryValidator.ValidateAggregations("terms:status", options).IsValid);
        Assert.Equal(QueryErrorCode.OperationNotAllowed, Assert.Single(QueryValidator.ValidateAggregations("terms:(status max:x)", options).ValidationErrors).Code);
    }

    [Theory]
    [InlineData("bogus:field")]
    [InlineData("terms:")]
    [InlineData("min:(field max:other)")]
    [InlineData("NOT terms:a")]
    public void ValidateAggregations_InvalidExpression_ReturnsError(string aggregations)
    {
        var result = QueryValidator.ValidateAggregations(aggregations);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_SharedOptionsAndIncludesConcurrently_ResultsAreIndependent()
    {
        // Arrange
        var options = new QueryValidationOptions { AllowLeadingWildcards = false };
        options.RestrictedFields.Add("secret");
        var includes = new Dictionary<string, string> { ["bad"] = "secret:1", ["good"] = "public:1" };
        var fieldMap = new FieldMap { { "alias", "secret" } };
        var failures = new ConcurrentBag<string>();

        // Act
        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            string query = (i % 4) switch
            {
                0 => "@include:bad",
                1 => "@include:good",
                2 => $"alias:{i}",
                _ => $"f{i}:*x"
            };
            bool expectedValid = i % 4 == 1;
            var context = new QueryVisitorContext { Includes = includes, FieldMap = fieldMap };
            var result = QueryValidator.ValidateQuery(query, options, context);
            if (result.IsValid != expectedValid)
                failures.Add(query);
        });

        // Assert
        Assert.Empty(failures);
        Assert.Single(options.RestrictedFields);
        Assert.Equal(2, includes.Count);
        Assert.Single(fieldMap);
    }
}
