using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class InvertQueryVisitorTests
{
    private static readonly string[] NonInvertedFields = ["noninvertedfield1", "noninvertedfield2"];

    [Theory]
    [InlineData("value", "(NOT value)")]
    [InlineData("field:value", "(NOT field:value)")]
    [InlineData("NOT field:value", "field:value")]
    [InlineData("-field:value", "field:value")]
    [InlineData("NOT status:fixed", "status:fixed")]
    [InlineData("field1:value field2:value field3:value", "(NOT (field1:value field2:value field3:value))")]
    [InlineData("(field1:value OR field2:value)", "(NOT (field1:value OR field2:value))")]
    [InlineData("status:open OR status:regressed", "(NOT (status:open OR status:regressed))")]
    [InlineData("field:value noninvertedfield1:value", "(NOT field:value) noninvertedfield1:value")]
    [InlineData("noninvertedfield1:value (field1:value OR field2:value)", "noninvertedfield1:value (NOT (field1:value OR field2:value))")]
    [InlineData("noninvertedfield1:value", "noninvertedfield1:value")]
    [InlineData("noninvertedfield1:value1 OR noninvertedfield1:value2", "noninvertedfield1:value1 OR noninvertedfield1:value2")]
    [InlineData("(noninvertedfield1:value)", "(noninvertedfield1:value)")]
    [InlineData("(noninvertedfield1:value AND (noninvertedfield2:value)) field1:value", "(noninvertedfield1:value AND (noninvertedfield2:value)) (NOT field1:value)")]
    [InlineData("noninvertedfield1:value field1:value field2:value field3:value", "noninvertedfield1:value (NOT (field1:value field2:value field3:value))")]
    [InlineData("noninvertedfield1:123 (status:open OR status:regressed) noninvertedfield1:234", "noninvertedfield1:123 (NOT (status:open OR status:regressed)) noninvertedfield1:234")]
    public void Run_Query_ReturnsComplementWithinScope(string query, string expected)
    {
        Assert.Equal(expected, InvertQueryVisitor.Run(query, NonInvertedFields));
    }

    [Theory]
    [InlineData("field1:value noninvertedfield1:value field2:value", "(NOT (field1:value field2:value)) noninvertedfield1:value")]
    [InlineData("(field1:value noninvertedfield1:value) field2:value", "(NOT (field1:value field2:value)) noninvertedfield1:value")]
    [InlineData("first_occurrence:[1609459200000 TO 1609730450521] (noninvertedfield1:537650f3b77efe23a47914f4 (status:open OR status:regressed))",
        "(NOT (first_occurrence:[1609459200000 TO 1609730450521] (status:open OR status:regressed))) noninvertedfield1:537650f3b77efe23a47914f4")]
    public void Run_ScopeFieldsBetweenInvertedClauses_NegatesTheRemainderAsOneUnit(string query, string expected)
    {
        // Foundatio.Parsers inverted each clause separately here, which is not the complement of the query.
        Assert.Equal(expected, InvertQueryVisitor.Run(query, NonInvertedFields));
    }

    [Theory]
    [InlineData("value", "(is_deleted:true OR (NOT value))")]
    [InlineData("noninvertedfield1:value field1:value", "noninvertedfield1:value (is_deleted:true OR (NOT field1:value))")]
    [InlineData("noninvertedfield1:value (field1:value OR field2:value)", "noninvertedfield1:value (is_deleted:true OR (NOT (field1:value OR field2:value)))")]
    [InlineData("noninvertedfield1:value field1:value field2:value field3:value", "noninvertedfield1:value (is_deleted:true OR (NOT (field1:value field2:value field3:value)))")]
    [InlineData("noninvertedfield1:value", "noninvertedfield1:value")]
    public void Run_WithAlternateCriteria_OrsItWithTheInvertedPart(string query, string expected)
    {
        Assert.Equal(expected, InvertQueryVisitor.Run(query, NonInvertedFields, "is_deleted:true"));
    }

    [Fact]
    public void Run_TwiceOnScopedQuery_RestoresOriginalMeaning()
    {
        const string query = "noninvertedfield1:1 status:open type:error";
        string once = InvertQueryVisitor.Run(query, NonInvertedFields);
        string twice = InvertQueryVisitor.Run(once, NonInvertedFields);

        Assert.Equal("noninvertedfield1:1 (status:open type:error)", twice);
    }

    [Fact]
    public void Accept_DefaultOperatorOr_Throws()
    {
        var document = LuceneQuery.Parse("a b").Document;
        var context = new QueryVisitorContext { ParserOptions = new LuceneParserOptions { DefaultOperator = BooleanOperator.Or } };

        Assert.Throws<ArgumentException>(() => new InvertQueryVisitor().Accept(document, context));
    }

    [Fact]
    public void Accept_DoesNotModifyInput()
    {
        var document = LuceneQuery.Parse("noninvertedfield1:1 status:open").Document;
        string before = QueryStringBuilder.ToQueryString(document);

        new InvertQueryVisitor(NonInvertedFields).Accept(document, new QueryVisitorContext());

        Assert.Equal(before, QueryStringBuilder.ToQueryString(document));
    }
}
