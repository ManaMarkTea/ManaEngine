using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Text;

namespace Mana
{
    public class ManaSemantic
    {
        public string[] Names = { };

        public ManaSemantic(string[] names)
        {
            Names = names;
        }

        public void Apply(ManaShaderUniform uniform, Action action)
        {
            foreach (var modelViewProjectUniform in Names)
            {
                if (uniform.name.Equals(modelViewProjectUniform, StringComparison.OrdinalIgnoreCase))
                {
                    action();
                    break;
                }
            }
        }
    }

    public class ManaShader : ManaObject
    {


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

        public List<string> Defines { get; set; }

        private static ManaSemantic ModelViewProjectionSemantics = new ManaSemantic(new string[] { "gWVP" });
        private static ManaSemantic ModelViewProjectionMatrixSemantics = new ManaSemantic(new string[] { "gWVP" });
        private static ManaSemantic ObjectMatrixSemantics = new ManaSemantic(new string[] { "gWorld", "model" });
        private static ManaSemantic ModelViewMatrixSemantics = new ManaSemantic(new string[] { "modelViewMatrix" });
        private static ManaSemantic ProjectionMatrixSemantics = new ManaSemantic(new string[] { "projectionMatrix", "projection" });
        private static ManaSemantic ViewMatrixSemantics = new ManaSemantic(new string[] { "view", "viewMatrix" });
        private static ManaSemantic NormalMatrixSemantics = new ManaSemantic(new string[] { "normalMatrix" });
        private static ManaSemantic BaseColorSemantics = new ManaSemantic(new string[] { "baseColor", "gColorMap" });
        private static ManaSemantic CameraPositionSemantics = new ManaSemantic(new string[] { "camPos", "gEyeWorldPos" });

        public ManaShader(ManaContext context) : base(context)
        {
            this.Defines = new List<string>();
        }

        protected virtual string CleanSource(string source) { return source; }

        public virtual void LoadShader(string vertPath, string fragPath, string geomShader = "")
        {
            var vertexSource = File.ReadAllText(vertPath, Encoding.UTF8);
            var fragSource = File.ReadAllText(fragPath, Encoding.UTF8);
            var geomSource = "";
            if (geomShader.Length != 0)
            {
                geomSource = File.ReadAllText(geomShader, Encoding.UTF8);
            }
            LoadShaderSource(vertexSource, fragSource, geomSource);
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

                //Trace.WriteLine($"Uniform #{i} Type: {type} Name: {uniformName}");
            }
        }

        static HashSet<string> WarnedNames = new HashSet<string>();

        public ManaShaderUniform FindUniform(string name)
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

        static long frame = 0;

        public unsafe virtual void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            var gl = Context.gl;

            gl.UseProgram(shaderProgram);

            foreach (var uniform in UniformNames)
            {
                if (uniform.type == UniformType.Sampler2D)
                {
                    BaseColorSemantics.Apply(uniform, () => gl.Uniform1(uniform.location, 0));
                }

                if (uniform.name == "gMatSpecularIntensity")
                {
                    var channel = material.Channels["$mat.roughnessFactor"];

                    gl.Uniform1(uniform.location, 1.0f);
                }

                if (uniform.name == "numSpheres")
                {
                    gl.Uniform1(uniform.location, 6);
                }

                if (uniform.name == "FrameNumber")
                {

                    gl.Uniform1(uniform.location, frame++);
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

                CameraPositionSemantics.Apply(uniform, () => gl.Uniform3(uniform.location, camera.Owner.Position));

                ModelViewProjectionMatrixSemantics.Apply(uniform, () =>
                {
                    Matrix4x4 mvpm = objectMatrix * camera._ViewMatrix * camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, mvpm.ToSpan());
                });

                ObjectMatrixSemantics.Apply(uniform, () => { gl.UniformMatrix4(uniform.location, 1, false, objectMatrix.ToSpan()); });

                ModelViewMatrixSemantics.Apply(uniform, () =>
                {
                    Matrix4x4 mvm = objectMatrix * camera._ViewMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, mvm.ToSpan());
                });

                ProjectionMatrixSemantics.Apply(uniform, () =>
                {
                    Matrix4x4 proj = camera._ProjectionMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, proj.ToSpan());
                });

                ViewMatrixSemantics.Apply(uniform, () =>
                {
                    Matrix4x4 view = camera._ViewMatrix;
                    gl.UniformMatrix4(uniform.location, 1, false, view.ToSpan());
                });

                NormalMatrixSemantics.Apply(uniform, () =>
                {
                    Matrix4x4.Invert(objectMatrix, out Matrix4x4 modelInv);
                    var debugModelInverse = Matrix4x4.Transpose(modelInv).ToMatrix3();
                    gl.UniformMatrix3(uniform.location, 1, false, debugModelInverse.ToSpan());
                });
            }
        }

        public void UnbindShader()
        {
            Context.gl.UseProgram(0);
        }



        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();
        public static Dictionary<string, string> shaderFiles = new Dictionary<string, string>();
        protected static void CacheShader(string shaderFile)
        {
            Encoding encoding = Encoding.UTF8;

            string source = File.ReadAllText(shaderFile, encoding);
            string key = System.IO.Path.GetFileName(shaderFile);
            shaderSources[key] = source;
            shaderFiles[key] = shaderFile;
        }


    }
}
