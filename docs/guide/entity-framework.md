# Entity Framework Core

`Foundatio.Lucene.EntityFramework` turns Lucene-syntax queries and sort expressions into LINQ expression trees that Entity Framework Core translates to SQL. Fields come from your EF Core model, values are converted with the invariant culture and sent as SQL parameters, and nothing is evaluated on the client.

```bash
dotnet add package Foundatio.Lucene.EntityFramework
```

## Quick start

```csharp
using Foundatio.Lucene.EntityFramework;

// Create the parser once and share it; it's thread-safe and caches field metadata.
var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name", "Title"));

var employees = await db.Employees
    .Where("Jo* AND salary:[50000 TO *] -status:Terminated", parser)
    .OrderBy("-hiredate name", parser)
    .ToListAsync();
```

`Where` and `OrderBy` read the entity's fields from the model of the query's `DbSet`, so they work on any EF Core query, including ones that are already filtered or projected. A null or blank query leaves the query unchanged.

To build the expression yourself, give the parser the model, either once or per request:

```csharp
var parser = new EntityFrameworkQueryParser(c => c.UseModel(db.Model));
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>("name:\"John Doe\" OR age:>=40");

// or
var filter2 = parser.BuildFilter<Employee>("age:>=40", new EntityFrameworkQueryOptions { Model = db.Model });
```

## Registering the parser with a DbContext

`AddLuceneQuery` associates a parser with a context so `DbSet<T>.Where(query)` can find it:

```csharp
var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"));

services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .AddLuceneQuery(parser));

// later
var results = await db.Employees.Where("department.name:Engineering").ToListAsync();
EntityFrameworkQueryParser? registered = db.GetQueryParser();
```

The parser is stored on the context options, not in EF Core's internal service provider, so any number of contexts and parsers share one internal service provider. Pass a shared parser instance so field metadata is discovered once; `AddLuceneQuery(c => ...)` creates a parser per delegate instance.

## How queries are translated

| Query | LINQ |
|---|---|
| `name:"John Doe"`, `name:John` | `e.Name == value` |
| `name:Jo*` | `e.Name.StartsWith(value)` |
| `name:*oh*` | `e.Name.Contains(value)` |
| `name:J?n*`, `name:*Doe` | `EF.Functions.Like(e.Name, "J_n%", "\\")` |
| `age:30`, `status:Active`, `active:true` | `e.Age == value` (values parsed to the property type) |
| `age:[30 TO 40}`, `age:>=30` | `e.Age >= a && e.Age < b` |
| `company.name:Acme` | `e.Company.Name == value` |
| `projects.name:Apollo` | `e.Projects.Any(p => p.Name == value)` |
| `_exists_:email`, `email:*` | `e.Email != null` |
| `_missing_:email` | `!(e.Email != null)` |
| `_exists_:projects` | `e.Projects.Any()` |
| `*`, `*:*`, empty query | `true` |

