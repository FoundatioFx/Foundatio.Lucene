using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests.Parity;

/// <summary>
/// The Foundatio.Parsers <c>SqlQueryParserTests</c> scenarios that execute against SQL Server with full-text search.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("TestType", "Integration")]
public class ParsersSqlServerTests(SqlServerFixture fixture)
{
    [Fact]
    public void BuildFilter_WithFullTextDefaultFields_ReturnsMatchingEmployee()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["FullName", "Title"], SearchOperator.StartsWith));

        Assert.Single(db.Employees.Where("John", parser).ToList());
    }

    [Fact]
    public void BuildFilter_WithAliasedIncludeAndSqlLikeCharacters_ReturnsMatchingNonNullRow()
    {
        using var db = fixture.CreateParsersContext();
        using var transaction = db.Database.BeginTransaction();
        db.Companies.Add(new Company { Name = "Second", Location = "jo%_hn" });
        db.SaveChanges();
        var parser = new EntityFrameworkQueryParser(c => c
            .UseFieldMap(new FieldMap { ["location"] = "Location" })
            .UseIncludes(new Dictionary<string, string> { ["matching"] = "location:jo%_?n" })
            .UseValidationOptions(new QueryValidationOptions { AllowedFields = { "location" } }));

        string sql = db.Companies.Where("@include:matching", parser).ToSql();
        var companies = db.Companies.Where("@include:matching", parser).ToList();

        Assert.Contains(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" ESCAPE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Second", Assert.Single(companies).Name);
    }

    [Theory]
    [InlineData(@"Location:jo\*n", "jo*n")]
    [InlineData(@"Location:jo\?n", "jo?n")]
    public void BuildFilter_WithEscapedWildcard_ReturnsLiteralMatch(string query, string location)
    {
        using var db = fixture.CreateParsersContext();
        using var transaction = db.Database.BeginTransaction();
        db.Companies.Add(new Company { Name = "Literal", Location = location });
        db.Companies.Add(new Company { Name = "Other", Location = "john" });
        db.SaveChanges();
        var parser = new EntityFrameworkQueryParser();

        string sql = db.Companies.Where(query, parser).ToSql();

        Assert.DoesNotContain(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Literal", Assert.Single(db.Companies.Where(query, parser).ToList()).Name);
    }

    [Fact]
    public void BuildFilter_WithExcludedDefaultWildcard_ReturnsOnlyNonMatchingEmployee()
    {
        using var db = fixture.CreateParsersContext();
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("FullName", "Title"));

        Assert.Equal("Jane Doe", Assert.Single(db.Employees.Where("-Jo?n*", parser).ToList()).FullName);
    }

    [Fact]
    public void BuildFilter_WithQuestionMarkAndTrailingStar_ReturnsMatchingEmployee()
    {
        using var db = fixture.CreateParsersContext();

        Assert.Equal("John Doe", Assert.Single(db.Employees.Where("FullName:Jo?n*", new EntityFrameworkQueryParser()).ToList()).FullName);
    }

    [Theory]
    [InlineData("214-222-2222")]
    [InlineData("2142222222")]
    [InlineData("21422")]
    public void BuildFilter_WithSearchTokenizer_SearchesNormalizedPhoneNumber(string query)
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser(c =>
        {
            c.SetDefaultFields("NationalPhoneNumber");
            c.UseSearchTokenizer(term =>
            {
                if (term.Field.FullName != "NationalPhoneNumber")
                    return;

                string digits = new([.. term.Term.Where(char.IsAsciiDigit)]);
                if (digits.Length == 0)
                    return;

                term.Tokens = [digits];
                term.Operator = SearchOperator.StartsWith;
            });
        });

        Assert.Equal("John Doe", Assert.Single(db.Employees.Where(query, parser).ToList()).FullName);
    }

    [Fact]
    public void BuildFilter_WithOnlyBlankTokens_MatchesNothing()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultFields("NationalPhoneNumber").UseSearchTokenizer(term => term.Tokens = ["", "    "]));

        Assert.Empty(db.Employees.Where("test", parser).ToList());
    }

    [Fact]
    public void BuildFilter_WithReferenceNavigationFullTextFields_ReturnsBothEmployees()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser(c => c.SetDefaultFields(["CurrentCompany.Name", "CurrentCompany.Location"], SearchOperator.StartsWith));

        Assert.Equal(2, db.Employees.Where("acme", parser).Count());
    }

    [Fact]
    public void BuildFilter_WithNavigationAndSkipNavigationFields_ReturnsCompany()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser();

        Assert.Single(db.Companies.Where("datadefinitions.key:age", parser).ToList());
        Assert.Single(db.Companies.Where("employees.salary:80000", parser).ToList());
    }

    [Fact]
    public void BuildFilter_WithCustomFieldAndFullTextNavigation_ReturnsMatchingEmployee()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser();
        var options = new EntityFrameworkQueryOptions { AdditionalFields = [ParsersSampleContext.DynamicField("age", typeof(decimal), 1)] };

        var employees = db.Employees.Where("companies.name:acme age:30", parser, options).ToList();

        Assert.Equal("John Doe", Assert.Single(employees).FullName);
    }

    [Fact]
    public void BuildFilter_WithDateMathAndTypedFields_ExecutesOnSqlServer()
    {
        using var db = fixture.CreateParsersContext();
        var parser = ParsersSampleContext.CreateParser();

        Assert.Equal(2, db.Employees.Where("created:>now-90d", parser).Count());
        Assert.Equal(2, db.Employees.Where("birthday:<now-90d", parser).Count());
        Assert.Equal("Jane Doe", Assert.Single(db.Employees.Where("happyhour:<\"6:00\"", parser).ToList()).FullName);
        Assert.Equal(2, db.Employees.Where("_exists_:title", parser).Count());
        Assert.Single(db.Companies.Where("_missing_:location", parser).ToList());
    }
}
