using BenchmarkDotNet.Attributes;
using Foundatio.LuceneQuery.Ast;
using Foundatio.Parsers.LuceneQueries.Visitors;
using OldParser = Foundatio.Parsers.LuceneQueries;

namespace Foundatio.LuceneQuery.Benchmarks;

/// <summary>
/// Core benchmarks for the Lucene Query Parser library.
/// Covers parsing, query string building, and visitor traversal.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3)]
public class Benchmarks
{
    // Representative query samples - simple to complex
    private const string SimpleQuery = "hello";
    private const string FieldQuery = "title:test AND status:active";
    private const string ComplexQuery = "title:\"hello world\" AND (status:active OR status:pending) AND price:[100 TO 500] AND NOT deleted:true";

    private QueryDocument _simpleDoc = null!;
    private QueryDocument _fieldDoc = null!;
    private QueryDocument _complexDoc = null!;
    private QueryStringBuilder _builder = null!;
    private QueryNodeVisitor _visitor = null!;

    // Foundatio.Parsers objects for comparison
    private OldParser.LuceneQueryParser _oldParser = null!;
    private OldParser.Nodes.IQueryNode _oldSimpleNode = null!;
    private OldParser.Nodes.IQueryNode _oldFieldNode = null!;
    private OldParser.Nodes.IQueryNode _oldComplexNode = null!;
    private GenerateQueryVisitor _oldGenerateVisitor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleDoc = LuceneQuery.Parse(SimpleQuery).Document;
        _fieldDoc = LuceneQuery.Parse(FieldQuery).Document;
        _complexDoc = LuceneQuery.Parse(ComplexQuery).Document;
        _builder = new QueryStringBuilder(256);
        _visitor = new NoOpVisitor();

        // Setup Foundatio.Parsers
        _oldParser = new OldParser.LuceneQueryParser();
        _oldSimpleNode = _oldParser.Parse(SimpleQuery);
        _oldFieldNode = _oldParser.Parse(FieldQuery);
        _oldComplexNode = _oldParser.Parse(ComplexQuery);
        _oldGenerateVisitor = new GenerateQueryVisitor();
    }

    #region Parsing

    [Benchmark]
    public LuceneParseResult Parse_Simple() => LuceneQuery.Parse(SimpleQuery);

    [Benchmark]
    public LuceneParseResult Parse_Field() => LuceneQuery.Parse(FieldQuery);

    [Benchmark]
    public LuceneParseResult Parse_Complex() => LuceneQuery.Parse(ComplexQuery);

    [Benchmark(Baseline = true)]
    public OldParser.Nodes.IQueryNode Parse_Simple_Old() => _oldParser.Parse(SimpleQuery);

    [Benchmark]
    public OldParser.Nodes.IQueryNode Parse_Field_Old() => _oldParser.Parse(FieldQuery);

    [Benchmark]
    public OldParser.Nodes.IQueryNode Parse_Complex_Old() => _oldParser.Parse(ComplexQuery);

    #endregion

    #region Query String Building

    [Benchmark]
    public string Build_Simple() => _builder.Visit(_simpleDoc);

    [Benchmark]
    public string Build_Complex() => _builder.Visit(_complexDoc);

    [Benchmark]
    public async Task<string> Build_Simple_Old() => await _oldGenerateVisitor.AcceptAsync(_oldSimpleNode, null);

    [Benchmark]
    public async Task<string> Build_Complex_Old() => await _oldGenerateVisitor.AcceptAsync(_oldComplexNode, null);

    #endregion

    #region Round-Trip (Parse + Build)

    [Benchmark]
    public string RoundTrip_Field()
    {
        var result = LuceneQuery.Parse(FieldQuery);
        return _builder.Visit(result.Document);
    }

    [Benchmark]
    public string RoundTrip_Complex()
    {
        var result = LuceneQuery.Parse(ComplexQuery);
        return _builder.Visit(result.Document);
    }

    [Benchmark]
    public async Task<string> RoundTrip_Field_Old()
    {
        var node = _oldParser.Parse(FieldQuery);
        return await _oldGenerateVisitor.AcceptAsync(node, null);
    }

    [Benchmark]
    public async Task<string> RoundTrip_Complex_Old()
    {
        var node = _oldParser.Parse(ComplexQuery);
        return await _oldGenerateVisitor.AcceptAsync(node, null);
    }

    #endregion

    #region Visitor Traversal

    [Benchmark]
    public async Task<QueryNode> Visit_Complex()
    {
        var context = new Foundatio.LuceneQuery.Visitors.QueryVisitorContext();
        return await _visitor.AcceptAsync(_complexDoc, context);
    }

    #endregion

    private class NoOpVisitor : QueryNodeVisitor { }
}
