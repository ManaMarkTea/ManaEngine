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

    /// <summary>Every 2nd depth sample is a mesh vertex: 64 x 48 a photo is plenty for a room.</summary>
    private const int MeshStep = 2;

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
    }

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
        var pose = CameraToWorld(camera.Pose);
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
    /// ARCore's pose is the phone's (x across the screen, y up it, z out of it), but the camera image is the sensor's:
    /// landscape, rotated a quarter turn from that. ScanKeyframe wants the image's own axes (x right, y up, -z ahead).
    /// </summary>
    private static Matrix4x4 CameraToWorld(Pose pose)
    {
        var m = new float[16];
        pose.ToMatrix(m, 0);
        // ARCore fills a column-major matrix, which read straight through is the row-vector form System.Numerics uses.
        var device = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        var image = new Matrix4x4(
            0, -1, 0, 0,
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        return image * device;
    }

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
