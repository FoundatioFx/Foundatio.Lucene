using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch.Tests.Integration;

/// <summary>
/// Document-level truth tables for required (<c>+</c>), prohibited (<c>-</c>, <c>NOT</c>), and optional clauses.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
[Trait("TestType", "Integration")]
public sealed class RequiredClauseIntegrationTests(ElasticsearchFixture fixture)
{
    public static IEnumerable<TheoryDataRow<string, BooleanOperator, bool, string?>> RequiredClauseCases()
    {
        // Null means the query is rejected. AND binds tighter than OR, so rows that mix them with a required clause
        // (a OR +b AND c OR a is a OR (b AND c) OR a) differ from Foundatio.Parsers, which grouped to the right.
        (string Query, string? Or, string? And)[] cases =
        [
            ("+tags:a tags:b", "1,3,5,7", "3,7"),
            ("tags:a +tags:b", "2,3,6,7", "3,7"),
            ("tags:a tags:b +tags:c", "4,5,6,7", "7"),
            ("+tags:a +tags:b tags:c", "3,7", "7"),
            ("+tags:a tags:b tags:c", "1,3,5,7", "7"),
            ("+tags:a -tags:b tags:c", "1,5", "5"),
            ("-tags:b tags:c +tags:a", "1,5", "5"),
            ("+tags:a NOT tags:b", "1,5", "1,5"),
            ("+tags:a !tags:b", "1,5", "1,5"),
            ("+tags:a NOT +tags:b", null, null),
            ("+(tags:a OR tags:b)", "1,2,3,5,6,7", "1,2,3,5,6,7"),
            ("+(tags:a OR tags:b) tags:c", "1,2,3,5,6,7", "5,6,7"),
            ("+(tags:a tags:b) tags:c", "1,2,3,5,6,7", "7"),
            ("+tags:a (tags:b OR tags:c)", "1,3,5,7", "3,5,7"),
            ("(+tags:a tags:b) OR tags:c", "1,3,4,5,6,7", "3,4,5,6,7"),
            ("+tags:a OR tags:b", "1,3,5,7", "1,3,5,7"),
            ("tags:a OR +tags:b OR tags:c", "2,3,6,7", "2,3,6,7"),
            ("tags:a OR +tags:b AND tags:c OR tags:a", "1,3,5,6,7", "1,3,5,6,7"),
            ("tags:a OR tags:b AND +tags:c", "1,3,5,6,7", "1,3,5,6,7"),
            ("tags:a OR tags:c AND +tags:b OR tags:a", "1,3,5,6,7", "1,3,5,6,7"),
            ("tags:a OR (+tags:b AND tags:c) OR tags:a", "1,3,5,6,7", "1,3,5,6,7"),
            ("+a b", "1,3,5,7", "3,7"),
            ("+alias:a tags:b", "1,3,5,7", "3,7"),
            ("+@include:a tags:b", "1,3,5,7", "3,7"),
            ("NOT @include:a", "0,2,4,6", "0,2,4,6"),
            ("-@include:a", "0,2,4,6", "0,2,4,6"),
            ("NOT @include:not-a", "1,3,5,7", "1,3,5,7"),
            ("+_exists_:tags tags:a", "1,2,3,4,5,6,7", "1,3,5,7"),
            ("+children.name:a children.value:y", "1,3,5,7", "3,7"),
            ("children.value:y +children.name:a", "1,3,5,7", "3,7"),
            ("+children.name:a tags:c", "1,3,5,7", "5,7"),
            ("+tags:c children.name:a", "4,5,6,7", "5,7"),
            ("+children.name:a -children.value:y", "5", "5"),
            ("+children:(children.name:a OR children.name:b) tags:c", "1,2,3,5,6,7", "5,6,7"),
            ("children:(+children.name:a children.value:y)", "1,3,5,7", "3,7"),
            ("tags:a OR NOT tags:b", "0,1,3,4,5,7", "0,1,3,4,5,7"),
            ("tags:a OR -tags:b", "1,5", "1,5"),
            ("tags:a AND NOT tags:b OR tags:c", "1,4,5,6,7", "1,4,5,6,7")
        ];

        foreach (var (query, or, and) in cases)
        {
            foreach (bool scoring in new[] { false, true })
            {
                yield return new(query, BooleanOperator.Or, scoring, or);
                yield return new(query, BooleanOperator.And, scoring, and);
            }
        }
    }

    [Theory]
    [MemberData(nameof(RequiredClauseCases))]
    public async Task BuildQuery_WithRequiredClauses_MatchesExpectedDocuments(string text, BooleanOperator defaultOperator, bool scoring, string? expected)
    {
        var parser = CreateParser();
        var options = new ElasticsearchQueryOptions { DefaultOperator = defaultOperator, UseScoring = scoring };

        if (expected is null)
        {
            Assert.ThrowsAny<QueryException>(() => parser.BuildQuery(text, options));
            return;
        }

        var response = await SearchAsync(parser.BuildQuery(text, options));

        Assert.Equal(expected, GetIds(response));
        if (!scoring)
            Assert.All(response.Hits, hit => Assert.Equal(0, hit.Score));
    }

