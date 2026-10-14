using System.Linq.Expressions;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.EntityFramework.Tests;

/// <summary>
/// Dynamic (entity-attribute-value) fields: custom fields carry the data definition in <see cref="EntityFieldInfo.Data"/>
/// and a <see cref="CustomFieldExpressionBuilder"/> applies the query to the matching value column.
/// </summary>
public class CustomFieldTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();

    public CustomFieldTests()
    {
        var john = new Contact { Name = "John" };
        var jane = new Contact { Name = "Jane" };
        var bob = new Contact { Name = "Bob" };
        var empty = new Contact { Name = "Empty" };
        _db.Contacts.AddRange(john, jane, bob, empty);
        _db.DataValues.AddRange(
            new DataValue { Contact = john, DataDefinitionId = 1, IntegerValue = 30 },
            new DataValue { Contact = john, DataDefinitionId = 2, StringValue = "New York" },
            new DataValue { Contact = john, DataDefinitionId = 3, DecimalValue = 9.5m },
            new DataValue { Contact = john, DataDefinitionId = 4, DateValue = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
            new DataValue { Contact = john, DataDefinitionId = 5, BooleanValue = true },
            new DataValue { Contact = jane, DataDefinitionId = 1, IntegerValue = 25 },
            new DataValue { Contact = jane, DataDefinitionId = 2, StringValue = "Boston" },
            new DataValue { Contact = jane, DataDefinitionId = 3, DecimalValue = 7.25m },
            new DataValue { Contact = jane, DataDefinitionId = 5, BooleanValue = false },
            new DataValue { Contact = bob, DataDefinitionId = 1, IntegerValue = 40 },
            new DataValue { Contact = bob, DataDefinitionId = 4, DateValue = new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc) });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static readonly EntityFieldInfo[] Fields =
    [
        Field("age", typeof(int), 1, nameof(DataValue.IntegerValue)),
        Field("city", typeof(string), 2, nameof(DataValue.StringValue)),
        Field("score", typeof(decimal), 3, nameof(DataValue.DecimalValue)),
        Field("joined", typeof(DateTime), 4, nameof(DataValue.DateValue)),
        Field("vip", typeof(bool), 5, nameof(DataValue.BooleanValue))
    ];

    private static EntityFieldInfo Field(string name, Type type, int definitionId, string column)
    {
        return new EntityFieldInfo
        {
            Name = name,
            ClrType = type,
            Data = new Dictionary<string, object?> { ["DataDefinitionId"] = definitionId, ["Column"] = column }
        };
    }

    /// <summary>
    /// <c>c.DataValues.Any(dv =&gt; dv.DataDefinitionId == id &amp;&amp; &lt;query applied to dv.Column&gt;)</c>.
    /// </summary>
    internal static Expression? DataValueBuilder(CustomFieldContext context)
    {
        if (context.Field.Data.GetValueOrDefault("DataDefinitionId") is not int definitionId || context.Field.Data["Column"] is not string column)
            return null;

        // A missing value means no row has a value, so it negates the whole Any instead of the column test.
        bool missing = context.Node is MissingNode;
        var row = Expression.Parameter(typeof(DataValue), "dv");
        var member = Expression.Property(row, column);
        var predicate = missing ? Expression.NotEqual(member, Expression.Constant(null, member.Type)) : context.BuildDefault(member);
        var body = Expression.AndAlso(
            Expression.Equal(Expression.Property(row, nameof(DataValue.DataDefinitionId)), context.Parameterize(definitionId)),
            predicate);

        var any = context.Any(Expression.Property(context.Instance, nameof(Contact.DataValues)), Expression.Lambda(body, row));
        return missing ? Expression.Not(any) : any;
    }

    private EntityFrameworkQueryParser CreateParser() => new(c => c.UseCustomFieldExpressionBuilder(DataValueBuilder));

    private EntityFrameworkQueryOptions Options => new() { AdditionalFields = Fields };

    private string[] Names(string query, EntityFrameworkQueryParser? parser = null, EntityFrameworkQueryOptions? options = null)
    {
        return [.. _db.Contacts.Where(query, parser ?? CreateParser(), options ?? Options).Select(c => c.Name).AsEnumerable().Order()];
    }

    [Theory]
    [InlineData("age:30", new[] { "John" })]
    [InlineData("age:>=30", new[] { "Bob", "John" })]
    [InlineData("age:[* TO 30}", new[] { "Jane" })]
    [InlineData("-age:30", new[] { "Bob", "Empty", "Jane" })]
    [InlineData("city:\"New York\"", new[] { "John" })]
    [InlineData("city:B*", new[] { "Jane" })]
    [InlineData("city:*o*", new[] { "Jane", "John" })]
    [InlineData("score:>8", new[] { "John" })]
    [InlineData("joined:2024", new[] { "John" })]
    [InlineData("joined:<2024-01-01", new[] { "Bob" })]
    [InlineData("vip:true", new[] { "John" })]
    [InlineData("_exists_:city", new[] { "Jane", "John" })]
    [InlineData("_missing_:city", new[] { "Bob", "Empty" })]
    [InlineData("age:>20 AND -vip:false AND name:J*", new[] { "John" })]
    public void BuildFilter_WithCustomFields_AppliesQueryToValueColumn(string query, string[] expected)
    {
        Assert.Equal(expected, Names(query));
    }

    [Fact]
    public void BuildFilter_WithRegisteredCustomFields_UsesThemForEveryRequest()
    {
        var parser = CreateParser();
        parser.SetOptions<Contact>(o => o.WithAdditionalFields(Fields));

        Assert.Equal(["John"], [.. _db.Contacts.Where("age:30", parser).Select(c => c.Name)]);
    }

    [Fact]
    public void BuildFilter_WithTenantSpecificDefinitions_KeepsTenantsIsolated()
    {
        var parser = CreateParser();
        var tenantA = new EntityFrameworkQueryOptions { AdditionalFields = [Field("rating", typeof(int), 1, nameof(DataValue.IntegerValue))] };
        var tenantB = new EntityFrameworkQueryOptions { AdditionalFields = [Field("rating", typeof(decimal), 3, nameof(DataValue.DecimalValue))] };

        Assert.Equal(["Bob"], Names("rating:40", parser, tenantA));
        Assert.Empty(Names("rating:40", parser, tenantB));
        Assert.Equal(["Jane"], Names("rating:7.25", parser, tenantB));
        Assert.Throws<QueryValidationException>(() => Names("rating:1", parser, EntityFrameworkQueryOptions.Empty));
    }

    [Fact]
    public void BuildFilter_WithCustomFieldAsDefaultField_PassesSearchOperator()
    {
        var options = Options with { DefaultFields = ["Name", "city"] };

        Assert.Equal(["Jane"], Names("Bos", options: options));
        Assert.Equal(["John"], Names("Jo", options: options));
    }

    [Fact]
    public void BuildFilter_WithInvalidCustomFieldValue_ThrowsValidationError()
    {
        var ex = Assert.Throws<QueryValidationException>(() => Names("age:old"));

        Assert.Contains("(old)", ex.Message);
        Assert.Contains("(age)", ex.Message);
    }

    [Fact]
    public void BuildFilter_WithCustomFieldAndNoBuilder_ThrowsValidationError()
    {
        var ex = Assert.Throws<QueryValidationException>(() => Names("age:30", new EntityFrameworkQueryParser()));

        Assert.Contains("custom field", ex.Message);
    }

    [Fact]
    public void BuildFilter_WithBuilderReturningNull_UsesDefaultExpressionForModelFields()
    {
        int calls = 0;
        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(context =>
        {
            calls++;
            return DataValueBuilder(context);
        }));

        Assert.Equal(["Jane"], Names("name:Jane", parser));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BuildFilter_WithBuilderReturningNonBoolean_ThrowsValidationError()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(_ => Expression.Constant(1)));

        Assert.Throws<QueryValidationException>(() => Names("name:Jane", parser));
    }

    [Fact]
    public void BuildFilter_WithPerRequestBuilder_OverridesConfiguredBuilder()
    {
        var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(_ => null));

        Assert.Equal(["John"], Names("age:30", parser, Options with { CustomFieldExpressionBuilder = DataValueBuilder }));
    }
}
