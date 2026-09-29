using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class RemoveFieldsQueryVisitorTests
{
    private const string Query = "field1:value field2:value (field3:value OR field4:value (field5:value)) field6:value";

    [Fact]
    public void Run_FieldList_RemovesClauses()
    {
        Assert.Equal("field2:value (field3:value OR field4:value (field5:value)) field6:value", RemoveFieldsQueryVisitor.Run(Query, ["field1"]));
    }

    [Fact]
    public void Run_Predicate_RemovesClausesAndCollapsesLevels()
    {
        Assert.Equal("field1:value field2:value (field4:value (field5:value)) field6:value", RemoveFieldsQueryVisitor.Run(Query, f => f == "field3"));
    }

    [Theory]
    [InlineData("FIELD1:value other:x", "other:x")]
    [InlineData("field1:value", "")]
    [InlineData("(field1:a OR field1:b) other:x", "other:x")]
    [InlineData("_exists_:field1 -field1:x other:y", "other:y")]
    [InlineData("-other:x field1:y", "-other:x")]
    public void Run_RemovesAllUsesOfField(string query, string expected)
    {
        Assert.Equal(expected, RemoveFieldsQueryVisitor.Run(query, ["field1"]));
    }
}
