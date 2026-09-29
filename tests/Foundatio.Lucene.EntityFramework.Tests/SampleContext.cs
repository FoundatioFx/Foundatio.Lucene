using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

public class SampleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<DataValue> DataValues => Set<DataValue>();
    public DbSet<TypeSample> TypeSamples => Set<TypeSample>();
    public DbSet<Flag> Flags => Set<Flag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Salary).HasPrecision(18, 2);
            entity.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);
            entity.Property<string>("InternalNotes").HasMaxLength(200);
            entity.HasOne(e => e.Company).WithMany(c => c.Employees).HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Department).WithMany(d => d.Employees).HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Manager).WithMany(e => e.Reports).HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Projects).WithMany(p => p.Employees);
            entity.OwnsOne(e => e.Address);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(100);
            entity.Property(c => c.Location).HasMaxLength(200);
            entity.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(100);
            entity.HasOne(d => d.Company).WithMany(c => c.Departments).HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DataValue>(entity =>
        {
            entity.Property(d => d.DecimalValue).HasPrecision(18, 4);
            entity.HasOne(d => d.Contact).WithMany(c => c.DataValues).HasForeignKey(d => d.ContactId);
        });

        modelBuilder.Entity<TypeSample>(entity =>
        {
            entity.Property(t => t.Decimal).HasPrecision(18, 4);
            entity.Property(t => t.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Flag>().Property(f => f.Id).ValueGeneratedNever();
    }
}

public enum EmployeeStatus
{
    Active,
    OnLeave,
    Terminated
}

public enum EmployeeLevel
{
    Junior,
    Mid,
    Senior
}

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
    public EmployeeStatus Status { get; set; }
    public EmployeeLevel Level { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = [];

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
    public ICollection<Employee> Reports { get; set; } = [];

    public Address Address { get; set; } = new();

    public ICollection<Project> Projects { get; set; } = [];
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int FoundedYear { get; set; }
    public bool IsPublic { get; set; }

    public int? OwnerId { get; set; }
    public User? Owner { get; set; }

    public ICollection<Employee> Employees { get; set; } = [];
    public ICollection<Department> Departments { get; set; } = [];
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Budget { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ICollection<Employee> Employees { get; set; } = [];
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Employee> Employees { get; set; } = [];
}

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<DataValue> DataValues { get; set; } = [];
}

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

public class TypeSample
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public int Int { get; set; }
    public int? NullableInt { get; set; }
    public long Long { get; set; }
    public short Short { get; set; }
    public byte Byte { get; set; }
    public decimal Decimal { get; set; }
    public double Double { get; set; }
    public float Float { get; set; }
    public bool Bool { get; set; }
    public bool? NullableBool { get; set; }
    public Guid Guid { get; set; }
    public char Char { get; set; }
    public EmployeeStatus Enum { get; set; }
    public EmployeeStatus? NullableEnum { get; set; }
    public DateTime DateTime { get; set; }
    public DateTime? NullableDateTime { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
    public DateOnly DateOnly { get; set; }
    public TimeOnly TimeOnly { get; set; }
    public TimeSpan TimeSpan { get; set; }
    public byte[]? Bytes { get; set; }
}

/// <summary>
/// A row for each combination of three flags, so boolean query semantics can be checked against an oracle.
/// </summary>
public class Flag
{
    public int Id { get; set; }
    public bool A { get; set; }
    public bool B { get; set; }
    public bool C { get; set; }
}