Plain terms and phrases on string fields are equality comparisons. Whether they match regardless of case depends on the database collation (SQL Server's default collation is case-insensitive).

Wildcards follow the term as written: `\*` and `\?` are literal characters, and `%`, `_`, `[`, and `\` in the value are escaped in `LIKE` patterns. Wildcards on non-string fields are validation errors.

## Boolean logic

Boolean queries follow Lucene semantics, the same as the Elasticsearch provider: every required clause must match, no prohibited clause may match, and if there are no required clauses at least one optional clause must match.

| Query | Matches |
|---|---|
| `a b`, `a AND b` | `a && b` (the default operator is AND) |
| `a OR b` | `a \|\| b` |
| `-a b` | `!a && b` |
| `a AND NOT b` | `a && !b` |
| `+a b` (default operator OR) | `a` (optional clauses only affect scoring) |
| `a OR -b` | `a && !b` (`-` always prohibits) |
| `a OR NOT b` | `a \|\| !b` |
| `-a -b` | `!a && !b` |
| `a AND b OR c` | `(a && b) \|\| c` (NOT binds tighter than AND, which binds tighter than OR) |

A field applies to everything inside its group, and a field inside the group overrides it: `name:(john OR jane)` searches `name` for both terms, and `company:(name:acme)` searches `name`. See [Query Syntax](./query-syntax#boolean-logic).

## Fields

Field names are dotted paths through the model, matched without regard to case: properties, reference navigations (including owned types), collection navigations, many-to-many skip navigations, complex properties, and primitive collections. Shadow and indexer properties are read with `EF.Property`.

- Collection navigations and primitive collections become `Any`: `employees.projects.name:Zephyr` → `e.Employees.Any(x => x.Projects.Any(p => p.Name == value))`. Each comparison gets its own `Any`, as in an Elasticsearch object (non-nested) field.
- `_exists_` on a collection means it has at least one element; `_exists_:departments.budget` means at least one department has a budget, and `_missing_:departments.budget` means none does.
- A value query on a navigation itself (`company:acme`) is an error.
- Paths can revisit a type (`manager.company.employees.name:x`); they may traverse at most `MaxFieldDepth` (default 10) navigations.

Field metadata is discovered lazily, one type at a time, and cached per parser, so wide or cyclic models cost only what queries use.

### Restricting fields

Only fields discovered from the model after the configured filters, plus [custom fields](#custom-fields) you register, can be queried. Any other name — a typo, a property you filtered out, or a path through an excluded navigation — makes the query invalid, whatever `AllowUnresolvedFields` is set to. Users therefore can't probe columns you didn't expose (for example with prefix or range queries on a password hash):

```csharp
var parser = new EntityFrameworkQueryParser(c => c
    .UseEntityTypePropertyFilter(p => p.Name is not ("PasswordHash" or "ApiKey"))
    .UseEntityTypeNavigationFilter(n => n.Name != nameof(Company.Owner))
    .UseEntityTypeSkipNavigationFilter(n => n.Name != "AuditLogs"));
```

The same rules apply to sorting, to default fields, and to fields reached through aliases, resolvers, and includes. [Validation options](./validation) (`AllowedFields`, `RestrictedFields`, `AllowLeadingWildcards`, operation restrictions) apply as well.

## Default fields

Terms without a field search the default fields, joined with OR:

```csharp
c.SetDefaultFields(["Name", "Title", "Company.Name"], SearchOperator.StartsWith);
```

| `DefaultSearchOperator` | `john` matches |
|---|---|
| `StartsWith` (default) | `Name.StartsWith("john") \|\| ...` |
| `Contains` | `Name.Contains("john") \|\| ...` |
| `Equals` | `Name == "john" \|\| ...` |

The operator applies to plain terms and phrases on string default fields; explicit wildcards (`jo*`, `j?hn`) keep their meaning. Default fields whose type can't hold the value are skipped, so `30` with default fields `Name` and `Age` searches both while `john` searches only `Name`; a value that fits none of them is an error. Default fields may use aliases. A query with fieldless terms and no default fields is an error.

A `SearchTokenizer` can rewrite the term for each default field, for example to normalize phone numbers. The field matches any token, and blank tokens never match:

```csharp
c.UseSearchTokenizer(term =>
{
    if (term.Field.Name != "PhoneNumber")
        return;

    string digits = new([.. term.Term.Where(char.IsAsciiDigit)]);
    term.Tokens = digits.Length > 0 ? [digits] : [];
    term.Operator = SearchOperator.StartsWith;
});
```

## Values and types

Values are parsed to the property type with the invariant culture, whatever the current culture. A value that can't be converted — `age:abc`, `age:99999999999` for an `int`, `id:not-a-guid`, `active:yes` — is a validation error that names the field and the value; it never silently matches nothing.

| Type | Values | Ranges |
|---|---|---|
| `string` | as written | `string.Compare`, which the database evaluates with its collation |
| integers, `decimal`, `double`, `float` | `42`, `-5` (quoted or escaped: `"-5"`), `1.5`, `1e10` | yes |
| `bool` | `true`, `false` | no |
| enums | names (any case) or numbers | by underlying value (not for enums stored as strings) |
| `Guid` | any `Guid` format | no |
| `char` | a single character | yes |
| `DateTime`, `DateTimeOffset`, `DateOnly` | dates, date math | yes |
| `TimeOnly` | `09:30`, `"6:00"`, `now` | yes |
| `TimeSpan` | `01:30:00` | yes |

`field:[* TO *]` matches rows that have a value, like `_exists_`.

## Dates and time zones

Dates follow Elasticsearch: a date written without some components covers the whole period, and date math (`now-7d/d`, `2024-01-01||+1M`) is evaluated in .NET with the configured `TimeProvider` and time zone.

| Query | Matches |
|---|---|
| `created:2024-01-31` | all of January 31 |
| `created:2024-01` | all of January |
| `created:[2024-01-01 TO 2024-02-01}` | January (February 1 excluded) |
| `created:<=2024-01-31` | up to the end of January 31 |
| `created:>2024-01-31` | from February 1 |
| `created:>=now/d` | from midnight today |

Bounds rounded up to the end of a period are compared against the start of the next one (`<= 2024-01-31` becomes `< 2024-02-01`), which is exact for every column precision.

```csharp
c.SetTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));   // now, rounding, dates without an offset
c.SetDateTimeStorageTimeZone(TimeZoneInfo.Utc);                             // what DateTime columns hold (default UTC)
```

- The time zone (default UTC) decides what `now/d` and `2024-01-31` mean. Offsets follow daylight-saving time for each bound. Set it per request with `EntityFrameworkQueryOptions.TimeZone`, or per range with `^`: `created:[2024-01-31 TO 2024-01-31]^"America/Chicago"`.
- `DateTime` columns are assumed to hold UTC values; if they hold local wall-clock times, set `DateTimeStorageTimeZone`. `DateTimeOffset` columns are compared as instants.
- `DateOnly` columns compare against the calendar date of each (rounded) bound in the query's time zone: `birthday:now-1d` is yesterday, and `birthday:[now-7d TO now]` covers the dates from seven days ago through today.

## Full-text search

Fields with a SQL Server full-text index can be searched with `CONTAINS`:

```csharp
c.AddFullTextFields("Employee.Name", "Title", "Company.Name");
```

An entry is either a field path from the queried entity (`Title`, `Company.Name`) or a declaring type and property (`Employee.Name`), which applies wherever that property is reached.

| Query on a full-text field | Search condition |
|---|---|
| `name:john`, fieldless `john` with `Equals` | `CONTAINS(Name, '"john"')` |
| `name:jo*`, fieldless `jo` with `StartsWith` or `Contains` | `CONTAINS(Name, '"jo*"')` |
| `title:"software developer"` | `CONTAINS(Title, '"software developer"')` |
| `name:*oh*`, `name:j?hn` | `LIKE` on the column (full-text search has no infix wildcards) |

The search condition is always a single quoted phrase. Double quotes and `*` in the user's text become word separators, so the text can't close the phrase or add operators like `OR` or `NEAR`. Full-text search requires the `Microsoft.EntityFrameworkCore.SqlServer` provider and a full-text index on the column.

## Regular expressions

Regular expression queries (`name:/jo.*/`) are rejected by default: most databases can't translate them, and evaluating them in .NET risks catastrophic backtracking. If your database supports them, supply the translation:

```csharp
// For example with a provider that translates Regex.IsMatch (such as Npgsql).
var isMatch = typeof(Regex).GetMethod(nameof(Regex.IsMatch), [typeof(string), typeof(string)])!;
c.UseRegex((field, pattern) => Expression.Call(isMatch, field, pattern));
```

## Custom fields

Dynamic fields (for example an entity-attribute-value table) are registered as custom fields and translated by a `CustomFieldExpressionBuilder`. The builder is called for every field comparison; return null to use the default translation.

```csharp
// age:30 → c.DataValues.Any(dv => dv.DataDefinitionId == 1 && dv.IntegerValue == 30)
static Expression? DataValueBuilder(CustomFieldContext context)
{
    if (context.Field.Data.GetValueOrDefault("DataDefinitionId") is not int definitionId
        || context.Field.Data["Column"] is not string column)
        return null;

    var row = Expression.Parameter(typeof(DataValue), "dv");
    var body = Expression.AndAlso(
        Expression.Equal(Expression.Property(row, nameof(DataValue.DataDefinitionId)), context.Parameterize(definitionId)),
        context.BuildDefault(Expression.Property(row, column)));

    return context.Any(Expression.Property(context.Instance, nameof(Contact.DataValues)), Expression.Lambda(body, row));
}

