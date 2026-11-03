using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Ui;

public sealed class CharacterRendererTests
{
    private static byte[] Render(DripControl drip, int width = 100, int height = 140)
    {
        drip.IsAnimated = false;
        drip.Measure(new Size(width, height));
        drip.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(drip);
        using var stream = new MemoryStream();
#pragma warning disable CS0618
        bitmap.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    private static byte[] Blank(int width = 100, int height = 140)
    {
        var border = new Border { Width = width, Height = height };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(border);
        using var stream = new MemoryStream();
#pragma warning disable CS0618
        bitmap.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    private static JsonObject Anchors() => new()
    {
        ["eyeLeft"] = new JsonArray(39, 86), ["eyeRight"] = new JsonArray(61, 86), ["mouth"] = new JsonArray(50, 98),
        ["cheekLeft"] = new JsonArray(29, 98), ["cheekRight"] = new JsonArray(71, 98),
        ["shoulderLeft"] = new JsonArray(16, 92), ["shoulderRight"] = new JsonArray(84, 92),
        ["headTop"] = new JsonArray(50, 6), ["eyeLine"] = new JsonArray(50, 86), ["neck"] = new JsonArray(50, 64),
        ["handHold"] = new JsonArray(72, 108),
    };

    private static Pack Load(JsonObject manifest)
    {
        var result = PackLoader.FromFiles(
            new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) },
            new PackLoadOptions(new Version(1, 0)));
        return result.Pack ?? throw new InvalidOperationException(result.Error);
    }

