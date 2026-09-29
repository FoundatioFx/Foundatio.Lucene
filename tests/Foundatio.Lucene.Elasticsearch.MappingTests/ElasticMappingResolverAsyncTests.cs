using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

/// <summary>The asynchronous resolution phase and how synchronous lookups behave around it.</summary>
public class ElasticMappingResolverAsyncTests(ITestOutputHelper output) : MappingTestBase(output)
{
    [Fact]
    public async Task EnsureLoadedAsync_WithAsyncLoader_EnablesSynchronousLookups()
    {
        // Arrange
        var mapping = CreateTextWithKeywordAndSortMapping("title");
        mapping.Properties!.Add("created", new DateProperty());
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(mapping), Inferrer, logger: Logger);
        Assert.False(resolver.IsLoaded);
        Assert.False(resolver.CanResolveSynchronously);

        // Act
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.True(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
        Assert.Equal("title", resolver.GetResolvedField("TITLE"));
        Assert.Equal("title.sort", resolver.GetSortFieldName("title"));
        Assert.Equal("title.keyword", resolver.GetAggregationsFieldName("title"));
        Assert.True(resolver.IsPropertyAnalyzed("title"));
        Assert.True(resolver.IsDatePropertyType("created"));
        Assert.Equal(FieldType.Date, resolver.GetFieldType("created"));
    }

