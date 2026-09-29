using System.Globalization;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Converts query text to CLR values using the invariant culture. Dates are handled by the expression builder
/// because they depend on date math, the time zone, and whether a value is a lower or upper bound.
/// </summary>
internal static class QueryValueParser
{
    public static bool IsNumeric(Type type)
    {
        return Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
            or TypeCode.Single or TypeCode.Double or TypeCode.Decimal && !type.IsEnum;
    }

    public static bool IsDate(Type type) => type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly);

    /// <summary>
    /// Whether values of <paramref name="type"/> can be parsed by <see cref="TryParse"/>.
    /// </summary>
    public static bool IsSupported(Type type)
    {
        return type == typeof(string) || type == typeof(bool) || type == typeof(char) || type == typeof(Guid)
            || type == typeof(TimeSpan) || type.IsEnum || IsNumeric(type);
    }

    /// <summary>
    /// Parses <paramref name="text"/> as a value of <paramref name="type"/> (a non-nullable type supported by
    /// <see cref="IsSupported"/>).
    /// </summary>
    public static bool TryParse(string text, Type type, out object? value)
    {
        value = null;
        var culture = CultureInfo.InvariantCulture;

        if (type == typeof(string))
        {
            value = text;
            return true;
        }

        if (type.IsEnum)
            return TryParseEnum(text, type, out value);

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                return Box(bool.TryParse(text, out bool b), b, out value);
            case TypeCode.Char:
                return Box(text.Length == 1, text.Length == 1 ? text[0] : default, out value);
            case TypeCode.Byte:
                return Box(byte.TryParse(text, NumberStyles.Integer, culture, out byte u8), u8, out value);
            case TypeCode.SByte:
                return Box(sbyte.TryParse(text, NumberStyles.Integer, culture, out sbyte i8), i8, out value);
            case TypeCode.Int16:
                return Box(short.TryParse(text, NumberStyles.Integer, culture, out short i16), i16, out value);
            case TypeCode.UInt16:
                return Box(ushort.TryParse(text, NumberStyles.Integer, culture, out ushort u16), u16, out value);
            case TypeCode.Int32:
                return Box(int.TryParse(text, NumberStyles.Integer, culture, out int i32), i32, out value);
            case TypeCode.UInt32:
                return Box(uint.TryParse(text, NumberStyles.Integer, culture, out uint u32), u32, out value);
            case TypeCode.Int64:
                return Box(long.TryParse(text, NumberStyles.Integer, culture, out long i64), i64, out value);
            case TypeCode.UInt64:
                return Box(ulong.TryParse(text, NumberStyles.Integer, culture, out ulong u64), u64, out value);
            case TypeCode.Single:
                return Box(float.TryParse(text, NumberStyles.Float, culture, out float f) && float.IsFinite(f), f, out value);
            case TypeCode.Double:
                return Box(double.TryParse(text, NumberStyles.Float, culture, out double d) && double.IsFinite(d), d, out value);
            case TypeCode.Decimal:
                return Box(decimal.TryParse(text, NumberStyles.Float, culture, out decimal m), m, out value);
        }

        if (type == typeof(Guid))
            return Box(Guid.TryParse(text, out var guid), guid, out value);

        if (type == typeof(TimeSpan))
            return Box(TimeSpan.TryParse(text, culture, out var span), span, out value);

        return false;
    }

    public static bool TryParseTime(string text, out TimeOnly value)
    {
        return TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryParseEnum(string text, Type type, out object? value)
    {
        value = null;
        if (text.Length == 0)
            return false;

        if (char.IsAsciiDigit(text[0]) || text[0] == '-')
        {
            var underlying = Enum.GetUnderlyingType(type);
            if (!TryParse(text, underlying, out object? number))
                return false;

            value = Enum.ToObject(type, number!);
            return true;
        }

        if (!Enum.TryParse(type, text, ignoreCase: true, out object? parsed))
            return false;

        bool isFlags = type.IsDefined(typeof(FlagsAttribute), inherit: false);
        if (!isFlags && !Enum.IsDefined(type, parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool Box<T>(bool success, T parsed, out object? value)
    {
        value = success ? parsed : null;
        return success;
    }
}
