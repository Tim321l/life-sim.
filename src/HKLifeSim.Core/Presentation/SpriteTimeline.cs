namespace HKLifeSim.Core.Presentation;

public readonly record struct ActionPlaybackFrame(int PoseFrame, int? IconFrame, bool IsFinished);

public static class SpriteTimeline
{
    public static int GetLoopingFrame(int frameCount, int msPerFrame, long elapsedMs)
    {
        if (frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Must be >= 1.");
        }

        if (msPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(msPerFrame), msPerFrame, "Must be >= 1.");
        }

        var elapsed = Math.Max(0, elapsedMs);
        return (int)(elapsed / msPerFrame % frameCount);
    }

    public static ActionPlaybackFrame GetActionFrame(ActionDef action, PoseDef pose, IconDef? icon, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(pose);

        var poseMs = action.PoseMs ?? pose.Ms;
        var poseFrame = GetLoopingFrame(pose.Frames, poseMs, elapsedMs);
        var iconFrame = icon is not null ? GetLoopingFrame(icon.Frames, icon.Ms, elapsedMs) : (int?)null;
        var isFinished = elapsedMs >= action.DurationMs;

        return new ActionPlaybackFrame(poseFrame, iconFrame, isFinished);
    }

    // Returns null (fallback: no specific action animation, caller shows plain happy/idle) when
    // actionKey isn't in the manifest — e.g. an unmapped Web hand-rolled action.
    public static ActionDef? ResolveAction(SpriteManifest manifest, string actionKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(actionKey);

        return manifest.Actions.TryGetValue(actionKey, out var action) ? action : null;
    }
}
