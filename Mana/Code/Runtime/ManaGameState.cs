using System.Globalization;
using ManaCore.Audio;

namespace Mana.Code
{
    public enum ManaGameStatus
    {
        Playing,
        Won
    }

    /// <summary>
    /// The rules of a play session -- coins, health, checkpoint, clock, win -- kept in one place so the
    /// gameplay components only report what happened to the player and never each hold a piece of the score.
    /// <para>
    /// Owned by the world and reset when play begins, so the editor's play state and the standalone
    /// <c>--play</c> run share one set of rules. It has no graphics or window dependency, which is what
    /// lets the whole loop be tested headlessly.
    /// </para>
    /// </summary>
    public sealed class ManaGameState
    {
        public event Action<ManaSoundCue>? SoundRequested;
        public const int DefaultMaxHealth = 3;
        public const float RespawnInvulnerableSeconds = 1.5f;
        public const float HitInvulnerableSeconds = 1.0f;

        private ManaPawnNode? _player;
        private float _invulnerableSeconds;
        private float _messageSeconds;

        public int MaxHealth { get; set; } = DefaultMaxHealth;

        public int Health { get; private set; } = DefaultMaxHealth;

        public int Coins { get; private set; }

        /// <summary>Coins on offer this run, counted as each collectible starts play.</summary>
        public int TotalCoins { get; private set; }

        public int Deaths { get; private set; }

        /// <summary>Enemies in the level this run, counted as each one starts play.</summary>
        public int TotalEnemies { get; private set; }

        public int EnemiesDefeated { get; private set; }

        public float ElapsedSeconds { get; private set; }

        public ManaGameStatus Status { get; private set; }

        /// <summary>Set once anything in the level can hurt, so a level without hazards shows no health.</summary>
        public bool UsesHealth { get; private set; }

        public string Message { get; private set; } = string.Empty;

        /// <summary>What last hurt or killed the player, by name -- what the HUD tells them at the top.</summary>
        public string LastCause { get; private set; } = string.Empty;

        public const float CauseMessageSeconds = 3.5f;

        public bool IsInvulnerable => _invulnerableSeconds > 0.0f;

        /// <summary>The character the rules apply to. Assigned by whichever game mode spawns one.</summary>
        public ManaPawnNode? Player
        {
            get => _player;
            set
            {
                if (ReferenceEquals(_player, value))
                {
                    return;
                }

                if (_player != null)
                {
                    _player.FellOutOfWorld -= OnPlayerFell;
                }

                _player = value;

                if (_player != null)
                {
                    _player.FellOutOfWorld += OnPlayerFell;
                }
            }
        }

        /// <summary>Back to a fresh run. The player is deliberately kept: it is spawned before play begins.</summary>
        public void Reset()
        {
            Health = MaxHealth;
            Coins = 0;
            TotalCoins = 0;
            Deaths = 0;
            TotalEnemies = 0;
            EnemiesDefeated = 0;
            ElapsedSeconds = 0.0f;
            Status = ManaGameStatus.Playing;
            UsesHealth = false;
            Message = string.Empty;
            _invulnerableSeconds = 0.0f;
            _messageSeconds = 0.0f;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0.0f)
            {
                return;
            }

            _invulnerableSeconds = MathF.Max(0.0f, _invulnerableSeconds - deltaSeconds);

            if (_messageSeconds > 0.0f)
            {
                _messageSeconds -= deltaSeconds;
                if (_messageSeconds <= 0.0f)
                {
                    Message = string.Empty;
                }
            }

            if (Status == ManaGameStatus.Playing)
            {
                ElapsedSeconds += deltaSeconds;
            }
        }

        public void RegisterCollectible(int value)
        {
            TotalCoins += value;
        }

        public void RegisterHazard()
        {
            UsesHealth = true;
        }

        /// <summary>An enemy can hurt, so health shows, and it is one more to count.</summary>
        public void RegisterEnemy()
        {
            UsesHealth = true;
            TotalEnemies++;
        }

        public void DefeatEnemy()
        {
            if (Status == ManaGameStatus.Playing)
            {
                EnemiesDefeated++;
            }
        }

        public void AddCoins(int value)
        {
            if (Status != ManaGameStatus.Playing)
            {
                return;
            }

            var previous = Coins;
            Coins += value;
            if (TotalCoins > 0 && previous < TotalCoins && Coins >= TotalCoins)
            {
                SoundRequested?.Invoke(ManaSoundCue.AllCoins);
            }
        }

