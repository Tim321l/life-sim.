using FluentAssertions;
using HKLifeSim.Core.Presentation;

namespace HKLifeSim.Core.Tests;

public sealed class SpriteManifestRepositoryTests
{
    [Fact]
    public void Load_Throws_On_Malformed_Json()
    {
        const string json = "{not valid json";

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*invalid JSON*");
    }

    [Fact]
    public void Load_Throws_When_Stages_Is_Missing()
    {
        const string json = """{"schemaVersion":1,"icons":{},"actions":{}}""";

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*invalid JSON*");
    }

    [Fact]
    public void Load_Throws_When_Stages_Is_Empty()
    {
        var json = WrapFile("""{}""", "{}", "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*stages must not be empty*");
    }

    [Fact]
    public void Load_Throws_When_A_Stage_Is_Missing_The_Stand_Pose()
    {
        var stages = """{"child":{"sheet":"child.png","poses":{"sit":{"row":1,"frames":2,"ms":500}}}}""";
        var json = WrapFile(stages, "{}", "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*'stand'*");
    }

    [Fact]
    public void Load_Throws_When_A_Stage_Is_Missing_The_Sit_Pose()
    {
        var stages = """{"child":{"sheet":"child.png","poses":{"stand":{"row":0,"frames":2,"ms":500}}}}""";
        var json = WrapFile(stages, "{}", "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*'sit'*");
    }

    [Fact]
    public void Load_Throws_On_A_Pose_With_Zero_Frames()
    {
        var stages = ValidStageJsonWithPoseFrames(0);
        var json = WrapFile(stages, "{}", "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*frames must be >= 1*");
    }

    [Fact]
    public void Load_Throws_On_A_Pose_With_Zero_Ms()
    {
        const string stages = """{"child":{"sheet":"child.png","poses":{"stand":{"row":0,"frames":2,"ms":0},"sit":{"row":1,"frames":2,"ms":500}}}}""";
        var json = WrapFile(stages, "{}", "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*ms must be >= 1*");
    }

    [Fact]
    public void Load_Throws_On_An_Icon_With_An_Invalid_Anchor()
    {
        var icons = """{"book":{"file":"icons/book.png","frames":2,"ms":400,"anchor":"sideways"}}""";
        var json = WrapFile(ValidStageJson(), icons, "{}");

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*anchor*");
    }

    [Fact]
    public void Load_Throws_When_An_Action_References_A_NonExistent_Icon()
    {
        var actions = """{"溫習":{"pose":"sit","icon":"does_not_exist","durationMs":2000}}""";
        var json = WrapFile(ValidStageJson(), "{}", actions);

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*does_not_exist*");
    }

    [Fact]
    public void Load_Throws_When_An_Action_Uses_A_Pose_Other_Than_Stand_Or_Sit()
    {
        var actions = """{"溫習":{"pose":"lying_down","durationMs":2000}}""";
        var json = WrapFile(ValidStageJson(), "{}", actions);

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*'stand' or 'sit'*");
    }

    [Fact]
    public void Load_Throws_On_An_Action_With_Zero_DurationMs()
    {
        var actions = """{"溫習":{"pose":"sit","durationMs":0}}""";
        var json = WrapFile(ValidStageJson(), "{}", actions);

        var act = () => SpriteManifestRepository.Load(json);

        act.Should().Throw<SpriteDataException>().WithMessage("*durationMs must be >= 1*");
    }

    [Fact]
    public void Load_Succeeds_For_A_Well_Formed_Minimal_Manifest()
    {
        var icons = """{"book":{"file":"icons/book.png","frames":2,"ms":400,"anchor":"front"}}""";
        var actions = """{"溫習":{"pose":"sit","icon":"book","durationMs":2000}}""";
        var json = WrapFile(ValidStageJson(), icons, actions);

        var manifest = SpriteManifestRepository.Load(json);

        manifest.Stages.Should().ContainKey("child");
        manifest.Icons.Should().ContainKey("book");
        manifest.Actions.Should().ContainKey("溫習");
    }

    [Fact]
    public void Load_Succeeds_For_The_Real_Manifest_Data_File()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "sprites", "manifest.json"));

        var manifest = SpriteManifestRepository.Load(json);

        manifest.Stages.Keys.Should().BeEquivalentTo(["baby", "child", "teen", "adult", "elder"]);
        manifest.Icons.Keys.Should().BeEquivalentTo(["book", "pencil", "rope", "mask", "wheel", "heart", "alert"]);
        manifest.Actions.Should().ContainKey("study_revision");
        manifest.Actions.Should().ContainKey("web:study_hard");
    }

    private static string ValidStageJson() =>
        """{"child":{"sheet":"child.png","poses":{"stand":{"row":0,"frames":2,"ms":500},"sit":{"row":1,"frames":2,"ms":500}}}}""";

    private static string ValidStageJsonWithPoseFrames(int frames) =>
        """{"child":{"sheet":"child.png","poses":{"stand":{"row":0,"frames":FRAMES,"ms":500},"sit":{"row":1,"frames":2,"ms":500}}}}"""
            .Replace("FRAMES", frames.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string WrapFile(string stages, string icons, string actions) =>
        $$"""{"schemaVersion":1,"stages":{{stages}},"icons":{{icons}},"actions":{{actions}}}""";
}
