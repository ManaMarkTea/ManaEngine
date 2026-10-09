using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;
using ManaVoxel.Code.Gameplay.TrainYardWars.View;

namespace ManaVoxel.Code.Gameplay.TrainYardWars
{
    /// <summary>
    /// The couch: any gamepad joins by pressing A or Start, taking the next seat a bot was keeping warm, and plays with
    /// a cursor of its own -- stick to move it, D-pad to step hex by hex, A to do what the label beside it says. The
    /// mouse player stays Red. A script can stand in for a pad (<c>tyw_pad</c>), which is how this is tested without one.
    /// </summary>
    public sealed partial class TrainYardWarsMode
    {
        private const float StickDeadzone = 0.22f;
        private const float StepRepeatSeconds = 0.16f;

        /// <summary>One couch player: the pad they hold and where their cursor is.</summary>
        private sealed class PadSeat
        {
            public int Seat;
            public IGamepad? Pad;
            public int ScriptPad = -1;
            public string Name = "";
            public Vector3 Cursor;
            public bool TorchTool;
            public HashSet<ButtonName> Down = new();
            public float StepCooldown;
            public string Flash = "";
            public float FlashUntil;
            public string Label = "";
        }

        /// <summary>What a pad is doing this frame: the left stick, and the buttons held.</summary>
        private sealed class PadState
        {
            public Vector2 Stick;
            public HashSet<ButtonName> Down = new();
        }

        private readonly List<PadSeat> _padSeats = new();
        private readonly Dictionary<int, PadState> _scriptPads = new();
        // Buttons held on pads that have not joined, so joining is a press, not a hold carried over.
        private readonly Dictionary<object, HashSet<ButtonName>> _idleButtons = new();

        private static readonly ButtonName[] JoinButtons = { ButtonName.A, ButtonName.Start };

        private static PadState Read(IGamepad pad)
        {
            var state = new PadState();
            if (pad.Thumbsticks.Count > 0)
            {
                var stick = pad.Thumbsticks[0];
                state.Stick = new Vector2(stick.X, stick.Y);
            }

            foreach (var button in pad.Buttons)
            {
                if (button.Pressed)
                {
                    state.Down.Add(button.Name);
                }
            }

            return state;
        }

        // A radial deadzone, then a curve: small tilts creep for precision, full tilt crosses the board quickly.
        private static Vector2 Shape(Vector2 stick)
        {
            var length = stick.Length();
            if (length < StickDeadzone)
            {
                return Vector2.Zero;
            }

            var scaled = MathF.Min(1.0f, (length - StickDeadzone) / (1.0f - StickDeadzone));
            return stick / length * scaled * scaled;
        }

        /// <summary>A new map or a replay: the people on pads keep their seats.</summary>
        private void ResetSeats()
        {
            var game = _game;
            if (game == null)
            {
                return;
            }

            _padSeats.RemoveAll(seat => seat.Seat >= game.Players.Count);
            foreach (var seat in _padSeats)
            {
                game.Players[seat.Seat].IsBot = false;
                seat.Cursor = TywGeometry.Center(game.Players[seat.Seat].Depot) with { Y = 0.0f };
                seat.TorchTool = false;
                _hud.SeatLabels[seat.Seat] = seat.Name;
            }
        }

        private void HandlePads(TywGame game, float dt)
        {
            var connected = Context.Input.Gamepads.Where(pad => pad.IsConnected).ToList();

            // A pad that went away gives its seat back to a bot.
            foreach (var seat in _padSeats.Where(s => s.Pad != null && !connected.Contains(s.Pad)).ToList())
            {
                Leave(game, seat, "lost their controller");
            }

            // Unjoined pads: A or Start joins.
            foreach (var pad in connected.Where(pad => _padSeats.All(s => s.Pad != pad)))
            {
                if (Pressed(pad, Read(pad).Down, JoinButtons))
                {
                    Join(game, pad, -1);
                }
            }

            foreach (var (index, state) in _scriptPads)
            {
                if (_padSeats.All(s => s.ScriptPad != index) && Pressed(index, state.Down, JoinButtons))
                {
                    Join(game, null, index);
                }
            }

            foreach (var seat in _padSeats.ToList())
            {
                var state = seat.Pad != null ? Read(seat.Pad) : _scriptPads.TryGetValue(seat.ScriptPad, out var scripted) ? scripted : new PadState();
                UpdateSeat(game, seat, state, dt);
            }
        }

        // An edge: true the frame a button goes down, for pads not yet holding a seat.
        private bool Pressed(object key, HashSet<ButtonName> down, ButtonName[] buttons)
        {
            _idleButtons.TryGetValue(key, out var was);
            _idleButtons[key] = new HashSet<ButtonName>(down);
            return buttons.Any(b => down.Contains(b) && (was == null || !was.Contains(b)));
        }

