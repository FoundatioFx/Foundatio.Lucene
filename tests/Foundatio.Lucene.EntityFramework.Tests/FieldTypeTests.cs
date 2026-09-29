using System.Globalization;

namespace Foundatio.Lucene.EntityFramework.Tests;

public class FieldTypeTests : IDisposable
{
    private readonly SampleContext _db = SampleData.CreateInMemory();
    private readonly EntityFrameworkQueryParser _parser = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("text:alpha", new[] { 1 })]
    [InlineData("Text:ALPHA", new int[0])]
    [InlineData("int:20", new[] { 2 })]
    [InlineData("nullableint:15", new[] { 3 })]
    [InlineData("long:10000000000", new[] { 1 })]
    [InlineData("short:\"-5\"", new[] { 1 })]
    [InlineData(@"short:\-5", new[] { 1 })]
    [InlineData("byte:200", new[] { 1 })]
    [InlineData("decimal:12.5", new[] { 1 })]
    [InlineData("decimal:12.50", new[] { 1 })]
    [InlineData("double:1.5", new[] { 1 })]
    [InlineData("double:1e10", new[] { 3 })]
    [InlineData("float:2.5", new[] { 1 })]
    [InlineData("bool:false", new[] { 2 })]
    [InlineData("bool:TRUE", new[] { 1, 3 })]
    [InlineData("nullablebool:true", new[] { 1 })]
    [InlineData("guid:22222222-2222-2222-2222-222222222222", new[] { 2 })]
    [InlineData("guid:\"{33333333-3333-3333-3333-333333333333}\"", new[] { 3 })]
    [InlineData("char:z", new[] { 3 })]
    [InlineData("enum:Terminated", new[] { 2 })]
    [InlineData("enum:onleave", new[] { 3 })]
    [InlineData("enum:2", new[] { 2 })]
    [InlineData("nullableenum:Active", new[] { 3 })]
    [InlineData("timeonly:17:30", new[] { 2 })]
    [InlineData("timeonly:\"9:00\"", new[] { 1 })]
    [InlineData("timespan:08:00:00", new[] { 2 })]
    [InlineData("timespan:\"01:30:00\"", new[] { 1 })]
    public void BuildFilter_WithTypedValue_MatchesEqualValues(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("int:[10 TO 20]", new[] { 1, 2 })]
    [InlineData("int:{10 TO 30}", new[] { 2 })]
    [InlineData("int:[10 TO 30}", new[] { 1, 2 })]
    [InlineData("int:>=20", new[] { 2, 3 })]
    [InlineData("int:>20", new[] { 3 })]
    [InlineData("int:<20", new[] { 1 })]
    [InlineData("int:<=20", new[] { 1, 2 })]
    [InlineData("int:[20 TO *]", new[] { 2, 3 })]
    [InlineData("int:[* TO 20}", new[] { 1 })]
    [InlineData("int:(>=15 AND <25)", new[] { 2 })]
    [InlineData("nullableint:[* TO *]", new[] { 1, 3 })]
    [InlineData("nullableint:>=5", new[] { 1, 3 })]
    [InlineData("long:<0", new[] { 2 })]
    [InlineData("short:[-10 TO 0]", new[] { 1, 3 })]
    [InlineData("byte:>100", new[] { 1 })]
    [InlineData("decimal:>50", new[] { 2 })]
    [InlineData("decimal:[0.001 TO 12.5]", new[] { 1, 3 })]
    [InlineData("double:[-5 TO 2]", new[] { 1, 2 })]
    [InlineData("float:<1", new[] { 2, 3 })]
    [InlineData("text:[alpha TO beta]", new[] { 1, 2 })]
    [InlineData("text:>alpha", new[] { 2 })]
    [InlineData("char:[a TO b]", new[] { 1, 2 })]
    [InlineData("enum:[OnLeave TO Terminated]", new[] { 2, 3 })]
    [InlineData("enum:>Active", new[] { 2, 3 })]
    [InlineData("nullableenum:<=OnLeave", new[] { 1, 3 })]
    [InlineData("timeonly:[09:00 TO 18:00]", new[] { 1, 2 })]
    [InlineData("timeonly:>12:00", new[] { 2, 3 })]
    [InlineData("timespan:<01:00:00", new[] { 3 })]
    public void BuildFilter_WithTypedRange_MatchesValuesInRange(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("_exists_:text", new[] { 1, 2 })]
    [InlineData("text:*", new[] { 1, 2 })]
    [InlineData("_missing_:text", new[] { 3 })]
    [InlineData("_exists_:nullableint", new[] { 1, 3 })]
    [InlineData("_missing_:nullableint", new[] { 2 })]
    [InlineData("_missing_:nullableenum", new[] { 2 })]
    [InlineData("_exists_:int", new[] { 1, 2, 3 })]
    [InlineData("_missing_:int", new int[0])]
    [InlineData("_exists_:bytes", new[] { 1 })]
    [InlineData("NOT _exists_:nullabledatetime", new[] { 1 })]
    public void BuildFilter_WithExistsOrMissing_ChecksForValues(string query, int[] expected)
    {
        Assert.Equal(expected, _db.TypeSamples.Where(query, _parser).Ids());
    }

    [Theory]
    [InlineData("int:abc", "abc", "Int")]
    [InlineData("int:99999999999", "99999999999", "Int")]
    [InlineData("byte:256", "256", "Byte")]
    [InlineData("decimal:1,5", "1,5", "Decimal")]
    [InlineData("double:NaN", "NaN", "Double")]
    [InlineData("guid:not-a-guid", "not-a-guid", "Guid")]
    [InlineData("bool:yes", "yes", "Bool")]
    [InlineData("char:ab", "ab", "Char")]
    [InlineData("enum:Missing", "Missing", "Enum")]
    [InlineData("timeonly:25:00", "25:00", "TimeOnly")]
    [InlineData("timespan:soon", "soon", "TimeSpan")]
    [InlineData("int:[a TO 5]", "a", "Int")]
    [InlineData("datetime:notadate", "notadate", "DateTime")]
    [InlineData("dateonly:[2024-13-01 TO *]", "2024-13-01", "DateOnly")]
    public void BuildFilter_WithUnparseableValue_ThrowsValidationErrorNamingFieldAndValue(string query, string value, string field)
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.TypeSamples.Where(query, _parser));

        var error = Assert.Single(ex.Errors);
        Assert.Equal(QueryErrorCode.TypeConversionError, error.Code);
        Assert.Contains($"({value})", error.Message);
        Assert.Contains($"({field})", error.Message);
        Assert.True(error.Position > 0);
    }

    [Fact]
    public void BuildFilter_WithUnparseableValue_ValidateReportsSameError()
    {
        var result = _parser.ValidateQuery<TypeSample>("int:abc OR bool:maybe", new EntityFrameworkQueryOptions { Model = _db.Model });

        Assert.False(result.IsValid);
        Assert.Equal(2, result.ValidationErrors.Count);
        Assert.All(result.ValidationErrors, e => Assert.Equal(QueryErrorCode.TypeConversionError, e.Code));
    }

    [Theory]
    [InlineData("bytes:abc", "does not support value queries")]
    [InlineData("bool:[false TO true]", "Range queries are not supported")]
    [InlineData("guid:[a TO b]", "Range queries are not supported")]
    [InlineData("int:1*", "require a string field")]
    [InlineData("int:1?", "require a string field")]
    [InlineData("datetime:2024*", "require a string field")]
    [InlineData("int:[1 TO 5]^\"America/Chicago\"", "only applies to date fields")]
    public void BuildFilter_WithUnsupportedOperationForType_ThrowsValidationError(string query, string message)
    {
        var ex = Assert.Throws<QueryValidationException>(() => _db.TypeSamples.Where(query, _parser));

        Assert.Contains(message, Assert.Single(ex.Errors).Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void BuildFilter_UnderAnyCulture_ParsesValuesWithInvariantCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal([1], _db.TypeSamples.Where("decimal:12.5", _parser).Ids());
            Assert.Equal([1], _db.TypeSamples.Where("double:1.5 AND float:2.5", _parser).Ids());
            Assert.Equal([2], _db.TypeSamples.Where("datetime:[2024-01-31 TO 2024-01-31]", _parser).Ids());
            Assert.Equal([1, 3], _db.TypeSamples.Where("bool:TRUE", _parser).Ids());
            Assert.Throws<QueryValidationException>(() => _db.TypeSamples.Where("decimal:12,5", _parser));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
