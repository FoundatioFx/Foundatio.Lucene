using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.LuceneQueryParser.EntityFramework;

/// <summary>
/// Extension methods for Entity Framework integration with Lucene query parsing.
/// </summary>
public static class EntityFrameworkExtensions
{
    /// <summary>
    /// Filters the query using a Lucene query string, retrieving the parser from the DbContext's service provider.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The DbSet source.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <returns>The filtered queryable.</returns>
    /// <exception cref="InvalidOperationException">Thrown when EntityFrameworkQueryParser is not registered in the DbContext.</exception>
    public static IQueryable<T> Where<T>(this DbSet<T> source, string query) where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var context = source.GetDbContext();
        var parser = context.GetQueryParser();
        
        if (parser == null)
            throw new InvalidOperationException(
                $"EntityFrameworkQueryParser is not registered in the DbContext. " +
                $"Use AddLuceneQueryParser() when configuring the DbContext options.");

        var filter = parser.BuildFilter<T>(query);
        return source.Where(filter);
    }

    /// <summary>
    /// Filters the query using a Lucene query string.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="parser">The query parser to use.</param>
    /// <returns>The filtered queryable.</returns>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, string query, EntityFrameworkQueryParser parser) where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var filter = parser.BuildFilter<T>(query);
        return source.Where(filter);
    }

    /// <summary>
    /// Filters the query using a Lucene query string with a custom context.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="parser">The query parser to use.</param>
    /// <param name="context">The query visitor context.</param>
    /// <returns>The filtered queryable.</returns>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, string query, EntityFrameworkQueryParser parser, EntityFrameworkQueryVisitorContext context) where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var filter = parser.BuildFilter<T>(query, context);
        return source.Where(filter);
    }

    /// <summary>
    /// Filters the DbSet using a Lucene query string.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The DbSet source.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="parser">The query parser to use.</param>
    /// <returns>The filtered queryable.</returns>
    public static IQueryable<T> Where<T>(this DbSet<T> source, string query, EntityFrameworkQueryParser parser) where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var filter = parser.BuildFilter<T>(query);
        return source.Where(filter);
    }

    /// <summary>
    /// Filters the DbSet using a Lucene query string with entity type metadata.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The DbSet source.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="parser">The query parser to use.</param>
    /// <param name="context">The DbContext to get entity type metadata from.</param>
    /// <returns>The filtered queryable.</returns>
    public static IQueryable<T> Where<T>(this DbSet<T> source, string query, EntityFrameworkQueryParser parser, DbContext context) where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        var entityType = context.Model.FindEntityType(typeof(T));
        if (entityType == null)
        {
            throw new InvalidOperationException($"Entity type {typeof(T).Name} is not registered in the DbContext model.");
        }

        var filter = parser.BuildFilter<T>(query, entityType);
        return source.Where(filter);
    }

    /// <summary>
    /// Gets the query parser from the DbContext service provider if registered.
    /// </summary>
    /// <param name="context">The DbContext.</param>
    /// <returns>The registered EntityFrameworkQueryParser or null.</returns>
    public static EntityFrameworkQueryParser? GetQueryParser(this DbContext context)
    {
        return context.GetService<EntityFrameworkQueryParser>();
    }

    /// <summary>
    /// Gets a service from the DbContext infrastructure service provider.
    /// </summary>
    private static T? GetService<T>(this DbContext context) where T : class
    {
        return ((IInfrastructure<IServiceProvider>)context).Instance.GetService(typeof(T)) as T;
    }
    
    /// <summary>
    /// Gets the DbContext from a DbSet.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="dbSet">The DbSet.</param>
    /// <returns>The DbContext that owns the DbSet.</returns>
#pragma warning disable EF1001 // Internal EF Core API usage
    private static DbContext GetDbContext<T>(this DbSet<T> dbSet) where T : class
    {
        var infrastructure = dbSet as IInfrastructure<IServiceProvider>;
        var serviceProvider = infrastructure?.Instance 
            ?? throw new InvalidOperationException("Unable to get service provider from DbSet.");
        
        var contextService = serviceProvider.GetService<ICurrentDbContext>()
            ?? throw new InvalidOperationException("Unable to get ICurrentDbContext from service provider.");
            
        return contextService.Context;
    }
#pragma warning restore EF1001 // Internal EF Core API usage

    /// <summary>
    /// Creates a filter expression from a Lucene query using a new parser instance.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="configure">Optional parser configuration.</param>
    /// <returns>A filter expression.</returns>
    public static Expression<Func<T, bool>> ToExpression<T>(string query, Action<EntityFrameworkQueryParserConfiguration>? configure = null) where T : class
    {
        var parser = new EntityFrameworkQueryParser(configure);
        return parser.BuildFilter<T>(query);
    }
    
    /// <summary>
    /// Adds the Lucene query parser to the DbContext options.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <param name="configure">Optional parser configuration.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> AddLuceneQueryParser<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<EntityFrameworkQueryParserConfiguration>? configure = null) where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).AddLuceneQueryParser(configure);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds the Lucene query parser to the DbContext options.
    /// </summary>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <param name="configure">Optional parser configuration.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder AddLuceneQueryParser(
        this DbContextOptionsBuilder optionsBuilder,
        Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        var parser = new EntityFrameworkQueryParser(configure);
        
        var extension = optionsBuilder.Options.FindExtension<LuceneQueryParserOptionsExtension>()
            ?? new LuceneQueryParserOptionsExtension(parser);
        
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        
        return optionsBuilder;
    }
}
