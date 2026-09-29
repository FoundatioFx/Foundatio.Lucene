using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("hello", QueryOperations.Term, "")]
    [InlineData("f:hello", QueryOperations.Term, "f")]
    [InlineData("f:\"a b\"", QueryOperations.Phrase, "f")]
    [InlineData("f:hel*", QueryOperations.Prefix, "f")]
    [InlineData("f:h?l*o", QueryOperations.Wildcard, "f")]
    [InlineData("f:hello~", QueryOperations.Fuzzy, "f")]
    [InlineData("f:/h.*/", QueryOperations.Regex, "f")]
    [InlineData("f:[1 TO 2]", QueryOperations.Range, "f")]
    [InlineData("f:>=1", QueryOperations.Range, "f")]
    [InlineData("_exists_:f", QueryOperations.Exists, "f")]
    [InlineData("f:*", QueryOperations.Exists, "f")]
    [InlineData("_missing_:f", QueryOperations.Missing, "f")]
    [InlineData("*:*", QueryOperations.MatchAll, "")]
    [InlineData("f:(a OR b)", QueryOperations.Term, "f")]
    public void Run_Query_RecordsOperationWithField(string query, string operation, string field)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;

        // Act
        var result = ValidationVisitor.Run(document);

        // Assert
        Assert.True(result.IsValid, result.Message);
        var entry = Assert.Single(result.Operations);
        Assert.Equal(operation, entry.Key);
        Assert.Equal([field], entry.Value);
    }

    [Fact]
    public void Run_SpecialFields_AreNotRecorded()
    {
        var result = ValidationVisitor.Run(LuceneQuery.Parse("@custom:value a:1").Document);

        Assert.Equal(["a"], result.ReferencedFields);
        Assert.Equal(["a"], result.Operations[QueryOperations.Term]);
    }

    [Fact]
    public void Run_ResolvedFields_RecordsWrittenAndResolvedNames()
    {
        // Arrange
        var document = LuceneQuery.Parse("user:john _exists_:email other:x").Document;
        var context = new QueryVisitorContext();
        FieldResolverQueryVisitor.Run(document, new FieldMap { { "user", "account.user" }, { "email", "contact.email" } }, context);

        // Act
        var result = ValidationVisitor.Run(document, context);

        // Assert
        Assert.Equal(["email", "other", "user"], result.ReferencedFields.Order());
        Assert.Equal(["account.user", "contact.email", "other"], result.ResolvedFields.Order());
        Assert.Equal(["account.user", "other"], result.Operations[QueryOperations.Term].Order());
    }

    [Fact]
    public void Run_TermsWithoutField_ReferenceDefaultFields()
    {
        // Arrange
        var document = LuceneQuery.Parse("hello f:x").Document;
        var context = new QueryVisitorContext
        {
            DefaultFields = ["title", "body"],
            FieldMap = new FieldMap { { "body", "content.body" } }
        };

        // Act
        var result = ValidationVisitor.Run(document, context);

        // Assert
        Assert.Equal(["body", "f", "title"], result.ReferencedFields.Order());
        Assert.Equal(["content.body", "f", "title"], result.ResolvedFields.Order());
    }

    [Fact]
    public void Run_TermsWithoutFieldAndRestrictedDefaultField_ReturnsError()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext { DefaultFields = ["title", "secret"], ValidationOptions = options };

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse("hello").Document, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(result.ValidationErrors).Code);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("(a)", 2)]
    [InlineData("((a))", 3)]
    [InlineData("(a) (b)", 2)]
    [InlineData("f:(a OR (b c))", 3)]
    [InlineData("a:1 AND b:2 OR c:3", 1)]
    [InlineData("((a) OR ((((b)))))", 6)]
    public void Run_Groups_TracksMaxNodeDepth(string query, int expected)
    {
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document);

        Assert.Equal(expected, result.MaxNodeDepth);
    }

    [Theory]
    [InlineData("((a))", 3, true)]
    [InlineData("(((a)))", 3, false)]
    [InlineData("f:((a))", 2, false)]
    [InlineData("a b c d e", 1, true)]
    public void Run_AllowedMaxNodeDepth_EnforcesLimit(string query, int max, bool valid)
    {
        // Arrange
        var options = new QueryValidationOptions { AllowedMaxNodeDepth = max };

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(QueryErrorCode.MaxDepthExceeded, Assert.Single(result.ValidationErrors).Code);
    }

    [Theory]
    [InlineData("*foo", false)]
    [InlineData("?oo", false)]
    [InlineData("f:*foo*", false)]
    [InlineData("f:?", false)]
    [InlineData("a OR (b AND f:*x)", false)]
    [InlineData("foo*", true)]
    [InlineData("f:fo?o", true)]
    [InlineData("f:\\*foo", true)]
    [InlineData("\"*foo\"", true)]
    [InlineData("*", true)]
    [InlineData("f:*", true)]
    public void Run_LeadingWildcardsNotAllowed_RejectsLeadingWildcardTerms(string query, bool valid)
    {
        // Arrange
        var options = new QueryValidationOptions { AllowLeadingWildcards = false };

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(QueryErrorCode.LeadingWildcardNotAllowed, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void Run_LeadingWildcardsAllowedByDefault_IsValid()
    {
        var result = ValidationVisitor.Run(LuceneQuery.Parse("*foo ?bar").Document, new QueryValidationOptions());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("data:1", true)]
    [InlineData("data.age:1", true)]
    [InlineData("DATA.Age.Deep:1", true)]
    [InlineData("name:x", true)]
    [InlineData("_exists_:data.x", true)]
    [InlineData("database:1", false)]
    [InlineData("dat:1", false)]
    [InlineData("name.first:x", true)]
    [InlineData("other:1", false)]
    [InlineData("_missing_:other", false)]
    [InlineData("name:x OR (other:1)", false)]
    [InlineData("hello", true)]
    public void Run_AllowedFields_AllowsListedFieldsAndSubFields(string query, bool valid)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("data");
        options.AllowedFields.Add("name");

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(QueryErrorCode.FieldNotAllowed, Assert.Single(result.ValidationErrors).Code);
    }

    [Theory]
    [InlineData("secret:1")]
    [InlineData("SECRET:1")]
    [InlineData("secret.keyword:1")]
    [InlineData("secret.a.b:1")]
    [InlineData("_exists_:secret")]
    [InlineData("_missing_:secret.raw")]
    [InlineData("secret:*")]
    [InlineData("sec\\ret:1")]
    [InlineData("-secret:1")]
    [InlineData("a:1 OR NOT secret:1")]
    [InlineData("x:(y OR (secret:1))")]
    public void Run_RestrictedFields_RejectsFieldAndSubFields(string query)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.False(result.IsValid);
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal(QueryErrorCode.FieldRestricted, error.Code);
    }

    [Theory]
    [InlineData("secretive:1")]
    [InlineData("my.secret:1")]
    [InlineData("secre:1")]
    [InlineData("secret")]
    public void Run_RestrictedFields_AllowsOtherFields(string query)
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        Assert.True(result.IsValid, result.Message);
    }

    [Theory]
    [InlineData("_exists_:secr*")]
    [InlineData("secr*:1")]
    [InlineData("sec?et:1")]
    [InlineData("*:1")]
    [InlineData("_missing_:*")]
    public void Run_WildcardFieldNamesWithFieldRules_ReturnsError(string query)
    {
        // Arrange
        var restricted = new QueryValidationOptions();
        restricted.RestrictedFields.Add("secret");
        var allowed = new QueryValidationOptions();
        allowed.AllowedFields.Add("public");
        allowed.AllowedFields.Add("*");

        // Act
        var restrictedResult = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, restricted);
        var allowedResult = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, allowed);

        // Assert
        Assert.Contains(restrictedResult.ValidationErrors, e => e.Code == QueryErrorCode.FieldNotAllowed && e.Message.Contains("wildcard"));
        Assert.Contains(allowedResult.ValidationErrors, e => e.Code == QueryErrorCode.FieldNotAllowed && e.Message.Contains("wildcard"));
    }

    [Fact]
    public void Run_WildcardFieldNamesWithoutFieldRules_IsValid()
    {
        var result = ValidationVisitor.Run(LuceneQuery.Parse("_exists_:secr* book.*:x").Document, new QueryValidationOptions());

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public void Run_RestrictedResolvedName_RejectsAlias()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext { ValidationOptions = options };
        var document = FieldResolverQueryVisitor.Run(LuceneQuery.Parse("innocent:1 data.x:2").Document,
            new FieldMap { { "innocent", "secret" }, { "data", "secret.nested" } }, context);

        // Act
        var result = ValidationVisitor.Run(document, context);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal(QueryErrorCode.FieldRestricted, error.Code);
        Assert.Contains("secret", error.Message);
        Assert.Contains("secret.nested.x", error.Message);
    }

    [Fact]
    public void Run_UnresolvedFieldsNotAllowed_ReturnsError()
    {
        // Arrange
        var context = new QueryVisitorContext { ValidationOptions = new QueryValidationOptions { AllowUnresolvedFields = false } };
        var document = FieldResolverQueryVisitor.Run(LuceneQuery.Parse("known:1 unknown:2").Document,
            (field, _) => field == "known" ? "known" : null, context);

        // Act
        var result = ValidationVisitor.Run(document, context);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal(QueryErrorCode.UnresolvedField, error.Code);
        Assert.Contains("unknown", error.Message);
    }

    [Fact]
    public void Run_UnresolvedFieldsAllowedByDefault_IsValid()
    {
        var context = new QueryVisitorContext();
        var document = FieldResolverQueryVisitor.Run(LuceneQuery.Parse("unknown:2").Document, (_, _) => null, context);

        var result = ValidationVisitor.Run(document, context);

        Assert.True(result.IsValid);
        Assert.Equal(["unknown"], result.UnresolvedFields);
    }

    [Fact]
    public void Run_UnresolvedIncludes_NotAllowedByDefault()
    {
        // Arrange
        var context = new QueryVisitorContext();
        var document = IncludeVisitor.ExpandIncludes(LuceneQuery.Parse("@include:missing").Document, new Dictionary<string, string>(), context);

        // Act
        var result = ValidationVisitor.Run(document, context);

        // Assert
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal(QueryErrorCode.UnresolvedInclude, error.Code);
        Assert.Contains("missing", error.Message);
    }

    [Fact]
    public void Run_UnresolvedIncludesAllowed_IsValid()
    {
        var context = new QueryVisitorContext { ValidationOptions = new QueryValidationOptions { AllowUnresolvedIncludes = true } };
        var document = IncludeVisitor.ExpandIncludes(LuceneQuery.Parse("@include:missing").Document, new Dictionary<string, string>(), context);

        var result = ValidationVisitor.Run(document, context);

        Assert.True(result.IsValid, result.Message);
    }

    [Theory]
    [InlineData("f:a", true)]
    [InlineData("f:\"a b\"", true)]
    [InlineData("f:a*", false)]
    [InlineData("f:/a/", false)]
    [InlineData("f:a OR g:[1 TO 2]", false)]
    public void Run_AllowedOperations_RejectsOtherOperations(string query, bool valid)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add(QueryOperations.Term);
        options.AllowedOperations.Add("PHRASE");

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(QueryErrorCode.OperationNotAllowed, Assert.Single(result.ValidationErrors).Code);
    }

    [Theory]
    [InlineData("f:a", true)]
    [InlineData("f:/a.*/", false)]
    [InlineData("f:a~2", false)]
    [InlineData("x OR (y AND f:/a/)", false)]
    public void Run_RestrictedOperations_RejectsRestrictedOperations(string query, bool valid)
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedOperations.Add(QueryOperations.Regex);
        options.RestrictedOperations.Add(QueryOperations.Fuzzy);

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);

        // Assert
        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(QueryErrorCode.OperationRestricted, Assert.Single(result.ValidationErrors).Code);
    }

    [Fact]
    public void Run_MultipleViolations_ReportsAllErrors()
    {
        // Arrange
        var options = new QueryValidationOptions { AllowLeadingWildcards = false, AllowedMaxNodeDepth = 1 };
        options.RestrictedFields.Add("secret");
        options.RestrictedOperations.Add(QueryOperations.Regex);

        // Act
        var result = ValidationVisitor.Run(LuceneQuery.Parse("secret:1 (f:*x) g:/r/").Document, options);

        // Assert
        Assert.Equal(
            [QueryErrorCode.LeadingWildcardNotAllowed, QueryErrorCode.FieldRestricted, QueryErrorCode.OperationRestricted, QueryErrorCode.MaxDepthExceeded],
            result.ValidationErrors.Select(e => e.Code));
        Assert.Equal(string.Join(Environment.NewLine, result.ValidationErrors.Select(e => e.ToString())), result.Message);
    }

    [Fact]
    public void Accept_NonDocumentNode_DoesNotApplyRestrictions()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var context = new QueryVisitorContext { ValidationOptions = options };
        var node = LuceneQuery.Parse("secret:1").Document.Query!;

        // Act
        ValidationVisitor.Instance.Accept(node, context);

        // Assert
        Assert.True(context.ValidationResult.IsValid);
        Assert.Equal(["secret"], context.ValidationResult.ReferencedFields);
    }

    [Fact]
    public void Run_NonDocumentNode_AppliesRestrictions()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");

        var result = ValidationVisitor.Run(LuceneQuery.Parse("secret:1").Document.Query!, options);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Run_NullNode_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationVisitor.Run(null!));
        Assert.Throws<ArgumentNullException>(() => ValidationVisitor.Run(LuceneQuery.Parse("a").Document, (QueryValidationOptions)null!));
        Assert.Throws<ArgumentNullException>(() => ValidationVisitor.ApplyRestrictions(null!));
    }

    [Fact]
    public void Run_SharedOptionsConcurrently_ResultsAreIndependent()
    {
        // Arrange
        var options = new QueryValidationOptions { AllowLeadingWildcards = false };
        options.RestrictedFields.Add("secret");
        options.AllowedFields.Add("secret");
        options.AllowedFields.Add("ok");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act
        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            bool shouldFail = i % 2 == 0;
            string query = shouldFail ? $"secret.x{i}:1" : $"ok.x{i}:1";
            var result = ValidationVisitor.Run(LuceneQuery.Parse(query).Document, options);
            if (result.IsValid == shouldFail || result.ReferencedFields.Count != 1)
                failures.Add(query);
        });

        // Assert
        Assert.Empty(failures);
        Assert.Equal(2, options.AllowedFields.Count);
        Assert.Single(options.RestrictedFields);
    }

    [Fact]
    public void ThrowIfInvalid_InvalidResult_ThrowsWithResult()
    {
        // Arrange
        var result = new QueryValidationResult { QueryType = QueryType.Sort };
        result.AddError("bad field", 3, QueryErrorCode.FieldRestricted);

        // Act
        var exception = Assert.Throws<QueryValidationException>(result.ThrowIfInvalid);

        // Assert
        Assert.Equal("Invalid sort: bad field", exception.Message);
        Assert.Same(result, exception.Result);
        Assert.Single(exception.Errors);
        Assert.Equal(QueryErrorCode.FieldRestricted, exception.ErrorCode);
    }

    [Fact]
    public void ThrowIfInvalid_ValidResult_DoesNotThrow()
    {
        var result = new QueryValidationResult();

        result.ThrowIfInvalid();

        Assert.True(result);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void AddOperation_NullField_RecordsEmptyField()
    {
        var result = new QueryValidationResult();

        result.AddOperation("term", null);
        result.AddOperation("TERM", "f");

        var entry = Assert.Single(result.Operations);
        Assert.Equal(["", "f"], entry.Value.Order());
        Assert.Throws<ArgumentException>(() => result.AddOperation("", "f"));
    }

    [Fact]
    public void QueryValidationError_ToString_IncludesPositionWhenKnown()
    {
        Assert.Equal("[5] msg", new QueryValidationError("msg", 5).ToString());
        Assert.Equal("msg", new QueryValidationError("msg").ToString());
    }

    [Fact]
    public void QueryValidationOptions_Defaults_AreSecureAndBounded()
    {
        var options = new QueryValidationOptions();

        Assert.False(options.ShouldThrow);
        Assert.True(options.AllowLeadingWildcards);
        Assert.True(options.AllowUnresolvedFields);
        Assert.False(options.AllowUnresolvedIncludes);
        Assert.Equal(0, options.AllowedMaxNodeDepth);
        Assert.Equal(10, options.MaxIncludeDepth);
        Assert.Equal(100, options.MaxIncludeExpansions);
        Assert.Empty(options.AllowedFields);
        Assert.Empty(options.RestrictedFields);
        options.AllowedFields.Add("Name");
        Assert.Contains("name", options.AllowedFields);
    }
}
