using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Normalizes time zone modifiers. Offsets written as time units (<c>1h</c>, <c>-5h</c>, <c>+330m</c>) become
/// <c>±hh:mm</c>; anything else (<c>America/Chicago</c>, <c>UTC</c>, <c>-05:00</c>) is passed through.
/// </summary>
internal static partial class ElasticsearchTimeZone
{
    [GeneratedRegex(@"^(?<sign>[+-]?)(?<amount>\d{1,4})(?<unit>[smhd])$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeUnitRegex();

    public static string? Normalize(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            return null;

        var match = TimeUnitRegex().Match(timeZone);
        if (!match.Success)
            return timeZone;

        int amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        var offset = match.Groups["unit"].Value switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount)
        };

        if (offset > TimeSpan.FromHours(18))
            return timeZone;

        string sign = match.Groups["sign"].Value == "-" ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{offset.Hours:00}:{offset.Minutes:00}");
    }
}
