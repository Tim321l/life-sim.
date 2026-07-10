using FluentAssertions;
using HKLifeSim.Core.Presentation;

namespace HKLifeSim.Core.Tests;

public sealed class SpriteTimelineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(499, 0)]
    [InlineData(500, 1)]
    [InlineData(999, 1)]
    [InlineData(1000, 0)]
    [InlineData(1499, 0)]
    [InlineData(1500, 1)]
    public void GetLoopingFrame_Wraps_Correctly_Around_The_Frame_Count(long elapsedMs, int expectedFrame)
    {
        var frame = SpriteTimeline.GetLoopingFrame(frameCount: 2, msPerFrame: 500, elapsedMs);

        frame.Should().Be(expectedFrame);
    }

    [Fact]
    public void GetLoopingFrame_Wraps_Across_More_Than_One_Full_Cycle()
    {
        // 3 frames * 400ms = 1200ms per full cycle; 2900ms is 2 full cycles + 500ms in => frame 1.
        var frame = SpriteTimeline.GetLoopingFrame(frameCount: 3, msPerFrame: 400, elapsedMs: 2900);

        frame.Should().Be(1);
    }

    [Fact]
    public void GetLoopingFrame_Throws_For_Zero_FrameCount()
    {
        var act = () => SpriteTimeline.GetLoopingFrame(frameCount: 0, msPerFrame: 500, elapsedMs: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetLoopingFrame_Throws_For_Zero_MsPerFrame()
    {
        var act = () => SpriteTimeline.GetLoopingFrame(frameCount: 2, msPerFrame: 0, elapsedMs: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetActionFrame_IsFinished_Is_False_Before_DurationMs()
    {
        var action = new ActionDef { Pose = "sit", Icon = "book", DurationMs = 2000 };
        var pose = new PoseDef { Row = 1, Frames = 2, Ms = 500 };
        var icon = new IconDef { File = "book.png", Frames = 2, Ms = 400, Anchor = "front" };

        var result = SpriteTimeline.GetActionFrame(action, pose, icon, elapsedMs: 1999);

        result.IsFinished.Should().BeFalse();
    }

    [Fact]
    public void GetActionFrame_IsFinished_Is_True_Exactly_At_DurationMs()
    {
        var action = new ActionDef { Pose = "sit", Icon = "book", DurationMs = 2000 };
        var pose = new PoseDef { Row = 1, Frames = 2, Ms = 500 };
        var icon = new IconDef { File = "book.png", Frames = 2, Ms = 400, Anchor = "front" };

        var result = SpriteTimeline.GetActionFrame(action, pose, icon, elapsedMs: 2000);

        result.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void GetActionFrame_Computes_Pose_And_Icon_Frames_Independently()
    {
        // pose: 2 frames @ 500ms => at 700ms -> frame 1
        // icon: 2 frames @ 400ms => at 700ms -> frame 1 too, so pick an elapsed where they diverge.
        var action = new ActionDef { Pose = "sit", Icon = "book", DurationMs = 5000 };
        var pose = new PoseDef { Row = 1, Frames = 2, Ms = 500 };
        var icon = new IconDef { File = "book.png", Frames = 2, Ms = 400, Anchor = "front" };

        // At 900ms: pose frame = (900/500)%2 = 1; icon frame = (900/400)%2 = 0.
        var result = SpriteTimeline.GetActionFrame(action, pose, icon, elapsedMs: 900);

        result.PoseFrame.Should().Be(1);
        result.IconFrame.Should().Be(0);
    }

    [Fact]
    public void GetActionFrame_IconFrame_Is_Null_When_The_Action_Has_No_Icon()
    {
        var action = new ActionDef { Pose = "stand", Icon = null, DurationMs = 2000 };
        var pose = new PoseDef { Row = 0, Frames = 2, Ms = 500 };

        var result = SpriteTimeline.GetActionFrame(action, pose, icon: null, elapsedMs: 100);

        result.IconFrame.Should().BeNull();
    }

    [Fact]
    public void GetActionFrame_Uses_PoseMs_Override_When_Set()
    {
        // pose.Ms=500 but action.PoseMs=150 (skipping rope bob) should win.
        var action = new ActionDef { Pose = "stand", Icon = "rope", DurationMs = 2000, PoseMs = 150 };
        var pose = new PoseDef { Row = 0, Frames = 2, Ms = 500 };
        var icon = new IconDef { File = "rope.png", Frames = 2, Ms = 150, Anchor = "overlay" };

        // At 300ms with poseMs=150: (300/150)%2 = 0. With the un-overridden pose.Ms=500 it'd be (300/500)%2=0 too,
        // so use 450ms where the override and the base Ms disagree: override -> (450/150)%2=1, base -> (450/500)%2=0.
        var result = SpriteTimeline.GetActionFrame(action, pose, icon, elapsedMs: 450);

        result.PoseFrame.Should().Be(1);
    }

    [Fact]
    public void ResolveAction_Returns_The_Action_For_A_Known_Key()
    {
        var manifest = MakeManifest();

        var action = SpriteTimeline.ResolveAction(manifest, "溫習");

        action.Should().NotBeNull();
        action!.Pose.Should().Be("sit");
    }

    [Fact]
    public void ResolveAction_Returns_Null_As_The_Fallback_To_Happy_For_An_Unknown_Key()
    {
        var manifest = MakeManifest();

        var action = SpriteTimeline.ResolveAction(manifest, "does_not_exist");

        action.Should().BeNull();
    }

    private static SpriteManifest MakeManifest() => new()
    {
        SchemaVersion = 1,
        Stages = new Dictionary<string, StageSheet>
        {
            ["child"] = new StageSheet
            {
                Sheet = "child.png",
                Poses = new Dictionary<string, PoseDef>
                {
                    ["stand"] = new PoseDef { Row = 0, Frames = 2, Ms = 500 },
                    ["sit"] = new PoseDef { Row = 1, Frames = 2, Ms = 500 },
                },
            },
        },
        Icons = new Dictionary<string, IconDef>
        {
            ["book"] = new IconDef { File = "icons/book.png", Frames = 2, Ms = 400, Anchor = "front" },
        },
        Actions = new Dictionary<string, ActionDef>
        {
            ["溫習"] = new ActionDef { Pose = "sit", Icon = "book", DurationMs = 2000 },
        },
    };
}
