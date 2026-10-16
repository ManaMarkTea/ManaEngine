using Mana.Code.Audio;
using Mana.Code.Nodes;
using ManaCore.Audio;

namespace Mana.Code.Components;

public sealed class ManaPawnAudioComponent(ManaPawnNode pawn) : ManaComponent(pawn), IDisposable
{
    private Vector3 previousAudioPosition;
    private float stepDistance, fallingSeconds;
    private bool audioInitialized, fallSoundPlayed;
    private int audioDeaths;

    private bool subscribed;

    public override void BeginPlay()
    {
        if (subscribed) { return; }
        pawn.Jumped += OnJumped;
        pawn.Landed += OnLanded;
        subscribed = true;
        ResetMovement();
    }

    public override void EndPlay()
    {
        if (subscribed)
        {
            pawn.Jumped -= OnJumped;
            pawn.Landed -= OnLanded;
            subscribed = false;
        }
        ResetMovement();
    }

    public void Dispose() => EndPlay();
    private void OnJumped() => EmitPlayerSound(ManaSoundCue.Jump);
    private void OnLanded() => EmitPlayerSound(ManaSoundCue.Land);

    private void ResetMovement()
    {
        audioInitialized = false;
        stepDistance = fallingSeconds = 0;
        fallSoundPlayed = false;
    }

    private void EmitPlayerSound(ManaSoundCue cue)
    {
        var world = pawn.Context.World;
        if (world != null && world.IsPlaying && ReferenceEquals(world.GameState.Player, pawn)
            && world.GameState.Status == ManaGameStatus.Playing)
        {
            world.AudioCues.Emit(cue);
        }
    }

    public override void UpdateComponent()
    {
        float dt = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
        var world = pawn.Context.World;
        if (!subscribed || world == null || !world.IsPlaying || !ReferenceEquals(world.GameState.Player, pawn)
            || world.GameState.Status != ManaGameStatus.Playing)
        {
            ResetMovement();
            return;
        }
        var position = pawn.WorldTransform.Translation;
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
        if (!pawn.IsGrounded)
        {
            stepDistance = 0;
            if ((pawn.Body?.Velocity.Y ?? 0) < -8) { fallingSeconds += dt; }
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
        if (!pawn.LocomotionEnabled || pawn.CurrentSpeed < 0.2f || distance > Math.Max(2, pawn.CurrentSpeed * dt * 3))
        {
            stepDistance = 0;
            return;
        }
        stepDistance += distance;
        if (stepDistance >= 1.25f)
        {
            stepDistance %= 1.25f;
            var surface = pawn.Body?.GroundNode;
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
