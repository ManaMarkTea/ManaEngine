using System.Numerics;
using CoreAnimation;
using CoreGraphics;
using Mana.Code.GameModes;
using Mana.Code.Input;
using Silk.NET.Input;

namespace ManaVoxel.iOS;

/// <summary>
/// On-screen controls over the game, in two schemes the level's modes choose (<see cref="ManaTouchControls"/>):
/// <list type="bullet">
/// <item>Stick: a bar along the bottom, below the game (which draws only above it), holding a thumbstick wherever a
/// thumb lands on its left that holds W/A/S/D and a row of coloured buttons on its right that light up and tick under
/// the thumb; over the game, a drag holds the right mouse button to look round.</item>
/// <item>Swipe (the runner): nothing on screen; a swipe left, right, up or down taps that arrow key.</item>
/// </list>
/// In both, a finger that barely moves is a tap that clicks (the HUD's buttons), and while a menu is up a drag scrolls.
/// </summary>
public sealed class TouchControlsView : UIView
{
    private const float StickRadius = 60;
    private const float TapSlop = 10;
    private const float SwipeDistance = 26;

    private readonly IosInput _input;
    private readonly Dictionary<IntPtr, Finger> _fingers = new();
    private readonly CAShapeLayer _stickBase = Circle(StickRadius, UIColor.FromWhiteAlpha(1, 0.12f), UIColor.FromWhiteAlpha(1, 0.45f));
    private readonly CAShapeLayer _stickKnob = Circle(28, UIColor.FromRGBA(0.35f, 0.75f, 1.0f, 0.85f), UIColor.White);
    private readonly TouchButton[] _buttons;
    private readonly UIView _bar = new() { UserInteractionEnabled = false };
    private readonly CAGradientLayer _barFill = new()
    {
        Colors = new[] { UIColor.FromRGB(0.16f, 0.15f, 0.27f).CGColor, UIColor.FromRGB(0.08f, 0.07f, 0.14f).CGColor },
    };
    private readonly CALayer _barEdge = new() { BackgroundColor = UIColor.FromWhiteAlpha(1, 0.22f).CGColor };
    // Where the stick lives, while no thumb is on it.
    private readonly CAShapeLayer _stickHome = Circle(StickRadius * 0.75f, UIColor.FromWhiteAlpha(1, 0.05f), UIColor.FromWhiteAlpha(1, 0.25f));
    private string _shown = string.Empty;

    // The bar's own height, above the home indicator's strip.
    private const float BarContent = 124;

    /// <summary>
    /// False while a menu is up (the start screen, the pause panel): the stick and buttons hide, a tap clicks and a
    /// drag scrolls, as a finger does on any list.
    /// </summary>
    public Func<bool> GameControls { get; set; } = () => true;

    /// <summary>Which controls the level's modes want: the stick and buttons, or swipes.</summary>
    public Func<ManaTouchControls> Scheme { get; set; } = () => ManaTouchControls.Stick;

    /// <summary>The player has a bow: the Shoot button shows.</summary>
    public Func<bool> ShowsShoot { get; set; } = () => false;

    /// <summary>A side-on level: the camera is fixed, so the Cam button (which orbits it) is hidden.</summary>
    public Func<bool> SideOn { get; set; } = () => false;

    /// <summary>The bar is up: in play, with the stick and buttons (swipes and menus have the whole screen).</summary>
    public bool ShowsBar => GameControls() && !Swiping;

    /// <summary>How much of the bottom the bar covers, in points; the game draws above it.</summary>
    public nfloat BarHeight => ShowsBar ? BarContent + SafeAreaInsets.Bottom : 0;

    // ManaUI scrolls 48 points a wheel notch (ScrollViewer.WheelStep).
    private const float PointsPerNotch = 48.0f;

    private sealed class Finger
    {
        public required string Kind;
        public CGPoint Start;
        public double StartTime;
        public bool Moved;
        public TouchButton? Button;
        public CGPoint Last;
    }

    /// <summary>A round button with a colour of its own that brightens and sinks while held.</summary>
    private sealed class TouchButton
    {
        public required Key Key { get; init; }
        public required UIView View { get; init; }
        public required UIColor Color { get; init; }
        public int Holds;
    }

