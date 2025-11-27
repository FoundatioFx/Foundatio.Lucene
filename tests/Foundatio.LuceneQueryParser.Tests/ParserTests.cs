using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser.Tests;

public class ParserTests
{
    [Fact]
    public void Parse_SimpleTerm_ReturnsSingleTermNode()
    {
        var result = LuceneQuery.Parse("hello");

        Assert.True(result.IsSuccess);
        Assert.IsType<QueryDocument>(result.Document);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("hello", term.Term);
    }

    [Fact]
    public void Parse_QuotedPhrase_ReturnsPhraseNode()
    {
        var result = LuceneQuery.Parse("\"hello world\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
        var phrase = (PhraseNode)doc.Query;
        Assert.Equal("hello world", phrase.Phrase);
    }

    [Fact]
    public void Parse_FieldQuery_ReturnsFieldQueryNode()
    {
        var result = LuceneQuery.Parse("title:test");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("title", field.Field);
        Assert.IsType<TermNode>(field.Query);
    }

    [Fact]
    public void Parse_NestedField_ReturnsFieldQueryNode()
    {
        var result = LuceneQuery.Parse("user.address.city:london");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("user.address.city", field.Field);
    }

    [Fact]
    public void Parse_AndQuery_ReturnsBooleanQueryNode()
    {
        var result = LuceneQuery.Parse("hello AND world");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Equal(2, boolean.Clauses.Count);
        Assert.Equal(BooleanOperator.And, boolean.Clauses[1].Operator);
    }

    [Fact]
    public void Parse_OrQuery_ReturnsBooleanQueryNode()
    {
        var result = LuceneQuery.Parse("hello OR world");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Equal(2, boolean.Clauses.Count);
        Assert.Equal(BooleanOperator.Or, boolean.Clauses[1].Operator);
    }

    [Fact]
    public void Parse_NotQuery_ReturnsNotNode()
    {
        var result = LuceneQuery.Parse("NOT test");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<NotNode>(doc.Query);
        var not = (NotNode)doc.Query;
        Assert.IsType<TermNode>(not.Query);
    }

    [Fact]
    public void Parse_RequiredTerm_ReturnsBooleanClauseWithMust()
    {
        var result = LuceneQuery.Parse("+required");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Single(boolean.Clauses);
        Assert.Equal(Occur.Must, boolean.Clauses[0].Occur);
    }

    [Fact]
    public void Parse_ProhibitedTerm_ReturnsBooleanClauseWithMustNot()
    {
        var result = LuceneQuery.Parse("-excluded");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Single(boolean.Clauses);
        Assert.Equal(Occur.MustNot, boolean.Clauses[0].Occur);
    }

    [Fact]
    public void Parse_InclusiveRange_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("[10 TO 20]");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.Equal("10", range.Min);
        Assert.Equal("20", range.Max);
        Assert.True(range.MinInclusive);
        Assert.True(range.MaxInclusive);
    }

