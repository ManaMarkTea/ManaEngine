using Mana.Code;
using Mana.Code.ManaShaders;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Text;

namespace Mana
{
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
        
        private static string[] ModelViewProjectMatrixUniforms = { "gWVP" };
        private static string[] ObjMatrixUniforms = { "gWorld", "model" };
        private static string[] ModelViewMatrixUniforms = { "modelViewMatrix" };
        private static string[] ProjectionMatrixUniforms = { "projectionMatrix", "projection" };
        private static string[] ViewMatrixUniforms = { "view", "viewMatrix" };
        private static string[] NormalMatrixUniforms = { "normalMatrix" };
        private static string[] BaseColorUniforms = { "baseColor", "gColorMap" };

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
                uniform.location = (int)uniformLocation;
                uniform.type = type;

                UniformNames.Add(uniform);
                UniformMap[uniformName] = uniform;

                Trace.WriteLine($"Uniform #{i} Type: {type} Name: {uniformName}");
            }
        }

        public ManaShaderUniform FindUniform(string name)
        {
            if (UniformMap.ContainsKey(name))
            {
                return UniformMap[name];
            }

            Trace.TraceWarning($"Uniform not found {name}");
            return null;
        }

        public void BindShader()
        {
            Context.gl.UseProgram(shaderProgram);
        }

        public unsafe virtual void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera)
        {
            var gl = Context.gl;

            gl.UseProgram(shaderProgram);

            foreach (var uniform in UniformNames)
            {
                if (uniform.type == UniformType.Sampler2D)
                {
                    foreach (var baseColorUniform in BaseColorUniforms)
                    {
                        if (uniform.name.Equals(baseColorUniform, StringComparison.OrdinalIgnoreCase))
                        {
                            gl.Uniform1(uniform.location, 0);
                        }
                    }
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
                    gl.Uniform1(uniform.location, Game.Instance.Frame);
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
                else if (uniform.name == "gEyeWorldPos")
                {
                    //gl.Uniform3(uniform.location, camera._WorldMatrix.Translation);
                    gl.Uniform3(uniform.location, camera.Owner.Position);
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


                foreach (var modelViewProjectUniform in ModelViewProjectMatrixUniforms)
                {
                    if (uniform.name.Equals(modelViewProjectUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 mvpm = objectMatrix * camera._ViewMatrix * camera._ProjectionMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, mvpm.ToSpan());
                        //gl.ProgramUniformMatrix4(shaderProgram, uniform.location, false, (double*)mvpm);
                    }
                }


                foreach (var objUniform in ObjMatrixUniforms)
                {
                    if (uniform.name.Equals(objUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        gl.UniformMatrix4(uniform.location, 1, false, objectMatrix.ToSpan());
                    }
                }

                foreach (var modelViewMatrixUniform in ModelViewMatrixUniforms)
                {
                    if (uniform.name.Equals(modelViewMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 mvm = objectMatrix * camera._ViewMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, mvm.ToSpan());
                    }
                }

                foreach (var projectMatrixUniform in ProjectionMatrixUniforms)
                {
                    if (uniform.name.Equals(projectMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 proj = camera._ProjectionMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, proj.ToSpan());
                    }
                }

                foreach (var viewMatrixUniform in ViewMatrixUniforms)
                {
                    if (uniform.name.Equals(viewMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 view = camera._ViewMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, view.ToSpan());
                    }
                }

                foreach (var normalMatrixUniform in NormalMatrixUniforms)
                {
                    if (uniform.name.Equals(normalMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        var normalMatrix = Matrix3X3<float>.Identity;
                        gl.UniformMatrix3(uniform.location, 1, false, normalMatrix.ToSpan());
                    }
                }
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
