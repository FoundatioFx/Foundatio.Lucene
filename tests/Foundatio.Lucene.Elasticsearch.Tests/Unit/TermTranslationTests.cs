using System.Globalization;
using Foundatio.Lucene.Elasticsearch.Tests.Utility;

namespace Foundatio.Lucene.Elasticsearch.Tests.Unit;

public class TermTranslationTests
{
    [Theory]
    [InlineData("text:1..5", "{'match':{'text':{'query':'1..5'}}}")]
    [InlineData("keyword:1..5", "{'term':{'keyword':{'value':'1..5'}}}")]
    [InlineData("text:john\\*", "{'match':{'text':{'query':'john*'}}}")]
    [InlineData("keyword:john\\*", "{'term':{'keyword':{'value':'john*'}}}")]
    [InlineData("keyword:jo\\?n", "{'term':{'keyword':{'value':'jo?n'}}}")]
    [InlineData("keyword:\"john*\"", "{'term':{'keyword':{'value':'john*'}}}")]
    [InlineData("keyword:Testing.Casing", "{'term':{'keyword':{'value':'Testing.Casing'}}}")]
    [InlineData("keyword:\"Blake Niemyjski\"", "{'term':{'keyword':{'value':'Blake Niemyjski'}}}")]
    [InlineData("text:one\\\\/two", "{'match':{'text':{'query':'one\\\\/two'}}}")]
    [InlineData("field\\.with\\.dots:value", "{'term':{'field.with.dots':{'value':'value'}}}")]
    [InlineData("first\\ name:Alice", "{'term':{'first name':{'value':'Alice'}}}")]
    public void BuildQuery_WithLiteralTerm_EmitsLiteralValue(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('!')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('"')]
    [InlineData('~')]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData(':')]
    [InlineData('\\')]
    [InlineData('/')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData('=')]
    [InlineData('<')]
    [InlineData('>')]
    public void BuildQuery_WithEscapedSpecialCharacter_UsesLiteralFieldAndValue(char character)
    {
        var parser = new ElasticsearchQueryParser(c => c.UseScoring = true);

        var result = parser.BuildQuery($"before\\{character}after:before\\{character}after");

        Assert.NotNull(result.Term);
        Assert.Equal($"before{character}after", result.Term.Field.Name);
        Assert.Equal($"before{character}after", result.Term.Value.ToString());
    }

    [Theory]
    [InlineData("before\\^after:value")]
    [InlineData("title\\^2:value")]
    [InlineData("_exists_:before\\^after")]
    public void BuildQuery_WithCaretInFieldName_ThrowsValidationException(string query)
    {
        var parser = new ElasticsearchQueryParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains("Field names cannot contain '^'", exception.Message);
    }

    [Theory]
    [InlineData("text:john*", "john*")]
    [InlineData("text:jo?n*", "jo?n*")]
    [InlineData("text:jo?n", "jo?n")]
    [InlineData("text:jo*n", "jo*n")]
    [InlineData("text:*john", "*john")]
    [InlineData("text:otherText\\:john*", "otherText\\:john*")]
    [InlineData("text:one\\\\/two*", "one\\\\\\/two*")]
    [InlineData("text:a\\ b*", "a\\ b*")]
    public void BuildQuery_WithAnalyzedWildcard_UsesEscapedQueryString(string query, string expression)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text", "otherText"]);

        var result = parser.BuildQuery(query);

        Assert.NotNull(result.QueryString);
        Assert.Equal(expression, result.QueryString.Query);
        Assert.Equal(["text"], result.QueryString.Fields!.Select(f => f.Name));
        Assert.True(result.QueryString.AnalyzeWildcard);
        Assert.True(result.QueryString.AllowLeadingWildcard);
    }

    [Fact]
    public void BuildQuery_WithWildcardOnAnalyzedDefaultFields_UsesQueryStringWithAllFields()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text", "otherText"]);

        var result = parser.BuildQuery("john*");

        ElasticAssert.Json("{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'fields':['text','otherText'],'query':'john*'}}", result);
    }

    [Theory]
    [InlineData("keyword:john*", "{'prefix':{'keyword':{'value':'john'}}}")]
    [InlineData("keyword:jo\\?n*", "{'prefix':{'keyword':{'value':'jo?n'}}}")]
    [InlineData("keyword:john\\**", "{'prefix':{'keyword':{'value':'john*'}}}")]
    [InlineData("keyword:jo?n", "{'wildcard':{'keyword':{'value':'jo?n'}}}")]
    [InlineData("keyword:jo*n", "{'wildcard':{'keyword':{'value':'jo*n'}}}")]
    [InlineData("keyword:*john", "{'wildcard':{'keyword':{'value':'*john'}}}")]
    [InlineData("keyword:jo?n*", "{'wildcard':{'keyword':{'value':'jo?n*'}}}")]
    [InlineData("keyword:jo\\*n?", "{'wildcard':{'keyword':{'value':'jo\\\\*n?'}}}")]
    public void BuildQuery_WithKeywordPattern_DistinguishesPrefixAndWildcard(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("text:/foo.bar/", "{'regexp':{'text':{'value':'foo.bar'}}}")]
    [InlineData("text:/foo\\.bar/", "{'regexp':{'text':{'value':'foo\\\\.bar'}}}")]
    [InlineData("text:/val.*/", "{'regexp':{'text':{'value':'val.*'}}}")]
    [InlineData("keyword:/[0-9]+/", "{'regexp':{'keyword':{'value':'[0-9]+'}}}")]
    [InlineData("keyword:/val.*/", "{'regexp':{'keyword':{'value':'val.*'}}}")]
    [InlineData("keyword:/foo\\.bar/", "{'regexp':{'keyword':{'value':'foo\\\\.bar'}}}")]
    [InlineData("keyword:/val.*/^5", "{'regexp':{'keyword':{'boost':5,'value':'val.*'}}}")]
    public void BuildQuery_WithRegex_PreservesRegexOperatorsAndEscapes(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("text:value~2", "match", "text", 2)]
    [InlineData("text:value~", "match", "text", 2)]
    [InlineData("text:value~0", "match", "text", 0)]
    [InlineData("text:value~1", "match", "text", 1)]
    [InlineData("keyword:value~0", "fuzzy", "keyword", 0)]
    [InlineData("keyword:value~1", "fuzzy", "keyword", 1)]
    [InlineData("keyword:value~2", "fuzzy", "keyword", 2)]
    [InlineData("keyword:value~", "fuzzy", "keyword", 2)]
    [InlineData("text:value~+1", "match", "text", 1)]
    [InlineData(@"text:value~\+1", "match", "text", 1)]
    [InlineData("keyword:value~+2", "fuzzy", "keyword", 2)]
    public void BuildQuery_WithFuzzyTerm_EmitsEditDistance(string query, string kind, string field, int distance)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        string property = kind == "match" ? "query" : "value";
        ElasticAssert.Json($"{{'{kind}':{{'{field}':{{'fuzziness':{distance},'{property}':'value'}}}}}}", result);
    }

    [Theory]
    [InlineData("text:value~AUTO", "match", "text", "AUTO")]
    [InlineData("keyword:value~auto", "fuzzy", "keyword", "AUTO")]
    [InlineData("text:value~\"AUTO:3,6\"", "match", "text", "AUTO:3,6")]
    [InlineData(@"text:value~AUTO\:3,6", "match", "text", "AUTO:3,6")]
    [InlineData(@"keyword:value~auto\:2,4", "fuzzy", "keyword", "AUTO:2,4")]
    [InlineData(@"keyword:value~AUTO\:0,0", "fuzzy", "keyword", "AUTO:0,0")]
    [InlineData(@"keyword:value~AUTO\:+3,+6", "fuzzy", "keyword", "AUTO:3,6")]
    [InlineData(@"keyword:value~AUTO\:0,2147483647", "fuzzy", "keyword", "AUTO:0,2147483647")]
    public void BuildQuery_WithAutoFuzziness_EmitsLengthDependentPolicy(string query, string kind, string field, string policy)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        string property = kind == "match" ? "query" : "value";
        ElasticAssert.Json($"{{'{kind}':{{'{field}':{{'fuzziness':'{policy}','{property}':'value'}}}}}}", result);
    }

    [Theory]
    [InlineData("text:\"a b\"~5", "text", 5)]
    [InlineData("text:\"a b\"~", "text", 0)]
    [InlineData("keyword:\"a b\"~3", "keyword", 3)]
    [InlineData("text:\"a b\"~+1", "text", 1)]
    [InlineData("text:\"a b\"~\\+1", "text", 1)]
    public void BuildQuery_WithPhraseSlop_EmitsMatchPhraseWithSlop(string query, string field, int slop)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json($"{{'match_phrase':{{'{field}':{{'query':'a b','slop':{slop}}}}}}}", result);
    }

    [Theory]
    [InlineData("text:value^2", "{'match':{'text':{'boost':2,'query':'value'}}}")]
    [InlineData("text:\"a b\"^2", "{'match_phrase':{'text':{'boost':2,'query':'a b'}}}")]
    [InlineData("keyword:value^2", "{'term':{'keyword':{'boost':2,'value':'value'}}}")]
    [InlineData("keyword:value^0", "{'term':{'keyword':{'boost':0,'value':'value'}}}")]
    [InlineData("keyword:value^+2", "{'term':{'keyword':{'boost':2,'value':'value'}}}")]
    [InlineData(@"keyword:value^\+2", "{'term':{'keyword':{'boost':2,'value':'value'}}}")]
    [InlineData("keyword:jo?n^4", "{'wildcard':{'keyword':{'boost':4,'value':'jo?n'}}}")]
    [InlineData("keyword:jo*^4", "{'prefix':{'keyword':{'boost':4,'value':'jo'}}}")]
    [InlineData("keyword:value~1^3", "{'fuzzy':{'keyword':{'boost':3,'fuzziness':1,'value':'value'}}}")]
    [InlineData("number:[1 TO 5]^2", "{'range':{'number':{'boost':2,'gte':'1','lte':'5'}}}")]
    [InlineData("text:jo*^2", "{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'boost':2,'fields':['text'],'query':'jo*'}}")]
    public void BuildQuery_WithBoost_EmitsNativeBoost(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithBoostUnderNonInvariantCulture_UsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var parser = TestMapping.CreateScoringParser();

            var result = parser.BuildQuery("text:value^1.5");

            ElasticAssert.Json("{'match':{'text':{'boost':1.5,'query':'value'}}}", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("text:value~-1")]
    [InlineData("keyword:value~-1")]
    [InlineData("text:value~3")]
    [InlineData("text:value~NaN")]
    [InlineData("text:value~0.8")]
    [InlineData("text:value~AUTOMATIC")]
    [InlineData(@"keyword:value~AUTO\:")]
    [InlineData(@"keyword:value~AUTO\:3")]
    [InlineData(@"keyword:value~AUTO\:3,")]
    [InlineData(@"keyword:value~AUTO\:,6")]
    [InlineData(@"keyword:value~AUTO\:3,6,9")]
    [InlineData(@"keyword:value~AUTO\:-1,6")]
    [InlineData(@"keyword:value~AUTO\:6,3")]
    [InlineData(@"keyword:value~AUTO\:3.5,6")]
    [InlineData(@"keyword:value~AUTO\:3,2147483648")]
    [InlineData("text:\"a b\"~AUTO")]
    [InlineData("keyword:jo*n~AUTO")]
    [InlineData("keyword:jo*~1")]
    [InlineData("text:value^NaN")]
    [InlineData("text:value^-1")]
    [InlineData("text:value^1e100")]
    [InlineData("text:value^Infinity")]
    [InlineData("text:value^garbage")]
    [InlineData("(text:a OR text:b)^garbage")]
    [InlineData("text:foo*~1")]
    [InlineData("text:\"a b\"~-1")]
    [InlineData("text:\"a b\"~1.5")]
    [InlineData("text:\"a b\"~2147483648")]
    [InlineData("text:\"a b\"~junk")]
    [InlineData("number:[1 TO 5]~2")]
    [InlineData("number:[1 TO 5]^garbage")]
    public void BuildQuery_WithInvalidModifier_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.False(exception.Result.IsValid);
        Assert.NotEmpty(exception.Errors);
    }

    [Theory]
    [InlineData("text:/foo/~1")]
    [InlineData("(text:a OR text:b)~AUTO")]
    [InlineData("_exists_:text^2")]
    public void BuildQuery_WithUnsupportedModifier_ThrowsParseException(string query)
    {
        var parser = TestMapping.CreateParser();

        Assert.Throws<QueryParseException>(() => parser.BuildQuery(query));
        Assert.False(parser.ValidateQuery(query).IsValid);
    }

    [Theory]
    [InlineData("number", "/12/")]
    [InlineData("date", "/2026/")]
    [InlineData("dateNanos", "/2026/")]
    [InlineData("bool", "/true/")]
    [InlineData("number", "12~1")]
    [InlineData("number", "\"12\"~1")]
    [InlineData("date", "\"2026\"~0")]
    [InlineData("bool", "\"true\"~")]
    [InlineData("date", "2026~AUTO")]
    [InlineData("bool", "true~AUTO")]
    [InlineData("double", "1.5~1")]
    public void BuildQuery_WithStringModifierOnScalarField_ThrowsValidationException(string field, string term)
    {
        var parser = TestMapping.CreateParser(c => c.DefaultFields = ["text", field]);

        var explicitError = Assert.Throws<QueryValidationException>(() => parser.BuildQuery($"{field}:{term}"));
        var defaultFieldError = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(term));

        Assert.Contains($"field '{field}'", explicitError.Message);
        Assert.Contains($"field '{field}'", defaultFieldError.Message);
    }

    [Theory]
    [InlineData("number:5", "{'term':{'number':{'value':5}}}")]
    [InlineData("number:-5", null)]
    [InlineData("number:5.5", "{'term':{'number':{'value':'5.5'}}}")]
    [InlineData("long:12345678901", "{'term':{'long':{'value':12345678901}}}")]
    [InlineData("double:1.5", "{'term':{'double':{'value':1.5}}}")]
    [InlineData("float:2.5", "{'term':{'float':{'value':2.5}}}")]
    [InlineData("bool:true", "{'term':{'bool':{'value':true}}}")]
    [InlineData("bool:TRUE", "{'term':{'bool':{'value':true}}}")]
    [InlineData("bool:false", "{'term':{'bool':{'value':false}}}")]
    [InlineData("date:2024-01-01", "{'term':{'date':{'value':'2024-01-01'}}}")]
    [InlineData("keyword:5", "{'term':{'keyword':{'value':'5'}}}")]
    [InlineData("number:\"5\"", "{'term':{'number':{'value':5}}}")]
    public void BuildQuery_WithTypedField_EmitsTypedTermValue(string query, string? expected)
    {
        var parser = TestMapping.CreateScoringParser();

        if (expected is null)
        {
            Assert.Throws<QueryParseException>(() => parser.BuildQuery(query));
            return;
        }

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("number:abc")]
    [InlineData("long:1x")]
    [InlineData("double:NaN")]
    [InlineData("double:Infinity")]
    [InlineData("bool:yes")]
    [InlineData("bool:1")]
    [InlineData("number:[a TO 5]")]
    [InlineData("number:>abc")]
    [InlineData("double:[1 TO x}")]
    public void BuildQuery_WithNonNumericValueOnTypedField_ThrowsValidationException(string query)
    {
        var parser = TestMapping.CreateParser();

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Contains("not a valid", exception.Message);
    }

    [Theory]
    [InlineData("keyword:*john", "*john")]
    [InlineData("text:?ohn", "?ohn")]
    [InlineData("*john", "*john")]
    [InlineData("keyword:(a OR *b)", "*b")]
    public void BuildQuery_WithDisallowedLeadingWildcard_ReportsOneError(string query, string term)
    {
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false });
        string expectedMessage = "Terms must not start with a wildcard: " + term;

        var validation = parser.ValidateQuery(query);
        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery(query));

        Assert.Equal(expectedMessage, Assert.Single(validation.ValidationErrors).Message);
        Assert.Equal(QueryErrorCode.LeadingWildcardNotAllowed, validation.ValidationErrors[0].Code);
        Assert.Equal(expectedMessage, Assert.Single(exception.Result.ValidationErrors).Message);
        Assert.Equal("Invalid query: " + expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData("keyword:jo*n")]
    [InlineData("keyword:john*")]
    [InlineData("text:jo?n")]
    public void BuildQuery_WithDisallowedLeadingWildcardAndInnerWildcard_Succeeds(string query)
    {
        var parser = TestMapping.CreateParser(c => c.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false });

        var result = parser.BuildQuery(query);

        Assert.DoesNotContain("allow_leading_wildcard\":true", ElasticAssert.Serialize(result));
    }

    [Fact]
    public void BuildQuery_WithDisallowedLeadingWildcardOnAnalyzedField_DisablesLeadingWildcardsInQueryString()
    {
        var parser = TestMapping.CreateScoringParser(c => c.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false });

        var result = parser.BuildQuery("text:jo*n");

        ElasticAssert.Json("{'query_string':{'allow_leading_wildcard':false,'analyze_wildcard':true,'fields':['text'],'query':'jo*n'}}", result);
    }

    [Theory]
    [InlineData("hello", "{'multi_match':{'query':'hello'}}")]
    [InlineData("\"a b\"", "{'multi_match':{'query':'a b','type':'phrase'}}")]
    [InlineData("\"alpha beta\"~2", "{'multi_match':{'query':'alpha beta','slop':2,'type':'phrase'}}")]
    [InlineData("john~1", "{'multi_match':{'fuzziness':1,'query':'john'}}")]
    [InlineData("john^2", "{'multi_match':{'boost':2,'query':'john'}}")]
    [InlineData("john*", "{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'query':'john*'}}")]
    [InlineData("/joh?n/", "{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'query':'/joh?n/'}}")]
    [InlineData("\"\\\"now there\"", "{'multi_match':{'query':'\\u0022now there','type':'phrase'}}")]
    public void BuildQuery_WithoutDefaultFields_LetsElasticsearchUseIndexDefaultField(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser();

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("hello", "{'match':{'text':{'query':'hello'}}}")]
    [InlineData("\"a b\"~2", "{'match_phrase':{'text':{'query':'a b','slop':2}}}")]
    [InlineData("hel*", "{'query_string':{'allow_leading_wildcard':true,'analyze_wildcard':true,'fields':['text'],'query':'hel*'}}")]
    [InlineData("/hel.*/", "{'regexp':{'text':{'value':'hel.*'}}}")]
    public void BuildQuery_WithOneAnalyzedDefaultField_UsesSingleFieldQuery(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text"]);

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("hello", "{'term':{'keyword':{'value':'hello'}}}")]
    [InlineData("hel*", "{'prefix':{'keyword':{'value':'hel'}}}")]
    [InlineData("\"a b\"", "{'term':{'keyword':{'value':'a b'}}}")]
    public void BuildQuery_WithOneNonAnalyzedDefaultField_UsesSingleFieldQuery(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["keyword"]);

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Theory]
    [InlineData("hello", "{'multi_match':{'fields':['text','otherText'],'query':'hello'}}")]
    [InlineData("\"a b\"~2", "{'multi_match':{'fields':['text','otherText'],'query':'a b','slop':2,'type':'phrase'}}")]
    [InlineData("value~1", "{'multi_match':{'fields':['text','otherText'],'fuzziness':1,'query':'value'}}")]
    [InlineData("/va.*/", "{'bool':{'should':[{'regexp':{'text':{'value':'va.*'}}},{'regexp':{'otherText':{'value':'va.*'}}}]}}")]
    public void BuildQuery_WithManyAnalyzedDefaultFields_UsesMultiMatch(string query, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text", "otherText"]);

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithManyNonAnalyzedDefaultFields_UsesShouldOfTermQueries()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["keyword", "obj.name"]);

        var result = parser.BuildQuery("value");

        ElasticAssert.Json("{'bool':{'should':[{'term':{'keyword':{'value':'value'}}},{'term':{'obj.name':{'value':'value'}}}]}}", result);
    }

    [Theory]
    [InlineData("hello", "text;keyword", "{'bool':{'should':[{'match':{'text':{'query':'hello'}}},{'term':{'keyword':{'value':'hello'}}}]}}")]
    [InlineData("42", "text;number", "{'bool':{'should':[{'match':{'text':{'query':'42'}}},{'term':{'number':{'value':42}}}]}}")]
    [InlineData("42", "text;otherText;number", "{'bool':{'should':[{'multi_match':{'fields':['text','otherText'],'query':'42'}},{'term':{'number':{'value':42}}}]}}")]
    public void BuildQuery_WithMixedDefaultFields_SplitsAnalyzedAndNonAnalyzedFields(string query, string fields, string expected)
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = fields.Split(';'));

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithTextOnNumericDefaultField_ThrowsValidationException()
    {
        var parser = TestMapping.CreateParser(c => c.DefaultFields = ["keyword", "long"]);

        var exception = Assert.Throws<QueryValidationException>(() => parser.BuildQuery("hello"));

        Assert.Contains("field 'long'", exception.Message);
    }

    [Theory]
    [InlineData("value1 abc", "{'bool':{'minimum_should_match':1,'should':[{'match':{'text':{'query':'value1'}}},{'match':{'text':{'query':'abc'}}}]}}")]
    [InlineData("text:(value1 abc) keyword:(value2 jhk)", "{'bool':{'minimum_should_match':1,'should':[{'bool':{'minimum_should_match':1,'should':[{'match':{'text':{'query':'value1'}}},{'match':{'text':{'query':'abc'}}}]}},{'bool':{'minimum_should_match':1,'should':[{'term':{'keyword':{'value':'value2'}}},{'term':{'keyword':{'value':'jhk'}}}]}}]}}")]
    public void BuildQuery_WithSearchModeAndMultipleTerms_CombinesTermsWithShould(string query, string expected)
    {
        var parser = TestMapping.CreateParser(c =>
        {
            c.UseSearchMode();
            c.DefaultFields = ["text"];
        });

        var result = parser.BuildQuery(query);

        ElasticAssert.Json(expected, result);
    }

    [Fact]
    public void BuildQuery_WithDefaultFieldsPerRequest_OverridesConfiguredFields()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["text"]);

        var result = parser.BuildQuery("hello", new ElasticsearchQueryOptions { DefaultFields = ["keyword"] });

        ElasticAssert.Json("{'term':{'keyword':{'value':'hello'}}}", result);
    }

    [Fact]
    public void BuildQuery_WithDefaultFieldCasing_UsesMappedFieldName()
    {
        var parser = TestMapping.CreateScoringParser(c => c.DefaultFields = ["TEXT", "Keyword"]);

        var result = parser.BuildQuery("hello");

        ElasticAssert.Json("{'bool':{'should':[{'match':{'text':{'query':'hello'}}},{'term':{'keyword':{'value':'hello'}}}]}}", result);
    }
}
