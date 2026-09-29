using System.Globalization;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Tests;

public class DateMathEvaluatorVisitorTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 15, 14, 30, 45, 123, TimeSpan.Zero);

    [Theory]
    [InlineData("created:now", "created:2024-06-15T14:30:45.123+00:00")]
    [InlineData("created:now-1d/d", "created:2024-06-14T00:00:00.000+00:00")]
    [InlineData("created:now+1h", "created:2024-06-15T15:30:45.123+00:00")]
    [InlineData("created:2024-01-15||+1M/d", "created:2024-02-15T00:00:00.000+00:00")]
    [InlineData("now", "2024-06-15T14:30:45.123+00:00")]
    [InlineData("created:[now-1d/d TO now/d]", "created:[2024-06-14T00:00:00.000+00:00 TO 2024-06-15T23:59:59.999+00:00]")]
    [InlineData("created:{now/d TO now/d}", "created:{2024-06-15T23:59:59.999+00:00 TO 2024-06-15T00:00:00.000+00:00}")]
    [InlineData("created:[now/d TO now/d}", "created:[2024-06-15T00:00:00.000+00:00 TO 2024-06-15T00:00:00.000+00:00}")]
    [InlineData("created:{now/d TO now/d]", "created:{2024-06-15T23:59:59.999+00:00 TO 2024-06-15T23:59:59.999+00:00]")]
    [InlineData("created:>now/d", "created:{2024-06-15T23:59:59.999+00:00 TO *]")]
    [InlineData("created:>=now/d", "created:[2024-06-15T00:00:00.000+00:00 TO *]")]
    [InlineData("created:<now/M", "created:[* TO 2024-06-01T00:00:00.000+00:00}")]
    [InlineData("created:<=now/M", "created:[* TO 2024-06-30T23:59:59.999+00:00]")]
    [InlineData("created:[2024-01-01||+1M/d TO 2024-03-01||/M]", "created:[2024-02-01T00:00:00.000+00:00 TO 2024-03-31T23:59:59.999+00:00]")]
    [InlineData("created:[* TO now]", "created:[* TO 2024-06-15T14:30:45.123+00:00]")]
    [InlineData("a:now AND (b:now-1h OR NOT c:now/d)", "(bool +a:2024-06-15T14:30:45.123+00:00 +(group (bool ?b:2024-06-15T13:30:45.123+00:00 ?(not c:2024-06-15T00:00:00.000+00:00))))")]
    public void Accept_DateMath_IsEvaluated(string query, string expected)
    {
        // Arrange
        var visitor = new DateMathEvaluatorVisitor(new FakeTimeProvider(Now));

        // Act
        var result = visitor.Run(LuceneQuery.Parse(query).Document);

        // Assert
        Assert.Equal(expected, result.ToDebugString());
    }

    [Theory]
    [InlineData("status:active")]
    [InlineData("status:nowhere")]
    [InlineData("created:2024-01-15")]
    [InlineData("created:[2024-01-01 TO 2024-02-01]")]
    [InlineData("created:now*")]
    [InlineData("created:now+1x")]
    [InlineData("created:\"now\"")]
    [InlineData("created:/now/")]
    public void Accept_NonDateMathValues_AreUnchanged(string query)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;
        string before = document.ToDebugString();

        // Act
        var result = new DateMathEvaluatorVisitor(Now).Run(document);

        // Assert
        Assert.Equal(before, result.ToDebugString());
    }

    [Fact]
    public void Accept_MultipleExpressions_ReadsClockOncePerDocument()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now) { AutoAdvanceAmount = TimeSpan.FromMinutes(1) };
        var visitor = new DateMathEvaluatorVisitor(clock);

        // Act
        var first = visitor.Run(LuceneQuery.Parse("a:now b:now c:[now TO now]").Document).ToDebugString();
        var second = visitor.Run(LuceneQuery.Parse("a:now").Document).ToDebugString();

        // Assert
        Assert.Equal("(bool +a:2024-06-15T14:30:45.123+00:00 +b:2024-06-15T14:30:45.123+00:00 +c:[2024-06-15T14:30:45.123+00:00 TO 2024-06-15T14:30:45.123+00:00])", first);
        Assert.Equal("a:2024-06-15T14:31:45.123+00:00", second);
    }

    [Fact]
    public void Accept_NoTimeProvider_UsesContextTimeProvider()
    {
        // Arrange
        var context = new QueryVisitorContext { TimeProvider = new FakeTimeProvider(Now) };

        // Act
        var result = new DateMathEvaluatorVisitor().Run(LuceneQuery.Parse("created:now/d").Document, context);

        // Assert
        Assert.Equal("created:2024-06-15T00:00:00.000+00:00", result.ToDebugString());
    }

    [Fact]
    public void Accept_IsDateField_OnlyEvaluatesMatchingFields()
    {
        // Arrange
        var visitor = new DateMathEvaluatorVisitor(new FakeTimeProvider(Now), isDateField: f => f.Equals("created", StringComparison.OrdinalIgnoreCase));

        // Act
        var result = visitor.Run(LuceneQuery.Parse("CREATED:now/d name:now now created:(now OR [now/d TO *]) other:[now TO *]").Document);

        // Assert
        Assert.Equal(
            "(bool +CREATED:2024-06-15T00:00:00.000+00:00 +name:now +now +created:(group (bool ?2024-06-15T14:30:45.123+00:00 ?[2024-06-15T00:00:00.000+00:00 TO *])) +other:[now TO *])",
            result.ToDebugString());
    }

    [Fact]
    public void Accept_TimeZone_EvaluatesInZone()
    {
        // Arrange
        var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var visitor = new DateMathEvaluatorVisitor(new FakeTimeProvider(Now), chicago);

        // Act
        var result = visitor.Run(LuceneQuery.Parse("created:[now/d TO now/d] other:2024-01-15||/d").Document);

        // Assert
        Assert.Equal("(bool +created:[2024-06-15T00:00:00.000-05:00 TO 2024-06-15T23:59:59.999-05:00] +other:2024-01-15T00:00:00.000-06:00)", result.ToDebugString());
    }

    [Fact]
    public void Accept_CustomFormat_UsesFormat()
    {
        var visitor = new DateMathEvaluatorVisitor(Now, dateFormat: "yyyy-MM-dd");

        var result = visitor.Run(LuceneQuery.Parse("created:[now-1d TO now]").Document);

        Assert.Equal("created:[2024-06-14 TO 2024-06-15]", result.ToDebugString());
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    public void Accept_NonInvariantCurrentCulture_FormatsInvariantly(string culture)
    {
        // Arrange
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            // Act
            var result = new DateMathEvaluatorVisitor(Now).Run(LuceneQuery.Parse("created:now/d").Document);

            // Assert
            Assert.Equal("created:2024-06-15T00:00:00.000+00:00", result.ToDebugString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Accept_EvaluatedTerm_RoundTripsThroughQueryString()
    {
        // Arrange
        var result = new DateMathEvaluatorVisitor(Now).Run(LuceneQuery.Parse("created:now").Document);

        // Act
        string query = QueryStringBuilder.ToQueryString(result);
        var reparsed = LuceneQuery.Parse(query);

        // Assert
        Assert.True(reparsed.IsSuccess, string.Join("; ", reparsed.Errors));
        var term = Assert.IsType<TermNode>(((FieldQueryNode)reparsed.Document.Query!).Query);
        Assert.Equal("2024-06-15T14:30:45.123+00:00", term.UnescapedTerm);
    }

    [Fact]
    public void Evaluate_StaticHelper_UsesFixedNow()
    {
        var result = DateMathEvaluatorVisitor.Evaluate(LuceneQuery.Parse("created:now-1d/d").Document, Now);

        Assert.Equal("created:2024-06-14T00:00:00.000+00:00", result.ToDebugString());
    }

    [Fact]
    public void Evaluate_NonDocumentNode_IsEvaluated()
    {
        var range = LuceneQuery.Parse("created:[now/d TO now/d]").Document.Query is FieldQueryNode { Query: RangeNode node } ? node : throw new InvalidOperationException();

        var result = DateMathEvaluatorVisitor.Evaluate(range, Now);

        Assert.Equal("[2024-06-15T00:00:00.000+00:00 TO 2024-06-15T23:59:59.999+00:00]", result.ToDebugString());
    }

    [Fact]
    public void Constructor_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DateMathEvaluatorVisitor(dateFormat: ""));
        Assert.Throws<ArgumentNullException>(() => DateMathEvaluatorVisitor.Evaluate(null!, Now));
    }

    [Fact]
    public void Accept_SharedVisitorConcurrently_ProducesConsistentResults()
    {
        // Arrange
        var visitor = new DateMathEvaluatorVisitor(isDateField: f => f.StartsWith('d'));
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act
        Parallel.For(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var now = Now.AddDays(i);
            var context = new QueryVisitorContext { TimeProvider = new FakeTimeProvider(now) };
            string result = visitor.Run(LuceneQuery.Parse("d:now/d x:now").Document, context).ToDebugString();
            string expected = $"(bool +d:{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}T00:00:00.000+00:00 +x:now)";
            if (result != expected)
                failures.Add(result);
        });

        // Assert
        Assert.Empty(failures);
    }
}
