namespace Foundatio.Lucene.Tests;

public class FieldMapTests
{
    [Theory]
    [InlineData("user", "account.user")]
    [InlineData("USER", "account.user")]
    [InlineData("user.name", "account.user.name")]
    [InlineData("data", "resolved")]
    [InlineData("data.a.b", "resolved.a.b")]
    [InlineData("data.exact", "exact.target")]
    [InlineData("data.exact.child", "exact.target.child")]
    [InlineData("unmapped", "unmapped")]
    [InlineData("unmapped.child", "unmapped.child")]
    [InlineData(".data", ".data")]
    public void ResolveField_HierarchicalMode_ResolvesLongestPrefix(string field, string expected)
    {
        Assert.Equal(expected, CreateMap().ResolveField(field));
    }

    [Theory]
    [InlineData("user", "account.user")]
    [InlineData("user.name", "user.name")]
    [InlineData("unmapped", "unmapped")]
    public void ResolveField_DirectMode_ResolvesExactMatchesOnly(string field, string expected)
    {
        var map = CreateMap();
        map.ResolutionMode = FieldResolutionMode.Direct;

        Assert.Equal(expected, map.ResolveField(field));
    }

    [Fact]
    public void ResolveField_ReportUnmappedFields_ReturnsNullForUnmapped()
    {
        var map = CreateMap();
        map.ReportUnmappedFields = true;

        Assert.Null(map.ResolveField("unmapped"));
        Assert.Equal("account.user.x", map.ResolveField("user.x"));
        Assert.Null(map.ResolveField(null));
    }

    [Fact]
    public void ResolveField_ResultPrefix_IsAddedToMappedFieldsOnly()
    {
        var map = CreateMap();
        map.ResultPrefix = "doc.";

        Assert.Equal("doc.account.user", map.ResolveField("user"));
        Assert.Equal("doc.resolved.x", map.ResolveField("data.x"));
        Assert.Equal("unmapped", map.ResolveField("unmapped"));
    }

    [Fact]
    public void Add_Fluent_OverwritesExistingAlias()
    {
        var map = new FieldMap().Add("a", "b").Add("A", "c");

        Assert.Single(map);
        Assert.Equal("c", map.ResolveField("a"));
    }

    [Fact]
    public void Constructor_Dictionary_IsCaseInsensitiveCopy()
    {
        var source = new Dictionary<string, string> { ["Name"] = "full_name" };

        var map = new FieldMap(source);
        source["Name"] = "changed";

        Assert.Equal("full_name", map.ResolveField("name"));
    }

    [Fact]
    public void GetValueOrNull_MissingOrNull_ReturnsNull()
    {
        var map = CreateMap();

        Assert.Equal("account.user", map.GetValueOrNull("user"));
        Assert.Null(map.GetValueOrNull("missing"));
        Assert.Null(map.GetValueOrNull(null));
    }

    [Fact]
    public void Builder_Configuration_BuildsIndependentMap()
    {
        // Arrange
        var builder = FieldMapBuilder.Create()
            .Map("user", "account.user")
            .MapMany("content", "body", "text")
            .MapFrom(new Dictionary<string, string> { ["created"] = "meta.created" })
            .MapNamespace("data", "document.data")
            .UseDirectResolution()
            .ReportUnmapped()
            .WithResultPrefix("p.");

        // Act
        var map = builder.Build();
        builder.Map("late", "added").AllowUnmapped().WithoutResultPrefix().UseHierarchicalResolution();

        // Assert
        Assert.Equal(5, map.Count);
        Assert.Equal("p.content", map.ResolveField("TEXT"));
        Assert.Equal("p.meta.created", map.ResolveField("created"));
        Assert.Null(map.ResolveField("data.x"));
        Assert.Equal(FieldResolutionMode.Direct, map.ResolutionMode);
        Assert.True(map.ReportUnmappedFields);
        Assert.False(map.ContainsKey("late"));
        var rebuilt = builder.Build();
        Assert.Equal("document.data.x", rebuilt.ResolveField("data.x"));
        Assert.Equal("added", rebuilt.ResolveField("late"));
        Assert.Equal("unmapped", rebuilt.ResolveField("unmapped"));
    }

    [Fact]
    public void ToBuilder_ExistingMap_CopiesSettingsWithoutModifyingOriginal()
    {
        // Arrange
        var original = CreateMap();
        original.ResolutionMode = FieldResolutionMode.Direct;
        original.ResultPrefix = "x.";
        original.ReportUnmappedFields = true;

        // Act
        var copy = original.ToBuilder().Map("extra", "value").Build();

        // Assert
        Assert.Equal(original.Count + 1, copy.Count);
        Assert.False(original.ContainsKey("extra"));
        Assert.Equal(FieldResolutionMode.Direct, copy.ResolutionMode);
        Assert.Equal("x.", copy.ResultPrefix);
        Assert.True(copy.ReportUnmappedFields);
    }

    [Fact]
    public void ResolveField_SharedMapConcurrently_ReturnsConsistentResults()
    {
        var map = CreateMap();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 5000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            if (map.ResolveField($"data.f{i}") != $"resolved.f{i}")
                failures.Add(i.ToString());
        });

        Assert.Empty(failures);
    }

    private static FieldMap CreateMap() => new()
    {
        { "user", "account.user" },
        { "data", "resolved" },
        { "data.exact", "exact.target" }
    };
}
