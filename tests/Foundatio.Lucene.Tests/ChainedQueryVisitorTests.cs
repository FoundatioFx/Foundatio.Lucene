using System.Collections.Concurrent;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class ChainedQueryVisitorTests
{
    private const string OrderKey = "order";

    [Fact]
    public void Accept_AllNodeTypes_DispatchesToTypedVisitMethods()
    {
        // Arrange
        var document = LuceneQuery.Parse("a \"b c\" /d/ e:[1 TO 2] _exists_:f _missing_:g *:* (h OR NOT i) j:k").Document;
        var visitor = new NodeTypeCountingVisitor();

        // Act
        visitor.Run(document);

        // Assert
        Assert.Equal(1, visitor.Counts[nameof(QueryDocument)]);
        Assert.Equal(1, visitor.Counts[nameof(PhraseNode)]);
        Assert.Equal(1, visitor.Counts[nameof(RegexNode)]);
        Assert.Equal(1, visitor.Counts[nameof(RangeNode)]);
        Assert.Equal(1, visitor.Counts[nameof(ExistsNode)]);
        Assert.Equal(1, visitor.Counts[nameof(MissingNode)]);
        Assert.Equal(1, visitor.Counts[nameof(MatchAllNode)]);
        Assert.Equal(1, visitor.Counts[nameof(GroupNode)]);
        Assert.Equal(1, visitor.Counts[nameof(NotNode)]);
        Assert.Equal(2, visitor.Counts[nameof(FieldQueryNode)]);
        Assert.Equal(4, visitor.Counts[nameof(TermNode)]);
        Assert.Equal(2, visitor.Counts[nameof(BooleanQueryNode)]);
    }

    [Fact]
    public void Accept_VisitorReturnsReplacementNode_ReplacesNodeInTree()
    {
        // Arrange
        var document = LuceneQuery.Parse("title:hello AND (world OR other)").Document;

        // Act
        var result = new UppercaseTermVisitor().Run(document);

        // Assert
        Assert.Equal("(bool +title:HELLO +(group (bool ?WORLD ?OTHER)))", result.ToDebugString());
    }

    [Fact]
    public void Accept_VisitorChangesFieldNames_UpdatesFields()
    {
        // Arrange
        var document = LuceneQuery.Parse("name:john AND _exists_:email").Document;

        // Act
        var result = new PrefixFieldVisitor("data.").Run(document);

        // Assert
        Assert.Equal("(bool +data.name:john +(exists email))", result.ToDebugString());
    }

    [Fact]
    public void Accept_TypedContext_ReceivesCustomContext()
    {
        // Arrange
        var document = LuceneQuery.Parse("a b").Document;
        var context = new CustomContext { Tag = "tenant-1" };
        var chain = new ChainedQueryVisitor<CustomContext>()
            .AddVisitor(new CustomContextVisitor())
            .AddVisitor(new AVisitor());

        // Act
        chain.Run(document, context);

        // Assert
        Assert.Equal(["tenant-1", "tenant-1"], context.SeenTags);
        Assert.Equal(["a"], GetOrder(context));
    }

    [Fact]
    public void Accept_DifferentPriorities_RunsLowestPriorityFirst()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new CVisitor(), 30)
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 20);

        // Act
        var order = RunAndGetOrder(chain);

        // Assert
        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal([typeof(AVisitor), typeof(BVisitor), typeof(CVisitor)], chain.Visitors.Select(v => v.GetType()));
    }

    [Fact]
    public void Accept_EqualPriorities_RunsInInsertionOrder()
    {
        // Arrange
        var chain = new ChainedQueryVisitor();
        for (int i = 0; i < 40; i++)
            chain.AddVisitor(new NamedVisitor(i.ToString()), i % 2);

        // Act
        var order = RunAndGetOrder(chain);

        // Assert
        var expected = Enumerable.Range(0, 40).Where(i => i % 2 == 0).Concat(Enumerable.Range(0, 40).Where(i => i % 2 == 1)).Select(i => i.ToString());
        Assert.Equal(expected, order);
    }

    [Fact]
    public void Accept_EachVisitorReceivesPreviousResult_ChainsTransformations()
    {
        // Arrange
        var document = LuceneQuery.Parse("name:john").Document;
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new PrefixFieldVisitor("b."), 20)
            .AddVisitor(new PrefixFieldVisitor("a."), 10)
            .AddVisitor(new UppercaseTermVisitor(), 30);

        // Act
        var result = chain.Run(document, new QueryVisitorContext());

        // Assert
        Assert.Equal("b.a.name:JOHN", result.ToDebugString());
    }

    [Fact]
    public void Accept_VisitorsShareContext_ValuesAreVisibleToLaterVisitors()
    {
        // Arrange
        var context = new QueryVisitorContext();
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor())
            .AddVisitor(new BVisitor());

        // Act
        chain.Run(LuceneQuery.Parse("x").Document, context);

        // Assert
        Assert.Equal(["a", "b"], GetOrder(context));
    }

    [Fact]
    public void AddVisitorBefore_ExistingType_RunsImmediatelyBefore()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 20)
            .AddVisitor(new CVisitor(), 20);

        // Act
        chain.AddVisitorBefore<CVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["a", "b", "d", "c"], RunAndGetOrder(chain));
    }

    [Fact]
    public void AddVisitorAfter_ExistingType_RunsImmediatelyAfter()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 10)
            .AddVisitor(new CVisitor(), 20);

        // Act
        chain.AddVisitorAfter<AVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["a", "d", "b", "c"], RunAndGetOrder(chain));
    }

    [Fact]
    public void AddVisitorBefore_ReferenceWasInsertedRelatively_RunsBeforeReference()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor())
            .AddVisitor(new BVisitor());
        chain.AddVisitorBefore<BVisitor>(new CVisitor());

        // Act
        chain.AddVisitorBefore<CVisitor>(new DVisitor());
        chain.AddVisitorAfter<CVisitor>(new EVisitor());

        // Assert
        Assert.Equal(["a", "d", "c", "e", "b"], RunAndGetOrder(chain));
    }

    [Fact]
    public void AddVisitorAfter_CalledTwiceForSameType_EachRunsImmediatelyAfterReference()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor())
            .AddVisitor(new BVisitor());

        // Act
        chain.AddVisitorAfter<AVisitor>(new CVisitor());
        chain.AddVisitorAfter<AVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["a", "d", "c", "b"], RunAndGetOrder(chain));
    }

    [Fact]
    public void AddVisitor_AfterRelativeInsert_KeepsRelativeOrder()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 20);
        chain.AddVisitorBefore<BVisitor>(new CVisitor());

        // Act
        chain.AddVisitor(new DVisitor(), 20);
        chain.AddVisitor(new EVisitor(), 15);

        // Assert
        Assert.Equal(["a", "e", "c", "b", "d"], RunAndGetOrder(chain));
    }

    [Fact]
    public void AddVisitorBefore_MissingType_ThrowsInvalidOperationException()
    {
        // Arrange
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => chain.AddVisitorBefore<BVisitor>(new CVisitor()));

        // Assert
        Assert.Contains(nameof(BVisitor), exception.Message);
        Assert.Single(chain.Visitors);
    }

    [Fact]
    public void AddVisitorAfter_MissingType_ThrowsInvalidOperationException()
    {
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor());

        Assert.Throws<InvalidOperationException>(() => chain.AddVisitorAfter<BVisitor>(new CVisitor()));
        Assert.Single(chain.Visitors);
    }

    [Fact]
    public void AddVisitor_NullVisitor_ThrowsArgumentNullException()
    {
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor());

        Assert.Throws<ArgumentNullException>(() => chain.AddVisitor(null!));
        Assert.Throws<ArgumentNullException>(() => chain.AddVisitorBefore<AVisitor>(null!));
        Assert.Throws<ArgumentNullException>(() => chain.AddVisitorAfter<AVisitor>(null!));
        Assert.Throws<ArgumentNullException>(() => chain.ReplaceVisitor<AVisitor>(null!));
    }

    [Fact]
    public void RemoveVisitor_ExistingType_RemovesAllInstances()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor())
            .AddVisitor(new BVisitor())
            .AddVisitor(new AVisitor(), 5);

        // Act
        chain.RemoveVisitor<AVisitor>();

        // Assert
        Assert.Equal(["b"], RunAndGetOrder(chain));
    }

    [Fact]
    public void RemoveVisitor_MissingType_LeavesChainUnchanged()
    {
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor());

        chain.RemoveVisitor<BVisitor>();

        Assert.Equal(["a"], RunAndGetOrder(chain));
    }

    [Fact]
    public void ReplaceVisitor_ExistingType_KeepsPosition()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 10)
            .AddVisitor(new CVisitor(), 10);

        // Act
        chain.ReplaceVisitor<BVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["a", "d", "c"], RunAndGetOrder(chain));
    }

    [Fact]
    public void ReplaceVisitor_RelativelyInsertedVisitor_KeepsPosition()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor())
            .AddVisitor(new BVisitor());
        chain.AddVisitorBefore<BVisitor>(new CVisitor());

        // Act
        chain.ReplaceVisitor<CVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["a", "d", "b"], RunAndGetOrder(chain));
    }

    [Fact]
    public void ReplaceVisitor_WithNewPriority_MovesVisitor()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 20)
            .AddVisitor(new CVisitor(), 30);

        // Act
        chain.ReplaceVisitor<AVisitor>(new DVisitor(), newPriority: 25);

        // Assert
        Assert.Equal(["b", "d", "c"], RunAndGetOrder(chain));
    }

    [Fact]
    public void ReplaceVisitor_MultipleInstances_ReplacesAllAtFirstPosition()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new AVisitor(), 10)
            .AddVisitor(new BVisitor(), 20)
            .AddVisitor(new AVisitor(), 30);

        // Act
        chain.ReplaceVisitor<AVisitor>(new DVisitor());

        // Assert
        Assert.Equal(["d", "b"], RunAndGetOrder(chain));
    }

    [Fact]
    public void ReplaceVisitor_MissingType_AddsVisitor()
    {
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor(), 10);

        chain.ReplaceVisitor<BVisitor>(new CVisitor(), 5);

        Assert.Equal(["c", "a"], RunAndGetOrder(chain));
    }

    [Fact]
    public void Visitors_ReturnsSnapshot_NotAffectedByLaterChanges()
    {
        // Arrange
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor());
        var snapshot = chain.Visitors;

        // Act
        chain.AddVisitor(new BVisitor());

        // Assert
        Assert.Single(snapshot);
        Assert.Equal(2, chain.Visitors.Count);
    }

    [Fact]
    public void Accept_ChainModifiedDuringRun_DoesNotAffectRunInProgress()
    {
        // Arrange
        var chain = new ChainedQueryVisitor();
        chain.AddVisitor(new AVisitor())
            .AddVisitor(new ActionVisitor(() => chain.AddVisitor(new CVisitor())))
            .AddVisitor(new BVisitor());

        // Act
        var first = RunAndGetOrder(chain);
        var second = RunAndGetOrder(chain);

        // Assert
        Assert.Equal(["a", "b"], first);
        Assert.Equal(["a", "b", "c"], second);
    }

    [Fact]
    public void Accept_SharedChainRunConcurrently_EachRunIsIndependent()
    {
        // Arrange
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new PrefixFieldVisitor("x."), 10)
            .AddVisitor(new UppercaseTermVisitor(), 20)
            .AddVisitor(new AVisitor(), 30);
        var failures = new ConcurrentBag<string>();

        // Act
        Parallel.For(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var context = new QueryVisitorContext();
            var result = chain.Run(LuceneQuery.Parse($"f{i}:v{i}").Document, context);
            string expected = $"x.f{i}:V{i}";
            if (result.ToDebugString() != expected || GetOrder(context) is not ["a"])
                failures.Add(result.ToDebugString());
        });

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void AddVisitor_ConcurrentlyWhileRunning_AllVisitorsAddedAndRunsSucceed()
    {
        // Arrange
        var chain = new ChainedQueryVisitor().AddVisitor(new AVisitor(), int.MinValue);
        var failures = new ConcurrentBag<Exception>();

        // Act
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    chain.AddVisitor(new NamedVisitor(i.ToString()), i);
                }
                else
                {
                    var order = RunAndGetOrder(chain);
                    if (order[0] != "a")
                        throw new InvalidOperationException("Priority order was not respected.");
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        // Assert
        Assert.Empty(failures);
        Assert.Equal(201, chain.Visitors.Count);
        var order = RunAndGetOrder(chain);
        Assert.Equal(["a", .. Enumerable.Range(0, 200).Select(i => (i * 2).ToString())], order);
    }

    [Fact]
    public void Run_WithoutContext_UsesNewContext()
    {
        // Arrange
        var visitor = new AVisitor();

        // Act
        var result = visitor.Run(LuceneQuery.Parse("x").Document);

        // Assert
        Assert.IsType<QueryDocument>(result);
    }

    [Fact]
    public void Run_NullArguments_ThrowsArgumentNullException()
    {
        var node = LuceneQuery.Parse("x").Document;

        Assert.Throws<ArgumentNullException>(() => ((IQueryVisitor)null!).Run(node));
        Assert.Throws<ArgumentNullException>(() => new AVisitor().Run(null!));
        Assert.Throws<ArgumentNullException>(() => new AVisitor().Run(null!, new QueryVisitorContext()));
    }

    [Fact]
    public void GetValue_MissingOrMismatchedKey_ReturnsDefault()
    {
        // Arrange
        var context = new QueryVisitorContext();

        // Act
        context.SetValue("number", 42);

        // Assert
        Assert.Equal(42, context.GetValue<int>("number"));
        Assert.Null(context.GetValue<string>("number"));
        Assert.Null(context.GetValue<string>("missing"));
        Assert.Equal(0, context.GetValue<int>("missing"));
        Assert.Single(context.Data);
    }

    [Fact]
    public void DefaultOperator_Set_UpdatesParserOptionsWithoutChangingSharedDefault()
    {
        // Arrange
        var context = new QueryVisitorContext();

        // Act
        context.DefaultOperator = BooleanOperator.Or;

        // Assert
        Assert.Equal(BooleanOperator.Or, context.ParserOptions.DefaultOperator);
        Assert.Equal(BooleanOperator.And, LuceneParserOptions.Default.DefaultOperator);
        Assert.NotSame(LuceneParserOptions.Default, context.ParserOptions);
    }

    [Fact]
    public void Defaults_NewContext_HasExpectedDefaults()
    {
        var context = new QueryVisitorContext();

        Assert.Same(LuceneParserOptions.Default, context.ParserOptions);
        Assert.Same(TimeProvider.System, context.TimeProvider);
        Assert.Equal(QueryType.Query, context.QueryType);
        Assert.False(context.IsResolved);
        Assert.True(context.ValidationResult.IsValid);
        Assert.Same(context.ValidationResult, context.ValidationResult);
    }

    private static List<string> RunAndGetOrder(IQueryVisitor<IQueryVisitorContext> visitor)
    {
        var context = new QueryVisitorContext();
        visitor.Run<IQueryVisitorContext>(LuceneQuery.Parse("x").Document, context);
        return GetOrder(context);
    }

    private static List<string> GetOrder(IQueryVisitorContext context) => context.GetValue<List<string>>(OrderKey) ?? [];

    private class NamedVisitor(string name) : QueryVisitor
    {
        public override QueryNode Accept(QueryNode node, IQueryVisitorContext context)
        {
            if (node is QueryDocument)
            {
                var order = context.GetValue<List<string>>(OrderKey);
                if (order is null)
                {
                    order = [];
                    context.SetValue(OrderKey, order);
                }

                order.Add(name);
            }

            return base.Accept(node, context);
        }
    }

    private sealed class AVisitor() : NamedVisitor("a");

    private sealed class BVisitor() : NamedVisitor("b");

    private sealed class CVisitor() : NamedVisitor("c");

    private sealed class DVisitor() : NamedVisitor("d");

    private sealed class EVisitor() : NamedVisitor("e");

    private sealed class ActionVisitor(Action action) : QueryVisitor
    {
        public override QueryNode Accept(QueryNode node, IQueryVisitorContext context)
        {
            action();
            return node;
        }
    }

    private sealed class NodeTypeCountingVisitor : QueryVisitor
    {
        public Dictionary<string, int> Counts { get; } = [];

        public override QueryNode Accept(QueryNode node, IQueryVisitorContext context)
        {
            string name = node.GetType().Name;
            Counts[name] = Counts.GetValueOrDefault(name) + 1;
            return base.Accept(node, context);
        }
    }

    private sealed class UppercaseTermVisitor : QueryVisitor
    {
        protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
        {
            return new TermNode { Term = node.Term.ToUpperInvariant() };
        }
    }

    private sealed class PrefixFieldVisitor(string prefix) : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            node.Field = prefix + node.Field;
            return base.Visit(node, context);
        }
    }

    private sealed class CustomContext : QueryVisitorContext
    {
        public string? Tag { get; init; }

        public List<string?> SeenTags { get; } = [];
    }

    private sealed class CustomContextVisitor : QueryVisitor<CustomContext>
    {
        protected override QueryNode Visit(TermNode node, CustomContext context)
        {
            context.SeenTags.Add(context.Tag);
            return node;
        }
    }
}
