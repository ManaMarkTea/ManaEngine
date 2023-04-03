using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public struct Uniform
    {
        public int location;
        public UniformType type;
        public string name;

        public override string ToString()
        {
            return $"{location}:{type} {name}";
        }
    }

    public class ManaShader
    {
        private GL gl = Game.Instance.gl;

        private uint shaderProgram = 0;
        private uint vertexShaderObject = 0;
        private uint fragmentShaderObject = 0;
        private uint geomShaderObject = 0;

        public string VertexShaderCompilerLog { get; set; } = "";
        public string FragmentShaderCompilerLog { get; set; } = "";
        public string GeometryShaderCompilerLog { get; set; } = "";

        public string LinkCompilerLog { get; set; } = "";

        public List<Uniform> UniformNames { get; set; } = new List<Uniform>();                
        public Dictionary<string, Uniform> UniformMap { get; set; } = new Dictionary<string, Uniform>();

        public void LoadShader(string vertPath, string fragPath, string geomShader = "")
        {
            gl = Game.Instance.gl;

            vertexShaderObject = gl.CreateShader(ShaderType.VertexShader);
            fragmentShaderObject = gl.CreateShader(ShaderType.FragmentShader);
        
            var vertexSource = File.ReadAllText(vertPath, Encoding.UTF8);
            var fragSource = File.ReadAllText(fragPath, Encoding.UTF8);

            gl.ShaderSource(vertexShaderObject, vertexSource);
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


            if (geomShader.Length != 0)
            {                                
                var geomSource = File.ReadAllText(geomShader, Encoding.UTF8);
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

            gl.GetProgram(shaderProgram, ProgramPropertyARB.ActiveUniforms, out int uniformCount);
            for (uint i = 0; i < uniformCount; i++)
            {
                gl.GetActiveUniform(shaderProgram, i, (uint)64, out uint uniformLength, out int uniformSize, out UniformType type, out string uniformName);

                Uniform uniform;
                uniform.name = uniformName;
                uniform.location = (int)i;
                uniform.type = type;

                UniformNames.Add(uniform);
                UniformMap[uniformName] = uniform;

                Console.WriteLine($"Uniform #{i} Type: {type} Name: {uniformName}\n");
            }
        }

        private static string[] ModelViewProjectMatrixUniforms = { "gWVP" };
        private static string[] ObjMatrixUniforms = { "gWorld" };        
        private static string[] ModelViewMatrixUniforms = { "modelViewMatrix" };
        private static string[] ProjectionMatrixUniforms = { "projectionMatrix" };

        private static string[] BaseColorUniforms = { "baseColor", "gColorMap" };

        public void BindShader()
        {
            gl.UseProgram(shaderProgram);
        }

        public unsafe void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera)
        {
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

                if (uniform.name == "spheres[0].position")
                {
                    gl.Uniform3(uniform.location, new Vector3(1,0,0));
                }
                if (uniform.name == "spheres[0].material.color")
                {
                    gl.Uniform4(uniform.location, new Vector4(1,0,0,1));
                }
                if (uniform.name == "spheres[0].radius")
                {
                    gl.Uniform1(uniform.location, 0.25f);
                }


                if (uniform.name == "spheres[1].position")
                {
                    gl.Uniform3(uniform.location, new Vector3(0, 1, 0));
                }
                if (uniform.name == "spheres[1].material.color")
                {
                    gl.Uniform4(uniform.location, new Vector4(0, 1, 0, 1));
                }
                if (uniform.name == "spheres[1].radius")
                {
                    gl.Uniform1(uniform.location, 0.25f);
                }

                if (uniform.name == "spheres[2].position")
                {
                    gl.Uniform3(uniform.location, new Vector3(0, 0, 1));
                }
                if (uniform.name == "spheres[2].material.color")
                {
                    gl.Uniform4(uniform.location, new Vector4(0, 0, 1, 1));
                }
                if (uniform.name == "spheres[2].radius")
                {
                    gl.Uniform1(uniform.location, 0.25f);
                }


                if (uniform.name == "spheres[3].position")
                {
                    gl.Uniform3(uniform.location, new Vector3(0.5f, 1, 1));
                }
                if (uniform.name == "spheres[3].material.color")
                {
                    gl.Uniform4(uniform.location, new Vector4(0.5f, 0.5f, 0.5f, 1));
                }
                if (uniform.name == "spheres[3].material.emission")
                {
                    gl.Uniform4(uniform.location, new Vector4(0.5f, 0.5f, 0.5f, 1));
                }
                if (uniform.name == "spheres[3].material.emissionStrength")
                {
                    gl.Uniform1(uniform.location, 7.0f);
                }
                if (uniform.name == "spheres[3].radius")
                {
                    gl.Uniform1(uniform.location, 0.55f);
                }

                if (uniform.name == "spheres[4].position")
                {
                    gl.Uniform3(uniform.location, new Vector3(1, -11, 1));
                }
                if (uniform.name == "spheres[4].material.color")
                {
                    gl.Uniform4(uniform.location, new Vector4(0.1f, 0.75f, 0.57f, 1));
                }
                if (uniform.name == "spheres[4].radius")
                {
                    gl.Uniform1(uniform.location, 10.25f);
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
                        gl.UniformMatrix4(uniform.location, 1, false, (float*)&mvpm);
                    }
                }
            

                foreach (var objUniform in ObjMatrixUniforms)
                {
                    if (uniform.name.Equals(objUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        gl.UniformMatrix4(uniform.location, 1, false, (float*)&objectMatrix);
                    }
                }

                foreach (var modelViewMatrixUniform in ModelViewMatrixUniforms)
                {
                    if (uniform.name.Equals(modelViewMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 mvm = objectMatrix * camera._ViewMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, (float*)&mvm);
                    }
                }

                foreach (var projectMatrixUniform in ProjectionMatrixUniforms)
                {
                    if (uniform.name.Equals(projectMatrixUniform, StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix4x4 proj = camera._ProjectionMatrix;
                        gl.UniformMatrix4(uniform.location, 1, false, (float*)&proj);
                    }
                }
            }
        }

        public void UnbindShader()
        {
            gl.UseProgram(0);
        }
    }
}
