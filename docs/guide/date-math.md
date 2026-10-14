# Date Math

Date values in queries can be relative to the current time or to another date, using Elasticsearch date math.

```
created:[now-7d TO now]
created:>=now/d
created:[2024-01-01||+1M/d TO *]
```

## Syntax

An expression is an **anchor**, an optional `||`, and zero or more **operations**.

| Anchor | Meaning |
|---|---|
| `now` | the current instant |
| `2024-01-15`, `2024-01`, `2024`, `20240115` | a date (in the time zone, if no offset is given) |
| `2024-01-15T10:30:00`, `2024-01-15T10:30:00.123Z`, `2024-01-15T10:30:00+05:00` | a date and time |

| Operation | Meaning |
|---|---|
| `+1d`, `-2h`, `+d` | add or subtract an amount (default 1) |
| `/d` | round to the unit; must be the last operation |

| Unit | |
|---|---|
| `y` | years |
| `M` | months |
| `w` | weeks |
| `d` | days |
| `h`, `H` | hours |
| `m` | minutes |
| `s` | seconds |

Units are case-sensitive: `M` is months and `m` minutes.

```
now-1h          one hour ago
now/d           start of today
now-1d/d        start of yesterday
now/M           start of this month
now+1w/w        start of next week (weeks start on Monday)
2024-01-01||+1M the first of February 2024
```

## Rounding and ranges

Rounding goes down to the start of the period, except for bounds that must include the whole period, which round up to its last instant — the same rules Elasticsearch uses:

| Bound | Rounds |
|---|---|
| `[now/d TO ...` (inclusive lower) | down: start of today |
| `{now/d TO ...` (exclusive lower) | up: end of today |
| `... TO now/d]` (inclusive upper) | up: end of today |
| `... TO now/d}` (exclusive upper) | down: start of today |

Dates that leave out components are rounded the same way, so `created:[2024-01 TO 2024-02]` covers all of January and February, and `created:<=2024-01-31` includes the whole day.

## Time zones

`now/d` means midnight in some time zone. Configure it:

```csharp
var parser = new ElasticsearchQueryParser(c => c.DefaultTimeZone = "America/Chicago");
```

or per range with `^`: `created:[now/d TO *]^"America/Chicago"`. Days, weeks, months, and years are calendar arithmetic in the time zone (so `now+1d` keeps the local time across a daylight-saving change), while hours, minutes, and seconds are exact durations. Dates without an offset use the offset in effect on that date.

## Where date math is evaluated

- **Elasticsearch** evaluates date math itself. The provider passes expressions through unchanged and sets `time_zone` on date ranges.
- **Entity Framework** evaluates date math in .NET using the configured `TimeProvider` and time zone, with the rounding rules above.
- Anywhere else, use `DateMath` or `DateMathEvaluatorVisitor`.

## Evaluating date math yourself

```csharp
DateTimeOffset start = DateMath.Parse("now-7d/d", DateTimeOffset.UtcNow);

var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
DateTimeOffset startOfDay = DateMath.Parse("now/d", DateTimeOffset.UtcNow, chicago);
DateTimeOffset endOfDay = DateMath.Parse("now/d", DateTimeOffset.UtcNow, chicago, isUpperLimit: true);

if (DateMath.TryParse("2024-01-01||+1M", DateTimeOffset.UtcNow, isUpperLimit: false, out var date))
    Console.WriteLine(date);
```

`DateMathEvaluatorVisitor` replaces date math in a parsed query with absolute dates. It reads the clock once per query, so every `now` in a query is the same instant:

```csharp
var visitor = new DateMathEvaluatorVisitor(timeProvider, chicago, isDateField: field => field is "created" or "updated");
var document = (QueryDocument)visitor.Accept(LuceneQuery.Parse("created:[now-1d/d TO now]").Document, new QueryVisitorContext());
```

## Testing

Inject a `TimeProvider` (every parser configuration has a `TimeProvider` property, and `DateMathEvaluatorVisitor` takes one) so date math is deterministic:

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero));
var visitor = new DateMathEvaluatorVisitor(time);
var document = visitor.Accept(LuceneQuery.Parse("created:>=now/d").Document, new QueryVisitorContext());
// created:>=2024-03-15T00:00:00.000+00:00
```
