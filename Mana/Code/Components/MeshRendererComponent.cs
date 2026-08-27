using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using System.Threading;
using ManaCore.Graphics;

namespace Mana
{
    public class MeshRendererComponent : ManaComponent
    {
        public ManaMesh Mesh { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool DrawAfterScene { get; set; }
        public bool OutlineEnabled { get; set; }
        public Vector4 OutlineColor { get; set; } = new(1.0f, 0.82f, 0.18f, 1.0f);
        public float OutlineWidth { get; set; } = 2.0f;

        private static long _nextInstanceId;
        private readonly long _instanceId = Interlocked.Increment(ref _nextInstanceId);

        // Stable per renderer instance. RuntimeHelpers.GetHashCode is stable but not unique; a pair of
        // KML airport line nodes collided there and fought over one WebGPU scene-mesh map entry every
        // frame.
        private string? _instanceKey;
        public string InstanceKey => _instanceKey ??= $"meshRenderer:{_instanceId:x16}";

        // The last data returned by ApplyMaterialTextures, kept so a repeat call with the same inputs hands
        // back the same object. The renderers reconcile by reference when a mesh has no version, so a fresh
        // copy every frame would have them rebuilding GPU buffers every frame.
        private ManaMeshCpuData? _texturedSource;
        private ManaMeshCpuData? _texturedResult;
        private string _texturedKey = string.Empty;

        /// <summary>
        /// The mesh's CPU data with this material's texture maps applied.
        /// <para>
        /// The backends take a mesh's maps from its CPU data, where they were baked in at load time by
        /// whatever produced the mesh -- and left null by everything procedural. That makes a map assigned
        /// to the material afterwards invisible, which is exactly what an inspector does. This overlays
        /// them, so a material is the single place a map is chosen regardless of where the mesh came from.
        /// </para>
        /// </summary>
        public ManaMeshCpuData ApplyMaterialTextures(ManaMeshCpuData meshData)
        {
            var current = Material;

            var albedo = PathOf(current.Albedo) ?? meshData.AlbedoTexturePath;
            var normal = PathOf(current.Normal) ?? meshData.NormalTexturePath;
            var emission = PathOf(current.Emission) ?? meshData.EmissionTexturePath;
            var metallic = PathOf(current.Metallic) ?? meshData.MetallicTexturePath;
            var roughness = PathOf(current.Roughness) ?? meshData.RoughnessTexturePath;
            var ambient = PathOf(current.Occlusion) ?? meshData.OcclusionTexturePath;
            var opacity = PathOf(current.Opacity) ?? meshData.OpacityTexturePath;

            // Nothing the material adds, so the mesh's own data is already the answer and the caller keeps
            // its reference. This is the common case -- most meshes wear what they were loaded with.
            if (ReferenceEquals(albedo, meshData.AlbedoTexturePath)
                && ReferenceEquals(normal, meshData.NormalTexturePath)
                && ReferenceEquals(emission, meshData.EmissionTexturePath)
                && ReferenceEquals(metallic, meshData.MetallicTexturePath)
                && ReferenceEquals(roughness, meshData.RoughnessTexturePath)
                && ReferenceEquals(ambient, meshData.OcclusionTexturePath)
                && ReferenceEquals(opacity, meshData.OpacityTexturePath))
            {
                return meshData;
            }

            var key = string.Join('|', albedo, normal, emission, metallic, roughness, ambient, opacity);

            if (ReferenceEquals(_texturedSource, meshData)
                && string.Equals(_texturedKey, key, StringComparison.Ordinal)
                && _texturedResult != null)
            {
                return _texturedResult;
            }

            // The arrays are shared rather than copied: this is the same geometry wearing different maps,
            // and the vertex data is the large part.
            _texturedResult = new ManaMeshCpuData
            {
                Indexes = meshData.Indexes,
                Positions = meshData.Positions,
                Normals = meshData.Normals,
                Colors = meshData.Colors,
                TextureCoordinates0 = meshData.TextureCoordinates0,
                TextureCoordinates1 = meshData.TextureCoordinates1,
                AlbedoTexturePath = albedo,
                AlbedoTextureBytes = ReferenceEquals(albedo, meshData.AlbedoTexturePath)
                    ? meshData.AlbedoTextureBytes
                    : null,
                AlbedoFactor = meshData.AlbedoFactor,
                NormalTexturePath = normal,
                EmissionTexturePath = emission,
                MetallicTexturePath = metallic,
                RoughnessTexturePath = roughness,
                OcclusionTexturePath = ambient,
                OpacityTexturePath = opacity
            };

            _texturedSource = meshData;
            _texturedKey = key;

            return _texturedResult;
        }

        /// <summary>
        /// A channel's map as a path the backend can open, or null when there is no such file.
        /// <para>
        /// Resolved to an absolute path here: a material stores a relative one so a scene survives being
        /// moved or opened on another machine, and the backend does a plain File.Exists.
        /// </para>
        /// <para>
        /// A name that resolves to nothing has to come back null rather than as the unresolved string,
        /// because the caller reads "the material named a map" as "the material overrides this channel"
        /// and drops the mesh's own embedded texture bytes on the strength of it. A glTF texture that
        /// lives inside its own file has no path to give: OriginalFilename holds the image's internal
        /// URI ("Textures/colormap.png") purely as a human-meaningful label, and returning that string
        /// left the backend with a path it could not open and no bytes to fall back on, so every mesh
        /// imported from a GLB rendered as untextured white.
        /// </para>
        /// </summary>
        private static string? PathOf(ManaTextureDynamic? channel)
        {
            var name = channel?.TextureValue?.OriginalFilename;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var resolved = ManaTexture.ResolveTexturePath(name);
            return File.Exists(resolved) ? resolved : null;
        }

        private ManaMaterial? material;
        public ManaMaterial Material
        {
            get
            {
                if (dynamicMaterial != null)
                {
                    return dynamicMaterial;
                }

                return material ?? ManaMaterial.GetDefaultMaterial(Context);
            }
            set
            {
                material = value;
            }
        }


        private ManaMaterial? dynamicMaterial = null;
        public ManaMaterial DynamicMaterial
        {
            get
            {
                if (dynamicMaterial == null)
                {
                    dynamicMaterial = Material.Clone() as ManaMaterial;
                    if (dynamicMaterial == null)
                    {
                        dynamicMaterial = new ManaMaterial(Context);
                    }

                    dynamicMaterial.Name = BuildDynamicMaterialName(Material.Name);
                }

                return dynamicMaterial;
            }
        }

        private string BuildDynamicMaterialName(string baseMaterialName)
        {
            var ownerName = string.IsNullOrWhiteSpace(Owner.Name)
                ? Owner.GetType().Name
                : Owner.Name;

            if (string.IsNullOrWhiteSpace(baseMaterialName) ||
                baseMaterialName == "PBR Material" ||
                baseMaterialName == "Default PBR Material")
            {
                return $"{ownerName} Material";
            }

            if (baseMaterialName.Contains(ownerName, StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMaterialName} Instance";
            }

            return $"{ownerName} {baseMaterialName}";
        }

        public MeshRendererComponent(ManaSceneNode owner, ManaMesh mesh) : base(owner)
        {
            Mesh = mesh;

            if (mesh.Materials.Length == 0 || mesh.Materials[0] == null)
            {
                Material = ManaMaterial.GetDefaultMaterial(owner.Context);
            }
            else
            {
                Material = mesh.Materials[0];
            }

        }

        public override void DrawComponent()
        {
            base.DrawComponent();
#if !MANA_BROWSER
            if (!IsVisible || Mesh == null || Mesh.VertexBufferObject == null || Mesh.VertexBufferObject.indexes.Length == 0)
            {
                return;
            }
#else
            // Same visibility contract as desktop; the VertexBufferObject half of that check is
            // OpenGL-only and has no browser equivalent (WebGPU uploads through its own path).
            if (!IsVisible || Mesh == null)
            {
                return;
            }
#endif

            if (DrawAfterScene)
            {
                Context.World.PostDrawFutures.Add(() => Mesh.Draw(Owner, this));
                return;
            }

            Mesh.Draw(Owner, this);
        }

        // World-space axis-aligned bounds of this renderer's mesh -- for "frame selected" camera framing
        // (see CameraComponent.TryFrameSelection) and any future use needing scene bounds. Transforms all
        // 8 local AABB corners through Owner.WorldTransform (not just min/max) since that transform can
        // include rotation, which a naive min/max-only transform would get wrong. Mirrors
        // ManaScenePicker's own local-bounds-from-CPU-data pattern.
        public bool TryGetWorldBounds(out Vector3 worldMin, out Vector3 worldMax)
        {
            worldMin = Vector3.Zero;
            worldMax = Vector3.Zero;

            if (Mesh == null)
            {
                return false;
            }

            ManaMeshCpuData cpuData;
            try
            {
                cpuData = Mesh.CpuData ?? Mesh.GetOrCreateCpuData();
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (cpuData.Positions.Length == 0)
            {
                return false;
            }

            var localMin = cpuData.Positions[0];
            var localMax = cpuData.Positions[0];
            foreach (var position in cpuData.Positions)
            {
                localMin = Vector3.Min(localMin, position);
                localMax = Vector3.Max(localMax, position);
            }

            var worldTransform = Owner.WorldTransform;
            Span<Vector3> localCorners = stackalloc Vector3[8]
            {
                new Vector3(localMin.X, localMin.Y, localMin.Z),
                new Vector3(localMax.X, localMin.Y, localMin.Z),
                new Vector3(localMin.X, localMax.Y, localMin.Z),
                new Vector3(localMax.X, localMax.Y, localMin.Z),
                new Vector3(localMin.X, localMin.Y, localMax.Z),
                new Vector3(localMax.X, localMin.Y, localMax.Z),
                new Vector3(localMin.X, localMax.Y, localMax.Z),
                new Vector3(localMax.X, localMax.Y, localMax.Z),
            };

            worldMin = Vector3.Transform(localCorners[0], worldTransform);
            worldMax = worldMin;
            foreach (var localCorner in localCorners)
            {
                var worldCorner = Vector3.Transform(localCorner, worldTransform);
                worldMin = Vector3.Min(worldMin, worldCorner);
                worldMax = Vector3.Max(worldMax, worldCorner);
            }

            return true;
        }
    }
}
