using Mana.Code.Nodes;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;

namespace Mana
{
    public class ShaderSource
    {
        public string Path = string.Empty;
        public string Source = string.Empty;
        
        public bool IsDirty { get; set; } = false;

        public override string ToString()
        {
            return $@"{Path} {(IsDirty ? "*" : "")}";
        }
    }

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
        public ManaCompiledShader CompiledShader { get; set; } = null!;
        public virtual bool IsTransparent => false;
        public virtual bool DisableDepthWrite => false;

        public ShaderSource VertexSource { get; set; } = null!;
        public ShaderSource FragmentSource { get; set; } = null!;
        public ShaderSource GeometrySource { get; set; } = null!;


        public ManaShader(ManaContext context) : base(context)
        {
            
        }

        protected virtual string CleanSource(string source) { return source; }

        public virtual void LoadShader(string vertPath, string fragPath, string geomShader = "")
        {
            VertexSource = CacheShader(vertPath);
            FragmentSource = CacheShader(fragPath);
            GeometrySource = CacheShader(geomShader);
            LoadShaderSource(VertexSource, FragmentSource, GeometrySource);
        }

        public virtual void LoadShaderSource(ShaderSource vertSource, ShaderSource fragSource, ShaderSource? geomSource = null)
        {
            VertexSource = vertSource;
            FragmentSource = fragSource;
            GeometrySource = geomSource ?? new ShaderSource() { Path = "", Source = "" };

            CompiledShader = ManaShaderCache.GetShaderCache(Context).GetCompiledShader(vertSource, fragSource, GeometrySource);
        }

        // Proxy for the compiled shader
        public ManaShaderUniform FindUniform(string v)
        {
            return CompiledShader.FindUniform(v)!;
        }

        public virtual void BindShader()
        {
            if (VertexSource.IsDirty || FragmentSource.IsDirty || GeometrySource.IsDirty)
            {
                CompiledShader = ManaShaderCache.GetShaderCache(Context).GetCompiledShader(VertexSource, FragmentSource, GeometrySource, CompiledShader);
                VertexSource.IsDirty = FragmentSource.IsDirty = GeometrySource.IsDirty = false;
            }

            CompiledShader.BindShader();
       }
        
        public virtual void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            if (VertexSource.IsDirty || FragmentSource.IsDirty || GeometrySource.IsDirty)
            {
                CompiledShader = ManaShaderCache.GetShaderCache(Context).GetCompiledShader(VertexSource, FragmentSource, GeometrySource, CompiledShader);
                VertexSource.IsDirty = FragmentSource.IsDirty = GeometrySource.IsDirty = false;
            }

            CompiledShader.BindShader(material, objectMatrix, camera, owner);
        }

        public virtual void UnbindShader()
        {
            CompiledShader.UnbindShader();
        }


        public static Dictionary<string, FileSystemWatcher> WatchedSources = new Dictionary<string, FileSystemWatcher>();
        public static Dictionary<string, ShaderSource> ShaderSources = new Dictionary<string, ShaderSource>();

        private static string ResolveShaderPath(string shaderFile)
        {
            if (string.IsNullOrWhiteSpace(shaderFile))
            {
                return shaderFile;
            }

            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, shaderFile),
                Path.Combine(Environment.CurrentDirectory, "Mana", shaderFile),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", shaderFile),
                Path.Combine(AppContext.BaseDirectory, shaderFile),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", shaderFile)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return shaderFile;
        }

        protected static ShaderSource CacheShader(string shaderFile)
        {
            if (string.IsNullOrWhiteSpace(shaderFile))
            {
                return new ShaderSource() { Path = shaderFile, Source = "" };
            }

            var resolvedPath = ResolveShaderPath(shaderFile);
            string key = System.IO.Path.GetFileName(shaderFile);

            if (!File.Exists(resolvedPath))
            {
                var missing = new ShaderSource() { Path = resolvedPath, Source = "" };
                ShaderSources[key] = missing;
                return missing;
            }

            Encoding encoding = Encoding.UTF8;

            var watchPath = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(watchPath) && Directory.Exists(watchPath) && !WatchedSources.ContainsKey(watchPath))
            {
                FileSystemWatcher watcher = new FileSystemWatcher(watchPath);
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                watcher.EnableRaisingEvents = true;
                watcher.Changed += Watcher_Changed;
                WatchedSources.Add(watchPath, watcher);
            }

#if DEBUG
            var devAssetFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Environment.CurrentDirectory))));
            if (string.IsNullOrEmpty(devAssetFolder))
            {
                goto skipDevWatch;
            }

            devAssetFolder = Path.Combine(devAssetFolder, "Mana");
            var fullDevPath = Path.Combine(devAssetFolder, shaderFile);
            var watchDevPath = Path.GetDirectoryName(fullDevPath);

            if (Directory.Exists(watchDevPath) && !WatchedSources.ContainsKey(watchDevPath))
            {
                FileSystemWatcher watcher = new FileSystemWatcher(watchDevPath);
                watcher.EnableRaisingEvents = true;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                watcher.Created += Watcher_Changed;
                watcher.Changed += Watcher_Changed;
                watcher.Deleted += Watcher_Changed;
                WatchedSources.Add(watchDevPath, watcher);
            }

skipDevWatch:
#endif

            string source = File.ReadAllText(resolvedPath, encoding);

            var output = new ShaderSource() { Path = resolvedPath, Source = source };
            ShaderSources[key] = output;
            return output;
        }

        private static void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            foreach (var fileNotification in ShaderSources)
            {
                if (e.FullPath.EndsWith(fileNotification.Key))
                {
                    for (int w = 0; w < 10; w++)
                    {
                        try
                        {
                            fileNotification.Value.Source = File.ReadAllText(e.FullPath);
                            fileNotification.Value.IsDirty = true;
                        }
                        catch (IOException)
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
            }

            Debug.WriteLine(e.FullPath);
        }

        protected void PushShaderSources()
        {
        }


    }
}
