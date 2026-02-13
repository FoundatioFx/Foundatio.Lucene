using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class ChainableVisitorTests
{
    #region QueryVisitorContext Tests

    [Fact]
    public void Context_SetAndGetValue_Works()
    {
        var context = new QueryVisitorContext();

        context.SetValue("key1", "value1");
        context.SetValue("key2", 42);
        context.SetValue("key3", true);

        Assert.Equal("value1", context.GetValue<string>("key1"));
        Assert.Equal(42, context.GetValue<int>("key2"));
        Assert.True(context.GetValue<bool>("key3"));
    }

    [Fact]
    public void Context_GetValue_ReturnsDefaultForMissingKey()
    {
        var context = new QueryVisitorContext();

        Assert.Null(context.GetValue<string>("missing"));
        Assert.Equal(0, context.GetValue<int>("missing"));
        Assert.False(context.GetValue<bool>("missing"));
    }

    [Fact]
    public void Context_GetOrCreateList_CreatesNewList()
    {
        var context = new QueryVisitorContext();

        var list1 = context.GetOrCreateList<string>("myList");
        list1.Add("item1");

        var list2 = context.GetOrCreateList<string>("myList");
        list2.Add("item2");

        Assert.Same(list1, list2);
        Assert.Equal(2, list1.Count);
    }

    [Fact]
    public void Context_Data_IsAccessible()
    {
        var context = new QueryVisitorContext();

        context.Data["direct"] = "access";

        Assert.Equal("access", context.GetValue<string>("direct"));
    }

    #endregion

    #region ChainableQueryVisitor Tests

    [Fact]
    public void ChainableVisitor_VisitsAllNodeTypes()
    {
        // Use a query that includes all the node types we want to test
        var query = "field:(hello world)^2 AND title:\"test phrase\"~3 NOT status:active age:[18 TO 65]";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;

        var visitor = new NodeTypeCollectorVisitor();
        var context = new QueryVisitorContext();

        visitor.Accept(document, context);

        var nodeTypes = context.GetValue<HashSet<string>>("NodeTypes");
        Assert.NotNull(nodeTypes);
        Assert.Contains("QueryDocument", nodeTypes);
        Assert.Contains("BooleanQueryNode", nodeTypes);
        Assert.Contains("FieldQueryNode", nodeTypes);
        Assert.Contains("GroupNode", nodeTypes);
        Assert.Contains("PhraseNode", nodeTypes);
        Assert.Contains("RangeNode", nodeTypes);
        Assert.Contains("NotNode", nodeTypes);
    }

    [Fact]
    public void ChainableVisitor_CanModifyNodes()
    {
        var query = "HELLO";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;

        var visitor = new LowercaseTermVisitor();
        var context = new QueryVisitorContext();

        visitor.Accept(document, context);

        var output = QueryStringBuilder.ToQueryString(document);

        Assert.Equal("hello", output);
    }

    [Fact]
    public void ChainableVisitor_CanModifyFieldNames()
    {
        var query = "author:john";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;

        var visitor = new FieldRenameVisitor("author", "metadata.author");
        var context = new QueryVisitorContext();

        visitor.Accept(document, context);

        var output = QueryStringBuilder.ToQueryString(document);

        Assert.Equal("metadata.author:john", output);
    }

    #endregion

    #region ChainedQueryVisitor Tests

    [Fact]
    public void ChainedVisitor_RunsVisitorsInPriorityOrder()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("third"), priority: 30)
            .AddVisitor(new OrderTrackerVisitor("first"), priority: 10)
            .AddVisitor(new OrderTrackerVisitor("second"), priority: 20);

        chain.Accept(document, context);

        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["first", "second", "third"], order);
    }

    [Fact]
    public void ChainedVisitor_AddVisitorBefore_InsertsCorrectly()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("target"), priority: 20);

        chain.AddVisitorBefore<OrderTrackerVisitor>(new OrderTrackerVisitor("before"));

        chain.Accept(document, context);

        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["before", "target"], order);
    }

    [Fact]
    public void ChainedVisitor_AddVisitorAfter_InsertsCorrectly()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("target"), priority: 20);

        chain.AddVisitorAfter<OrderTrackerVisitor>(new OrderTrackerVisitor("after"));

        chain.Accept(document, context);

        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["target", "after"], order);
    }

    [Fact]
    public void ChainedVisitor_RemoveVisitor_Works()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("first"), priority: 10)
            .AddVisitor(new LowercaseTermVisitor(), priority: 20)
            .AddVisitor(new OrderTrackerVisitor("third"), priority: 30);

        chain.RemoveVisitor<LowercaseTermVisitor>();
        chain.Accept(document, context);

        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["first", "third"], order);
    }

    [Fact]
    public void ChainedVisitor_ReplaceVisitor_Works()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("original"), priority: 10);

        chain.ReplaceVisitor<OrderTrackerVisitor>(new OrderTrackerVisitor("replacement"));
        chain.Accept(document, context);

        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["replacement"], order);
    }

    [Fact]
    public void ChainedVisitor_CombinesMultipleTransformations()
    {
        var query = "Author:HELLO status:Active";
        var document = LuceneQuery.Parse(query).Document;
        var context = new QueryVisitorContext();

        var aliases = new Dictionary<string, string>
        {
            ["Author"] = "metadata.author",
            ["status"] = "doc.status"
        };

        var chain = new ChainedQueryVisitor()
            .AddVisitor(new FieldAliasVisitor(aliases), priority: 10)
            .AddVisitor(new LowercaseTermVisitor(), priority: 20);

        chain.Accept(document, context);

        var output = QueryStringBuilder.ToQueryString(document);

        Assert.Equal("metadata.author:hello doc.status:active", output);
    }

    [Fact]
    public void ChainedVisitor_SharesContextBetweenVisitors()
    {
        var query = "field:value";
        var document = LuceneQuery.Parse(query).Document;
        var context = new QueryVisitorContext();

        // First visitor sets a value
        var setter = new ContextSetterVisitor("sharedKey", "sharedValue");
        // Second visitor reads and asserts the value
        var reader = new ContextReaderVisitor("sharedKey", "sharedValue");

        var chain = new ChainedQueryVisitor()
            .AddVisitor(setter, priority: 10)
            .AddVisitor(reader, priority: 20);

        chain.Accept(document, context);

        Assert.True(context.GetValue<bool>("ReaderFoundValue"));
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void Run_WithNewContext_Works()
    {
        var document = LuceneQuery.Parse("TEST").Document;
        var visitor = new LowercaseTermVisitor();

        var result = visitor.Run(document);

        var output = QueryStringBuilder.ToQueryString(result);

        Assert.Equal("test", output);
    }

    [Fact]
    public void Run_WithProvidedContext_PreservesContext()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        context.SetValue("preExisting", "value");

        var visitor = new OrderTrackerVisitor("visitor");

        visitor.Run(document, context);

        // Both pre-existing and visitor-added values should be in context
        Assert.Equal("value", context.GetValue<string>("preExisting"));
        Assert.NotNull(context.GetValue<List<string>>("ExecutionOrder"));
    }

    #endregion

    #region Test Helper Visitors

    private class NodeTypeCollectorVisitor : QueryVisitor
    {
        private void TrackNodeType(QueryNode node, IQueryVisitorContext context)
        {
            var nodeTypes = context.GetValue<HashSet<string>>("NodeTypes");
            if (nodeTypes is null)
            {
                nodeTypes = new HashSet<string>();
                context.SetValue("NodeTypes", nodeTypes);
            }
            nodeTypes.Add(node.GetType().Name);
        }

        protected override QueryNode Visit(QueryDocument node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return base.Visit(node, context);
        }

        protected override QueryNode Visit(GroupNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return base.Visit(node, context);
        }

        protected override QueryNode Visit(BooleanQueryNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return base.Visit(node, context);
        }

        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return base.Visit(node, context);
        }

        protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(PhraseNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(RegexNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(NotNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return base.Visit(node, context);
        }

        protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(MatchAllNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }

        protected override QueryNode Visit(MultiTermNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return node;
        }
    }

    private class LowercaseTermVisitor : QueryVisitor
    {
        protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
        {
            node.Term = node.Term.ToLowerInvariant();
            node.UnescapedTerm = node.UnescapedTerm.ToLowerInvariant();
            return node;
        }
    }

    private class FieldRenameVisitor : QueryVisitor
    {
        private readonly string _oldName;
        private readonly string _newName;

        public FieldRenameVisitor(string oldName, string newName)
        {
            _oldName = oldName;
            _newName = newName;
        }

        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field == _oldName)
                node.Field = _newName;
            return base.Visit(node, context);
        }
    }

    private class FieldAliasVisitor : QueryVisitor
    {
        private readonly Dictionary<string, string> _aliases;

        public FieldAliasVisitor(Dictionary<string, string> aliases)
        {
            _aliases = aliases;
        }

        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (_aliases.TryGetValue(node.Field, out var newName))
                node.Field = newName;
            return base.Visit(node, context);
        }
    }

    private class OrderTrackerVisitor : QueryVisitor
    {
        private readonly string _name;
        private bool _tracked;

        public OrderTrackerVisitor(string name)
        {
            _name = name;
        }

        protected override QueryNode Visit(QueryDocument node, IQueryVisitorContext context)
        {
            // Only track once at the document level
            if (!_tracked)
            {
                _tracked = true;
                var order = context.GetValue<List<string>>("ExecutionOrder");
                if (order is null)
                {
                    order = new List<string>();
                    context.SetValue("ExecutionOrder", order);
                }
                order.Add(_name);
            }
            return base.Visit(node, context);
        }
    }

    private class ContextSetterVisitor : QueryVisitor
    {
        private readonly string _key;
        private readonly object _value;

        public ContextSetterVisitor(string key, object value)
        {
            _key = key;
            _value = value;
        }

        protected override QueryNode Visit(QueryDocument node, IQueryVisitorContext context)
        {
            context.SetValue(_key, _value);
            return base.Visit(node, context);
        }
    }

    private class ContextReaderVisitor : QueryVisitor
    {
        private readonly string _key;
        private readonly object _expectedValue;

        public ContextReaderVisitor(string key, object expectedValue)
        {
            _key = key;
            _expectedValue = expectedValue;
        }

        protected override QueryNode Visit(QueryDocument node, IQueryVisitorContext context)
        {
            var value = context.GetValue<object>(_key);
            context.SetValue("ReaderFoundValue", Equals(value, _expectedValue));
            return base.Visit(node, context);
        }
    }

    #endregion
}
