using System.Numerics;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// Moving round a scan by touch, in place of SceneKit's own camera control (whose two-finger pan was unreliable).
/// Orbit: one finger turns round a pivot, two fingers slide the pivot, pinch closes in. Walk: one finger looks round
/// from where you stand, two fingers move you forward, back and sideways at the same height, pinch steps forward.
/// Press and hold a spot to go there. Single taps are left alone, for measuring.
/// </summary>
public sealed class ScanCamera
{
    private const float MinPitch = -1.45f;
    private const float MaxPitch = 1.45f;

    private readonly SCNView _view;
    private readonly SCNNode _node;
    private readonly Func<CGPoint, Vector3?> _pick;
    private Vector3 _pivot;
    private Vector3 _position;
    private float _yaw;
    private float _pitch = -0.5f;
    private float _distance = 4f;
    private CGPoint _lastOne;
    private CGPoint _lastTwo;
    private nfloat _lastScale = 1;

    /// <param name="pick">The surface point under a screen point, for press-and-hold; null over nothing.</param>
    public ScanCamera(SCNView view, SCNNode node, Func<CGPoint, Vector3?> pick)
    {
        _view = view;
        _node = node;
        _pick = pick;

        var one = new UIPanGestureRecognizer(OnOneFinger) { MinimumNumberOfTouches = 1, MaximumNumberOfTouches = 1 };
        var two = new UIPanGestureRecognizer(OnTwoFingers) { MinimumNumberOfTouches = 2, MaximumNumberOfTouches = 2 };
        var pinch = new UIPinchGestureRecognizer(OnPinch);
        var hold = new UILongPressGestureRecognizer(OnHold) { MinimumPressDuration = 0.45 };
        // A two-finger drag and a pinch are the same hands doing both at once.
        two.ShouldRecognizeSimultaneously = (_, _) => true;
        pinch.ShouldRecognizeSimultaneously = (_, _) => true;
        foreach (var gesture in new UIGestureRecognizer[] { one, two, pinch, hold })
        {
            view.AddGestureRecognizer(gesture);
        }
    }

    public bool Walking { get; private set; }

    /// <summary>Looks at a scan from above and in front, far enough back to see all of it.</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        _pivot = (min + max) * 0.5f;
        _distance = MathF.Max((max - min).Length() * 0.9f, 1.5f);
        _yaw = 0;
        _pitch = -0.6f;
        Walking = false;
        Apply();
    }

    /// <summary>Orbit round what is in front, or walk from where the camera is.</summary>
    public void SetWalking(bool walking)
    {
        if (walking == Walking)
        {
            return;
        }
        if (walking)
        {
            // Stand where the camera is, but at eye height above the floor (y = 0) and looking level-ish.
            _position = _pivot - Forward() * _distance;
            _position.Y = MathF.Max(_position.Y, 0.3f);
            _pitch = Math.Clamp(_pitch, -0.35f, 0.35f);
        }
        else
        {
            _distance = 2.5f;
            _pivot = _position + Forward() * _distance;
        }
        Walking = walking;
        Apply();
    }

    /// <summary>Straight down from above, for a plan view.</summary>
    public void TopDown()
    {
        if (Walking)
        {
            SetWalking(false);
        }
        _pitch = MinPitch;
        Apply();
    }

    private void OnOneFinger(UIPanGestureRecognizer gesture)
    {
        var at = gesture.TranslationInView(_view);
        if (gesture.State == UIGestureRecognizerState.Began)
        {
            _lastOne = at;
            return;
        }
        var dx = (float)(at.X - _lastOne.X);
        var dy = (float)(at.Y - _lastOne.Y);
        _lastOne = at;
        // Walking looks round more gently: it is the view you are standing in, not a model in your hand.
        var rate = Walking ? 0.005f : 0.008f;
        _yaw -= dx * rate;
        _pitch = Math.Clamp(_pitch - dy * rate * (Walking ? -1 : 1), MinPitch, MaxPitch);
        Apply();
    }

    private void OnTwoFingers(UIPanGestureRecognizer gesture)
    {
        var at = gesture.TranslationInView(_view);
        if (gesture.State == UIGestureRecognizerState.Began)
        {
            _lastTwo = at;
            return;
        }
        var dx = (float)(at.X - _lastTwo.X);
        var dy = (float)(at.Y - _lastTwo.Y);
        _lastTwo = at;
        var right = Right();
        if (Walking)
        {
            var ahead = Vector3.Normalize(new Vector3(Forward().X, 0, Forward().Z));
            _position += (ahead * -dy + right * -dx) * 0.006f;
        }
        else
        {
            var up = Vector3.Cross(right, Forward());
            _pivot += (right * -dx + up * dy) * (_distance * 0.0016f);
        }
        Apply();
    }

    private void OnPinch(UIPinchGestureRecognizer gesture)
    {
        if (gesture.State == UIGestureRecognizerState.Began)
        {
            _lastScale = gesture.Scale;
            return;
        }
        var change = (float)(gesture.Scale / _lastScale);
        _lastScale = gesture.Scale;
        if (Walking)
        {
            _position += Forward() * (change - 1f) * 2.5f;
        }
        else
        {
            _distance = Math.Clamp(_distance / change, 0.15f, 60f);
        }
        Apply();
    }

    private void OnHold(UILongPressGestureRecognizer gesture)
    {
        if (gesture.State != UIGestureRecognizerState.Began || _pick(gesture.LocationInView(_view)) is not { } target)
        {
            return;
        }
        SCNTransaction.Begin();
        SCNTransaction.AnimationDuration = 0.35;
        if (Walking)
        {
            // Up to the spot, stopping short of it, at the height already stood at.
            var to = new Vector3(target.X, _position.Y, target.Z);
            var gap = to - _position;
            if (gap.Length() > 0.8f)
            {
                _position = to - Vector3.Normalize(gap) * 0.8f;
            }
        }
        else
        {
            _pivot = target;
            _distance = MathF.Min(_distance, 1.5f);
        }
        Apply();
        SCNTransaction.Commit();
    }

    private Vector3 Forward() => new(
        -MathF.Sin(_yaw) * MathF.Cos(_pitch),
        MathF.Sin(_pitch),
        -MathF.Cos(_yaw) * MathF.Cos(_pitch));

    private Vector3 Right() => new(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw));

    private void Apply()
    {
        var eye = Walking ? _position : _pivot - Forward() * _distance;
        var target = eye + Forward();
        _node.Position = new SCNVector3(eye.X, eye.Y, eye.Z);
        _node.Look(new SCNVector3(target.X, target.Y, target.Z));
    }
}