var parser = new EntityFrameworkQueryParser(c => c.UseCustomFieldExpressionBuilder(DataValueBuilder));

// The fields differ per tenant, so they're passed as (cacheable) options.
var tenantOptions = new EntityFrameworkQueryOptions
{
    AdditionalFields =
    [
        new EntityFieldInfo { Name = "age", ClrType = typeof(int), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 1, ["Column"] = "IntegerValue" } },
        new EntityFieldInfo { Name = "city", ClrType = typeof(string), Data = new Dictionary<string, object?> { ["DataDefinitionId"] = 2, ["Column"] = "StringValue" } }
    ]
};

var contacts = await db.Contacts.Where("age:>=30 AND city:New*", parser, tenantOptions).ToListAsync();
```

`BuildDefault(member)` applies the query node to any member — terms, phrases, wildcards, ranges, dates, and `_exists_` — using the member's type to convert values, so a custom field supports the whole syntax. `Parameterize` supplies values as SQL parameters. The builder receives the node (`context.Node`) and the object that declares the field (`context.Instance`); negation and boolean logic are applied around what it returns. A custom field with the same name as a model field replaces it.

A visitor can also replace the predicate for any node, which is handy for special syntax:

```csharp
public class TeamVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field != "@team" || node.Query is not TermNode term)
            return base.Visit(node, context);

        string team = term.UnescapedTerm;
        Expression<Func<Employee, bool>> filter = e => e.Projects.Any(p => p.Name == team);
        node.SetFilterExpression(filter);
        return node;
    }
}

