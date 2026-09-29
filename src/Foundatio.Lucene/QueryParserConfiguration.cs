using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Configuration shared by the query parsers. Configure it once when the parser is created; do not change it
/// while queries are being built.
/// </summary>
public abstract class QueryParserConfiguration
{
    /// <summary>
    /// The priority of the built-in <see cref="IncludeVisitor"/>.
    /// </summary>
    public const int IncludeVisitorPriority = 0;

    /// <summary>
    /// The priority of the built-in <see cref="FieldResolverQueryVisitor"/>.
    /// </summary>
    public const int FieldResolverVisitorPriority = 10;

    /// <summary>
    /// The priority of the built-in <see cref="ValidationVisitor"/>.
    /// </summary>
    public const int ValidationVisitorPriority = 30;

    /// <summary>
    /// Creates a configuration with the built-in visitors registered.
    /// </summary>
    protected QueryParserConfiguration()
    {
        QueryVisitor
            .AddVisitor(IncludeVisitor.Instance, IncludeVisitorPriority)
            .AddVisitor(FieldResolverQueryVisitor.Instance, FieldResolverVisitorPriority)
            .AddVisitor(ValidationVisitor.Instance, ValidationVisitorPriority);
    }

    /// <summary>
    /// Options for parsing query text.
    /// </summary>
    public LuceneParserOptions ParserOptions { get; set; } = LuceneParserOptions.Default;

    /// <summary>
    /// The operator used between juxtaposed clauses. Defaults to <see cref="BooleanOperator.And"/>.
    /// </summary>
    public BooleanOperator DefaultOperator
    {
        get => ParserOptions.DefaultOperator;
        set => ParserOptions = ParserOptions with { DefaultOperator = value };
    }

    /// <summary>
    /// Fields searched by terms that have no field.
    /// </summary>
    public string[]? DefaultFields { get; set; }

    /// <summary>
    /// Field aliases.
    /// </summary>
    public FieldMap? FieldMap { get; set; }

    /// <summary>
    /// A synchronous field resolver, applied before <see cref="FieldMap"/>.
    /// </summary>
    public QueryFieldResolver? FieldResolver { get; set; }

    /// <summary>
    /// An asynchronous field resolver. It runs in the resolution phase of the <c>Async</c> build methods and is
    /// applied before <see cref="FieldResolver"/>.
    /// </summary>
    public AsyncQueryFieldResolver? AsyncFieldResolver { get; set; }

    /// <summary>
    /// Query text for <c>@include:name</c> references.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Includes { get; set; }

    /// <summary>
    /// An asynchronous include resolver. It runs in the resolution phase of the <c>Async</c> build methods.
    /// </summary>
    public IncludeResolver? IncludeResolver { get; set; }

    /// <summary>
    /// Decides whether an <c>@include</c> reference should be left unexpanded.
    /// </summary>
    public ShouldSkipIncludeFunc? ShouldSkipInclude { get; set; }

    /// <summary>
    /// Validation rules applied to every query.
    /// </summary>
    public QueryValidationOptions? ValidationOptions { get; set; }

    /// <summary>
    /// The clock used for relative dates such as <c>now-1d</c>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// The visitors run on every query before it is built: the built-in <see cref="IncludeVisitor"/> (priority 0),
    /// <see cref="FieldResolverQueryVisitor"/> (10), and <see cref="ValidationVisitor"/> (30), plus any added visitors.
    /// </summary>
    public ChainedQueryVisitor QueryVisitor { get; } = new();

    /// <summary>
    /// Adds a visitor to the query pipeline. The default priority of 0 runs it after include expansion and before
    /// field resolution, so it sees field names as written.
    /// </summary>
    public void AddVisitor(IQueryVisitor visitor, int priority = 0) => QueryVisitor.AddVisitor(visitor, priority);

    /// <summary>
    /// Adds a visitor to the query pipeline immediately before the visitor of type <typeparamref name="T"/>.
    /// </summary>
    public void AddVisitorBefore<T>(IQueryVisitor visitor) where T : IQueryVisitor => QueryVisitor.AddVisitorBefore<T>(visitor);

    /// <summary>
    /// Adds a visitor to the query pipeline immediately after the visitor of type <typeparamref name="T"/>.
    /// </summary>
    public void AddVisitorAfter<T>(IQueryVisitor visitor) where T : IQueryVisitor => QueryVisitor.AddVisitorAfter<T>(visitor);

    /// <summary>
    /// Removes visitors of type <typeparamref name="T"/> from the query pipeline.
    /// </summary>
    public void RemoveVisitor<T>() where T : IQueryVisitor => QueryVisitor.RemoveVisitor<T>();

    /// <summary>
    /// Replaces visitors of type <typeparamref name="T"/> in the query pipeline.
    /// </summary>
    public void ReplaceVisitor<T>(IQueryVisitor visitor, int? priority = null) where T : IQueryVisitor => QueryVisitor.ReplaceVisitor<T>(visitor, priority);
}
