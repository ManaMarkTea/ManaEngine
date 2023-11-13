using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaCompiledShader : ManaObject
    {
        static HashSet<string> WarnedNames = new HashSet<string>();
        
        private uint shaderProgram = 0;
        private uint vertexShaderObject = 0;
        private uint fragmentShaderObject = 0;
        private uint geomShaderObject = 0;

        public string VertexShaderCompilerLog { get; set; } = "";
        public string FragmentShaderCompilerLog { get; set; } = "";
        public string GeometryShaderCompilerLog { get; set; } = "";

        public string LinkCompilerLog { get; set; } = "";

        public List<ManaShaderUniform> UniformNames { get; set; } = new List<ManaShaderUniform>();
        public Dictionary<string, ManaShaderUniform> UniformMap { get; set; } = new Dictionary<string, ManaShaderUniform>();
        public List<string> Defines { get; set; } = new List<string>();

        private static class ManaSemantics
        {
            public static ManaSemantic ModelViewProjection = new ManaSemantic(new string[] { "gWVP" });
            public static ManaSemantic ModelViewProjectionMatrix = new ManaSemantic(new string[] { "gWVP" });
            public static ManaSemantic ObjectMatrix = new ManaSemantic(new string[] { "gWorld", "model" });
            public static ManaSemantic ModelViewMatrix = new ManaSemantic(new string[] { "modelViewMatrix" });
            public static ManaSemantic ProjectionMatrix = new ManaSemantic(new string[] { "projectionMatrix", "projection" });
            public static ManaSemantic ViewMatrix = new ManaSemantic(new string[] { "view", "viewMatrix" });
            public static ManaSemantic NormalMatrix = new ManaSemantic(new string[] { "normalMatrix" });
            public static ManaSemantic BaseColor = new ManaSemantic(new string[] { });
            public static ManaSemantic ObjectPosition = new ManaSemantic(new string[] { "objPos", "object", "obj" });
            public static ManaSemantic CameraPosition = new ManaSemantic(new string[] { "camPos", "iCameraPosition", "gEyeWorldPos" });

            // Shader Toy Common Semantics
            public static ManaSemantic iResolution = new ManaSemantic(new string[] { "iResolution" });
            public static ManaSemantic iTime = new ManaSemantic(new string[] { "iTime" });
            public static ManaSemantic iChannelTime = new ManaSemantic(new string[] { "iChannelTime" });
            public static ManaSemantic iChannelResolution = new ManaSemantic(new string[] { "iChannelResolution" });
            public static ManaSemantic iMouse = new ManaSemantic(new string[] { "iMouse" });
            public static ManaSemantic iDate = new ManaSemantic(new string[] { "iDate" });
            public static ManaSemantic iSampleRate = new ManaSemantic(new string[] { "iSampleRate" });
            public static ManaSemantic iFrame = new ManaSemantic(new string[] { "iFrame" });
            public static ManaSemantic iTimeDelta = new ManaSemantic(new string[] { "iTimeDelta" });
            public static ManaSemantic iFrameRate = new ManaSemantic(new string[] { "iFrameRate" });

            public static ManaSemantic iChannel0 = new ManaSemantic(new string[] { "iChannel0", "baseColor", "gColorMap"});
            public static ManaSemantic iChannel1 = new ManaSemantic(new string[] { "iChannel1" });
            public static ManaSemantic iChannel2 = new ManaSemantic(new string[] { "iChannel2" });
        }

        public ManaCompiledShader(ManaContext context) : base(context)
        {
        }

        public virtual void LoadShaderSource(string vertSource, string fragSource, string geomSource = "")
        {
            var gl = Context.gl;

            vertexShaderObject = gl.CreateShader(ShaderType.VertexShader);
            fragmentShaderObject = gl.CreateShader(ShaderType.FragmentShader);
            
            gl.ShaderSource(vertexShaderObject, vertSource);
            gl.ShaderSource(fragmentShaderObject, fragSource);

            gl.CompileShader(vertexShaderObject);
            gl.CompileShader(fragmentShaderObject);

            gl.GetShader(vertexShaderObject, ShaderParameterName.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = gl.GetShaderInfoLog(vertexShaderObject);
                VertexShaderCompilerLog = infoLog;
                Debug.WriteLine(infoLog);
            }

            gl.GetShader(fragmentShaderObject, ShaderParameterName.CompileStatus, out success);
            if (success == 0)
            {
                string infoLog = gl.GetShaderInfoLog(fragmentShaderObject);
                FragmentShaderCompilerLog = infoLog;
                Debug.WriteLine(infoLog);
            }

            shaderProgram = gl.CreateProgram();
            gl.AttachShader(shaderProgram, vertexShaderObject);
            gl.AttachShader(shaderProgram, fragmentShaderObject);


            if (geomSource.Length != 0)
            {
                geomShaderObject = gl.CreateShader(ShaderType.GeometryShader);
                gl.ShaderSource(geomShaderObject, geomSource);
                gl.CompileShader(geomShaderObject);
                gl.GetShader(geomShaderObject, ShaderParameterName.CompileStatus, out success);
                if (success == 0)
                {
                    string infoLog = gl.GetShaderInfoLog(geomShaderObject);
                    GeometryShaderCompilerLog = infoLog;
                    Debug.WriteLine(infoLog);
                }

                gl.AttachShader(shaderProgram, geomShaderObject);
            }

            gl.LinkProgram(shaderProgram);

            gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out success);
            if (success == 0)
            {
                LinkCompilerLog = gl.GetProgramInfoLog(shaderProgram);
                Debug.WriteLine(LinkCompilerLog);
            }

            LoadUniforms();
        }

        public void LoadUniforms()
        {
            var gl = Context.gl;

            gl.GetProgram(shaderProgram, ProgramPropertyARB.ActiveUniforms, out int uniformCount);
            for (uint i = 0; i < uniformCount; i++)
            {
                gl.GetActiveUniform(shaderProgram, i, (uint)64, out uint uniformLength, out int uniformSize, out UniformType type, out string uniformName);

                int uniformLocation = gl.GetUniformLocation(shaderProgram, uniformName);

                ManaShaderUniform uniform = new ManaShaderUniform();
                uniform.shaderLocation = shaderProgram;
                uniform.gl = gl;
                uniform.name = uniformName;
                uniform.size = uniformSize;
                uniform.location = (int)uniformLocation;
                uniform.type = type;

                UniformNames.Add(uniform);
                UniformMap[uniformName] = uniform;

                Trace.WriteLine($"Uniform #{i} Type: {type} Name: {uniformName}");
            }
        }

        public ManaShaderUniform? FindUniform(string name)
        {
            if (UniformMap.ContainsKey(name))
            {
                return UniformMap[name];
            }

            if (!WarnedNames.Contains(name))
            {
                Trace.TraceWarning($"Uniform not found {name}");
                WarnedNames.Add(name);
            }
            return null;
        }

        public void BindShader()
        {
            Context.gl.UseProgram(shaderProgram);
        }

        public unsafe virtual void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            var gl = Context.gl;

            gl.UseProgram(shaderProgram);
            
            foreach (var uniform in UniformNames)
            {
                if (uniform.type == UniformType.Sampler2D)
                {
                    ManaSemantics.iChannel0.Apply(uniform, () => gl.Uniform1(uniform.location, 0));
                    ManaSemantics.iChannel1.Apply(uniform, () => gl.Uniform1(uniform.location, 1));
                    ManaSemantics.iChannel2.Apply(uniform, () => gl.Uniform1(uniform.location, 2));
                }
                else if (uniform.type == UniformType.Int)
                {
                    ManaSemantics.iFrame.Apply(uniform, () => gl.Uniform1(uniform.location, (int)Context.GameMode.UpdateFrame % int.MaxValue));
                }
                else if (uniform.type == UniformType.Float)
                {
                    ManaSemantics.iTime.Apply(uniform, () => gl.Uniform1(uniform.location, (float)ManaTime.GlobalTime.GameTime.TotalSeconds));
                    ManaSemantics.iSampleRate.Apply(uniform, () => gl.Uniform1(uniform.location, 44100.0f));

                    ManaSemantics.iFrameRate.Apply(uniform, () => gl.Uniform1(uniform.location, (float)ManaTime.GlobalTime.FramesPerSecond));
                    ManaSemantics.iTimeDelta.Apply(uniform, () => gl.Uniform1(uniform.location, (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds));
                }
                else if (uniform.type == UniformType.FloatVec3)
                {
                    Vector3 resolutionXYPixelRatio = new Vector3(Context.Window.Size.X, Context.Window.Size.Y, Context.Window.Size.X / Context.Window.Size.Y);
                    resolutionXYPixelRatio = Vector3.One;

                    ManaSemantics.iResolution.Apply(uniform, () => gl.Uniform3(uniform.location, resolutionXYPixelRatio));
                    ManaSemantics.iChannelResolution.ApplyArray(uniform, 4, new Action[] 
                    { 
                        () => gl.Uniform3(uniform.location, resolutionXYPixelRatio),
                        () => gl.Uniform3(uniform.location, resolutionXYPixelRatio),
                        () => gl.Uniform3(uniform.location, resolutionXYPixelRatio),
                        () => gl.Uniform3(uniform.location, resolutionXYPixelRatio)
                    });

                }
                else if (uniform.type == UniformType.FloatVec4)
                {
                    Vector2 mousePos = Context.Input.Mice.First().Position;
                    Vector4 mouseInfo = new Vector4(mousePos.X, mousePos.Y, mousePos.X, mousePos.Y);

                    ManaSemantics.iDate.Apply(uniform, () => gl.Uniform4(uniform.location, ManaTime.GlobalTime.EncodedDateTime));                    
                    ManaSemantics.iMouse.Apply(uniform, () => gl.Uniform4(uniform.location, mouseInfo));
                }

                ManaSemantics.CameraPosition.Apply(uniform, () => gl.Uniform3(uniform.location, camera.Owner.Position));
                
                ManaSemantics.ObjectPosition.Apply(uniform, () => gl.Uniform3(uniform.location, owner.Position));

                ManaSemantics.ModelViewProjectionMatrix.Apply(uniform, () =>
                {
                    Matrix4x4 mvpm = objectMatrix * camera._ViewMatrix * camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, mvpm.ToSpan());
                });

                ManaSemantics.ObjectMatrix.Apply(uniform, () => { gl.UniformMatrix4(uniform.location, 1, false, objectMatrix.ToSpan()); });

                ManaSemantics.ModelViewMatrix.Apply(uniform, () =>
                {
                    Matrix4x4 mvm = objectMatrix * camera._ViewMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, mvm.ToSpan());
                });

                ManaSemantics.ProjectionMatrix.Apply(uniform, () =>
                {
                    Matrix4x4 proj = camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, proj.ToSpan());
                });

                ManaSemantics.ViewMatrix.Apply(uniform, () =>
                {
                    Matrix4x4 view = camera._ViewMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, view.ToSpan());
                });

                ManaSemantics.NormalMatrix.Apply(uniform, () =>
                {
                    Matrix4x4.Invert(objectMatrix, out Matrix4x4 modelInv);
                    var debugModelInverse = Matrix4x4.Transpose(modelInv).ToMatrix3();
                    gl.UniformMatrix3(uniform.location, 1, false, debugModelInverse.ToSpan());
                });

                // Special ones
                if (uniform.name == "gMatSpecularIntensity")
                {
                    var channel = material.Channels["$mat.roughnessFactor"];

                    gl.Uniform1(uniform.location, 1.0f);
                }

                if (uniform.name == "FrameNumber")
                {
                    gl.Uniform1(uniform.location, Context.GameMode.UpdateFrame);
                }

                if (uniform.name == "ViewParams")
                {
                    gl.Uniform3(uniform.location, camera.ViewParams);
                }

                if (uniform.name == "gPointLight.Base.Color")
                {
                    gl.Uniform3(uniform.location, new Vector3(1.0f, 1.0f, 1.0f));
                }
                else if (uniform.name == "gPointLight.Position")
                {
                    gl.Uniform3(uniform.location, new Vector3(1.0f, 1.0f, 1.0f));
                }
                else if (uniform.name == "gPointLight.Atten.Constant")
                {
                    gl.Uniform1(uniform.location, 1.0f);
                }
                else if (uniform.name == "gCameraWorldMatrix")
                {
                    //gl.Uniform3(uniform.location, camera._WorldMatrix.Translation);
                    var mat = camera.Owner.WorldTransform;
                    gl.UniformMatrix4(uniform.location, 1, true, (float*)&mat);
                }
                else if (uniform.name == "gProjectionMatrix")
                {
                    //gl.Uniform3(uniform.location, camera._WorldMatrix.Translation);
                    var mat = camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, true, (float*)&mat);
                }
                else if (uniform.name == "gCameraWorldMatrixProjection")
                {
                    //gl.Uniform3(uniform.location, camera._WorldMatrix.Translation);
                    var mat = camera.Owner.WorldTransform * camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, true, (float*)&mat);
                }
                else if (uniform.name == "gPointLight.Base.DiffuseIntensity")
                {
                    gl.Uniform1(uniform.location, 2.75f);
                }
            }
        }

        public void UnbindShader()
        {
            Context.gl.UseProgram(0);
        }

    }
}
