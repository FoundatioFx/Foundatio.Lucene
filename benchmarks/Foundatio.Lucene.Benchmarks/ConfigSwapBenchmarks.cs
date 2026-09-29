using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Foundatio.Lucene.Elasticsearch;
using Foundatio.Lucene.EntityFramework;

namespace Foundatio.Lucene.Benchmarks;

/// <summary>
/// Benchmarks the primary high-throughput use case: a single shared parser serving many
/// scopes/tenants, each supplying its own per-request options (field map, default fields,
/// validation) on every call. Measures the per-query cost of swapping scope configuration.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(AntiVirusFriendlyConfig))]
public class ConfigSwapBenchmarks
{
    private class AntiVirusFriendlyConfig : ManualConfig
    {
        public AntiVirusFriendlyConfig()
        {
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    private const string Query = "user:john AND created:[2020-01-01 TO *]";
    private const int ScopeCount = 8;

    private ElasticsearchQueryParser _esParser = null!;
    private ElasticsearchQueryOptions[] _esScopes = null!;

    private EntityFrameworkQueryParser _efParser = null!;
    private EntityFrameworkQueryOptions[] _efScopes = null!;

    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        _esParser = new ElasticsearchQueryParser(c => c.UseScoring = true);
        _esScopes = new ElasticsearchQueryOptions[ScopeCount];
        for (int n = 0; n < ScopeCount; n++)
        {
            _esScopes[n] = new ElasticsearchQueryOptions
            {
                FieldMap = new FieldMap { { "user", $"tenant{n}.userName" }, { "created", $"tenant{n}.createdAt" } },
                DefaultFields = [$"tenant{n}.title"]
            };
        }

        _efParser = new EntityFrameworkQueryParser();
        _efScopes = new EntityFrameworkQueryOptions[ScopeCount];
        for (int n = 0; n < ScopeCount; n++)
        {
            _efScopes[n] = new EntityFrameworkQueryOptionsBuilder()
                .WithFieldMap(new FieldMap { { "user", "Name" }, { "created", "HireDate" } })
                .WithDefaultFields("Name")
                .Build();
        }
    }

    [Benchmark]
    public object Elasticsearch_SwapScopePerQuery()
    {
        var scope = _esScopes[_index++ % ScopeCount];
        return _esParser.BuildQuery(Query, scope);
    }

    [Benchmark]
    public object EntityFramework_SwapScopePerQuery()
    {
        var scope = _efScopes[_index++ % ScopeCount];
        return _efParser.BuildFilter<Employee>(Query, context: null, options: scope);
    }
}
