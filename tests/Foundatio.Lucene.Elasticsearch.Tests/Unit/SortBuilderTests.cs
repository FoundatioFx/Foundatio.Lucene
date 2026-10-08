using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class SortBuilderTests
{
    [Theory]
    [InlineData("keyword", "[{'keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("+keyword", "[{'keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("-keyword", "[{'keyword':{'order':'desc','unmapped_type':'keyword'}}]")]
    [InlineData("keyword:asc", "[{'keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("keyword:DESC", "[{'keyword':{'order':'desc','unmapped_type':'keyword'}}]")]
    [InlineData("text", "[{'text.keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("text2", "[{'text2.sort':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("plaintext", "[{'plaintext':{'order':'asc','unmapped_type':'text'}}]")]
    [InlineData("number -date", "[{'number':{'order':'asc','unmapped_type':'integer'}},{'date':{'order':'desc','unmapped_type':'date'}}]")]
    [InlineData("bool long double dateNanos", "[{'bool':{'order':'asc','unmapped_type':'boolean'}},{'long':{'order':'asc','unmapped_type':'long'}},{'double':{'order':'asc','unmapped_type':'double'}},{'dateNanos':{'order':'asc','unmapped_type':'date_nanos'}}]")]
    [InlineData("-(keyword number +date)", "[{'keyword':{'order':'desc','unmapped_type':'keyword'}},{'number':{'order':'desc','unmapped_type':'integer'}},{'date':{'order':'asc','unmapped_type':'date'}}]")]
    [InlineData("geo", "[{'geo':{'order':'asc','unmapped_type':'geo_point'}}]")]
    [InlineData("unknown", "[{'unknown':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("KEYWORD", "[{'keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("multiWord -multiword", "[{'multiWord.keyword':{'order':'asc','unmapped_type':'keyword'}},{'multiWord.keyword':{'order':'desc','unmapped_type':'keyword'}}]")]
    [InlineData("obj.name obj.desc", "[{'obj.name':{'order':'asc','unmapped_type':'keyword'}},{'obj.desc':{'order':'asc','unmapped_type':'text'}}]")]
    [InlineData("alias", "[{'alias':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("keyword,number", "[{'keyword,number':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("\"spaced field\"", "[{'spaced field':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("spaced\\ field", "[{'spaced field':{'order':'asc','unmapped_type':'keyword'}}]")]
    [InlineData("keyword AND number", "[{'keyword':{'order':'asc','unmapped_type':'keyword'}},{'number':{'order':'asc','unmapped_type':'integer'}}]")]
    public void BuildSort_WithSortExpression_EmitsFieldSorts(string sort, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildSort(sort);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("geo:\"51.5,-0.12\"", "[{'_geo_distance':{'distance_type':'arc','order':'asc','geo':{'lat':51.5,'lon':-0.12}}}]")]
    [InlineData("-geo:u4pruydqqvj", "[{'_geo_distance':{'distance_type':'arc','order':'desc','geo':'u4pruydqqvj'}}]")]
    public void BuildSort_WithValueOnGeoField_EmitsGeoDistanceSort(string sort, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildSort(sort);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("keyword:foo")]
    [InlineData("number:\"51.5,-0.12\"")]
    public void BuildSort_WithValueOnNonGeoField_ThrowsValidationError(string sort)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildSort(sort));

        Assert.Contains("only supported on geo_point fields", exception.Message);
    }

    [Theory]
    [InlineData("_score", "[{'_score':{'order':'asc'}}]")]
    [InlineData("-_score", "[{'_score':{'order':'desc'}}]")]
    [InlineData("_doc", "[{'_doc':{'order':'asc'}}]")]
    [InlineData("-_score keyword", "[{'_score':{'order':'desc'}},{'keyword':{'order':'asc','unmapped_type':'keyword'}}]")]
    public void BuildSort_WithSpecialFields_EmitsScoreAndDocSorts(string sort, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildSort(sort);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildSort_WithoutMapping_UsesKeywordUnmappedType()
    {
        var parser = new ElasticsearchQueryParser();

        var result = parser.BuildSort("-field1 _score");

        ElasticAssert.Json("[{'field1':{'order':'desc','unmapped_type':'keyword'}},{'_score':{'order':'asc'}}]", result);
    }

    [Fact]
    public void BuildSort_WithEmptyExpression_ReturnsNoSorts()
    {
        var parser = TestMapping.CreateParser();

        Assert.Empty(parser.BuildSort(""));
    }

    [Fact]
    public void BuildSort_WithFieldMap_SortsOnTargetField()
    {
        var parser = TestMapping.CreateParser(c => c.FieldMap = new FieldMap { { "geo2", "geo" }, { "name", "text" } });

        var result = parser.BuildSort("geo2 -name");

        ElasticAssert.Json("[{'geo':{'order':'asc','unmapped_type':'geo_point'}},{'text.keyword':{'order':'desc','unmapped_type':'keyword'}}]", result);
    }

    [Theory]
    [InlineData("-children.name", "[{'children.name':{'nested':{'path':'children'},'order':'desc','unmapped_type':'keyword'}}]")]
    [InlineData("children.num", "[{'children.num':{'nested':{'path':'children'},'order':'asc','unmapped_type':'integer'}}]")]
    [InlineData("children.grand.name", "[{'children.grand.name':{'nested':{'nested':{'path':'children.grand'},'path':'children'},'order':'asc','unmapped_type':'keyword'}}]")]
    public void BuildSort_WithNestedField_AddsNestedPathChain(string sort, string expected)
    {
        var parser = TestMapping.CreateParser();

        var result = parser.BuildSort(sort);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildSort_WithNestedDisabled_SortsWithoutNestedPath()
    {
        var parser = TestMapping.CreateParser(c => c.UseNested = false);

        var result = parser.BuildSort("children.name");

        ElasticAssert.Json("[{'children.name':{'order':'asc','unmapped_type':'keyword'}}]", result);
    }

    [Fact]
    public async Task BuildSortAsync_WithNestedFilterResolver_SetsFilterOnDeepestNestedLevel()
    {
        var parser = TestMapping.CreateParser(c => c.NestedFilterResolver = (filter, _, _) =>
            ValueTask.FromResult<Query?>(new TermQuery(filter.NestedPath + ".name", "Official")));

        var resellers = await parser.BuildSortAsync("-resellers.price", cancellationToken: TestContext.Current.CancellationToken);
        var grand = await parser.BuildSortAsync("children.grand.name", cancellationToken: TestContext.Current.CancellationToken);

        ElasticAssert.Json("[{'resellers.price':{'nested':{'filter':{'term':{'resellers.name':{'value':'Official'}}},'path':'resellers'},'order':'desc','unmapped_type':'double'}}]", resellers);
        ElasticAssert.Json("[{'children.grand.name':{'nested':{'nested':{'filter':{'term':{'children.grand.name':{'value':'Official'}}},'path':'children.grand'},'path':'children'},'order':'asc','unmapped_type':'keyword'}}]", grand);
    }

    [Theory]
    [InlineData("!keyword")]
    [InlineData("NOT keyword")]
    [InlineData("keyword !number")]
    [InlineData("!(keyword number)")]
    [InlineData("NOT (keyword number)")]
    public void BuildSort_WithBooleanNegation_ThrowsValidationException(string sort)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildSort(sort));

        Assert.Contains("is not supported in sort expressions", exception.Message);
        Assert.Contains("use + for ascending or - for descending order", exception.Message);
        Assert.False(parser.ValidateSort(sort).IsValid);
    }

    [Theory]
    [InlineData("keyword~1")]
    [InlineData("keyword*")]
    [InlineData("keyword^2")]
    [InlineData("keyword:value")]
    [InlineData("number:[1 TO 2]")]
    [InlineData("/regex/")]
    [InlineData("_exists_:keyword")]
    [InlineData("keyword:+asc")]
    [InlineData("NOT -keyword")]
    [InlineData("before\\^after")]
    public void BuildSort_WithUnsupportedSyntax_ThrowsValidationException(string sort)
    {
        var parser = TestMapping.CreateParser();

        Assert.ThrowsAny<QueryException>(() => parser.BuildSort(sort));
        Assert.False(parser.ValidateSort(sort).IsValid);
    }

    [Theory]
    [InlineData("!@include:ordering")]
    [InlineData("NOT @include:ordering")]
    public void BuildSort_WithNegatedInclude_ThrowsValidationException(string sort)
    {
        var parser = TestMapping.CreateParser(c => c.Includes = new Dictionary<string, string> { ["ordering"] = "number" });

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildSort(sort));

        Assert.Contains("is not supported in sort expressions", Assert.Single(exception.Errors).Message);
        Assert.Empty(exception.Result.UnresolvedIncludes);
    }

    [Fact]
    public void BuildSort_WithInclude_ExpandsIncludedSort()
    {
        var parser = TestMapping.CreateParser(c => c.Includes = new Dictionary<string, string> { ["recent"] = "-date +keyword" });

        var result = parser.BuildSort("@include:recent number");

        ElasticAssert.Json("[{'date':{'order':'desc','unmapped_type':'date'}},{'keyword':{'order':'asc','unmapped_type':'keyword'}},{'number':{'order':'asc','unmapped_type':'integer'}}]", result);
    }

    [Fact]
    public async Task BuildSortAsync_WithIncludeResolverAndNestedNegatedInclude_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.IncludeResolver = (name, _, _) =>
            ValueTask.FromResult<string?>(name == "outer" ? "@include:inner" : "NOT +number"));

        var exception = await Assert.ThrowsAsync<QueryValidationException>(() => parser.BuildSortAsync("@include:outer", cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(exception.Result.UnresolvedIncludes);
    }
    [Theory]
    [InlineData("91,0")]
    [InlineData("-91,0")]
    [InlineData("0,181")]
    [InlineData("0,-181")]
    [InlineData("NaN,0")]
    [InlineData("0,NaN")]
    [InlineData("Infinity,0")]
    [InlineData("0,-Infinity")]
    public void BuildSort_WithInvalidGeoCoordinates_ThrowsValidationException(string coordinates)
    {
        // Arrange
        var parser = TestMapping.CreateParser();

        // Act
        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildSort($"geo:\"{coordinates}\""));

        // Assert
        Assert.Contains("coordinates", exception.Message);
    }

    [Theory]
    [InlineData("90,180", "[{'_geo_distance':{'distance_type':'arc','order':'asc','geo':{'lat':90,'lon':180}}}]")]
    [InlineData("-90,-180", "[{'_geo_distance':{'distance_type':'arc','order':'asc','geo':{'lat':-90,'lon':-180}}}]")]
    [InlineData("u4pruydqqvj", "[{'_geo_distance':{'distance_type':'arc','order':'asc','geo':'u4pruydqqvj'}}]")]
    public void BuildSort_WithValidGeoBoundaryOrGeohash_PreservesLocation(string location, string expected)
    {
        // Arrange
        var parser = TestMapping.CreateParser();

        // Act
        var result = parser.BuildSort($"geo:\"{location}\"");

        // Assert
        ElasticAssert.Json(expected, result);
    }

}
