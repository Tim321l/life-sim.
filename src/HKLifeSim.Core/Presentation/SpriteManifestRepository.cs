using System.Text.Json;
using HKLifeSim.Core.Data;

namespace HKLifeSim.Core.Presentation;

public static class SpriteManifestRepository
{
    private static readonly string[] RequiredPoses = ["stand", "sit"];
    private static readonly string[] ValidAnchors = ["front", "overlay"];
    private static readonly string[] ValidActionPoses = ["stand", "sit"];

    public static SpriteManifest Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var manifest = Deserialize(json);

        if (manifest.Stages.Count == 0)
        {
            throw new SpriteDataException("manifest.json: stages must not be empty.");
        }

        foreach (var (stageKey, stage) in manifest.Stages)
        {
            ValidateStage(stageKey, stage);
        }

        foreach (var (iconKey, icon) in manifest.Icons)
        {
            ValidateIcon(iconKey, icon);
        }

        foreach (var (actionKey, action) in manifest.Actions)
        {
            ValidateAction(actionKey, action, manifest.Icons);
        }

        return manifest;
    }

    private static SpriteManifest Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, HkJsonContext.Default.SpriteManifest)
                ?? throw new SpriteDataException("manifest.json: file is empty.");
        }
        catch (JsonException ex)
        {
            throw new SpriteDataException($"manifest.json: invalid JSON — {ex.Message}", ex);
        }
    }

    private static void ValidateStage(string stageKey, StageSheet stage)
    {
        if (string.IsNullOrWhiteSpace(stage.Sheet))
        {
            throw new SpriteDataException($"manifest.json/stages/{stageKey}: sheet must not be empty.");
        }

        foreach (var requiredPose in RequiredPoses)
        {
            if (!stage.Poses.ContainsKey(requiredPose))
            {
                throw new SpriteDataException($"manifest.json/stages/{stageKey}: poses must include '{requiredPose}'.");
            }
        }

        foreach (var (poseKey, pose) in stage.Poses)
        {
            if (pose.Row < 0)
            {
                throw new SpriteDataException($"manifest.json/stages/{stageKey}/poses/{poseKey}: row must be >= 0.");
            }

            if (pose.Frames < 1)
            {
                throw new SpriteDataException($"manifest.json/stages/{stageKey}/poses/{poseKey}: frames must be >= 1.");
            }

            if (pose.Ms < 1)
            {
                throw new SpriteDataException($"manifest.json/stages/{stageKey}/poses/{poseKey}: ms must be >= 1.");
            }
        }
    }

    private static void ValidateIcon(string iconKey, IconDef icon)
    {
        if (string.IsNullOrWhiteSpace(icon.File))
        {
            throw new SpriteDataException($"manifest.json/icons/{iconKey}: file must not be empty.");
        }

        if (icon.Frames < 1)
        {
            throw new SpriteDataException($"manifest.json/icons/{iconKey}: frames must be >= 1.");
        }

        if (icon.Ms < 1)
        {
            throw new SpriteDataException($"manifest.json/icons/{iconKey}: ms must be >= 1.");
        }

        if (!ValidAnchors.Contains(icon.Anchor, StringComparer.Ordinal))
        {
            throw new SpriteDataException($"manifest.json/icons/{iconKey}: anchor must be 'front' or 'overlay'.");
        }
    }

    private static void ValidateAction(string actionKey, ActionDef action, IReadOnlyDictionary<string, IconDef> icons)
    {
        if (!ValidActionPoses.Contains(action.Pose, StringComparer.Ordinal))
        {
            throw new SpriteDataException($"manifest.json/actions/{actionKey}: pose must be 'stand' or 'sit'.");
        }

        if (action.DurationMs < 1)
        {
            throw new SpriteDataException($"manifest.json/actions/{actionKey}: durationMs must be >= 1.");
        }

        if (action.PoseMs is < 1)
        {
            throw new SpriteDataException($"manifest.json/actions/{actionKey}: poseMs must be >= 1 when set.");
        }

        if (action.Icon is not null && !icons.ContainsKey(action.Icon))
        {
            throw new SpriteDataException($"manifest.json/actions/{actionKey}: icon '{action.Icon}' does not exist.");
        }
    }
}
