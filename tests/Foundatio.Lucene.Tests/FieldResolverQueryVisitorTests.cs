using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class FieldResolverQueryVisitorTests
{
    [Theory]
    [InlineData("user:john", "account.user:john")]
    [InlineData("USER:john", "account.user:john")]
    [InlineData("user.name:john", "account.user.name:john")]
    [InlineData("data.age:>5", "resolved.age:{5 TO *]")]
    [InlineData("data.nested.deep:1", "resolved.nested.deep:1")]
    [InlineData("data.exact:1", "exact.target:1")]
    [InlineData("data.exact.child:1", "exact.target.child:1")]
    [InlineData("other:1", "other:1")]
    [InlineData("user:\"john smith\"", "account.user:\"john smith\"")]
    [InlineData("user:[a TO b]", "account.user:[a TO b]")]
    [InlineData("user:/jo.*/", "account.user:/jo.*/")]
    [InlineData("user:john~2^3", "account.user:john~2^3")]
    [InlineData("_exists_:user", "(exists account.user)")]
    [InlineData("user:*", "(exists account.user)")]
    [InlineData("_missing_:user", "(missing account.user)")]
    [InlineData("-user:john", "(bool -account.user:john)")]
    [InlineData("user:(john OR jane)", "account.user:(group (bool ?john ?jane))")]
    [InlineData("(user:a OR (data.x:b AND NOT other:c))", "(group (bool ?account.user:a ?(group (bool +resolved.x:b -other:c))))")]
    public void Run_FieldMap_ResolvesFieldsEverywhere(string query, string expected)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;
        var fieldMap = new FieldMap { { "user", "account.user" }, { "data", "resolved" }, { "data.exact", "exact.target" } };

        // Act
        var result = FieldResolverQueryVisitor.Run(document, fieldMap);

        // Assert
        Assert.Equal(expected, result.ToDebugString());
    }

    [Fact]
    public void Run_ResolvedField_RecordsOriginalField()
    {
        // Arrange
        var document = LuceneQuery.Parse("user:john AND _exists_:user AND _missing_:user AND other:x").Document;

        // Act
        FieldResolverQueryVisitor.Run(document, new FieldMap { { "user", "account.user" } });

        // Assert
        var fieldNodes = new List<QueryNode>();
        document.Walk(n =>
        {
            if (n is IFieldNode)
                fieldNodes.Add(n);
        });
        var field = Assert.IsType<FieldQueryNode>(fieldNodes[0]);
        Assert.Equal("account.user", field.Field);
        Assert.Equal("user", field.GetOriginalField());
        Assert.Equal("user", ((ExistsNode)fieldNodes[1]).GetOriginalField());
        Assert.Equal("user", ((MissingNode)fieldNodes[2]).GetOriginalField());
        var unchanged = (FieldQueryNode)fieldNodes[3];
        Assert.Equal("other", unchanged.GetOriginalField());
        Assert.False(unchanged.HasData);
    }

    [Fact]
    public void Run_ResolverAndFieldMap_ResolverTakesPrecedence()
    {
        // Arrange
        var document = LuceneQuery.Parse("a:1 b:2 c:3").Document;
        var context = new QueryVisitorContext
        {
            FieldMap = new FieldMap { { "a", "map.a" }, { "b", "map.b" } }
        };

        // Act
        FieldResolverQueryVisitor.Run(document, (field, _) => field == "a" ? "resolver.a" : null, context);

        // Assert
        Assert.Equal("(bool +resolver.a:1 +map.b:2 +c:3)", document.ToDebugString());
        Assert.Empty(context.ValidationResult.UnresolvedFields);
    }

    [Fact]
    public void Run_ResolverReturnsNullAndNoFieldMap_RecordsUnresolvedField()
    {
        // Arrange
        var document = LuceneQuery.Parse("known:1 unknown:2 _exists_:missing").Document;
        var context = new QueryVisitorContext();

        // Act
        FieldResolverQueryVisitor.Run(document, (field, _) => field == "known" ? "resolved.known" : null, context);

        // Assert
        Assert.Equal("(bool +resolved.known:1 +unknown:2 +(exists missing))", document.ToDebugString());
        Assert.Equal(["missing", "unknown"], context.ValidationResult.UnresolvedFields.Order());
        Assert.True(context.ValidationResult.IsValid);
    }

    [Fact]
    public void Run_FieldMapReportsUnmappedFields_RecordsUnresolvedField()
    {
        // Arrange
        var document = LuceneQuery.Parse("mapped:1 unmapped:2").Document;
        var context = new QueryVisitorContext();
        var fieldMap = new FieldMap { ReportUnmappedFields = true }.Add("mapped", "target");

        // Act
        FieldResolverQueryVisitor.Run(document, fieldMap, context);

        // Assert
        Assert.Equal("(bool +target:1 +unmapped:2)", document.ToDebugString());
        Assert.Equal(["unmapped"], context.ValidationResult.UnresolvedFields);
    }

    [Fact]
    public void Run_ResolverThrows_RecordsValidationErrorAndLeavesField()
    {
        // Arrange
        var document = LuceneQuery.Parse("boom:1 ok:2").Document;
        var context = new QueryVisitorContext();

        // Act
        FieldResolverQueryVisitor.Run(document, (field, _) => field == "boom" ? throw new InvalidOperationException("kaboom") : "x." + field, context);

        // Assert
        Assert.Equal("(bool +boom:1 +x.ok:2)", document.ToDebugString());
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Equal(QueryErrorCode.UnresolvedField, error.Code);
        Assert.Contains("boom", error.Message);
        Assert.Contains("kaboom", error.Message);
        Assert.Contains("boom", context.ValidationResult.UnresolvedFields);
    }

    [Fact]
    public void Run_SpecialFields_AreNotResolved()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:saved @custom:x").Document;
        var calls = new List<string>();

        // Act
        FieldResolverQueryVisitor.Run(document, (field, _) =>
        {
            calls.Add(field);
            return "resolved";
        });

        // Assert
        Assert.Empty(calls);
        Assert.Equal("(bool +@include:saved +@custom:x)", document.ToDebugString());
    }

    [Fact]
    public void Run_AlreadyResolvedDocument_DoesNotResolveTwice()
    {
        // Arrange
        var document = LuceneQuery.Parse("a:1").Document;
        var fieldMap = new FieldMap { { "a", "b" }, { "b", "c" } };

        // Act
        FieldResolverQueryVisitor.Run(document, fieldMap);
        FieldResolverQueryVisitor.Run(document, fieldMap);

        // Assert
        Assert.Equal("b:1", document.ToDebugString());
        Assert.Equal("a", ((FieldQueryNode)document.Query!).GetOriginalField());
    }

    [Fact]
    public void Run_ResolverReturnsSameName_DoesNotRecordOriginalField()
    {
        var document = LuceneQuery.Parse("a:1").Document;

        FieldResolverQueryVisitor.Run(document, (field, _) => field);

        Assert.False(document.Query!.HasData);
    }

    [Fact]
    public void Run_ResolverReceivesContext_CanUseContextData()
    {
        // Arrange
        var document = LuceneQuery.Parse("name:x").Document;
        var context = new QueryVisitorContext();
        context.SetValue("tenant", "t1");

        // Act
        FieldResolverQueryVisitor.Run(document, (field, ctx) => $"{ctx.GetValue<string>("tenant")}.{field}", context);

        // Assert
        Assert.Equal("t1.name:x", document.ToDebugString());
    }

    [Fact]
    public void Run_NullArguments_ThrowsArgumentNullException()
    {
        var document = LuceneQuery.Parse("a").Document;

        Assert.Throws<ArgumentNullException>(() => FieldResolverQueryVisitor.Run(null!, new FieldMap()));
        Assert.Throws<ArgumentNullException>(() => FieldResolverQueryVisitor.Run(document, (FieldMap)null!));
        Assert.Throws<ArgumentNullException>(() => FieldResolverQueryVisitor.Run(document, (QueryFieldResolver)null!));
    }

    [Fact]
    public void TryResolveField_NoResolverOrFieldMap_ResolvesToItself()
    {
        bool success = FieldResolverQueryVisitor.TryResolveField("anything", new QueryVisitorContext(), out string resolved);

        Assert.True(success);
        Assert.Equal("anything", resolved);
    }

    [Fact]
    public void TryResolveField_Unresolvable_ReturnsFalseAndOriginalName()
    {
        var context = new QueryVisitorContext { FieldResolver = (_, _) => null };

        bool success = FieldResolverQueryVisitor.TryResolveField("field", context, out string resolved);

        Assert.False(success);
        Assert.Equal("field", resolved);
    }

    [Fact]
    public void Run_WithIncludeVisitorInChain_ResolvesFieldsInsideExpandedIncludes()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:active AND user:john").Document;
        var context = new QueryVisitorContext
        {
            Includes = new Dictionary<string, string> { ["active"] = "status:active" },
            FieldMap = new FieldMap { { "status", "data.status" }, { "user", "account.user" } }
        };
        var chain = new ChainedQueryVisitor()
            .AddVisitor(FieldResolverQueryVisitor.Instance, 10)
            .AddVisitor(IncludeVisitor.Instance, 0);

        // Act
        var result = chain.Run(document, context);

        // Assert
        Assert.Equal("(bool +(group data.status:active) +account.user:john)", result.ToDebugString());
    }

    [Fact]
    public void Run_SharedFieldMapConcurrently_ProducesConsistentResults()
    {
        // Arrange
        var fieldMap = new FieldMap { { "user", "account.user" }, { "data", "resolved" } };
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act
        Parallel.For(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var document = LuceneQuery.Parse($"user:{i} data.x{i}:y").Document;
            string result = FieldResolverQueryVisitor.Run(document, fieldMap).ToDebugString();
            if (result != $"(bool +account.user:{i} +resolved.x{i}:y)")
                failures.Add(result);
        });

        // Assert
        Assert.Empty(failures);
    }
}
