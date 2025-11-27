using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

namespace Foundatio.LuceneQueryParser.Tests;

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
    public async Task ChainableVisitor_VisitsAllNodeTypes()
    {
        // Use a query that includes all the node types we want to test
        var query = "field:(hello world)^2 AND title:\"test phrase\"~3 NOT status:active age:[18 TO 65]";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;
        
        var visitor = new NodeTypeCollectorVisitor();
        var context = new QueryVisitorContext();
        
        await visitor.AcceptAsync(document, context);
        
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
    public async Task ChainableVisitor_CanModifyNodes()
    {
        var query = "HELLO";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;
        
        var visitor = new LowercaseTermVisitor();
        var context = new QueryVisitorContext();
        
        await visitor.AcceptAsync(document, context);
        
        var output = QueryStringBuilder.ToQueryString(document);
        
        Assert.Equal("hello", output);
    }

    [Fact]
    public async Task ChainableVisitor_CanModifyFieldNames()
    {
        var query = "author:john";
        var result = LuceneQuery.Parse(query);
        var document = result.Document;
        
        var visitor = new FieldRenameVisitor("author", "metadata.author");
        var context = new QueryVisitorContext();
        
        await visitor.AcceptAsync(document, context);
        
        var output = QueryStringBuilder.ToQueryString(document);
        
        Assert.Equal("metadata.author:john", output);
    }

    #endregion

    #region ChainedQueryVisitor Tests

    [Fact]
    public async Task ChainedVisitor_RunsVisitorsInPriorityOrder()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("third"), priority: 30)
            .AddVisitor(new OrderTrackerVisitor("first"), priority: 10)
            .AddVisitor(new OrderTrackerVisitor("second"), priority: 20);
        
        await chain.AcceptAsync(document, context);
        
        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["first", "second", "third"], order);
    }

    [Fact]
    public async Task ChainedVisitor_AddVisitorBefore_InsertsCorrectly()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("target"), priority: 20);
        
        chain.AddVisitorBefore<OrderTrackerVisitor>(new OrderTrackerVisitor("before"));
        
        await chain.AcceptAsync(document, context);
        
        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["before", "target"], order);
    }

    [Fact]
    public async Task ChainedVisitor_AddVisitorAfter_InsertsCorrectly()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("target"), priority: 20);
        
        chain.AddVisitorAfter<OrderTrackerVisitor>(new OrderTrackerVisitor("after"));
        
        await chain.AcceptAsync(document, context);
        
        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["target", "after"], order);
    }

    [Fact]
    public async Task ChainedVisitor_RemoveVisitor_Works()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("first"), priority: 10)
            .AddVisitor(new LowercaseTermVisitor(), priority: 20)
            .AddVisitor(new OrderTrackerVisitor("third"), priority: 30);
        
        chain.RemoveVisitor<LowercaseTermVisitor>();
        await chain.AcceptAsync(document, context);
        
        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["first", "third"], order);
    }

    [Fact]
    public async Task ChainedVisitor_ReplaceVisitor_Works()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        
        var chain = new ChainedQueryVisitor()
            .AddVisitor(new OrderTrackerVisitor("original"), priority: 10);
        
        chain.ReplaceVisitor<OrderTrackerVisitor>(new OrderTrackerVisitor("replacement"));
        await chain.AcceptAsync(document, context);
        
        var order = context.GetValue<List<string>>("ExecutionOrder");
        Assert.NotNull(order);
        Assert.Equal(["replacement"], order);
    }

    [Fact]
    public async Task ChainedVisitor_CombinesMultipleTransformations()
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
        
        await chain.AcceptAsync(document, context);
        
        var output = QueryStringBuilder.ToQueryString(document);
        
        Assert.Equal("metadata.author:hello doc.status:active", output);
    }

    [Fact]
    public async Task ChainedVisitor_SharesContextBetweenVisitors()
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
        
        await chain.AcceptAsync(document, context);
        
        Assert.True(context.GetValue<bool>("ReaderFoundValue"));
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public async Task RunAsync_WithNewContext_Works()
    {
        var document = LuceneQuery.Parse("TEST").Document;
        var visitor = new LowercaseTermVisitor();
        
        var result = await visitor.RunAsync(document);
        
        var output = QueryStringBuilder.ToQueryString(result);
        
        Assert.Equal("test", output);
    }

    [Fact]
    public async Task RunAsync_WithProvidedContext_PreservesContext()
    {
        var document = LuceneQuery.Parse("test").Document;
        var context = new QueryVisitorContext();
        context.SetValue("preExisting", "value");
        
        var visitor = new OrderTrackerVisitor("visitor");
        
        await visitor.RunAsync(document, context);
        
        // Both pre-existing and visitor-added values should be in context
        Assert.Equal("value", context.GetValue<string>("preExisting"));
        Assert.NotNull(context.GetValue<List<string>>("ExecutionOrder"));
    }

    #endregion

    #region Test Helper Visitors

    private class NodeTypeCollectorVisitor : QueryNodeVisitor
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

        public override async Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return await base.VisitAsync(node, context);
        }

        public override async Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return await base.VisitAsync(node, context);
        }

        public override async Task<QueryNode> VisitAsync(BooleanQueryNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return await base.VisitAsync(node, context);
        }

        public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return await base.VisitAsync(node, context);
        }

        public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override async Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return await base.VisitAsync(node, context);
        }

        public override Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(MatchAllNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }

        public override Task<QueryNode> VisitAsync(MultiTermNode node, IQueryVisitorContext context)
        {
            TrackNodeType(node, context);
            return Task.FromResult<QueryNode>(node);
        }
    }

    private class LowercaseTermVisitor : QueryNodeVisitor
    {
        public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
        {
            node.Term = node.Term.ToLowerInvariant();
            node.UnescapedTerm = node.UnescapedTerm.ToLowerInvariant();
            return Task.FromResult<QueryNode>(node);
        }
    }

    private class FieldRenameVisitor : QueryNodeVisitor
    {
        private readonly string _oldName;
        private readonly string _newName;

        public FieldRenameVisitor(string oldName, string newName)
        {
            _oldName = oldName;
            _newName = newName;
        }

        public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (node.Field == _oldName)
                node.Field = _newName;
            return await base.VisitAsync(node, context);
        }
    }

    private class FieldAliasVisitor : QueryNodeVisitor
    {
        private readonly Dictionary<string, string> _aliases;

        public FieldAliasVisitor(Dictionary<string, string> aliases)
        {
            _aliases = aliases;
        }

        public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
        {
            if (_aliases.TryGetValue(node.Field, out var newName))
                node.Field = newName;
            return await base.VisitAsync(node, context);
        }
    }

    private class OrderTrackerVisitor : QueryNodeVisitor
    {
        private readonly string _name;
        private bool _tracked;

        public OrderTrackerVisitor(string name)
        {
            _name = name;
        }

        public override async Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
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
            return await base.VisitAsync(node, context);
        }
    }

    private class ContextSetterVisitor : QueryNodeVisitor
    {
        private readonly string _key;
        private readonly object _value;

        public ContextSetterVisitor(string key, object value)
        {
            _key = key;
            _value = value;
        }

        public override async Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
        {
            context.SetValue(_key, _value);
            return await base.VisitAsync(node, context);
        }
    }

    private class ContextReaderVisitor : QueryNodeVisitor
    {
        private readonly string _key;
        private readonly object _expectedValue;

        public ContextReaderVisitor(string key, object expectedValue)
        {
            _key = key;
            _expectedValue = expectedValue;
        }

        public override async Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
        {
            var value = context.GetValue<object>(_key);
            context.SetValue("ReaderFoundValue", Equals(value, _expectedValue));
            return await base.VisitAsync(node, context);
        }
    }

    #endregion
}