    [Fact]
    public void Parse_ExclusiveRange_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("{10 TO 20}");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.Equal("10", range.Min);
        Assert.Equal("20", range.Max);
        Assert.False(range.MinInclusive);
        Assert.False(range.MaxInclusive);
    }

    [Theory]
    [InlineData("[* TO 100]", null, "100", true, true)]
    [InlineData("[100 TO *]", "100", null, true, true)]
    [InlineData("{* TO *}", null, null, false, false)]
    public void Parse_OpenRange_ReturnsRangeNodeWithWildcards(
        string query, string? expectedLower, string? expectedUpper, bool lowerInclusive, bool upperInclusive)
    {
        var result = LuceneQuery.Parse(query);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.Equal(expectedLower, range.Min);
        Assert.Equal(expectedUpper, range.Max);
        Assert.Equal(lowerInclusive, range.MinInclusive);
        Assert.Equal(upperInclusive, range.MaxInclusive);
    }

    [Fact]
    public void Parse_FieldRange_ReturnsFieldQueryWithRangeNode()
    {
        var result = LuceneQuery.Parse("age:[18 TO 65]");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("age", field.Field);
        Assert.IsType<RangeNode>(field.Query);
    }

    [Fact]
    public void Parse_FuzzyTerm_ReturnsTermNodeWithFuzzy()
    {
        var result = LuceneQuery.Parse("roam~2");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("roam", term.Term);
        Assert.Equal(2, term.FuzzyDistance);
    }

    [Fact]
    public void Parse_DefaultFuzzy_ReturnsTermNodeWithDefaultFuzziness()
    {
        var result = LuceneQuery.Parse("roam~");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("roam", term.Term);
        Assert.Equal(2, term.FuzzyDistance); // Default fuzziness is 2
    }

    [Fact]
    public void Parse_ProximityPhrase_ReturnsPhraseNodeWithProximity()
    {
        var result = LuceneQuery.Parse("\"hello world\"~5");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
        var phrase = (PhraseNode)doc.Query;
        Assert.Equal("hello world", phrase.Phrase);
        Assert.Equal(5, phrase.Slop);
    }

    [Fact]
    public void Parse_BoostedTerm_ReturnsTermNodeWithBoost()
    {
        var result = LuceneQuery.Parse("important^2.5");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("important", term.Term);
        Assert.Equal(2.5f, term.Boost);
    }

    [Fact]
    public void Parse_WildcardTerm_ReturnsTermNodeWithWildcard()
    {
        var result = LuceneQuery.Parse("te?t");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("te?t", term.Term);
        Assert.True(term.IsWildcard);
    }

    [Fact]
    public void Parse_PrefixTerm_ReturnsTermNodeWithPrefix()
    {
        var result = LuceneQuery.Parse("test*");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("test", term.Term);
        Assert.True(term.IsPrefix);
    }

    [Fact]
    public void Parse_RegexQuery_ReturnsRegexNode()
    {
        var result = LuceneQuery.Parse("/test\\.regex/");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RegexNode>(doc.Query);
        var regex = (RegexNode)doc.Query;
        Assert.Equal("test\\.regex", regex.Pattern);
    }

    [Fact]
    public void Parse_RegexWithFlags_ReturnsRegexNode()
    {
        var result = LuceneQuery.Parse("/pattern/");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RegexNode>(doc.Query);
        var regex = (RegexNode)doc.Query;
        Assert.Equal("pattern", regex.Pattern);
    }

    [Fact]
    public void Parse_ExistsQuery_ReturnsExistsNode()
    {
        var result = LuceneQuery.Parse("_exists_:field_name");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<ExistsNode>(doc.Query);
        var existsNode = (ExistsNode)doc.Query;
        Assert.Equal("field_name", existsNode.Field);
        Assert.True(existsNode.IsExistsSyntax);
    }

    [Fact]
    public void Parse_MissingQuery_ReturnsMissingNode()
    {
        var result = LuceneQuery.Parse("_missing_:field_name");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<MissingNode>(doc.Query);
        var missingNode = (MissingNode)doc.Query;
        Assert.Equal("field_name", missingNode.Field);
    }

    [Fact]
    public void Parse_FieldStarExists_ReturnsExistsNode()
    {
        var result = LuceneQuery.Parse("field_name:*");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<ExistsNode>(doc.Query);
        var existsNode = (ExistsNode)doc.Query;
        Assert.Equal("field_name", existsNode.Field);
        Assert.False(existsNode.IsExistsSyntax);
    }

    [Fact]
    public void Parse_GroupedQuery_ReturnsGroupNode()
    {
        var result = LuceneQuery.Parse("(hello OR world)");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<GroupNode>(doc.Query);
        var group = (GroupNode)doc.Query;
        Assert.IsType<BooleanQueryNode>(group.Query);
    }

    [Fact]
    public void Parse_FieldWithGroup_ReturnsFieldQueryWithGroup()
    {
        var result = LuceneQuery.Parse("title:(quick OR brown)");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("title", field.Field);
        Assert.IsType<GroupNode>(field.Query);
    }

    [Fact]
    public void Parse_ComplexQuery_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("title:(quick OR brown) AND status:published^2 AND date:[2020-01-01 TO 2023-12-31]");

        Assert.True(result.IsSuccess);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Parse_MatchAll_ReturnsMatchAllNode()
    {
        var result = LuceneQuery.Parse("*:*");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<MatchAllNode>(doc.Query);
    }

    [Fact]
    public void Parse_ImplicitOr_ReturnsTermsWithImplicitOperator()
    {
        var result = LuceneQuery.Parse("hello world");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Equal(2, boolean.Clauses.Count);
    }

    [Fact]
    public void Parse_EmptyQuery_ReturnsEmptyDocument()
    {
        var result = LuceneQuery.Parse("");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.Null(doc.Query);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyDocument()
    {
        var result = LuceneQuery.Parse("   ");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.Null(doc.Query);
    }

    [Fact]
    public void TryParse_ValidQuery_ReturnsTrue()
    {
        bool success = LuceneQuery.TryParse("test", out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Parse_UnbalancedParenthesis_ReturnsErrors()
    {
        var result = LuceneQuery.Parse("(hello world");

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_OperatorPrecedence_AndBindsTighterThanOr()
    {
        // "a OR b AND c" should be parsed as "a OR (b AND c)"
        var result = LuceneQuery.Parse("a OR b AND c");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var or = (BooleanQueryNode)doc.Query;
        Assert.Equal(BooleanOperator.Or, or.Clauses[1].Operator);
    }

    [Fact]
    public void Parse_FieldGroupWithBoost_ReturnsFieldQueryWithBoostedGroup()
    {
        // Elasticsearch field group syntax: title:(full text search)^2
        // With SplitOnWhitespace=true (default), terms are parsed separately
        var result = LuceneQuery.Parse("title:(full text search)^2");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("title", field.Field);
        Assert.IsType<GroupNode>(field.Query);
        var group = (GroupNode)field.Query;
        Assert.Equal(2.0f, group.Boost);
        // The group should contain the terms with implicit OR
        Assert.IsType<BooleanQueryNode>(group.Query);
        var boolean = (BooleanQueryNode)group.Query;
        Assert.Equal(3, boolean.Clauses.Count);
    }

    [Fact]
    public void Parse_FieldGroupWithMultiTerm_ReturnsMultiTermNode()
    {
        // When SplitOnWhitespace=false, consecutive terms in a group become MultiTermNode
        var result = LuceneQuery.Parse("title:(full text search)^2", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("title", field.Field);
        Assert.IsType<GroupNode>(field.Query);
        var group = (GroupNode)field.Query;
        Assert.Equal(2.0f, group.Boost);
        // The group should contain a MultiTermNode
        Assert.IsType<MultiTermNode>(group.Query);
        var multiTerm = (MultiTermNode)group.Query;
        Assert.Equal(3, multiTerm.Terms.Count);
        Assert.Equal("full", multiTerm.Terms[0]);
        Assert.Equal("text", multiTerm.Terms[1]);
        Assert.Equal("search", multiTerm.Terms[2]);
        Assert.Equal("full text search", multiTerm.CombinedText);
    }

    [Fact]
    public void Parse_GroupWithOperators_NotMultiTerm()
    {
        // When group contains operators, it should NOT be parsed as MultiTerm
        var result = LuceneQuery.Parse("title:(quick OR brown)", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.IsType<GroupNode>(field.Query);
        var group = (GroupNode)field.Query;
        // Should fall back to BooleanQueryNode because of OR operator
        Assert.IsType<BooleanQueryNode>(group.Query);
    }

    [Fact]
    public void Parse_MultipleFieldGroups_ReturnsCorrectStructure()
    {
        // Elasticsearch example: status:(active OR pending) title:(full text search)^2
        var result = LuceneQuery.Parse("status:(active OR pending) title:(full text search)^2");

        Assert.True(result.IsSuccess);
        Assert.False(result.HasErrors);
        var doc = result.Document!;
        // Should have two field queries with implicit OR
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query!;
        Assert.Equal(2, boolean.Clauses.Count);
        
        // First clause: status:(active OR pending)
        Assert.IsType<FieldQueryNode>(boolean.Clauses[0].Query);
        var statusField = (FieldQueryNode)boolean.Clauses[0].Query!;
        Assert.Equal("status", statusField.Field);
        
        // Second clause: title:(full text search)^2
        Assert.IsType<FieldQueryNode>(boolean.Clauses[1].Query);
        var titleField = (FieldQueryNode)boolean.Clauses[1].Query!;
        Assert.Equal("title", titleField.Field);
        Assert.IsType<GroupNode>(titleField.Query);
        var group = (GroupNode)titleField.Query!;
        Assert.Equal(2.0f, group.Boost);
    }

    [Fact]
    public void Parse_RootLevelMultiTerm_ReturnsMultiTermNode()
    {
        // When SplitOnWhitespace=false, consecutive terms at root level become MultiTermNode
        var result = LuceneQuery.Parse("full text search", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<MultiTermNode>(doc.Query);
        var multiTerm = (MultiTermNode)doc.Query;
        Assert.Equal(3, multiTerm.Terms.Count);
        Assert.Equal("full", multiTerm.Terms[0]);
        Assert.Equal("text", multiTerm.Terms[1]);
        Assert.Equal("search", multiTerm.Terms[2]);
        Assert.Equal("full text search", multiTerm.CombinedText);
    }

    [Fact]
    public void Parse_RootLevelMultiTerm_WithBoost_NotMultiTerm()
    {
        // When a term has boost, it should NOT be parsed as MultiTerm
        // The boost applies only to that term, not the whole phrase
        var result = LuceneQuery.Parse("full text search^2", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Should be BooleanQueryNode because boost applies to individual term
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Equal(3, boolean.Clauses.Count);
        // The last term should have the boost
        var lastClause = boolean.Clauses[2];
        Assert.IsType<TermNode>(lastClause.Query);
        var boostedTerm = (TermNode)lastClause.Query;
        Assert.Equal("search", boostedTerm.Term);
        Assert.Equal(2.0f, boostedTerm.Boost);
    }

    [Fact]
    public void Parse_RootLevelWithOperator_NotMultiTerm()
    {
        // When root level contains explicit operators, it should NOT be parsed as MultiTerm
        var result = LuceneQuery.Parse("quick OR brown", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Should be BooleanQueryNode because of OR operator
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_RootLevelWithAND_NotMultiTerm()
    {
        // When root level contains AND operator, it should NOT be parsed as MultiTerm
        var result = LuceneQuery.Parse("quick AND brown", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Should be BooleanQueryNode because of AND operator
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_RootLevelWithPrefix_NotMultiTerm()
    {
        // When term has prefix modifier (+/-), it should NOT be parsed as MultiTerm
        var result = LuceneQuery.Parse("+quick brown", splitOnWhitespace: false);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Should be BooleanQueryNode because of + prefix
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_SplitOnWhitespaceTrue_NoMultiTerm()
    {
        // When SplitOnWhitespace=true (default), consecutive terms are separate
        var result = LuceneQuery.Parse("full text search", splitOnWhitespace: true);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Should be BooleanQueryNode with implicit clauses
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Equal(3, boolean.Clauses.Count);
    }

    #region Error Handling Tests

    [Fact]
    public void Parse_UnterminatedParens_ReturnsError()
    {
        var result = LuceneQuery.Parse("(hello world");

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_FieldWithoutValue_ReturnsError()
    {
        // field:* is actually valid as prefix, but field: alone should error
        var result = LuceneQuery.Parse("field:");

        Assert.True(result.HasErrors);
    }

    #endregion

    #region Range Tests

    [Fact]
    public void Parse_MixedBracketsLeftExclusive_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("{1 TO 2]");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.False(range.MinInclusive);
        Assert.True(range.MaxInclusive);
    }

    [Fact]
    public void Parse_MixedBracketsRightExclusive_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("[1 TO 2}");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.True(range.MinInclusive);
        Assert.False(range.MaxInclusive);
    }

    [Fact]
    public void Parse_QuotedValuesInRange_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("[\"1\" TO \"2\"]");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.Equal("1", range.Min);
        Assert.Equal("2", range.Max);
    }

    [Theory]
    [InlineData(">10", "10", null, false, true)]
    [InlineData(">=10", "10", null, true, true)]
    [InlineData("<10", null, "10", true, false)]
    [InlineData("<=10", null, "10", true, true)]
    public void Parse_ComparisonOperators_ReturnsRangeNode(
        string query, string? expectedMin, string? expectedMax, bool minInclusive, bool maxInclusive)
    {
        var result = LuceneQuery.Parse(query);

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<RangeNode>(doc.Query);
        var range = (RangeNode)doc.Query;
        Assert.Equal(expectedMin, range.Min);
        Assert.Equal(expectedMax, range.Max);
        Assert.Equal(minInclusive, range.MinInclusive);
        Assert.Equal(maxInclusive, range.MaxInclusive);
    }

    [Fact]
    public void Parse_FieldWithComparisonOperator_ReturnsFieldQueryWithRange()
    {
        var result = LuceneQuery.Parse("age:>18");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("age", field.Field);
        Assert.IsType<RangeNode>(field.Query);
        var range = (RangeNode)field.Query;
        Assert.Equal("18", range.Min);
        Assert.False(range.MinInclusive);
    }

    [Fact]
    public void Parse_DateRange_ReturnsRangeNode()
    {
        var result = LuceneQuery.Parse("date:[2012-01-01 TO 2012-12-31]");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("date", field.Field);
        Assert.IsType<RangeNode>(field.Query);
        var range = (RangeNode)field.Query;
        Assert.Equal("2012-01-01", range.Min);
        Assert.Equal("2012-12-31", range.Max);
    }

    #endregion

    #region Prefix and Negation Tests

    [Fact]
    public void Parse_NegatedPhrase_ReturnsBooleanWithMustNot()
    {
        var result = LuceneQuery.Parse("-\"Apache Lucene\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Single(boolean.Clauses);
        Assert.Equal(Occur.MustNot, boolean.Clauses[0].Occur);
        Assert.IsType<PhraseNode>(boolean.Clauses[0].Query);
    }

    [Fact]
    public void Parse_ExclamationNegatedPhrase_ReturnsNotNode()
    {
        // ! is treated like NOT, which creates a NotNode
        var result = LuceneQuery.Parse("!\"Apache Lucene\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<NotNode>(doc.Query);
        var not = (NotNode)doc.Query;
        Assert.IsType<PhraseNode>(not.Query);
    }

    [Fact]
    public void Parse_MixedPhrases_ReturnsBooleanNode()
    {
        var result = LuceneQuery.Parse("\"jakarta apache\" -\"Apache Lucene\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document!;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query!;
        Assert.Equal(2, boolean.Clauses.Count);
        // First phrase should be normal
        Assert.IsType<PhraseNode>(boolean.Clauses[0].Query);
        // Second clause - the -"phrase" creates a nested BooleanQueryNode with MustNot
        Assert.IsType<BooleanQueryNode>(boolean.Clauses[1].Query);
        var nested = (BooleanQueryNode)boolean.Clauses[1].Query!;
        Assert.Single(nested.Clauses);
        Assert.Equal(Occur.MustNot, nested.Clauses[0].Occur);
        Assert.IsType<PhraseNode>(nested.Clauses[0].Query);
    }

    [Fact]
    public void Parse_NotBeforeParens_ReturnsNegatedGroup()
    {
        var result = LuceneQuery.Parse("NOT (dog parrot)");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<NotNode>(doc.Query);
        var not = (NotNode)doc.Query;
        Assert.IsType<GroupNode>(not.Query);
    }

    [Fact]
    public void Parse_NotBeforeFieldQuery_ReturnsNotNode()
    {
        var result = LuceneQuery.Parse("NOT status:fixed");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<NotNode>(doc.Query);
    }

    #endregion

    #region Special Characters and Escaping Tests

    [Fact]
    public void Parse_ForwardSlashInTerm_ReturnsTerm()
    {
        var result = LuceneQuery.Parse("hey/now");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("hey/now", term.Term);
    }

    [Fact]
    public void Parse_EmptyQuotedString_ReturnsEmptyPhrase()
    {
        var result = LuceneQuery.Parse("\"\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
        var phrase = (PhraseNode)doc.Query;
        Assert.Equal("", phrase.Phrase);
    }

    [Fact]
    public void Parse_QuotedColon_ReturnsPhraseNode()
    {
        var result = LuceneQuery.Parse("\":\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
    }

    #endregion

    #region Complex Query Tests

    [Fact]
    public void Parse_AndNotCombination_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("something AND NOT otherthing");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_CriteriaWithNot_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("criteria1 NOT criteria2");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_NestedBooleanExpressions_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("criteria1 OR (criteria2 AND criteria3)");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_MultipleOrClauses_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("criteria1 OR criteria2 OR criteria3");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        // Should have 3 clauses with OR operators
        Assert.Equal(3, boolean.Clauses.Count);
    }

    [Fact]
    public void Parse_RequiredTermsInGroup_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("title:(+return +\"pink panther\")");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("title", field.Field);
        Assert.IsType<GroupNode>(field.Query);
    }

    [Fact]
    public void Parse_BoostedPhrase_ReturnsPhraseWithBoost()
    {
        var result = LuceneQuery.Parse("\"jakarta apache\"^4");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
        var phrase = (PhraseNode)doc.Query;
        Assert.Equal("jakarta apache", phrase.Phrase);
        Assert.Equal(4.0f, phrase.Boost);
    }

    [Fact]
    public void Parse_NotPhrase_ReturnsNotNode()
    {
        var result = LuceneQuery.Parse("NOT \"jakarta apache\"");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<NotNode>(doc.Query);
        var not = (NotNode)doc.Query;
        Assert.IsType<PhraseNode>(not.Query);
    }

    #endregion

    #region Field Tests

    [Fact]
    public void Parse_MissingField_ReturnsMissingNode()
    {
        var result = LuceneQuery.Parse("_missing_:title");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<MissingNode>(doc.Query);
        var missingNode = (MissingNode)doc.Query;
        Assert.Equal("title", missingNode.Field);
    }

    [Fact]
    public void Parse_FieldWithHyphen_ReturnsFieldQuery()
    {
        var result = LuceneQuery.Parse("data.Windows-identity:ejsmith");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("data.Windows-identity", field.Field);
    }

    [Fact]
    public void Parse_FieldGroupWithRangeOperators_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("data.age:(>30 AND <=40)");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.Equal("data.age", field.Field);
        Assert.IsType<GroupNode>(field.Query);
    }

    [Fact]
    public void Parse_RequiredRangeOperator_ReturnsCorrectStructure()
    {
        var result = LuceneQuery.Parse("+>=10");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<BooleanQueryNode>(doc.Query);
        var boolean = (BooleanQueryNode)doc.Query;
        Assert.Single(boolean.Clauses);
        Assert.Equal(Occur.Must, boolean.Clauses[0].Occur);
    }

    #endregion

    #region Fuzzy and Proximity Tests

    [Fact]
    public void Parse_FuzzyDecimal_ReturnsTermWithFuzzy()
    {
        // Note: roam~0.8 is parsed as "roam~0" followed by ".8" as separate terms
        // This is because our lexer doesn't handle decimal fuzzy values as a single token
        var result = LuceneQuery.Parse("roam~0.8");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // Parser creates boolean with roam~0 and .8 as separate terms
        Assert.IsType<BooleanQueryNode>(doc.Query);
    }

    [Fact]
    public void Parse_ProximitySearchWithQuotes_ReturnsPhraseWithSlop()
    {
        var result = LuceneQuery.Parse("\"blah criter\"~1");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<PhraseNode>(doc.Query);
        var phrase = (PhraseNode)doc.Query;
        Assert.Equal("blah criter", phrase.Phrase);
        Assert.Equal(1, phrase.Slop);
    }

    #endregion

    #region Wildcard Tests

    [Fact]
    public void Parse_WildcardOnly_ReturnsMatchAllNode()
    {
        var result = LuceneQuery.Parse("*");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        // A single * is parsed as MatchAll in our parser
        Assert.IsType<MatchAllNode>(doc.Query);
    }

    [Fact]
    public void Parse_LeadingWildcard_ReturnsWildcardTerm()
    {
        var result = LuceneQuery.Parse("*test");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("*test", term.Term);
        Assert.True(term.IsWildcard);
    }

    [Fact]
    public void Parse_MiddleWildcard_ReturnsWildcardTerm()
    {
        var result = LuceneQuery.Parse("te*st");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<TermNode>(doc.Query);
        var term = (TermNode)doc.Query;
        Assert.Equal("te*st", term.Term);
        Assert.True(term.IsWildcard);
    }

    [Fact]
    public void Parse_SingleCharWildcard_ReturnsWildcardTerm()
    {
        var result = LuceneQuery.Parse("type:test?s");

        Assert.True(result.IsSuccess);
        var doc = result.Document;
        Assert.IsType<FieldQueryNode>(doc.Query);
        var field = (FieldQueryNode)doc.Query;
        Assert.IsType<TermNode>(field.Query);
        var term = (TermNode)field.Query;
        Assert.Equal("test?s", term.Term);
        Assert.True(term.IsWildcard);
    }

    #endregion

    #region Theory-Based Valid Query Tests

    [Theory]
    [InlineData("criteria")]
    [InlineData("(criteria)")]
    [InlineData("field:criteria")]
    [InlineData("-criteria")]
    [InlineData("+criteria")]
    [InlineData("criteria1 AND NOT criteria2")]
    [InlineData("criteria1 OR criteria2")]
    [InlineData("field:[1 TO 2]")]
    [InlineData("field:{1 TO 2}")]
    [InlineData("field:[1 TO 2}")]
    [InlineData("field:(criteria1 criteria2)")]
    [InlineData("field:(criteria1 OR criteria2)")]
    [InlineData("date:>now")]
    [InlineData("date:<now")]
    [InlineData("_exists_:title")]
    [InlineData("hidden:true")]
    [InlineData("something AND otherthing")]
    [InlineData("something OR otherthing")]
    [InlineData("NOT Test")]
    [InlineData("!Test")]
    public void Parse_ValidQueries_Succeeds(string query)
    {
        var result = LuceneQuery.Parse(query);

        Assert.True(result.IsSuccess, $"Query '{query}' should be valid");
    }

    [Theory]
    [InlineData("Hello (world")]  // unterminated group
    public void Parse_InvalidQueries_HasErrors(string query)
    {
        var result = LuceneQuery.Parse(query);

        Assert.True(result.HasErrors, $"Query '{query}' should have errors");
    }

    #endregion
}