    [Fact]
    public async Task BuildQuery_WithOptionalClause_ContributesScoreWithoutAdmittingOtherDocuments()
    {
        var parser = CreateParser();
        var options = new ElasticsearchQueryOptions { DefaultOperator = BooleanOperator.Or, UseScoring = true };

        var native = await SearchAsync(parser.BuildQuery("+tags:a tags:b", options));
        var reference = await SearchAsync(new QueryStringQuery("+tags:a tags:b") { DefaultOperator = Operator.Or });

        Assert.Equal("1,3,5,7", GetIds(native));
        var nativeScores = native.Hits.ToDictionary(hit => hit.Id!, hit => hit.Score!.Value);
        var referenceScores = reference.Hits.ToDictionary(hit => hit.Id!, hit => hit.Score!.Value);
        Assert.Equal(referenceScores.Keys.Order(StringComparer.Ordinal), nativeScores.Keys.Order(StringComparer.Ordinal));
        foreach (string id in nativeScores.Keys)
            Assert.Equal(referenceScores[id], nativeScores[id], 0.00001);
        Assert.True(nativeScores["3"] > nativeScores["1"]);
    }

    public static IEnumerable<TheoryDataRow<string, BooleanOperator, bool, string>> RequiredClausePermutations()
    {
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        string[] prefixes = ["", "+", "-", "NOT "];
        for (int combination = 0; combination < 64; combination++)
        {
            int[] modifiers = [combination % 4, combination / 4 % 4, combination / 16];
            if (!modifiers.Contains(1))
                continue;

            foreach (var order in orders)
            {
                string query = string.Join(' ', order.Select(field => prefixes[modifiers[field]] + "tags:" + (char)('a' + field)));
                foreach (var op in new[] { BooleanOperator.And, BooleanOperator.Or })
                {
                    // Evaluates the eight documents as sets, independently of the parser, the generated DSL, and Elasticsearch.
                    var ids = Enumerable.Range(0, 8).Where(document => Enumerable.Range(0, 3).All(field =>
                    {
                        bool present = (document & (1 << field)) != 0;
                        return modifiers[field] switch
                        {
                            1 => present,
                            2 or 3 => !present,
                            _ => op != BooleanOperator.And || present
                        };
                    })).Select(id => id.ToString(CultureInfo.InvariantCulture));

                    string expected = string.Join(',', ids);
                    foreach (bool scoring in new[] { false, true })
                        yield return new(query, op, scoring, expected);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(RequiredClausePermutations))]
    public async Task BuildQuery_WithEveryThreeTermRequiredCombination_MatchesTruthTableAndQueryString(string text, BooleanOperator op, bool scoring, string expected)
    {
        var parser = CreateParser();
        var referenceClause = new QueryStringQuery(text) { DefaultOperator = op == BooleanOperator.And ? Operator.And : Operator.Or };
        Query referenceQuery = scoring ? referenceClause : new BoolQuery { Filter = [referenceClause] };

        var native = await SearchAsync(parser.BuildQuery(text, new ElasticsearchQueryOptions { DefaultOperator = op, UseScoring = scoring }));
        var reference = await SearchAsync(referenceQuery);

        Assert.Equal(expected, GetIds(native));
        Assert.Equal(expected, GetIds(reference));
        if (!scoring)
        {
            Assert.All(native.Hits, hit => Assert.Equal(0, hit.Score));
            Assert.All(reference.Hits, hit => Assert.Equal(0, hit.Score));
        }
    }

    private static ElasticsearchQueryParser CreateParser() => new(c =>
    {
        c.UseMappings(RequiredClauseData.Mapping);
        c.DefaultFields = ["tags"];
        c.FieldResolver = (field, _) => field == "alias" ? "tags" : null;
        c.Includes = new Dictionary<string, string> { ["a"] = "tags:a", ["not-a"] = "NOT tags:a" };
    });

    private async Task<SearchResponse<RequiredClauseData.Document>> SearchAsync(Query query)
    {
        var response = await fixture.SearchAsync<RequiredClauseData.Document>(RequiredClauseData.Index, query, cancellationToken: TestContext.Current.CancellationToken);
        ElasticsearchFixture.AssertComplete(response);
        Assert.All(response.Hits, hit => Assert.True(hit.Score.HasValue && double.IsFinite(hit.Score.Value)));
        return response;
    }

    private static string GetIds<T>(SearchResponse<T> response) => string.Join(',', response.Hits.Select(hit => hit.Id).Order(StringComparer.Ordinal));
}
