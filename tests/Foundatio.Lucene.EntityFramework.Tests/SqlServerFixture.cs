using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// xUnit fixture that manages a SQL Server container with full-text search support for integration tests.
/// The container is started once and shared across all tests in the collection.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container;
    private IServiceProvider? _serviceProvider;
    private IServiceProvider? _fullTextServiceProvider;

    public SqlServerFixture()
    {
        _container = new MsSqlBuilder()
            .WithImage("concordservicing/sqlserver-fts:2022-latest")
            .WithPassword("P@ssword1!")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString() + ";Initial Catalog=foundatio_lucene;Encrypt=False";

    public IServiceProvider ServiceProvider => _serviceProvider
        ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public IServiceProvider FullTextServiceProvider => _fullTextServiceProvider
        ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public EntityFrameworkQueryParser Parser => ServiceProvider.GetRequiredService<EntityFrameworkQueryParser>();

    public EntityFrameworkQueryParser FullTextParser => FullTextServiceProvider.GetRequiredService<EntityFrameworkQueryParser>();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        _serviceProvider = BuildServiceProvider();
        _fullTextServiceProvider = BuildFullTextServiceProvider();

        await SeedDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
        if (_fullTextServiceProvider is IDisposable ftDisposable)
            ftDisposable.Dispose();

        return _container.DisposeAsync();
    }

    public SampleContext CreateSampleContext()
    {
        return ServiceProvider.GetRequiredService<SampleContext>();
    }

    public SampleContext CreateFullTextSampleContext()
    {
        return FullTextServiceProvider.GetRequiredService<SampleContext>();
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace));

        services.AddDbContext<SampleContext>((_, options) =>
        {
            options.UseSqlServer(ConnectionString).AddLuceneQuery(c =>
            {
                c.UseEntityTypePropertyFilter(p => p.Name != nameof(Company.Location));
            });
        }, ServiceLifetime.Transient, ServiceLifetime.Singleton);

        var parser = new EntityFrameworkQueryParser(config =>
        {
            config.UseEntityTypePropertyFilter(p => p.Name != nameof(Company.Location));
        });
        services.AddSingleton(parser);

        return services.BuildServiceProvider();
    }

    private IServiceProvider BuildFullTextServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace));

        services.AddDbContext<SampleContext>((_, options) =>
        {
            options.UseSqlServer(ConnectionString).AddLuceneQuery(c =>
            {
                c.UseEntityTypePropertyFilter(p => p.Name != nameof(Company.Location));
                c.AddFullTextFields("Employee.Name", "Employee.Title", "Company.Name");
            });
        }, ServiceLifetime.Transient, ServiceLifetime.Singleton);

        var parser = new EntityFrameworkQueryParser(config =>
        {
            config.UseEntityTypePropertyFilter(p => p.Name != nameof(Company.Location));
            config.AddFullTextFields("Employee.Name", "Employee.Title", "Company.Name");
        });
        services.AddSingleton(parser);

        return services.BuildServiceProvider();
    }

    private async Task SeedDataAsync()
    {
        await using var db = CreateSampleContext();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var company1 = new Company
        {
            Name = "Acme Corp",
            Location = "New York",
            FoundedYear = 2000,
            IsPublic = true
        };

        var company2 = new Company
        {
            Name = "Tech Solutions",
            Location = "San Francisco",
            FoundedYear = 2015,
            IsPublic = false
        };

        db.Companies.AddRange(company1, company2);
        await db.SaveChangesAsync();

        var dept1 = new Department { Name = "Engineering", Budget = 1000000, CompanyId = company1.Id };
        var dept2 = new Department { Name = "Sales", Budget = 500000, CompanyId = company1.Id };
        var dept3 = new Department { Name = "Research", Budget = 750000, CompanyId = company2.Id };

        db.Departments.AddRange(dept1, dept2, dept3);
        await db.SaveChangesAsync();

        var employees = new[]
        {
            new Employee
            {
                Name = "John Doe", Email = "john@acme.com", Title = "Software Developer",
                Salary = 80000, Age = 30, HireDate = new DateTime(2020, 1, 15), IsActive = true,
                CompanyId = company1.Id, DepartmentId = dept1.Id
            },
            new Employee
            {
                Name = "Jane Smith", Email = "jane@acme.com", Title = "Project Manager",
                Salary = 95000, Age = 35, HireDate = new DateTime(2019, 6, 1), IsActive = true,
                CompanyId = company1.Id, DepartmentId = dept2.Id
            },
            new Employee
            {
                Name = "Bob Wilson", Email = "bob@tech.com", Title = "Senior Developer",
                Salary = 110000, Age = 40, HireDate = new DateTime(2018, 3, 20), IsActive = true,
                CompanyId = company2.Id, DepartmentId = dept3.Id
            },
            new Employee
            {
                Name = "Alice Brown", Email = "alice@acme.com", Title = "Junior Developer",
                Salary = 55000, Age = 25, HireDate = new DateTime(2022, 9, 1), IsActive = false,
                CompanyId = company1.Id, DepartmentId = dept1.Id
            }
        };

        db.Employees.AddRange(employees);
        await db.SaveChangesAsync();

        // Create full-text catalog and indexes
        await CreateFullTextIndexesAsync(db);
    }

    private async Task CreateFullTextIndexesAsync(DbContext db)
    {
        // Create full-text catalog
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'FTCatalog')
            BEGIN
                CREATE FULLTEXT CATALOG FTCatalog AS DEFAULT;
            END");

        // Create full-text index on Employees table (Name, Title columns)
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Employees'))
            BEGIN
                CREATE FULLTEXT INDEX ON Employees(Name, Title)
                KEY INDEX PK_Employees ON FTCatalog
                WITH CHANGE_TRACKING AUTO;
            END");

        // Create full-text index on Companies table (Name column)
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Companies'))
            BEGIN
                CREATE FULLTEXT INDEX ON Companies(Name)
                KEY INDEX PK_Companies ON FTCatalog
                WITH CHANGE_TRACKING AUTO;
            END");

        // Wait for full-text indexes to populate
        await SqlWaiter.WaitForFullTextIndexAsync(db, "Employees");
        await SqlWaiter.WaitForFullTextIndexAsync(db, "Companies");
    }
}

/// <summary>
/// Helper class for waiting on SQL Server operations like full-text index population.
/// </summary>
public static class SqlWaiter
{
    /// <summary>
    /// Waits for a full-text index to finish populating.
    /// </summary>
    /// <param name="context">The DbContext to use for querying.</param>
    /// <param name="tableName">The name of the table with the full-text index.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WaitForFullTextIndexAsync(
        DbContext context,
        string tableName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT OBJECTPROPERTYEX(OBJECT_ID('{tableName}'), 'TableFullTextPopulateStatus')";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var status = result == DBNull.Value ? -1 : Convert.ToInt32(result);

            // 0 = Idle (population complete), 1 = Full population in progress, 2 = Incremental population
            if (status == 0)
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"Full-text index on table '{tableName}' did not finish populating within {timeout}.");
    }
}

/// <summary>
/// Collection definition for SQL Server integration tests.
/// Tests in this collection share a single SQL Server container instance.
/// </summary>
[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