    public TouchControlsView(IosInput input)
    {
        _input = input;
        MultipleTouchEnabled = true;
        BackgroundColor = UIColor.Clear;
        _bar.Layer.AddSublayer(_barFill);
        _bar.Layer.AddSublayer(_barEdge);
        AddSubview(_bar);
        Layer.AddSublayer(_stickHome);
        _stickBase.Hidden = true;
        _stickKnob.Hidden = true;
        Layer.AddSublayer(_stickBase);
        Layer.AddSublayer(_stickKnob);
        _buttons = new[]
        {
            MakeButton("⬆︎\nJump", Key.Space, UIColor.FromRGB(0.22f, 0.78f, 0.45f)),
            MakeButton("»\nRun", Key.ShiftLeft, UIColor.FromRGB(0.24f, 0.55f, 1.0f)),
            MakeButton("◎\nCam", Key.C, UIColor.FromRGB(0.62f, 0.38f, 0.95f)),
            // The Bow's fire key: hold to draw, let go to loose (stick up aims high, as W does).
            MakeButton("🏹\nShoot", Key.F, UIColor.FromRGB(0.95f, 0.55f, 0.18f)),
        };
        foreach (var button in _buttons)
        {
            AddSubview(button.View);
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        var insets = SafeAreaInsets;
        var top = Bounds.Height - BarContent - insets.Bottom;
        var middle = top + BarContent / 2;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _bar.Frame = new CGRect(0, top, Bounds.Width, Bounds.Height - top);
        _barFill.Frame = _bar.Bounds;
        _barEdge.Frame = new CGRect(0, 0, Bounds.Width, 1.5);
        _stickHome.Position = new CGPoint(insets.Left + 28 + StickRadius, middle);
        CATransaction.Commit();
        // In a row across the bar's right: Jump biggest under the thumb, the rest leftwards from it.
        var x = Bounds.Width - insets.Right - 20 - 88;
        Place(_buttons[0].View, x, middle - 44, 88);
        foreach (var button in _buttons.Skip(1).Where(b => !b.View.Hidden))
        {
            x -= 14 + 66;
            Place(button.View, x, middle - 33, 66);
        }
    }

    private static void Place(UIView view, nfloat x, nfloat y, nfloat size)
    {
        view.Transform = CGAffineTransform.MakeIdentity();
        view.Frame = new CGRect(x, y, size, size);
        view.Layer.CornerRadius = size / 2;
    }

    private bool Swiping => Scheme() == ManaTouchControls.Swipe;

    public override void TouchesBegan(NSSet touches, UIEvent? evt)
    {
        foreach (UITouch touch in touches)
        {
            var at = touch.LocationInView(this);
            var finger = new Finger { Kind = "look", Start = at, Last = at, StartTime = touch.Timestamp };
            if (!GameControls())
            {
                finger.Kind = _fingers.Values.Any(f => f.Kind == "menu") ? "ignored" : "menu";
                if (finger.Kind == "menu")
                {
                    _input.Mouse.Move(ToVector(at));
                }
            }
            else if (Swiping)
            {
                finger.Kind = "swipe";
                _input.Mouse.Move(ToVector(at));
            }
            else if (ButtonAt(at) is { } button)
            {
                finger.Kind = "key";
                finger.Button = button;
                Press(button, true);
            }
            else if (at.Y >= _bar.Frame.Y - 12 && at.X < Bounds.Width * 0.5 && !_fingers.Values.Any(f => f.Kind == "stick"))
            {
                finger.Kind = "stick";
                ShowStick(at, at);
            }
            else if (at.Y >= _bar.Frame.Y)
            {
                // The bar between the stick and the buttons: nothing there to touch.
                finger.Kind = "ignored";
            }
            else if (!_fingers.Values.Any(f => f.Kind == "look"))
            {
                _input.Mouse.Move(ToVector(at));
            }
            else
            {
                finger.Kind = "ignored";
            }
            _fingers[touch.Handle] = finger;
        }
    }

    public override void TouchesMoved(NSSet touches, UIEvent? evt)
    {
        foreach (UITouch touch in touches)
        {
            if (!_fingers.TryGetValue(touch.Handle, out var finger))
            {
                continue;
            }
            var at = touch.LocationInView(this);
            switch (finger.Kind)
            {
                case "menu":
                    if (!finger.Moved && Distance(at, finger.Start) > TapSlop)
                    {
                        finger.Moved = true;
                    }
                    if (finger.Moved)
                    {
                        // Content follows the finger: dragging up scrolls down the list.
                        _input.Mouse.Wheel(0, (float)(at.Y - finger.Last.Y) / PointsPerNotch);
                    }
                    finger.Last = at;
                    break;
                case "swipe" when !finger.Moved && Distance(at, finger.Start) > SwipeDistance:
                    // One swipe a touch, as soon as it is one -- waiting for the finger to lift feels late.
                    finger.Moved = true;
                    var dx = at.X - finger.Start.X;
                    var dy = at.Y - finger.Start.Y;
                    var key = Math.Abs(dx) > Math.Abs(dy) ? (dx < 0 ? Key.Left : Key.Right) : (dy < 0 ? Key.Up : Key.Down);
                    _input.Keyboard.Set(key, true);
                    _input.Keyboard.Set(key, false);
                    ManaRumble.Play(ManaRumbleStrength.Tap);
                    break;
                case "stick":
                    MoveStick(finger, at);
                    break;
                case "look":
                    if (!finger.Moved && Distance(at, finger.Start) > TapSlop)
                    {
                        finger.Moved = true;
                        _input.Mouse.Down(MouseButton.Right);
                    }
                    _input.Mouse.Move(ToVector(at));
                    break;
            }
        }
    }

    private void MoveStick(Finger finger, CGPoint at)
    {
        var dx = (float)(at.X - finger.Start.X);
        var dy = (float)(at.Y - finger.Start.Y);
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length > StickRadius)
        {
            dx *= StickRadius / length;
            dy *= StickRadius / length;
        }
        ShowStick(finger.Start, new CGPoint(finger.Start.X + dx, finger.Start.Y + dy));
        var threshold = StickRadius * 0.3f;
        _input.Keyboard.Set(Key.A, dx < -threshold);
        _input.Keyboard.Set(Key.D, dx > threshold);
        _input.Keyboard.Set(Key.W, dy < -threshold);
        _input.Keyboard.Set(Key.S, dy > threshold);
    }

