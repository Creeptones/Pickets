namespace Pickets.Tests;

public sealed class LayoutStoreTests
{
    [Fact]
    public void ParseWithMigration_ConvertsLegacyFencesToProfileSeed()
    {
        const string json = """
            {
              "Version": 1,
              "Fences": [
                { "Title": "Legacy", "Items": [] }
              ]
            }
            """;

        var layout = LayoutStore.ParseWithMigration(json);

        Assert.NotNull(layout);
        Assert.Equal(2, layout.Version);
        Assert.Equal("Legacy", Assert.Single(layout.Profiles["_legacy"]).Title);
        Assert.Same(layout.Profiles["_legacy"], layout.LastProfileSeed);
    }

    [Fact]
    public void Normalize_RepairsInvalidPersistedValues()
    {
        var layout = new LayoutFile
        {
            DefaultColorKey = "black",
            Profiles = new()
            {
                ["display"] =
                [
                    new PicketState
                    {
                        Id = "",
                        Title = "",
                        ColorKey = "sky",
                        TransparencyKey = "invalid",
                        TransparencyCustomPercent = 150,
                        Items = [new ItemState { Path = "" }],
                    },
                ],
            },
        };

        LayoutStore.Normalize(layout);

        var state = Assert.Single(layout.Profiles["display"]);
        Assert.False(string.IsNullOrWhiteSpace(state.Id));
        Assert.Equal("Picket", state.Title);
        Assert.Equal("ocean", state.ColorKey);
        Assert.Equal("solid", state.TransparencyKey);
        Assert.Equal(100, state.TransparencyCustomPercent);
        Assert.Empty(state.Items);
        Assert.Equal("graphite", layout.DefaultColorKey);
    }

    [Theory]
    [InlineData("white", "porcelain")]
    [InlineData("teal", "ocean")]
    [InlineData("black", "graphite")]
    [InlineData("not-a-theme", "porcelain")]
    public void LegacyThemeAliasesResolve(string legacy, string expected)
        => Assert.Equal(expected, PicketColors.Get(legacy).Key);
}
