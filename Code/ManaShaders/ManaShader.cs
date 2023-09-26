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

        public void ApplyArray(ManaShaderUniform uniform, int amount, Action[] action)
        {
            foreach (var uniformName in Names)
            {
                for (int i = 0; i < amount; i++)
                {
                    if (uniform.name.Equals(uniformName + $"[{i}]", StringComparison.OrdinalIgnoreCase))
                    {
                        action[i]();
                    }
                }
            }
        }

        public void Apply(ManaShaderUniform uniform, Action action)
        {
            foreach (var uniformName in Names)
            {
                if (uniform.name.Equals(uniformName, StringComparison.OrdinalIgnoreCase))
                {
                    action();
                    break;
                }
            }
        }
    }

    public class ManaShader : ManaObject
    {
        public ManaCompiledShader CompiledShader { get; set; }

        public ManaShader(ManaContext context) : base(context)
        {
            
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
            CompiledShader = ManaShaderCache.GetShaderCache(Context).GetCompiledShader(vertSource, fragSource, geomSource);
        }

        // Proxy for the compiled shader
        public ManaShaderUniform FindUniform(string v)
        {
            return CompiledShader.FindUniform(v);
        }

        public void BindShader()
        {
            CompiledShader.BindShader();
        }
        
        public virtual void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            CompiledShader.BindShader(material, objectMatrix, camera, owner);
        }

        public void UnbindShader()
        {
            CompiledShader.UnbindShader();
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

        protected void PushShaderSources()
        {
        }


    }
}