        private void Join(TywGame game, IGamepad? pad, int scriptPad)
        {
            var redIdle = !_mousePlayed && !_hud.Autoplay && _padSeats.All(s => s.Seat != HumanSeat);
            var free = redIdle
                ? game.Players[HumanSeat]
                : game.Players.FirstOrDefault(p => p.Index != HumanSeat && p.IsBot && _padSeats.All(s => s.Seat != p.Index));
            if (free == null)
            {
                Say("The table is full -- add players in the level's Train Yard Wars settings");
                return;
            }

            var number = _padSeats.Count + 2;
            var seat = new PadSeat
            {
                Seat = free.Index,
                Pad = pad,
                ScriptPad = scriptPad,
                Name = $"pad {number - 1}",
                Cursor = TywGeometry.Center(free.Depot) with { Y = 0.0f }
            };

            // The press that joined must not also act.
            seat.Down = new HashSet<ButtonName>(JoinButtons);
            _padSeats.Add(seat);
            free.IsBot = false;
            _hud.SeatLabels[free.Index] = seat.Name;
            var color = Vector4.Lerp(TywBoardView.PlayerColors[free.Index], Vector4.One, 0.3f);
            _popups?.Add(TywGeometry.Center(free.Depot) + new Vector3(0, 0.6f, 0), $"{free.Name} joined!", color, _clock, big: true, seconds: 4.0f);
            _sounds?.Play(TywSounds.Confirm, 0.7f);
            Say($"{free.Name} joined on {seat.Name}: stick moves, A builds, X buys a train, Y torches, Back leaves");
        }

        private void Leave(TywGame game, PadSeat seat, string why)
        {
            _padSeats.Remove(seat);
            // Red goes back to the mouse; any other seat to a bot.
            game.Players[seat.Seat].IsBot = seat.Seat != HumanSeat;
            _hud.SeatLabels.Remove(seat.Seat);
            _view?.ShowCursor(seat.Seat, null, null, Vector4.One, _clock);
            _view?.ShowPlan(seat.Seat, null, Vector4.One);
            _popups?.SetLabel(seat.Seat, null, "", Vector4.One);
            Say($"{game.Players[seat.Seat].Name} {why}; " + (seat.Seat == HumanSeat ? "the seat is the mouse's again" : "a bot takes the seat"));
        }

