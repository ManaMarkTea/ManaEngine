using System.Text.Json;
using ManaCore.Graphics;
#if MANA_BROWSER
using ManaCore.Browser;
#endif

namespace ManaVoxel.Code.UI
{
    /// <summary>
    /// The editor's grid snapping: one switch, and a separate step for each kind of transform.
    /// <para>
    /// Applies to placing a model, to the move gizmo, to the rotate gizmo and to the scale gizmo alike --
    /// this replaced a "snap models to grid" checkbox that only placement ever read. The sizes are a taste
    /// preference, so they are remembered across runs (see <see cref="Restore"/>); tests and headless hosts
    /// simply never call it and get plain in-memory defaults.
    /// </para>
    /// </summary>
    public sealed class EditorSnapSettings
    {
        public const float DefaultTranslateStep = 1.0f;
        public const float DefaultRotationStepDegrees = 15.0f;
        public const float DefaultScaleStep = 0.1f;

        // A step of zero would divide by it and a negative one would flip every snapped value, so each
        // setter clamps rather than trusting whatever a text box was typed.
        public const float MinimumTranslateStep = 0.01f;
        public const float MinimumRotationStepDegrees = 0.1f;
        public const float MinimumScaleStep = 0.01f;

        private const string SettingsFileName = "manaui-snap.json";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private bool _enabled;
        private float _translateStep = DefaultTranslateStep;
        private float _rotationStepDegrees = DefaultRotationStepDegrees;
        private float _scaleStep = DefaultScaleStep;
        private float _gizmoSize = 1.0f;

        /// <summary>Manual world-size multiplier for transform handles, independent of camera distance.</summary>
        public float GizmoSize
        {
            get => _gizmoSize;
            set => SetStep(ref _gizmoSize, float.IsFinite(value) ? Math.Clamp(value, 0.25f, 3.0f) : 1.0f, 0.25f);
        }
        private string? _persistPath;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    Persist();
                }
            }
        }

        /// <summary>World units between grid lines when moving or placing.</summary>
        public float TranslateStep
        {
            get => _translateStep;
            set => SetStep(ref _translateStep, value, MinimumTranslateStep);
        }

        public float RotationStepDegrees
        {
            get => _rotationStepDegrees;
            set => SetStep(ref _rotationStepDegrees, value, MinimumRotationStepDegrees);
        }

        public float ScaleStep
        {
            get => _scaleStep;
            set => SetStep(ref _scaleStep, value, MinimumScaleStep);
        }

        /// <summary>The value on the nearest multiple of <paramref name="step"/>.</summary>
        public static float SnapTo(float value, float step)
        {
            return step > 0.0f ? MathF.Round(value / step) * step : value;
        }

        /// <summary>Each axis snapped independently, or the point untouched while snapping is off.</summary>
        public Vector3 SnapPosition(Vector3 position)
        {
            if (!_enabled)
            {
                return position;
            }

            return new Vector3(
                SnapTo(position.X, _translateStep),
                SnapTo(position.Y, _translateStep),
                SnapTo(position.Z, _translateStep));
        }

        /// <summary>
        /// How far to drag a move handle so the object lands on the grid. <paramref name="delta"/> is the
        /// distance the pointer asked for along <paramref name="axis"/> from <paramref name="pivot"/>.
        /// <para>
        /// When the handle points along a world axis it is the pivot's own coordinate that snaps, so the
        /// object ends up on a grid line rather than merely moving in grid-sized jumps from wherever it
        /// started off-grid. A handle turned by the object's rotation has no grid line to land on, so only
        /// the distance snaps.
        /// </para>
        /// </summary>
        public float SnapDragDistance(Vector3 pivot, Vector3 axis, float delta)
        {
            if (!_enabled)
            {
                return delta;
            }

            if (MathF.Abs(axis.X) > 0.9999f)
            {
                return SnapAlong(pivot.X, axis.X, delta);
            }

            if (MathF.Abs(axis.Y) > 0.9999f)
            {
                return SnapAlong(pivot.Y, axis.Y, delta);
            }

            if (MathF.Abs(axis.Z) > 0.9999f)
            {
                return SnapAlong(pivot.Z, axis.Z, delta);
            }

            return SnapTo(delta, _translateStep);
        }

        // alignment is +-1 for a world-aligned handle, so dividing by it turns the coordinate change back
        // into a distance along the handle (and flips it for a handle pointing down an axis).
        private float SnapAlong(float start, float alignment, float delta)
        {
            var snapped = SnapTo(start + alignment * delta, _translateStep);
            return (snapped - start) / alignment;
        }

        public float SnapRotationDegrees(float degrees)
        {
            return _enabled ? SnapTo(degrees, _rotationStepDegrees) : degrees;
        }

        /// <summary>
        /// A scale on the step, but never collapsed to zero -- a snapped-away scale would make the object
        /// vanish, and the smallest scale the grid can express is one step.
        /// </summary>
        public float SnapScale(float scale)
        {
            return _enabled ? MathF.Max(_scaleStep, SnapTo(scale, _scaleStep)) : scale;
        }

        /// <summary>
        /// Loads what was saved last time and, from here on, saves every change. Kept apart from the
        /// constructor so the view model can be built in a test without reading (or overwriting) the
        /// user's real preferences.
        /// </summary>
        public void Restore(string? filePath = null)
        {
            _persistPath = null;

            var path = filePath ?? ManaUiSettingsStorage.SharedSettingsFilePath(SettingsFileName);

            try
            {
                if (ReadStored(path) is { } json && JsonSerializer.Deserialize<StoredSettings>(json, SerializerOptions) is { } stored)
                {
                    _enabled = stored.Enabled;
                    _translateStep = Math.Max(MinimumTranslateStep, stored.TranslateStep);
                    _rotationStepDegrees = Math.Max(MinimumRotationStepDegrees, stored.RotationStepDegrees);
                    _scaleStep = Math.Max(MinimumScaleStep, stored.ScaleStep);
                    GizmoSize = stored.GizmoSize;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A missing or corrupt file just means defaults. Preferences are never worth a failed start.
            }

            _persistPath = path;
        }

        private void SetStep(ref float field, float value, float minimum)
        {
            // NaN fails both comparisons, so it is caught here too rather than being saved as a step.
            var clamped = value >= minimum ? value : minimum;
            if (field != clamped)
            {
                field = clamped;
                Persist();
            }
        }

        private void Persist()
        {
            if (_persistPath == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(
                new StoredSettings(_enabled, _translateStep, _rotationStepDegrees, _scaleStep, _gizmoSize),
                SerializerOptions);

            try
            {
#if MANA_BROWSER
                ManaBrowserSettingsBridge.Set(Path.GetFileName(_persistPath), json);
#else
                File.WriteAllText(_persistPath, json);
#endif
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Same as reading: losing a preference is not worth surfacing over.
            }
        }

        private static string? ReadStored(string path)
        {
#if MANA_BROWSER
            // localStorage is the one store on this target that survives a reload, keyed by file name.
            var text = ManaBrowserSettingsBridge.TryGet(Path.GetFileName(path));
            return string.IsNullOrEmpty(text) ? null : text;
#else
            return File.Exists(path) ? File.ReadAllText(path) : null;
#endif
        }

        private sealed record StoredSettings(
            bool Enabled,
            float TranslateStep,
            float RotationStepDegrees,
            float ScaleStep,
            float GizmoSize = 1.0f);
    }
}
