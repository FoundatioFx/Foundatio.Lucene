using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class CleanupQueryVisitorTests
{
    [Theory]
    [InlineData("value", "value")]
    [InlineData("(value)", "value")]
    [InlineData("((value))", "value")]
    [InlineData("(((value)))", "value")]
    [InlineData("((value )    )", "value")]
    [InlineData("test:(value)", "test:(value)")]
    [InlineData("test:((value))", "test:(value)")]
    [InlineData("NOT value", "NOT value")]
    [InlineData("NOT (value)", "NOT value")]
    [InlineData("NOT (status:fixed)", "NOT status:fixed")]
    [InlineData("project:123 NOT (status:open OR status:regressed)", "project:123 NOT (status:open OR status:regressed)")]
    [InlineData("((a OR b))", "(a OR b)")]
    [InlineData("(a)^2", "(a)^2")]
    public void Run_Query_Simplifies(string query, string expected)
    {
        Assert.Equal(expected, CleanupQueryVisitor.Run(query));
    }
}
