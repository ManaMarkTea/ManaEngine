using Mana.Code.Audio;
using ManaCore.Audio;

namespace Mana.Code.Nodes;

public partial class ManaPawnNode
{
    private Vector3 previousAudioPosition;
    private float stepDistance, fallingSeconds;
    private bool audioInitialized, fallSoundPlayed;
    private int audioDeaths;

    private void EmitPlayerSound(ManaSoundCue cue)
    {
        var world = Context.World;
        if (world != null && world.IsPlaying && ReferenceEquals(world.GameState.Player, this)
            && world.GameState.Status == ManaGameStatus.Playing)
        {
            world.AudioCues.Emit(cue);
        }
    }

    private void UpdateMovementAudio(float dt)
    {
        var world = Context.World;
        if (world == null || !world.IsPlaying || !ReferenceEquals(world.GameState.Player, this)
            || world.GameState.Status != ManaGameStatus.Playing)
        {
            audioInitialized = false;
            stepDistance = fallingSeconds = 0;
            fallSoundPlayed = false;
            return;
        }
        var position = WorldTransform.Translation;
        if (!audioInitialized || audioDeaths != world.GameState.Deaths)
        {
            audioInitialized = true;
            audioDeaths = world.GameState.Deaths;
            previousAudioPosition = position;
            stepDistance = fallingSeconds = 0;
            fallSoundPlayed = false;
            return;
        }
        var displacement = position - previousAudioPosition;
        previousAudioPosition = position;
        if (dt <= 0) { return; }
        if (!IsGrounded)
        {
            stepDistance = 0;
            if ((Body?.Velocity.Y ?? 0) < -8) { fallingSeconds += dt; }
            if (fallingSeconds > 0.3f && !fallSoundPlayed)
            {
                fallSoundPlayed = true;
                EmitPlayerSound(ManaSoundCue.Fall);
            }
            return;
        }
        fallingSeconds = 0;
        fallSoundPlayed = false;
        float distance = new Vector2(displacement.X, displacement.Z).Length();
        // Ignore teleports, pushing into walls, and being carried while standing still.
        if (!LocomotionEnabled || CurrentSpeed < 0.2f || distance > Math.Max(2, CurrentSpeed * dt * 3))
        {
            stepDistance = 0;
            return;
        }
        stepDistance += distance;
        if (stepDistance >= 1.25f)
        {
            stepDistance %= 1.25f;
            var surface = Body?.GroundNode;
            bool matched = false;
            while (surface != null && !matched)
            {
                matched = ManaAudioCueRouter.EmitAt(surface, ManaSoundCue.Footstep, position);
                surface = surface.Parent;
            }
            if (!matched)
            {
                EmitPlayerSound(ManaSoundCue.Footstep);
            }
        }
    }
}
