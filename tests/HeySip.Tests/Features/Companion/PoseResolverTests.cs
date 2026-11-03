using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PoseResolverTests
{
    private static CharacterPack Pack(Action<JsonObject>? change = null)
    {
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = "tester.blob",
            ["name"] = "Blob",
            ["version"] = "1.0.0",
            ["palette"] = new JsonObject { ["light"] = "#FFFFFF", ["deep"] = "#888888", ["outline"] = "#444444" },
            ["anchors"] = new JsonObject
            {
                ["eyeLeft"] = new JsonArray(39, 86), ["eyeRight"] = new JsonArray(61, 86), ["mouth"] = new JsonArray(50, 98),
                ["cheekLeft"] = new JsonArray(29, 98), ["cheekRight"] = new JsonArray(71, 98),
                ["shoulderLeft"] = new JsonArray(16, 92), ["shoulderRight"] = new JsonArray(84, 92),
                ["headTop"] = new JsonArray(50, 6), ["eyeLine"] = new JsonArray(50, 86), ["neck"] = new JsonArray(50, 64),
                ["handHold"] = new JsonArray(72, 108),
            },
            ["body"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(50, 80, 36, 40), ["fill"] = "$light" }),
        };
        change?.Invoke(manifest);

        var result = PackLoader.FromFiles(
            new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) },
            new PackLoadOptions(new Version(1, 0)));
        return (CharacterPack)(result.Pack ?? throw new InvalidOperationException(result.Error));
    }

    [Fact]
    public void A_minimal_pack_gets_every_standard_pose()
    {
        var all = PoseResolver.ResolveAll(Pack());

        Assert.Equal(Enum.GetValues<StandardPose>().Length, all.Count);
        foreach (var pose in Enum.GetValues<StandardPose>())
        {
            Assert.True(all.ContainsKey(pose), $"{pose} is missing");
        }
    }

    [Fact]
    public void Hands_follow_the_characters_own_shoulders()
    {
        // The same arm pose, on a character whose shoulders sit elsewhere, ends up in a different place.
        var standard = Pack();
        var narrow = Pack(m =>
        {
            var anchors = (JsonObject)m["anchors"]!;
            anchors["shoulderLeft"] = new JsonArray(30, 92);
            anchors["shoulderRight"] = new JsonArray(70, 92);
        });

        var happyStandard = PoseResolver.Resolve(standard, StandardPose.Happy);
        var happyNarrow = PoseResolver.Resolve(narrow, StandardPose.Happy);

        // The arms of a standard character would reach just past the picture, so the hands are held a little inside it.
        Assert.Equal(new PackPoint(6.5, 64), happyStandard.LeftHand);
        Assert.Equal(new PackPoint(93.5, 64), happyStandard.RightHand);
        Assert.Equal(new PackPoint(19, 64), happyNarrow.LeftHand);
        Assert.Equal(new PackPoint(81, 64), happyNarrow.RightHand);
    }

    [Fact]
    public void The_hands_of_every_standard_pose_stay_inside_the_picture_even_for_a_character_with_shoulders_at_the_edge()
    {
        var atTheEdge = Pack(m =>
        {
            var anchors = (JsonObject)m["anchors"]!;
            anchors["shoulderLeft"] = new JsonArray(2, 92);
            anchors["shoulderRight"] = new JsonArray(98, 92);
        });

        foreach (var pose in Enum.GetValues<StandardPose>().Where(pose => pose != StandardPose.Drinking))
        {
            var look = PoseResolver.Resolve(atTheEdge, pose);
            foreach (var hand in new[] { look.LeftHand, look.RightHand })
            {
                Assert.InRange(hand.X, 0, 100);
                Assert.InRange(hand.Y, 0, 140);
            }
        }
    }

    [Fact]
    public void Poses_have_the_faces_and_effects_the_app_relies_on()
    {
        var pack = Pack();

        Assert.Equal(EyeStyle.Happy, PoseResolver.Resolve(pack, StandardPose.Happy).Eyes);
        Assert.Equal(BrowStyle.Worried, PoseResolver.Resolve(pack, StandardPose.Worried).Brows);
        Assert.Equal(EyeStyle.Closed, PoseResolver.Resolve(pack, StandardPose.Sleeping).Eyes);
        Assert.Contains(PoseEffect.Zzz, PoseResolver.Resolve(pack, StandardPose.Sleeping).Effects);
        Assert.Contains(PoseEffect.Sparkle, PoseResolver.Resolve(pack, StandardPose.Cheering).Effects);
        Assert.True(PoseResolver.Resolve(pack, StandardPose.Cheering).Bounce > 0);
        Assert.True(PoseResolver.Resolve(pack, StandardPose.Shivering).Shake > 0);
        Assert.Contains(PoseEffect.Glass, PoseResolver.Resolve(pack, StandardPose.Drinking).Effects);
        Assert.Equal(new PackPoint(72, 108), PoseResolver.Resolve(pack, StandardPose.Drinking).RightHand);
        Assert.NotNull(PoseResolver.Resolve(pack, StandardPose.Waving).Sway);
        Assert.Null(PoseResolver.Resolve(pack, StandardPose.Idle).Sway);
    }

    [Fact]
    public void Calm_poses_have_no_bounce_or_shake()
    {
        var pack = Pack();

        foreach (var pose in new[] { StandardPose.Idle, StandardPose.Sleepy, StandardPose.Sleeping, StandardPose.Waving, StandardPose.Thinking })
        {
            var look = PoseResolver.Resolve(pack, pose);
            Assert.Equal(0, look.Bounce);
            Assert.Equal(0, look.Shake);
        }
    }

    [Fact]
    public void A_pack_override_changes_only_what_it_names()
    {
        var pack = Pack(m => m["poses"] = new JsonObject
        {
            ["happy"] = new JsonObject
            {
                ["mouth"] = "o",
                ["arms"] = new JsonObject { ["right"] = new JsonArray(90, 50) },
                ["effects"] = new JsonArray(),
            },
        });

        var happy = PoseResolver.Resolve(pack, StandardPose.Happy);
        var standard = PoseResolver.Resolve(Pack(), StandardPose.Happy);

        Assert.Equal(MouthStyle.O, happy.Mouth);
        Assert.Equal(new PackPoint(90, 50), happy.RightHand);
        Assert.Equal(standard.Eyes, happy.Eyes);
        Assert.Equal(standard.LeftHand, happy.LeftHand);
        Assert.Empty(happy.Effects);
    }

    [Fact]
    public void An_override_that_does_not_mention_effects_keeps_the_standard_ones()
    {
        var pack = Pack(m => m["poses"] = new JsonObject { ["sleeping"] = new JsonObject { ["mouth"] = "sleepy" } });

        var sleeping = PoseResolver.Resolve(pack, StandardPose.Sleeping);

        Assert.Equal(MouthStyle.Sleepy, sleeping.Mouth);
        Assert.Contains(PoseEffect.Zzz, sleeping.Effects);
    }
}
