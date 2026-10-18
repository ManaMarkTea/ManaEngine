using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

public static class MaterialRenderInstanceChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var meshNode = new ManaSceneNode(context, Matrix4x4.Identity);
        var meshRenderer = new MeshRendererComponent(meshNode, new ManaCube(context, Vector4.One, Vector3.One));
        meshNode.Components.Add(meshRenderer);
        var material = meshRenderer.Material;
        material.Shader = new ManaPbrShader(context);
        meshRenderer.OutlineWidth = 1.5f;
        var outlinedInstances = new List<ManaRenderMeshInstance>();
        meshRenderer.EditorOutlineEnabled = true;
        meshRenderer.EditorOutlineEnabled = false;
        meshNode.CollectRenderMeshInstances(outlinedInstances);
        Check("clearing both outline sources disables the rendered outline", !outlinedInstances.Single().OutlineEnabled);
        meshRenderer.OutlineEnabled = true;
        meshRenderer.EditorOutlineEnabled = true;
        meshRenderer.EditorOutlineEnabled = false;
        Check("deselecting a mesh preserves its custom outline and width",
            meshRenderer.RenderOutlineEnabled && meshRenderer.OutlineWidth == 1.5f);
        var texturePath = Path.Combine(Path.GetTempPath(), $"mana-material-{Guid.NewGuid():N}.png");
        var textureReference = Path.GetRelativePath(Environment.CurrentDirectory, texturePath);
        try
        {
            ManaCore.Imaging.ManaImageCodec.SavePng(new ManaCore.Imaging.ManaImage(1, 1, [255, 255, 255, 255]), texturePath);
            material.Albedo.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            var collected = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(collected);

            var instance = collected.FirstOrDefault();
            Check("the cube is collected for rendering", instance != null, $"{collected.Count} instances");

            Check("an assigned map reaches the render instance",
                !string.IsNullOrWhiteSpace(instance?.MeshData.AlbedoTexturePath),
                instance?.MeshData.AlbedoTexturePath ?? "null -- the material's map was dropped");

            // Resolved to somewhere on disk, because a relative path is what a material stores and an
            // absolute one is what the backend opens. This is the exact test the WebGPU loader makes.
            Check("and points at a file the backend can open",
                instance?.MeshData.AlbedoTexturePath is { } path && File.Exists(path),
                instance?.MeshData.AlbedoTexturePath ?? "none");

            // Collected every frame. A fresh copy each time would fail the renderers' reference check and
            // have them rebuilding the mesh's GPU buffers on every frame it is visible.
            var again = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(again);

            Check("the same assignment yields the same data object",
                ReferenceEquals(instance?.MeshData, again.FirstOrDefault()?.MeshData),
                "so the renderer does not rebuild it every frame");

            // Every channel, not just albedo. The maps used to stop at the material: the picker wrote them
            // and nothing downstream read them, so assigning a normal or emission map did nothing at all.
            material.Normal.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.Emission.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.Metallic.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.Roughness.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.Occlusion.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.Opacity.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };

            var allMaps = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(allMaps);
            var mapped = allMaps.FirstOrDefault()?.MeshData;

            Check("a normal map reaches the render instance",
                mapped?.NormalTexturePath is { } n && File.Exists(n), mapped?.NormalTexturePath ?? "null");
            Check("an emission map does too",
                mapped?.EmissionTexturePath is { } e && File.Exists(e), mapped?.EmissionTexturePath ?? "null");
            Check("and metallic, roughness, ambient and opacity",
                mapped?.MetallicTexturePath != null && mapped.RoughnessTexturePath != null
                    && mapped.OcclusionTexturePath != null && mapped.OpacityTexturePath != null,
                $"{mapped?.MetallicTexturePath != null}/{mapped?.RoughnessTexturePath != null}/"
                    + $"{mapped?.OcclusionTexturePath != null}/{mapped?.OpacityTexturePath != null}");

            // Still stable frame to frame, or the renderer rebuilds every mesh every frame.
            var repeat = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(repeat);
            Check("with all six assigned it is still the same object",
                ReferenceEquals(mapped, repeat.FirstOrDefault()?.MeshData), "no per-frame rebuild");

            // Auto must distinguish a masked texture from a uniform fade.
            material.Opacity.TextureValue = new ManaTexture(context) { OriginalFilename = textureReference };
            material.OpacityMode = ManaOpacityMode.Auto;

            Check("assigning an opacity map makes the material cut out",
                material.EffectiveOpacityMode == ManaOpacityMode.Cutout,
                material.EffectiveOpacityMode.ToString());

            material.Opacity.TextureValue = null;
            material.Opacity.Value = 1.0f;
            Check("a material with nothing to say stays opaque",
                material.EffectiveOpacityMode == ManaOpacityMode.Opaque,
                material.EffectiveOpacityMode.ToString());

            // A uniform value is a fade, and only blending can express that. Cutting out with the same
            // alpha everywhere is a cliff: nothing, nothing, then the whole surface at once.
            material.Opacity.Value = 0.5f;
            Check("lowering the value alone asks for a fade, so it blends",
                material.EffectiveOpacityMode == ManaOpacityMode.Blend,
                material.EffectiveOpacityMode.ToString());

            // And the value has to actually reach the renderer, which is the half that was missing: the
            // map was wired and the slider was not, so it moved and nothing happened.
            var faded = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(faded);

            Check("the opacity value reaches the render instance",
                MathF.Abs((faded.FirstOrDefault()?.Opacity ?? 1.0f) - 0.5f) < 0.001f,
                $"{faded.FirstOrDefault()?.Opacity}");

            // Blending is a material decision as much as a shader one: a PBR shader is opaque by nature,
            // but a PBR material set to blend is not, and the pipeline is chosen from the instance.
            material.OpacityMode = ManaOpacityMode.Blend;

            var blended = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(blended);
            var blendInstance = blended.FirstOrDefault();

            Check("a blending material reaches the renderer as transparent",
                blendInstance?.IsTransparent == true, $"{blendInstance?.IsTransparent}");
            Check("and stops writing depth",
                blendInstance?.DisableDepthWrite == true, $"{blendInstance?.DisableDepthWrite}");
            Check("with its mode and cutoff",
                blendInstance?.OpacityMode == ManaOpacityMode.Blend
                    && MathF.Abs((blendInstance?.AlphaCutoff ?? 0.0f) - 0.5f) < 0.001f,
                $"{blendInstance?.OpacityMode}, cutoff {blendInstance?.AlphaCutoff}");

            material.OpacityMode = ManaOpacityMode.Auto;
            material.Opacity.Value = 1.0f;

            var opaque = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(opaque);
            Check("an ordinary material is still opaque",
                opaque.FirstOrDefault()?.IsTransparent == false, "no blending unless asked");

            // Selector zero means full map strength; render instances expose the inverse convention.
            material.NormalSelector = 0.0;

            var normals = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(normals);

            Check("a fully applied normal map reaches the renderer at full strength",
                MathF.Abs((normals.FirstOrDefault()?.NormalStrength ?? 0.0f) - 1.0f) < 0.001f,
                $"{normals.FirstOrDefault()?.NormalStrength}");

            material.NormalSelector = 1.0;
            var ignored = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(ignored);

            Check("and one mixed all the way to the vertex normal arrives at zero",
                MathF.Abs(ignored.FirstOrDefault()?.NormalStrength ?? 1.0f) < 0.001f,
                $"{ignored.FirstOrDefault()?.NormalStrength}");

            material.NormalSelector = 0.0;

            // UV tiling rides on the instance rather than the mesh: it is a material property, and the
            // same brick wants a different repeat on a wall than on a doorframe.
            material.UvScale = new Vector2(4.0f, 2.0f);
            material.UvOffset = new Vector2(0.25f, 0.5f);

            var tiled = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(tiled);

            Check("uv tiling reaches the render instance",
                tiled.FirstOrDefault()?.UvTransform == new Vector4(4.0f, 2.0f, 0.25f, 0.5f),
                tiled.FirstOrDefault()?.UvTransform.ToString() ?? "none");

            material.UvScale = Vector2.One;
            material.UvOffset = Vector2.Zero;
            material.Normal.TextureValue = null;
            material.Emission.TextureValue = null;
            material.Metallic.TextureValue = null;
            material.Roughness.TextureValue = null;
            material.Occlusion.TextureValue = null;
            material.Opacity.TextureValue = null;

            // Cleared, and the mesh goes back to whatever it was carrying itself.
            material.Albedo.TextureValue = null;
            var cleared = new List<ManaCore.Graphics.ManaRenderMeshInstance>();
            meshNode.CollectRenderMeshInstances(cleared);

            Check("clearing the channel takes the map off the mesh",
                cleared.FirstOrDefault()?.MeshData.AlbedoTexturePath == null,
                cleared.FirstOrDefault()?.MeshData.AlbedoTexturePath ?? "null");
        }
        finally
        {
            File.Delete(texturePath);
        }
    }
}
