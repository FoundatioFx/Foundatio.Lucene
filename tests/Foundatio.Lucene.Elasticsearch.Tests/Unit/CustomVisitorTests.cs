using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class CustomVisitorTests
{
    [Fact]
    public void BuildQuery_WithCustomFilterVisitor_UsesQuerySetOnNode()
    {
        var parser = CreateParser();

        var result = parser.BuildQuery("@custom:(one)");

        ElasticAssert.Json("{'bool':{'filter':{'terms':{'id':['1']}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithCustomFilterContainingIncludeText_KeepsIncludeTextForVisitor()
    {
        var parser = CreateParser();

        var result = parser.BuildQuery("@custom:(one @include:3)");

        ElasticAssert.Json("{'bool':{'filter':{'terms':{'id':['1','3']}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithSeveralCustomFilters_CombinesCustomQueries()
    {
        var parser = CreateParser();

        var result = parser.BuildQuery("@custom:(one) OR (keyword:Test @custom:(two))");

        ElasticAssert.Json("{'bool':{'filter':{'bool':{'minimum_should_match':1,'should':[{'terms':{'id':['1']}},{'bool':{'filter':[{'term':{'keyword':{'value':'Test'}}},{'terms':{'id':['2']}}]}}]}}}}", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildQuery_WithRequiredCustomFilterAndOptionalClause_PreservesCustomQuery(bool scoring)
    {
        var parser = CreateParser(c =>
        {
            c.DefaultOperator = BooleanOperator.Or;
            c.UseScoring = scoring;
        });

        var result = parser.BuildQuery("+@custom:(one) keyword:b");

        ElasticAssert.Json(scoring
            ? "{'bool':{'must':{'terms':{'id':['1']}},'should':{'term':{'keyword':{'value':'b'}}}}}"
            : "{'bool':{'filter':{'terms':{'id':['1']}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithCustomFilterInsideInclude_ResolvesCustomFilterFromExpandedInclude()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(TestMapping.Create());
            c.Includes = new Dictionary<string, string> { ["test"] = "@custom:(two)" };
            c.AddVisitorAfter<IncludeVisitor>(new CustomFilterVisitor());
        });

        var result = parser.BuildQuery("@include:test");

        ElasticAssert.Json("{'bool':{'filter':{'terms':{'id':['2']}}}}", result);
    }

    [Fact]
    public void BuildQuery_WithSkippedInclude_LetsCustomVisitorTranslateReference()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(TestMapping.Create());
            c.UseScoring = true;
            c.Includes = new Dictionary<string, string> { ["regular"] = "keyword:a" };
            c.ShouldSkipInclude = (node, _) => IncludeVisitor.GetIncludeName(node) is "saved-search";
            c.AddVisitorAfter<IncludeVisitor>(new SavedSearchVisitor());
        });

        var result = parser.BuildQuery("@include:regular @include:saved-search");

        ElasticAssert.Json("{'bool':{'must':[{'term':{'keyword':{'value':'a'}}},{'ids':{'values':['saved-1']}}]}}", result);
    }

    [Fact]
    public void BuildQuery_WithVisitorOverridingTermQuery_ReplacesDefaultTranslation()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(TestMapping.Create());
            c.AddVisitor(new FixedFieldVisitor());
        });

        var result = parser.BuildQuery("fixed:true");
        var negated = parser.BuildQuery("fixed:false");

        ElasticAssert.Json("{'bool':{'filter':{'exists':{'field':'date_fixed'}}}}", result);
        ElasticAssert.Json("{'bool':{'filter':{'bool':{'must_not':{'exists':{'field':'date_fixed'}}}}}}", negated);
    }

    [Fact]
    public void BuildQuery_WithCustomQueryOnNestedField_WrapsCustomQueryInNestedQuery()
    {
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(TestMapping.Create());
            c.UseScoring = true;
            c.AddVisitorAfter<FieldResolverQueryVisitor>(new NestedOverrideVisitor());
        });

        var result = parser.BuildQuery("children.name:special");

        ElasticAssert.Json("{'nested':{'path':'children','query':{'match_all':{}}}}", result);
    }

    [Fact]
    public void Configuration_WithAddedVisitor_RunsItInPriorityOrder()
    {
        var recorder = new FieldRecordingVisitor();
        var parser = new ElasticsearchQueryParser(c =>
        {
            c.FieldMap = new FieldMap { { "user", "keyword" } };
            c.AddVisitor(recorder);
        });

        parser.BuildQuery("user:x");

        Assert.Equal(["user"], recorder.Fields);
    }

    private static ElasticsearchQueryParser CreateParser(Action<ElasticsearchQueryParserConfiguration>? configure = null)
    {
        return new ElasticsearchQueryParser(c =>
        {
            c.UseMappings(TestMapping.Create());
            c.AddVisitorBefore<IncludeVisitor>(new CustomFilterVisitor());
            configure?.Invoke(c);
        });
    }

    /// <summary>
    /// Resolves <c>@custom:(filter)</c> to a <c>terms</c> query on ids.
    /// </summary>
    private sealed class CustomFilterVisitor : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field != "@custom" || node.Query is not GroupNode { Query: { } inner })
                return base.Visit(node, context);

            string[] ids = QueryStringBuilder.ToQueryString(inner) switch
            {
                "one" => ["1"],
                "two" => ["2"],
                "one @include:3" => ["1", "3"],
                _ => []
            };

            node.SetQuery(ids.Length > 0
                ? new TermsQuery { Field = "id", Terms = new TermsQueryField(ids.Select(FieldValue.String).ToArray()) }
                : new TermQuery("id", "none"));
            node.Query = null;
            return node;
        }
    }

    private sealed class SavedSearchVisitor : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (IncludeVisitor.IsInclude(node) && IncludeVisitor.GetIncludeName(node) is "saved-search")
                node.SetQuery(new IdsQuery { Values = new Ids(["saved-1"]) });

            return base.Visit(node, context);
        }
    }

    private sealed class FixedFieldVisitor : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field != "fixed" || node.Query is not TermNode term || !bool.TryParse(term.Term, out bool isFixed))
                return base.Visit(node, context);

            Query exists = new ExistsQuery("date_fixed");
            node.SetQuery(isFixed ? exists : new BoolQuery { MustNot = [exists] });
            return node;
        }
    }

    private sealed class NestedOverrideVisitor : QueryVisitor
    {
        protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
        {
            if (node.Term == "special")
                node.SetQuery(new MatchAllQuery());

            return node;
        }
    }

    private sealed class FieldRecordingVisitor : QueryVisitor
    {
        public List<string> Fields { get; } = [];

        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            Fields.Add(node.Field);
            return base.Visit(node, context);
        }
    }
}