    [Fact]
    public async Task GetMapping_WithAsyncOnlyLoaderBeforeLoad_ThrowsInvalidOperationException()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(CreateTextOnlyMapping("name"));
        }, Inferrer, logger: Logger);

        // Act
        var exception = Record.Exception(() => resolver.GetMapping("name"));

        // Assert - the synchronous path never waits for, or starts, an asynchronous load.
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(nameof(ElasticMappingResolver.EnsureLoadedAsync), exception.Message);
        Assert.Throws<InvalidOperationException>(() => resolver.GetSortFieldName("name"));
        Assert.Equal(0, fetchCount);
        Assert.True((await resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken)).Found);
        Assert.True(resolver.GetMapping("name").Found);
    }

    [Fact]
    public async Task GetMapping_WithAsyncOnlyLoaderOnNonPumpingSynchronizationContext_FailsFastInsteadOfDeadlocking()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async _ =>
        {
            await Task.Yield();
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var lookup = NonPumpingSynchronizationContext.Run(() => Record.Exception(() => resolver.GetMapping("name")));
        var exception = await lookup.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureLoadedAsync_WhenLoadFails_AllowsSynchronousLookupsAgainstCodeMapping(bool throwException)
    {
        // Arrange
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader<object>(m => m.Properties(p => p.Keyword("code")), Inferrer,
            _ => throwException ? Task.FromException<TypeMapping?>(new InvalidOperationException("Elasticsearch is unavailable")) : Task.FromResult<TypeMapping?>(null),
            logger: Logger);

        // Act
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.False(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
        Assert.True(resolver.GetMapping("code").Found);
        Assert.False(resolver.GetMapping("server").Found);
    }

    [Fact]
    public async Task GetMapping_WithAsyncOnlyLoaderAndMissingField_DoesNotReloadSynchronously()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(CreateTextOnlyMapping("name"));
        }, Inferrer, logger: Logger);
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Act
        var mapping = resolver.GetMapping("missing");

        // Assert
        Assert.False(mapping.Found);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WithMissingFieldOnLoadedMapping_ReloadsOnceAndReportsResult()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(serverMapping);
        }, Inferrer, timeProvider, Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name", " ", "NAME.keyword"], TestCancellationToken));

        // Act
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        bool created = await resolver.EnsureFieldsAsync(["name", "idx.keyword-000001"], TestCancellationToken);
        bool missing = await resolver.EnsureFieldsAsync(["idx.keyword-000002"], TestCancellationToken);

        // Assert - the second miss is within the throttle interval, so it does not reload.
        Assert.True(created);
        Assert.False(missing);
        Assert.Equal(2, fetchCount);
        Assert.True(resolver.GetMapping("idx.keyword-000001").Found);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WithAliasToMissingTarget_ReportsMissingField()
    {
        // Arrange
        var mapping = new TypeMapping
        {
            Properties = CreateProperties(("alias", new FieldAliasProperty { Path = "gone" }), ("good", new FieldAliasProperty { Path = "target" }), ("target", new KeywordProperty()))
        };
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(mapping), Inferrer, logger: Logger);

        // Act & Assert
        Assert.True(await resolver.EnsureFieldsAsync(["good"], TestCancellationToken));
        Assert.False(await resolver.EnsureFieldsAsync(["good", "alias"], TestCancellationToken));
        Assert.Equal("target", (await resolver.GetMappingAsync("good", followAlias: true, TestCancellationToken)).FullPath);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WithMissingFieldOnColdStart_DoesNotLoadAgainInSynchronousBuild()
    {
        // Arrange - models an asynchronous resolution phase followed by a synchronous query build that looks the
        // same fields up again through a resolver that also has a synchronous loader.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int syncFetches = 0;
        int asyncFetches = 0;
        using var resolver = ElasticMappingResolver.CreateWithLoaders(
            () =>
            {
                Interlocked.Increment(ref syncFetches);
                return CreateTextWithKeywordMapping("name");
            },
            _ =>
            {
                Interlocked.Increment(ref asyncFetches);
                return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));
            }, Inferrer, timeProvider: timeProvider, logger: Logger);

        // Act
        bool found = await resolver.EnsureFieldsAsync(["name", "missing"], TestCancellationToken);
        var name = resolver.GetMapping("name");
        var missing = resolver.GetMapping("missing");

        // Assert
        Assert.False(found);
        Assert.True(name.Found);
        Assert.False(missing.Found);
        Assert.Equal(1, asyncFetches);
        Assert.Equal(0, syncFetches);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithConcurrentCallers_SharesOneFetch()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(cancellationToken);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var loads = Enumerable.Range(0, 100)
            .Select(i => i % 2 == 0
                ? resolver.EnsureLoadedAsync(TestCancellationToken).AsTask()
                : resolver.EnsureFieldsAsync(["name"], TestCancellationToken).AsTask())
            .ToArray();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Assert
        Assert.Equal(1, Volatile.Read(ref fetchCount));
        Assert.All(loads, task => Assert.False(task.IsCompleted));
        releaseFetch.TrySetResult();
        await Task.WhenAll(loads);
        Assert.Equal(1, fetchCount);
        Assert.True(resolver.GetMapping("name").Found);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithAsyncOnlyLoader_StartsLoadInline()
    {
        // Arrange
        int callerThread = Environment.CurrentManagedThreadId;
        int loaderThread = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            loaderThread = Environment.CurrentManagedThreadId;
            return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));
        }, Inferrer, logger: Logger);

        // Act
        var load = resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.True(load.IsCompletedSuccessfully);
        await load;
        Assert.Equal(callerThread, loaderThread);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithSynchronousLoaderOnly_RunsLoaderInline()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = new ElasticMappingResolver(() =>
        {
            Interlocked.Increment(ref fetchCount);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var load = resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.True(load.IsCompletedSuccessfully);
        await load;
        Assert.True(resolver.IsLoaded);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WithConcurrentMissingFields_SharesOneFetch()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                fetchStarted.TrySetResult();
                await releaseFetch.Task.WaitAsync(cancellationToken);
            }

            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));

        // Act
        var lookups = Enumerable.Range(0, 100)
            .Select(index => resolver.EnsureFieldsAsync([$"missing_{index}"], TestCancellationToken).AsTask())
            .ToArray();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Assert
        Assert.Equal(2, Volatile.Read(ref fetchCount));
        Assert.All(lookups, task => Assert.False(task.IsCompleted));
        releaseFetch.TrySetResult();
        await Task.WhenAll(lookups);
        Assert.Equal(2, fetchCount);
        Assert.All(lookups, task => Assert.False(task.Result));
    }

    [Fact]
    public async Task EnsureLoadedAsync_WhenCallerCancels_DoesNotCancelSharedFetch()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(cancellationToken);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        using var cancellationSource = new CancellationTokenSource();

        // Act
        var cancelledLoad = resolver.EnsureLoadedAsync(cancellationSource.Token).AsTask();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        await cancellationSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledLoad);

        var joiningLoad = resolver.EnsureFieldsAsync(["name"], TestCancellationToken).AsTask();
        Assert.False(joiningLoad.IsCompleted);
        releaseFetch.TrySetResult();

        // Assert
        Assert.True(await joiningLoad);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WhenAlreadyCancelled_DoesNotStartSharedFetch()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));
        }, Inferrer, logger: Logger);
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.EnsureLoadedAsync(cancellationSource.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.GetMappingAsync("name", cancellationToken: cancellationSource.Token).AsTask());

        // Assert
        Assert.Equal(0, fetchCount);
        Assert.False(resolver.CanResolveSynchronously);
        Assert.True((await resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken)).Found);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WhenMissingFieldLookupIsAlreadyCancelled_DoesNotStartRefresh()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));
        }, Inferrer, logger: Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.EnsureFieldsAsync(["missing"], cancellationSource.Token).AsTask());

        // Assert
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task Dispose_DuringAsyncFetch_CancelsResolverLifetimeLoad()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            fetchStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                fetchCancelled.TrySetResult();
                throw;
            }

            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        var lookup = resolver.EnsureFieldsAsync(["name"], TestCancellationToken).AsTask();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Act
        resolver.Dispose();

        // Assert
        await fetchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        Assert.False(await lookup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureFieldsAsync_WithContinuousFailedRefreshes_AttemptsAtMostOncePerRefreshInterval(bool throwException)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        bool failRefresh = false;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            if (!failRefresh)
                return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));

            return throwException
                ? Task.FromException<TypeMapping?>(new InvalidOperationException("Elasticsearch is unavailable"))
                : Task.FromResult<TypeMapping?>(null);
        }, Inferrer, timeProvider, Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
        failRefresh = true;

        // Act
        for (int interval = 0; interval < 12; interval++)
        {
            for (int miss = 0; miss < 25; miss++)
                Assert.False(await resolver.EnsureFieldsAsync([$"missing_{interval}_{miss}"], TestCancellationToken));

            Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
            if (interval < 11)
                timeProvider.Advance(resolver.UnmappedFieldRefreshInterval);
        }

        // Assert - one cold start plus at most twelve automatic attempts, including null results and exceptions.
        Assert.Equal(13, fetchCount);
    }

    [Fact]
    public async Task GetMappingAsync_WithCachedKnownField_CompletesSynchronously()
    {
        // Arrange
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(CreateTextWithKeywordMapping("name"));
        }, Inferrer, logger: Logger);
        Assert.True((await resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken)).Found);

        // Act
        var cachedLookup = resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken);
        var loaded = resolver.EnsureLoadedAsync(TestCancellationToken);
        var ensured = resolver.EnsureFieldsAsync(["name"], TestCancellationToken);

        // Assert
        Assert.True(cachedLookup.IsCompletedSuccessfully);
        Assert.True(loaded.IsCompletedSuccessfully);
        Assert.True(ensured.IsCompletedSuccessfully);
        Assert.True((await cachedLookup).Found);
        await loaded;
        Assert.True(await ensured);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task Lookups_WithKnownFieldDuringBlockedAsyncRefresh_CompleteImmediately()
    {
        // Arrange
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                refreshStarted.TrySetResult();
                await releaseRefresh.Task.WaitAsync(cancellationToken);
            }

            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
        var blockedMiss = resolver.EnsureFieldsAsync(["missing"], TestCancellationToken).AsTask();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Act
        var cachedLookup = resolver.GetMappingAsync("name", cancellationToken: TestCancellationToken);
        var syncLookup = resolver.GetMapping("name");
        var syncMiss = resolver.GetMapping("other-missing");

        // Assert
        Assert.True(cachedLookup.IsCompletedSuccessfully);
        Assert.True((await cachedLookup).Found);
        Assert.True(syncLookup.Found);
        Assert.False(syncMiss.Found);
        releaseRefresh.TrySetResult();
        Assert.False(await blockedMiss);
    }

    [Fact]
    public async Task EnsureFieldsAsync_WhenJoinTimesOut_LaterCallerStillJoinsSharedFetch()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            if (Interlocked.Increment(ref fetchCount) > 1)
            {
                fetchStarted.TrySetResult();
                await releaseFetch.Task.WaitAsync(cancellationToken);
            }

            return serverMapping;
        }, Inferrer, logger: Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());
        var inFlightLookup = resolver.EnsureFieldsAsync(["idx.keyword-000001"], TestCancellationToken).AsTask();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Act
        resolver.MappingRefreshWaitTimeout = TimeSpan.FromMilliseconds(50);
        bool timedOut = await resolver.EnsureFieldsAsync(["idx.keyword-000001"], TestCancellationToken);
        resolver.MappingRefreshWaitTimeout = TimeSpan.FromSeconds(30);
        var joiningLookup = resolver.EnsureFieldsAsync(["idx.keyword-000001"], TestCancellationToken).AsTask();
        Assert.False(joiningLookup.IsCompleted);
        releaseFetch.TrySetResult();

        // Assert
        Assert.False(timedOut);
        Assert.True(await inFlightLookup);
        Assert.True(await joiningLookup);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WhenJoiningInitialLoadTimesOut_ThrowsTimeoutException()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            fetchStarted.TrySetResult();
            await releaseFetch.Task.WaitAsync(cancellationToken);
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);
        var owner = resolver.EnsureLoadedAsync(TestCancellationToken).AsTask();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        resolver.MappingRefreshWaitTimeout = TimeSpan.FromMilliseconds(50);

        // Act
        var exception = await Record.ExceptionAsync(() => resolver.EnsureLoadedAsync(TestCancellationToken).AsTask());
        releaseFetch.TrySetResult();
        await owner;

        // Assert
        Assert.IsType<TimeoutException>(exception);
        Assert.True(resolver.IsLoaded);
    }

    [Fact]
    public async Task GetMapping_WhileAsyncInitialLoadIsInFlight_UsesSynchronousLoaderWithoutWaiting()
    {
        // Arrange
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int syncFetches = 0;
        int asyncFetches = 0;
        using var resolver = ElasticMappingResolver.CreateWithLoaders(
            () =>
            {
                Interlocked.Increment(ref syncFetches);
                return CreateTextWithKeywordMapping("name");
            },
            async cancellationToken =>
            {
                Interlocked.Increment(ref asyncFetches);
                await releaseFetch.Task.WaitAsync(cancellationToken);
                return CreateTextWithKeywordMapping("name");
            }, Inferrer, logger: Logger);
        var asyncLoad = resolver.EnsureLoadedAsync(TestCancellationToken).AsTask();

        // Act
        var syncLookup = await Task.Run(() => resolver.GetMapping("name"), TestCancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        releaseFetch.TrySetResult();
        await asyncLoad;

        // Assert
        Assert.True(syncLookup.Found);
        Assert.Equal(1, syncFetches);
        Assert.Equal(1, asyncFetches);
        Assert.True(resolver.GetMapping("name").Found);
    }

    [Fact]
    public async Task GetMapping_WhileAsyncRefreshIsInFlight_UsesCurrentMappingWithoutWaitingOrFetching()
    {
        // Arrange
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int syncFetches = 0;
        int asyncFetches = 0;
        using var resolver = ElasticMappingResolver.CreateWithLoaders(
            () =>
            {
                Interlocked.Increment(ref syncFetches);
                return CreateTextWithKeywordMapping("name");
            },
            async cancellationToken =>
            {
                if (Interlocked.Increment(ref asyncFetches) > 1)
                {
                    refreshStarted.TrySetResult();
                    await releaseRefresh.Task.WaitAsync(cancellationToken);
                }

                return CreateTextWithKeywordMapping("name");
            }, Inferrer, logger: Logger);
        await resolver.EnsureLoadedAsync(TestCancellationToken);
        var asyncMiss = resolver.EnsureFieldsAsync(["missing"], TestCancellationToken).AsTask();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);

        // Act
        var syncMiss = await Task.Run(() => resolver.GetMapping("other-missing"), TestCancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        releaseRefresh.TrySetResult();

        // Assert
        Assert.False(syncMiss.Found);
        Assert.False(await asyncMiss);
        Assert.Equal(0, syncFetches);
        Assert.Equal(2, asyncFetches);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WhenOwnerHasNonPumpingSynchronizationContext_JoinersStillComplete()
    {
        // Arrange - an asynchronous loader that captures the owner's context must not stall other callers.
        int fetchCount = 0;
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async _ =>
        {
            Interlocked.Increment(ref fetchCount);
            await Task.Yield();
            return CreateTextWithKeywordMapping("name");
        }, Inferrer, logger: Logger);

        // Act
        var ownerLoad = NonPumpingSynchronizationContext.Run(() => resolver.EnsureLoadedAsync(TestCancellationToken).AsTask()).Unwrap();
        await resolver.EnsureLoadedAsync(TestCancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await ownerLoad.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        // Assert
        Assert.True(resolver.GetMapping("name").Found);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task RefreshAsync_WithinThrottleInterval_ReloadsImmediately()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        int fetchCount = 0;
        var serverMapping = CreateTextOnlyMapping("name");
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult<TypeMapping?>(serverMapping);
        }, Inferrer, timeProvider, Logger);
        Assert.False(await resolver.EnsureFieldsAsync(["missing"], TestCancellationToken));
        serverMapping = CreateTextWithKeywordMapping("name");

        // Act
        await resolver.RefreshAsync(TestCancellationToken);

        // Assert
        Assert.Equal(2, fetchCount);
        Assert.True(resolver.IsLoaded);
        Assert.Equal("name.keyword", resolver.GetAggregationsFieldName("name"));
    }

    [Fact]
    public async Task RefreshMapping_WithAsyncOnlyLoader_KeepsPreviousMappingReadableUntilReload()
    {
        // Arrange
        var serverMapping = CreateTextOnlyMapping("name");
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(_ => Task.FromResult<TypeMapping?>(serverMapping), Inferrer, logger: Logger);
        await resolver.EnsureLoadedAsync(TestCancellationToken);
        serverMapping = CreateTextWithKeywordMapping("name");

        // Act
        resolver.RefreshMapping();
        string beforeReload = resolver.GetAggregationsFieldName("name");
        bool loadedBeforeReload = resolver.IsLoaded;
        await resolver.EnsureLoadedAsync(TestCancellationToken);

        // Assert
        Assert.Equal("name", beforeReload);
        Assert.False(loadedBeforeReload);
        Assert.True(resolver.CanResolveSynchronously);
        Assert.Equal("name.keyword", resolver.GetAggregationsFieldName("name"));
    }

    [Fact]
    public async Task RefreshMapping_DuringAsyncRefresh_DiscardsSupersededResult()
    {
        // Arrange
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        var serverMapping = CreateTextWithKeywordMapping("name");
        using var resolver = ElasticMappingResolver.CreateWithAsyncLoader(async cancellationToken =>
        {
            int callNumber = Interlocked.Increment(ref fetchCount);
            var capturedMapping = serverMapping;
            if (callNumber == 2)
            {
                fetchStarted.TrySetResult();
                await releaseFetch.Task.WaitAsync(cancellationToken);
            }

            return capturedMapping;
        }, Inferrer, logger: Logger);
        Assert.True(await resolver.EnsureFieldsAsync(["name"], TestCancellationToken));
        var staleLookup = resolver.EnsureFieldsAsync(["idx.keyword-000001"], TestCancellationToken).AsTask();
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        serverMapping = CreateDynamicCustomFieldMapping("name", "keyword-000001", new KeywordProperty());

        // Act
        resolver.RefreshMapping();
        bool staleResult = await staleLookup;
        bool refreshedResult = await resolver.EnsureFieldsAsync(["idx.keyword-000001"], TestCancellationToken);
        releaseFetch.TrySetResult();

        // Assert
        Assert.False(staleResult);
        Assert.True(refreshedResult);
        Assert.Equal(3, fetchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FieldNameHelpers_WithColdMissingField_FetchOnce(bool asynchronous)
    {
        // Arrange
        int loads = 0;
        using var resolver = ElasticMappingResolver.CreateWithLoaders(
            () =>
            {
                Interlocked.Increment(ref loads);
                return new TypeMapping { Properties = new Properties() };
            },
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<TypeMapping?>(new TypeMapping { Properties = new Properties() });
            }, Inferrer, logger: Logger);

        // Act
        if (asynchronous)
            Assert.False(await resolver.EnsureFieldsAsync(["missing"], TestCancellationToken));

        string sort = resolver.GetSortFieldName("missing");
        string aggregation = resolver.GetAggregationsFieldName("missing");
        string nonAnalyzed = resolver.GetNonAnalyzedFieldName("missing");

        // Assert
        Assert.Equal("missing", sort);
        Assert.Equal("missing", aggregation);
        Assert.Equal("missing", nonAnalyzed);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task CodeOnlyResolver_Always_IsLoadedWithoutIo()
    {
        // Arrange
        using var resolver = ElasticMappingResolver.Create(CreateTextWithKeywordMapping("name"), Inferrer, Logger);

        // Act
        var load = resolver.EnsureLoadedAsync(TestCancellationToken);
        bool loadedSynchronously = load.IsCompletedSuccessfully;
        await load;
        bool found = await resolver.EnsureFieldsAsync(["name", "missing"], TestCancellationToken);
        resolver.RefreshMapping();

        // Assert
        Assert.True(loadedSynchronously);
        Assert.False(found);
        Assert.True(resolver.IsLoaded);
        Assert.True(resolver.CanResolveSynchronously);
        Assert.Equal("name.keyword", resolver.GetAggregationsFieldName("name"));
    }
}
