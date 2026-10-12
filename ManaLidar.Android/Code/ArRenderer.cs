using System.Numerics;
using Android.Content;
using Android.Opengl;
using Android.Views;
using Google.AR.Core;
using Google.AR.Core.Exceptions;
using Image = global::Android.Media.Image;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace ManaLidar.Droid;

/// <summary>
/// Draws ARCore's camera image full screen and, while capturing, keeps a keyframe (small photo, depth, pose) whenever the
/// phone has moved enough, turning each into a mesh piece with <see cref="ScanDepthMesher"/>.
/// </summary>
public sealed class ArRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const string VertexShader = """
        attribute vec4 a_position;
        attribute vec2 a_uv;
        varying vec2 v_uv;
        void main() { gl_Position = a_position; v_uv = a_uv; }
        """;

    private const string FragmentShader = """
        #extension GL_OES_EGL_image_external : require
        precision mediump float;
        varying vec2 v_uv;
        uniform samplerExternalOES u_texture;
        void main() { gl_FragColor = texture2D(u_texture, v_uv); }
        """;

    private const string LineVertexShader = """
        uniform mat4 u_mvp;
        attribute vec3 a_pos;
        void main() { gl_Position = u_mvp * vec4(a_pos, 1.0); }
        """;

    private const string LineFragmentShader = """
        precision mediump float;
        void main() { gl_FragColor = vec4(0.2, 0.95, 1.0, 0.55); }
        """;

    private static readonly float[] QuadNdc = { -1, -1, +1, -1, -1, +1, +1, +1 };

    private readonly Context _context;
    private readonly ScanRecording _recording;
    private readonly FloatBuffer _quadNdc;
    private readonly FloatBuffer _quadUv;
    private int _textureId;
    private int _program;
    private int _positionAttribute;
    private int _uvAttribute;
    private int _width;
    private int _height;
    private int _depthImagesSeen;
    private int _busy;
    private int _lineProgram;
    private int _linePosition;
    private int _lineMvp;
    private readonly System.Collections.Concurrent.ConcurrentQueue<ScanMeshPiece> _newPieces = new();
    private readonly List<(int Vbo, int Ibo, int Count)> _overlay = new();
    private readonly float[] _view = new float[16];
    private readonly float[] _projection = new float[16];
    private readonly float[] _mvp = new float[16];
    private volatile bool _clearOverlay;

    /// <summary>Every 2nd depth sample is a mesh vertex: 64 x 48 a photo is plenty for a room.</summary>
    private const int MeshStep = 2;
    private const int OverlayPieces = 5;

    public ArRenderer(Context context, ScanRecording recording)
    {
        _context = context;
        _recording = recording;
        _quadNdc = ToBuffer(QuadNdc);
        _quadUv = ToBuffer(new float[8]);
    }

    public Session? Session { get; set; }
    public volatile bool Capturing;
    public volatile string? Hint;

    /// <summary>Photos kept from now on, the same size the iPhone keeps at Standard detail.</summary>
    public (int Width, int Height) PhotoSize = ScanDetailSettings.PhotoSize(ScanDetail.Standard);

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        GLES20.GlClearColor(0.05f, 0.07f, 0.1f, 1f);
        var textures = new int[1];
        GLES20.GlGenTextures(1, textures, 0);
        _textureId = textures[0];
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _textureId);
        GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
        GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
        GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureMinFilter, GLES20.GlLinear);
        GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureMagFilter, GLES20.GlLinear);

        _program = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(_program, Compile(GLES20.GlVertexShader, VertexShader));
        GLES20.GlAttachShader(_program, Compile(GLES20.GlFragmentShader, FragmentShader));
        GLES20.GlLinkProgram(_program);
        _positionAttribute = GLES20.GlGetAttribLocation(_program, "a_position");
        _uvAttribute = GLES20.GlGetAttribLocation(_program, "a_uv");

        _lineProgram = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(_lineProgram, Compile(GLES20.GlVertexShader, LineVertexShader));
        GLES20.GlAttachShader(_lineProgram, Compile(GLES20.GlFragmentShader, LineFragmentShader));
        GLES20.GlLinkProgram(_lineProgram);
        _linePosition = GLES20.GlGetAttribLocation(_lineProgram, "a_pos");
        _lineMvp = GLES20.GlGetUniformLocation(_lineProgram, "u_mvp");
        // The context is new, so any old buffers are gone with it.
        _overlay.Clear();
    }

    /// <summary>The next frame drops the wireframe, for New Scan.</summary>
    public void ClearOverlay() => _clearOverlay = true;

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _width = width;
        _height = height;
        GLES20.GlViewport(0, 0, width, height);
        var rotation = (_context.GetSystemService(Context.WindowService) as IWindowManager)?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0;
        Session?.SetDisplayGeometry((int)rotation, width, height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);
        var session = Session;
        if (session == null)
        {
            return;
        }
        try
        {
            session.SetCameraTextureName(_textureId);
            var frame = session.Update();
            if (frame.HasDisplayGeometryChanged)
            {
                frame.TransformCoordinates2d(Coordinates2d.OpenglNormalizedDeviceCoordinates, _quadNdc, Coordinates2d.TextureNormalized, _quadUv);
            }
            if (frame.Timestamp != 0)
            {
                DrawBackground();
            }

            var camera = frame.Camera;
            if (camera.TrackingState == TrackingState.Tracking)
            {
                DrawOverlay(camera);
            }
            Hint = camera.TrackingState == TrackingState.Tracking ? null : $"Tracking lost: {camera.TrackingFailureReason}";
            if (Capturing && camera.TrackingState == TrackingState.Tracking)
            {
                Capture(frame);
            }
        }
        catch (Exception e)
        {
            Hint = e.Message;
        }
    }

    private void DrawOverlay(Camera camera)
    {
        if (_clearOverlay)
        {
            _clearOverlay = false;
            foreach (var (vbo, ibo, _) in _overlay)
            {
                GLES20.GlDeleteBuffers(2, new[] { vbo, ibo }, 0);
            }
            _overlay.Clear();
            while (_newPieces.TryDequeue(out _))
            {
            }
        }
        // Upload a couple of pieces a frame, so a burst never stalls the camera.
        for (var i = 0; i < 2 && _newPieces.TryDequeue(out var piece); i++)
        {
            _overlay.Add(Upload(piece));
        }
        // Every photo's sheet overlaps the last: only the newest few stay drawn, so the view reads as what is being scanned now.
        while (_overlay.Count > OverlayPieces)
        {
            var (vbo, ibo, _) = _overlay[0];
            GLES20.GlDeleteBuffers(2, new[] { vbo, ibo }, 0);
            _overlay.RemoveAt(0);
        }
        if (_overlay.Count == 0)
        {
            return;
        }

        camera.GetViewMatrix(_view, 0);
        camera.GetProjectionMatrix(_projection, 0, 0.05f, 30f);
        Matrix.MultiplyMM(_mvp, 0, _projection, 0, _view, 0);
        GLES20.GlUseProgram(_lineProgram);
        GLES20.GlUniformMatrix4fv(_lineMvp, 1, false, _mvp, 0);
        GLES20.GlEnable(GLES20.GlBlend);
        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
        GLES20.GlEnableVertexAttribArray(_linePosition);
        foreach (var (vbo, ibo, count) in _overlay)
        {
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, vbo);
            GLES20.GlVertexAttribPointer(_linePosition, 3, GLES20.GlFloat, false, 0, 0);
            GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, ibo);
            GLES20.GlDrawElements(GLES20.GlLines, count, GLES20.GlUnsignedShort, 0);
        }
        GLES20.GlDisableVertexAttribArray(_linePosition);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, 0);
        GLES20.GlDisable(GLES20.GlBlend);
    }

    /// <summary>A piece's triangle edges as GL lines; pieces are in world space, so the camera's matrices place them.</summary>
    private static (int Vbo, int Ibo, int Count) Upload(ScanMeshPiece piece)
    {
        var vertices = new float[piece.Positions.Length * 3];
        for (var i = 0; i < piece.Positions.Length; i++)
        {
            vertices[i * 3] = piece.Positions[i].X;
            vertices[i * 3 + 1] = piece.Positions[i].Y;
            vertices[i * 3 + 2] = piece.Positions[i].Z;
        }
        var lines = new short[piece.Indices.Length * 2];
        for (var t = 0; t < piece.Indices.Length; t += 3)
        {
            for (var e = 0; e < 3; e++)
            {
                lines[t * 2 + e * 2] = (short)piece.Indices[t + e];
                lines[t * 2 + e * 2 + 1] = (short)piece.Indices[t + (e + 1) % 3];
            }
        }
        var buffers = new int[2];
        GLES20.GlGenBuffers(2, buffers, 0);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, buffers[0]);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, vertices.Length * sizeof(float), ToBuffer(vertices), GLES20.GlStaticDraw);
        var index = ByteBuffer.AllocateDirect(lines.Length * sizeof(short))!.Order(ByteOrder.NativeOrder())!.AsShortBuffer()!;
        index.Put(lines);
        index.Position(0);
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, buffers[1]);
        GLES20.GlBufferData(GLES20.GlElementArrayBuffer, lines.Length * sizeof(short), index, GLES20.GlStaticDraw);
        return (buffers[0], buffers[1], lines.Length);
    }

    private void DrawBackground()
    {
        GLES20.GlDisable(GLES20.GlDepthTest);
        GLES20.GlUseProgram(_program);
        GLES20.GlActiveTexture(GLES20.GlTexture0);
        GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, _textureId);
        _quadNdc.Position(0);
        _quadUv.Position(0);
        GLES20.GlVertexAttribPointer(_positionAttribute, 2, GLES20.GlFloat, false, 0, _quadNdc);
        GLES20.GlVertexAttribPointer(_uvAttribute, 2, GLES20.GlFloat, false, 0, _quadUv);
        GLES20.GlEnableVertexAttribArray(_positionAttribute);
        GLES20.GlEnableVertexAttribArray(_uvAttribute);
        GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
        GLES20.GlDisableVertexAttribArray(_positionAttribute);
        GLES20.GlDisableVertexAttribArray(_uvAttribute);
    }

    private void Capture(Frame frame)
    {
        var camera = frame.Camera;
        var pose = CameraToWorld(camera);
        var seconds = frame.Timestamp / 1e9;
        if (!_recording.WantsKeyframe(pose, seconds))
        {
            return;
        }
        // The depth image is only there some frames after the camera starts; no depth, no keyframe.
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }
        Image? depthImage = null;
        Image? colorImage = null;
        try
        {
            depthImage = frame.AcquireDepthImage16Bits();
            colorImage = frame.AcquireCameraImage();
            if (_depthImagesSeen++ == 0)
            {
                global::Android.Util.Log.Info("ManaLidar", $"first keyframe: camera {colorImage.Width}x{colorImage.Height}, depth {depthImage.Width}x{depthImage.Height}");
            }
            var raw = ImageConvert.Copy(colorImage, depthImage, camera.ImageIntrinsics);
            var (width, height) = PhotoSize;
            // _busy holds further frames back until this keyframe is in the recording.
            Task.Run(() =>
            {
                try
                {
                    var keyframe = ImageConvert.ToKeyframe(raw, pose, seconds, width, height);
                    _recording.AddKeyframe(keyframe);
                    if (ScanDepthMesher.Build(keyframe, MeshStep) is { } piece)
                    {
                        _recording.SetPiece(piece);
                        _newPieces.Enqueue(piece);
                    }
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            });
        }
        catch (Exception e) when (e is NotYetAvailableException or DeadlineExceededException)
        {
            Volatile.Write(ref _busy, 0);
        }
        finally
        {
            depthImage?.Close();
            colorImage?.Close();
        }
    }

    /// <summary>
    /// ScanKeyframe wants the camera image's own axes (x right, y up, -z ahead), but the camera image is the sensor's:
    /// landscape, a quarter turn from the display. Which pose ARCore reports and which turn fixes it is checked on the
    /// device by tapping the status text (<see cref="Variant"/>), because the docs leave it open.
    /// </summary>
    private Matrix4x4 CameraToWorld(Camera camera)
    {
        var m = new float[16];
        (Variant < 4 ? camera.DisplayOrientedPose : camera.Pose).ToMatrix(m, 0);
        // ARCore fills a column-major matrix, which read straight through is the row-vector form System.Numerics uses.
        var device = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        return Turns[Variant % 4] * device;
    }

    // Image axes in the pose's frame, row-vector form: a quarter turn each way, none, and a half turn.
    private static readonly Matrix4x4[] Turns =
    {
        new(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
        new(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
        Matrix4x4.Identity,
        new(-1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
    };

    /// <summary>0..7: pose source (display-oriented, then plain) times the four turns; tap the status text to step.</summary>
    public volatile int Variant;

    public string VariantName => $"{(Variant < 4 ? "display pose" : "sensor pose")} / turn {Variant % 4}";

    private static int Compile(int type, string source)
    {
        var shader = GLES20.GlCreateShader(type);
        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);
        return shader;
    }

    private static FloatBuffer ToBuffer(float[] values)
    {
        var buffer = ByteBuffer.AllocateDirect(values.Length * sizeof(float))!.Order(ByteOrder.NativeOrder())!.AsFloatBuffer()!;
        buffer.Put(values);
        buffer.Position(0);
        return buffer;
    }
}
