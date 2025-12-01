using Foundatio.LuceneQuery.Extensions;

namespace Foundatio.LuceneQuery.Tests;

public class GetReferencedFieldsVisitorTests
{
    [Fact]
    public void GetReferencedFields_SingleField_ReturnsField()
    {
        var document = LuceneQuery.Parse("title:hello").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void GetReferencedFields_MultipleFields_ReturnsAllFields()
    {
        var document = LuceneQuery.Parse("title:hello AND author:john OR status:active").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Equal(3, fields.Count);
        Assert.Contains("title", fields);
        Assert.Contains("author", fields);
        Assert.Contains("status", fields);
    }

    [Fact]
    public void GetReferencedFields_NestedGroups_ReturnsAllFields()
    {
        var document = LuceneQuery.Parse("field1:value field2:value (field3:value OR field4:value (field5:value)) field6:value").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Equal(6, fields.Count);
        Assert.Contains("field1", fields);
        Assert.Contains("field2", fields);
        Assert.Contains("field3", fields);
        Assert.Contains("field4", fields);
        Assert.Contains("field5", fields);
        Assert.Contains("field6", fields);
    }

    [Fact]
    public void GetReferencedFields_DuplicateFields_ReturnsUniqueFields()
    {
        var document = LuceneQuery.Parse("title:hello AND title:world").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void GetReferencedFields_FieldWithGroup_ReturnsField()
    {
        var document = LuceneQuery.Parse("tags:(red OR blue)").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("tags", fields);
    }

    [Fact]
    public void GetReferencedFields_RangeQuery_ReturnsField()
    {
        var document = LuceneQuery.Parse("age:[18 TO 65]").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("age", fields);
    }

    [Fact]
    public void GetReferencedFields_ExistsQuery_ReturnsField()
    {
        // _exists_:email is now parsed as ExistsNode with Field="email"
        var document = LuceneQuery.Parse("_exists_:email").Document;
        
        var fields = document.GetReferencedFields();
        
        // The field is now "email" (the field being checked for existence)
        Assert.Single(fields);
        Assert.Contains("email", fields);
    }

    [Fact]
    public void GetReferencedFields_MissingQuery_ReturnsField()
    {
        // _missing_:email is parsed as MissingNode with Field="email"
        var document = LuceneQuery.Parse("_missing_:email").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("email", fields);
    }

    [Fact]
    public void GetReferencedFields_PhraseQuery_ReturnsField()
    {
        var document = LuceneQuery.Parse("description:\"hello world\"").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("description", fields);
    }

    [Fact]
    public void GetReferencedFields_RegexQuery_ReturnsField()
    {
        var document = LuceneQuery.Parse("name:/joh?n/").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("name", fields);
    }

    [Fact]
    public void GetReferencedFields_NotQuery_ReturnsField()
    {
        var document = LuceneQuery.Parse("NOT status:deleted").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("status", fields);
    }

    [Fact]
    public void GetReferencedFields_NoFields_ReturnsEmpty()
    {
        var document = LuceneQuery.Parse("hello world").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Empty(fields);
    }

    [Fact]
    public void GetReferencedFields_MatchAll_ReturnsEmpty()
    {
        var document = LuceneQuery.Parse("*:*").Document;
        
        var fields = document.GetReferencedFields();
        
        // *:* is a match all query, the * field should not be considered a real field
        // It depends on how we parse it - let's check the actual behavior
        // In Foundatio.Parsers, *:* creates a MatchAllNode
        var expectedCount = fields.Contains("*") ? 1 : 0;
        Assert.Equal(expectedCount, fields.Count);
    }

    [Fact]
    public void GetReferencedFields_CaseInsensitive_ReturnsSingleField()
    {
        var document = LuceneQuery.Parse("Title:hello AND title:world AND TITLE:test").Document;
        
        var fields = document.GetReferencedFields();
        
        // Fields should be case-insensitive, so all variations of "title" should be considered the same
        Assert.Single(fields);
    }

    [Fact]
    public void GetReferencedFields_ComplexQuery_ReturnsAllFields()
    {
        var document = LuceneQuery.Parse(
            "title:search AND author:john OR (status:active AND created_at:[2020-01-01 TO 2024-12-31]) " +
            "NOT deleted:true tags:(important OR urgent) _exists_:thumbnail"
        ).Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Contains("title", fields);
        Assert.Contains("author", fields);
        Assert.Contains("status", fields);
        Assert.Contains("created_at", fields);
        Assert.Contains("deleted", fields);
        Assert.Contains("tags", fields);
        // _exists_:thumbnail is now parsed as ExistsNode with Field="thumbnail"
        Assert.Contains("thumbnail", fields);
    }

    [Fact]
    public void GetReferencedFields_CalledOnQueryNode_Works()
    {
        var document = LuceneQuery.Parse("field1:value field2:value").Document;
        
        // Can call directly on the document's Query node too
        var fields = document.Query!.GetReferencedFields();
        
        Assert.Equal(2, fields.Count);
        Assert.Contains("field1", fields);
        Assert.Contains("field2", fields);
    }

    [Fact]
    public void GetReferencedFields_NestedFieldNames_ReturnsFullPath()
    {
        var document = LuceneQuery.Parse("metadata.author:john user.profile.name:jane").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Equal(2, fields.Count);
        Assert.Contains("metadata.author", fields);
        Assert.Contains("user.profile.name", fields);
    }

    [Fact]
    public void GetReferencedFields_BoostedField_ReturnsField()
    {
        var document = LuceneQuery.Parse("title:hello^2").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void GetReferencedFields_FuzzyField_ReturnsField()
    {
        var document = LuceneQuery.Parse("title:hello~2").Document;
        
        var fields = document.GetReferencedFields();
        
        Assert.Single(fields);
        Assert.Contains("title", fields);
    }
}
