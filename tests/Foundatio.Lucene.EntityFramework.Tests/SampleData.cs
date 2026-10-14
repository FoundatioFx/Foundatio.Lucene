using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

public static class SampleData
{
    public static readonly Guid FirstGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SecondGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ThirdGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public class Unmapped
    {
        public int X { get; set; }
    }

    public static SampleContext CreateInMemory(string? databaseName = null)
    {
        var db = new SampleContext(new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options);

        Seed(db);
        return db;
    }

    /// <summary>
    /// A SQL Server context that is never connected: <c>ToQueryString()</c> translates queries without a server.
    /// </summary>
    public static SampleContext CreateOfflineSqlServer()
    {
        return new SampleContext(new DbContextOptionsBuilder<SampleContext>()
            .UseSqlServer("Server=localhost;Database=offline;Integrated Security=true;TrustServerCertificate=true")
            .Options);
    }

    public static void Seed(SampleContext db)
    {
        var alice = new User { UserName = "alice.admin", PasswordHash = "hash-abc" };
        var bob = new User { UserName = "bob.owner", PasswordHash = "hash-xyz" };

        var acme = new Company { Name = "Acme Corp", Location = "New York", FoundedYear = 2000, IsPublic = true, Owner = alice };
        var tech = new Company { Name = "Tech Solutions", Location = "San Francisco", FoundedYear = 2015, IsPublic = false, Owner = bob };

        var engineering = new Department { Name = "Engineering", Budget = 1_000_000, Company = acme };
        var sales = new Department { Name = "Sales", Budget = 500_000, Company = acme };
        var research = new Department { Name = "Research", Budget = null, Company = tech };

        var apollo = new Project { Name = "Apollo" };
        var zephyr = new Project { Name = "Zephyr" };

        var john = new Employee
        {
            Name = "John Doe",
            Email = "john@acme.com",
            Title = "Software Developer",
            Salary = 80_000,
            Age = 30,
            HireDate = new DateTime(2020, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            Status = EmployeeStatus.Active,
            Level = EmployeeLevel.Mid,
            PasswordHash = "secret-john",
            Skills = ["csharp", "sql"],
            Company = acme,
            Department = engineering,
            Address = new Address { Street = "1 Main St", City = "New York" },
            Projects = [apollo]
        };

        var jane = new Employee
        {
            Name = "Jane Smith",
            Email = "jane@acme.com",
            Title = "Project Manager",
            Salary = 95_000,
            Age = 35,
            HireDate = new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            Status = EmployeeStatus.Active,
            Level = EmployeeLevel.Senior,
            PasswordHash = "secret-jane",
            Skills = ["management"],
            Company = acme,
            Department = sales,
            Address = new Address { Street = "2 Oak Ave", City = "Boston" },
            Projects = [apollo, zephyr]
        };

        var bobWilson = new Employee
        {
            Name = "Bob Wilson",
            Email = "bob@tech.com",
            Title = "Senior Developer",
            Salary = 110_000,
            Age = 40,
            HireDate = new DateTime(2018, 3, 20, 17, 45, 0, DateTimeKind.Utc),
            IsActive = true,
            Status = EmployeeStatus.OnLeave,
            Level = EmployeeLevel.Senior,
            PasswordHash = "secret-bob",
            Skills = ["csharp", "go"],
            Company = tech,
            Department = research,
            Address = new Address { Street = "3 Pine Rd", City = "San Francisco" },
            Projects = [zephyr]
        };

        var aliceBrown = new Employee
        {
            Name = "Alice Brown",
            Email = null,
            Title = "Junior Developer",
            Salary = 55_000,
            Age = 25,
            HireDate = new DateTime(2022, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            IsActive = false,
            Status = EmployeeStatus.Terminated,
            Level = EmployeeLevel.Junior,
            PasswordHash = "secret-alice",
            Skills = [],
            Company = acme,
            Department = engineering,
            Manager = john,
            Address = new Address { Street = "4 Elm St", City = "New York" }
        };

        db.AddRange(alice, bob, acme, tech, engineering, sales, research, apollo, zephyr, john, jane, bobWilson, aliceBrown);
        db.AddRange(CreateTypeSamples());
        db.AddRange(Enumerable.Range(0, 8).Select(id => new Flag { Id = id, A = (id & 4) != 0, B = (id & 2) != 0, C = (id & 1) != 0 }));
        db.SaveChanges();
    }

    public static IEnumerable<TypeSample> CreateTypeSamples()
    {
        yield return new TypeSample
        {
            Id = 1,
            Text = "alpha",
            Int = 10,
            NullableInt = 5,
            Long = 10_000_000_000,
            Short = -5,
            Byte = 200,
            Decimal = 12.5m,
            Double = 1.5,
            Float = 2.5f,
            Bool = true,
            NullableBool = true,
            Guid = FirstGuid,
            Char = 'a',
            Enum = EmployeeStatus.Active,
            NullableEnum = EmployeeStatus.OnLeave,
            DateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            NullableDateTime = null,
            DateTimeOffset = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            DateOnly = new DateOnly(2024, 1, 15),
            TimeOnly = new TimeOnly(9, 0),
            TimeSpan = TimeSpan.FromMinutes(90),
            Bytes = [1, 2]
        };

        yield return new TypeSample
        {
            Id = 2,
            Text = "beta",
            Int = 20,
            NullableInt = null,
            Long = -1,
            Short = 7,
            Byte = 0,
            Decimal = 99.99m,
            Double = -3.25,
            Float = 0f,
            Bool = false,
            NullableBool = null,
            Guid = SecondGuid,
            Char = 'b',
            Enum = EmployeeStatus.Terminated,
            NullableEnum = null,
            DateTime = new DateTime(2024, 1, 31, 23, 30, 0, DateTimeKind.Utc),
            NullableDateTime = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTimeOffset = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.FromHours(-6)),
            DateOnly = new DateOnly(2024, 1, 31),
            TimeOnly = new TimeOnly(17, 30),
            TimeSpan = TimeSpan.FromHours(8),
            Bytes = null
        };

        yield return new TypeSample
        {
            Id = 3,
            Text = null,
            Int = 30,
            NullableInt = 15,
            Long = 0,
            Short = 0,
            Byte = 1,
            Decimal = 0.001m,
            Double = 1e10,
            Float = -1f,
            Bool = true,
            NullableBool = false,
            Guid = ThirdGuid,
            Char = 'z',
            Enum = EmployeeStatus.OnLeave,
            NullableEnum = EmployeeStatus.Active,
            DateTime = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            NullableDateTime = new DateTime(2024, 3, 10, 8, 30, 0, DateTimeKind.Utc),
            DateTimeOffset = new DateTimeOffset(2024, 3, 10, 8, 30, 0, TimeSpan.Zero),
            DateOnly = new DateOnly(2024, 2, 1),
            TimeOnly = new TimeOnly(23, 59),
            TimeSpan = TimeSpan.FromSeconds(30),
            Bytes = null
        };
    }
}

public static class QueryableTestExtensions
{
    public static string[] Names(this IQueryable<Employee> query) => [.. query.Select(e => e.Name).AsEnumerable().Order(StringComparer.Ordinal)];

    public static int[] Ids(this IQueryable<TypeSample> query) => [.. query.Select(e => e.Id).AsEnumerable().Order()];

    public static int[] Ids(this IQueryable<Flag> query) => [.. query.Select(e => e.Id).AsEnumerable().Order()];
}
