using System.Linq.Expressions;
using Foundatio.Lucene.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.Tests;

public class MultiTenantEntityFrameworkTests
{
    [Fact]
    public void BuildFilter_WithPerRequestAdditionalFields_MergesWithDiscoveredFields()
    {
        // Arrange
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name");
        });

        // Create per-request field for a tenant-specific custom field
        var customField = new EntityFieldInfo
        {
            Name = "TenantCustomField1",
            FullName = "TenantCustomField1",
            ClrType = typeof(string),
            IsString = true
        };

        var options = new EntityFrameworkQueryOptions
        {
            AdditionalFields = [customField]
        };

        // Act
        var filter = parser.BuildFilter<Employee>("TenantCustomField1:value", null, options);

        // Assert - Should build without error (field was added via options)
        Assert.NotNull(filter);
    }

    [Fact]
    public void BuildFilter_WithPerRequestDefaultFields_OverridesGlobal()
    {
        // Arrange
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name");
        });

        var options = new EntityFrameworkQueryOptions
        {
            DefaultFields = ["Title", "Department"]
        };

        // Act - Query with no field specified should use per-request default fields
        var filter = parser.BuildFilter<Employee>("test", null, options);

        // Assert
        Assert.NotNull(filter);
        var body = filter.Body as BinaryExpression;
        Assert.NotNull(body);
        // Should be OR expression combining Title and Department fields
        Assert.Equal(ExpressionType.OrElse, body.NodeType);
    }

    [Fact]
    public async Task BuildFilter_ConcurrentRequests_ThreadSafe()
    {
        // Arrange - Single parser instance
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name");
        });

        // Simulate 3 tenants with different custom fields
        var tenant1Field = new EntityFieldInfo
        {
            Name = "Tenant1CustomField",
            FullName = "Tenant1CustomField",
            ClrType = typeof(string),
            IsString = true
        };

        var tenant2Field = new EntityFieldInfo
        {
            Name = "Tenant2CustomField",
            FullName = "Tenant2CustomField",
            ClrType = typeof(int),
            IsNumber = true
        };

        var tenant3Field = new EntityFieldInfo
        {
            Name = "Tenant3CustomField",
            FullName = "Tenant3CustomField",
            ClrType = typeof(bool),
            IsBoolean = true
        };

        var tenant1Options = new EntityFrameworkQueryOptions
        {
            AdditionalFields = [tenant1Field],
            DefaultFields = ["Name", "Tenant1CustomField"]
        };

        var tenant2Options = new EntityFrameworkQueryOptions
        {
            AdditionalFields = [tenant2Field],
            DefaultFields = ["Title", "Tenant2CustomField"]
        };

        var tenant3Options = new EntityFrameworkQueryOptions
        {
            AdditionalFields = [tenant3Field],
            DefaultFields = ["Department", "Tenant3CustomField"]
        };

        // Act - Execute concurrent requests (300 total = very high concurrency)
        var tasks = new List<Task<Expression<Func<Employee, bool>>>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => parser.BuildFilter<Employee>("Tenant1CustomField:value1", null, tenant1Options)));
            tasks.Add(Task.Run(() => parser.BuildFilter<Employee>("Tenant2CustomField:123", null, tenant2Options)));
            tasks.Add(Task.Run(() => parser.BuildFilter<Employee>("Tenant3CustomField:true", null, tenant3Options)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All queries should complete successfully
        Assert.Equal(300, results.Length);
        Assert.All(results, expr => Assert.NotNull(expr));
    }

    [Fact]
    public void BuildFilter_NoPerRequestOptions_UsesGlobalConfig()
    {
        // Arrange
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name", "Title");
        });

        // Act - No per-request options
        var filter = parser.BuildFilter<Employee>("test");

        // Assert
        Assert.NotNull(filter);
        var body = filter.Body as BinaryExpression;
        Assert.NotNull(body);
        Assert.Equal(ExpressionType.OrElse, body.NodeType);
    }

    [Fact]
    public void BuildFilter_PerRequestOptionsEmpty_UsesGlobalConfig()
    {
        // Arrange
        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name", "Title");
        });

        // Act - Empty per-request options
        var filter = parser.BuildFilter<Employee>("test", null, EntityFrameworkQueryOptions.Empty);

        // Assert
        Assert.NotNull(filter);
        var body = filter.Body as BinaryExpression;
        Assert.NotNull(body);
        Assert.Equal(ExpressionType.OrElse, body.NodeType);
    }

    [Fact]
    public async Task BuildFilter_WithInMemoryDatabase_ExecutesCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: "MultiTenantTest")
            .Options;

        using var context = new TestDbContext(options);

        // Add test data
        context.Employees.Add(new Employee { Id = 1, Name = "John Doe", Title = "Engineer", Department = "IT" });
        context.Employees.Add(new Employee { Id = 2, Name = "Jane Smith", Title = "Manager", Department = "HR" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var parser = new EntityFrameworkQueryParser(c =>
        {
            c.SetDefaultFields("Name");
        });

        var tenant1Options = new EntityFrameworkQueryOptions
        {
            DefaultFields = ["Name"]
        };

        var tenant2Options = new EntityFrameworkQueryOptions
        {
            DefaultFields = ["Title"]
        };

        // Act - Different tenants searching different fields
        var tenant1Filter = parser.BuildFilter<Employee>("John", null, tenant1Options);
        var tenant2Filter = parser.BuildFilter<Employee>("Manager", null, tenant2Options);

        var tenant1Results = await context.Employees.Where(tenant1Filter).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tenant2Results = await context.Employees.Where(tenant2Filter).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(tenant1Results);
        Assert.Equal("John Doe", tenant1Results[0].Name);

        Assert.Single(tenant2Results);
        Assert.Equal("Jane Smith", tenant2Results[0].Name);
    }

    private class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
    }

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public DbSet<Employee> Employees { get; set; }
    }
}
