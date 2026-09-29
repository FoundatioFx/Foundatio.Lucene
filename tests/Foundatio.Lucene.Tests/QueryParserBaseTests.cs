using System.Collections.Concurrent;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Lucene.Tests;

public class QueryParserBaseTests
{
    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TestQueryParser(null!));
    }

    [Fact]
    public void Configuration_BuiltInVisitors_AreRegisteredInOrder()
    {
        var configuration = new TestQueryParserConfiguration();

        Assert.Equal([typeof(IncludeVisitor), typeof(FieldResolverQueryVisitor), typeof(ValidationVisitor)], configuration.QueryVisitor.Visitors.Select(v => v.GetType()));
    }

    [Fact]
    public void ApplyOptions_OnlyConfiguration_UsesConfiguration()
    {
        // Arrange
        var configuration = CreateConfiguration();
        var parser = new TestQueryParser(configuration);

        // Act
        var context = parser.CreateContext();

        // Assert
        Assert.Same(configuration.ParserOptions, context.ParserOptions);
        Assert.Same(configuration.DefaultFields, context.DefaultFields);
        Assert.Same(configuration.FieldMap, context.FieldMap);
        Assert.Same(configuration.FieldResolver, context.FieldResolver);
        Assert.Same(configuration.AsyncFieldResolver, context.AsyncFieldResolver);
        Assert.Same(configuration.Includes, context.Includes);
        Assert.Same(configuration.IncludeResolver, context.IncludeResolver);
        Assert.Same(configuration.ShouldSkipInclude, context.ShouldSkipInclude);
        Assert.Same(configuration.ValidationOptions, context.ValidationOptions);
        Assert.Same(configuration.TimeProvider, context.TimeProvider);
    }

    [Fact]
    public void ApplyOptions_RegisteredOptions_OverrideConfiguration()
    {
        // Arrange
        var parser = new TestQueryParser(CreateConfiguration());
        var registered = CreateOptions(BooleanOperator.Or);

        // Act
        var context = parser.CreateContext(registered);

        // Assert
        AssertUsesOptions(registered, context);
    }

    [Fact]
    public void ApplyOptions_RequestOptions_OverrideRegisteredOptions()
    {
        // Arrange
        var parser = new TestQueryParser(CreateConfiguration());
        var registered = CreateOptions(BooleanOperator.Or);
        var request = CreateOptions(BooleanOperator.And);

        // Act
        var context = parser.CreateContext(registered, request);

        // Assert
        AssertUsesOptions(request, context);
    }

    [Fact]
    public void ApplyOptions_PartialRequestOptions_FallBackPerSetting()
    {
        // Arrange
        var configuration = CreateConfiguration();
        var parser = new TestQueryParser(configuration);
        var registered = new TestQueryOptions { FieldMap = new FieldMap { { "r", "registered" } } };
        var request = new TestQueryOptions { DefaultFields = ["request"] };

        // Act
        var context = parser.CreateContext(registered, request);

        // Assert
        Assert.Equal(["request"], context.DefaultFields!);
        Assert.Same(registered.FieldMap, context.FieldMap);
        Assert.Same(configuration.Includes, context.Includes);
        Assert.Same(configuration.ParserOptions, context.ParserOptions);
    }

    [Fact]
    public void ProcessQuery_FullPipeline_ExpandsResolvesAndValidates()
    {
        // Arrange
        var configuration = new TestQueryParserConfiguration
        {
            Includes = new Dictionary<string, string> { ["active"] = "status:active" },
            FieldMap = new FieldMap { { "status", "data.status" }, { "user", "account.user" } }
        };
        var parser = new TestQueryParser(configuration);
        var context = parser.CreateContext();

        // Act
        var result = parser.Process("user:john @include:active", context);

        // Assert
        Assert.Equal("(bool +account.user:john +(group data.status:active))", result.ToDebugString());
        Assert.Equal(["status", "user"], context.ValidationResult.ReferencedFields.Order());
        Assert.Equal(["active"], context.ValidationResult.ReferencedIncludes);
    }

    [Fact]
    public void ProcessQuery_SyntaxError_ThrowsQueryParseException()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration());

        var exception = Assert.Throws<QueryParseException>(() => parser.Process("title:(a"));

        Assert.NotEmpty(exception.Errors);
        Assert.Equal(QueryErrorCode.ParseError, exception.ErrorCode);
    }

    [Fact]
    public void ProcessQuery_InvalidQuery_ThrowsQueryValidationException()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            ValidationOptions = options,
            FieldMap = new FieldMap { { "alias", "secret" } }
        });

        // Act
        var exception = Assert.Throws<QueryValidationException>(() => parser.Process("alias:1"));

        // Assert
        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(exception.Errors).Code);
        Assert.StartsWith("Invalid query:", exception.Message);
    }

    [Fact]
    public void ProcessQuery_UnresolvedIncludeByDefault_Throws()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration());

        var exception = Assert.Throws<QueryValidationException>(() => parser.Process("@include:missing"));

        Assert.Equal(QueryErrorCode.UnresolvedInclude, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void ProcessQuery_CallerDocument_IsNotModified()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            Includes = new Dictionary<string, string> { ["active"] = "status:active" },
            FieldMap = new FieldMap { { "user", "account.user" } }
        });
        var document = LuceneQuery.Parse("user:john @include:active").Document;
        string before = document.ToDebugString();

        // Act
        var result = parser.Process(document, parser.CreateContext());

        // Assert
        Assert.NotSame(document, result);
        Assert.Equal(before, document.ToDebugString());
        bool hasData = false;
        document.Walk(n => hasData |= n.HasData);
        Assert.False(hasData);
        Assert.Equal("(bool +account.user:john +(group status:active))", result.ToDebugString());
    }

    [Fact]
    public void ProcessQuery_VisitorReplacesDocument_ThrowsInvalidOperationException()
    {
        var configuration = new TestQueryParserConfiguration();
        configuration.AddVisitor(new UnwrapDocumentVisitor());
        var parser = new TestQueryParser(configuration);

        Assert.Throws<InvalidOperationException>(() => parser.Process("a"));
    }

    [Fact]
    public void AddVisitor_DefaultPriority_RunsAfterIncludesAndBeforeFieldResolution()
    {
        // Arrange
        var recorder = new FieldRecordingVisitor();
        var configuration = new TestQueryParserConfiguration
        {
            Includes = new Dictionary<string, string> { ["saved"] = "inner:1" },
            FieldMap = new FieldMap { { "inner", "resolved.inner" }, { "outer", "resolved.outer" } }
        };
        configuration.AddVisitor(recorder);
        var parser = new TestQueryParser(configuration);

        // Act
        parser.Process("outer:1 @include:saved");

        // Assert
        Assert.Equal(["outer", "inner"], recorder.Fields);
    }

    [Fact]
    public void AddVisitorAfter_FieldResolver_SeesResolvedFields()
    {
        // Arrange
        var recorder = new FieldRecordingVisitor();
        var configuration = new TestQueryParserConfiguration { FieldMap = new FieldMap { { "a", "b" } } };
        configuration.AddVisitorAfter<FieldResolverQueryVisitor>(recorder);
        var parser = new TestQueryParser(configuration);

        // Act
        parser.Process("a:1");

        // Assert
        Assert.Equal(["b"], recorder.Fields);
    }

    [Fact]
    public void RemoveVisitor_Validation_SkipsValidation()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var configuration = new TestQueryParserConfiguration { ValidationOptions = options };
        configuration.RemoveVisitor<ValidationVisitor>();
        var parser = new TestQueryParser(configuration);

        // Act
        var result = parser.Process("secret:1");

        // Assert
        Assert.Equal("secret:1", result.ToDebugString());
    }

    [Fact]
    public void ReplaceVisitor_FieldResolver_UsesReplacement()
    {
        var configuration = new TestQueryParserConfiguration();
        configuration.ReplaceVisitor<FieldResolverQueryVisitor>(new PrefixFieldVisitor("x."));
        configuration.AddVisitorBefore<PrefixFieldVisitor>(new PrefixFieldVisitor("y."));
        var parser = new TestQueryParser(configuration);

        var result = parser.Process("a:1");

        Assert.Equal("x.y.a:1", result.ToDebugString());
    }

    [Fact]
    public void ValidateQuery_InvalidQuery_ReturnsErrorsWithoutThrowing()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("title");
        var parser = new TestQueryParser(new TestQueryParserConfiguration { ValidationOptions = options });

        // Act
        var result = parser.Validate("title:x other:y", parser.CreateContext());
        var syntax = parser.Validate("title:(x", parser.CreateContext());

        // Assert
        Assert.Equal(QueryErrorCode.FieldNotAllowed, Assert.Single(result.ValidationErrors).Code);
        Assert.False(syntax.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldThrow_Throws()
    {
        var options = new QueryValidationOptions { ShouldThrow = true };
        options.RestrictedFields.Add("secret");
        var parser = new TestQueryParser(new TestQueryParserConfiguration { ValidationOptions = options });

        Assert.Throws<QueryValidationException>(() => parser.Validate("secret:1", parser.CreateContext()));
    }

    [Fact]
    public void SyncMethods_AsyncResolversNotResolved_ThrowInvalidOperationException()
    {
        // Arrange
        var includeParser = new TestQueryParser(new TestQueryParserConfiguration { IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("a:1") });
        var fieldParser = new TestQueryParser(new TestQueryParserConfiguration { AsyncFieldResolver = (f, _, _) => ValueTask.FromResult<string?>(f) });
        var providerParser = new TestQueryParser(new TestQueryParserConfiguration()) { RequireResolution = true };

        // Act & Assert
        foreach (var parser in new[] { includeParser, fieldParser, providerParser })
        {
            Assert.Throws<InvalidOperationException>(() => parser.Process("a:1", parser.CreateContext()));
            Assert.Throws<InvalidOperationException>(() => parser.Sort("a", parser.CreateContext()));
            Assert.Throws<InvalidOperationException>(() => parser.Aggregations("terms:a", parser.CreateContext()));
            Assert.Throws<InvalidOperationException>(() => parser.Validate("a:1", parser.CreateContext()));
        }
    }

    [Fact]
    public async Task ProcessQueryAsync_IncludeResolver_ResolvesNestedIncludesOncePerName()
    {
        // Arrange
        var calls = new ConcurrentBag<string>();
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["outer"] = "@include:inner OR @include:INNER x:1",
            ["inner"] = "@include:leaf y:2",
            ["leaf"] = "z:3"
        };
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = async (name, _, ct) =>
            {
                await Task.Yield();
                calls.Add(name.ToLowerInvariant());
                return stored.GetValueOrDefault(name);
            }
        });
        var context = parser.CreateContext();

        // Act
        var result = await parser.ProcessAsync("@include:outer @include:Outer", context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["inner", "leaf", "outer"], calls.Order());
        Assert.True(context.IsResolved);
        Assert.True(context.ValidationResult.IsValid, context.ValidationResult.Message);
        Assert.Equal(["x", "y", "z"], result.GetReferencedFields().Order());
    }

    [Fact]
    public async Task ProcessQueryAsync_ConfiguredIncludes_AreNotFetchedOrModified()
    {
        // Arrange
        var includes = new Dictionary<string, string> { ["known"] = "@include:remote a:1" };
        var calls = new ConcurrentBag<string>();
        var configuration = new TestQueryParserConfiguration
        {
            Includes = includes,
            IncludeResolver = (name, _, _) =>
            {
                calls.Add(name);
                return ValueTask.FromResult<string?>("b:2");
            }
        };
        var parser = new TestQueryParser(configuration);

        // Act
        var result = await parser.ProcessAsync("@include:known", parser.CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["remote"], calls);
        Assert.Single(includes);
        Assert.Same(includes, configuration.Includes);
        Assert.Equal(["a", "b"], result.GetReferencedFields().Order());
    }

    [Fact]
    public async Task ProcessQueryAsync_IncludeResolverReturnsNull_ThrowsUnresolvedInclude()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration { IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>(null) });

        var exception = await Assert.ThrowsAsync<QueryValidationException>(async () => await parser.ProcessAsync("@include:missing", parser.CreateContext(), TestContext.Current.CancellationToken));

        Assert.Contains("missing", exception.Result.UnresolvedIncludes);
    }

    [Fact]
    public async Task ProcessQueryAsync_IncludeResolverThrows_ThrowsValidationExceptionWithResolverError()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = async (name, _, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException($"store unavailable for {name}");
            }
        });

        // Act
        var exception = await Assert.ThrowsAsync<QueryValidationException>(async () => await parser.ProcessAsync("@include:a", parser.CreateContext(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(exception.Errors, e => e.Message.Contains("store unavailable for a"));
    }

    [Fact]
    public async Task ProcessQueryAsync_ManyResolversFailConcurrently_RecordsEveryError()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            AsyncFieldResolver = async (field, _, _) =>
            {
                await Task.Delay(1);
                throw new InvalidOperationException($"failed {field}");
            }
        });
        string query = string.Join(" ", Enumerable.Range(0, 300).Select(i => $"f{i}:x"));

        // Act
        var exception = await Assert.ThrowsAsync<QueryValidationException>(async () => await parser.ProcessAsync(query, parser.CreateContext(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(300, exception.Errors.Count(e => e.Message.StartsWith("Error in field resolver callback")));
    }

    [Fact]
    public async Task ProcessQueryAsync_RecursiveIncludesFromResolver_ReportsRecursion()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = (name, _, _) => ValueTask.FromResult<string?>(name == "a" ? "x:1 @include:b" : "y:1 @include:a")
        });

        var exception = await Assert.ThrowsAsync<QueryValidationException>(async () => await parser.ProcessAsync("@include:a", parser.CreateContext(), TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, e => e.Message.Contains("Recursive"));
    }

    [Fact]
    public async Task ProcessQueryAsync_IncludeResolverFanOutBomb_IsBounded()
    {
        // Arrange
        int calls = 0;
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = (name, _, _) =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult<string?>(string.Join(" OR ", Enumerable.Range(0, 20).Select(i => $"@include:{name}_{i}")));
            }
        });

        // Act
        var exception = await Assert.ThrowsAsync<QueryValidationException>(async () => await parser.ProcessAsync("@include:root", parser.CreateContext(), TestContext.Current.CancellationToken));

        // Assert
        Assert.InRange(calls, 1, 100);
        Assert.Contains(exception.Errors, e => e.Code == QueryErrorCode.MaxDepthExceeded || e.Code == QueryErrorCode.UnresolvedInclude);
    }

    [Fact]
    public async Task ProcessQueryAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await parser.ProcessAsync("@include:a", parser.CreateContext(), cancellation.Token));
    }

    [Fact]
    public async Task ProcessQueryAsync_AsyncFieldResolver_ResolvesBeforeSyncResolverAndFieldMap()
    {
        // Arrange
        var calls = new ConcurrentBag<string>();
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            Includes = new Dictionary<string, string> { ["saved"] = "included:1" },
            AsyncFieldResolver = async (field, _, _) =>
            {
                await Task.Yield();
                calls.Add(field);
                return field is "a" or "included" ? $"async.{field}" : null;
            },
            FieldResolver = (field, _) => field is "a" or "b" ? $"sync.{field}" : null,
            FieldMap = new FieldMap { { "c", "map.c" } }
        });
        var context = parser.CreateContext();

        // Act
        var result = await parser.ProcessAsync("a:1 b:2 c:3 A:4 @include:saved", context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("(bool +async.a:1 +sync.b:2 +map.c:3 +async.a:4 +(group async.included:1))", result.ToDebugString());
        Assert.Equal(["a", "b", "c", "included"], calls.Select(c => c.ToLowerInvariant()).Order());
        Assert.Empty(context.ValidationResult.UnresolvedFields);
    }

    [Fact]
    public async Task ProcessQueryAsync_OnResolveAsync_ReceivesResolvedFieldNames()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            FieldResolver = (field, context) => $"{context.GetValue<string>("tenant")}.{field}"
        });
        var context = parser.CreateContext();
        context.SetValue("tenant", "t1");

        // Act
        var result = await parser.ProcessAsync("a:1 _exists_:b @custom:x", context, TestContext.Current.CancellationToken);

        // Assert
        var (type, fields) = Assert.Single(parser.Resolutions);
        Assert.Equal(QueryType.Query, type);
        Assert.Equal(["t1.a", "t1.b"], fields.Order());
        Assert.Equal(["t1.a", "t1.b"], result.GetReferencedFields().Order());
    }

    [Fact]
    public async Task ValidateQueryAsync_IncludeResolver_ResolvesIncludesBeforeValidating()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            ValidationOptions = options,
            IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("secret:1")
        });

        // Act
        var result = await parser.ValidateAsync("@include:sneaky", parser.CreateContext(), TestContext.Current.CancellationToken);
        var syntax = await parser.ValidateAsync("a:(b", parser.CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryErrorCode.FieldRestricted, Assert.Single(result.ValidationErrors).Code);
        Assert.False(syntax.IsValid);
    }

    [Fact]
    public void ProcessSort_ResolvesAndValidatesFields()
    {
        // Arrange
        var options = new QueryValidationOptions { AllowedMaxSortFields = 3 };
        options.RestrictedFields.Add("secret");
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            ValidationOptions = options,
            FieldMap = new FieldMap { { "created", "meta.created" }, { "hidden", "secret" } },
            Includes = new Dictionary<string, string> { ["default"] = "-created name" }
        });
        var context = parser.CreateContext();

        // Act
        var fields = parser.Sort("@include:default +id", context);

        // Assert
        Assert.Equal("-meta.created name id", string.Join(" ", fields));
        Assert.Equal("created", fields[0].OriginalField);
        Assert.Equal(QueryType.Sort, context.ValidationResult.QueryType);
        Assert.Throws<QueryValidationException>(() => parser.Sort("hidden", parser.CreateContext()));
        Assert.Throws<QueryValidationException>(() => parser.Sort("a b c d", parser.CreateContext()));
        Assert.Throws<QueryValidationException>(() => parser.Sort("NOT a", parser.CreateContext()));
        Assert.Throws<QueryValidationException>(() => parser.Sort("(a", parser.CreateContext()));
    }

    [Fact]
    public async Task ProcessSortAsync_AsyncResolvers_ResolveFieldsAndCallHook()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("-created"),
            AsyncFieldResolver = (field, _, _) => ValueTask.FromResult<string?>($"async.{field}")
        });

        // Act
        var fields = await parser.SortAsync("@include:default name", parser.CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("-async.created async.name", string.Join(" ", fields));
        var (type, resolved) = Assert.Single(parser.Resolutions);
        Assert.Equal(QueryType.Sort, type);
        Assert.Equal(["async.created", "async.name"], resolved.Order());
    }

    [Fact]
    public void ProcessAggregations_ResolvesAndValidatesFields()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add(AggregationTypes.Terms);
        options.AllowedOperations.Add(AggregationTypes.Min);
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            ValidationOptions = options,
            FieldMap = new FieldMap { { "created", "meta.created" } }
        });
        var context = parser.CreateContext();

        // Act
        var aggregations = parser.Aggregations("terms:(status min:created)", context);

        // Assert
        var terms = Assert.Single(aggregations);
        Assert.Equal("meta.created", terms.Aggregations[0].Field);
        Assert.Equal("min_created", terms.Aggregations[0].Name);
        Assert.Equal(QueryType.Aggregation, context.ValidationResult.QueryType);
        Assert.Throws<QueryValidationException>(() => parser.Aggregations("max:x", parser.CreateContext()));
        Assert.Throws<QueryValidationException>(() => parser.Aggregations("bogus:x", parser.CreateContext()));
    }

    [Fact]
    public void ProcessAggregations_IncludeModifier_IsNotExpandedAsQueryInclude()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration());

        var aggregation = Assert.Single(parser.Aggregations("terms:(status @include:active)", parser.CreateContext()));

        Assert.Equal("active", aggregation.GetModifier("include")!.Value);
    }

    [Fact]
    public async Task ProcessAggregationsAsync_AsyncFieldResolver_ResolvesFieldsAndCallsHook()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            AsyncFieldResolver = (field, _, _) => ValueTask.FromResult<string?>($"async.{field}")
        });

        // Act
        var aggregations = await parser.AggregationsAsync("terms:(status min:created) tophits:_", parser.CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("async.status", aggregations[0].Field);
        Assert.Equal("async.created", aggregations[0].Aggregations[0].Field);
        Assert.Equal("_", aggregations[1].Field);
        var (type, resolved) = Assert.Single(parser.Resolutions);
        Assert.Equal(QueryType.Aggregation, type);
        Assert.Equal(["async.created", "async.status"], resolved.Order());
    }

    [Fact]
    public async Task ProcessQueryAsync_ProviderRequiresResolution_SucceedsAfterResolution()
    {
        var parser = new TestQueryParser(new TestQueryParserConfiguration()) { RequireResolution = true };

        var result = await parser.ProcessAsync("a:1", parser.CreateContext(), TestContext.Current.CancellationToken);

        Assert.Equal("a:1", result.ToDebugString());
    }

    [Fact]
    public void ProcessQuery_SharedParserConcurrently_ResultsAreIndependent()
    {
        // Arrange
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("secret");
        var configuration = new TestQueryParserConfiguration
        {
            ValidationOptions = options,
            FieldMap = new FieldMap { { "alias", "secret" }, { "user", "account.user" } },
            Includes = new Dictionary<string, string> { ["bad"] = "secret:1", ["good"] = "user:x" },
            TimeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
        };
        configuration.AddVisitor(new DateMathEvaluatorVisitor(), 20);
        var parser = new TestQueryParser(configuration);
        var tenantOptions = new TestQueryOptions { FieldMap = new FieldMap { { "user", "tenant.user" } } };
        var failures = new ConcurrentBag<string>();

        // Act
        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                switch (i % 4)
                {
                    case 0:
                        Assert.Throws<QueryValidationException>(() => parser.Process("@include:bad", parser.CreateContext()));
                        break;
                    case 1:
                        Assert.Throws<QueryValidationException>(() => parser.Process($"alias:{i}", parser.CreateContext()));
                        break;
                    case 2:
                        Assert.Equal($"(bool +account.user:{i} +(group account.user:x) +d:2024-01-01T00:00:00.000+00:00)", parser.Process($"user:{i} @include:good d:now/d", parser.CreateContext()).ToDebugString());
                        break;
                    default:
                        Assert.Equal($"tenant.user:{i}", parser.Process($"user:{i}", parser.CreateContext(tenantOptions)).ToDebugString());
                        break;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{i}: {ex.Message}");
            }
        });

        // Assert
        Assert.Empty(failures);
        Assert.Single(options.RestrictedFields);
        Assert.Equal(2, configuration.Includes!.Count);
    }

    [Fact]
    public async Task ProcessQueryAsync_SharedParserConcurrently_ResultsAreIndependent()
    {
        // Arrange
        var parser = new TestQueryParser(new TestQueryParserConfiguration
        {
            IncludeResolver = async (name, _, ct) =>
            {
                await Task.Yield();
                return $"{name}_field:1";
            },
            AsyncFieldResolver = async (field, _, ct) =>
            {
                await Task.Yield();
                return $"async.{field}";
            }
        });

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            var document = await parser.ProcessAsync($"@include:i{i} f{i}:x", parser.CreateContext(), TestContext.Current.CancellationToken);
            return (i, document.ToDebugString());
        }));

        // Assert
        Assert.All(results, r => Assert.Equal($"(bool +(group async.i{r.i}_field:1) +async.f{r.i}:x)", r.Item2));
    }

    private static TestQueryParserConfiguration CreateConfiguration() => new()
    {
        ParserOptions = new LuceneParserOptions { MaxDepth = 200 },
        DefaultFields = ["config"],
        FieldMap = new FieldMap { { "c", "config" } },
        FieldResolver = (_, _) => "config",
        AsyncFieldResolver = (_, _, _) => ValueTask.FromResult<string?>("config"),
        Includes = new Dictionary<string, string> { ["c"] = "config:1" },
        IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("config:1"),
        ShouldSkipInclude = (_, _) => false,
        ValidationOptions = new QueryValidationOptions(),
        TimeProvider = new FakeTimeProvider()
    };

    private static TestQueryOptions CreateOptions(BooleanOperator defaultOperator) => new()
    {
        DefaultOperator = defaultOperator,
        DefaultFields = ["options"],
        FieldMap = new FieldMap { { "o", "options" } },
        FieldResolver = (_, _) => "options",
        AsyncFieldResolver = (_, _, _) => ValueTask.FromResult<string?>("options"),
        Includes = new Dictionary<string, string> { ["o"] = "options:1" },
        IncludeResolver = (_, _, _) => ValueTask.FromResult<string?>("options:1"),
        ValidationOptions = new QueryValidationOptions()
    };

    private static void AssertUsesOptions(TestQueryOptions options, QueryVisitorContext context)
    {
        Assert.Equal(options.DefaultOperator, context.ParserOptions.DefaultOperator);
        Assert.Equal(200, context.ParserOptions.MaxDepth);
        Assert.Same(options.DefaultFields, context.DefaultFields);
        Assert.Same(options.FieldMap, context.FieldMap);
        Assert.Same(options.FieldResolver, context.FieldResolver);
        Assert.Same(options.AsyncFieldResolver, context.AsyncFieldResolver);
        Assert.Same(options.Includes, context.Includes);
        Assert.Same(options.IncludeResolver, context.IncludeResolver);
        Assert.Same(options.ValidationOptions, context.ValidationOptions);
    }

    private sealed class TestQueryParserConfiguration : QueryParserConfiguration;

    private sealed record TestQueryOptions : QueryOptionsBase;

    private sealed class TestQueryParser(QueryParserConfiguration configuration) : QueryParserBase<QueryVisitorContext>(configuration)
    {
        public ConcurrentQueue<(QueryType Type, IReadOnlyCollection<string> Fields)> Resolutions { get; } = new();

        public bool RequireResolution { get; init; }

        public QueryVisitorContext CreateContext(QueryOptionsBase? registered = null, QueryOptionsBase? options = null)
        {
            var context = new QueryVisitorContext();
            ApplyOptions(context, registered, options);
            return context;
        }

        public QueryDocument Process(string query, QueryVisitorContext? context = null)
        {
            context ??= CreateContext();
            return ProcessQuery(ParseQuery(query, context), context, clone: false);
        }

        public QueryDocument Process(QueryDocument document, QueryVisitorContext context) => ProcessQuery(document, context, clone: true);

        public async ValueTask<QueryDocument> ProcessAsync(string query, QueryVisitorContext context, CancellationToken cancellationToken)
        {
            var document = ParseQuery(query, context);
            await ResolveQueryAsync(document, context, cancellationToken);
            return ProcessQuery(document, context, clone: false);
        }

        public QueryValidationResult Validate(string query, QueryVisitorContext context) => ValidateQuery(query, context);

        public ValueTask<QueryValidationResult> ValidateAsync(string query, QueryVisitorContext context, CancellationToken cancellationToken) => ValidateQueryAsync(query, context, cancellationToken);

        public List<SortField> Sort(string sort, QueryVisitorContext context) => ProcessSort(sort, context);

        public ValueTask<List<SortField>> SortAsync(string sort, QueryVisitorContext context, CancellationToken cancellationToken) => ProcessSortAsync(sort, context, cancellationToken);

        public List<AggregationExpression> Aggregations(string aggregations, QueryVisitorContext context) => ProcessAggregations(aggregations, context);

        public ValueTask<List<AggregationExpression>> AggregationsAsync(string aggregations, QueryVisitorContext context, CancellationToken cancellationToken) => ProcessAggregationsAsync(aggregations, context, cancellationToken);

        protected override ValueTask OnResolveAsync(QueryType type, QueryDocument document, IReadOnlyDictionary<string, string> fields, QueryVisitorContext context, CancellationToken cancellationToken)
        {
            Resolutions.Enqueue((type, fields.Values.ToList()));
            return base.OnResolveAsync(type, document, fields, context, cancellationToken);
        }

        protected override bool RequiresResolution(QueryVisitorContext context) => RequireResolution || base.RequiresResolution(context);
    }

    private sealed class UnwrapDocumentVisitor : QueryVisitor
    {
        public override QueryNode Accept(QueryNode node, IQueryVisitorContext context) => node is QueryDocument { Query: { } query } ? query : node;
    }

    private sealed class FieldRecordingVisitor : QueryVisitor
    {
        public List<string> Fields { get; } = [];

        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            Fields.Add(node.Field);
            return base.Visit(node, context);
        }
    }

    private sealed class PrefixFieldVisitor(string prefix) : QueryVisitor
    {
        protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
        {
            node.Field = prefix + node.Field;
            return base.Visit(node, context);
        }
    }
}
