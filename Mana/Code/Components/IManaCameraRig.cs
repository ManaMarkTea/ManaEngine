namespace Mana
{
    /// <summary>
    /// Something that drives the camera: a follow boom, a game's own framing. Attached with
    /// <see cref="CameraComponent.AttachRig"/>, it is evaluated once a frame after the scene has moved (so it frames
    /// this frame's positions, not last frame's) and the camera takes the pose it gives; an attached camera does not fly.
    /// </summary>
    public interface IManaCameraRig
    {
        /// <summary>Works out this frame's pose.</summary>
        void Evaluate(float deltaSeconds);

        /// <summary>Drops any smoothing for one frame: on attaching, or when what it follows teleports.</summary>
        void Snap();

        Vector3 CameraPosition { get; }

        /// <summary>Pitch, yaw and roll in radians, in <see cref="ManaSceneNode.Rotation"/>'s convention.</summary>
        Vector3 CameraRotation { get; }

        /// <summary>The field of view to draw with, given the camera's own setting (which a rig never changes).</summary>
        float FieldOfView(float cameraDegrees);
    }

    /// <summary>A rig that is steered by the mouse: the camera feeds it look drags and the scroll wheel while attached.</summary>
    public interface IManaCameraLookRig : IManaCameraRig
    {
        /// <summary>Play-mode mouse scheme: a left-drag looks as well as a right-drag, and the pointer is held during either.</summary>
        bool MouseLookControls { get; }

        bool IsSteerGestureActive { get; set; }
        bool IsOrbitGestureActive { get; set; }

        void AddLookInput(float yawRadians, float pitchRadians);
        void AddZoomInput(float notches);
    }
}
