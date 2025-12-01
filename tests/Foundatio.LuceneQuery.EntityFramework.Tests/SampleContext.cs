using Microsoft.EntityFrameworkCore;

namespace Foundatio.LuceneQuery.EntityFramework.Tests;

/// <summary>
/// Sample DbContext for testing Entity Framework integration.
/// </summary>
public class SampleContext : DbContext
{
    public SampleContext(DbContextOptions<SampleContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<DataValue> DataValues => Set<DataValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Employee
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.HasOne(e => e.Company)
                  .WithMany(c => c.Employees)
                  .HasForeignKey(e => e.CompanyId);
            entity.HasOne(e => e.Department)
                  .WithMany(d => d.Employees)
                  .HasForeignKey(e => e.DepartmentId);
        });

        // Configure Company
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(100);
            entity.Property(c => c.Location).HasMaxLength(200);
        });

        // Configure Department
        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Name).HasMaxLength(100);
        });

        // Configure Contact
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(100);
        });

        // Configure DataValue
        modelBuilder.Entity<DataValue>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasOne(d => d.Contact)
                  .WithMany(c => c.DataValues)
                  .HasForeignKey(d => d.ContactId);
        });
    }
}

/// <summary>
/// Sample employee entity.
/// </summary>
public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Title { get; set; }
    public decimal Salary { get; set; }
    public int Age { get; set; }
    public DateTime HireDate { get; set; }
    public bool IsActive { get; set; }
    
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
}

/// <summary>
/// Sample company entity.
/// </summary>
public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int FoundedYear { get; set; }
    public bool IsPublic { get; set; }
    
    public ICollection<Employee> Employees { get; set; } = [];
    public ICollection<Department> Departments { get; set; } = [];
}

/// <summary>
/// Sample department entity.
/// </summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Budget { get; set; }
    
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    
    public ICollection<Employee> Employees { get; set; } = [];
}

/// <summary>
/// Entity with dynamic data values for custom field testing.
/// </summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<DataValue> DataValues { get; set; } = [];
}

/// <summary>
/// Dynamic data value for custom fields.
/// </summary>
public class DataValue
{
    public int Id { get; set; }
    public int DataDefinitionId { get; set; }
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    
    public string? StringValue { get; set; }
    public int? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public DateTime? DateValue { get; set; }
    public bool? BooleanValue { get; set; }
}
