using System.Globalization;

namespace Foundatio.Lucene.Ast;

internal static class Modifiers
{
    public static float? ParseBoost(string? text)
    {
        return text is not null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float boost)
            ? boost
            : null;
    }

    public static string? FormatBoost(float? boost)
    {
        return boost?.ToString("R", CultureInfo.InvariantCulture);
    }

    public static int? ParseInt(string? text)
    {
        return text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    public static string? FormatInt(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}