        private void UpdateSeat(TywGame game, PadSeat seat, PadState state, float dt)
        {
            bool Edge(ButtonName button) => state.Down.Contains(button) && !seat.Down.Contains(button);
            var player = game.Players[seat.Seat];
            var color = TywBoardView.PlayerColors[seat.Seat];

            if (Edge(ButtonName.Back))
            {
                seat.Down = new HashSet<ButtonName>(state.Down);
                Leave(game, seat, "left");
                return;
            }

            if (Edge(ButtonName.Start))
            {
                if (game.IsOver)
                {
                    Restart(Environment.TickCount & 0xffff);
                    seat.Down = new HashSet<ButtonName>(state.Down);
                    return;
                }

                _hud.Paused = !_hud.Paused;
                _hud.MessageUntil = 0.0f;
            }

            // The stick glides; the D-pad steps a hex at a time (repeating while held). Slower when zoomed in.
            var stick = Shape(state.Stick);
            seat.Cursor += new Vector3(stick.X, 0.0f, stick.Y) * (38.0f * MathF.Max(0.3f, _zoom) * dt);
            var step = Vector3.Zero;
            if (state.Down.Contains(ButtonName.DPadUp)) { step.Z -= 1.0f; }
            if (state.Down.Contains(ButtonName.DPadDown)) { step.Z += 1.0f; }
            if (state.Down.Contains(ButtonName.DPadLeft)) { step.X -= 1.0f; }
            if (state.Down.Contains(ButtonName.DPadRight)) { step.X += 1.0f; }
            // Let go, and the next tap steps at once: the wait is only for repeating while held.
            seat.StepCooldown = step == Vector3.Zero ? 0.0f : seat.StepCooldown - dt;
            if (step != Vector3.Zero && seat.StepCooldown <= 0.0f)
            {
                var from = TywHex.FromWorld(new Vector2(seat.Cursor.X, seat.Cursor.Z), TywGeometry.HexRadius);
                var stepTo = seat.Cursor + Vector3.Normalize(step) * TywGeometry.HexRadius * TywHex.Sqrt3;
                var to = TywHex.FromWorld(new Vector2(stepTo.X, stepTo.Z), TywGeometry.HexRadius);
                seat.Cursor = TywGeometry.Center(to == from ? from : to) with { Y = 0.0f };
                seat.StepCooldown = seat.Down.Overlaps(new[] { ButtonName.DPadUp, ButtonName.DPadDown, ButtonName.DPadLeft, ButtonName.DPadRight })
                    ? StepRepeatSeconds
                    : StepRepeatSeconds * 2.2f;
            }

            var reach = _mapHalf * 0.98f;
            seat.Cursor = new Vector3(
                Math.Clamp(seat.Cursor.X, _mapCenter.X - reach.X, _mapCenter.X + reach.X),
                0.0f,
                Math.Clamp(seat.Cursor.Z, _mapCenter.Z - reach.Y, _mapCenter.Z + reach.Y));

            if (Edge(ButtonName.Y))
            {
                seat.TorchTool = !seat.TorchTool;
                _sounds?.Play(TywSounds.Toggle, 0.4f);
            }

            if (Edge(ButtonName.B))
            {
                seat.TorchTool = false;
            }

            if (Edge(ButtonName.X) && !_hud.Paused && !game.IsOver && game.BuyTrain(seat.Seat) is { } noTrain)
            {
                Flash(seat, $"No train: {noTrain}");
            }

            var hexAt = TywHex.FromWorld(new Vector2(seat.Cursor.X, seat.Cursor.Z), TywGeometry.HexRadius);
            TywHex? hex = game.Board.Contains(hexAt) ? hexAt : null;
            TywAction? action = null;
            TywTrackPlan? plan = null;
            if (hex is { } at && !game.IsOver)
            {
                action = seat.TorchTool ? game.TorchActionAt(seat.Seat, at) : game.ActionAt(seat.Seat, at);
                if (!seat.TorchTool && action.Kind == TywActionKind.Track)
                {
                    plan = PlanFor(game, seat.Seat, at);
                }
            }

            var shown = plan is { NewLinks.Count: > 0 } ? plan : null;
            _view?.ShowPlan(seat.Seat, shown?.Path,
                shown == null || player.CanAfford(shown.Cost) ? Vector4.Lerp(color, Vector4.One, 0.35f) : new Vector4(1.0f, 0.25f, 0.2f, 1.0f));
            _view?.ShowCursor(seat.Seat, seat.Cursor, hex, color, _clock);

            if (Edge(ButtonName.A) && hex is { } target && !game.IsOver)
            {
                if (_hud.Paused)
                {
                    Flash(seat, "Paused -- Start to play on");
                }
                else if (seat.TorchTool)
                {
                    if (game.PlaceTorch(seat.Seat, target) is { } why)
                    {
                        Flash(seat, $"Torch: {why}");
                    }
                }
                else if (action?.Kind == TywActionKind.Track)
                {
                    if (shown == null || game.BuildTrack(seat.Seat, shown) == 0)
                    {
                        Flash(seat, shown == null ? "No way to lay track there" : $"Can't afford track ({TywCosts.Format(shown.Cost)})");
                    }
                }
                else if (game.Act(seat.Seat, target) is { } why)
                {
                    Flash(seat, $"{action?.Label}: {why}");
                }
            }

            var label = _clock < seat.FlashUntil ? seat.Flash : PadHint(game, seat, hex, action, shown);
            var labelColor = _clock < seat.FlashUntil ? new Vector4(1.0f, 0.55f, 0.45f, 1.0f) : Vector4.Lerp(color, Vector4.One, 0.45f);
            seat.Label = label;
            _popups?.SetLabel(seat.Seat, seat.Cursor with { Y = TywGeometry.TopY }, label, labelColor);
            seat.Down = new HashSet<ButtonName>(state.Down);
        }

        private void Flash(PadSeat seat, string text)
        {
            seat.Flash = text;
            seat.FlashUntil = _clock + 2.0f;
            _sounds?.Play(TywSounds.Error, 0.4f);
        }

        // Short: it floats over the board beside the cursor, among everyone else's.
        private static string PadHint(TywGame game, PadSeat seat, TywHex? hex, TywAction? action, TywTrackPlan? plan)
        {
            var name = game.Players[seat.Seat].Name;
            var tool = seat.TorchTool ? "Torch  " : "";
            if (hex == null)
            {
                return $"{name}: {tool}";
            }

            if (plan != null)
            {
                var afford = game.Players[seat.Seat].CanAfford(plan.Cost) ? "" : " (short)";
                return $"{name}: A lays {plan.NewLinks.Count} hex{(plan.NewLinks.Count == 1 ? "" : "es")} -- {TywCosts.Format(plan.Cost)}{afford}";
            }

            if (action is { Kind: not (TywActionKind.None or TywActionKind.Track) })
            {
                return $"{name}: {tool}A {action.Label.ToLowerInvariant()} -- {TywCosts.Format(action.Cost)}" +
                       (action.Blocked != null ? $" ({action.Blocked})" : "");
            }

            var tile = game.Board[hex.Value]!;
            return $"{name}: {tool}{(tile.Building == TywBuilding.None ? tile.Terrain.ToString() : tile.Building.ToString())}";
        }
    }
}
