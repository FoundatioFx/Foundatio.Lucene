using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;

namespace Foundatio.Lucene.Tests;

public class QueryNodeExtensionsTests
{
    [Theory]
    [InlineData("title:hello", "title")]
    [InlineData("title:hello AND status:active", "status,title")]
    [InlineData("title:a title:b TITLE:c", "title")]
    [InlineData("(a:1 OR (b:2 AND (c:3)))", "a,b,c")]
    [InlineData("tags:(a OR b)", "tags")]
    [InlineData("price:[1 TO 5]", "price")]
    [InlineData("created:>=2024-01-01", "created")]
    [InlineData("_exists_:email", "email")]
    [InlineData("email:*", "email")]
    [InlineData("_missing_:email", "email")]
    [InlineData("title:\"a b\"~2^3", "title")]
    [InlineData("title:/re.*/", "title")]
    [InlineData("title:hello~2", "title")]
    [InlineData("NOT title:a", "title")]
    [InlineData("a OR NOT title:a", "title")]
    [InlineData("user.name.first:john", "user.name.first")]
    [InlineData("hello world", "")]
    [InlineData("*:*", "")]
    [InlineData("@include:saved title:x", "title")]
    [InlineData("", "")]
    public void GetReferencedFields_Query_ReturnsDistinctFields(string query, string expected)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;

        // Act
        var fields = document.GetReferencedFields();

        // Assert
        Assert.Equal(expected, string.Join(",", fields.Select(f => f.ToLowerInvariant()).Order()));
    }

    [Fact]
    public void GetReferencedFields_IncludeSpecialFields_ReturnsSpecialFields()
    {
        var fields = LuceneQuery.Parse("@include:saved @custom:x title:y").Document.GetReferencedFields(includeSpecialFields: true);

        Assert.Equal(["@custom", "@include", "title"], fields.Order());
    }

    [Fact]
    public void GetReferencedFields_SubNode_ReturnsOnlyItsFields()
    {
        var document = LuceneQuery.Parse("a:1 AND (b:2 OR c:3)").Document;
        var group = ((BooleanQueryNode)document.Query!).Clauses[1].Query!;

        Assert.Equal(["b", "c"], group.GetReferencedFields().Order());
    }

    [Fact]
    public void GetReferencedFields_ResultSet_IsCaseInsensitive()
    {
        var fields = LuceneQuery.Parse("Title:a").Document.GetReferencedFields();

        Assert.Contains("title", fields);
    }

    [Fact]
    public void Walk_Tree_VisitsNodesDepthFirstInOrder()
    {
        // Arrange
        var document = LuceneQuery.Parse("a:1 (b OR NOT c)").Document;
        var visited = new List<string>();

        // Act
        document.Walk(n => visited.Add(n switch
        {
            FieldQueryNode f => $"field:{f.Field}",
            TermNode t => $"term:{t.Term}",
            _ => n.GetType().Name
        }));

        // Assert
        Assert.Equal(["QueryDocument", "BooleanQueryNode", "field:a", "term:1", "GroupNode", "BooleanQueryNode", "term:b", "NotNode", "term:c"], visited);
    }

    [Fact]
    public void Walk_VeryDeepTree_DoesNotOverflowStack()
    {
        // Arrange
        QueryNode node = new TermNode { Term = "leaf" };
        for (int i = 0; i < 100_000; i++)
            node = new GroupNode { Query = node };
        int count = 0;

        // Act
        node.Walk(_ => count++);

        // Assert
        Assert.Equal(100_001, count);
    }

    [Fact]
    public void Walk_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((QueryNode)null!).Walk(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new TermNode().Walk(null!));
    }

    [Fact]
    public void GetOriginalField_NotSet_ReturnsCurrentField()
    {
        // Arrange
        var node = new FieldQueryNode { Field = "resolved" };

        // Act
        string before = node.GetOriginalField();
        node.SetOriginalField("written");

        // Assert
        Assert.Equal("resolved", before);
        Assert.Equal("written", node.GetOriginalField());
        Assert.Equal("written", node.GetData<string>(QueryNodeExtensions.OriginalFieldKey));
    }

    [Fact]
    public void Clone_Document_IsDeepCopyIncludingData()
    {
        // Arrange
        var document = LuceneQuery.Parse("a:(b OR \"c d\"~2) -e:[1 TO 5}^2 _exists_:f NOT g:/h/ i:j~1").Document;
        document.Query!.SetData("key", "value");

        // Act
        var clone = document.CloneDocument();
        clone.Query!.SetData("key", "changed");
        ((FieldQueryNode)((BooleanQueryNode)clone.Query).Clauses[0].Query!).Field = "changed";

        // Assert
        Assert.Equal(QueryStringBuilder.ToQueryString(LuceneQuery.Parse("a:(b OR \"c d\"~2) -e:[1 TO 5}^2 _exists_:f NOT g:/h/ i:j~1").Document), QueryStringBuilder.ToQueryString(document));
        Assert.Equal("value", document.Query.GetData<string>("key"));
        Assert.Equal(document.Query.StartPosition, clone.Query.StartPosition);
        Assert.Equal(document.Query.EndPosition, clone.Query.EndPosition);
    }

    [Fact]
    public void SetData_NullValue_RemovesKey()
    {
        var node = new TermNode();

        node.SetData("key", 1);
        Assert.True(node.HasData);
        node.SetData("key", null);

        Assert.False(node.HasData);
        Assert.Equal(0, node.GetData<int>("key"));
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a*", "a*(prefix)")]
    [InlineData("a?b", "a?b(wildcard)")]
    [InlineData("a~2^3", "a~2^3")]
    [InlineData("\"a b\"~1^2", "\"a b\"~1^2")]
    [InlineData("/r/^2", "/r/^2")]
    [InlineData("f:{1 TO *]", "f:{1 TO *]")]
    [InlineData("_exists_:f", "(exists f)")]
    [InlineData("_missing_:f", "(missing f)")]
    [InlineData("*:*", "(all)")]
    [InlineData("(a)^2", "(group a^2)")]
    [InlineData("a OR NOT b", "(bool ?a ?(not b))")]
    [InlineData("", "null")]
    public void ToDebugString_Node_DescribesTree(string query, string expected)
    {
        Assert.Equal(expected, LuceneQuery.Parse(query).Document.ToDebugString());
    }

    [Fact]
    public void ToDebugString_Null_ReturnsNull()
    {
        Assert.Equal("null", ((QueryNode?)null).ToDebugString());
    }
}
