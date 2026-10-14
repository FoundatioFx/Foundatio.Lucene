namespace Foundatio.Lucene.Tests;

public class AggregationExpressionParserTests
{
    [Theory]
    [InlineData("min:a", AggregationTypes.Min, "a")]
    [InlineData("max:a", AggregationTypes.Max, "a")]
    [InlineData("avg:a", AggregationTypes.Avg, "a")]
    [InlineData("sum:a", AggregationTypes.Sum, "a")]
    [InlineData("stats:a", AggregationTypes.Stats, "a")]
    [InlineData("exstats:a", AggregationTypes.ExtendedStats, "a")]
    [InlineData("cardinality:a", AggregationTypes.Cardinality, "a")]
    [InlineData("missing:a", AggregationTypes.Missing, "a")]
    [InlineData("percentiles:a", AggregationTypes.Percentiles, "a")]
    [InlineData("date:a", AggregationTypes.DateHistogram, "a")]
    [InlineData("histogram:a", AggregationTypes.Histogram, "a")]
    [InlineData("geogrid:a", AggregationTypes.GeoGrid, "a")]
    [InlineData("terms:a", AggregationTypes.Terms, "a")]
    [InlineData("TERMS:Status", AggregationTypes.Terms, "Status")]
    [InlineData("terms:data.status", AggregationTypes.Terms, "data.status")]
    [InlineData("terms:\"my field\"", AggregationTypes.Terms, "my field")]
    public void Parse_SimpleAggregation_ReturnsTypeAndField(string expression, string type, string field)
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        var aggregations = AggregationExpressionParser.Parse(expression, result);

