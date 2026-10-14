using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class ValidationTests
{
    [Theory]
    [InlineData("keyword:x", true)]
    [InlineData("KEYWORD:x", true)]
    [InlineData("obj.name:x", true)]
    [InlineData("obj:(name:x)", false)]
    [InlineData("text:x", false)]
    [InlineData("_exists_:text", false)]
    [InlineData("_missing_:text", false)]
    [InlineData("keyword:x OR text:y", false)]
    [InlineData("keyword:x AND NOT (text:y)", false)]
    [InlineData("tex*:x", false)]
    [InlineData("@include:secret", false)]
    [InlineData("user:x", true)]
    [InlineData("keyword:(x OR y)", true)]
    public void ValidateQuery_WithAllowedFields_RejectsOtherFields(string query, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("keyword");
        options.AllowedFields.Add("obj");
        options.AllowedFields.Add("user");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.FieldMap = new FieldMap { { "user", "text" } };
            c.Includes = new Dictionary<string, string> { ["secret"] = "text:hidden" };
        });

        var result = parser.ValidateQuery(query);

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
            Assert.Contains(result.ValidationErrors, e => e.Code == QueryErrorCode.FieldNotAllowed);
    }

    [Theory]
    [InlineData("keyword:x", true)]
    [InlineData("secret:x", false)]
    [InlineData("SECRET:x", false)]
    [InlineData("Secret.Inner:x", false)]
    [InlineData("secret.keyword:x", false)]
    [InlineData("se\\cret:x", false)]
    [InlineData("hidden:x", false)]
    [InlineData("hidden.sub:x", false)]
    [InlineData("_exists_:secret", false)]
    [InlineData("_missing_:hidden", false)]
    [InlineData("keyword:x OR (secret:y)", false)]
    [InlineData("NOT secret:y", false)]
    [InlineData("obj:(secret:x)", false)]
    [InlineData("secret:(a OR b)", false)]
    [InlineData("secret:[1 TO 2]", false)]
    [InlineData("@include:peek", false)]
    [InlineData("-@include:peek", false)]
    [InlineData("secre?:x", false)]
    [InlineData("*:*", true)]
    public void ValidateQuery_WithRestrictedFields_BlocksDirectAndIndirectUse(string query, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.FieldMap = new FieldMap { { "hidden", "secret" } };
            c.Includes = new Dictionary<string, string> { ["peek"] = "secret:x" };
        });

        var result = parser.ValidateQuery(query);

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
            Assert.Contains(result.ValidationErrors, e => e.Code is QueryErrorCode.FieldRestricted or QueryErrorCode.FieldNotAllowed);
    }

    [Fact]
    public void BuildQuery_WithRestrictedField_ThrowsValidationException()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("keyword");
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("keyword:x"));

        Assert.Contains("restricted", exception.Message);
        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void BuildQuery_WithRestrictedFieldResolvedFromMapping_ThrowsValidationException()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("obj.name");
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        Assert.Throws<QueryValidationException>(() => parser.BuildQuery("OBJ.NAME:x"));
    }

    [Fact]
    public Task BuildQueryAsync_WithRestrictedFieldBehindAsyncResolver_ThrowsValidationException()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("keyword");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.AsyncFieldResolver = (field, _, _) => ValueTask.FromResult(field == "innocent" ? "keyword" : null);
        });

        return Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("innocent:x", cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task BuildQueryAsync_WithRestrictedFieldInResolvedInclude_ThrowsValidationException()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("keyword");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("obj.name:x OR keyword:y");
        });

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildQueryAsync("@include:any", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("keyword", exception.Message);
    }

    [Theory]
    [InlineData("keyword:x", true)]
    [InlineData("keyword:x*", false)]
    [InlineData("keyword:x?y", false)]
    [InlineData("keyword:x~1", false)]
    [InlineData("keyword:/x.*/", false)]
    [InlineData("number:[1 TO 2]", false)]
    [InlineData("_exists_:keyword", false)]
    [InlineData("\"a b\"", false)]
    [InlineData("keyword:(a OR b)", true)]
    public void ValidateQuery_WithAllowedOperations_RejectsOtherOperations(string query, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add(QueryOperations.Term);
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        var result = parser.ValidateQuery(query);

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
            Assert.Equal(QueryErrorCode.OperationNotAllowed, Assert.Single(result.ValidationErrors).Code);
    }

    [Theory]
    [InlineData("keyword:/x.*/", QueryOperations.Regex)]
    [InlineData("keyword:x*", QueryOperations.Prefix)]
    [InlineData("keyword:*x", QueryOperations.Wildcard)]
    [InlineData("keyword:x~", QueryOperations.Fuzzy)]
    [InlineData("_missing_:keyword", QueryOperations.Missing)]
    [InlineData("*:*", QueryOperations.MatchAll)]
    public void ValidateQuery_WithRestrictedOperation_RejectsOperation(string query, string operation)
    {
        var options = new QueryValidationOptions();
        options.RestrictedOperations.Add(operation);
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        var result = parser.ValidateQuery(query);

        Assert.False(result.IsValid);
        Assert.Equal(QueryErrorCode.OperationRestricted, Assert.Single(result.ValidationErrors).Code);
        Assert.True(result.Operations.ContainsKey(operation));
    }

    [Theory]
    [InlineData("keyword:x", true)]
    [InlineData("(keyword:x)", true)]
    [InlineData("((keyword:x))", false)]
    [InlineData("keyword:((x))", false)]
    public void ValidateQuery_WithMaxNodeDepth_RejectsDeeperQueries(string query, bool isValid)
    {
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = new QueryValidationOptions { AllowedMaxNodeDepth = 2 });

        var result = parser.ValidateQuery(query);

        Assert.Equal(isValid, result.IsValid);
    }

    [Fact]
    public void ValidateQuery_WithValidQuery_ReportsReferencedFieldsAndOperations()
    {
        var parser = TestMapping.CreateParser(c => c.FieldMap = new FieldMap { { "user", "keyword" } });

        var result = parser.ValidateQuery("user:x obj.name:y* number:[1 TO 5] _exists_:date (text:\"a b\")");

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(["date", "number", "obj.name", "text", "user"], result.ReferencedFields.Order(StringComparer.Ordinal));
        Assert.Contains("keyword", result.ResolvedFields);
        Assert.Equal(2, result.MaxNodeDepth);
        Assert.Contains("keyword", result.Operations[QueryOperations.Term]);
        Assert.Contains("obj.name", result.Operations[QueryOperations.Prefix]);
        Assert.Contains("number", result.Operations[QueryOperations.Range]);
        Assert.Contains("date", result.Operations[QueryOperations.Exists]);
        Assert.Contains("text", result.Operations[QueryOperations.Phrase]);
    }

    [Fact]
    public void ValidateQuery_WithSyntaxError_ReturnsInvalidResultWithPosition()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.ValidateQuery("keyword:(a OR");

        Assert.False(result.IsValid);
        Assert.All(result.ValidationErrors, e => Assert.True(e.Position >= 0));
    }

    [Theory]
    [InlineData("number:abc")]
    [InlineData("text:value~9")]
    [InlineData("date:[2024 TO *]^2")]
    public void ValidateQuery_WithBuildTimeError_ReturnsValidResultBecauseBuildIsNotRun(string query)
    {
        var parser = TestMapping.CreateParser();

        var validation = parser.ValidateQuery(query);

        Assert.True(validation.IsValid);
        Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));
    }

    [Fact]
    public void ValidateQuery_WithShouldThrow_ThrowsValidationException()
    {
        var options = new QueryValidationOptions { ShouldThrow = true };
        options.RestrictedFields.Add("keyword");
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        Assert.Throws<QueryValidationException>(() => parser.ValidateQuery("keyword:x"));
        Assert.Throws<QueryValidationException>(() => parser.ValidateSort("keyword"));
        Assert.Throws<QueryValidationException>(() => parser.ValidateAggregations("terms:keyword"));
    }

    [Fact]
    public void ValidateQuery_WithOptionsPerRequest_OverridesConfiguredOptions()
    {
        var restrictive = new QueryValidationOptions();
        restrictive.RestrictedFields.Add("keyword");
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = restrictive);

        var configured = parser.ValidateQuery("keyword:x");
        var overridden = parser.ValidateQuery("keyword:x", new ElasticsearchQueryOptions { ValidationOptions = new QueryValidationOptions() });

        Assert.False(configured.IsValid);
        Assert.True(overridden.IsValid);
    }

    [Theory]
    [InlineData("keyword", true)]
    [InlineData("-keyword +number", true)]
    [InlineData("keyword number date", false)]
    public void ValidateSort_WithMaxSortFields_RejectsLongerSorts(string sort, bool isValid)
    {
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = new QueryValidationOptions { AllowedMaxSortFields = 2 });

        var result = parser.ValidateSort(sort);

        Assert.Equal(isValid, result.IsValid);
    }

    [Theory]
    [InlineData("keyword", true)]
    [InlineData("-keyword", true)]
    [InlineData("secret", false)]
    [InlineData("-hidden", false)]
    [InlineData("SECRET.sub", false)]
    public void ValidateSort_WithRestrictedFields_BlocksRestrictedSorts(string sort, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.FieldMap = new FieldMap { { "hidden", "secret" } };
        });

        var result = parser.ValidateSort(sort);

        Assert.Equal(isValid, result.IsValid);
    }

    [Theory]
    [InlineData("terms:keyword", true)]
    [InlineData("max:number terms:keyword", true)]
    [InlineData("terms:(keyword max:number)", true)]
    [InlineData("cardinality:keyword", false)]
    [InlineData("terms:(keyword cardinality:number)", false)]
    public void ValidateAggregations_WithAllowedOperations_RejectsOtherAggregationTypes(string aggregations, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add(AggregationTypes.Terms);
        options.AllowedOperations.Add(AggregationTypes.Max);
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = options);

        var result = parser.ValidateAggregations(aggregations);

        Assert.Equal(isValid, result.IsValid);
    }

    [Theory]
    [InlineData("terms:keyword", true)]
    [InlineData("terms:secret", false)]
    [InlineData("terms:(keyword max:secret)", false)]
    [InlineData("max:hidden", false)]
    public void ValidateAggregations_WithRestrictedFields_BlocksRestrictedFields(string aggregations, bool isValid)
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var parser = TestMapping.CreateParser(c =>
        {
            c.ValidationOptions = options;
            c.FieldMap = new FieldMap { { "hidden", "secret" } };
        });

        var result = parser.ValidateAggregations(aggregations);

        Assert.Equal(isValid, result.IsValid);
    }

    [Fact]
    public void ValidateAggregations_WithValidExpression_ReportsFieldsAndOperations()
    {
        var parser = TestMapping.CreateParser();

        var result = parser.ValidateAggregations("avg:value cardinality:value sum:value2 terms:(keyword min:number)");

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(["keyword", "number", "value", "value2"], result.ReferencedFields.Order(StringComparer.Ordinal));
        Assert.Equal(["avg", "cardinality", "min", "sum", "terms"], result.Operations.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["value", "value2"], result.Operations[AggregationTypes.Sum].Concat(result.Operations[AggregationTypes.Avg]).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("avg:value")]
    [InlineData("    avg     :   value")]
    public void ValidateAggregations_WithValidExpression_IsValid(string aggregations)
    {
        var parser = new ElasticsearchQueryParser();

        Assert.True(parser.ValidateAggregations(aggregations).IsValid);
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("avg:")]
    [InlineData("foo:value")]
    [InlineData("terms:(!keyword)")]
    public void ValidateAggregations_WithInvalidExpression_IsInvalid(string aggregations)
    {
        var parser = new ElasticsearchQueryParser();

        Assert.False(parser.ValidateAggregations(aggregations).IsValid);
    }
}
