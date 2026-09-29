using System.Globalization;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Tests;

public class ParserTests
{
    private static readonly LuceneParserOptions OrOptions = new() { DefaultOperator = BooleanOperator.Or };

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("a b", "(bool +a +b)")]
    [InlineData("a AND b", "(bool +a +b)")]
    [InlineData("a && b", "(bool +a +b)")]
    [InlineData("a OR b", "(bool ?a ?b)")]
    [InlineData("a || b", "(bool ?a ?b)")]
    [InlineData("a OR b AND c", "(bool ?a ?(bool +b +c))")]
    [InlineData("a AND b OR c", "(bool ?(bool +a +b) ?c)")]
    [InlineData("a b OR c", "(bool ?(bool +a +b) ?c)")]
    [InlineData("a NOT b", "(bool +a -b)")]
    [InlineData("a AND NOT b", "(bool +a -b)")]
    [InlineData("a -b", "(bool +a -b)")]
    [InlineData("a !b", "(bool +a -b)")]
    [InlineData("a OR NOT b", "(bool ?a ?(not b))")]
    [InlineData("NOT a OR b", "(bool ?(not a) ?b)")]
    [InlineData("a OR -b", "(bool ?a -b)")]
    [InlineData("a OR +b", "(bool ?a +b)")]
    [InlineData("-a", "(bool -a)")]
    [InlineData("NOT a", "(bool -a)")]
    [InlineData("!a", "(bool -a)")]
    [InlineData("+a", "(bool +a)")]
    [InlineData("(a OR b) AND (c OR d)", "(bool +(group (bool ?a ?b)) +(group (bool ?c ?d)))")]
    [InlineData("((a))", "(group (group a))")]
    [InlineData("a b c d", "(bool +a +b +c +d)")]
    public void Parse_BooleanExpressions_DefaultAnd(string query, string expected)
    {
        AssertParses(query, expected);
    }

    [Theory]
    [InlineData("a b", "(bool ?a ?b)")]
    [InlineData("a b AND c", "(bool ?a ?(bool +b +c))")]
    [InlineData("a NOT b", "(bool ?a -b)")]
    [InlineData("NOT a b", "(bool -a ?b)")]
    [InlineData("+a b", "(bool +a ?b)")]
    [InlineData("-a b", "(bool -a ?b)")]
    [InlineData("a OR NOT b", "(bool ?a ?(not b))")]
    [InlineData("a NOT b OR c", "(bool ?a ?(not b) ?c)")]
    public void Parse_BooleanExpressions_DefaultOr(string query, string expected)
    {
        AssertParses(query, expected, OrOptions);
    }

    [Theory]
    [InlineData("title:hello", "title:hello")]
    [InlineData("title:\"hello world\"", "title:\"hello world\"")]
    [InlineData("title:(a OR b)", "title:(group (bool ?a ?b))")]
    [InlineData("-title:(a OR b)", "(bool -title:(group (bool ?a ?b)))")]
    [InlineData("user.name:john", "user.name:john")]
    [InlineData("user-name:x", "user-name:x")]
    [InlineData("field1:2", "field1:2")]
    [InlineData("my\\ field:x", "my field:x")]
    [InlineData("book.*:quick", "book.*:quick")]
    [InlineData("date:2024-01-01T10:30:00", "date:2024-01-01T10:30:00")]
    [InlineData("time:12:30", "time:12:30")]
    [InlineData("title:/ab\\/c/", "title:/ab\\/c/")]
    [InlineData("@include:saved", "@include:saved")]
    [InlineData("@include:\"saved query\"", "@include:\"saved query\"")]
    [InlineData("title : hello", "title:hello")]
    public void Parse_FieldQueries(string query, string expected)
    {
        AssertParses(query, expected);
    }

    [Theory]
    [InlineData("foo*", "foo*(prefix)")]
    [InlineData("f*o", "f*o(wildcard)")]
    [InlineData("f?o", "f?o(wildcard)")]
    [InlineData("fo?*", "fo?*(wildcard)")]
    [InlineData("*foo", "*foo(wildcard)")]
    [InlineData("foo\\*", "foo*")]
    [InlineData("foo\\:bar", "foo:bar")]
    [InlineData("c\\\\temp", "c\\temp")]
    [InlineData("foo~", "foo~")]
    [InlineData("foo~1", "foo~1")]
    [InlineData("foo~0.8", "foo~0.8")]
    [InlineData("foo^2", "foo^2")]
    [InlineData("foo^2.5", "foo^2.5")]
    [InlineData("foo~1^2", "foo~1^2")]
    [InlineData("foo^\\+2", "foo^+2")]
    [InlineData("foo~AUTO\\:3,6", "foo~AUTO:3,6")]
    [InlineData("\"hello world\"~2^3", "\"hello world\"~2^3")]
    [InlineData("\"say \\\"hi\\\"\"", "\"say \"hi\"\"")]
    [InlineData("/ab[c]+/", "/ab[c]+/")]
    [InlineData("*", "(all)")]
    [InlineData("*:*", "(all)")]
    [InlineData("#hashtag", "#hashtag")]
    [InlineData("price:$100", "price:$100")]
    [InlineData(".net", ".net")]
    [InlineData("'hello'", "'hello'")]
    [InlineData("a=b", "a=b")]
    [InlineData("a>b", "a>b")]
    [InlineData("a&&b", "a&&b")]
    [InlineData("🚀", "🚀")]
    [InlineData("go TO school", "(bool +go +TO +school)")]
    [InlineData("! a", "(bool +! +a)")]
    [InlineData("- a", "(bool +- +a)")]
    public void Parse_Terms(string query, string expected)
    {
        AssertParses(query, expected);
    }

    [Theory]
    [InlineData("title:*", "(exists title)")]
    [InlineData("_exists_:title", "(exists title)")]
    [InlineData("_missing_:title", "(missing title)")]
    [InlineData("NOT _exists_:title", "(bool -(exists title))")]
    public void Parse_ExistsAndMissing(string query, string expected)
    {
        AssertParses(query, expected);
    }

    [Theory]
    [InlineData("price:[1 TO 5]", "price:[1 TO 5]")]
    [InlineData("price:{1 TO 5]", "price:{1 TO 5]")]
    [InlineData("price:[1 TO 5}", "price:[1 TO 5}")]
    [InlineData("price:[* TO 5]", "price:[* TO 5]")]
    [InlineData("price:[1 TO *]", "price:[1 TO *]")]
    [InlineData("price:[-10 TO -5]", "price:[-10 TO -5]")]
    [InlineData("price:[1.5 TO 2.5]", "price:[1.5 TO 2.5]")]
    [InlineData("price:[1..5]", "price:[1 TO 5]")]
    [InlineData("price:[1 .. 5]", "price:[1 TO 5]")]
    [InlineData("name:[\"a b\" TO \"c d\"]", "name:[a b TO c d]")]
    [InlineData("time:[10:00 TO 12:00]", "time:[10:00 TO 12:00]")]
    [InlineData("date:[now-1d/d TO now]", "date:[now-1d/d TO now]")]
    [InlineData("date:[2024-01-01 TO *]^\"America/Chicago\"", "date:[2024-01-01 TO *]^America/Chicago")]
    [InlineData("price:>5", "price:{5 TO *]")]
    [InlineData("price:>=5", "price:[5 TO *]")]
    [InlineData("price:<5", "price:[* TO 5}")]
    [InlineData("price:<=5", "price:[* TO 5]")]
    [InlineData("price:<-5", "price:[* TO -5}")]
    [InlineData("price:>= 5", "price:[5 TO *]")]
    [InlineData("date:>=2024-01-01T10:30:00", "date:[2024-01-01T10:30:00 TO *]")]
    [InlineData(">5", "{5 TO *]")]
    [InlineData("(price:>5)", "(group price:{5 TO *]})")]
    public void Parse_Ranges(string query, string expected)
    {
        AssertParses(query, expected.Replace("]})", "])"));
    }

    [Theory]
    [InlineData("terms:(status @missing:none min:created~5)", "terms:(group (bool +status +@missing:none +min:created~5))")]
    [InlineData("date:created~1d^-5h", "date:created~1d^-5h")]
    [InlineData("geo:75044~75mi", "geo:75044~75mi")]
    [InlineData("-created +name", "(bool -created +name)")]
    public void Parse_AggregationSortAndGeoSyntax(string query, string expected)
    {
        AssertParses(query, expected);
    }

    [Theory]
    [InlineData("()", "Empty group")]
    [InlineData("(a", "Missing closing ')'")]
    [InlineData("a)", "Unexpected ')'")]
    [InlineData("a AND", "Expected a query after 'AND'")]
    [InlineData("a OR", "Expected a query after 'OR'")]
    [InlineData("AND a", "Unexpected 'AND'")]
    [InlineData("a AND OR b", "Expected a query after 'AND'")]
    [InlineData("\"unterminated", "Unterminated quoted string")]
    [InlineData("/unterminated", "Unterminated regular expression")]
    [InlineData("trailing\\", "Dangling escape")]
    [InlineData("title:-a", "before the field name")]
    [InlineData("title:NOT a", "before the field name")]
    [InlineData("title:", "Expected a value after 'title:'")]
    [InlineData("a:b:c", "Unexpected ':'")]
    [InlineData("NOT NOT a", "Unexpected operator 'NOT'")]
    [InlineData("price:[1 5]", "Expected 'TO'")]
    [InlineData("price:[1 TO 5", "Missing closing ']' or '}'")]
    [InlineData("price:[TO 5]", "Expected a lower bound")]
    [InlineData("price:>", "Expected a value after '>'")]
    [InlineData("foo^", "Expected a value after '^'")]
    [InlineData("/re/~2", "'~' is not supported")]
    [InlineData("foo~1~2", "Duplicate '~'")]
    [InlineData("_exists_:", "Expected a field name")]
    public void Parse_InvalidQuery_ReportsError(string query, string expectedMessage)
    {
        var result = LuceneQuery.Parse(query);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains(expectedMessage, StringComparison.Ordinal));
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void Parse_InvalidQuery_KeepsWhatCouldBeParsed()
    {
        var result = LuceneQuery.Parse("a:b:c OR d");

        Assert.False(result.IsSuccess);
        Assert.Equal("(bool +a:b +(bool ?c ?d))", result.Document.ToDebugString());
    }

    [Fact]
    public void Parse_DeeplyNestedGroups_ReportsErrorInsteadOfOverflowingStack()
    {
        string query = new string('(', 100_000) + "a" + new string(')', 100_000);

        var result = LuceneQuery.Parse(query);

        Assert.Contains(result.Errors, e => e.Code == QueryErrorCode.MaxDepthExceeded);
    }

    [Fact]
    public void Parse_DeeplyNestedFieldGroups_ReportsErrorInsteadOfOverflowingStack()
    {
        string query = string.Concat(Enumerable.Repeat("a:(", 50_000)) + "b" + new string(')', 50_000);

        var result = LuceneQuery.Parse(query);

        Assert.Contains(result.Errors, e => e.Code == QueryErrorCode.MaxDepthExceeded);
    }

    [Fact]
    public void Parse_WithinMaxDepth_Succeeds()
    {
        string query = new string('(', 100) + "a" + new string(')', 100);

        var result = LuceneQuery.Parse(query);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Parse_CustomMaxDepth_IsEnforced()
    {
        var result = LuceneQuery.Parse("((a))", new LuceneParserOptions { MaxDepth = 1 });

        Assert.Contains(result.Errors, e => e.Code == QueryErrorCode.MaxDepthExceeded);
    }

    [Fact]
    public void Parse_LongFlatQuery_IsLinear()
    {
        string query = string.Join(" AND ", Enumerable.Range(0, 50_000).Select(i => $"f{i}:v{i}"));

        var result = LuceneQuery.Parse(query);

        Assert.True(result.IsSuccess);
        var boolean = Assert.IsType<BooleanQueryNode>(result.Document.Query);
        Assert.Equal(50_000, boolean.Clauses.Count);
    }

    [Fact]
    public void Parse_ErrorsAreNotShared()
    {
        var first = LuceneQuery.Parse("a");
        var second = LuceneQuery.Parse("b");

        Assert.Empty(first.Errors);
        Assert.Empty(second.Errors);
        Assert.IsNotType<List<ParseError>>(first.Errors, exactMatch: true);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fi-FI")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void Parse_Modifiers_AreCultureInvariant(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var result = LuceneQuery.Parse("foo^2.5 bar~1");

            var boolean = Assert.IsType<BooleanQueryNode>(result.Document.Query);
            Assert.Equal(2.5f, Assert.IsType<TermNode>(boolean.Clauses[0].Query).Boost);
            Assert.Equal(1, Assert.IsType<TermNode>(boolean.Clauses[1].Query).FuzzyDistance);
            Assert.Equal("foo^2.5 bar~1", QueryStringBuilder.ToQueryString(result.Document));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Parse_TermNode_ExposesRawAndUnescapedValues()
    {
        var result = LuceneQuery.Parse("path:c\\\\temp\\*");

        var field = Assert.IsType<FieldQueryNode>(result.Document.Query);
        var term = Assert.IsType<TermNode>(field.Query);
        Assert.Equal("c\\\\temp\\*", term.Term);
        Assert.Equal("c\\temp*", term.UnescapedTerm);
        Assert.False(term.IsPrefix);
        Assert.False(term.IsWildcard);
    }

    [Fact]
    public void Parse_Positions_AreTracked()
    {
        var result = LuceneQuery.Parse("a AND\n  title:\"x y\"");

        var boolean = Assert.IsType<BooleanQueryNode>(result.Document.Query);
        var field = Assert.IsType<FieldQueryNode>(boolean.Clauses[1].Query);
        Assert.Equal(8, field.StartPosition);
        Assert.Equal(19, field.EndPosition);
        Assert.Equal(2, field.StartLine);
        Assert.Equal(3, field.StartColumn);
    }

    [Fact]
    public void Parse_ErrorPositions_AreTracked()
    {
        var result = LuceneQuery.Parse("a AND\n  b)");

        var error = Assert.Single(result.Errors);
        Assert.Equal(9, error.Position);
        Assert.Equal(2, error.Line);
        Assert.Equal(4, error.Column);
    }

    [Fact]
    public void Parse_EmptyAndWhitespace_ReturnsEmptyDocument()
    {
        Assert.Null(LuceneQuery.Parse("").Document.Query);
        Assert.Null(LuceneQuery.Parse("  \t\n ").Document.Query);
        Assert.True(LuceneQuery.Parse("  ").IsSuccess);
    }

    [Fact]
    public void Parse_NullQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LuceneQuery.Parse((string)null!));
    }

    [Fact]
    public void TryParse_ReturnsFalseWhenThereAreErrors()
    {
        Assert.True(LuceneQuery.TryParse("a AND b", out _));
        Assert.False(LuceneQuery.TryParse("a AND", out var result));
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Clone_CreatesIndependentDeepCopy()
    {
        var document = LuceneQuery.Parse("title:(a OR b^2) AND price:[1 TO 5] -c").Document;
        document.Query!.SetData("key", "value");

        var clone = document.CloneDocument();
        var cloneField = (FieldQueryNode)((BooleanQueryNode)clone.Query!).Clauses[0].Query!;
        cloneField.Field = "changed";

        Assert.Equal(document.ToDebugString().Replace("title:", "changed:"), clone.ToDebugString());
        Assert.Equal("title", ((FieldQueryNode)((BooleanQueryNode)document.Query).Clauses[0].Query!).Field);
        Assert.Equal("value", clone.Query!.GetData<string>("key"));
        Assert.NotSame(document.Query, clone.Query);
    }

    public static IEnumerable<object[]> RoundTripQueries()
    {
        string[] queries =
        [
            "a", "a b", "a AND b", "a OR b", "a OR b AND c", "(a OR b) AND c", "a -b", "a NOT b", "a OR NOT b",
            "NOT a OR b", "+a OR b", "-(a OR b)", "title:(a OR b)", "title:\"hello world\"~2^3", "foo~", "foo~1^2",
            "foo*", "f?o*", "foo\\*", "\\-foo", "\\AND", "foo\\:bar:baz", "my\\ field:x", "\"say \\\"hi\\\"\"",
            "price:[1 TO 5]", "price:{* TO 5]", "name:[\"a b\" TO \"c d\"]", "price:>=5", "price:<-5",
            "date:<=2024-01-01T10:30:00", "date:[2024-01-01 TO *]^\"America/Chicago\"", "_exists_:a", "_missing_:a",
            "a:*", "*:*", "/re[g]ex/^2", "@include:\"a b\"", "(a)^2", "a:(b c)^3", "\"a\"~", "foo^0.125",
            "terms:(status @missing:none min:created~5)", "foo~AUTO\\:3,6", "foo^\\+2", "foo~\\\\x"
        ];

        foreach (string query in queries)
        {
            yield return [query, BooleanOperator.And];
            yield return [query, BooleanOperator.Or];
        }
    }

    [Theory]
    [MemberData(nameof(RoundTripQueries))]
    public void RoundTrip_PreservesMeaning(string query, BooleanOperator defaultOperator)
    {
        var options = new LuceneParserOptions { DefaultOperator = defaultOperator };
        var original = LuceneQuery.Parse(query, options);
        Assert.True(original.IsSuccess, string.Join("; ", original.Errors));

        string text = QueryStringBuilder.ToQueryString(original.Document, defaultOperator);
        var reparsed = LuceneQuery.Parse(text, options);

        Assert.True(reparsed.IsSuccess, $"'{text}': {string.Join("; ", reparsed.Errors)}");
        Assert.Equal(original.Document.ToDebugString(), reparsed.Document.ToDebugString());
    }

    [Theory]
    [InlineData("a AND b OR c")]
    [InlineData("title:(a OR b) -c")]
    [InlineData("price:[1 TO 5] AND date:>=2024-01-01")]
    [InlineData("\"a b\"~2^3 foo~1 bar^2")]
    [InlineData("a OR NOT b")]
    public void RoundTrip_PreservesText(string query)
    {
        var result = LuceneQuery.Parse(query);

        Assert.Equal(query, QueryStringBuilder.ToQueryString(result.Document));
    }

    [Fact]
    public void RoundTrip_AcrossDefaultOperators_PreservesMeaning()
    {
        var andDocument = LuceneQuery.Parse("a b OR c").Document;

        string text = QueryStringBuilder.ToQueryString(andDocument, BooleanOperator.Or);
        var orDocument = LuceneQuery.Parse(text, OrOptions).Document;

        Assert.Equal(andDocument.ToDebugString(), orDocument.ToDebugString());
    }

    [Fact]
    public void Parse_RandomInput_NeverThrowsAndRoundTripsWhenValid()
    {
        const string alphabet = "ab AND OR NOT:()[]{}\"\\/^~*?+-!<>=.TO 01 _exists_ @@&&|| \t\n";
        var random = new Random(12345);
        for (int i = 0; i < 20_000; i++)
        {
            int length = random.Next(1, 30);
            var chars = new char[length];
            for (int j = 0; j < length; j++)
                chars[j] = alphabet[random.Next(alphabet.Length)];

            string query = new(chars);
            foreach (var options in new[] { LuceneParserOptions.Default, OrOptions })
            {
                var result = LuceneQuery.Parse(query, options);
                Assert.NotNull(result.Document);
                if (!result.IsSuccess)
                    continue;

                string text = QueryStringBuilder.ToQueryString(result.Document, options.DefaultOperator);
                var reparsed = LuceneQuery.Parse(text, options);
                Assert.True(reparsed.IsSuccess, $"'{query}' -> '{text}': {string.Join("; ", reparsed.Errors)}");
                Assert.Equal(result.Document.ToDebugString(), reparsed.Document.ToDebugString());
            }
        }
    }

    private static void AssertParses(string query, string expected, LuceneParserOptions? options = null)
    {
        var result = LuceneQuery.Parse(query, options);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.Equal(expected, result.Document.ToDebugString());
    }
}
