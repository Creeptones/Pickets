using System.Text.Json;

namespace Pickets.Tests;

public sealed class LayoutRecoveryValidationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"Version\":2}")]
    [InlineData("{\"Version\":2,\"Profiles\":null}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":null}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[null]}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[{\"Items\":null}]}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[{\"Items\":[null]}]}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[{\"Items\":[{}]}]}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[{\"Items\":[{\"Path\":\"file\",\"OriginalX\":1}]}]}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{},\"LastProfileSeed\":[null]}")]
    [InlineData("{\"Version\":999,\"Profiles\":{}}")]
    [InlineData("{\"Version\":0,\"Fences\":[]}")]
    [InlineData("{\"Version\":null,\"Fences\":[]}")]
    [InlineData("{\"Version\":\"2\",\"Profiles\":{}}")]
    [InlineData("{\"Version\":1,\"Fences\":null}")]
    public void InvalidPrimary_UsesBackupAndNeverTreatsUnreadableStateAsEmpty(string json)
    {
        var directory = Directory.CreateTempSubdirectory("PicketsSchemaTests-");
        try
        {
            var primary = Path.Combine(directory.FullName, "layout.json");
            var backup = primary + ".bak";
            var good = JsonSerializer.Serialize(new LayoutFile
            {
                Profiles = new()
                {
                    ["display"] = [new PicketState
                    {
                        Items = [new ItemState { Path = @"C:\Desktop\Example.lnk", OriginalX = 10, OriginalY = 20 }]
                    }]
                }
            });
            File.WriteAllText(primary, json);
            File.WriteAllText(backup, good);

            var recovered = LayoutStore.LoadForRecovery(primary, backup);
            Assert.NotNull(recovered);
            Assert.Single(IconCaptureRecovery.Collect(recovered, "display"));
            var startup = LayoutStore.Load(primary, backup);
            Assert.NotNull(startup);
            Assert.Single(IconCaptureRecovery.Collect(startup, "display"));
            Assert.Equal(json, File.ReadAllText(primary));
            Assert.Equal(good, File.ReadAllText(backup));

            File.WriteAllText(backup, json);
            Assert.Null(LayoutStore.LoadForRecovery(primary, backup));
            Assert.Null(LayoutStore.Load(primary, backup));
            File.Delete(backup);
            Assert.Null(LayoutStore.LoadForRecovery(primary, backup));
            Assert.Null(LayoutStore.Load(primary, backup));
            Assert.Equal(json, File.ReadAllText(primary));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("{\"Version\":2,\"Profiles\":{}}")]
    [InlineData("{\"Version\":2,\"Profiles\":{\"display\":[]},\"LastProfileSeed\":null}")]
    [InlineData("{\"Version\":1,\"Fences\":[]}")]
    [InlineData("{\"Fences\":[]}")]
    public void ExplicitEmptySupportedLayout_RemainsValid(string json)
        => Assert.NotNull(LayoutStore.ParseWithMigration(json));

    [Fact]
    public void MissingFiles_AllowFirstRunAndEmptyRecovery()
    {
        var directory = Directory.CreateTempSubdirectory("PicketsFirstRunTests-");
        try
        {
            var primary = Path.Combine(directory.FullName, "layout.json");
            Assert.NotNull(LayoutStore.LoadForRecovery(primary, primary + ".bak"));
            Assert.NotNull(LayoutStore.Load(primary, primary + ".bak"));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void SerializedLayout_WithLabelsAndOrdinaryReferences_RoundTrips()
    {
        var layout = new LayoutFile
        {
            Profiles = new()
            {
                ["display"] = [new PicketState
                {
                    Items = [new ItemState { Kind = ItemKind.Label, LabelText = "Work" },
                        new ItemState { Path = @"D:\File.txt" },
                        new ItemState { Path = @"C:\Desktop\Example.lnk", OriginalX = 10, OriginalY = 20 }]
                }]
            }
        };
        var restored = LayoutStore.ParseWithMigration(JsonSerializer.Serialize(layout));
        Assert.NotNull(restored);
        Assert.Equal(3, restored.Profiles["display"][0].Items.Count);
        Assert.Single(IconCaptureRecovery.Collect(restored, "display"));
    }
}
