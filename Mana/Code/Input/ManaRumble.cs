using ManaCore.Audio;
using Silk.NET.Input;

namespace Mana.Code.Input
{
    /// <summary>How hard a rumble is: a UI tap, a light bump, a hit, a crash.</summary>
    public enum ManaRumbleStrength
    {
        Tap,
        Light,
        Medium,
        Heavy
    }

    /// <summary>A platform's own haptics -- a phone's Taptic Engine. Installed by the app (<see cref="ManaRumble.Haptics"/>).</summary>
    public interface IManaHaptics
    {
        void Play(ManaRumbleStrength strength);
    }

    /// <summary>
    /// Rumble, whatever is there to feel it: the platform's haptics (a phone) and every connected gamepad's motors. A
    /// game calls <see cref="Play"/>; the host keeps the motors' timing with <see cref="Update"/> once a frame. Play modes
    /// also rumble on the game state's cues (<see cref="For"/>), so any level gets it -- hurt, a fall, a stomp.
    /// <para>GLFW (the desktop's input) reports no motors, so a desktop pad stays still until input moves to SDL.</para>
    /// </summary>
    public static class ManaRumble
    {
        private static float _remaining;
        private static float _speed;
        private static IInputContext? _input;

        /// <summary>The platform's own haptics, or null where there are none.</summary>
        public static IManaHaptics? Haptics { get; set; }

        /// <summary>Off for a player who would rather not.</summary>
        public static bool Enabled { get; set; } = true;

        public static void Play(ManaRumbleStrength strength)
        {
            if (!Enabled)
            {
                return;
            }
            Haptics?.Play(strength);
            var (speed, seconds) = strength switch
            {
                ManaRumbleStrength.Tap => (0.15f, 0.05f),
                ManaRumbleStrength.Light => (0.3f, 0.08f),
                ManaRumbleStrength.Medium => (0.6f, 0.18f),
                _ => (1.0f, 0.4f)
            };
            // A softer rumble does not cut a harder one short.
            if (_remaining <= 0.0f || speed >= _speed)
            {
                _speed = speed;
                _remaining = MathF.Max(_remaining, seconds);
                SetMotors(_speed);
            }
        }

        /// <summary>Once a frame: which pads to shake, and stopping them when a rumble runs out.</summary>
        public static void Update(float deltaSeconds, IInputContext? input)
        {
            _input = input;
            if (_remaining <= 0.0f)
            {
                return;
            }
            _remaining -= deltaSeconds;
            if (_remaining <= 0.0f)
            {
                _remaining = 0.0f;
                _speed = 0.0f;
                SetMotors(0.0f);
            }
        }

        /// <summary>The rumble a game-state cue earns, or null for one that should not be felt (footsteps, a coin).</summary>
        public static ManaRumbleStrength? For(ManaSoundCue cue) => cue switch
        {
            ManaSoundCue.Hurt or ManaSoundCue.HazardContact => ManaRumbleStrength.Medium,
            ManaSoundCue.Death or ManaSoundCue.Fall => ManaRumbleStrength.Heavy,
            ManaSoundCue.EnemyStomp or ManaSoundCue.EnemyDefeat or ManaSoundCue.Checkpoint => ManaRumbleStrength.Light,
            ManaSoundCue.Goal => ManaRumbleStrength.Medium,
            _ => null
        };

        private static void SetMotors(float speed)
        {
            if (_input == null)
            {
                return;
            }
            foreach (var gamepad in _input.Gamepads)
            {
                if (!gamepad.IsConnected)
                {
                    continue;
                }
                foreach (var motor in gamepad.VibrationMotors)
                {
                    try
                    {
                        motor.Speed = speed;
                    }
                    catch (Exception)
                    {
                        // A pad that went away mid-rumble.
                    }
                }
            }
        }
    }
}
