using BenchmarkDotNet.Attributes;
using Foundatio.Lucene.Benchmarks;
using Foundatio.Parsers.ElasticQueries;

namespace Foundatio.Parsers.Benchmarks;

/// <summary>
/// The Foundatio.Lucene Elasticsearch scenarios run against Foundatio.Parsers for comparison.
/// </summary>
[MemoryDiagnoser]
public class ElasticsearchBenchmarks
{
    private ElasticQueryParser _parser = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var mapping = BenchmarkScenarios.CreateMapping();
        _parser = new ElasticQueryParser(c => c.UseMappings(() => mapping).UseNested());
        await _parser.BuildQueryAsync(BenchmarkScenarios.SimpleQuery);
    }

    [Benchmark]
    public Task<Elastic.Clients.Elasticsearch.QueryDsl.Query> BuildQuery_Simple() => _parser.BuildQueryAsync(BenchmarkScenarios.SimpleQuery);

    [Benchmark]
    public Task<Elastic.Clients.Elasticsearch.QueryDsl.Query> BuildQuery_Typical() => _parser.BuildQueryAsync(BenchmarkScenarios.TypicalQuery);

    [Benchmark]
    public Task<Elastic.Clients.Elasticsearch.QueryDsl.Query> BuildQuery_Complex() => _parser.BuildQueryAsync(BenchmarkScenarios.ComplexQuery);

    [Benchmark]
    public Task<Elastic.Clients.Elasticsearch.QueryDsl.Query> BuildQuery_Nested() => _parser.BuildQueryAsync(BenchmarkScenarios.NestedQuery);

    [Benchmark]
    public Task<AggregationMap?> BuildAggregations() => _parser.BuildAggregationsAsync(BenchmarkScenarios.Aggregations);

    [Benchmark]
    public Task<ICollection<Elastic.Clients.Elasticsearch.SortOptions>> BuildSort() => _parser.BuildSortAsync(BenchmarkScenarios.Sort);
}
