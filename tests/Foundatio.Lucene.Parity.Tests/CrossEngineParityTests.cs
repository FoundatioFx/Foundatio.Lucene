namespace Foundatio.Lucene.Parity.Tests;

/// <summary>
/// The same query must return the same documents from SQL Server (Entity Framework provider) and Elasticsearch, and
/// both must match the expected ids worked out by hand from the seed data in <see cref="CrossEngineFixture"/>.
/// Constructs only one engine supports (fuzzy, regex, proximity, scoring) are covered by the provider test suites.
/// </summary>
[Collection("CrossEngine")]
public class CrossEngineParityTests(CrossEngineFixture fixture)
{
    public static TheoryData<string, int[]> ParityQueries() => new()
    {
        { "*:*", [1, 2, 3, 4, 5, 6, 7, 8] },
        { "", [1, 2, 3, 4, 5, 6, 7, 8] },
        { "name:alpha", [1] },
        { "category:engineering", [1, 4, 7] },
        { "active:true", [1, 2, 3, 7] },
        { "age:30", [1, 6] },
        { "category:research active:true", [3] },
        { "category:research AND active:true", [3] },
        { "category:engineering OR category:sales", [1, 2, 4, 6, 7] },
        { "category:engineering OR category:sales AND active:true", [1, 2, 4, 7] },
        { "(category:engineering OR category:sales) AND active:true", [1, 2, 7] },
        { "category:engineering AND active:true OR category:research", [1, 3, 5, 7, 8] },
        { "NOT active:true", [4, 5, 6, 8] },
        { "-active:true", [4, 5, 6, 8] },
        { "category:engineering -active:true", [4] },
        { "category:engineering NOT active:true", [4] },
        { "category:engineering OR NOT active:true", [1, 4, 5, 6, 7, 8] },
        { "NOT (category:engineering OR category:sales)", [3, 5, 8] },
        { "-category:(engineering OR sales)", [3, 5, 8] },
        { "category:(engineering OR sales) -name:bravo", [1, 4, 6, 7] },
        { "+category:research age:40", [3] },
        { "NOT name:alpha NOT name:bravo", [3, 4, 5, 6, 7, 8] },
        { "age:[30 TO 40]", [1, 2, 3, 6] },
        { "age:{30 TO 40}", [2] },
        { "age:[30 TO 40}", [1, 2, 6] },
        { "age:>35", [3, 5, 8] },
        { "age:>=35", [2, 3, 5, 8] },
        { "age:<30", [4, 7] },
        { "age:[45 TO *]", [5, 8] },
        { "age:[* TO 25]", [4, 7] },
        { "balance:[-100 TO 0]", [4, 6, 7] },
        { "balance:<0", [4, 7] },
        { "salary:<90000", [1, 4, 7] },
        { "salary:[95000 TO 110000]", [2, 3, 6] },
        { "age:(>=30 AND <40)", [1, 2, 6] },
        { "created:[2020-01-01 TO 2021-12-31]", [1, 5, 6] },
        { "created:[2020-06-15 TO 2020-06-15]", [1, 6] },
        { "created:{2020-06-15 TO *]", [4, 5, 7] },
        { "created:>=2021-01", [4, 5, 7] },
        { "created:<2020", [2, 3, 8] },
        { "created:<=2019-03-10", [2, 3, 8] },
        { "name:ch*", [3] },
        { "name:*a", [1, 4, 7] },
        { "name:?ravo", [2] },
        { "name:d*a", [4] },
        { "_exists_:notes", [1, 3, 5, 6] },
        { "notes:*", [1, 3, 5, 6] },
        { "_missing_:notes", [2, 4, 7, 8] },
        { "NOT _exists_:notes", [2, 4, 7, 8] },
        { "@include:seniors", [3, 5, 8] },
        { "@include:seniors -name:echo", [3, 8] },
        { "dept:engineering", [1, 4, 7] },
        { "-category:engineering active:true", [2, 3] },
        { "+active:true category:sales", [2] },
        { "category:research OR NOT active:true", [3, 4, 5, 6, 8] },
        { "category:engineering OR -active:true", [4] },
        { "-category:engineering -category:sales", [3, 5, 8] },
        { "category:engineering AND active:true OR category:sales", [1, 2, 6, 7] },
        { "created:[2020-06-15 TO 2021-01-25}", [1, 6] },
        { "created:<=2020-06-15", [1, 2, 3, 6, 8] },
        { "created:>2020-06-15", [4, 5, 7] },
        { "created:2020-06", [1, 6] },
        { "name:a*", [1] },
        { "name:*har*", [3] },
        { "name:ch?rlie", [3] },
        { "name:*o", [2, 5] },
        { "age:{30 TO 40]", [2, 3] },
        { "salary:[* TO 95000]", [1, 2, 4, 7] },
        { "category:(sales OR research) -age:>45", [2, 3, 6, 8] },
    };

    [Theory]
    [MemberData(nameof(ParityQueries))]
    public async Task Query_ReturnsSameResultsOnSqlServerAndElasticsearch(string query, int[] expectedIds)
    {
        int[] sqlIds = fixture.QuerySql(query).Order().ToArray();
        int[] esIds = (await fixture.QueryElasticsearchAsync(query)).Order().ToArray();

        Assert.Equal(expectedIds, sqlIds);
        Assert.Equal(expectedIds, esIds);
    }

    public static TheoryData<string, int[]> SortQueries() => new()
    {
        { "age name", [7, 4, 1, 6, 2, 3, 8, 5] },
        { "-salary", [8, 5, 3, 6, 2, 1, 7, 4] },
        { "category -age", [1, 4, 7, 5, 8, 3, 2, 6] },
    };

    [Theory]
    [MemberData(nameof(SortQueries))]
    public async Task Sort_ReturnsSameOrderOnSqlServerAndElasticsearch(string sort, int[] expectedIds)
    {
        int[] sqlIds = fixture.SortSql(sort).ToArray();
        int[] esIds = (await fixture.SortElasticsearchAsync(sort)).ToArray();

        Assert.Equal(expectedIds, sqlIds);
        Assert.Equal(expectedIds, esIds);
    }
}
