using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Extension methods that apply Lucene queries and sort expressions to EF Core queries.
/// </summary>
public static class EntityFrameworkExtensions
{
    private static readonly ConditionalWeakTable<Action<EntityFrameworkQueryParserConfiguration>, EntityFrameworkQueryParser> ConfiguredParsers = new();

    /// <summary>
    /// Filters <paramref name="source"/> with a Lucene query. The entity's fields are discovered from the model of the
    /// query's <c>DbSet</c>. A null or blank query returns <paramref name="source"/> unchanged.
    /// </summary>
    /// <exception cref="QueryParseException">The query has syntax errors.</exception>
    /// <exception cref="QueryValidationException">The query is invalid.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, string? query, EntityFrameworkQueryParser parser, EntityFrameworkQueryOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parser);
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var filter = (Expression<Func<T, bool>>)parser.BuildFilter(GetEntityType(source, parser, options), query, options);
        return source.Where(filter);
    }

    /// <summary>
    /// Filters <paramref name="source"/> with a Lucene query using the parser registered with
    /// <see cref="AddLuceneQuery(DbContextOptionsBuilder, EntityFrameworkQueryParser)"/>. A null or blank query
    /// returns <paramref name="source"/> unchanged.
    /// </summary>
    /// <exception cref="InvalidOperationException">No parser is registered for the context.</exception>
    /// <inheritdoc cref="Where{T}(IQueryable{T}, string?, EntityFrameworkQueryParser, EntityFrameworkQueryOptions?)"/>
    public static IQueryable<T> Where<T>(this DbSet<T> source, string? query, EntityFrameworkQueryOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(query))
            return source;

        return source.Where(query, GetRequiredQueryParser(source), options);
    }

    /// <summary>
    /// Filters <paramref name="source"/> with a Lucene query, first running the parser's asynchronous resolution phase
    /// (include and field resolvers).
    /// </summary>
    /// <inheritdoc cref="Where{T}(IQueryable{T}, string?, EntityFrameworkQueryParser, EntityFrameworkQueryOptions?)"/>
    public static async ValueTask<IQueryable<T>> WhereAsync<T>(this IQueryable<T> source, string? query, EntityFrameworkQueryParser parser, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parser);
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var filter = await parser.BuildFilterAsync(GetEntityType(source, parser, options), query, options, cancellationToken).ConfigureAwait(false);
        return source.Where((Expression<Func<T, bool>>)filter);
    }

    /// <summary>
    /// Orders <paramref name="source"/> by a sort expression such as <c>-created +name</c>.
    /// </summary>
    /// <exception cref="QueryValidationException">The sort expression is empty or invalid.</exception>
    public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, string sort, EntityFrameworkQueryParser parser, EntityFrameworkQueryOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(parser);

        return parser.BuildSort<T>(GetEntityType(source, parser, options), sort, options).Apply(source);
    }

    /// <summary>
    /// Gets the parser registered for the context with <c>AddLuceneQuery</c>, or null.
    /// </summary>
    public static EntityFrameworkQueryParser? GetQueryParser(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetService<IDbContextOptions>().FindExtension<LuceneQueryOptionsExtension>()?.Parser;
    }

    /// <summary>
    /// Associates <paramref name="parser"/> with the contexts built from these options, for use by
    /// <see cref="Where{T}(DbSet{T}, string?, EntityFrameworkQueryOptions?)"/> and <see cref="GetQueryParser"/>. Share one
    /// parser instance across contexts so field metadata is discovered once.
    /// </summary>
    public static DbContextOptionsBuilder AddLuceneQuery(this DbContextOptionsBuilder optionsBuilder, EntityFrameworkQueryParser parser)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(parser);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new LuceneQueryOptionsExtension(parser));
        return optionsBuilder;
    }

    /// <summary>
    /// Associates a parser created with <paramref name="configure"/> with the contexts built from these options. The
    /// parser is reused whenever the same delegate instance is passed again (as it is for lambdas that capture
    /// nothing); otherwise prefer <see cref="AddLuceneQuery(DbContextOptionsBuilder, EntityFrameworkQueryParser)"/>
    /// with a shared parser.
    /// </summary>
    public static DbContextOptionsBuilder AddLuceneQuery(this DbContextOptionsBuilder optionsBuilder, Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        var parser = configure is null
            ? new EntityFrameworkQueryParser()
            : ConfiguredParsers.GetValue(configure, static c => new EntityFrameworkQueryParser(c));

        return optionsBuilder.AddLuceneQuery(parser);
    }

    /// <inheritdoc cref="AddLuceneQuery(DbContextOptionsBuilder, EntityFrameworkQueryParser)"/>
    public static DbContextOptionsBuilder<TContext> AddLuceneQuery<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, EntityFrameworkQueryParser parser) where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).AddLuceneQuery(parser);
        return optionsBuilder;
    }

    /// <inheritdoc cref="AddLuceneQuery(DbContextOptionsBuilder, Action{EntityFrameworkQueryParserConfiguration}?)"/>
    public static DbContextOptionsBuilder<TContext> AddLuceneQuery<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<EntityFrameworkQueryParserConfiguration>? configure = null) where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).AddLuceneQuery(configure);
        return optionsBuilder;
    }

    private static EntityFrameworkQueryParser GetRequiredQueryParser<T>(DbSet<T> source) where T : class
    {
        return source.GetService<IDbContextOptions>().FindExtension<LuceneQueryOptionsExtension>()?.Parser
            ?? throw new InvalidOperationException("No EntityFrameworkQueryParser is registered for the DbContext. Call AddLuceneQuery() when configuring the DbContext options, or pass a parser.");
    }

    private static IEntityType GetEntityType<T>(IQueryable<T> source, EntityFrameworkQueryParser parser, EntityFrameworkQueryOptions? options)
    {
        if (options?.Model is null && FindQueryRoot(source.Expression) is { } root)
        {
            if (root.EntityType.ClrType == typeof(T))
                return root.EntityType;

            if (root.EntityType.Model.FindEntityType(typeof(T)) is { } entityType)
                return entityType;
        }

        return parser.GetEntityType(typeof(T), options);
    }

    private static EntityQueryRootExpression? FindQueryRoot(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case EntityQueryRootExpression root:
                    return root;
                case MethodCallExpression { Arguments.Count: > 0 } call:
                    expression = call.Arguments[0];
                    break;
                default:
                    return null;
            }
        }
    }
}
