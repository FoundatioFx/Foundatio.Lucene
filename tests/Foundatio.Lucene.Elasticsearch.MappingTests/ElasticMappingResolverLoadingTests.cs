using System.Diagnostics;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

/// <summary>Loading, caching, throttling, and coalescing through a synchronous loader.</summary>
public class ElasticMappingResolverLoadingTests(ITestOutputHelper output) : MappingTestBase(output)
{
    [Fact]
    public void RefreshMapping_WhenCalled_ClearsCachedMappings()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() => Interlocked.Increment(ref fetchCount) <= 1
            ? CreateTextOnlyMapping("name")
            : CreateTextWithKeywordMapping("name"), Inferrer, logger: Logger);

        // Act
        string beforeRefresh = resolver.GetNonAnalyzedFieldName("name", "keyword");
        resolver.RefreshMapping();
        string afterRefresh = resolver.GetNonAnalyzedFieldName("name", "keyword");

        // Assert
        Assert.Equal("name", beforeRefresh);
        Assert.Equal("name.keyword", afterRefresh);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public void GetNonAnalyzedFieldName_WithCodeAndServerMerge_ReturnsKeywordSubField()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordMapping("name"), Inferrer, () => CreateTextOnlyMapping("name"), logger: Logger);
        resolver.RefreshMapping();

        // Act
        string result = resolver.GetNonAnalyzedFieldName("name", "keyword");

        // Assert
        Assert.Equal("name.keyword", result);
    }

    [Fact]
    public void GetNonAnalyzedFieldName_AfterRefreshAndServerMappingChange_ReturnsUpdatedKeywordPath()
    {
        // Arrange
        int callCount = 0;
        using var resolver = new ElasticMappingResolver(CreateTextOnlyMapping("name"), Inferrer,
            () => Interlocked.Increment(ref callCount) <= 1 ? null : CreateTextWithKeywordMapping("name"), logger: Logger);

        // Act
        string initial = resolver.GetNonAnalyzedFieldName("name", "keyword");
        resolver.RefreshMapping();
        string updated = resolver.GetNonAnalyzedFieldName("name", "keyword");

        // Assert
        Assert.Equal("name", initial);
        Assert.Equal("name.keyword", updated);
    }

    [Fact]
    public async Task GetMapping_WithConcurrentRefreshMapping_AlwaysReturnsKeywordPath()
    {
        // Arrange
        using var resolver = new ElasticMappingResolver(CreateTextWithKeywordMapping("name"), Inferrer, () =>
        {
            Thread.Yield();
            return CreateTextWithKeywordMapping("name");
        }, logger: Logger);
        const int iterations = 200;
        using var barrier = new Barrier(3);

        // Act
        var readerTask = Task.Run(() =>
        {
            barrier.SignalAndWait(TestCancellationToken);
            for (int i = 0; i < iterations; i++)
                Assert.Equal("name.keyword", resolver.GetNonAnalyzedFieldName("name", "keyword"));
        }, TestCancellationToken);
        var aggregationReaderTask = Task.Run(() =>
        {
            barrier.SignalAndWait(TestCancellationToken);
            for (int i = 0; i < iterations; i++)
                Assert.Equal("name.keyword", resolver.GetAggregationsFieldName("name"));
        }, TestCancellationToken);
        var refreshTask = Task.Run(() =>
        {
            barrier.SignalAndWait(TestCancellationToken);
            for (int i = 0; i < iterations; i++)
            {
                resolver.RefreshMapping();
                Thread.Yield();
            }
        }, TestCancellationToken);

        // Assert
        await Task.WhenAll(readerTask, aggregationReaderTask, refreshTask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshMapping_DuringInFlightRefresh_DiscardsSupersededResult(bool throwException)
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            int callNumber = Interlocked.Increment(ref fetchCount);
            var capturedMapping = serverMapping;
            if (callNumber == 2)
            {
                fetchStarted.Set();
                releaseFetch.Wait(TimeSpan.FromSeconds(30));
                if (throwException)
                    throw new InvalidOperationException("Elasticsearch is unavailable");
            }

            return capturedMapping;
        }, Inferrer, logger: Logger);

        Assert.True(resolver.GetMapping("name").Found);
        var staleLookup = Task.Run(() => resolver.GetMapping("idx.keyword-000001"), TestCancellationToken);
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));

        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        resolver.RefreshMapping();

        // Act - the superseded fetch completes with the mapping it captured before the refresh.
        releaseFetch.Set();
        var staleResult = await staleLookup;
        var refreshedResult = resolver.GetMapping("idx.keyword-000001");

        // Assert
        Assert.False(staleResult.Found);
        Assert.True(refreshedResult.Found);
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public void GetMapping_WithResolvedField_DoesNotRefetchServerMapping()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);

        // Act
        resolver.GetNonAnalyzedFieldName("name", "keyword");
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        resolver.GetNonAnalyzedFieldName("name", "keyword");

        // Assert
        Assert.Equal(1, fetchCount);
        Assert.True(resolver.IsLoaded);
    }

    [Fact]
    public void GetMapping_WithUnknownFieldOnColdStart_FetchesServerMappingOnce()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var mapping = resolver.GetMapping("missing");

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public void GetMapping_WithMissingFieldAfterColdStart_TreatsColdStartAsTheRefreshAttempt()
    {
        // Arrange - a load that already found the field missing must not be repeated immediately, which is what
        // keeps a synchronous build after an asynchronous resolution phase from fetching again.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);

        // Act
        Assert.False(resolver.GetMapping("missing").Found);
        Assert.False(resolver.GetMapping("missing").Found);
        int withinInterval = fetchCount;
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        Assert.False(resolver.GetMapping("missing").Found);

        // Assert
        Assert.Equal(1, withinInterval);
        Assert.Equal(2, fetchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithUnchangedServerRevision_PreservesResolvedFields(bool asynchronous)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        int fetches = 0;
        TypeMapping Load()
        {
            fetches++;
            return CreateTextWithKeywordMapping("name");
        }

        using var resolver = asynchronous
            ? ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(Load()), Inferrer, timeProvider)
            : new ElasticMappingResolver(Load, Inferrer, timeProvider);
        resolver.ServerMappingRevisionResolver = _ => "index-uuid:1";

        // Act & Assert
        var original = await ResolveAsync(resolver, "name.keyword", asynchronous);
        Assert.True(original.Found);
        for (int i = 0; i < 3; i++)
        {
            timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
            Assert.False((await ResolveAsync(resolver, "missing", asynchronous)).Found);
            Assert.Same(original, await ResolveAsync(resolver, "name.keyword", asynchronous));
            Assert.False((await ResolveAsync(resolver, "another-missing", asynchronous)).Found);
            Assert.Equal(i + 2, fetches);
        }
    }

    [Theory]
    [InlineData("index-uuid:2", false)]
    [InlineData("new-index-uuid:1", false)]
    [InlineData("INDEX-UUID:1", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("index-uuid:1", true)]
    public async Task GetMapping_WithChangedOrInvalidatedServerRevision_RebuildsMapping(string? revision, bool explicitRefresh)
    {
        // Arrange
        var mapping = CreateTextWithKeywordMapping("name");
        string? currentRevision = "index-uuid:1";
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(mapping), Inferrer);
        resolver.ServerMappingRevisionResolver = _ => currentRevision;
        var original = await resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken);

        mapping = new TypeMapping { Properties = CreateProperties(("name", new KeywordProperty()), ("new-field", new BooleanProperty())) };
        currentRevision = revision;
        if (explicitRefresh)
            resolver.RefreshMapping();

        // Act
        bool found = await resolver.EnsureFieldsAsync(["new-field"], TestCancellationToken);
        var updated = resolver.GetMapping("name");

        // Assert
        Assert.True(found);
        Assert.NotSame(original, updated);
        Assert.IsType<KeywordProperty>(updated.Property);
    }

    [Fact]
    public void GetMapping_WithoutServerRevision_RebuildsMutatedMapping()
    {
        // Arrange
        var mapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() => mapping, Inferrer);
        var original = resolver.GetMapping("name");

        mapping.Properties!["name"] = new KeywordProperty();
        mapping.Properties.Add("new-field", new BooleanProperty());

        // Act & Assert
        Assert.True(resolver.GetMapping("new-field").Found);
        Assert.NotSame(original, resolver.GetMapping("name"));
        Assert.IsType<KeywordProperty>(resolver.GetMappingProperty("name"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithFailingServerRevisionResolver_PreservesMappingAndRetries(bool asynchronous)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var mapping = CreateTextWithKeywordMapping("name");
        using var resolver = asynchronous
            ? ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(mapping), Inferrer, timeProvider)
            : new ElasticMappingResolver(() => mapping, Inferrer, timeProvider);
        bool fail = false;
        resolver.ServerMappingRevisionResolver = _ => fail ? throw new InvalidOperationException("Revision unavailable") : "index-uuid:1";
        var original = await ResolveAsync(resolver, "name", asynchronous);

        // Act & Assert
        fail = true;
        mapping = new TypeMapping { Properties = CreateProperties(("new-field", new BooleanProperty())) };
        Assert.False((await ResolveAsync(resolver, "new-field", asynchronous)).Found);
        Assert.Same(original, await ResolveAsync(resolver, "name", asynchronous));

        fail = false;
        resolver.ServerMappingRevisionResolver = _ => "index-uuid:2";
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        Assert.True((await ResolveAsync(resolver, "new-field", asynchronous)).Found);
    }

    [Fact]
    public void GetMapping_WithChangedResolvedField_RequiresExplicitRefresh()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return serverMapping;
        }, Inferrer, timeProvider, Logger);

        Assert.IsType<TextProperty>(resolver.GetMappingProperty("name"));
        serverMapping = new TypeMapping { Properties = CreateProperties(("name", new KeywordProperty())) };
        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var staleProperty = resolver.GetMappingProperty("name");
        resolver.RefreshMapping();
        var refreshedProperty = resolver.GetMappingProperty("name");

        // Assert
        Assert.IsType<TextProperty>(staleProperty);
        Assert.IsType<KeywordProperty>(refreshedProperty);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public void IsNestedPropertyType_WithNestedFieldCreatedAfterColdStartFetch_ReturnsTrue()
    {
        // Arrange - a dynamic template creates idx.nested-000001 only after the first document is indexed.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var serverMapping = CreateTextWithKeywordMapping("field1");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, timeProvider, Logger);
        Assert.Equal("field1.keyword", resolver.GetNonAnalyzedFieldName("field1", "keyword"));

        // Act
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        serverMapping = CreateDynamicCustomFieldMapping("field1", "nested-000001", new NestedProperty { Properties = CreateProperties(("value", new KeywordProperty())) });

        // Assert
        Assert.True(resolver.IsNestedPropertyType("idx.nested-000001"));
        Assert.Equal("idx.nested-000001.value", resolver.GetResolvedField("idx.nested-000001.value"));
    }

    [Fact]
    public void GetSortFieldName_WithKeywordSubFieldCreatedAfterColdStartFetch_ReturnsKeywordSubField()
    {
        // Arrange - sorting on a text field without its keyword sub field fails in Elasticsearch, so a stale
        // mapping is a hard failure here.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var serverMapping = CreateTextWithKeywordMapping("field1");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, timeProvider, Logger);
        resolver.GetNonAnalyzedFieldName("field1", "keyword");

        // Act
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        serverMapping = CreateDynamicCustomFieldMapping("field1", "string-000001",
            new TextProperty { Fields = CreateProperties(("keyword", new KeywordProperty { IgnoreAbove = 256 })) });

        // Assert
        Assert.Equal("idx.string-000001.keyword", resolver.GetSortFieldName("idx.string-000001"));
    }

    [Fact]
    public void GetMapping_WithUnmappedFieldWithinUnmappedRefreshInterval_DoesNotRefetchServerMapping()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);
        resolver.GetMapping("name");

        // Act
        resolver.GetMapping("missing_one");
        int afterFirstMiss = fetchCount;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        resolver.GetMapping("missing_two");

        // Assert
        Assert.Equal(2, afterFirstMiss);
        Assert.Equal(afterFirstMiss, fetchCount);
    }

    [Fact]
    public void GetMapping_WithContinuousDistinctMisses_AttemptsAtMostOncePerRefreshInterval()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);

        // Act - twelve intervals model one minute of continuous caller-controlled misses.
        for (int interval = 0; interval < 12; interval++)
        {
            for (int miss = 0; miss < 25; miss++)
                Assert.False(resolver.GetMapping($"missing_{interval}_{miss}").Found);

            if (interval < 11)
                timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        }

        // Assert - one cold start plus at most twelve automatic reload attempts.
        Assert.Equal(13, fetchCount);
    }

    [Fact]
    public void GetMapping_WhenTimeProviderTimestampStartsAtZero_ThrottlesRepeatedMisses()
    {
        // Arrange
        var timeProvider = new ZeroOriginTimeProvider();
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);

        // Act
        Assert.False(resolver.GetMapping("missing1").Found);
        Assert.False(resolver.GetMapping("missing2").Found);
        int withinInterval = fetchCount;
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        Assert.False(resolver.GetMapping("missing3").Found);

        // Assert
        Assert.Equal(2, withinInterval);
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public void GetMapping_WithNewFieldAfterUnrelatedMiss_RefreshesAtUnmappedFieldInterval()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return serverMapping;
        }, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);
        Assert.False(resolver.GetMapping("typo").Found);

        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);

        // Act
        var mapping = resolver.GetMapping("idx.keyword-000001");

        // Assert
        Assert.True(mapping.Found);
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public void GetMapping_WithMissAfterFieldIsCreated_ResolvesFieldOnNextLookup()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, timeProvider, Logger);
        Assert.False(resolver.GetMapping("idx.keyword-000001").Found);

        // Act
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var afterCreate = resolver.GetMapping("idx.keyword-000001");

        // Assert
        Assert.True(afterCreate.Found);
        Assert.Equal("idx.keyword-000001", afterCreate.FullPath);
    }

    [Fact]
    public void RefreshMapping_WithNewlyCreatedField_ResolvesFieldWithoutWaitingForThrottle()
    {
        // Arrange - arm the unmapped field throttle before the field exists.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, timeProvider, Logger);
        Assert.False(resolver.GetMapping("name.missing").Found);
        Assert.False(resolver.GetMapping("idx.keyword-000001").Found);

        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        Assert.False(resolver.GetMapping("idx.keyword-000001").Found);

        // Act
        resolver.RefreshMapping();
        var mapping = resolver.GetMapping("idx.keyword-000001");

        // Assert
        Assert.True(mapping.Found);
    }

    [Fact]
    public void GetMapping_WhenServerMappingFuncThrows_DoesNotRefetchOnEveryLookup()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            throw new InvalidOperationException("Elasticsearch is unavailable");
        }, Inferrer, timeProvider, Logger);

        // Act
        for (int i = 0; i < 25; i++)
            Assert.False(resolver.GetMapping($"field_{i}").Found);

        // Assert - the failed cold start also arms the throttle, so a lookup cannot immediately retry.
        Assert.Equal(1, fetchCount);
        Assert.False(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMapping_WithContinuousFailedRefreshes_AttemptsAtMostOncePerRefreshInterval(bool throwException)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        bool failRefresh = false;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            if (!failRefresh)
                return CreateTextWithKeywordMapping("name");

            return throwException ? throw new InvalidOperationException("Elasticsearch is unavailable") : null;
        }, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);
        failRefresh = true;

        // Act
        for (int interval = 0; interval < 12; interval++)
        {
            for (int miss = 0; miss < 25; miss++)
                Assert.False(resolver.GetMapping($"missing_{interval}_{miss}").Found);

            Assert.True(resolver.GetMapping("name").Found);
            if (interval < 11)
                timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        }

        // Assert - failures preserve the known mapping and obey the same ceiling as successful reloads.
        Assert.Equal(13, fetchCount);
        Assert.True(resolver.IsLoaded);
    }

    [Fact]
    public void RefreshMapping_WithinMissCooldown_PerformsUnthrottledHardRefresh()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, new FakeTimeProvider(DateTimeOffset.UtcNow), Logger);

        // Act
        Assert.True(resolver.GetMapping("name").Found);
        Assert.False(resolver.GetMapping("missing").Found);
        resolver.RefreshMapping();
        Assert.False(resolver.IsLoaded);
        Assert.True(resolver.GetMapping("name").Found);

        // Assert
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public void RefreshMapping_AfterTargetRecreation_DiscardsLastKnownGoodMapping()
    {
        // Arrange
        TypeMapping? serverMapping = CreateTextOnlyMapping("name");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, new FakeTimeProvider(DateTimeOffset.UtcNow), Logger);
        Assert.Equal("name", resolver.GetNonAnalyzedFieldName("name", "keyword"));

        // An automatic failure during the deletion gap retains the last known good mapping.
        serverMapping = null;
        Assert.False(resolver.GetMapping("missing").Found);
        Assert.Equal("name", resolver.GetNonAnalyzedFieldName("name", "keyword"));

        // Act - an explicit refresh discards it, and a second one bypasses the failed-load cooldown.
        resolver.RefreshMapping();
        Assert.False(resolver.GetMapping("name").Found);

        serverMapping = CreateTextWithKeywordMapping("name");
        resolver.RefreshMapping();
        string recreatedField = resolver.GetNonAnalyzedFieldName("name", "keyword");

        // Assert
        Assert.Equal("name.keyword", recreatedField);
    }

    [Fact]
    public void GetMapping_WhenAutomaticRefreshReturnsNull_PreservesLastKnownGoodMapping()
    {
        // Arrange
        int fetchCount = 0;
        TypeMapping? serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return serverMapping;
        }, Inferrer, new FakeTimeProvider(DateTimeOffset.UtcNow), Logger);
        Assert.True(resolver.GetMapping("name").Found);
        serverMapping = null;

        // Act
        Assert.False(resolver.GetMapping("missing").Found);
        var knownMapping = resolver.GetMapping("name");

        // Assert
        Assert.True(knownMapping.Found);
        Assert.Equal(2, fetchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMapping_AfterFailedMissRefresh_RetriesAfterInterval(bool throwException)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        bool failRefresh = false;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            if (failRefresh)
                return throwException ? throw new InvalidOperationException("Elasticsearch is unavailable") : null;

            return serverMapping;
        }, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);
        failRefresh = true;
        Assert.False(resolver.GetMapping("missing").Found);

        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        failRefresh = false;

        // Act
        Assert.False(resolver.GetMapping("idx.keyword-000001").Found);
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        var mapping = resolver.GetMapping("idx.keyword-000001");

        // Assert
        Assert.True(mapping.Found);
        Assert.Equal(3, fetchCount);
    }

    [Fact]
    public async Task GetMapping_WithConcurrentUnmappedLookups_FetchesServerMappingOnce()
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                fetchStarted.Set();
                releaseFetch.Wait(TimeSpan.FromSeconds(30));
            }

            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        resolver.GetMapping("name");

        // Act
        var lookups = Enumerable.Range(0, 100).Select(i => Task.Run(() => resolver.GetMapping($"missing{i}"), TestCancellationToken)).ToArray();
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));
        releaseFetch.Set();
        await Task.WhenAll(lookups);

        // Assert
        Assert.Equal(2, fetchCount);
        Assert.All(lookups, task => Assert.False(task.Result.Found));
    }

    [Fact]
    public async Task GetMapping_WithCachedKnownFieldDuringBlockedRefresh_ReturnsImmediately()
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                fetchStarted.Set();
                releaseFetch.Wait(TimeSpan.FromSeconds(30));
            }

            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        Assert.True(resolver.GetMapping("name").Found);
        var blockedMiss = Task.Run(() => resolver.GetMapping("missing"), TestCancellationToken);
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));

        // Act
        var cachedLookup = Task.Run(() => resolver.GetMapping("name"), TestCancellationToken);

        // Assert
        try
        {
            var cachedMapping = await cachedLookup.WaitAsync(TimeSpan.FromSeconds(1), TestCancellationToken);
            Assert.True(cachedMapping.Found);
        }
        finally
        {
            releaseFetch.Set();
            await blockedMiss;
        }
    }

    [Fact]
    public async Task GetMapping_WhenAnotherCallerPublishesNewerMapping_UsesNewerMapping()
    {
        // Arrange - pause a reload after it published its snapshot but before it released its lock.
        using var timeProvider = new BlockingTimeProvider();
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() => serverMapping, Inferrer, timeProvider, Logger);
        Assert.True(resolver.GetMapping("name").Found);
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());

        timeProvider.BlockNextTimestampRead();
        var staleLookup = Task.Run(() => resolver.GetMapping("idx.keyword-000001"), TestCancellationToken);
        Assert.True(timeProvider.WaitUntilBlocked(TimeSpan.FromSeconds(10)));

        // Act
        var freshLookup = resolver.GetMapping("idx.keyword-000001");
        timeProvider.ReleaseTimestampRead();
        var result = await staleLookup;

        // Assert
        Assert.True(freshLookup.Found);
        Assert.True(result.Found);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithConcurrentFailedMissRefresh_AttemptsOnce(bool throwException)
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            if (Interlocked.Increment(ref fetchCount) == 1)
                return CreateTextWithKeywordMapping("name");

            fetchStarted.Set();
            releaseFetch.Wait(TimeSpan.FromSeconds(30));
            return throwException ? throw new InvalidOperationException("Elasticsearch is unavailable") : null;
        }, Inferrer, logger: Logger);
        Assert.True(resolver.GetMapping("name").Found);

        // Act
        var lookups = Enumerable.Range(0, 100).Select(i => Task.Run(() => resolver.GetMapping($"missing{i}"), TestCancellationToken)).ToArray();
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));
        releaseFetch.Set();
        await Task.WhenAll(lookups);

        // Assert
        Assert.Equal(2, fetchCount);
        Assert.All(lookups, task => Assert.False(task.Result.Found));
        Assert.True(resolver.GetMapping("name").Found);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetMapping_WithConcurrentFailedInitialFetch_AttemptsOnce(bool throwException)
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.Set();
            releaseFetch.Wait(TimeSpan.FromSeconds(30));
            return throwException ? throw new InvalidOperationException("Elasticsearch is unavailable") : null;
        }, Inferrer, logger: Logger);

        // Act
        var lookups = Enumerable.Range(0, 100).Select(i => Task.Run(() => resolver.GetMapping($"field{i}"), TestCancellationToken)).ToArray();
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));
        releaseFetch.Set();
        await Task.WhenAll(lookups);

        // Assert
        Assert.Equal(1, fetchCount);
        Assert.All(lookups, task => Assert.False(task.Result.Found));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMapping_WhenInitialLoadFails_FallsBackToCodeMappingUntilRetry(bool throwException)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        var codeMapping = new TypeMapping { Properties = CreateProperties(("value", new TextProperty())) };
        using var resolver = new ElasticMappingResolver(codeMapping, Inferrer, () =>
        {
            if (Interlocked.Increment(ref fetchCount) == 1)
                return throwException ? throw new InvalidOperationException("Elasticsearch is unavailable") : null;

            return new TypeMapping { Properties = CreateProperties(("value", new KeywordProperty())) };
        }, timeProvider, Logger);
        Assert.IsType<TextProperty>(resolver.GetMapping("value").Property);
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);

        // Act
        var recovered = resolver.GetMapping("value");

        // Assert
        Assert.IsType<KeywordProperty>(recovered.Property);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task GetMapping_WithConcurrentSuccessfulInitialFetch_AttemptsOnce()
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        using var callersStarted = new CountdownEvent(20);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.Set();
            releaseFetch.Wait(TimeSpan.FromSeconds(30));
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var lookups = Enumerable.Range(0, 20)
            .Select(_ => Task.Factory.StartNew(() =>
            {
                callersStarted.Signal();
                return resolver.GetMapping("name");
            }, TestCancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        try
        {
            Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));
            Assert.True(callersStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));
        }
        finally
        {
            releaseFetch.Set();
        }

        await Task.WhenAll(lookups);

        // Assert
        Assert.Equal(1, fetchCount);
        Assert.All(lookups, task => Assert.True(task.Result.Found));
    }

    [Fact]
    public async Task GetMapping_WhenInitialFetchOutlastsJoinTimeout_WaitsOnlyOnce()
    {
        // Arrange
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        using var resolver = new ElasticMappingResolver(() =>
        {
            fetchStarted.Set();
            releaseFetch.Wait(TimeSpan.FromSeconds(30));
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger)
        {
            MappingRefreshWaitTimeout = TimeSpan.FromSeconds(1)
        };
        var initialLookup = Task.Run(() => resolver.GetMapping("name"), TestCancellationToken);
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));

        // Act
        FieldMapping timedOutLookup;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            timedOutLookup = resolver.GetMapping("missing");
        }
        finally
        {
            stopwatch.Stop();
            releaseFetch.Set();
            await initialLookup;
        }

        // Assert
        Assert.False(timedOutLookup.Found);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1750),
            $"Lookup waited {stopwatch.Elapsed} even though the configured join timeout was {resolver.MappingRefreshWaitTimeout}.");
    }

    [Fact]
    public async Task GetMapping_WithLookupDuringFetchThatOutlastedJoinTimeout_StillJoinsInFlightFetch()
    {
        // Arrange - a lookup that gives up joining an in-flight fetch reports the field as unmapped, but later
        // lookups must still join the same fetch and get the real answer.
        using var fetchStarted = new ManualResetEventSlim(false);
        using var releaseFetch = new ManualResetEventSlim(false);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = new ElasticMappingResolver(() =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                fetchStarted.Set();
                releaseFetch.Wait(TimeSpan.FromSeconds(30));
            }

            return serverMapping;
        }, Inferrer, logger: Logger);
        resolver.GetMapping("name");
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        var inFlightFetch = Task.Run(() => resolver.GetMapping("idx.keyword-000001"), TestCancellationToken);
        Assert.True(fetchStarted.Wait(TimeSpan.FromSeconds(10), TestCancellationToken));

        // Act
        resolver.MappingRefreshWaitTimeout = TimeSpan.FromMilliseconds(50);
        var timedOutLookup = resolver.GetMapping("idx.keyword-000001");
        resolver.MappingRefreshWaitTimeout = TimeSpan.FromSeconds(30);
        var joiningLookup = Task.Run(() => resolver.GetMapping("idx.keyword-000001"), TestCancellationToken);
        await Task.Delay(200, TestCancellationToken);
        releaseFetch.Set();

        // Assert
        Assert.False(timedOutLookup.Found);
        var joined = await joiningLookup;
        Assert.True(joined.Found);
        Assert.Equal("idx.keyword-000001", joined.FullPath);
        Assert.True((await inFlightFetch).Found);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public void GetMapping_WithManyDistinctMisses_KeepsKnownFieldsResolvable()
    {
        // Arrange
        var properties = CreateProperties(("name", new KeywordProperty()), ("status", new KeywordProperty()), ("created", new DateProperty()), ("count", new LongNumberProperty()));
        string[] realFields = ["name", "status", "created", "count"];
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return new TypeMapping { Properties = properties };
        }, Inferrer, new FakeTimeProvider(DateTimeOffset.UtcNow), Logger);
        resolver.UnmappedFieldRefreshInterval = TimeSpan.FromHours(1);
        foreach (string realField in realFields)
            Assert.True(resolver.GetMapping(realField).Found);

        // Act
        for (int i = 0; i < 100; i++)
            Assert.False(resolver.GetMapping($"missing{i}").Found);

        // Assert
        Assert.Equal(2, fetchCount);
        foreach (string realField in realFields)
            Assert.True(resolver.GetMapping(realField).Found);
    }

    [Fact]
    public void GetMapping_AfterFailedColdStart_RetriesOnceIntervalElapses()
    {
        // Arrange - a failed cold start must not permanently mark the mapping as loaded.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        bool serverUnavailable = true;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return serverUnavailable ? throw new InvalidOperationException("Elasticsearch is unavailable") : CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, Logger);
        Assert.False(resolver.GetMapping("name").Found);
        Assert.Equal(1, fetchCount);

        // Act
        serverUnavailable = false;
        timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);

        // Assert
        Assert.True(resolver.GetMapping("name").Found);
        Assert.True(resolver.IsLoaded);
    }

    [Fact]
    public void GetMapping_WithSuppressedReloads_LogsAtMostOneWarningPerWarningInterval()
    {
        // Arrange
        var capturingLogger = new CapturingLogger();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, timeProvider, capturingLogger);
        // A reload interval longer than the warning window keeps every later miss suppressed.
        resolver.UnmappedFieldRefreshInterval = TimeSpan.FromMinutes(5);
        Assert.True(resolver.GetMapping("name").Found);

        // Act - the first miss reloads and arms the throttle; every later miss is suppressed by it.
        Assert.False(resolver.GetMapping("missing1").Found);
        resolver.GetMapping("missing2");
        int warningsAfterFirstSuppression = CountUnresolvedFieldWarnings(capturingLogger);
        resolver.GetMapping("missing3");
        timeProvider.Advance(TimeSpan.FromSeconds(45));
        resolver.GetMapping("missing4");
        timeProvider.Advance(TimeSpan.FromSeconds(16));
        resolver.GetMapping("missing5");

        // Assert
        Assert.Equal(2, fetchCount);
        Assert.Equal(1, warningsAfterFirstSuppression);
        Assert.Equal(2, CountUnresolvedFieldWarnings(capturingLogger));
    }

    [Fact]
    public void GetMapping_WhenLoaderFails_LogsError()
    {
        // Arrange
        var capturingLogger = new CapturingLogger();
        using var resolver = new ElasticMappingResolver(() => throw new InvalidOperationException("Elasticsearch is unavailable"), Inferrer, logger: capturingLogger);

        // Act
        resolver.GetMapping("name");

        // Assert
        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Elasticsearch is unavailable"));
    }

    private static int CountUnresolvedFieldWarnings(CapturingLogger logger)
    {
        return logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Unable to resolve mapping for field"));
    }

    private static async ValueTask<FieldMapping> ResolveAsync(ElasticMappingResolver resolver, string field, bool asynchronous)
    {
        return asynchronous
            ? await resolver.GetMappingAsync(field, cancellationToken: TestCancellationToken)
            : resolver.GetMapping(field);
    }
}
