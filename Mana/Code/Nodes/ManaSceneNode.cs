using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code.Nodes
{
    public class ManaSceneNode : ManaObject
    {
        private Vector3 _position;
        private Vector3 _rotation;
        private Vector3 _scale;

        // UpdateTransform() used to unconditionally rebuild every node's matrices every frame --
        // measured as a top-3 per-frame CPU cost (ManaWebGpuContext trace: ~28% of frame time) on a
        // scene with ~2000 mostly-static meshes (planet terrain, KML placemarks). These flags let it
        // skip the matrix math for nodes that haven't actually changed, while still visiting every
        // node every frame (components still need their per-frame UpdateComponent() tick regardless).
        //
        // _needsLocalTransformRebuild: true when Position/Rotation/Scale changed since LocalTransform
        // was last derived from them. Two call sites bypass Position/Rotation/Scale and write
        // LocalTransform/WorldTransform directly -- CameraComponent's mouse-look (LocalTransform) and
        // ManaCollisionComponent's physics sync (both, using the physics engine's own quaternion, not
        // Owner.Rotation). The LocalTransform setter below clears _needsLocalTransformRebuild on every
        // write for exactly this reason: if it didn't, the next UpdateTransform() call would see a
        // stale "true" value and "correct" the physics-driven LocalTransform back using the stale
        // Euler Owner.Rotation, silently discarding the physics simulation's actual orientation. Both
        // known direct-write call sites are also careful to immediately reset Position/Rotation to
        // match (or, for physics, update Position via its normal setter right before), so the next
        // real P/R/S change still triggers a correct regen.
        private bool _needsLocalTransformRebuild = true;
        private Matrix4x4 _localTransform;
        private Matrix4x4 _worldTransform;
        private int _localVersion;
        private int _worldVersion;
        private int _worldComputedForLocalVersion = -1;
        private int _worldComputedForParentVersion = -1;

        public ManaSceneNode? Parent { get; set; }
        public List<ManaSceneNode> Children { get; set; }

        public List<ManaComponent> Components { get; set; }

        public Vector3 Position
        {
            get => _position;
            set
            {
                if (!_position.Equals(value))
                {
                    _position = value;
                    _needsLocalTransformRebuild = true;
                }
            }
        }

        /// <summary>
        /// Euler angles in radians, folded into (-pi, pi].
        /// <para>
        /// Wrapped here rather than by each caller, because anything that turns a node a little every
        /// frame -- a spinning pickup, a turret, a pawn -- otherwise accumulates without bound. Left to
        /// run it reaches magnitudes where a float has no precision left for a degree, and an inspector
        /// shows an ever-climbing number that means nothing.
        /// </para>
        /// <para>
        /// Signed rather than 0..2pi so a small pitch reads as -6 degrees rather than 354, which is the
        /// convention <see cref="ManaPawnNode"/> already used for yaw.
        /// </para>
        /// </summary>
        public Vector3 Rotation
        {
            get => _rotation;
            set
            {
                var wrapped = new Vector3(WrapRadians(value.X), WrapRadians(value.Y), WrapRadians(value.Z));

                if (!_rotation.Equals(wrapped))
                {
                    _rotation = wrapped;
                    _needsLocalTransformRebuild = true;
                }
            }
        }

        /// <summary>Folds an angle into (-pi, pi]. NaN and infinity fold to zero rather than propagating.</summary>
        private static float WrapRadians(float radians)
        {
            var wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return float.IsFinite(wrapped) ? wrapped : 0.0f;
        }
        public Vector3 RotationDegrees { get { return Rotation.ToEulerDegrees(); } }

        public Vector3 Scale
        {
            get => _scale;
            set
            {
                if (!_scale.Equals(value))
                {
                    _scale = value;
                    _needsLocalTransformRebuild = true;
                }
            }
        }

        public bool IncludeInEnvironmentCapture { get; set; } = true;

        /// <summary>
        /// Editor-owned helper geometry such as transform handles and debug bounds. Transient nodes render
        /// normally, but editor hierarchy views omit them and level capture ignores them because they have
        /// no <see cref="LevelSource"/>.
        /// </summary>
        public bool IsEditorTransient { get; set; }

        /// <summary>
        /// How this node was authored, or null for scenery the game mode builds in code. Saving a level
        /// keeps the nodes that have one and skips the rest -- see <see cref="Levels.ManaLevelSource"/>.
        /// </summary>
        public Levels.ManaLevelSource? LevelSource { get; set; }

        private string? _authoredId;
        public string AuthoredId { get => _authoredId ??= Guid.NewGuid().ToString("N"); set => _authoredId = value; }
        public Levels.ManaPrefabLink? PrefabLink { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }

        public Vector3 Right
        {
            get
            {
                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var right = Vector3.TransformNormal(Vector3.UnitX, rotationMatrix);
                return right;
            }
        }

        public Vector3 Up
        {
            get
            {
                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var up = Vector3.TransformNormal(Vector3.UnitY, rotationMatrix);
                return up;
            }
        }

        public Vector3 Forward
        {
            get
            {
                // The scene uses right-handed coordinates, so -z is forward.

                var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
                var up = Vector3.TransformNormal(-Vector3.UnitZ, rotationMatrix);
                return up;
            }
        }

        public Matrix4x4 LocalTransform
        {
            get => _localTransform;
            set
            {
                _localTransform = value;
                _localVersion++;
                // See the big comment above _needsLocalTransformRebuild: any direct LocalTransform
                // write (camera, physics) is authoritative and must not be clobbered by a
                // P/R/S-driven regen on the next UpdateTransform() call.
                _needsLocalTransformRebuild = false;
            }
        }

        public Matrix4x4 WorldTransform
        {
            get => _worldTransform;
            set
            {
                _worldTransform = value;
                _worldVersion++;
            }
        }


        public ManaSceneNode(ManaContext context, Matrix4x4 localTransform) : base(context)
        {
            Parent = null;
            LocalTransform = localTransform;
            WorldTransform = localTransform;
            Children = new List<ManaSceneNode>();
            Components = new List<ManaComponent>();

            Matrix4x4.Decompose(localTransform, out Vector3 dScale, out Quaternion dRotation, out Vector3 dPosition);
            Rotation = dRotation.ToEuler();
            Position = dPosition;
            Scale = dScale;
        }

        public void AddChild(ManaSceneNode child)
        {
            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }
            Children.Add(child);
            child.Parent = this;
            // Versions count per node: the new parent's can equal the old one's, which would keep the old world transform.
            child._worldComputedForParentVersion = -1;
        }

        public void RemoveChild(ManaSceneNode child)
        {
            Children.Remove(child);
            child.Parent = null;
        }

        // virtual so a node can wrap the walk with work that has to straddle it -- see
        // ManaSkeletalActorNode, which poses bones before the walk and skins after it.
        public virtual void UpdateTransform()
        {
            foreach (ManaComponent component in Components)
            {
                if (component.UpdatesBeforeNodeTransform)
                {
                    component.UpdateComponent();
                }
            }

            if (_needsLocalTransformRebuild)
            {
                var localTransform = Matrix4x4.Identity;
                localTransform *= Matrix4x4.CreateScale(Scale);
                localTransform *= Matrix4x4.CreateFromQuaternion(Rotation.ToQuaternionFromEuler());
                localTransform *= Matrix4x4.CreateTranslation(Position);
                LocalTransform = localTransform; // setter bumps _localVersion, clears _needsLocalTransformRebuild
            }

            // Recompute WorldTransform only if our own LocalTransform changed (_localVersion) or the
            // parent's WorldTransform changed (_worldVersion) since we last derived ours from it --
            // otherwise it's already correct and re-deriving it would just reproduce the same value.
            var parentWorldVersion = Parent?._worldVersion ?? 0;
            if (_localVersion != _worldComputedForLocalVersion || parentWorldVersion != _worldComputedForParentVersion)
            {
                WorldTransform = Parent != null ? LocalTransform * Parent.WorldTransform : LocalTransform;
                _worldComputedForLocalVersion = _localVersion;
                _worldComputedForParentVersion = parentWorldVersion;
            }

            // Still visited unconditionally: components (physics, cameras, animations) need their
            // per-frame tick regardless of whether this node's own transform changed, and a child
            // further down the tree may have its own independent reason to be dirty this frame.
            foreach (ManaSceneNode child in Children)
            {
                child.UpdateTransform();
            }

            // By index, not foreach: a component may add one to its own node while it ticks (a Sprite its mesh
            // renderer, a PolygonCollider its body), and the new one simply gets its turn at the end.
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.UpdatesBeforeNodeTransform)
                {
                    component.UpdateComponent();
                }
            }
        }

        public void Draw()
        {
            foreach (ManaSceneNode child in Children)
            {
                child.Draw();
            }

            foreach (ManaComponent component in Components)
            {
                component.DrawComponent();
            }
        }

        public void CollectGpuParticles(List<ManaGpuParticleEmitter> output)
        {
            foreach (var component in Components)
                if (component is Components.ManaGpuParticleEmitterComponent { Enabled: true } emitter)
                    output.Add(emitter.Describe());
            foreach (var child in Children) child.CollectGpuParticles(output);
        }

        public void CollectRenderMeshInstances(List<ManaRenderMeshInstance> output)
        {
            CollectRenderMeshInstances(output, IncludeInEnvironmentCapture, null, null);
        }

        // `glow` is the nearest enabled Glow on this node or above it: it replaces the emission of every mesh
        // beneath (see ManaGlowComponent), leaving the shared materials untouched.
        private void CollectRenderMeshInstances(List<ManaRenderMeshInstance> output, bool includeInEnvironmentCapture, Components.ManaGlowComponent? glow, string? staticBatchKey)
        {
            foreach (var component in Components)
            {
                if (component is Components.ManaGlowComponent { Enabled: true } own)
                {
                    glow = own;
                    break;
                }
            }

            // Editor helpers under a glowing node -- its trigger box, say -- are not the thing that glows.
            if (LevelSource?.MergeParts == true) staticBatchKey = AuthoredId;
            var glowing = includeInEnvironmentCapture ? glow : null;
            var glowStrength = glowing?.CurrentStrength() ?? 0.0f;
            foreach (var component in Components)
            {
                if (component is not MeshRendererComponent renderer || !renderer.IsVisible || renderer.Mesh == null)
                {
                    continue;
                }

                try
                {
                    var meshData = renderer.ApplyMaterialTextures(renderer.Mesh.GetOrCreateCpuData());
                    output.Add(new ManaRenderMeshInstance
                    {
                        StaticBatchKey = staticBatchKey,
                        InstanceKey = renderer.InstanceKey,
                        MeshKey = renderer.Mesh.RenderKey,
                        MeshVersion = renderer.Mesh.CpuDataVersion,
                        Name = string.IsNullOrWhiteSpace(Name) ? renderer.Mesh.Name : Name,
                        MeshData = meshData,
                        WorldTransform = WorldTransform,
                        ShaderName = renderer.Material.Shader?.GetType().Name,
                        Albedo = renderer.Material.Albedo.Value,
                        Metallic = renderer.Material.Metallic.Value,
                        Roughness = renderer.Material.Roughness.Value,
                        FlatShaded = renderer.Material.FlatShaded,
                        Unlit = renderer.Material.Unlit,
                        AmbientOcclusion = renderer.Material.Occlusion.Value,
                        Emission = glowing?.CurrentColor() ?? renderer.Material.Emission.Value,
                        EmissionStrength = glowing != null ? glowStrength : (float)renderer.Material.EmissionStrength,
                        PbrDebugMode = GetPbrDebugMode(renderer.Material),
                        IncludeInEnvironmentCapture = includeInEnvironmentCapture,
                        // Blending is a material decision as much as a shader one: a PBR shader is opaque
                        // by nature, but a PBR material wearing an opacity map is not.
                        IsTransparent = (renderer.Material.Shader?.IsTransparent ?? false)
                            || renderer.Material.EffectiveOpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                        DisableDepthWrite = (renderer.Material.Shader?.DisableDepthWrite ?? false)
                            || renderer.Material.EffectiveOpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                        OpacityMode = renderer.Material.EffectiveOpacityMode,
                        Opacity = (float)renderer.Material.Opacity.Value,
                        AlphaCutoff = (float)renderer.Material.AlphaCutoff,
                        AlwaysOnTop = renderer.AlwaysOnTop,
                        OutlineEnabled = renderer.RenderOutlineEnabled,
                        OutlineColor = renderer.RenderOutlineColor,
                        OutlineWidth = renderer.RenderOutlineWidth,
                        SelectionWireframe = renderer.EditorWireframe,
                        InsetDepth = renderer.Material.InsetDepth,
                        UvTransform = new Vector4(
                            renderer.Material.UvScale.X,
                            renderer.Material.UvScale.Y,
                            renderer.Material.UvOffset.X,
                            renderer.Material.UvOffset.Y),
                        // Inverted on the way out. NormalSelector is a mix toward the vertex normal --
                        // the GLSL does mix(fromMap, vertexNormal, selector), so 0 means "use the map
                        // fully" -- and every loader writes it that way. The render instance carries a
                        // strength instead, because 0 meaning "fully on" is a trap for everything
                        // downstream, the inspector included.
                        NormalStrength = 1.0f - (float)renderer.Material.NormalSelector,
                        CustomShaderUniforms = BuildCustomShaderUniforms(renderer.Material)
                    });
                }
                catch (InvalidOperationException)
                {
                    // Legacy meshes that only exist as backend GPU buffers are skipped.
                }
            }

            foreach (var child in Children)
            {
                child.CollectRenderMeshInstances(output, includeInEnvironmentCapture && child.IncludeInEnvironmentCapture, child.IsEditorTransient ? null : glow, child.IsEditorTransient ? null : staticBatchKey);
            }
        }

        private static float? GetPbrDebugMode(Materials.ManaMaterial material)
        {
            return material.ShaderUniforms.TryGetValue("pbrDebugMode", out var value)
                ? value.FloatValue
                : null;
        }

        // Normalizes ManaMaterial.ShaderUniforms (GL-typed) into a backend-agnostic Vector4 map so
        // WebGPU custom-uniform shaders (see ManaWebGpuContext.Uniforms.cs) can read the same
        // material.SetShaderUniform(...) values the GLSL path already consumes.
        private static IReadOnlyDictionary<string, Vector4>? BuildCustomShaderUniforms(Materials.ManaMaterial material)
        {
            material.EnsureShaderMetadataDefaults();

            if (material.ShaderUniforms.Count == 0)
            {
                return null;
            }

            Dictionary<string, Vector4>? result = null;
            foreach (var entry in material.ShaderUniforms)
            {
                Vector4? packed = entry.Value.Type switch
                {
                    ManaCore.Graphics.ManaUniformType.Float => new Vector4(entry.Value.FloatValue, 0.0f, 0.0f, 0.0f),
                    ManaCore.Graphics.ManaUniformType.FloatVec2 => new Vector4(entry.Value.Vector2Value, 0.0f, 0.0f),
                    ManaCore.Graphics.ManaUniformType.FloatVec3 => new Vector4(entry.Value.Vector3Value, 0.0f),
                    ManaCore.Graphics.ManaUniformType.FloatVec4 => entry.Value.Vector4Value,
                    ManaCore.Graphics.ManaUniformType.Int => new Vector4(entry.Value.IntValue, 0.0f, 0.0f, 0.0f),
                    ManaCore.Graphics.ManaUniformType.Bool => new Vector4(entry.Value.BoolValue ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f),
                    _ => null
                };

                if (packed.HasValue)
                {
                    result ??= new Dictionary<string, Vector4>(material.ShaderUniforms.Count);
                    result[entry.Key] = packed.Value;
                }
            }

            return result;
        }

        public void RotateX(float degrees)
        {
            Rotation = new Vector3(Rotation.X + degrees.ToRadians(), Rotation.Y, Rotation.Z);
        }

        public void RotateY(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y + degrees.ToRadians(), Rotation.Z);
        }

        public void RotateZ(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, Rotation.Z + degrees.ToRadians());
        }

        public void SetLocalTransform(Matrix4x4 localTransform)
        {
            LocalTransform = localTransform;
        }

        public Matrix4x4 GetLocalTransform()
        {
            return LocalTransform;
        }

        public List<T> FindShapes<T>()
        {
            List<T> output = new List<T>();

            foreach (var component in Components)
            {
                if (component is MeshRendererComponent renderer)
                {
                    if (renderer.Mesh is T shape)
                    {
                        output.Add(shape);
                    }
                }
            }

            return output;
        }

        public List<T> GetComponents<T>()
        {
            return Components.OfType<T>().ToList();
        }

        /// <summary>
        /// The first component of this type (or one derived from it), or null if the node has none. What a component
        /// that works alongside another on its node -- a listener and its trigger, a platform and its collider --
        /// reaches for, rather than looping over <see cref="Components"/> itself.
        /// </summary>
        public T? GetComponent<T>() where T : class
        {
            foreach (var component in Components)
            {
                if (component is T match)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>The component a <see cref="ManaComponentReference"/> on this node points at, or null.</summary>
        public T? GetComponent<T>(ManaComponentReference reference) where T : class
        {
            return reference.Resolve<T>(this);
        }

        /// <summary><see cref="GetComponent{T}()"/> in the shape that reads well in an <c>if</c>.</summary>
        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>()!;
            return component != null;
        }
    }


    public class ManaMeshSceneNode : ManaSceneNode
    {
        public MeshRendererComponent MeshRenderer;
        public ManaMesh Mesh;

        public ManaMeshSceneNode(ManaContext context, ManaMesh mesh) : base(context, Matrix4x4.Identity)
        {
            Mesh = mesh;
            MeshRenderer = new MeshRendererComponent(this, mesh);
            Components.Add(MeshRenderer);
        }
    }
}