    public override void TouchesEnded(NSSet touches, UIEvent? evt) => End(touches);

    public override void TouchesCancelled(NSSet touches, UIEvent? evt) => End(touches);

    private void End(NSSet touches)
    {
        foreach (UITouch touch in touches)
        {
            if (!_fingers.Remove(touch.Handle, out var finger))
            {
                continue;
            }
            switch (finger.Kind)
            {
                case "menu":
                case "swipe":
                    if (!finger.Moved)
                    {
                        _input.Mouse.Down(MouseButton.Left);
                        _input.Mouse.Up(MouseButton.Left);
                    }
                    break;
                case "key" when finger.Button != null:
                    Press(finger.Button, false);
                    break;
                case "stick":
                    foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D })
                    {
                        _input.Keyboard.Set(key, false);
                    }
                    _stickBase.Hidden = true;
                    _stickKnob.Hidden = true;
                    break;
                case "look":
                    if (finger.Moved)
                    {
                        _input.Mouse.Up(MouseButton.Right);
                    }
                    else if (touch.Timestamp - finger.StartTime < 0.4)
                    {
                        // A tap: a click where it landed, for the HUD.
                        _input.Mouse.Down(MouseButton.Left);
                        _input.Mouse.Up(MouseButton.Left);
                    }
                    break;
            }
        }
    }

    /// <summary>Holds a button's key, and shows it: brighter, a little sunk, a tick under the thumb.</summary>
    private void Press(TouchButton button, bool down)
    {
        button.Holds = Math.Max(0, button.Holds + (down ? 1 : -1));
        var held = button.Holds > 0;
        _input.Keyboard.Set(button.Key, held);
        if (down)
        {
            ManaRumble.Play(ManaRumbleStrength.Tap);
        }
        UIView.Animate(held ? 0.06 : 0.14, () =>
        {
            button.View.Transform = held ? CGAffineTransform.MakeScale(0.88f, 0.88f) : CGAffineTransform.MakeIdentity();
            button.View.BackgroundColor = held ? Brighter(button.Color) : button.Color.ColorWithAlpha(0.82f);
            button.View.Layer.BorderColor = UIColor.FromWhiteAlpha(1, held ? 1.0f : 0.75f).CGColor;
        });
    }

    /// <summary>Shows the scheme's controls, and hides them while a menu is up; the window calls it every frame.</summary>
    public void Refresh()
    {
        var game = GameControls();
        var buttons = game && !Swiping;
        var shown = string.Empty;
        foreach (var button in _buttons)
        {
            button.View.Hidden = !buttons || (button.Key == Key.F && !ShowsShoot()) || (button.Key == Key.C && SideOn());
            shown += button.View.Hidden ? "0" : "1";
        }
        if (shown != _shown)
        {
            // A button came or went: the row closes up.
            _shown = shown;
            SetNeedsLayout();
        }
        _bar.Hidden = !buttons;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _stickHome.Hidden = !buttons || _fingers.Values.Any(f => f.Kind == "stick");
        CATransaction.Commit();
        if (!game && _fingers.Values.Any(f => f.Kind is "stick" or "key" or "look" or "swipe"))
        {
            Reset();
        }
    }

    /// <summary>Everything let go, when the app leaves the foreground with fingers down.</summary>
    public void Reset()
    {
        foreach (var finger in _fingers.Values.Where(f => f.Button != null))
        {
            finger.Button!.Holds = 1;
            Press(finger.Button, false);
        }
        _fingers.Clear();
        _stickBase.Hidden = true;
        _stickKnob.Hidden = true;
        _input.ReleaseAll();
    }

    private TouchButton? ButtonAt(CGPoint at)
    {
        foreach (var button in _buttons)
        {
            // A little larger than drawn: thumbs land near, not on. The frame is the unsunk one.
            if (!button.View.Hidden && button.View.Frame.Inset(-14, -14).Contains(at))
            {
                return button;
            }
        }
        return null;
    }

    private void ShowStick(CGPoint centre, CGPoint knob)
    {
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _stickBase.Position = centre;
        _stickKnob.Position = knob;
        _stickBase.Hidden = false;
        _stickKnob.Hidden = false;
        CATransaction.Commit();
    }

    private static Vector2 ToVector(CGPoint point) => new((float)point.X, (float)point.Y);

    private static double Distance(CGPoint a, CGPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static UIColor Brighter(UIColor color)
    {
        color.GetRGBA(out var r, out var g, out var b, out _);
        return UIColor.FromRGBA((nfloat)Math.Min(1.0, r + 0.25), (nfloat)Math.Min(1.0, g + 0.25), (nfloat)Math.Min(1.0, b + 0.25), 1.0f);
    }

    private static CAShapeLayer Circle(float radius, UIColor fill, UIColor stroke) => new()
    {
        Path = UIBezierPath.FromOval(new CGRect(-radius, -radius, radius * 2, radius * 2)).CGPath,
        FillColor = fill.CGColor,
        StrokeColor = stroke.CGColor,
        LineWidth = 2.5f,
        ShadowColor = UIColor.Black.CGColor,
        ShadowOpacity = 0.3f,
        ShadowRadius = 6,
        ShadowOffset = new CGSize(0, 2),
    };

    private static TouchButton MakeButton(string label, Key key, UIColor color)
    {
        var view = new UIView
        {
            BackgroundColor = color.ColorWithAlpha(0.82f),
            UserInteractionEnabled = false,
        };
        view.Layer.BorderColor = UIColor.FromWhiteAlpha(1, 0.75f).CGColor;
        view.Layer.BorderWidth = 2.5f;
        view.Layer.ShadowColor = UIColor.Black.CGColor;
        view.Layer.ShadowOpacity = 0.35f;
        view.Layer.ShadowRadius = 6;
        view.Layer.ShadowOffset = new CGSize(0, 3);
        var text = new UILabel
        {
            Text = label,
            TextColor = UIColor.White,
            TextAlignment = UITextAlignment.Center,
            Lines = 2,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
        };
        if (UIFont.BoldSystemFontOfSize(14) is { } bold)
        {
            text.Font = bold;
        }
        view.AddSubview(text);
        text.Frame = view.Bounds;
        return new TouchButton { Key = key, View = view, Color = color };
    }
}
