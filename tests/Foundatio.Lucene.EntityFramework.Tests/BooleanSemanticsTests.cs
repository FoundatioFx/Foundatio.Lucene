using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Boolean semantics follow Lucene: every required clause must match, no prohibited clause may match, and when there
/// are no required clauses at least one optional clause must match. Flag rows exist for every combination of A, B,
/// and C, with <c>Id = A*4 + B*2 + C</c>.
/// </summary>
public class BooleanSemanticsTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("a:true b:true", new[] { 6, 7 })]
    [InlineData("a:true AND b:true", new[] { 6, 7 })]
    [InlineData("a:true OR b:true", new[] { 2, 3, 4, 5, 6, 7 })]
    [InlineData("-a:true b:true", new[] { 2, 3 })]
    [InlineData("+a:true b:true", new[] { 6, 7 })]
    [InlineData("a:true OR NOT b:true", new[] { 0, 1, 4, 5, 6, 7 })]
    [InlineData("NOT a:true OR b:true", new[] { 0, 1, 2, 3, 6, 7 })]
    [InlineData("a:true OR -b:true", new[] { 4, 5 })]
    [InlineData("NOT a:true", new[] { 0, 1, 2, 3 })]
    [InlineData("!a:true", new[] { 0, 1, 2, 3 })]
    [InlineData("-a:true", new[] { 0, 1, 2, 3 })]
    [InlineData("-a:true -b:true", new[] { 0, 1 })]
    [InlineData("a:true AND NOT b:true", new[] { 4, 5 })]
    [InlineData("a:true AND b:true OR c:true", new[] { 1, 3, 5, 6, 7 })]
    [InlineData("a:true OR b:true AND c:true", new[] { 3, 4, 5, 6, 7 })]
    [InlineData("(a:true OR b:true) AND -c:true", new[] { 2, 4, 6 })]
    [InlineData("NOT (a:true OR b:true)", new[] { 0, 1 })]
    [InlineData("-(a:true b:true)", new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData("a:true -a:true", new int[0])]
    [InlineData("a:(true OR false) -c:true", new[] { 0, 2, 4, 6 })]
    [InlineData("a:(true -false)", new[] { 4, 5, 6, 7 })]
    [InlineData("a:(-true)", new[] { 0, 1, 2, 3 })]
    [InlineData("*:*", new[] { 0, 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData("*", new[] { 0, 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData("-*:*", new int[0])]
    [InlineData("", new[] { 0, 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData("((a:true))", new[] { 4, 5, 6, 7 })]
    public void BuildFilter_WithDefaultAndOperator_FollowsLuceneBooleanSemantics(string query, int[] expected)
    {
        var parser = new EntityFrameworkQueryParser();

        Assert.Equal(expected, _db.Flags.Where(query, parser).Ids());
        Assert.Equal(expected, _db.Flags.Where(parser.BuildFilter<Flag>(query, new EntityFrameworkQueryOptions { Model = _db.Model })).Ids());
    }

    [Theory]
    [InlineData("a:true b:true", new[] { 2, 3, 4, 5, 6, 7 })]
    [InlineData("-a:true b:true", new[] { 2, 3 })]
    [InlineData("+a:true b:true", new[] { 4, 5, 6, 7 })]
    [InlineData("+a:true +b:true c:true", new[] { 6, 7 })]
    [InlineData("a:true b:true -c:true", new[] { 2, 4, 6 })]
    [InlineData("a:true OR NOT b:true", new[] { 0, 1, 4, 5, 6, 7 })]
    [InlineData("a:true b:true AND c:true", new[] { 3, 4, 5, 6, 7 })]
    public void BuildFilter_WithDefaultOrOperator_FollowsLuceneBooleanSemantics(string query, int[] expected)
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultOperator(BooleanOperator.Or));
        var perRequest = new EntityFrameworkQueryParser();

        Assert.Equal(expected, _db.Flags.Where(query, parser).Ids());
        Assert.Equal(expected, _db.Flags.Where(query, perRequest, new EntityFrameworkQueryOptions { DefaultOperator = BooleanOperator.Or }).Ids());
    }

    [Fact]
    public void BuildFilter_WithPureNegativeQuery_MatchesEverythingExcept()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model));

        var filter = parser.BuildFilter<Flag>("-a:true -c:true");

        Assert.Equal([0, 2], _db.Flags.Where(filter).Ids());
    }

    [Fact]
    public void BuildFilter_WithMustClause_IgnoresShouldClauses()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseModel(_db.Model).SetDefaultOperator(BooleanOperator.Or));

        var filter = parser.BuildFilter<Flag>("+a:true b:true c:true");

        Assert.Contains("e.A", filter.ToString());
        Assert.DoesNotContain("e.B", filter.ToString());
        Assert.DoesNotContain("e.C", filter.ToString());
    }
}
