using System.Globalization;
using ManaCore.Graphics;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The sky's clock, beside the wall clock on the menu bar: the time of day it shows, a list of fixed times
    /// to jump to, play and pause, and how fast the day runs -- real time, or so much sky time a second.
    /// Any of them turns the Day and Night sky on.
    /// </summary>
    public static class SkyClockManaView
    {
        private static readonly (string Name, double Hours)[] Times =
        {
            ("Dawn", 5.5), ("Sunrise", 6.2), ("Morning", 9.0), ("Midday", 12.0), ("Afternoon", 15.0),
            ("Sunset", 17.85), ("Dusk", 18.6), ("Night", 21.0), ("Midnight", 0.0)
        };

        // Sky time a real second, on the clock's own units: a day at 1 hr/s takes 24 s. Index 0 is the wall clock.
        private static readonly (string Name, float Speed, bool RealTime)[] Speeds =
        {
            ("Real time", 1.0f, true),
            ("1 min/s  (24 min day)", 60.0f, false),
            ("5 min/s  (4.8 min day)", 300.0f, false),
            ("10 min/s  (2.4 min day)", 600.0f, false),
            ("30 min/s  (48 s day)", 1800.0f, false),
            ("1 hr/s  (24 s day)", 3600.0f, false),
            ("2 hr/s  (12 s day)", 7200.0f, false),
            ("6 hr/s  (4 s day)", 21600.0f, false)
        };

        public static UiElement Build(UiTheme theme, ManaIrradianceSettings sky)
        {
            // The speed play goes back to after a pause: 5 min/s unless something else was running.
            var resume = 2;

            bool Cycling() => sky.Sky == ManaSkyMode.DayNightCycle;
            bool Paused() => !Cycling() || (!sky.RealTime && sky.TimeSpeed == 0.0f);

            int Running() => !Cycling() || Paused()
                ? -1
                : Array.FindIndex(Speeds, speed => speed.RealTime == sky.RealTime && (speed.RealTime || speed.Speed == sky.TimeSpeed));

            void Run(int index)
            {
                sky.Sky = ManaSkyMode.DayNightCycle;
                sky.RealTime = Speeds[index].RealTime;
                sky.TimeSpeed = Speeds[index].Speed;
            }

            void Hold()
            {
                if (Running() is >= 0 and var index)
                {
                    resume = index;
                }
                sky.Sky = ManaSkyMode.DayNightCycle;
                sky.RealTime = false;
                sky.TimeSpeed = 0.0f;
            }

            // Shows the sky's own time while nothing is picked; a pick jumps there and holds, then clears
            // itself so the box goes back to showing the time.
            var times = new ComboBox { Width = 132.0f };
            times.SetItems(Times, time => time.Name);
            times.SelectedIndex = -1;
            times.BindPolled(c => c.Placeholder, () => Cycling()
                ? $"{(ManaSky.Evaluate(sky).TowardSun.Y > 0.0f ? "Day" : "Night")}  {Format(ManaSky.HoursAt(sky.TimeOfDay))}"
                : "Time of day");
            times.SelectionChanged += (_, index) =>
            {
                if (times.IsApplyingBinding || index < 0 || index >= Times.Length)
                {
                    return;
                }

                Hold();
                sky.TimeOfDay = ManaSky.PhaseAt(Times[index].Hours);
                times.SelectedIndex = -1;
            };
            times.WithTooltip("The sky's time of day. Pick one to jump there and hold it; play runs the day on.");

            var play = ToolbarIcons.IconButton(
                theme,
                (drawList, box, colour) =>
                {
                    if (Paused())
                    {
                        DrawPlay(drawList, box, colour);
                    }
                    else
                    {
                        DrawPause(drawList, box, colour);
                    }
                },
                "Play or pause the day.",
                () => false,
                () =>
                {
                    if (Paused())
                    {
                        Run(resume);
                    }
                    else
                    {
                        Hold();
                    }
                });

            // What is running, or -- while held -- what play will run.
            var speeds = new ComboBox { Width = 172.0f };
            speeds.SetItems(Speeds, speed => speed.Name);
            speeds.BindPolled(c => c.SelectedIndex, () => Running() is >= 0 and var index ? index : resume);
            speeds.SelectionChanged += (_, index) =>
            {
                if (!speeds.IsApplyingBinding && index >= 0 && index < Speeds.Length)
                {
                    resume = index;
                    Run(index);
                }
            };
            speeds.WithTooltip("How fast the day runs, in sky time a second -- or real time, following the wall clock.");

            return new StackPanel().Horizontal(2.0f).Children(times.CenterV(), play.CenterV(), speeds.CenterV());
        }

        private static string Format(float hours)
        {
            var time = DateTime.Today.AddHours(hours);
            return time.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }

        private static void DrawPlay(ManaUiDrawList drawList, UiRect box, Vector4 colour)
        {
            const float thickness = 1.6f;
            var left = box.X + box.Width * 0.34f;
            var right = box.X + box.Width * 0.72f;
            var top = box.Y + box.Height * 0.26f;
            var bottom = box.Y + box.Height * 0.74f;
            var middle = box.Y + box.Height * 0.5f;
            drawList.DrawLine(new Vector2(left, top), new Vector2(left, bottom), colour, thickness);
            drawList.DrawLine(new Vector2(left, top), new Vector2(right, middle), colour, thickness);
            drawList.DrawLine(new Vector2(left, bottom), new Vector2(right, middle), colour, thickness);
        }

        private static void DrawPause(ManaUiDrawList drawList, UiRect box, Vector4 colour)
        {
            var width = box.Width * 0.12f;
            var top = box.Y + box.Height * 0.27f;
            var height = box.Height * 0.46f;
            drawList.FillRect(new UiRect(box.X + box.Width * 0.34f, top, width, height), colour, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.X + box.Width * 0.54f, top, width, height), colour, new CornerRadius(1.0f));
        }
    }
}