        // Assert
        Assert.True(result.IsValid, result.Message);
        var aggregation = Assert.Single(aggregations);
        Assert.Equal(type, aggregation.Type);
        Assert.Equal(field, aggregation.Field);
        Assert.Equal(field, aggregation.OriginalField);
        Assert.Equal($"{type}_{field}", aggregation.Name);
        Assert.False(aggregation.IsGroup);
        Assert.Null(aggregation.Order);
    }

    [Fact]
    public void Parse_TopHits_UsesFixedName()
    {
        var aggregation = Assert.Single(AggregationExpressionParser.Parse("tophits:_", new QueryValidationResult()));

        Assert.Equal(AggregationTypes.TopHits, aggregation.Type);
        Assert.Equal("tophits", aggregation.Name);
    }

    [Fact]
    public void Parse_Modifiers_KeepsRawProximityAndBoostText()
    {
        // Act
        var aggregations = AggregationExpressionParser.Parse("terms:status~10^2 date:created~1d^\"America/Chicago\" histogram:price~0.5", new QueryValidationResult());

        // Assert
        Assert.Equal(3, aggregations.Count);
        Assert.Equal(("10", "2"), (aggregations[0].ProximityText, aggregations[0].BoostText));
        Assert.Equal(("1d", "America/Chicago"), (aggregations[1].ProximityText, aggregations[1].BoostText));
        Assert.Equal("0.5", aggregations[2].ProximityText);
        Assert.Null(aggregations[2].BoostText);
    }

    [Fact]
    public void Parse_GroupForm_ReturnsModifiersAndSubAggregations()
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        var aggregations = AggregationExpressionParser.Parse("terms:(status~10 @include:a @exclude:/test.*/ -@offset:1h @missing:\"n/a\" +min:f -max:g)", result);

        // Assert
        Assert.True(result.IsValid, result.Message);
        var terms = Assert.Single(aggregations);
        Assert.Equal("status", terms.Field);
        Assert.Equal("10", terms.ProximityText);
        Assert.True(terms.IsGroup);
        Assert.Equal(
            [
                new AggregationModifier("include", "a", Position: 17),
                new AggregationModifier("exclude", "test.*", IsRegex: true, Position: 28),
                new AggregationModifier("offset", "1h", IsExcluded: true, Position: 46),
                new AggregationModifier("missing", "n/a", Position: 58)
            ],
            terms.Modifiers);
        Assert.Equal(2, terms.Aggregations.Count);
        Assert.Equal((AggregationTypes.Min, "f", SortDirection.Ascending), (terms.Aggregations[0].Type, terms.Aggregations[0].Field, terms.Aggregations[0].Order!.Value));
        Assert.Equal((AggregationTypes.Max, "g", SortDirection.Descending), (terms.Aggregations[1].Type, terms.Aggregations[1].Field, terms.Aggregations[1].Order!.Value));
    }

    [Fact]
    public void Parse_GroupBoost_AppliesToAggregation()
    {
        var aggregation = Assert.Single(AggregationExpressionParser.Parse("date:(created)^\"Europe/London\"", new QueryValidationResult()));

        Assert.Equal("Europe/London", aggregation.BoostText);
        Assert.True(aggregation.IsGroup);
    }

    [Fact]
    public void Parse_NestedGroups_BuildsTree()
    {
        // Act
        var aggregations = AggregationExpressionParser.Parse("date:(created~1M terms:(status cardinality:user -avg:score)) min:created", new QueryValidationResult());

        // Assert
        Assert.Equal(2, aggregations.Count);
        var date = aggregations[0];
        var terms = Assert.Single(date.Aggregations);
        Assert.Equal("terms_status", terms.Name);
        Assert.Equal(["cardinality_user", "avg_score"], terms.Aggregations.Select(a => a.Name));
        Assert.Equal(SortDirection.Descending, terms.Aggregations[1].Order);
        Assert.Equal("min_created", aggregations[1].Name);
    }

    [Theory]
    [InlineData("-terms:status", SortDirection.Descending)]
    [InlineData("+terms:status", SortDirection.Ascending)]
    public void Parse_TopLevelPrefix_SetsOrder(string expression, SortDirection expected)
    {
        var aggregation = Assert.Single(AggregationExpressionParser.Parse(expression, new QueryValidationResult()));

        Assert.Equal(expected, aggregation.Order);
    }

    [Fact]
    public void Parse_MultipleAggregationsWithOperators_ReturnsAll()
    {
        var aggregations = AggregationExpressionParser.Parse("terms:a OR min:b AND (max:c)", new QueryValidationResult());

        Assert.Equal(["terms_a", "min_b", "max_c"], aggregations.Select(a => a.Name));
    }

    [Fact]
    public void GetModifier_RepeatedName_ReturnsLast()
    {
        var aggregation = Assert.Single(AggregationExpressionParser.Parse("terms:(status @include:a @INCLUDE:b)", new QueryValidationResult()));

        Assert.Equal("b", aggregation.GetModifier("include")!.Value);
        Assert.Equal(["a", "b"], aggregation.GetModifiers("Include").Select(m => m.Value));
        Assert.Null(aggregation.GetModifier("exclude"));
    }

    [Theory]
    [InlineData("bogus:field", "Unknown aggregation type 'bogus'")]
    [InlineData("status", "Unexpected 'status'")]
    [InlineData("@include:saved", "Unknown aggregation type '@include'")]
    [InlineData("terms:a*", "must specify a field")]
    [InlineData("terms:[1 TO 2]", "must specify a field")]
    [InlineData("terms:(@include:a)", "must specify a field")]
    [InlineData("terms:(a b)", "Only one field may be specified")]
    [InlineData("min:(a max:b)", "does not support sub-aggregations")]
    [InlineData("stats:(a terms:b)", "does not support sub-aggregations")]
    [InlineData("NOT terms:a", "Boolean operator (NOT|!) is not supported in aggregation expressions")]
    [InlineData("terms:(a !min:b)", "Boolean operator (NOT|!) is not supported in aggregation expressions")]
    [InlineData("terms:(a NOT min:b)", "Boolean operator (NOT|!) is not supported in aggregation expressions")]
    [InlineData("terms:(a @missing:(b c))", "Aggregation modifier (@missing) must have a value")]
    [InlineData("terms:(a bogus:b)", "Unknown aggregation type 'bogus'")]
    public void Parse_InvalidExpression_ReturnsError(string expression, string message)
    {
        // Arrange
        var result = new QueryValidationResult();

        // Act
        AggregationExpressionParser.Parse(expression, result);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Parse_UnknownType_UsesOperationNotAllowedCode()
    {
        var result = new QueryValidationResult();

        AggregationExpressionParser.Parse("bogus:field", result);

        Assert.Equal(QueryErrorCode.OperationNotAllowed, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void Parse_SyntaxError_AddsParseErrors()
    {
        var result = new QueryValidationResult();

        AggregationExpressionParser.Parse("terms:(status", result);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AggregationExpressionParser.Parse(null!, new QueryValidationResult()));
        Assert.Throws<ArgumentNullException>(() => AggregationExpressionParser.Parse("terms:a", null!));
        Assert.Throws<ArgumentNullException>(() => AggregationExpressionParser.FromDocument(null!, new QueryValidationResult()));
    }

    [Theory]
    [InlineData("terms", true, true)]
    [InlineData("DATE", true, true)]
    [InlineData("histogram", true, true)]
    [InlineData("geogrid", true, true)]
    [InlineData("missing", true, true)]
    [InlineData("min", true, false)]
    [InlineData("tophits", true, false)]
    [InlineData("bogus", false, false)]
    public void AggregationTypes_KnownAndBucketTypes_AreClassified(string type, bool known, bool bucket)
    {
        Assert.Equal(known, AggregationTypes.IsKnown(type));
        Assert.Equal(bucket, AggregationTypes.IsBucket(type));
    }

    [Fact]
    public void AggregationExpression_Constructor_ValidatesArguments()
    {
        var aggregation = new AggregationExpression("terms", "");

        Assert.Equal("terms:", aggregation.ToString());
        Assert.Throws<ArgumentException>(() => new AggregationExpression("", "a"));
        Assert.Throws<ArgumentNullException>(() => new AggregationExpression("terms", null!));
    }
}
