using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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

        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        // For < (exclusive upper) with /d rounding, should floor-round to start of day
        // Meaning "less than start of day" — correct Elasticsearch lt behavior
        var value = range.Min ?? range.Max;
        Assert.NotNull(value);
        Assert.Contains("2024-06-15", value);
        Assert.Contains("00:00:00", value); // Start of day (floor for exclusive upper)
    }

    [Fact]
    public async Task DoesNotModifyNonDateMathTerms()
    {
        // Arrange
        var result = LuceneQuery.Parse("status:active");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        // Act
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = visitor.Evaluate(result.Document!);

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
        var evaluated = DateMathEvaluatorVisitor.Evaluate(result.Document!, null, _fixedTime);

        // Assert
        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var term = field.Query as TermNode;
        Assert.NotNull(term);
        Assert.Contains("2024-06-14", term.Term);
    }

    #region Inclusive/Exclusive Range with Date Math Rounding Tests

    [Fact]
    public async Task InclusiveRange_DateMathRounding_RoundsMinDownAndMaxUp()
    {
        // [now/d TO now/d] — inclusive on both sides
        // Min (inclusive): rounds to start of day (isUpperLimit=false)
        // Max (inclusive): rounds to end of day (isUpperLimit=true)
        var result = LuceneQuery.Parse("timestamp:[now/d TO now/d]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.True(range.MaxInclusive);
        Assert.Contains("2024-06-15", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of day
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("23:59:59", range.Max); // End of day
    }

    [Fact]
    public async Task InclusiveExclusiveRange_DateMathRounding_RoundsMaxDown()
    {
        // [now/d TO now/d} — inclusive min, exclusive max
        // Min (inclusive): rounds to start of day (isUpperLimit=false)
        // Max (exclusive): rounds to start of day (isUpperLimit=false) so < start_of_day
        var result = LuceneQuery.Parse("timestamp:[now/d TO now/d}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("2024-06-15", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of day
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("00:00:00", range.Max); // Start of day (floor for exclusive upper)
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_DateMathRounding_RoundsMinUp()
    {
        // {now/d TO now/d] — exclusive min, inclusive max
        // Min (exclusive): rounds to end of day (isUpperLimit=true) so > end_of_day
        // Max (inclusive): rounds to end of day (isUpperLimit=true)
        var result = LuceneQuery.Parse("timestamp:{now/d TO now/d]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.False(range.MinInclusive);
        Assert.True(range.MaxInclusive);
        Assert.Contains("2024-06-15", range.Min);
        Assert.Contains("23:59:59", range.Min); // End of day (ceiling for exclusive lower)
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("23:59:59", range.Max); // End of day
    }

    [Fact]
    public async Task ExclusiveRange_DateMathRounding_RoundsMinUpAndMaxDown()
    {
        // {now/d TO now/d} — exclusive on both sides
        // Min (exclusive): rounds to end of day (isUpperLimit=true)
        // Max (exclusive): rounds to start of day (isUpperLimit=false)
        var result = LuceneQuery.Parse("timestamp:{now/d TO now/d}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.False(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("2024-06-15", range.Min);
        Assert.Contains("23:59:59", range.Min); // End of day (ceiling for exclusive lower)
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("00:00:00", range.Max); // Start of day (floor for exclusive upper)
    }

    [Fact]
    public async Task InclusiveExclusiveRange_MonthRounding_RoundsCorrectly()
    {
        // [now/M TO now/M} — inclusive min, exclusive max with month rounding
        // Min (inclusive): rounds to start of month (isUpperLimit=false)
        // Max (exclusive): rounds to start of month (isUpperLimit=false)
        var result = LuceneQuery.Parse("timestamp:[now/M TO now/M}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("2024-06-01", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of month
        Assert.Contains("2024-06-01", range.Max);
        Assert.Contains("00:00:00", range.Max); // Start of month (floor for exclusive upper)
    }

    [Fact]
    public async Task InclusiveRange_MonthRounding_RoundsMinToStartMaxToEnd()
    {
        // [now/M TO now/M] — inclusive on both sides with month rounding
        // Min (inclusive): rounds to start of month
        // Max (inclusive): rounds to end of month
        var result = LuceneQuery.Parse("timestamp:[now/M TO now/M]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.True(range.MaxInclusive);
        Assert.Contains("2024-06-01", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of month
        Assert.Contains("2024-06-30", range.Max); // End of June
        Assert.Contains("23:59:59", range.Max); // End of day
    }

    [Fact]
    public async Task InclusiveExclusiveRange_DifferentDates_RoundsCorrectly()
    {
        // [now-7d/d TO now/d} — common pattern: "last 7 full days"
        // Min (inclusive): now-7d rounded to start of day
        // Max (exclusive): now rounded to start of day
        var result = LuceneQuery.Parse("timestamp:[now-7d/d TO now/d}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("2024-06-08", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of day 7 days ago
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("00:00:00", range.Max); // Start of today (exclusive upper → floor)
    }

    [Fact]
    public async Task ExclusiveInclusiveRange_DifferentDates_RoundsCorrectly()
    {
        // {now-7d/d TO now/d] — exclusive lower, inclusive upper
        // Min (exclusive): now-7d rounded to end of day
        // Max (inclusive): now rounded to end of day
        var result = LuceneQuery.Parse("timestamp:{now-7d/d TO now/d]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.False(range.MinInclusive);
        Assert.True(range.MaxInclusive);
        Assert.Contains("2024-06-08", range.Min);
        Assert.Contains("23:59:59", range.Min); // End of day 7 days ago (ceiling for exclusive lower)
        Assert.Contains("2024-06-15", range.Max);
        Assert.Contains("23:59:59", range.Max); // End of today
    }

    [Fact]
    public async Task InclusiveExclusiveRange_HourRounding_RoundsCorrectly()
    {
        // [now/h TO now/h} — inclusive min, exclusive max with hour rounding
        // Min (inclusive): rounds to start of hour (12:00:00)
        // Max (exclusive): rounds to start of hour (12:00:00)
        var result = LuceneQuery.Parse("timestamp:[now/h TO now/h}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        // _fixedTime is 12:30, so /h floor = 12:00:00, /h ceiling = 12:59:59
        Assert.Contains("12:00:00", range.Min); // Start of hour
        Assert.Contains("12:00:00", range.Max); // Start of hour (floor for exclusive upper)
    }

    [Fact]
    public async Task ExclusiveRange_HourRounding_RoundsMinUpMaxDown()
    {
        // {now/h TO now/h} — exclusive on both sides with hour rounding
        // Min (exclusive): rounds to end of hour (12:59:59)
        // Max (exclusive): rounds to start of hour (12:00:00)
        var result = LuceneQuery.Parse("timestamp:{now/h TO now/h}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.False(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("12:59:59", range.Min); // End of hour (ceiling for exclusive lower)
        Assert.Contains("12:00:00", range.Max); // Start of hour (floor for exclusive upper)
    }

    [Fact]
    public async Task InclusiveExclusiveRange_ExplicitDate_RoundsCorrectly()
    {
        // [2024-01-01||/M TO 2024-03-01||/M} — explicit dates with month rounding
        // Min (inclusive): 2024-01-01 rounded to start of month → 2024-01-01T00:00:00
        // Max (exclusive): 2024-03-01 rounded to start of month → 2024-03-01T00:00:00
        var result = LuceneQuery.Parse("timestamp:[2024-01-01||/M TO 2024-03-01||/M}");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
        Assert.Contains("2024-01-01", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of January
        Assert.Contains("2024-03-01", range.Max);
        Assert.Contains("00:00:00", range.Max); // Start of March (floor for exclusive upper)
    }

    [Fact]
    public async Task InclusiveRange_ExplicitDate_RoundsMinDownMaxUp()
    {
        // [2024-01-01||/M TO 2024-03-01||/M] — inclusive on both sides, explicit dates
        // Min (inclusive): start of January
        // Max (inclusive): end of March
        var result = LuceneQuery.Parse("timestamp:[2024-01-01||/M TO 2024-03-01||/M]");
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var evaluated = visitor.Evaluate(result.Document!);

        var doc = evaluated as QueryDocument;
        Assert.NotNull(doc?.Query);
        var field = doc.Query as FieldQueryNode;
        Assert.NotNull(field);
        var range = field.Query as RangeNode;
        Assert.NotNull(range);
        Assert.True(range.MinInclusive);
        Assert.True(range.MaxInclusive);
        Assert.Contains("2024-01-01", range.Min);
        Assert.Contains("00:00:00", range.Min); // Start of January
        Assert.Contains("2024-03-31", range.Max); // End of March
        Assert.Contains("23:59:59", range.Max); // End of day
    }

    #endregion

    #region ES-Compatible Escaped Date Math Tests

    [Fact]
    public async Task EscapedSlash_ShortFormGreaterThan_ParsesSameAsUnescaped()
    {
        // ES query_string requires \/ but our parser should handle both
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var unescaped = LuceneQuery.Parse(@"created:>now/d");
        var escaped = LuceneQuery.Parse(@"created:>now\/d");
        var unescapedDoc = visitor.Evaluate(unescaped.Document!) as QueryDocument;
        var escapedDoc = visitor.Evaluate(escaped.Document!) as QueryDocument;

        var unescapedRange = (unescapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;
        var escapedRange = (escapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;

        Assert.NotNull(unescapedRange);
        Assert.NotNull(escapedRange);
        Assert.Equal(unescapedRange.Min, escapedRange.Min);
        Assert.Equal(unescapedRange.Max, escapedRange.Max);
        Assert.Equal(unescapedRange.MinInclusive, escapedRange.MinInclusive);
        Assert.Equal(unescapedRange.MaxInclusive, escapedRange.MaxInclusive);
    }

    [Fact]
    public async Task EscapedSlash_ShortFormLessThanOrEqual_ParsesSameAsUnescaped()
    {
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var unescaped = LuceneQuery.Parse(@"created:<=now/d");
        var escaped = LuceneQuery.Parse(@"created:<=now\/d");
        var unescapedDoc = visitor.Evaluate(unescaped.Document!) as QueryDocument;
        var escapedDoc = visitor.Evaluate(escaped.Document!) as QueryDocument;

        var unescapedRange = (unescapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;
        var escapedRange = (escapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;

        Assert.NotNull(unescapedRange);
        Assert.NotNull(escapedRange);
        Assert.Equal(unescapedRange.Min, escapedRange.Min);
        Assert.Equal(unescapedRange.Max, escapedRange.Max);
    }

    [Fact]
    public async Task EscapedSlash_BracketRange_ParsesSameAsUnescaped()
    {
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var unescaped = LuceneQuery.Parse(@"created:[now-1d/d TO now/d]");
        var escaped = LuceneQuery.Parse(@"created:[now-1d\/d TO now\/d]");
        var unescapedDoc = visitor.Evaluate(unescaped.Document!) as QueryDocument;
        var escapedDoc = visitor.Evaluate(escaped.Document!) as QueryDocument;

        var unescapedRange = (unescapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;
        var escapedRange = (escapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;

        Assert.NotNull(unescapedRange);
        Assert.NotNull(escapedRange);
        Assert.Equal(unescapedRange.Min, escapedRange.Min);
        Assert.Equal(unescapedRange.Max, escapedRange.Max);
    }

    [Fact]
    public async Task EscapedSlash_DateWithRounding_ParsesSameAsUnescaped()
    {
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var unescaped = LuceneQuery.Parse(@"created:>=2024-01-15||/M");
        var escaped = LuceneQuery.Parse(@"created:>=2024-01-15||\/M");
        var unescapedDoc = visitor.Evaluate(unescaped.Document!) as QueryDocument;
        var escapedDoc = visitor.Evaluate(escaped.Document!) as QueryDocument;

        var unescapedRange = (unescapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;
        var escapedRange = (escapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;

        Assert.NotNull(unescapedRange);
        Assert.NotNull(escapedRange);
        Assert.Equal(unescapedRange.Min, escapedRange.Min);
    }

    [Fact]
    public async Task EscapedSlash_DateWithArithmeticAndRounding_ParsesSameAsUnescaped()
    {
        var visitor = new DateMathEvaluatorVisitor(_fixedTime);

        var unescaped = LuceneQuery.Parse(@"created:>2024-01-15||+1M/d");
        var escaped = LuceneQuery.Parse(@"created:>2024-01-15||+1M\/d");
        var unescapedDoc = visitor.Evaluate(unescaped.Document!) as QueryDocument;
        var escapedDoc = visitor.Evaluate(escaped.Document!) as QueryDocument;

        var unescapedRange = (unescapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;
        var escapedRange = (escapedDoc!.Query as FieldQueryNode)!.Query as RangeNode;

        Assert.NotNull(unescapedRange);
        Assert.NotNull(escapedRange);
        Assert.Equal(unescapedRange.Min, escapedRange.Min);
    }

    #endregion
}
