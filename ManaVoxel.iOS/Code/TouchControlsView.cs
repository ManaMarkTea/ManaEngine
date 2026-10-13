using System.Numerics;
using CoreAnimation;
using CoreGraphics;
using Silk.NET.Input;

namespace ManaVoxel.iOS;

/// <summary>
/// On-screen controls over the game, as the web build's touch-controls.js: a thumbstick wherever a thumb lands on
/// the left that holds W/A/S/D, Jump/Run/Camera buttons on the right, and anywhere else a drag that holds the right
/// mouse button to look round -- or, if the finger barely moved, a tap that clicks (the HUD's buttons).
/// </summary>
public sealed class TouchControlsView : UIView
{
    private const float StickRadius = 60;
    private const float TapSlop = 10;

    private readonly IosInput _input;
    private readonly Dictionary<IntPtr, Finger> _fingers = new();
    private readonly CAShapeLayer _stickBase = Circle(StickRadius, 0.10f, 0.35f);
    private readonly CAShapeLayer _stickKnob = Circle(26, 0.35f, 0.6f);
    private readonly (string Label, Key Key, UIView View)[] _buttons;

    /// <summary>
    /// False while a menu is up (the start screen, the pause panel): the stick and buttons hide, a tap clicks and a
    /// drag scrolls, as a finger does on any list.
    /// </summary>
    public Func<bool> GameControls { get; set; } = () => true;

    // ManaUI scrolls 48 points a wheel notch (ScrollViewer.WheelStep).
    private const float PointsPerNotch = 48.0f;

    private sealed class Finger
    {
        public required string Kind;
        public CGPoint Start;
        public double StartTime;
        public bool Moved;
        public Key Key;
        public CGPoint Last;
    }

    public TouchControlsView(IosInput input)
    {
        _input = input;
        MultipleTouchEnabled = true;
        BackgroundColor = UIColor.Clear;
        _stickBase.Hidden = true;
        _stickKnob.Hidden = true;
        Layer.AddSublayer(_stickBase);
        Layer.AddSublayer(_stickKnob);
        _buttons = new[] { ("Jump", Key.Space, Button("Jump")), ("Run", Key.ShiftLeft, Button("Run")), ("Cam", Key.C, Button("Cam")) };
        foreach (var (_, _, view) in _buttons)
        {
            AddSubview(view);
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        // Jump biggest, under the right thumb; Run and Camera above it.
        var insets = SafeAreaInsets;
        var right = Bounds.Width - insets.Right - 24;
        var bottom = Bounds.Height - insets.Bottom - 24;
        Place(_buttons[0].View, right - 84, bottom - 84, 84);
        Place(_buttons[1].View, right - 84 - 76, bottom - 60, 60);
        Place(_buttons[2].View, right - 60, bottom - 84 - 76, 60);
    }

    private static void Place(UIView view, nfloat x, nfloat y, nfloat size)
    {
        view.Frame = new CGRect(x, y, size, size);
        view.Layer.CornerRadius = size / 2;
    }

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
            else if (ButtonAt(at) is { } button)
            {
                finger.Kind = "key";
                finger.Key = button.Key;
                button.View.Alpha = 1.0f;
                _input.Keyboard.Set(button.Key, true);
            }
            else if (at.X < Bounds.Width * 0.4 && at.Y > Bounds.Height * 0.3 && !_fingers.Values.Any(f => f.Kind == "stick"))
            {
                finger.Kind = "stick";
                ShowStick(at, at);
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
            if (finger.Kind == "menu")
            {
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
            }
            else if (finger.Kind == "stick")
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
            else if (finger.Kind == "look")
            {
                if (!finger.Moved && Distance(at, finger.Start) > TapSlop)
                {
                    finger.Moved = true;
                    _input.Mouse.Down(MouseButton.Right);
                }
                _input.Mouse.Move(ToVector(at));
            }
        }
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
                    if (!finger.Moved)
                    {
                        _input.Mouse.Down(MouseButton.Left);
                        _input.Mouse.Up(MouseButton.Left);
                    }
                    break;
                case "key":
                    _input.Keyboard.Set(finger.Key, false);
                    foreach (var button in _buttons.Where(b => b.Key == finger.Key))
                    {
                        button.View.Alpha = 0.55f;
                    }
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

    /// <summary>Shows or hides the game's buttons as menus come and go; the window calls it every frame.</summary>
    public void Refresh()
    {
        var visible = GameControls();
        foreach (var (_, _, view) in _buttons)
        {
            view.Hidden = !visible;
        }
        if (!visible && _fingers.Values.Any(f => f.Kind is "stick" or "key" or "look"))
        {
            Reset();
        }
    }

    /// <summary>Everything let go, when the app leaves the foreground with fingers down.</summary>
    public void Reset()
    {
        _fingers.Clear();
        _stickBase.Hidden = true;
        _stickKnob.Hidden = true;
        _input.ReleaseAll();
    }

    private (string Label, Key Key, UIView View)? ButtonAt(CGPoint at)
    {
        foreach (var button in _buttons)
        {
            // A little larger than drawn: thumbs land near, not on.
            if (button.View.Frame.Inset(-12, -12).Contains(at))
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

    private static CAShapeLayer Circle(float radius, float fill, float stroke) => new()
    {
        Path = UIBezierPath.FromOval(new CGRect(-radius, -radius, radius * 2, radius * 2)).CGPath,
        FillColor = UIColor.FromWhiteAlpha(1, fill).CGColor,
        StrokeColor = UIColor.FromWhiteAlpha(1, stroke).CGColor,
        LineWidth = 2,
    };

    private static UIView Button(string label)
    {
        var view = new UIView
        {
            BackgroundColor = UIColor.FromWhiteAlpha(0.1f, 0.6f),
            Alpha = 0.55f,
            UserInteractionEnabled = false,
        };
        view.Layer.BorderColor = UIColor.FromWhiteAlpha(1, 0.7f).CGColor;
        view.Layer.BorderWidth = 2;
        var text = new UILabel
        {
            Text = label,
            TextColor = UIColor.White,
            TextAlignment = UITextAlignment.Center,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
        };
        if (UIFont.BoldSystemFontOfSize(15) is { } bold)
        {
            text.Font = bold;
        }
        view.AddSubview(text);
        text.Frame = view.Bounds;
        return view;
    }
}
