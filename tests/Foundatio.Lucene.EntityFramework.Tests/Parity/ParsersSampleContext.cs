using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests.Parity;

/// <summary>
/// The model used by Foundatio.Parsers' SqlQueries tests, so its scenarios can be ported unchanged. The
/// <see cref="WorkItem"/> entity stands in for the ad-hoc field lists those tests declared by hand.
/// </summary>
public class ParsersSampleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<DataDefinition> DataDefinitions => Set<DataDefinition>();
    public DbSet<DataValue> DataValues => Set<DataValue>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var employee = modelBuilder.Entity<Employee>();
        employee.HasIndex(e => new { e.FullName, e.Title });
        employee.HasOne(e => e.CurrentCompany).WithMany().HasForeignKey(e => e.CurrentCompanyId).OnDelete(DeleteBehavior.Restrict);
        employee.HasMany(e => e.Companies).WithMany(c => c.Employees);

        modelBuilder.Entity<Company>().HasIndex(e => new { e.Name });
        modelBuilder.Entity<DataDefinition>().Property(c => c.DataType).IsRequired();
        modelBuilder.Entity<DataDefinition>().HasIndex(c => new { c.CompanyId, c.Key }).IsUnique();
        modelBuilder.Entity<DataValue>().Property(e => e.NumberValue).HasPrecision(15, 3);
        modelBuilder.Entity<DataValue>().HasOne(e => e.Employee).WithMany(e => e.DataValues).HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<DataValue>().HasOne(e => e.Definition).WithMany().HasForeignKey(e => e.DataDefinitionId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<WorkItem>().Property(e => e.Id).ValueGeneratedNever();
    }

    public static ParsersSampleContext CreateOffline()
    {
        return new ParsersSampleContext(new DbContextOptionsBuilder<ParsersSampleContext>()
            .UseSqlServer("Server=localhost;Database=offline;Integrated Security=true;TrustServerCertificate=true")
            .Options);
    }

    public static ParsersSampleContext CreateInMemory()
    {
        return new ParsersSampleContext(new DbContextOptionsBuilder<ParsersSampleContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    /// <summary>
    /// The data seeded by Parsers' <c>GetSampleContextWithDataAsync</c>.
    /// </summary>
    public void Seed()
    {
        var company = new Company
        {
            Name = "Acme",
            DataDefinitions = [new DataDefinition { Key = "age", DataType = DataType.Number }]
        };

        Companies.Add(company);
        Employees.Add(new Employee
        {
            FullName = "John Doe",
            Title = "Software Developer",
            PhoneNumber = "(214) 222-2222",
            NationalPhoneNumber = "2142222222",
            Salary = 80_000,
            Birthday = new DateOnly(1980, 1, 1),
            HappyHour = new TimeOnly(17, 0),
            DataValues = [new DataValue { Definition = company.DataDefinitions[0], NumberValue = 30 }],
            Companies = [company],
            CurrentCompany = company
        });
        Employees.Add(new Employee
        {
            FullName = "Jane Doe",
            Title = "Software Developer",
            PhoneNumber = "+52 55 1234 5678",
            NationalPhoneNumber = "5512345678",
            Salary = 90_000,
            Birthday = new DateOnly(1972, 11, 6),
            HappyHour = new TimeOnly(5, 30),
            DataValues = [new DataValue { Definition = company.DataDefinitions[0], NumberValue = 23 }],
            Companies = [company],
            CurrentCompany = company
        });

        SaveChanges();
    }

    /// <summary>
    /// The configuration of Parsers' <c>GetServiceProvider</c>: <c>Company.Description</c> is excluded, fieldless
    /// terms search <c>FullName</c> with Contains, and several fields are full-text indexed.
    /// </summary>
    public static EntityFrameworkQueryParser CreateParser(Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        return new EntityFrameworkQueryParser(c =>
        {
            c.UseEntityTypePropertyFilter(p => p.Name != nameof(Company.Description));
            c.UseCustomFieldExpressionBuilder(DynamicFieldBuilder);
            c.SetDefaultFields(["FullName"], SearchOperator.Contains);
            c.AddFullTextFields("Name", "FullName", "Title", "NationalPhoneNumber", "CurrentCompany.Name", "CurrentCompany.Location", "Companies.Name", "Companies.Location");
            configure?.Invoke(c);
        });
    }

    /// <summary>
    /// The equivalent of Parsers' <c>DynamicFieldVisitor</c>: a field with a <c>DataDefinitionId</c> queries
    /// <c>DataValues.Any(dv =&gt; dv.DataDefinitionId == id &amp;&amp; dv.&lt;typed value&gt; ...)</c>.
    /// </summary>
    public static Expression? DynamicFieldBuilder(CustomFieldContext context)
    {
        if (context.Field.Data.GetValueOrDefault("DataDefinitionId") is not int definitionId)
            return null;

        string column = context.Field switch
        {
            { IsNumber: true } => nameof(DataValue.NumberValue),
            { IsBoolean: true } => nameof(DataValue.BooleanValue),
            { IsDate: true } => nameof(DataValue.DateValue),
            _ => nameof(DataValue.StringValue)
        };

        var row = Expression.Parameter(typeof(DataValue), "dv");
        var body = Expression.AndAlso(
            Expression.Equal(Expression.Property(row, nameof(DataValue.DataDefinitionId)), context.Parameterize(definitionId)),
            context.BuildDefault(Expression.Property(row, column)));

        return context.Any(Expression.Property(context.Instance, nameof(Employee.DataValues)), Expression.Lambda(body, row));
    }

    public static EntityFieldInfo DynamicField(string name, Type type, int definitionId)
    {
        return new EntityFieldInfo { Name = name, ClrType = type, Data = new Dictionary<string, object?> { ["DataDefinitionId"] = definitionId } };
    }
}

public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string NationalPhoneNumber { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int Salary { get; set; }
    public int? CurrentCompanyId { get; set; }
    public Company? CurrentCompany { get; set; }
    public List<Company> Companies { get; set; } = null!;
    public List<DataValue> DataValues { get; set; } = null!;
    public TimeOnly HappyHour { get; set; }
    public DateOnly Birthday { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public List<Employee> Employees { get; set; } = null!;
    public List<DataDefinition> DataDefinitions { get; set; } = null!;
}

public class DataValue
{
    public int Id { get; set; }
    public int DataDefinitionId { get; set; }
    public int EmployeeId { get; set; }
    public string? StringValue { get; set; }
    public DateTime? DateValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DataDefinition? Definition { get; set; }
    public Employee? Employee { get; set; }
}

public class DataDefinition
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public DataType DataType { get; set; }
    public string Key { get; set; } = string.Empty;
    public Company? Company { get; set; }
}

public enum DataType
{
    String,
    Number,
    Boolean,
    Date
}

public class WorkItem
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Department { get; set; }
    public string? Team { get; set; }
    public int Salary { get; set; }
    public List<WorkCompany> Companies { get; set; } = [];
}

public class WorkCompany
{
    public int Id { get; set; }
    public string? Name { get; set; }
}
