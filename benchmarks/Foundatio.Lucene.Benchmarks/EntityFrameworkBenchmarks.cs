using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Foundatio.Lucene.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.Benchmarks;

/// <summary>
/// Benchmarks for Entity Framework query generation from Lucene queries.
/// Measures the overhead of converting Lucene AST to LINQ expressions.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(AntiVirusFriendlyConfig))]
public class EntityFrameworkBenchmarks
{
    // Use InProcess toolchain to avoid antivirus/rebuild issues
    private class AntiVirusFriendlyConfig : ManualConfig
    {
        public AntiVirusFriendlyConfig()
        {
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    // Query complexity levels
    private const string SimpleTermQuery = "John";
    private const string SimpleFieldQuery = "Name:John";
    private const string MultiFieldQuery = "Name:John AND Age:30";
    private const string WildcardQuery = "Name:John* AND Email:*@acme.com";
    private const string RangeQuery = "Salary:[50000 TO 100000] AND Age:[25 TO 40]";
    private const string ComplexQuery = "Name:John AND (Title:Engineer OR Title:Developer) AND Salary:[50000 TO *] AND IsActive:true";
    private const string NavigationQuery = "Company.Name:Acme AND Department.Name:Engineering";
    private const string CollectionQuery = "Employees.Name:John";
    private const string FullTextQuery = "Name:developer AND Title:senior";
    private const string SortExpression = "-Salary Company.Name +Name";

    private EntityFrameworkQueryParser _parser = null!;
    private EntityFrameworkQueryParser _parserWithFullText = null!;
    private EntityFrameworkQueryOptions _tenantOptions = null!;
    private BenchmarkDbContext _dbContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbContext = new BenchmarkDbContext(new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseInMemoryDatabase("BenchmarkDb")
            .Options);

        var model = _dbContext.Model;
        _parser = new EntityFrameworkQueryParser(config => config.UseModel(model).SetDefaultFields("Name", "Title"));
        _parserWithFullText = new EntityFrameworkQueryParser(config => config.UseModel(model).AddFullTextFields("Employee.Name", "Employee.Title"));
        _tenantOptions = new EntityFrameworkQueryOptions
        {
            FieldMap = new FieldMap { { "who", "Name" }, { "org", "Company.Name" } },
            DefaultFields = ["Name"]
        };

        // Warm the per-parser field metadata caches so the benchmarks measure steady-state cost.
        _parser.BuildFilter<Employee>(ComplexQuery);
        _parser.BuildFilter<Employee>(NavigationQuery);
        _parser.BuildFilter<Company>(CollectionQuery);
        _parserWithFullText.BuildFilter<Employee>(FullTextQuery);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    #region Full Pipeline (Parse + Build Expression)

    [Benchmark(Baseline = true)]
    public Expression<Func<Employee, bool>> BuildFilter_SimpleTerm()
        => _parser.BuildFilter<Employee>(SimpleTermQuery);

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_SimpleField()
        => _parser.BuildFilter<Employee>(SimpleFieldQuery);

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_MultiField()
        => _parser.BuildFilter<Employee>(MultiFieldQuery);

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_Wildcard()
        => _parser.BuildFilter<Employee>(WildcardQuery);

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_Range()
        => _parser.BuildFilter<Employee>(RangeQuery);

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_Complex()
        => _parser.BuildFilter<Employee>(ComplexQuery);

    #endregion

    #region Navigation Properties

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_Navigation()
        => _parser.BuildFilter<Employee>(NavigationQuery);

    [Benchmark]
    public Expression<Func<Company, bool>> BuildFilter_Collection()
        => _parser.BuildFilter<Company>(CollectionQuery);

    #endregion

    #region Full-Text Search

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_FullText()
        => _parserWithFullText.BuildFilter<Employee>(FullTextQuery);

    #endregion

    #region Per-Request Options, Queryable Extensions, and Sorting

    [Benchmark]
    public Expression<Func<Employee, bool>> BuildFilter_WithTenantOptions()
        => _parser.BuildFilter<Employee>("who:John AND org:Acme*", _tenantOptions);

    [Benchmark]
    public Expression Where_OnDbSet()
        => _dbContext.Employees.Where(ComplexQuery, _parser).Expression;

    [Benchmark]
    public Expression OrderBy_OnDbSet()
        => _dbContext.Employees.OrderBy(SortExpression, _parser).Expression;

    #endregion
}

#region Benchmark Entities

public class BenchmarkDbContext : DbContext
{
    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Company).WithMany(c => c.Employees).HasForeignKey(e => e.CompanyId);
            entity.HasOne(e => e.Department).WithMany(d => d.Employees).HasForeignKey(e => e.DepartmentId);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(c => c.Id);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasOne(d => d.Company).WithMany(c => c.Departments).HasForeignKey(d => d.CompanyId);
        });
    }
}

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Title { get; set; }
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public bool IsActive { get; set; }
    public DateTime HireDate { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Location { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Budget { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

#endregion
