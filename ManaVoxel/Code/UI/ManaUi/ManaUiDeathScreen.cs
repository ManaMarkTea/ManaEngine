using Mana.Code;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The "YOU DIED" screen: a blood-red flash, a card that slams in from above with a row of skulls popping
    /// up one at a time, a line of gallows humour picked for how you died, what did it, your running tally, and
    /// a bar counting down to the end of the screen. Also the red flash when you are merely hurt.
    /// <para>
    /// Everything on it is a function of <see cref="ManaGameState.SecondsSinceDeath"/> (or
    /// <see cref="ManaGameState.SecondsSinceHurt"/>), polled each frame, so nothing has to be told to start or
    /// stop: the state notes when you died and the screen works out the rest. The curves are plain static
    /// functions so they can be checked without a window.
    /// </para>
    /// <para>
    /// It never takes input. The player has already been put back at their checkpoint and is free to move --
    /// the screen is a flourish over the top, not a pause.
    /// </para>
    /// </summary>
    public static class ManaUiDeathScreen
    {
        public const int SkullCount = 5;

        private static readonly Vector4 Blood = new(0.92f, 0.10f, 0.12f, 1.0f);

        private static readonly string[] DrownedLines =
        {
            "Sleeps with the fishes.", "Should have taken the ferry.", "Glub glub glub.", "Swimming lessons: cancelled."
        };

        private static readonly string[] SlickLines =
        {
            "That was slippery.", "Oil be seeing you.", "Grip strength: none.", "Should have watched your step."
        };

        private static readonly string[] ZappedLines =
        {
            "Shocking, really.", "The rails were live.", "Static electricity: 1, you: 0.", "Well, that stings."
        };

        private static readonly string[] EnemyLines =
        {
            "Should have jumped on its head.", "Outplayed.", "It had a lot of teeth.", "That one saw you coming."
        };

        private static readonly string[] FallLines =
        {
            "Gravity always wins.", "Mind the gap.", "The ground was very far away.", "Down you go."
        };

        private static readonly string[] GenericLines =
        {
            "Ouch.", "Not your finest moment.", "Try again.", "That could have gone better."
        };

        // ---- content -------------------------------------------------------------------------------------

        /// <summary>The icon beside the cause. Single code points from the emoji font the UI ships with.</summary>
        public static string Icon(ManaDeathFlavor flavor) => flavor switch
        {
            ManaDeathFlavor.Drowned => "\U0001F30A",
            ManaDeathFlavor.Slick => "\U0001F6E2",
            ManaDeathFlavor.Zapped => "\U0001F50C",
            ManaDeathFlavor.Enemy => "\U0001F479",
            ManaDeathFlavor.Fall => "\U0001F4A8",
            _ => "\U0001F4A5"
        };

        /// <summary>A line of gallows humour for how you died. Varies with the death count, so repeat deaths do not repeat a joke.</summary>
        public static string Quip(ManaDeathFlavor flavor, int deaths)
        {
            var lines = flavor switch
            {
                ManaDeathFlavor.Drowned => DrownedLines,
                ManaDeathFlavor.Slick => SlickLines,
                ManaDeathFlavor.Zapped => ZappedLines,
                ManaDeathFlavor.Enemy => EnemyLines,
                ManaDeathFlavor.Fall => FallLines,
                _ => GenericLines
            };

            return lines[Math.Abs(deaths) % lines.Length];
        }

        /// <summary>"Drowned in Harbor Water" and so on, so the cause is named as well as illustrated.</summary>
        public static string CauseLine(ManaDeathFlavor flavor, string cause)
        {
            var name = string.IsNullOrWhiteSpace(cause) ? "something" : cause;
            var verb = flavor switch
            {
                ManaDeathFlavor.Drowned => "Drowned in",
                ManaDeathFlavor.Slick => "Slipped in",
                ManaDeathFlavor.Zapped => "Zapped by",
                ManaDeathFlavor.Enemy => "Defeated by",
                ManaDeathFlavor.Fall => "Lost to",
                _ => "Killed by"
            };

            return $"{Icon(flavor)}  {verb} {name}";
        }

        /// <summary>The skull tally: one skull per death up to a handful, then a count.</summary>
        public static string Tally(int deaths)
        {
            if (deaths <= 0)
            {
                return string.Empty;
            }

            const string skull = "\U0001F480";
            return deaths <= 6
                ? string.Concat(Enumerable.Repeat(skull, deaths)) + $"   {deaths}"
                : $"{skull} × {deaths}";
        }

        // ---- animation -----------------------------------------------------------------------------------

        /// <summary>Opacity of the red wash over the whole screen: a hard flash that decays, over a haze that lingers.</summary>
        public static float WashOpacity(float secondsSinceDeath, float secondsSinceHurt)
        {
            var death = 0.0f;
            if (secondsSinceDeath < ManaGameState.DeathScreenSeconds)
            {
                var ramp = MathF.Min(1.0f, secondsSinceDeath / 0.06f);
                var flash = 0.62f * MathF.Exp(-secondsSinceDeath * 2.4f);
                var haze = 0.16f * (1.0f - (secondsSinceDeath / ManaGameState.DeathScreenSeconds));
                death = ramp * (flash + haze);
            }

            var hurt = 0.0f;
            if (secondsSinceHurt < 0.5f)
            {
                var left = 1.0f - (secondsSinceHurt / 0.5f);
                hurt = 0.32f * left * left;
            }

            return Math.Clamp(MathF.Max(death, hurt), 0.0f, 0.75f);
        }

        /// <summary>Fades the card in fast, holds it, and lets it go over the last stretch.</summary>
        public static float CardOpacity(float secondsSinceDeath)
        {
            if (secondsSinceDeath < 0.0f || secondsSinceDeath >= ManaGameState.DeathScreenSeconds)
            {
                return 0.0f;
            }

            var fadeIn = Math.Clamp(secondsSinceDeath / 0.18f, 0.0f, 1.0f);
            var fadeOut = Math.Clamp((ManaGameState.DeathScreenSeconds - secondsSinceDeath) / 0.6f, 0.0f, 1.0f);
            return MathF.Min(fadeIn, fadeOut);
        }

        /// <summary>Vertical offset of the card in pixels: drops in from above and settles with an ease-out.</summary>
        public static float SlideY(float secondsSinceDeath)
        {
            var progress = Math.Clamp(secondsSinceDeath / 0.32f, 0.0f, 1.0f);
            var eased = 1.0f - MathF.Pow(1.0f - progress, 3.0f);
            return -46.0f * (1.0f - eased);
        }

        /// <summary>A short sideways judder as the card lands, dying away quickly.</summary>
        public static float ShakeX(float secondsSinceDeath)
        {
            return secondsSinceDeath < 0.0f ? 0.0f : MathF.Sin(secondsSinceDeath * 70.0f) * 9.0f * MathF.Exp(-secondsSinceDeath * 9.0f);
        }

        /// <summary>Each skull appears a beat after the last, then bobs gently.</summary>
        public static float SkullOpacity(int index, float secondsSinceDeath)
        {
            var appear = Math.Clamp((secondsSinceDeath - 0.10f - (index * 0.09f)) / 0.12f, 0.0f, 1.0f);
            return appear * CardOpacity(secondsSinceDeath);
        }

        public static float SkullBob(int index, float secondsSinceDeath)
        {
            return MathF.Sin((secondsSinceDeath * 5.0f) + (index * 0.9f)) * 4.0f;
        }

        /// <summary>The bar under the card: full when you died, empty when the screen goes.</summary>
        public static float Countdown(float secondsSinceDeath)
        {
            return Math.Clamp(1.0f - (secondsSinceDeath / ManaGameState.DeathScreenSeconds), 0.0f, 1.0f);
        }

        // ---- the view ------------------------------------------------------------------------------------

        public static UiElement Build(UiTheme theme, ManaGameState state)
        {
            // The wash: a full-screen red rectangle. Nothing hit-tests it, so it can never eat a click.
            var wash = new Border().Fill(new Vector4(0.62f, 0.02f, 0.04f, 1.0f)).StretchH().StretchV();
            wash.IsHitTestVisible = false;
            wash.BindPolled(el => el.Opacity, () => WashOpacity(state.SecondsSinceDeath, state.SecondsSinceHurt));

            var skulls = new StackPanel().Horizontal(6.0f).CenterH();
            for (var i = 0; i < SkullCount; i++)
            {
                var index = i;
                var skull = new TextBlock().FontSize(theme.FontSize + 30.0f).Text("\U0001F480");
                skull.BindPolled(el => el.Opacity, () => SkullOpacity(index, state.SecondsSinceDeath));
                skull.BindPolled(el => el.Margin, () => new Thickness(0.0f, 4.0f + SkullBob(index, state.SecondsSinceDeath), 0.0f, 0.0f));
                skull.IsHitTestVisible = false;
                skulls.Children.Add(skull);
            }

            var title = new TextBlock()
                .FontSize(theme.FontSize + 52.0f)
                .Foreground(Blood)
                .Text("YOU DIED")
                .CenterH();

            var quip = Line(theme, theme.FontSize + 5.0f, new Vector4(1.0f, 0.92f, 0.82f, 0.96f),
                () => Quip(state.LastDeathFlavor, state.Deaths));
            var cause = Line(theme, theme.FontSize + 2.0f, new Vector4(0.95f, 0.66f, 0.62f, 0.95f),
                () => CauseLine(state.LastDeathFlavor, state.LastDeathCause));
            var tally = Line(theme, theme.FontSize + 4.0f, new Vector4(1.0f, 1.0f, 1.0f, 0.9f), () => Tally(state.Deaths));
            var respawn = Line(theme, theme.FontSize - 1.0f, new Vector4(0.8f, 0.82f, 0.9f, 0.75f),
                () => "Back to your checkpoint...");

            var bar = new ProgressBar { Fraction = 1.0f }.Width(300.0f).Height(6.0f).CenterH();
            bar.BindPolled(el => el.Fraction, () => Countdown(state.SecondsSinceDeath));
            bar.IsHitTestVisible = false;

            var column = new StackPanel().Vertical(6.0f);
            column.Children.Add(skulls);
            column.Children.Add(title);
            column.Children.Add(quip);
            column.Children.Add(cause);
            column.Children.Add(tally);
            column.Children.Add(bar);
            column.Children.Add(respawn);

            var card = new Border()
                .Fill(new Vector4(0.07f, 0.01f, 0.02f, 0.86f))
                .Rounded(18.0f)
                .Outline(new Vector4(0.92f, 0.10f, 0.12f, 0.75f), 2.0f)
                .PaddingOf(46.0f, 24.0f)
                .Content(column)
                .CenterH()
                .Top();
            card.IsHitTestVisible = false;
            card.BindPolled(el => el.Opacity, () => CardOpacity(state.SecondsSinceDeath));
            card.BindPolled(el => el.Visibility, () => state.IsShowingDeathScreen ? Visibility.Visible : Visibility.Collapsed);
            card.BindPolled(el => el.Margin, () => new Thickness(ShakeX(state.SecondsSinceDeath), 70.0f + SlideY(state.SecondsSinceDeath), 0.0f, 0.0f));

            var root = new Grid();
            root.Children.Add(wash);
            root.Children.Add(card);
            root.IsHitTestVisible = false;
            return root;
        }

        private static UiElement Line(UiTheme theme, float size, Vector4 color, Func<string> text)
        {
            var block = new TextBlock().FontSize(size).Foreground(color).CenterH();
            block.BindPolled(t => t.Text, text);
            block.BindPolled(t => t.Visibility, () => text().Length > 0 ? Visibility.Visible : Visibility.Collapsed);
            block.IsHitTestVisible = false;
            return block;
        }
    }
}
