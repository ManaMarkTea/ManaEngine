using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        // Shared with the inspector/schema cache so the .wgsl file used for rendering and the .wgsl
        // file parsed for CustomMaterialUniform metadata never drift apart.
        private static string ResolveShaderFileName(string shaderName)
        {
            return ManaWebGpuCustomUniformSchemaCache.ResolveShaderFileNameOrDefault(shaderName);
        }

        private RenderPipeline* GetOrCreatePipeline(
            string? shaderName, bool isTransparent = false, bool disableDepthWrite = false, bool cullBackFaces = false,
            bool alwaysOnTop = false, bool additiveBlend = false)
        {
            shaderName ??= "ManaPbrShader";

            string pipelineKey =
                $"{shaderName}_T{(isTransparent ? 1 : 0)}_D{(disableDepthWrite ? 1 : 0)}_C{(cullBackFaces ? 1 : 0)}_A{(alwaysOnTop ? 1 : 0)}_B{(additiveBlend ? 1 : 0)}";
            if (_pipelines.TryGetValue(pipelineKey, out var cachedPipeline))
            {
                return (RenderPipeline*)cachedPipeline;
            }

            string shaderFile = ResolveShaderFileName(shaderName);

            ShaderModule* shaderModule = null;
            try
            {
                shaderModule = CreateShaderModule(shaderFile);
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"Failed to load WGSL shader '{shaderFile}', falling back to default colored_mesh.wgsl. Error: {ex.Message}");
                shaderFile = "colored_mesh.wgsl";
                shaderModule = CreateShaderModule(shaderFile);
            }

            _shaderModules[shaderName] = (nint)shaderModule;
            // Every material pipeline gets group 1 (per-material custom uniforms) available, whether
            // or not a given .wgsl file actually declares it — same "declare only what you use"
            // precedent already relied on for group 0's TransformUniform.
            var pipeline = ManaWebGpuMeshPipeline.Create(_gpu, 
                shaderModule, GetSceneColorFormat(), _customUniformPipelineLayout, shaderName, isTransparent,
                alwaysOnTop ? false : disableDepthWrite,
                depthCompare: CompareFunction.Less,
                cullBackFaces: cullBackFaces,
                additiveBlend: additiveBlend);
            _pipelines[pipelineKey] = (nint)pipeline;

            ManaConsole.WriteDiagnostic($"WebGPU render pipeline for '{shaderName}' created successfully using '{shaderFile}'.");
            return pipeline;
        }

        private RenderPipeline* GetOrCreateSceneCapturePipeline()
        {
            if (_sceneCapturePipeline != null)
            {
                return _sceneCapturePipeline;
            }

            var shaderModule = CreateShaderModule("scene_capture.wgsl");
            _sceneCapturePipeline = ManaWebGpuMeshPipeline.Create(_gpu, shaderModule, TextureFormat.Rgba8UnormSrgb, _sceneCapturePipelineLayout, "SceneCapture");
            _shaderModules["SceneCapture"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic("WebGPU scene capture pipeline created successfully.");
            return _sceneCapturePipeline;
        }

        private RenderPipeline* GetOrCreateOutlineMaskPipeline(bool alwaysOnTop = false)
        {
            if (alwaysOnTop)
            {
                return _outlineMaskAlwaysOnTopPipeline != null
                    ? _outlineMaskAlwaysOnTopPipeline
                    : _outlineMaskAlwaysOnTopPipeline = CreateOutlineMaskPipeline(CompareFunction.Always);
            }

            if (_outlineMaskPipeline != null)
            {
                return _outlineMaskPipeline;
            }

            _outlineMaskPipeline = CreateOutlineMaskPipeline(CompareFunction.LessEqual);
            return _outlineMaskPipeline;
        }

        // The mask is depth-tested against what the scene pass left behind, so a highlighted mesh only
        // shows where nothing is in front of it. Handles use their own depth buffer for the same test.
        private RenderPipeline* CreateOutlineMaskPipeline(CompareFunction depthCompare)
        {
            var shaderModule = CreateShaderModule("outline_mask.wgsl");
            var pipeline = ManaWebGpuMeshPipeline.Create(_gpu, 
                shaderModule,
                TextureFormat.Rgba8Unorm,
                _demoPipelineLayout,
                "OutlineMask",
                disableDepthWrite: true,
                depthCompare: depthCompare);
            _shaderModules[depthCompare == CompareFunction.Always ? "OutlineMaskAlwaysOnTop" : "OutlineMask"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic($"WebGPU outline-mask pipeline created successfully ({depthCompare}).");
            return pipeline;
        }

        private RenderPipeline* GetOrCreateOverlayPipeline(ManaWebGpuOverlayPass overlay)
        {
            if (_overlayPipelines.TryGetValue(overlay.Name, out var cached))
            {
                return (RenderPipeline*)cached;
            }

            var shaderModule = CreateShaderModule(overlay.ShaderFileName);
            var pipeline = ManaWebGpuMeshPipeline.Create(_gpu, 
                shaderModule,
                GetSceneColorFormat(),
                _demoPipelineLayout,
                overlay.Name,
                disableDepthWrite: true,
                depthCompare: CompareFunction.LessEqual,
                primitiveTopology: overlay.Topology);

            _shaderModules[overlay.Name] = (nint)shaderModule;
            _overlayPipelines[overlay.Name] = (nint)pipeline;
            ManaConsole.WriteDiagnostic($"WebGPU {overlay.Name} overlay pipeline created successfully.");
            return pipeline;
        }

        private ShaderModule* CreateShaderModule(string fileName) => _gpu.CreateShaderModule(fileName);
    }
}
