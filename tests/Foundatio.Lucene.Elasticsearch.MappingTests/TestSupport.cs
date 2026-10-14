using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lucene.Elasticsearch.MappingTests;

/// <summary>Shared inferrer, logger, and mapping builders for resolver unit tests.</summary>
public abstract class MappingTestBase : IDisposable
{
    private readonly ElasticsearchClientSettings _clientSettings;

    protected MappingTestBase(ITestOutputHelper output)
    {
        _clientSettings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"));
        Inferrer = new Inferrer(_clientSettings);
        Logger = new TestOutputLogger(output);
    }

    protected Inferrer Inferrer { get; }

    protected ILogger Logger { get; }

    protected static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        ((IDisposable)_clientSettings).Dispose();
        GC.SuppressFinalize(this);
    }

    protected static Properties CreateProperties(params (string Name, IProperty Property)[] properties)
    {
        var result = new Properties();
        foreach ((string name, var property) in properties)
            result.Add(name, property);

        return result;
    }

    protected static TypeMapping CreateTextWithKeywordMapping(string fieldName)
    {
        return new TypeMapping
        {
            Properties = CreateProperties((fieldName, new TextProperty { Fields = CreateProperties(("keyword", new KeywordProperty { IgnoreAbove = 256 })) }))
        };
    }

    protected static TypeMapping CreateTextWithKeywordAndSortMapping(string fieldName)
    {
        return new TypeMapping
        {
            Properties = CreateProperties((fieldName, new TextProperty
            {
                Fields = CreateProperties(("keyword", new KeywordProperty { IgnoreAbove = 256 }), ("sort", new KeywordProperty { IgnoreAbove = 256 }))
            }))
        };
    }

    protected static TypeMapping CreateTextOnlyMapping(string fieldName)
    {
        return new TypeMapping { Properties = CreateProperties((fieldName, new TextProperty())) };
    }

    /// <summary>
    /// Builds a mapping containing the original field plus an <c>idx.&lt;name&gt;</c> custom field of the kind created
    /// at runtime by an <c>idx.*</c> dynamic template.
    /// </summary>
    protected static TypeMapping CreateDynamicCustomFieldMapping(string existingFieldName, string customFieldName, IProperty customField)
    {
        var mapping = CreateTextWithKeywordMapping(existingFieldName);
        mapping.Properties!.Add("idx", new ObjectProperty { Properties = CreateProperties((customFieldName, customField)) });
        return mapping;
    }

    protected static IProperty CreateProperty(string propertyType) => propertyType switch
    {
        "keyword" => new KeywordProperty(),
        "date" => new DateProperty(),
        "long" => new LongNumberProperty(),
        "boolean" => new BooleanProperty(),
        "ip" => new IpProperty(),
        _ => throw new ArgumentOutOfRangeException(nameof(propertyType))
    };

    protected static void SetMultiFields(IProperty property, Properties fields)
    {
        switch (property)
        {
            case KeywordProperty p: p.Fields = fields; break;
            case DateProperty p: p.Fields = fields; break;
            case LongNumberProperty p: p.Fields = fields; break;
            case BooleanProperty p: p.Fields = fields; break;
            case IpProperty p: p.Fields = fields; break;
            default: throw new ArgumentOutOfRangeException(nameof(property));
        }
    }
}

/// <summary>Writes log entries to the xUnit test output.</summary>
public sealed class TestOutputLogger(ITestOutputHelper output) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            output.WriteLine($"[{logLevel}] {formatter(state, exception)}{(exception is null ? "" : " " + exception.Message)}");
        }
        catch (InvalidOperationException)
        {
            // Background work can log after the test that owns the output helper has finished.
        }
    }
}

/// <summary>Captures log entries so tests can assert on them.</summary>
public sealed class CapturingLogger : ILogger
{
    private readonly object _lock = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToArray();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
            _entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>A time provider whose timestamps start at zero, which must not be mistaken for "never".</summary>
public sealed class ZeroOriginTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

    public void Advance(TimeSpan value) => Interlocked.Add(ref _timestamp, value.Ticks);
}

/// <summary>A time provider that can block the next timestamp read to pause a caller mid-load.</summary>
public sealed class BlockingTimeProvider : TimeProvider, IDisposable
{
    private readonly ManualResetEventSlim _timestampReadBlocked = new(false);
    private readonly ManualResetEventSlim _releaseTimestampRead = new(false);
    private long _timestamp;
    private int _blockNextTimestampRead;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Volatile.Read(ref _timestamp));

    public override long GetTimestamp()
    {
        if (Interlocked.Exchange(ref _blockNextTimestampRead, 0) == 1)
        {
            _timestampReadBlocked.Set();
            _releaseTimestampRead.Wait(TimeSpan.FromSeconds(30));
        }

        return Volatile.Read(ref _timestamp);
    }

    public void Advance(TimeSpan value) => Interlocked.Add(ref _timestamp, value.Ticks);

    public void BlockNextTimestampRead()
    {
        _timestampReadBlocked.Reset();
        _releaseTimestampRead.Reset();
        Volatile.Write(ref _blockNextTimestampRead, 1);
    }

    public bool WaitUntilBlocked(TimeSpan timeout) => _timestampReadBlocked.Wait(timeout);

    public void ReleaseTimestampRead() => _releaseTimestampRead.Set();

    public void Dispose()
    {
        _releaseTimestampRead.Set();
        _timestampReadBlocked.Dispose();
        _releaseTimestampRead.Dispose();
    }
}

/// <summary>A synchronization context that never runs posted callbacks, like a blocked UI thread.</summary>
public sealed class NonPumpingSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
    }

    public static Task<T> Run<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        return completion.Task;
    }
}