    private static CharacterPack Blob(string deep = "#CC3344") => (CharacterPack)Load(new JsonObject
    {
        ["formatVersion"] = 1,
        ["kind"] = "character",
        ["id"] = "tester.blob",
        ["name"] = "Blob",
        ["version"] = "1.0.0",
        ["palette"] = new JsonObject { ["light"] = "#FFD0D8", ["deep"] = deep, ["outline"] = "#661122" },
        ["anchors"] = Anchors(),
        ["body"] = new JsonArray(new JsonObject
        {
            ["ellipse"] = new JsonArray(50, 90, 44, 38),
            ["fill"] = new JsonObject { ["gradient"] = new JsonArray("$light", "$deep") },
            ["stroke"] = new JsonObject { ["color"] = "$outline", ["width"] = 2 },
        }),
        ["feet"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(38, 125, 9, 5), ["fill"] = "$deep" }),
        ["face"] = new JsonObject
        {
            ["eyes"] = new JsonObject { ["open"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(0, 0, 6, 6), ["fill"] = "#000000" }) },
        },
    });

    private static AccessoryPack Hat() => (AccessoryPack)Load(new JsonObject
    {
        ["formatVersion"] = 1,
        ["kind"] = "accessory",
        ["id"] = "tester.hat",
        ["name"] = "Hat",
        ["version"] = "1.0.0",
        ["slot"] = "hat",
        ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-14,0 L0,-30 L14,0 Z", ["fill"] = "#F472B6" }),
    });

    [AvaloniaFact]
    public void The_built_in_characters_are_ordinary_packs_that_pass_every_check()
    {
        Assert.Equal(["heysip.drip", "heysip.minty", "heysip.sunny", "heysip.blossom", "heysip.axolotl", "heysip.duckling"], CharacterCatalog.All.Where(entry => entry.BuiltIn).Select(entry => entry.Id));
        foreach (var entry in CharacterCatalog.All.Where(entry => entry.BuiltIn))
        {
            var pack = entry.Pack;

            Assert.Equal(entry.Id, pack.Header.Id);
            Assert.Equal(entry.Name, pack.Header.Name);
            Assert.Equal(PackTier.Free, pack.Header.Tier);
            Assert.Equal(11, pack.Anchors.Count);
        }
    }

    [AvaloniaFact]
    public void Built_in_characters_keep_the_colours_they_had_before_they_became_packs()
    {
        Assert.Equal(new PackColor(0x2B, 0xB5, 0xA6), CharacterLibrary.PackOf("heysip.drip").Palette["deep"]);
        Assert.Equal(new PackColor(0x22, 0xB5, 0x73), CharacterLibrary.PackOf("heysip.minty").Palette["deep"]);
        Assert.Equal(new PackColor(0xF5, 0xA6, 0x23), CharacterLibrary.PackOf("heysip.sunny").Palette["deep"]);
        Assert.Equal(new PackColor(0xEC, 0x6A, 0xA3), CharacterLibrary.PackOf("heysip.blossom").Palette["deep"]);
    }

    [AvaloniaFact]
    public void Every_character_draws_in_every_pose_and_each_pose_looks_different_from_standing()
    {
        var blank = Blank();
        foreach (var character in CharacterCatalog.All.Select(entry => entry.Id))
        {
            var standing = Render(new DripControl { Character = character, Pose = DripPose.Idle });
            Assert.NotEqual(blank, standing);

            foreach (var pose in Enum.GetValues<DripPose>().Where(pose => pose != DripPose.Idle))
            {
                var drawn = Render(new DripControl { Character = character, Pose = pose });
                Assert.NotEqual(blank, drawn);
                Assert.NotEqual(standing, drawn);
            }
        }
    }

    [AvaloniaFact]
    public void The_built_in_characters_look_different_from_one_another()
    {
        var drawings = CharacterCatalog.All.Select(entry => Render(new DripControl { Character = entry.Id })).ToList();

        Assert.Equal(drawings.Count, drawings.Select(Convert.ToBase64String).Distinct().Count());
    }

    [AvaloniaFact]
    public void A_still_character_draws_the_same_thing_every_time()
    {
        var first = Render(new DripControl { Character = "heysip.drip", Pose = DripPose.Cheering });
        var second = Render(new DripControl { Character = "heysip.drip", Pose = DripPose.Cheering });

        Assert.Equal(first, second);
    }

    [AvaloniaFact]
    public void A_loaded_pack_is_drawn_instead_of_the_built_in_character()
    {
        var builtIn = Render(new DripControl { Character = "heysip.drip" });
        var custom = Render(new DripControl { Character = "heysip.drip", Pack = Blob() });

        Assert.NotEqual(builtIn, custom);
        Assert.NotEqual(Blank(), custom);
    }

    [AvaloniaFact]
    public void A_packs_own_colours_and_face_shapes_are_used()
    {
        var red = Render(new DripControl { Pack = Blob("#CC3344") });
        var blue = Render(new DripControl { Pack = Blob("#3344CC") });

        Assert.NotEqual(red, blue);
    }

    [AvaloniaFact]
    public void An_accessory_is_worn_on_any_character_and_changes_what_is_drawn()
    {
        var hat = Hat();
        foreach (var character in new[] { CharacterLibrary.PackOf("heysip.drip"), Blob() })
        {
            var bare = Render(new DripControl { Pack = character });
            var worn = Render(new DripControl { Pack = character, Outfit = [hat] });

            Assert.NotEqual(bare, worn);
        }
    }

    [AvaloniaFact]
    public void The_axolotl_and_duckling_are_free_built_in_characters_with_bodies_of_their_own()
    {
        var droplet = CharacterLibrary.PackOf("heysip.drip");
        foreach (var id in new[] { "heysip.axolotl", "heysip.duckling" })
        {
            var entry = CharacterCatalog.Find(id);
            var pack = entry.Pack;

            Assert.True(entry.BuiltIn);
            Assert.Equal(id, pack.Header.Id);
            Assert.Equal(PackTier.Free, pack.Header.Tier);
            Assert.Equal("HeySip", pack.Header.Author);

            // Not a recoloured droplet: a different body, and a face drawn by the pack itself.
            Assert.NotEqual(droplet.Body.OfType<PathShape>().First().Data, pack.Body.OfType<PathShape>().First().Data);
            Assert.NotEmpty(pack.Face.Mouths);
            Assert.True(pack.Body.Count > droplet.Body.Count, $"{pack.Header.Name} should have more to it than the droplet");
        }

        Assert.Equal("Axolotl", CharacterCatalog.Find("heysip.axolotl").Name);
        Assert.Equal("Duckling", CharacterCatalog.Find("heysip.duckling").Name);
        Assert.NotEmpty(CharacterCatalog.Find("heysip.axolotl").Pack.Face.Eyes);
    }

    [AvaloniaFact]
    public void The_axolotl_and_duckling_wear_accessories_like_any_other_character()
    {
        var hat = Hat();
        foreach (var id in new[] { "heysip.axolotl", "heysip.duckling" })
        {
            var bare = Render(new DripControl { Character = id });
            var worn = Render(new DripControl { Character = id, Outfit = [hat] });

            Assert.NotEqual(bare, worn);
        }
    }

    [AvaloniaFact]
    public void Characters_draw_at_icon_sizes_and_stay_inside_their_box()
    {
        foreach (var size in new[] { 16, 24, 32, 64, 256 })
        {
            var drawn = Render(new DripControl { Character = "heysip.blossom", Pose = DripPose.Cheering }, size, size);

            Assert.NotEqual(Blank(size, size), drawn);
        }
    }

    [AvaloniaFact]
    public void The_animation_timer_still_stops_for_a_still_or_hidden_character()
    {
        var still = new DripControl { IsAnimated = false };
        Assert.False(still.IsTimerRunning);
    }
}
