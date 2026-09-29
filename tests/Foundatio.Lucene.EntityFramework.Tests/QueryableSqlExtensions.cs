using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Foundatio.Lucene.EntityFramework.Tests;

public static partial class QueryableSqlExtensions
{
    /// <summary>
    /// Returns the SQL for a query with parameter names normalized across EF Core versions
    /// (EF Core 8 names parameters <c>@__Value_0</c>, later versions <c>@Value</c>).
    /// </summary>
    public static string ToSql(this IQueryable query) => Ef8ParameterName().Replace(query.ToQueryString(), "@$1");

    [GeneratedRegex(@"@__([A-Za-z][A-Za-z0-9]*)_\d+")]
    private static partial Regex Ef8ParameterName();
}
