namespace Foundatio.Lucene.Parity.Tests;

/// <summary>
/// Strict cross-engine parity gate: the same Lucene query must return the same result set from
/// SQL Server (via the EntityFramework parser) and Elasticsearch, for every construct both engines
/// support. Constructs SQL cannot honor (fuzzy, regex, proximity) are intentionally excluded — they
/// are covered as engine-specific behavior elsewhere.
/// </summary>
[Collection("CrossEngine")]
public class CrossEngineParityTests(CrossEngineFixture fixture)
{
    public static IEnumerable<object[]> ParityQueries() =>
    [
        ["*:*", new[] { 1, 2, 3, 4, 5 }],
        ["age:[30 TO 40]", new[] { 1, 2, 3 }],
        ["age:>35", new[] { 3, 5 }],
        ["salary:<90000", new[] { 1, 4 }],
        ["category:engineering", new[] { 1, 4 }],
        ["name:alpha", new[] { 1 }],
        ["category:research AND active:true", new[] { 3 }],
        ["category:engineering OR category:sales", new[] { 1, 2, 4 }],
        ["NOT active:true", new[] { 4, 5 }],
        ["_exists_:notes", new[] { 1, 3, 5 }],
        ["created:[2020-01-01 TO 2021-12-31]", new[] { 1, 5 }],
    ];

    [Theory]
    [MemberData(nameof(ParityQueries))]
    public async Task Query_ReturnsSameResultSetOnSqlAndElasticsearch(string query, int[] expectedIds)
    {
        var expected = expectedIds.OrderBy(id => id).ToArray();

        var sqlIds = fixture.QuerySql(query).OrderBy(id => id).ToArray();
        var esIds = (await fixture.QueryElasticsearchAsync(query)).OrderBy(id => id).ToArray();

        Assert.Equal(expected, sqlIds);
        Assert.Equal(expected, esIds);
    }
}
