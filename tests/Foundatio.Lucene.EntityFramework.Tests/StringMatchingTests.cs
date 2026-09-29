namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// String fields: plain terms and phrases are equality (case sensitivity is the database collation's), a trailing
/// <c>*</c> is StartsWith, <c>*x*</c> is Contains, and other wildcards use LIKE. Escaped wildcards are literal.
/// </summary>
public class StringMatchingTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();
    private readonly EntityFrameworkQueryParser _parser = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("name:\"John Doe\"", new[] { "John Doe" })]
    [InlineData(@"name:John\ Doe", new[] { "John Doe" })]
    [InlineData("name:John", new string[0])]
    [InlineData("title:\"Senior Developer\"", new[] { "Bob Wilson" })]
    [InlineData("name:Jo*", new[] { "John Doe" })]
    [InlineData("name:J*", new[] { "Jane Smith", "John Doe" })]
    [InlineData("name:*oh*", new[] { "John Doe" })]
    [InlineData("title:*Developer*", new[] { "Alice Brown", "Bob Wilson", "John Doe" })]
    [InlineData("name:J*n*", new[] { "Jane Smith", "John Doe" })]
    [InlineData("name:Jo?n*", new[] { "John Doe" })]
    [InlineData("name:J?ne*", new[] { "Jane Smith" })]
    [InlineData("name:*Doe", new[] { "John Doe" })]
    [InlineData("name:Bob?Wilson", new[] { "Bob Wilson" })]
    [InlineData("name:Bo?", new string[0])]
    [InlineData("email:*@acme.com", new[] { "Jane Smith", "John Doe" })]
    [InlineData("email:*acme*", new[] { "Jane Smith", "John Doe" })]
    [InlineData("-name:*o*", new[] { "Jane Smith" })]
    [InlineData("name:(Jo* OR *Wilson)", new[] { "Bob Wilson", "John Doe" })]
    public void BuildFilter_WithStringTerm_MatchesByEqualityOrWildcard(string query, string[] expected)
    {
        Assert.Equal(expected, _db.Employees.Where(query, _parser).Names());
    }

    [Theory]
    [InlineData(@"name:Pro\*ject", new[] { "Pro*ject" })]
    [InlineData(@"name:Pro\?ject", new[] { "Pro?ject" })]
    [InlineData("name:\"Pro*ject\"", new[] { "Pro*ject" })]
    [InlineData("name:Pro*ject", new[] { "Pro*ject", "Pro?ject", "Project" })]
    [InlineData(@"name:Pro\**", new[] { "Pro*ject" })]
    [InlineData(@"name:*\?*", new[] { "Pro?ject" })]
    [InlineData("name:100%", new[] { "100%" })]
    [InlineData("name:100%*", new[] { "100%" })]
    [InlineData("name:*%*", new[] { "100%" })]
    [InlineData("name:?00%", new[] { "100%" })]
    [InlineData("name:a_?", new[] { "a_b" })]
    [InlineData("name:*_*", new[] { "a_b" })]
    [InlineData(@"name:back\\slash", new[] { @"back\slash" })]
    [InlineData(@"name:back\\*", new[] { @"back\slash" })]
    public void BuildFilter_WithEscapedOrSpecialCharacters_MatchesLiterally(string query, string[] expected)
    {
        _db.Projects.AddRange(
            new Project { Name = "Pro*ject" }, new Project { Name = "Pro?ject" }, new Project { Name = "Project" },
            new Project { Name = "100%" }, new Project { Name = "1000" }, new Project { Name = "a_b" },
            new Project { Name = "axb" }, new Project { Name = @"back\slash" });
        _db.SaveChanges();

        Assert.Equal(expected, _db.Projects.Where(query, _parser).Select(p => p.Name).AsEnumerable().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("John", SearchOperator.StartsWith, new[] { "John Doe" })]
    [InlineData("Senior", SearchOperator.StartsWith, new[] { "Bob Wilson" })]
    [InlineData("Developer", SearchOperator.StartsWith, new string[0])]
    [InlineData("Developer", SearchOperator.Contains, new[] { "Alice Brown", "Bob Wilson", "John Doe" })]
    [InlineData("\"Project Manager\"", SearchOperator.Equals, new[] { "Jane Smith" })]
    [InlineData("Project", SearchOperator.Equals, new string[0])]
    [InlineData("\"Jane Smith\"", SearchOperator.StartsWith, new[] { "Jane Smith" })]
    [InlineData("J*", SearchOperator.Equals, new[] { "Alice Brown", "Jane Smith", "John Doe" })]
    [InlineData("*Doe", SearchOperator.StartsWith, new[] { "John Doe" })]
    [InlineData("-J*", SearchOperator.StartsWith, new[] { "Bob Wilson" })]
    [InlineData("John Senior", SearchOperator.StartsWith, new string[0])]
    [InlineData("John OR Senior", SearchOperator.StartsWith, new[] { "Bob Wilson", "John Doe" })]
    [InlineData("Jo* title:*Developer*", SearchOperator.StartsWith, new[] { "John Doe" })]
    public void BuildFilter_WithFieldlessTerm_SearchesDefaultFields(string query, SearchOperator searchOperator, string[] expected)
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields(["Name", "Title"], searchOperator));
        var perRequest = new EntityFrameworkQueryOptions { DefaultFields = ["Name", "Title"], DefaultSearchOperator = searchOperator };

        Assert.Equal(expected, _db.Employees.Where(query, parser).Names());
        Assert.Equal(expected, _db.Employees.Where(query, _parser, perRequest).Names());
    }

    [Fact]
    public void BuildFilter_WithEscapedWildcardInFieldlessTerm_SearchesForLiteralCharacter()
    {
        _db.Projects.AddRange(new Project { Name = "jo*n" }, new Project { Name = "join" });
        _db.SaveChanges();
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"));

        Assert.Equal(["jo*n"], _db.Projects.Where(@"jo\*n", parser).Select(p => p.Name));
        Assert.Equal(["jo*n", "join"], _db.Projects.Where("jo*n", parser).Select(p => p.Name).AsEnumerable().Order());
    }

    [Fact]
    public void BuildFilter_WithFieldlessTermAndNoDefaultFields_ThrowsValidationError()
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("john", _parser));

        Assert.Contains("require default fields", Assert.Single(ex.Errors).Message);
    }

    [Fact]
    public void BuildFilter_WithMixedTypeDefaultFields_SkipsFieldsThatCannotHoldTheValue()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name", "Age"));
        var numericOnly = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Age", "HireDate"));

        Assert.Equal(["John Doe"], _db.Employees.Where("30", parser).Names());
        Assert.Equal(["John Doe"], _db.Employees.Where("Jo*", parser).Names());
        Assert.Equal(["Jane Smith"], _db.Employees.Where("Ja*", parser).Names());
        Assert.Equal(["Bob Wilson"], _db.Employees.Where(">39", numericOnly).Names());

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("John", numericOnly));
        Assert.Contains("not valid for any of the default fields (Age, HireDate)", ex.Message);
    }

    [Fact]
    public void BuildFilter_WithAliasedAndNavigationDefaultFields_ResolvesEachField()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.FieldMap = new FieldMap { { "company", "Company.Name" } };
            c.SetDefaultFields("company", "Projects.Name");
        });

        Assert.Equal(["Alice Brown", "Jane Smith", "John Doe"], _db.Employees.Where("Acme", parser).Names());
        Assert.Equal(["Bob Wilson", "Jane Smith"], _db.Employees.Where("Zep", parser).Names());
    }

    [Fact]
    public void BuildFilter_WithUnknownDefaultField_ThrowsValidationError()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name", "Nickname"));

        var ex = Assert.Throws<QueryValidationException>(() => _db.Employees.Where("John", parser));

        Assert.Contains("Nickname", ex.Message);
        Assert.Contains("Nickname", ex.Result.UnresolvedFields);
    }

    [Fact]
    public void BuildFilter_WithSearchTokenizer_SearchesEachToken()
    {
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Email", "Name");
            c.UseSearchTokenizer(term =>
            {
                if (term.Field.Name != "Email")
                    return;

                if (term.Term.Contains('@'))
                {
                    term.Tokens = [term.Term.ToLowerInvariant()];
                    term.Operator = SearchOperator.Equals;
                }
                else
                {
                    term.Tokens = ["", "  "];
                }
            });
        });

        Assert.Equal(["John Doe"], _db.Employees.Where("JOHN@ACME.COM", parser).Names());
        Assert.Equal(["Jane Smith"], _db.Employees.Where("Jane", parser).Names());
        Assert.Empty(_db.Employees.Where("acme", parser).Names());
    }
}