        /// <summary>
        /// Hurts the player. Returns true when this hit was the one that killed them. <paramref name="cause"/> is
        /// what did it, by name, and is shown at the top of the screen -- without it a hit from something the
        /// player cannot see (a hazard volume, an enemy behind them) is just health going down for no reason.
        /// </summary>
        public bool Damage(int amount, string? cause = null)
        {
            if (Status != ManaGameStatus.Playing || IsInvulnerable || amount <= 0)
            {
                return false;
            }

            Health = Math.Max(0, Health - amount);
            if (Health == 0)
            {
                Die(cause);
                return true;
            }

            _invulnerableSeconds = HitInvulnerableSeconds;
            if (!string.IsNullOrWhiteSpace(cause))
            {
                LastCause = cause;
                ShowMessage($"Hurt by {cause} (-{amount})", CauseMessageSeconds);
            }

            SoundRequested?.Invoke(ManaSoundCue.Hurt);
            if (Health == 1) { SoundRequested?.Invoke(ManaSoundCue.LowHealth); }
            return false;
        }

        /// <summary>Moves the respawn point, so falling or dying brings the player back here.</summary>
        public void SetCheckpoint(Vector3 position)
        {
            if (_player != null)
            {
                _player.RespawnPoint = position;
            }

            ShowMessage("Checkpoint reached", 2.0f);
        }

        public void Win()
        {
            if (Status == ManaGameStatus.Won)
            {
                return;
            }

            Status = ManaGameStatus.Won;
            SoundRequested?.Invoke(ManaSoundCue.Goal);
            ShowMessage("Level complete!", float.PositiveInfinity);
        }

        public void ShowMessage(string message, float seconds)
        {
            Message = message;
            _messageSeconds = seconds;
        }

        /// <summary>Loses a life's worth of progress: full health, back at the respawn point, briefly untouchable.</summary>
        public void Die(string? cause = null)
        {
            if (!string.IsNullOrWhiteSpace(cause))
            {
                LastCause = cause;
                ShowMessage($"Killed by {cause}", CauseMessageSeconds);
            }

            Deaths++;
            Health = MaxHealth;
            _invulnerableSeconds = RespawnInvulnerableSeconds;

            if (_player != null)
            {
                _player.TeleportTo(_player.RespawnPoint);
            }
            SoundRequested?.Invoke(ManaSoundCue.Death);
            SoundRequested?.Invoke(ManaSoundCue.Respawn);
        }

        /// <summary>
        /// Starts the level again from the top: stops play, which puts every coin, enemy and platform back,
        /// puts the player at the spawn, and begins play afresh. Works from a win or from the middle of a run.
        /// </summary>
        public void Restart(ManaWorld world, Vector3 spawnPosition, float spawnYawRadians)
        {
            world.EndPlay();

            if (_player != null)
            {
                _player.RespawnPoint = spawnPosition;
                _player.TeleportTo(spawnPosition);

                var facing = _player.Rotation;
                facing.Y = spawnYawRadians;
                _player.Rotation = facing;
            }

            world.BeginPlay();
        }

        /// <summary>The one line the play HUD shows above the controls hint.</summary>
        public string FormatHud()
        {
            var parts = new List<string>();

            if (TotalCoins > 0)
            {
                parts.Add($"Coins {Coins}/{TotalCoins}");
            }

            if (TotalEnemies > 0)
            {
                parts.Add($"Enemies {EnemiesDefeated}/{TotalEnemies}");
            }

            if (UsesHealth)
            {
                parts.Add($"Health {Health}/{MaxHealth}");
            }

            var minutes = (int)(ElapsedSeconds / 60.0f);
            var seconds = ElapsedSeconds - (minutes * 60.0f);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Time {minutes}:{seconds:00.0}"));

            if (Deaths > 0)
            {
                parts.Add($"Deaths {Deaths}");
            }

            var line = string.Join("   ", parts);
            return Message.Length > 0 ? $"{Message}   |   {line}" : line;
        }

        private void OnPlayerFell() => RegisterFall();

        /// <summary>
        /// Falling out of the world already put the pawn back at its respawn point; all that is left is to
        /// count it, so the void costs the same as a hazard does.
        /// </summary>
        public void RegisterFall()
        {
            if (Status != ManaGameStatus.Playing)
            {
                return;
            }

            Deaths++;
            Health = MaxHealth;
            _invulnerableSeconds = RespawnInvulnerableSeconds;
            LastCause = "falling out of the world";
            ShowMessage("Fell out of the world", CauseMessageSeconds);
            SoundRequested?.Invoke(ManaSoundCue.Death);
            SoundRequested?.Invoke(ManaSoundCue.Respawn);
        }
    }
}
