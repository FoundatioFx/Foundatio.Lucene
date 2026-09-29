using Foundatio.Lucene.EntityFramework.Tests.Parity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// A SQL Server container with full-text search, shared by the integration tests. It hosts the sample database and
/// the Foundatio.Parsers sample database, both seeded and full-text indexed.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("concordservicing/sqlserver-fts:2022-latest")
        .WithPassword("P@ssword1!")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using (var db = CreateSampleContext())
        {
            await db.Database.EnsureCreatedAsync();
            SampleData.Seed(db);
            await CreateFullTextIndexAsync(db, "Employees", "Name, Title");
            await CreateFullTextIndexAsync(db, "Companies", "Name");
        }

        await using (var db = CreateParsersContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Seed();
            await CreateFullTextIndexAsync(db, "Employees", "FullName, NationalPhoneNumber, Title");
            await CreateFullTextIndexAsync(db, "Companies", "Name, Location");
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public SampleContext CreateSampleContext()
    {
        return new SampleContext(new DbContextOptionsBuilder<SampleContext>().UseSqlServer(GetConnectionString("foundatio_lucene")).Options);
    }

    public ParsersSampleContext CreateParsersContext()
    {
        return new ParsersSampleContext(new DbContextOptionsBuilder<ParsersSampleContext>().UseSqlServer(GetConnectionString("foundatio_parsers")).Options);
    }

    private string GetConnectionString(string database)
    {
        return new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database, Encrypt = false }.ConnectionString;
    }

    private static async Task CreateFullTextIndexAsync(DbContext db, string table, string columns)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'ftCatalog')
                CREATE FULLTEXT CATALOG ftCatalog AS DEFAULT;
            """);

        string createIndex = $"CREATE FULLTEXT INDEX ON {table} ({columns}) KEY INDEX PK_{table} ON ftCatalog WITH CHANGE_TRACKING AUTO;";
        await db.Database.ExecuteSqlRawAsync(createIndex);
        await WaitForFullTextIndexAsync(db, table);
    }

    private static async Task WaitForFullTextIndexAsync(DbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < timeout)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT OBJECTPROPERTYEX(OBJECT_ID('{table}'), 'TableFullTextPopulateStatus')";
            object? status = await command.ExecuteScalarAsync();
            if (status is int value && value == 0)
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException($"The full-text index on {table} did not finish populating.");
    }
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
