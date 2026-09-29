using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Foundatio.Lucene.Ast;
using Foundatio.Parsers.LuceneQueries.Visitors;
using FP = Foundatio.Parsers.LuceneQueries;

namespace Foundatio.Lucene.Benchmarks;

/// <summary>
/// Parsing and round-tripping compared with Foundatio.Parsers (the baseline in each category).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ParserBenchmarks
{
    private readonly FP.LuceneQueryParser _parsersParser = new();
    private readonly GenerateQueryVisitor _parsersGenerator = new();
    private readonly QueryStringBuilder _builder = new();
    private QueryDocument _complexDocument = null!;
    private FP.Nodes.IQueryNode _parsersComplexNode = null!;

    [GlobalSetup]
    public void Setup()
    {
        _complexDocument = LuceneQuery.Parse(BenchmarkScenarios.ComplexQuery).Document;
        _parsersComplexNode = _parsersParser.Parse(BenchmarkScenarios.ComplexQuery);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Parse simple")]
    public object Parsers_ParseSimple() => _parsersParser.Parse(BenchmarkScenarios.SimpleQuery);

    [Benchmark, BenchmarkCategory("Parse simple")]
    public object Lucene_ParseSimple() => LuceneQuery.Parse(BenchmarkScenarios.SimpleQuery);

    [Benchmark(Baseline = true), BenchmarkCategory("Parse typical")]
    public object Parsers_ParseTypical() => _parsersParser.Parse(BenchmarkScenarios.TypicalQuery);

    [Benchmark, BenchmarkCategory("Parse typical")]
    public object Lucene_ParseTypical() => LuceneQuery.Parse(BenchmarkScenarios.TypicalQuery);

    [Benchmark(Baseline = true), BenchmarkCategory("Parse complex")]
    public object Parsers_ParseComplex() => _parsersParser.Parse(BenchmarkScenarios.ComplexQuery);

    [Benchmark, BenchmarkCategory("Parse complex")]
    public object Lucene_ParseComplex() => LuceneQuery.Parse(BenchmarkScenarios.ComplexQuery);

    [Benchmark(Baseline = true), BenchmarkCategory("To query string")]
    public Task<string> Parsers_ToQueryString() => _parsersGenerator.AcceptAsync(_parsersComplexNode, null);

    [Benchmark, BenchmarkCategory("To query string")]
    public string Lucene_ToQueryString() => _builder.Build(_complexDocument);
}
