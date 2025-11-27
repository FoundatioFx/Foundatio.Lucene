using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

namespace Foundatio.LuceneQueryParser.Tests;

public class DateMathEvaluatorVisitorTests
{
    private readonly DateTimeOffset _fixedTime = new(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);

    #region Explicit Date Math Tests

    [Fact]
    public async Task EvaluatesExplicitDateWithPipeOperator()
    {
        // Elasticsearch standard format: 2024-01-01||+1M/d
        var result = LuceneQuery.Parse("timestamp:2024-01-01||+1M/d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        // 2024-01-01 + 1 month = 2024-02-01, rounded to start of day
        Assert.Contains("2024-02-01", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithoutPipeOperator()
    {
        // Simplified format: 2024-01-01+1M/d (no ||)
        var result = LuceneQuery.Parse("timestamp:2024-01-01+1M/d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        // 2024-01-01 + 1 month = 2024-02-01, rounded to start of day
        Assert.Contains("2024-02-01", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithAddDays()
    {
        var result = LuceneQuery.Parse("timestamp:2024-01-15||+10d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-01-25", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithSubtractDays()
    {
        var result = LuceneQuery.Parse("timestamp:2024-01-15||-5d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-01-10", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithMultipleOperations()
    {
        // 2024-01-01 + 1 month + 5 days, rounded to day
        var result = LuceneQuery.Parse("timestamp:2024-01-01||+1M+5d/d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-02-06", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithYearRounding()
    {
        var result = LuceneQuery.Parse("timestamp:2024-06-15||/y");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        // Rounded to start of year
        Assert.Contains("2024-01-01", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithMonthRounding()
    {
        var result = LuceneQuery.Parse("timestamp:2024-06-15||/M");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        // Rounded to start of month
        Assert.Contains("2024-06-01", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesExplicitDateTimeWithOperations()
    {
        // Full datetime with operations
        var result = LuceneQuery.Parse("timestamp:2024-01-01T10:30:00Z||+2h");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-01-01", term.Term);
        Assert.Contains("12:30:00", term.Term); // 10:30 + 2h = 12:30
    }

    [Fact]
    public async Task EvaluatesExplicitDateInRangeQuery()
    {
        // Range with explicit date math on both sides
        var result = LuceneQuery.Parse("timestamp:[2024-01-01||/M TO 2024-01-01||+1M/M]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.Contains("2024-01-01", range.Min!); // Start of January
        Assert.Contains("2024-02", range.Max!); // End of February (upper limit rounding)
    }

    [Fact]
    public async Task EvaluatesExplicitDateWithWeekRounding()
    {
        // 2024-06-15 is a Saturday, start of week (Monday) should be 2024-06-10
        var result = LuceneQuery.Parse("timestamp:2024-06-15||/w");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-10", term.Term); // Monday of that week
    }

    [Fact]
    public async Task EvaluatesSimplifiedDateMathWithSubtract()
    {
        // Simplified format without ||
        var result = LuceneQuery.Parse("timestamp:2024-06-15-7d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-08", term.Term);
    }

    [Fact]
    public async Task EvaluatesSimplifiedDateMathWithRoundingOnly()
    {
        // Simplified format with just rounding: 2024-01-15/M -> start of January
        var result = LuceneQuery.Parse("timestamp:2024-01-15/M");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-01-01", term.Term);
        Assert.Contains("00:00:00", term.Term);
    }

    [Theory]
    [InlineData("2024-01-01||+1y", "2025-01-01")]
    [InlineData("2024-01-01||+6M", "2024-07-01")]
    [InlineData("2024-01-01||+2w", "2024-01-15")]
    [InlineData("2024-01-15||+10d", "2024-01-25")]
    [InlineData("2024-01-01||+5h", "2024-01-01")]
    [InlineData("2024-01-01||+30m", "2024-01-01")]
    [InlineData("2024-01-01||+45s", "2024-01-01")]
    public async Task EvaluatesExplicitDateWithAllTimeUnits(string expression, string expectedDatePart)
    {
        var result = LuceneQuery.Parse($"timestamp:{expression}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = await visitor.EvaluateAsync(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains(expectedDatePart, term.Term);
    }

    #endregion

    [Fact]
    public async Task EvaluatesNowInTermNode()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var fieldNode = evaluated as QueryDocument;
        Assert.NotNull(fieldNode?.Query);
        var field = fieldNode.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-15", term.Term);
        Assert.Contains("12:30:00", term.Term);
    }

    [Fact]
    public async Task EvaluatesNowMinusOneDayInTermNode()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now-1d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var fieldNode = evaluated as QueryDocument;
        Assert.NotNull(fieldNode?.Query);
        var field = fieldNode.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-14", term.Term); // One day before
    }

    [Fact]
    public async Task EvaluatesNowPlusOneHourInTermNode()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now+1h");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var fieldNode = evaluated as QueryDocument;
        Assert.NotNull(fieldNode?.Query);
        var field = fieldNode.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-15", term.Term);
        Assert.Contains("13:30:00", term.Term); // One hour later
    }

    [Fact]
    public async Task EvaluatesNowRoundedToDay()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now/d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var fieldNode = evaluated as QueryDocument;
        Assert.NotNull(fieldNode?.Query);
        var field = fieldNode.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-15", term.Term);
        Assert.Contains("00:00:00", term.Term); // Start of day
    }

    [Fact]
    public async Task EvaluatesDateMathInRangeWithExplicitDate()
    {
        // Arrange - Use date math in a range context where dates are properly parsed
        var result = LuceneQuery.Parse("timestamp:[2024-01-01 TO now+1M]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var fieldNode = evaluated as QueryDocument;
        Assert.NotNull(fieldNode?.Query);
        var field = fieldNode.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.Equal("2024-01-01", range.Min); // Static date unchanged
        Assert.Contains("2024-07-15", range.Max); // now+1M evaluated (June 15 + 1 month)
    }

    [Fact]
    public async Task EvaluatesRangeNodeMinAndMax()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:[now-7d TO now]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.Contains("2024-06-08", range.Min); // 7 days before
        Assert.Contains("2024-06-15", range.Max); // Now (end of period due to isUpperLimit)
    }

    [Fact]
    public async Task EvaluatesGreaterThanOperator()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:>now-1d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.Contains("2024-06-14", range.Min ?? range.Max);
    }

    [Fact]
    public async Task EvaluatesLessThanOperatorAsUpperLimit()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:<now/d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        // For < with /d rounding and isUpperLimit=true, should round to end of day
        var value = range.Min ?? range.Max;
        Assert.NotNull(value);
        Assert.Contains("2024-06-15", value);
        Assert.Contains("23:59:59", value); // End of day due to upper limit rounding
    }

    [Fact]
    public async Task DoesNotModifyNonDateMathTerms()
    {
        // Arrange
        var result = LuceneQuery.Parse("status:active");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Equal("active", term.Term);
    }

    [Fact]
    public async Task DoesNotModifyRegularDateStrings()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:2024-01-01");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Equal("2024-01-01", term.Term); // Unchanged
    }

    [Fact]
    public async Task EvaluatesComplexBooleanQuery()
    {
        // Arrange
        var result = LuceneQuery.Parse("status:active AND created:[now-30d TO now] AND updated:>now-7d");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert - Just verify it doesn't throw and produces a result
        Assert.NotNull(evaluated);
        
        // Convert back to string and verify DateMath expressions are resolved
        var builder = new QueryStringBuilder();
        var queryString = builder.Visit(evaluated);
        
        Assert.DoesNotContain("now", queryString);
        Assert.Contains("2024", queryString); // Contains evaluated dates
    }

    [Fact]
    public async Task UsesTimeZoneWhenProvided()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now");
        var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var visitor = new DateMathEvaluatorVisitor(pacificZone);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        // Should contain a timezone offset (Pacific is -07:00 or -08:00 depending on DST)
        Assert.Matches(@"-0[78]:00", term.Term);
    }

    [Fact]
    public async Task PreservesWildcardRangeBoundaries()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:[now-7d TO *]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = await visitor.EvaluateAsync(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.Contains("2024-06-08", range.Min);
        Assert.Null(range.Max); // Wildcard preserved as null
    }

    [Fact]
    public async Task StaticEvaluateMethodWorks()
    {
        // Arrange
        var result = LuceneQuery.Parse("timestamp:now-1d");

        // Act
        var evaluated = await DateMathEvaluatorVisitor.EvaluateAsync(result.Document!, null, _fixedTime);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-14", term.Term);
    }
}
