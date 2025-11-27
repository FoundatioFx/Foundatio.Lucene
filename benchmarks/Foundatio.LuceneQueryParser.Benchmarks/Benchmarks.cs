using BenchmarkDotNet.Attributes;
using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

namespace Foundatio.LuceneQueryParser.Benchmarks;

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

    [GlobalSetup]
    public void Setup()
    {
        _simpleDoc = LuceneQuery.Parse(SimpleQuery).Document;
        _fieldDoc = LuceneQuery.Parse(FieldQuery).Document;
        _complexDoc = LuceneQuery.Parse(ComplexQuery).Document;
        _builder = new QueryStringBuilder(256);
        _visitor = new NoOpVisitor();
    }

    #region Parsing

    [Benchmark]
    public LuceneParseResult Parse_Simple() => LuceneQuery.Parse(SimpleQuery);

    [Benchmark]
    public LuceneParseResult Parse_Field() => LuceneQuery.Parse(FieldQuery);

    [Benchmark]
    public LuceneParseResult Parse_Complex() => LuceneQuery.Parse(ComplexQuery);

    #endregion

    #region Query String Building

    [Benchmark]
    public string Build_Simple() => _builder.Visit(_simpleDoc);

    [Benchmark]
    public string Build_Complex() => _builder.Visit(_complexDoc);

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

    #endregion

    #region Visitor Traversal

    [Benchmark]
    public async Task<QueryNode> Visit_Complex()
    {
        var context = new QueryVisitorContext();
        return await _visitor.AcceptAsync(_complexDoc, context);
    }

    #endregion

    private class NoOpVisitor : QueryNodeVisitor { }
}
