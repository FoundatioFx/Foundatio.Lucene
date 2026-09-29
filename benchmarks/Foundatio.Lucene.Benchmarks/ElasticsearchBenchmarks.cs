using BenchmarkDotNet.Attributes;
using Foundatio.Lucene.Elasticsearch;

namespace Foundatio.Lucene.Benchmarks;

/// <summary>
/// Elasticsearch query, aggregation, and sort building with a mapping. The same scenarios run against
/// Foundatio.Parsers in the Foundatio.Parsers.Benchmarks project.
/// </summary>
[MemoryDiagnoser]
public class ElasticsearchBenchmarks
{
    private ElasticsearchQueryParser _parser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new ElasticsearchQueryParser(c => c.UseMappings(BenchmarkScenarios.CreateMapping()));
        _parser.BuildQuery(BenchmarkScenarios.SimpleQuery);
    }

    [Benchmark]
    public object BuildQuery_Simple() => _parser.BuildQuery(BenchmarkScenarios.SimpleQuery);

    [Benchmark]
    public object BuildQuery_Typical() => _parser.BuildQuery(BenchmarkScenarios.TypicalQuery);

    [Benchmark]
    public object BuildQuery_Complex() => _parser.BuildQuery(BenchmarkScenarios.ComplexQuery);

    [Benchmark]
    public object BuildQuery_Nested() => _parser.BuildQuery(BenchmarkScenarios.NestedQuery);

    [Benchmark]
    public object BuildAggregations() => _parser.BuildAggregations(BenchmarkScenarios.Aggregations);

    [Benchmark]
    public object BuildSort() => _parser.BuildSort(BenchmarkScenarios.Sort);
}
