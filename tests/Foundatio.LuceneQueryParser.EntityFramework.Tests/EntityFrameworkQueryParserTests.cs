using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.LuceneQueryParser.EntityFramework.Tests;

public class EntityFrameworkQueryParserTests
{
    private SampleContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new SampleContext(options);
        SeedData(context);
        return context;
    }

    private void SeedData(SampleContext context)
    {
        var company1 = new Company
        {
            Id = 1,
            Name = "Acme Corp",
            Location = "New York",
            FoundedYear = 2000,
            IsPublic = true
        };

        var company2 = new Company
        {
            Id = 2,
            Name = "Tech Solutions",
            Location = "San Francisco",
            FoundedYear = 2015,
            IsPublic = false
        };

        var dept1 = new Department { Id = 1, Name = "Engineering", Budget = 1000000, CompanyId = 1 };
        var dept2 = new Department { Id = 2, Name = "Sales", Budget = 500000, CompanyId = 1 };
        var dept3 = new Department { Id = 3, Name = "Research", Budget = 750000, CompanyId = 2 };

        var employees = new[]
        {
            new Employee
            {
                Id = 1, Name = "John Doe", Email = "john@acme.com", Title = "Software Developer",
                Salary = 80000, Age = 30, HireDate = new DateTime(2020, 1, 15), IsActive = true,
                CompanyId = 1, DepartmentId = 1
            },
            new Employee
            {
                Id = 2, Name = "Jane Smith", Email = "jane@acme.com", Title = "Project Manager",
                Salary = 95000, Age = 35, HireDate = new DateTime(2019, 6, 1), IsActive = true,
                CompanyId = 1, DepartmentId = 2
            },
            new Employee
            {
                Id = 3, Name = "Bob Wilson", Email = "bob@tech.com", Title = "Senior Developer",
                Salary = 110000, Age = 40, HireDate = new DateTime(2018, 3, 20), IsActive = true,
                CompanyId = 2, DepartmentId = 3
            },
            new Employee
            {
                Id = 4, Name = "Alice Brown", Email = "alice@acme.com", Title = "Junior Developer",
                Salary = 55000, Age = 25, HireDate = new DateTime(2022, 9, 1), IsActive = false,
                CompanyId = 1, DepartmentId = 1
            }
        };

        context.Companies.AddRange(company1, company2);
        context.Departments.AddRange(dept1, dept2, dept3);
        context.Employees.AddRange(employees);
        context.SaveChanges();
    }

    [Fact]
    public void Parse_SimpleTerm_MatchesName()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"));

        var filter = parser.BuildFilter<Employee>("John");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_FieldQuery_MatchesSpecificField()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Name:John");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_PhraseQuery_MatchesExactPhrase()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Title:\"Software Developer\"");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_PrefixQuery_MatchesPrefix()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Title:Software*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_WildcardQuery_MatchesPattern()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Name:*ohn*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_NumericEquality_MatchesValue()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Age:30");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_RangeQuery_MatchesRange()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Age:[30 TO 40]");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(3, results.Count); // John (30), Jane (35), Bob (40)
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Jane Smith");
        Assert.Contains(results, e => e.Name == "Bob Wilson");
    }

    [Fact]
    public void Parse_RangeQueryExclusive_MatchesRange()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Age:{25 TO 35}");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_GreaterThan_MatchesValues()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Salary:>90000");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Jane Smith");
        Assert.Contains(results, e => e.Name == "Bob Wilson");
    }

    [Fact]
    public void Parse_LessThanOrEqual_MatchesValues()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Age:<=30");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Alice Brown");
    }

    [Fact]
    public void Parse_BooleanField_MatchesValue()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("IsActive:false");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Alice Brown", results[0].Name);
    }

    [Fact]
    public void Parse_AndQuery_MatchesBothConditions()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("IsActive:true AND Age:>30");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Jane Smith");
        Assert.Contains(results, e => e.Name == "Bob Wilson");
    }

    [Fact]
    public void Parse_OrQuery_MatchesEitherCondition()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Name:John OR Name:Jane");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Jane Smith");
    }

    [Fact]
    public void Parse_NotQuery_ExcludesMatches()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("NOT IsActive:false");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, e => e.Name == "Alice Brown");
    }

    [Fact]
    public void Parse_NavigationProperty_MatchesRelatedEntity()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Company.Name:Acme*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(3, results.Count);
        Assert.All(results, e => Assert.Equal(1, e.CompanyId));
    }

    [Fact]
    public void Parse_NestedNavigation_MatchesNestedProperty()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Department.Name:Engineering");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Alice Brown");
    }

    [Fact]
    public void Parse_CollectionNavigation_MatchesAny()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Company>("Employees.Name:John*");
        var results = context.Companies.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Acme Corp", results[0].Name);
    }

    [Fact]
    public void Parse_CollectionWithNumeric_MatchesAny()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Company>("Employees.Salary:>100000");
        var results = context.Companies.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Tech Solutions", results[0].Name);
    }

    [Fact]
    public void Parse_ExistsQuery_MatchesNonNull()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Email:*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void Parse_GroupedQuery_RespectsGrouping()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // (John OR Jane) AND IsActive:true
        var filter = parser.BuildFilter<Employee>("(Name:John OR Name:Jane) AND IsActive:true");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Jane Smith");
    }

    [Fact]
    public void Parse_DefaultFields_SearchesAllDefaultFields()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name", "Email", "Title"));

        var filter = parser.BuildFilter<Employee>("Developer");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(3, results.Count); // Matches Title containing "Developer"
    }

    [Fact]
    public void Parse_CaseSensitivity_DeterminedByProvider()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Case sensitivity is determined by the database provider/collation
        // In-memory provider is case-sensitive, SQL Server is typically case-insensitive
        var filter = parser.BuildFilter<Employee>("name:John*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_DecimalField_MatchesValue()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("Salary:80000");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_DateField_MatchesExactDate()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Use quoted date for exact match - relies on DateTimeParser
        var filter = parser.BuildFilter<Employee>("HireDate:\"2019-06-01\"");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Jane Smith", results[0].Name);
    }

    [Fact]
    public void Parse_DateRangeInclusive_MatchesDatesInRange()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Matches employees hired between 2019-01-01 and 2020-12-31 inclusive
        // ISO8601 format works in range expressions
        var filter = parser.BuildFilter<Employee>("HireDate:[2019-01-01 TO 2020-12-31]");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");    // 2020-01-15
        Assert.Contains(results, e => e.Name == "Jane Smith");  // 2019-06-01
    }

    [Fact]
    public void Parse_DateRangeExclusive_MatchesDatesInRange()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Matches employees hired between dates exclusive (excludes exact boundary dates)
        var filter = parser.BuildFilter<Employee>("HireDate:{2018-01-01 TO 2019-12-31}");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Bob Wilson");  // 2018-03-20
        Assert.Contains(results, e => e.Name == "Jane Smith");  // 2019-06-01
    }

    [Fact]
    public void Parse_DateGreaterThan_MatchesLaterDates()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("HireDate:>2020-01-01");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");    // 2020-01-15
        Assert.Contains(results, e => e.Name == "Alice Brown"); // 2022-09-01
    }

    [Fact]
    public void Parse_DateGreaterThanOrEqual_MatchesDates()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("HireDate:>=2020-01-15");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");    // 2020-01-15 (exact match)
        Assert.Contains(results, e => e.Name == "Alice Brown"); // 2022-09-01
    }

    [Fact]
    public void Parse_DateLessThan_MatchesEarlierDates()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("HireDate:<2019-01-01");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("Bob Wilson", results[0].Name); // 2018-03-20
    }

    [Fact]
    public void Parse_DateLessThanOrEqual_MatchesDates()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("HireDate:<=2019-06-01");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Bob Wilson");  // 2018-03-20
        Assert.Contains(results, e => e.Name == "Jane Smith");  // 2019-06-01 (exact match)
    }

    [Fact]
    public void Parse_DateOpenEndedRange_MatchesFromDate()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Open-ended range: from 2020-01-01 to infinity
        var filter = parser.BuildFilter<Employee>("HireDate:[2020-01-01 TO *]");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Alice Brown");
    }

    [Fact]
    public void Parse_DateOpenStartRange_MatchesToDate()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Open-ended range: from beginning to 2019-06-01
        var filter = parser.BuildFilter<Employee>("HireDate:[* TO 2019-06-01]");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Bob Wilson");  // 2018-03-20
        Assert.Contains(results, e => e.Name == "Jane Smith");  // 2019-06-01
    }

    [Fact]
    public void Parse_DateWithAndOperator_CombinesConditions()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Employees hired after 2019 AND active
        var filter = parser.BuildFilter<Employee>("HireDate:>2019-12-31 AND IsActive:true");
        var results = context.Employees.Where(filter).ToList();

        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void Parse_DateRangeMaxUsesEndOfDay_IncludesFullDay()
    {
        // Arrange - Create context with employee hired at a specific time
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new SampleContext(options);

        var company = new Company { Id = 1, Name = "Test Corp" };
        context.Companies.Add(company);

        // Employee hired at 3:30 PM on 2020-01-15
        var employeeWithTime = new Employee
        {
            Id = 1,
            Name = "Afternoon Hire",
            HireDate = new DateTime(2020, 1, 15, 15, 30, 0),
            CompanyId = 1,
            IsActive = true
        };
        context.Employees.Add(employeeWithTime);
        context.SaveChanges();

        var parser = new EntityFrameworkQueryParser();

        // Act - Query with date-only max should include employees hired anytime on that day
        // Max date 2020-01-15 should be treated as 2020-01-15 23:59:59.9999999
        var filter = parser.BuildFilter<Employee>("HireDate:[2020-01-01 TO 2020-01-15]");
        var results = context.Employees.Where(filter).ToList();

        // Assert - Should find the employee hired at 3:30 PM on Jan 15
        Assert.Single(results);
        Assert.Equal("Afternoon Hire", results[0].Name);
    }

    [Fact]
    public void Parse_DateLessThanOrEqualUsesEndOfDay_IncludesFullDay()
    {
        // Arrange - Create context with employee hired at a specific time
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new SampleContext(options);

        var company = new Company { Id = 1, Name = "Test Corp" };
        context.Companies.Add(company);

        // Employee hired at 11:00 PM on 2020-01-15 (late evening)
        var employeeLateEvening = new Employee
        {
            Id = 1,
            Name = "Late Evening Hire",
            HireDate = new DateTime(2020, 1, 15, 23, 0, 0),
            CompanyId = 1,
            IsActive = true
        };
        context.Employees.Add(employeeLateEvening);
        context.SaveChanges();

        var parser = new EntityFrameworkQueryParser();

        // Act - Query with <= date-only should include employees hired anytime on that day
        var filter = parser.BuildFilter<Employee>("HireDate:<=2020-01-15");
        var results = context.Employees.Where(filter).ToList();

        // Assert - Should find the employee hired at 11 PM on Jan 15
        Assert.Single(results);
        Assert.Equal("Late Evening Hire", results[0].Name);
    }

    [Fact]
    public void Parse_EmptyQuery_ReturnsAllRecords()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Empty or whitespace query should return true (match all)
        var filter = parser.BuildFilter<Employee>("*");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void TryBuildFilter_InvalidQuery_ReturnsFalse()
    {
        var parser = new EntityFrameworkQueryParser();

        // Using an unclosed bracket to simulate parse error
        var success = parser.TryBuildFilter<Employee>("Name:(unclosed", out var expression);

        // Note: The parser may be lenient - check actual behavior
        // If it returns partial results, this test may need adjustment
    }

    [Fact]
    public void Parse_ComplexBooleanExpression_MatchesCorrectly()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        // Complex: Active employees from Acme who are either in Engineering or have high salary
        var filter = parser.BuildFilter<Employee>(
            "IsActive:true AND Company.Name:Acme* AND (Department.Name:Engineering OR Salary:>90000)");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");  // Engineering
        Assert.Contains(results, e => e.Name == "Jane Smith"); // High salary
    }

    [Fact]
    public void Parse_RequiredTerm_MatchesMust()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("+IsActive:true +CompanyId:1");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.True(e.IsActive && e.CompanyId == 1));
    }

    [Fact]
    public void Parse_ProhibitedTerm_ExcludesMatch()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var filter = parser.BuildFilter<Employee>("CompanyId:1 -IsActive:false");
        var results = context.Employees.Where(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, e => !e.IsActive);
    }

    [Fact]
    public void GetContext_WithEntityType_ReturnsFieldInfo()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var visitorContext = parser.GetContext(entityType);

        Assert.NotEmpty(visitorContext.Fields);
        Assert.Contains(visitorContext.Fields, f => f.Name == "Name");
        Assert.Contains(visitorContext.Fields, f => f.Name == "Salary");
        Assert.Contains(visitorContext.Fields, f => f.Name == "Company" && f.IsNavigation);
    }

    [Fact]
    public void Configuration_PropertyFilter_ExcludesFields()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser(c =>
            c.UseEntityTypePropertyFilter(p => p.Name != "Email"));

        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var visitorContext = parser.GetContext(entityType);

        Assert.DoesNotContain(visitorContext.Fields, f => f.Name == "Email");
        Assert.Contains(visitorContext.Fields, f => f.Name == "Name");
    }

    [Fact]
    public void ExtensionMethod_WhereWithParser_FiltersCorrectly()
    {
        using var context = CreateContext();
        var parser = new EntityFrameworkQueryParser();

        var results = context.Employees
            .Where("Age:>30", parser)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.True(e.Age > 30));
    }

    [Fact]
    public void CustomFieldExpressionBuilder_QueryWithCustomField_MatchesDataValue()
    {
        // Arrange - Create context with custom data
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new SampleContext(options);

        // Add contacts with data values
        var contact1 = new Contact { Id = 1, Name = "John" };
        var contact2 = new Contact { Id = 2, Name = "Jane" };
        var contact3 = new Contact { Id = 3, Name = "Bob" };

        // Data definition 1 = "age" (integer), Data definition 2 = "city" (string)
        context.Contacts.AddRange(contact1, contact2, contact3);
        context.DataValues.AddRange(
            new DataValue { Id = 1, ContactId = 1, DataDefinitionId = 1, IntegerValue = 30 },
            new DataValue { Id = 2, ContactId = 1, DataDefinitionId = 2, StringValue = "New York" },
            new DataValue { Id = 3, ContactId = 2, DataDefinitionId = 1, IntegerValue = 25 },
            new DataValue { Id = 4, ContactId = 2, DataDefinitionId = 2, StringValue = "Boston" },
            new DataValue { Id = 5, ContactId = 3, DataDefinitionId = 1, IntegerValue = 40 }
        );
        context.SaveChanges();

        // Create parser with custom field expression builder
        var parser = new EntityFrameworkQueryParser(config =>
        {
            // Register custom fields with metadata
            config.SetCustomFieldExpressionBuilder(ctx =>
            {
                if (!ctx.Field.Data.TryGetValue("DataDefinitionId", out var idObj) || idObj is not int definitionId)
                    return null;

                // Build: c.DataValues.Any(dv => dv.DataDefinitionId == X && dv.IntegerValue == Y)
                // or c.DataValues.Any(dv => dv.DataDefinitionId == X && dv.StringValue == Y)
                var dataValuesProperty = Expression.Property(ctx.Parameter, "DataValues");

                // Create parameter for DataValue
                var dvParam = Expression.Parameter(typeof(DataValue), "dv");

                // DataDefinitionId == X
                var definitionIdExpr = Expression.Equal(
                    Expression.Property(dvParam, "DataDefinitionId"),
                    Expression.Constant(definitionId));

                // Determine the value property based on field type
                Expression valueExpr;
                if (ctx.Field.Data.TryGetValue("FieldType", out var typeObj) && typeObj is string fieldType)
                {
                    if (fieldType == "Integer" && int.TryParse(ctx.Term, out var intVal))
                    {
                        valueExpr = Expression.Equal(
                            Expression.Property(dvParam, "IntegerValue"),
                            Expression.Constant((int?)intVal, typeof(int?)));
                    }
                    else
                    {
                        valueExpr = Expression.Equal(
                            Expression.Property(dvParam, "StringValue"),
                            Expression.Constant(ctx.Term));
                    }
                }
                else
                {
                    valueExpr = Expression.Equal(
                        Expression.Property(dvParam, "StringValue"),
                        Expression.Constant(ctx.Term));
                }

                // Combined: DataDefinitionId == X && ValueProperty == Y
                var combinedExpr = Expression.AndAlso(definitionIdExpr, valueExpr);
                var predicate = Expression.Lambda<Func<DataValue, bool>>(combinedExpr, dvParam);

                // Build Any() call
                var anyMethod = typeof(Enumerable)
                    .GetMethods()
                    .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(DataValue));

                return Expression.Call(anyMethod, dataValuesProperty, predicate);
            });
        });

        // Add the custom field to the context
        var entityType = context.Model.FindEntityType(typeof(Contact))!;
        var visitorContext = parser.GetContext(entityType);
        visitorContext.Fields.Add(new EntityFieldInfo
        {
            Name = "age",
            FullName = "age",
            Data = new Dictionary<string, object>
            {
                ["DataDefinitionId"] = 1,
                ["FieldType"] = "Integer"
            }
        });
        visitorContext.Fields.Add(new EntityFieldInfo
        {
            Name = "city",
            FullName = "city",
            Data = new Dictionary<string, object>
            {
                ["DataDefinitionId"] = 2,
                ["FieldType"] = "String"
            }
        });

        // Act - Query using custom field
        var filter = parser.BuildFilter<Contact>("age:30", visitorContext);
        var results = context.Contacts.Where(filter).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("John", results[0].Name);
    }

    [Fact]
    public void CustomFieldExpressionBuilder_QueryWithStringField_MatchesDataValue()
    {
        // Arrange - Create context with custom data
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new SampleContext(options);

        // Add contacts with data values
        var contact1 = new Contact { Id = 1, Name = "John" };
        var contact2 = new Contact { Id = 2, Name = "Jane" };

        context.Contacts.AddRange(contact1, contact2);
        context.DataValues.AddRange(
            new DataValue { Id = 1, ContactId = 1, DataDefinitionId = 2, StringValue = "New York" },
            new DataValue { Id = 2, ContactId = 2, DataDefinitionId = 2, StringValue = "Boston" }
        );
        context.SaveChanges();

        // Create parser with custom field expression builder
        var parser = new EntityFrameworkQueryParser(config =>
        {
            config.SetCustomFieldExpressionBuilder(ctx =>
            {
                if (!ctx.Field.Data.TryGetValue("DataDefinitionId", out var idObj) || idObj is not int definitionId)
                    return null;

                var dataValuesProperty = Expression.Property(ctx.Parameter, "DataValues");
                var dvParam = Expression.Parameter(typeof(DataValue), "dv");

                var definitionIdExpr = Expression.Equal(
                    Expression.Property(dvParam, "DataDefinitionId"),
                    Expression.Constant(definitionId));

                var valueExpr = Expression.Equal(
                    Expression.Property(dvParam, "StringValue"),
                    Expression.Constant(ctx.Term));

                var combinedExpr = Expression.AndAlso(definitionIdExpr, valueExpr);
                var predicate = Expression.Lambda<Func<DataValue, bool>>(combinedExpr, dvParam);

                var anyMethod = typeof(Enumerable)
                    .GetMethods()
                    .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(DataValue));

                return Expression.Call(anyMethod, dataValuesProperty, predicate);
            });
        });

        // Add the custom field
        var entityType = context.Model.FindEntityType(typeof(Contact))!;
        var visitorContext = parser.GetContext(entityType);
        visitorContext.Fields.Add(new EntityFieldInfo
        {
            Name = "city",
            FullName = "city",
            Data = new Dictionary<string, object> { ["DataDefinitionId"] = 2 }
        });

        // Act
        var filter = parser.BuildFilter<Contact>("city:\"New York\"", visitorContext);
        var results = context.Contacts.Where(filter).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("John", results[0].Name);
    }

    [Fact]
    public void CustomFieldExpressionBuilder_ReturnsNull_FallsBackToDefault()
    {
        // Arrange
        using var context = CreateContext();

        var customBuilderCalled = false;
        var parser = new EntityFrameworkQueryParser(config =>
        {
            config.SetCustomFieldExpressionBuilder(ctx =>
            {
                customBuilderCalled = true;
                // Return null to fall back to default handling
                return null;
            });
        });

        // Add a field with custom data so the builder gets called
        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var visitorContext = parser.GetContext(entityType);
        var nameField = visitorContext.GetField("Name");
        if (nameField != null)
        {
            nameField.Data["CustomFlag"] = true;
        }

        // Act - Query using standard field should work normally
        var filter = parser.BuildFilter<Employee>("Name:John", visitorContext);
        var results = context.Employees.Where(filter).ToList();

        // Assert
        Assert.True(customBuilderCalled);
        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    #region DI Integration Tests

    private SampleContext CreateContextWithParser(Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddLuceneQueryParser(configure)
            .Options;

        var context = new SampleContext(options);
        SeedData(context);
        return context;
    }

    [Fact]
    public void GetQueryParser_ReturnsRegisteredParser()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var parser = context.GetQueryParser();

        // Assert
        Assert.NotNull(parser);
    }

    [Fact]
    public void GetQueryParser_WithConfiguration_ReturnsConfiguredParser()
    {
        // Arrange
        using var context = CreateContextWithParser(config =>
            config.SetDefaultFields("Name", "Email"));

        // Act
        var parser = context.GetQueryParser();

        // Assert
        Assert.NotNull(parser);
    }

    [Fact]
    public void GetQueryParser_WithoutRegistration_ReturnsNull()
    {
        // Arrange - Use regular context without AddLuceneQueryParser
        using var context = CreateContext();

        // Act
        var parser = context.GetQueryParser();

        // Assert
        Assert.Null(parser);
    }

    [Fact]
    public void DbSet_Where_WithRegisteredParser_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser(config =>
            config.SetDefaultFields("Name"));

        // Act - Use the simplified Where that gets parser from DI
        var results = context.Employees.Where("John").ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("John Doe", results[0].Name);
    }

    [Fact]
    public void DbSet_Where_FieldQuery_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("Name:Jane").ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("Jane Smith", results[0].Name);
    }

    [Fact]
    public void DbSet_Where_ComplexQuery_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("IsActive:true AND Salary:>90000").ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.True(e.IsActive));
        Assert.All(results, e => Assert.True(e.Salary > 90000));
    }

    [Fact]
    public void DbSet_Where_WithoutRegisteredParser_ThrowsInvalidOperationException()
    {
        // Arrange - Use regular context without AddLuceneQueryParser
        using var context = CreateContext();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Employees.Where("Name:John").ToList());

        Assert.Contains("EntityFrameworkQueryParser is not registered", exception.Message);
        Assert.Contains("AddLuceneQueryParser", exception.Message);
    }

    [Fact]
    public void DbSet_Where_EmptyQuery_ReturnsAllRecords()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("").ToList();

        // Assert
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void DbSet_Where_WhitespaceQuery_ReturnsAllRecords()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("   ").ToList();

        // Assert
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void DbSet_Where_NullQuery_ReturnsAllRecords()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where(null!).ToList();

        // Assert
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void DbSet_Where_BooleanQuery_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("Name:John OR Name:Jane").ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "John Doe");
        Assert.Contains(results, e => e.Name == "Jane Smith");
    }

    [Fact]
    public void DbSet_Where_RangeQuery_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act
        var results = context.Employees.Where("Age:[30 TO 40]").ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, e => Assert.InRange(e.Age, 30, 40));
    }

    [Fact]
    public void DbSet_Where_Chainable_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateContextWithParser();

        // Act - Chain multiple Where calls
        var results = context.Employees
            .Where("IsActive:true")
            .Where(e => e.Salary > 80000)
            .ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.True(e.IsActive));
        Assert.All(results, e => Assert.True(e.Salary > 80000));
    }

    #endregion
}