c.AddVisitor(new TeamVisitor());   // then "@team:apollo -name:bob"
```

## Sorting

```csharp
var sorted = db.Employees.OrderBy("-salary company.name +name", parser);

// or build once and apply to several queries
EntityFrameworkSort<Employee> sort = parser.BuildSort<Employee>("hiredate:desc name", new EntityFrameworkQueryOptions { Model = db.Model });
IOrderedQueryable<Employee> page = sort.Apply(db.Employees.Where("active:true", parser)).ThenBy(e => e.Id);
```

`-field` sorts descending and `field` or `+field` ascending; `field:desc` and `field:asc` are explicit (see [Sorting and Aggregations](./sorting-and-aggregations)). Sort fields must be scalar fields reached through reference navigations only; collections, navigations, custom fields, and unknown fields are errors. `sort.Fields` lists the fields with their model paths.

## Per-request options and tenants

The parser configuration is fixed when the parser is created. Settings that vary by request, user, or tenant go in an immutable `EntityFrameworkQueryOptions` record, which can be cached and shared:

```csharp
var options = EntityFrameworkQueryOptions.CreateBuilder()
    .WithFieldMap(m => m.Map("who", "Name").Map("org", "Company.Name"))
    .WithDefaultFields("Name", "Title")
    .WithDefaultSearchOperator(SearchOperator.Contains)
    .WithTimeZone(userTimeZone)
    .WithValidationOptions(v => v.AllowLeadingWildcards = false)
    .WithIntField("age", new Dictionary<string, object?> { ["DataDefinitionId"] = 1, ["Column"] = "IntegerValue" })
    .Build();

var results = await db.Employees.Where(userQuery, parser, options).ToListAsync();
```

Options can also be registered per entity type with `parser.SetOptions<Employee>(...)`. Per-request options win over registered options, which win over the configuration; registered and per-request `AdditionalFields` are combined. The per-request state of each call lives in its own context, so one parser can serve every tenant concurrently.

## Asynchronous resolvers

Include resolvers and asynchronous field resolvers run in a resolution phase before the query is built, so use the `Async` methods with them:

```csharp
var parser = new EntityFrameworkQueryParser(c => c.IncludeResolver = (name, context, ct) => savedQueries.GetAsync(name, ct));

var query = await db.Employees.WhereAsync("@include:engineers -status:Terminated", parser, cancellationToken: ct);
var filter = await parser.BuildFilterAsync<Employee>("@include:engineers", new() { Model = db.Model }, ct);
```

The synchronous methods throw `InvalidOperationException` when asynchronous resolvers are configured.

## Validation and errors

```csharp
QueryValidationResult result = parser.ValidateQuery<Employee>("nope:1 AND age:old", options);
// result.IsValid == false; result.ValidationErrors has an UnresolvedField and a TypeConversionError error, each with a position

QueryValidationResult sortResult = parser.ValidateSort<Employee>("-salary nope", options);

QueryResult<Expression<Func<Employee, bool>>> built = parser.TryBuildFilter<Employee>(userQuery, options);
if (!built.IsSuccess)
    return BadRequest(built.ErrorMessage);
```

Building throws `QueryParseException` for syntax errors and `QueryValidationException` for invalid queries; `ValidateQuery` reports the same problems without throwing (unless `ShouldThrow` is set). Validation includes everything the build checks: unknown fields, values that don't fit the field type, and unsupported syntax.

These are errors because SQL can't express them: fuzzy terms (`john~`), phrase proximity (`"a b"~2`), boosts (`name:john^2`), wildcard field names (`name*:x`), and, by default, regular expressions.

## Parameters and query plans

Every value is supplied through a parameter (`DECLARE @Value ...` in `ToQueryString()`), never inlined as a SQL literal. Queries that differ only in their values share one compiled query and one database query plan, and values can't change the SQL.

## Differences from Foundatio.Parsers SqlQueries

- Expression trees instead of Dynamic LINQ strings, so there's no expression injection and no `FTS.Contains` function to map.
- Unknown fields and values that don't fit the field type are errors instead of passing through or producing empty output.
- Lucene boolean semantics and operator precedence (see [Migrating from Foundatio.Parsers](./migrating-from-parsers#behavior-changes)): `a OR -b` means a and not b.
- Field groups (`name:(a OR b)`), open-ended ranges (`[* TO 5]`), rounding (`now/d`), and date rounding (`>2024-01-01` starts on January 2) work.
- Terms on full-text fields use `CONTAINS` whether or not they have a field; `Contains` searches on full-text fields use the prefix term `"john*"` (a leading `*` never matched anything more); advanced wildcards on full-text fields fall back to `LIKE` instead of being rejected.
- Sorting is supported.
- Paths may revisit an entity type (`manager.manager.name`), bounded by `MaxFieldDepth`.
