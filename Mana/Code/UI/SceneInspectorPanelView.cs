using ImGuiNET;
using Mana.Code.Materials;
using Mana.Code.ManaShaders;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Silk.NET.OpenGL;
using Mana;
using Mana.Code;
using ManaCore.Graphics;

namespace Mana.Code.UI
{
    public sealed class SceneInspectorPanelView
    {
        // Extensibility seam for node types that can't be special-cased here directly (e.g. types
        // defined in a downstream project like ManaGeoTools, which Mana can't reference without a
        // circular dependency). A consuming app assigns this once at startup; called with whatever
        // node is currently selected, so the hook itself decides what (if anything) to draw.
        public static Action<ManaSceneNode>? CustomNodeInspectorHook { get; set; }

        // Component-level counterpart to CustomNodeInspectorHook -- invoked whenever
        // SceneSelectionState.SelectedComponent is set (e.g. a specific patch on one of PlanetNode's
        // shared root nodes), in addition to (not instead of) the node hook above.
        public static Action<ManaComponent>? CustomComponentInspectorHook { get; set; }

        private static readonly HashSet<string> AutomaticUniformNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "gWVP",
            "gWorld",
            "model",
            "modelInverse",
            "inverseModel",
            "inverseModelMatrix",
            "gWorldInverse",
            "modelViewMatrix",
            "projectionMatrix",
            "projection",
            "view",
            "viewMatrix",
            "normalMatrix",
            "objPos",
            "object",
            "obj",
            "camPos",
            "iCameraPosition",
            "gEyeWorldPos",
            "camPosObject",
            "iCameraPositionObject",
            "cameraObjectPosition",
            "objectCameraPos",
            "iResolution",
            "iTime",
            "uTime",
            "iChannelTime",
            "iChannelResolution",
            "iMouse",
            "iDate",
            "iSampleRate",
            "iFrame",
            "iTimeDelta",
            "iFrameRate",
            "iChannel0",
            "iChannel1",
            "iChannel2",
            "baseColor",
            "gColorMap",
            "FrameNumber",
            "ViewParams",
            "gPointLight.Base.Color",
            "gPointLight.Position",
            "gPointLight.Atten.Constant",
            "gCameraWorldMatrix",
            "gProjectionMatrix",
            "gCameraWorldMatrixProjection",
            "gPointLight.Base.DiffuseIntensity",
            // Ocean tier cross-fade / wave-plane framing, rewritten every frame by
            // PlanetNode.UpdateNearOcean -- editing them would be overwritten before the next draw.
            "nearFadeCenter",
            "nearFadeInnerRadius",
            "nearFadeOuterRadius",
            "waveEast",
            "waveNorth",
            "unitsPerMeter",
            "waveTileMeters",
            "vertexSpacingMeters",
            "depthLiftCoefficient",
            "depthLiftMaxUnits",
        };

        private readonly SceneSelectionState selectionState;

        public SceneInspectorPanelView(SceneSelectionState selectionState)
        {
            this.selectionState = selectionState;
        }

        public void Draw(ImFontPtr inspectorFont)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            var useInspectorFont = !inspectorFont.Equals(default(ImFontPtr));
            if (useInspectorFont)
            {
                ImGui.PushFont(inspectorFont);
            }

            var visible = ImGui.Begin("Inspector");
            try
            {
                if (visible)
                {
                    DrawSelectionInspector();
                }
            }
            finally
            {
                ImGui.End();
                if (useInspectorFont)
                {
                    ImGui.PopFont();
                }
            }
        }

        private void DrawSelectionInspector()
        {
            if (selectionState.SelectedNode == null)
            {
                ImGui.TextDisabled("No node selected");
                return;
            }

            ImGui.PushID($"inspector_{selectionState.SelectedNode.GetHashCode()}");
            DrawSelectedNodeInspector(selectionState.SelectedNode);
            ImGui.PopID();

            if (selectionState.SelectedComponent != null)
            {
                ImGui.PushID($"inspector_component_{selectionState.SelectedComponent.GetHashCode()}");
                CustomComponentInspectorHook?.Invoke(selectionState.SelectedComponent);
                ImGui.PopID();
            }
        }

        private void DrawSelectedNodeInspector(ManaSceneNode node)
        {
            var nodeName = node.Name;
            if (BeginPropertyTable("NodeProperties"))
            {
                if (DrawInputTextProperty("Name", ref nodeName, 160))
                {
                    node.Name = nodeName;
                }

                DrawReadOnlyProperty("Type", node.GetType().Name);
                ImGui.EndTable();
            }

            ImGui.SeparatorText("Transform");
            DrawTransformEditor(node);

            if (node is ManaVoxelsNode voxelsNode)
            {
                DrawVoxelsNodeInspector(voxelsNode);
            }

            if (node is ManaPawnNode pawn)
            {
                DrawPawnInspector(pawn);
            }

            if (node is ManaSkeletalActorNode skeletalActor)
            {
                DrawSkeletalActorInspector(skeletalActor);
            }

            CustomNodeInspectorHook?.Invoke(node);

            if (node.Components.Count == 0)
            {
                return;
            }

            // When a specific component on this node is selected (e.g. one patch on a PlanetNode shared
            // root node hosting many), show just that one's detail instead of dumping every sibling --
            // the node can host components belonging to unrelated things once one node is shared.
            if (selectionState.SelectedComponent != null && node.Components.Contains(selectionState.SelectedComponent))
            {
                ImGui.SeparatorText("Component");
                ImGui.PushID($"inspect_component_{selectionState.SelectedComponent.GetHashCode()}");
                DrawComponentInspector(selectionState.SelectedComponent);
                ImGui.PopID();
                return;
            }

            ImGui.SeparatorText($"Components ({node.Components.Count})");
            foreach (var component in node.Components)
            {
                ImGui.PushID($"inspect_component_{component.GetHashCode()}");
                DrawComponentInspector(component);
                ImGui.PopID();
            }
        }

        private static void DrawTransformEditor(ManaSceneNode node)
        {
            var position = node.Position;
            var rotationDegrees = node.RotationDegrees;
            var scale = node.Scale;
            var changed = false;

            if (!BeginPropertyTable("TransformProperties"))
            {
                return;
            }

            if (DrawVector3Property("Position", ref position))
            {
                node.Position = position;
                changed = true;
            }

            if (DrawVector3Property("Rotation", ref rotationDegrees))
            {
                node.Rotation = new Vector3(
                    rotationDegrees.X.ToRadians(),
                    rotationDegrees.Y.ToRadians(),
                    rotationDegrees.Z.ToRadians());
                changed = true;
            }

            if (DrawVector3Property("Scale", ref scale))
            {
                node.Scale = scale;
                changed = true;
            }

            ImGui.EndTable();

            if (changed)
            {
                node.UpdateTransform();
            }
        }

        private void DrawComponentInspector(ManaComponent component)
        {
            if (component is MeshRendererComponent meshRenderer)
            {
                DrawMeshRendererInspector(meshRenderer);
                return;
            }

            if (component is ManaLightSourceComponent light)
            {
                DrawLightSourceInspector(light);
                return;
            }

            if (component is ManaSkeletalAnimationComponent animation)
            {
                DrawSkeletalAnimationInspector(animation);
                return;
            }

            if (component is ManaSkinnedMeshComponent skinnedMesh)
            {
                DrawSkinnedMeshInspector(skinnedMesh);
                return;
            }

            if (component is ManaSpringArmComponent springArm)
            {
                DrawSpringArmInspector(springArm);
                return;
            }

            ImGui.TextUnformatted(GetComponentLabel(component));
        }

        private void DrawMeshRendererInspector(MeshRendererComponent meshRenderer)
        {
            if (!ImGui.CollapsingHeader(GetComponentLabel(meshRenderer), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            if (BeginPropertyTable("MeshRendererProperties"))
            {
                DrawReadOnlyProperty("Mesh", meshRenderer.Mesh.GetType().Name);

                var outlineEnabled = meshRenderer.OutlineEnabled;
                if (DrawCheckboxProperty("Outline", ref outlineEnabled))
                {
                    meshRenderer.OutlineEnabled = outlineEnabled;
                }

                var outlineColor = meshRenderer.OutlineColor;
                if (DrawColorProperty("Outline Color", ref outlineColor))
                {
                    meshRenderer.OutlineColor = outlineColor;
                }

                var outlineWidth = meshRenderer.OutlineWidth;
                if (DrawSliderFloatProperty("Outline Width", ref outlineWidth, 1.0f, 4.0f))
                {
                    meshRenderer.OutlineWidth = outlineWidth;
                }

                ImGui.EndTable();
            }

            DrawPrimitiveMeshInspector(meshRenderer.Mesh);
            DrawMaterialInspector(meshRenderer.Material);
        }

        private void DrawPrimitiveMeshInspector(ManaMesh mesh)
        {
            if (mesh is not ManaProceduralMesh proceduralMesh)
            {
                return;
            }

            if (!ImGui.CollapsingHeader("Primitive", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var changed = false;
            if (BeginPropertyTable("PrimitiveProperties"))
            {
                switch (mesh)
                {
                    case ManaTorus torus:
                        changed |= DrawTorusInspector(torus);
                        break;
                    case ManaSphere sphere:
                        changed |= DrawSphereInspector(sphere);
                        break;
                    case ManaIsoSphere isoSphere:
                        changed |= DrawIsoSphereInspector(isoSphere);
                        break;
                    case ManaIcosahedron icosahedron:
                        changed |= DrawRadiusInspector(icosahedron);
                        break;
                    case ManaCube cube:
                        changed |= DrawCubeInspector(cube);
                        break;
                    case ManaPyramid pyramid:
                        changed |= DrawPyramidInspector(pyramid);
                        break;
                }

                ImGui.EndTable();
            }

            if (changed)
            {
                proceduralMesh.RebuildGeometry();
            }
        }

        private static bool DrawTorusInspector(ManaTorus torus)
        {
            var changed = false;
            var innerRadius = torus.InnerRadius;
            var outerRadius = torus.OuterRadius;
            var majorSegments = torus.MajorSegments;
            var minorSegments = torus.MinorSegments;

            if (DrawPositiveFloat("Inner Radius", ref innerRadius, 0.001f))
            {
                torus.SetInnerOuterRadii(innerRadius, outerRadius);
                changed = true;
            }

            if (DrawPositiveFloat("Outer Radius", ref outerRadius, 0.002f))
            {
                torus.SetInnerOuterRadii(torus.InnerRadius, outerRadius);
                changed = true;
            }

            if (DrawSliderIntProperty("Major Segments", ref majorSegments, 3, 192))
            {
                torus.MajorSegments = Math.Clamp(majorSegments, 3, 192);
                changed = true;
            }

            if (DrawSliderIntProperty("Minor Segments", ref minorSegments, 3, 96))
            {
                torus.MinorSegments = Math.Clamp(minorSegments, 3, 96);
                changed = true;
            }

            DrawReadOnlyProperty("Major Radius", $"{torus.MajorRadius:0.###}");
            DrawReadOnlyProperty("Minor Radius", $"{torus.MinorRadius:0.###}");
            return changed;
        }

        private static bool DrawSphereInspector(ManaSphere sphere)
        {
            var changed = false;
            var radius = sphere.Radius;
            var latitudeSegments = sphere.LatitudeSegments;
            var longitudeSegments = sphere.LongitudeSegments;

            if (DrawPositiveFloat("Radius", ref radius, 0.001f))
            {
                sphere.Radius = radius;
                changed = true;
            }

            if (DrawSliderIntProperty("Latitude Segments", ref latitudeSegments, 3, 192))
            {
                sphere.LatitudeSegments = Math.Clamp(latitudeSegments, 3, 192);
                changed = true;
            }

            if (DrawSliderIntProperty("Longitude Segments", ref longitudeSegments, 3, 192))
            {
                sphere.LongitudeSegments = Math.Clamp(longitudeSegments, 3, 192);
                changed = true;
            }

            return changed;
        }

        private static bool DrawIsoSphereInspector(ManaIsoSphere isoSphere)
        {
            var changed = false;
            var radius = isoSphere.Radius;
            var recursionLevel = isoSphere.RecursionLevel;

            if (DrawPositiveFloat("Radius", ref radius, 0.001f))
            {
                isoSphere.Radius = radius;
                changed = true;
            }

            if (DrawSliderIntProperty("Subdivisions", ref recursionLevel, 0, 5))
            {
                isoSphere.RecursionLevel = Math.Clamp(recursionLevel, 0, 5);
                changed = true;
            }

            return changed;
        }

        private static bool DrawRadiusInspector(ManaIcosahedron icosahedron)
        {
            var radius = icosahedron.Radius;
            if (!DrawPositiveFloat("Radius", ref radius, 0.001f))
            {
                return false;
            }

            icosahedron.Radius = radius;
            return true;
        }

        private static bool DrawCubeInspector(ManaCube cube)
        {
            var size = cube.CubeSize;
            if (!DrawVector3Inspector("Size", ref size, 0.001f))
            {
                return false;
            }

            cube.CubeSize = size;
            return true;
        }

        private static bool DrawPyramidInspector(ManaPyramid pyramid)
        {
            var size = pyramid.CubeSize;
            if (!DrawVector3Inspector("Size", ref size, 0.001f))
            {
                return false;
            }

            pyramid.CubeSize = size;
            return true;
        }

        private static bool DrawVector3Inspector(string label, ref Vector3 value, float min)
        {
            var edited = value;
            if (!DrawVector3Property(label, ref edited))
            {
                return false;
            }

            value = new Vector3(
                MathF.Max(min, edited.X),
                MathF.Max(min, edited.Y),
                MathF.Max(min, edited.Z));
            return true;
        }

        private static bool DrawPositiveFloat(string label, ref float value, float min)
        {
            var edited = value;
            if (!DrawDragFloatProperty(label, ref edited, 0.01f))
            {
                return false;
            }

            value = MathF.Max(min, edited);
            return true;
        }

        private void DrawMaterialInspector(ManaMaterial material)
        {
            if (!ImGui.CollapsingHeader("Material", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var materialName = material.Name;

            // Albedo/Emission/Metallic/Roughness/Occlusion/NormalSelector/EmissionStrength are only ever
            // read by ManaPbrShader.BindShader — "shader toy" style shaders (lightning ball, liquid ball,
            // etc.) ignore them entirely, so showing them there is misleading.
            var usesPbrChannels = material.Shader is ManaPbrShader;
            if (BeginPropertyTable("MaterialProperties"))
            {
                if (DrawInputTextProperty("Name", ref materialName, 160))
                {
                    material.Name = materialName;
                }

                DrawReadOnlyProperty("Shader", material.Shader?.GetType().Name ?? "None");

                if (usesPbrChannels)
                {
                    DrawColorEditor("Albedo", material.Albedo);
                    DrawColorEditor("Emission", material.Emission);
                    DrawScalarEditor("Metallic", material.Metallic, 0.0f, 1.0f);
                    DrawScalarEditor("Roughness", material.Roughness, 0.0f, 1.0f);
                    DrawScalarEditor("Occlusion", material.Occlusion, 0.0f, 1.0f);

                    var normalSelector = (float)material.NormalSelector;
                    if (DrawSliderFloatProperty("Normal Texture Mix", ref normalSelector, 0.0f, 1.0f))
                    {
                        material.NormalSelector = normalSelector;
                    }

                    var emissionStrength = (float)material.EmissionStrength;
                    if (DrawSliderFloatProperty("Emission Strength", ref emissionStrength, 0.0f, 25.0f))
                    {
                        material.EmissionStrength = emissionStrength;
                    }

                    var flatShaded = material.FlatShaded;
                    if (DrawCheckboxProperty("Flat Shading", ref flatShaded))
                    {
                        material.FlatShaded = flatShaded;
                    }
                }

                ImGui.EndTable();
            }

            if (!usesPbrChannels)
            {
                ImGui.TextDisabled("This shader ignores the standard PBR channels; see Shader Uniforms below.");
            }

            DrawShaderUniformInspector(material);
        }

        private static void DrawShaderUniformInspector(ManaMaterial material)
        {
            EnsureKnownCustomUniforms(material);
            var shaderName = material.Shader?.GetType().Name;
            var schema = string.IsNullOrWhiteSpace(shaderName)
                ? ManaWebGpuCustomUniformSchema.Empty
                : ManaWebGpuCustomUniformSchemaCache.GetSchema(shaderName);

            // Editable overrides come straight from ManaMaterial.ShaderUniforms — backend-agnostic,
            // since it's populated by SetShaderUniform(...) regardless of whether a GLSL shader ever
            // compiled for this material (e.g. running the WebGPU backend, where CompiledShader below
            // stays empty since GL compilation is skipped entirely).
            var editableUniforms = material.ShaderUniforms
                .Where(entry => !IsAutomaticUniformName(entry.Key) && CanEditUniform(entry.Value.Type))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();

            if (!ImGui.CollapsingHeader("Shader Uniforms", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var propertyTableOpen = editableUniforms.Count > 0 && BeginPropertyTable("ShaderUniformProperties");
            if (propertyTableOpen)
            {
                foreach (var entry in editableUniforms)
                {
                    ImGui.PushID($"uniform_{entry.Key}");
                    schema.FieldMap.TryGetValue(entry.Key, out var schemaField);
                    DrawEditableUniform(material, entry.Key, entry.Value.Type, schemaField);
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            if (editableUniforms.Count == 0)
            {
                var diagnostic = ManaWebGpuCustomUniformSchemaCache.GetSchemaDiagnostic(shaderName);
                if (!string.IsNullOrWhiteSpace(diagnostic))
                {
                    ImGui.TextDisabled(diagnostic);
                }
                else if (schema.Fields.Count > 0)
                {
                    ImGui.TextDisabled($"Found {schema.Fields.Count} WGSL properties, but no editable material uniform values are populated.");
                }
                else
                {
                    ImGui.TextDisabled("No editable material uniforms");
                }
            }

            // GLSL-reflected uniform list, only available when a GLSL shader actually compiled for
            // this material (OpenGL backend). Shows the automatic/engine uniforms (transforms, iTime,
            // etc.) that were never set via SetShaderUniform, for reference.
            var compiledUniforms = material.Shader?.CompiledShader?.UniformNames;
            if (compiledUniforms is { Count: > 0 })
            {
                var sortedUniforms = compiledUniforms.OrderBy(uniform => uniform.name).ToList();
                var automaticUniforms = sortedUniforms.Where(IsAutomaticUniform).ToList();
                var unsupportedUniforms = sortedUniforms
                    .Where(uniform => !IsAutomaticUniform(uniform) && !CanEditUniform(uniform.type))
                    .ToList();

                if (automaticUniforms.Count > 0)
                {
                    ImGui.SeparatorText($"Automatic / Engine ({automaticUniforms.Count})");
                    foreach (var uniform in automaticUniforms)
                    {
                        ImGui.BulletText($"{uniform.name}: {uniform.type}");
                    }
                }

                if (unsupportedUniforms.Count > 0)
                {
                    ImGui.SeparatorText($"Unsupported ({unsupportedUniforms.Count})");
                    foreach (var uniform in unsupportedUniforms)
                    {
                        ImGui.BulletText($"{uniform.name}: {uniform.type}");
                    }
                }
            }

            if (material.DefaultShaderUniforms.Count > 0)
            {
                ImGui.Spacing();
                if (ImGui.SmallButton("Reset All to Defaults"))
                {
                    foreach (var entry in material.DefaultShaderUniforms)
                    {
                        material.ShaderUniforms[entry.Key] = entry.Value.Clone();
                    }
                }
            }
        }

        private static void EnsureKnownCustomUniforms(ManaMaterial material)
        {
            material.EnsureShaderMetadataDefaults();
        }

        private static void DrawEditableUniform(
            ManaMaterial material,
            string name,
            UniformType type,
            ManaCustomUniformFieldDefinition? schemaField)
        {
            var value = material.ShaderUniforms.TryGetValue(name, out var existing)
                ? existing
                : CreateDefaultUniformValue(type);
            var changed = false;
            var hasDefault = material.DefaultShaderUniforms.TryGetValue(name, out var defaultValue);
            var showReset = hasDefault && !UniformValuesEqual(type, value, defaultValue!);
            var resetWidth = showReset
                ? ImGui.CalcTextSize("Reset").X + ImGui.GetStyle().FramePadding.X * 2.0f + ImGui.GetStyle().ItemSpacing.X
                : 0.0f;
            var isColor = LooksLikeColorUniformName(name);
            var hasSoftRange = TryGetSoftRange(schemaField, out var softMin, out var softMax);
            BeginPropertyRow(name);
            ImGui.SetNextItemWidth(showReset ? -resetWidth : -1.0f);
            PushCompactValueStyle();

            switch (type)
            {
                case UniformType.Float:
                    var floatValue = value.FloatValue;
                    if (hasSoftRange)
                    {
                        changed = ImGui.DragFloat("##Value", ref floatValue, 0.01f, softMin, softMax);
                    }
                    else
                    {
                        changed = ImGui.DragFloat("##Value", ref floatValue, 0.01f);
                    }
                    value.FloatValue = floatValue;
                    break;
                case UniformType.FloatVec2:
                    var vector2Value = value.Vector2Value;
                    changed = ImGuiExt.DrawVector2Value(ref vector2Value, resetWidth);
                    value.Vector2Value = vector2Value;
                    break;
                case UniformType.FloatVec3:
                    if (isColor)
                    {
                        var colorValue = new Vector4(value.Vector3Value, 1.0f);
                        changed = ImGuiExt.DrawColorValue(ref colorValue);
                        value.Vector3Value = new Vector3(colorValue.X, colorValue.Y, colorValue.Z);
                    }
                    else
                    {
                        var vector3Value = value.Vector3Value;
                        changed = ImGuiExt.DrawVector3Value(ref vector3Value, resetWidth);
                        value.Vector3Value = vector3Value;
                    }
                    break;
                case UniformType.FloatVec4:
                    if (isColor)
                    {
                        var colorValue = value.Vector4Value;
                        changed = ImGuiExt.DrawColorValue(ref colorValue);
                        value.Vector4Value = colorValue;
                    }
                    else
                    {
                        var vector4Value = value.Vector4Value;
                        changed = ImGuiExt.DrawVector4Value(ref vector4Value, resetWidth);
                        value.Vector4Value = vector4Value;
                    }
                    break;
                case UniformType.Int:
                    var intValue = value.IntValue;
                    if (hasSoftRange)
                    {
                        changed = ImGui.DragInt("##Value", ref intValue, 1.0f, (int)softMin, (int)softMax);
                    }
                    else
                    {
                        changed = ImGui.InputInt("##Value", ref intValue);
                    }
                    value.IntValue = intValue;
                    break;
                case UniformType.Bool:
                    var boolValue = value.BoolValue;
                    changed = ImGui.Checkbox("##Value", ref boolValue);
                    value.BoolValue = boolValue;
                    break;
            }

                    PopCompactValueStyle();

            if (changed)
            {
                value.Type = type;
                material.ShaderUniforms[name] = value;
            }

            if (showReset)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset"))
                {
                    material.ShaderUniforms[name] = defaultValue!.Clone();
                }
            }
        }

        private static bool TryGetSoftRange(
            ManaCustomUniformFieldDefinition? schemaField,
            out float min,
            out float max)
        {
            min = 0.0f;
            max = 0.0f;
            if (!schemaField.HasValue)
            {
                return false;
            }

            if (!schemaField.Value.SoftMin.HasValue || !schemaField.Value.SoftMax.HasValue)
            {
                return false;
            }

            min = schemaField.Value.SoftMin.Value;
            max = schemaField.Value.SoftMax.Value;
            return max > min;
        }

        private static bool LooksLikeColorUniformName(string name)
        {
            return name.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("tint", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UniformValuesEqual(UniformType type, ManaMaterialUniformValue a, ManaMaterialUniformValue b)
        {
            return type switch
            {
                UniformType.Float => a.FloatValue.Equals(b.FloatValue),
                UniformType.FloatVec2 => a.Vector2Value.Equals(b.Vector2Value),
                UniformType.FloatVec3 => a.Vector3Value.Equals(b.Vector3Value),
                UniformType.FloatVec4 => a.Vector4Value.Equals(b.Vector4Value),
                UniformType.Int => a.IntValue == b.IntValue,
                UniformType.Bool => a.BoolValue == b.BoolValue,
                _ => true
            };
        }

        private static ManaMaterialUniformValue CreateDefaultUniformValue(UniformType type)
        {
            return new ManaMaterialUniformValue
            {
                Type = type,
                BoolValue = true,
                FloatValue = 1.0f,
                Vector2Value = Vector2.One,
                Vector3Value = Vector3.One,
                Vector4Value = Vector4.One
            };
        }

        private static bool CanEditUniform(UniformType type)
        {
            return type is UniformType.Float
                or UniformType.FloatVec2
                or UniformType.FloatVec3
                or UniformType.FloatVec4
                or UniformType.Int
                or UniformType.Bool;
        }

        private static bool IsAutomaticUniform(ManaShaderUniform uniform)
        {
            return IsAutomaticUniformName(uniform.name);
        }

        private static bool IsAutomaticUniformName(string name)
        {
            return AutomaticUniformNames.Contains(GetUniformBaseName(name));
        }

        private static string GetUniformBaseName(string uniformName)
        {
            var bracketIndex = uniformName.IndexOf('[');
            return bracketIndex < 0 ? uniformName : uniformName[..bracketIndex];
        }

        private static void DrawColorEditor(string label, ManaTextureVector4 value)
        {
            var color = value.Value;
            if (DrawColorProperty(label, ref color))
            {
                value.Value = color;
            }
        }

        private static void DrawScalarEditor(string label, ManaTextureSingle value, float min, float max)
        {
            var scalar = value.Value;
            if (DrawSliderFloatProperty(label, ref scalar, min, max))
            {
                value.Value = scalar;
            }
        }

        private void DrawLightSourceInspector(ManaLightSourceComponent light)
        {
            if (!ImGui.CollapsingHeader(GetComponentLabel(light), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            if (BeginPropertyTable("LightProperties"))
            {
                var enabled = light.Enabled;
                if (DrawCheckboxProperty("Enabled", ref enabled))
                {
                    light.Enabled = enabled;
                }

                var intensity = light.Intensity;
                if (DrawSliderFloatProperty("Intensity", ref intensity, 0.0f, 8.0f))
                {
                    light.Intensity = intensity;
                }

                var color = light.Color;
                if (DrawColorProperty("Color", ref color))
                {
                    light.Color = color;
                }

                var direction = light.Direction;
                if (DrawVector3Property("Direction", ref direction))
                {
                    light.Direction = direction;
                }

                ImGui.EndTable();
            }
        }

        private static bool BeginPropertyTable(string id)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings;

            var style = ImGui.GetStyle();
            var compactCellPadding = new Vector2(
                style.CellPadding.X,
                ManaUiDensity.PropertyTableCellPaddingY);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, compactCellPadding);

            if (!ImGui.BeginTable(id, 2, flags))
            {
                ImGui.PopStyleVar();
                return false;
            }

            ImGui.PopStyleVar();

            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 112.0f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        private static void BeginPropertyRow(string label)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
        }

        private static void DrawReadOnlyProperty(string label, string value)
        {
            BeginPropertyRow(label);
            ImGui.TextUnformatted(value);
        }

        private static bool DrawInputTextProperty(string label, ref string value, uint maxLength)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.InputText("##Value", ref value, maxLength);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawCheckboxProperty(string label, ref bool value)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            PushCompactValueStyle();
            var changed = ImGui.Checkbox("##Value", ref value);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawSliderFloatProperty(string label, ref float value, float min, float max)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.SliderFloat("##Value", ref value, min, max);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawSliderIntProperty(string label, ref int value, int min, int max)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.SliderInt("##Value", ref value, min, max);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawDragFloatProperty(string label, ref float value, float speed)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.DragFloat("##Value", ref value, speed);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static void PushCompactValueStyle()
        {
            var style = ImGui.GetStyle();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, ManaUiDensity.CompactValueFramePaddingY));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, ManaUiDensity.CompactValueItemSpacingY));
        }

        private static void PopCompactValueStyle()
        {
            ImGui.PopStyleVar(2);
        }

        private void DrawVoxelsNodeInspector(ManaVoxelsNode voxelsNode)
        {
            ImGui.SeparatorText("Voxel Node");
            if (BeginPropertyTable("VoxelNodeProperties"))
            {
                DrawReadOnlyProperty("Source", voxelsNode.Source);
                ImGui.EndTable();
            }
        }

        private static void DrawSpringArmInspector(ManaSpringArmComponent springArm)
        {
            if (!ImGui.CollapsingHeader(GetComponentLabel(springArm), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            if (BeginPropertyTable("SpringArmProperties"))
            {
                var armLength = springArm.ArmLength;
                if (DrawSliderFloatProperty("Arm length", ref armLength, springArm.MinArmLength, springArm.MaxArmLength))
                {
                    springArm.ArmLength = armLength;
                }

                DrawReadOnlyProperty("Current length", springArm.IsProbeShortened
                    ? $"{springArm.CurrentArmLength:0.00} (pulled in)"
                    : $"{springArm.CurrentArmLength:0.00}");

                var socketOffset = springArm.SocketOffset;
                if (DrawVector3Property("Socket offset", ref socketOffset))
                {
                    springArm.SocketOffset = socketOffset;
                }

                var lagSpeed = springArm.SocketLagSpeed;
                if (DrawSliderFloatProperty("Lag speed", ref lagSpeed, 0.0f, 40.0f))
                {
                    springArm.SocketLagSpeed = lagSpeed;
                }

                var probe = springArm.ProbeForCollision;
                if (DrawCheckboxProperty("Probe collision", ref probe))
                {
                    springArm.ProbeForCollision = probe;
                }

                var probeBuffer = springArm.ProbeBuffer;
                if (DrawSliderFloatProperty("Probe buffer", ref probeBuffer, 0.0f, 2.0f))
                {
                    springArm.ProbeBuffer = probeBuffer;
                }

                var autoAlign = springArm.AutoAlignBehindTarget;
                if (DrawCheckboxProperty("Auto align", ref autoAlign))
                {
                    springArm.AutoAlignBehindTarget = autoAlign;
                }

                var autoAlignSpeed = springArm.AutoAlignSpeed;
                if (DrawSliderFloatProperty("Align speed", ref autoAlignSpeed, 0.0f, 12.0f))
                {
                    springArm.AutoAlignSpeed = autoAlignSpeed;
                }

                DrawReadOnlyProperty("Yaw / Pitch", $"{springArm.YawRadians.ToDegrees():0.} / {springArm.PitchRadians.ToDegrees():0.}");

                ImGui.EndTable();
            }
        }

        private static void DrawPawnInspector(ManaPawnNode pawn)
        {
            ImGui.SeparatorText("Pawn");
            if (BeginPropertyTable("PawnProperties"))
            {
                DrawReadOnlyProperty("Possessed", pawn.IsPossessed ? "Yes" : "No");
                DrawReadOnlyProperty("Speed", $"{pawn.CurrentSpeed:0.00} u/s");
                DrawReadOnlyProperty("Destination", pawn.Destination.HasValue
                    ? $"{pawn.Destination.Value.X:0.0}, {pawn.Destination.Value.Y:0.0}, {pawn.Destination.Value.Z:0.0}"
                    : "<none>");

                var walkSpeed = pawn.WalkSpeed;
                if (DrawSliderFloatProperty("Walk speed", ref walkSpeed, 0.1f, 20.0f))
                {
                    pawn.WalkSpeed = walkSpeed;
                }

                var runSpeed = pawn.RunSpeed;
                if (DrawSliderFloatProperty("Run speed", ref runSpeed, 0.1f, 40.0f))
                {
                    pawn.RunSpeed = runSpeed;
                }

                var turnSpeed = pawn.TurnDegreesPerSecond;
                if (DrawSliderFloatProperty("Turn deg/s", ref turnSpeed, 45.0f, 1440.0f))
                {
                    pawn.TurnDegreesPerSecond = turnSpeed;
                }

                // Exposed because it is model-dependent: a rig whose mesh faces -Z instead of +Z walks
                // backwards until this is 180.
                var facingOffset = pawn.FacingOffsetDegrees;
                if (DrawSliderFloatProperty("Facing offset", ref facingOffset, -180.0f, 180.0f))
                {
                    pawn.FacingOffsetDegrees = facingOffset;
                }

                var groundHeight = pawn.GroundHeight;
                if (DrawDragFloatProperty("Ground Y", ref groundHeight, 0.05f))
                {
                    pawn.GroundHeight = groundHeight;
                }

                ImGui.EndTable();
            }

            ImGui.SeparatorText("Physics & Jump");
            if (BeginPropertyTable("PawnPhysicsProperties"))
            {
                DrawReadOnlyProperty("Capsule", pawn.Body == null
                    ? "<none - kinematic>"
                    : $"r={pawn.Body.Radius:0.00} h={pawn.Body.Height:0.00}");

                if (pawn.Body != null)
                {
                    DrawReadOnlyProperty("Grounded", pawn.IsGrounded ? "Yes" : "No");
                    DrawReadOnlyProperty("Vertical vel", $"{pawn.Body.Velocity.Y:0.00} u/s");
                    DrawReadOnlyProperty("Jumps used", $"{pawn.JumpsUsed} / {pawn.MaxJumpCount}");

                    var jumpHeight = pawn.JumpHeight;
                    if (DrawSliderFloatProperty("Jump height", ref jumpHeight, 0.5f, 12.0f))
                    {
                        pawn.JumpHeight = jumpHeight;
                    }

                    var releaseCutoff = pawn.JumpReleaseCutoff;
                    if (DrawSliderFloatProperty("Release cutoff", ref releaseCutoff, 0.0f, 1.0f))
                    {
                        pawn.JumpReleaseCutoff = releaseCutoff;
                    }

                    var fallMultiplier = pawn.FallGravityMultiplier;
                    if (DrawSliderFloatProperty("Fall gravity", ref fallMultiplier, 1.0f, 4.0f))
                    {
                        pawn.FallGravityMultiplier = fallMultiplier;
                    }

                    var airControl = pawn.AirControl;
                    if (DrawSliderFloatProperty("Air control", ref airControl, 0.0f, 1.0f))
                    {
                        pawn.AirControl = airControl;
                    }

                    var coyoteTime = pawn.CoyoteTimeSeconds;
                    if (DrawSliderFloatProperty("Coyote time", ref coyoteTime, 0.0f, 0.4f))
                    {
                        pawn.CoyoteTimeSeconds = coyoteTime;
                    }

                    var jumpBuffer = pawn.JumpBufferSeconds;
                    if (DrawSliderFloatProperty("Jump buffer", ref jumpBuffer, 0.0f, 0.4f))
                    {
                        pawn.JumpBufferSeconds = jumpBuffer;
                    }

                    var maxJumps = pawn.MaxJumpCount;
                    if (DrawSliderIntProperty("Max jumps", ref maxJumps, 1, 3))
                    {
                        pawn.MaxJumpCount = maxJumps;
                    }
                }

                ImGui.EndTable();
            }

            if (pawn.Body == null)
            {
                if (ImGui.Button("Add Physics Capsule"))
                {
                    pawn.EnablePhysics();
                    pawn.RespawnPoint = pawn.Position;
                }
            }
            else
            {
                if (ImGui.Button("Remove Physics Capsule"))
                {
                    pawn.DisablePhysics();
                }

                ImGui.SameLine();
                if (ImGui.Button("Respawn"))
                {
                    pawn.TeleportTo(pawn.RespawnPoint);
                }
            }

            ImGui.SeparatorText("Foot Sliding");
            if (BeginPropertyTable("PawnStrideProperties"))
            {
                var matchAnimation = pawn.MatchAnimationToSpeed;
                if (DrawCheckboxProperty("Match to speed", ref matchAnimation))
                {
                    pawn.MatchAnimationToSpeed = matchAnimation;
                }

                // The ratio between these two is the playback rate; equal means the clip is running at
                // 1.0x and the feet are as planted as the source animation allows.
                DrawReadOnlyProperty("Walk authored for", pawn.TryGetClipReferenceSpeed(pawn.WalkClipName, out var walkReference)
                    ? $"{walkReference:0.00} u/s"
                    : "<not calibrated>");
                DrawReadOnlyProperty("Run authored for", pawn.TryGetClipReferenceSpeed(pawn.RunClipName, out var runReference)
                    ? $"{runReference:0.00} u/s"
                    : "<not calibrated>");
                DrawReadOnlyProperty("Playback rate", $"{pawn.Animation.PlaybackSpeed:0.00}x");

                ImGui.EndTable();
            }

            if (ImGui.Button("Recalibrate"))
            {
                pawn.CalibrateLocomotionClips();
            }

            ImGui.SameLine();
            if (ImGui.Button("Use Authored Speeds"))
            {
                pawn.CalibrateLocomotionClips();
                pawn.ApplyCalibratedSpeeds();
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                pawn.StopMoving();
            }
        }

        private static void DrawSkeletalActorInspector(ManaSkeletalActorNode actor)
        {
            ImGui.SeparatorText("Skeletal Actor");
            if (BeginPropertyTable("SkeletalActorProperties"))
            {
                DrawReadOnlyProperty("Bones", (actor.Skeleton?.BoneCount ?? 0).ToString());
                DrawReadOnlyProperty("Skinned meshes", actor.SkinnedMeshes.Count.ToString());
                DrawReadOnlyProperty("Clips", actor.Animation.Clips.Count.ToString());

                var skinningEnabled = actor.SkinningEnabled;
                if (DrawCheckboxProperty("Skinning", ref skinningEnabled))
                {
                    actor.SkinningEnabled = skinningEnabled;
                }

                ImGui.EndTable();
            }
        }

        private static void DrawSkeletalAnimationInspector(ManaSkeletalAnimationComponent animation)
        {
            if (!ImGui.CollapsingHeader(GetComponentLabel(animation), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            if (animation.Clips.Count == 0)
            {
                ImGui.TextUnformatted("No animation clips.");
                return;
            }

            if (BeginPropertyTable("SkeletalAnimationProperties"))
            {
                BeginPropertyRow("Clip");
                ImGui.SetNextItemWidth(-1.0f);
                PushCompactValueStyle();
                var activeName = animation.ActiveClip?.Name ?? "<none>";
                if (ImGui.BeginCombo("##Clip", activeName))
                {
                    foreach (var clip in animation.Clips)
                    {
                        if (ImGui.Selectable(clip.Name, ReferenceEquals(clip, animation.ActiveClip)))
                        {
                            animation.Play(clip);
                        }
                    }

                    ImGui.EndCombo();
                }
                PopCompactValueStyle();

                var duration = (float)(animation.ActiveClip?.DurationSeconds ?? 0.0);
                var time = (float)animation.TimeSeconds;
                if (DrawSliderFloatProperty("Time", ref time, 0.0f, MathF.Max(duration, 0.0001f)))
                {
                    animation.TimeSeconds = time;
                    animation.IsPlaying = false;
                }

                var speed = animation.PlaybackSpeed;
                if (DrawSliderFloatProperty("Speed", ref speed, -2.0f, 4.0f))
                {
                    animation.PlaybackSpeed = speed;
                }

                var blendDuration = animation.BlendDurationSeconds;
                if (DrawSliderFloatProperty("Blend", ref blendDuration, 0.0f, 0.6f))
                {
                    animation.BlendDurationSeconds = blendDuration;
                }

                var looping = animation.Looping;
                if (DrawCheckboxProperty("Loop", ref looping))
                {
                    animation.Looping = looping;
                }

                var playing = animation.IsPlaying;
                if (DrawCheckboxProperty("Playing", ref playing))
                {
                    animation.IsPlaying = playing;
                }

                DrawReadOnlyProperty("Duration", $"{duration:0.000} s");
                ImGui.EndTable();
            }
        }

        private static void DrawSkinnedMeshInspector(ManaSkinnedMeshComponent skinnedMesh)
        {
            if (!ImGui.CollapsingHeader(GetComponentLabel(skinnedMesh), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            if (BeginPropertyTable("SkinnedMeshProperties"))
            {
                DrawReadOnlyProperty("Vertices", skinnedMesh.Binding.VertexCount.ToString());
                DrawReadOnlyProperty("Bones", skinnedMesh.Binding.Skeleton.BoneCount.ToString());

                var enabled = skinnedMesh.Enabled;
                if (DrawCheckboxProperty("Enabled", ref enabled))
                {
                    skinnedMesh.Enabled = enabled;
                }

                ImGui.EndTable();
            }
        }

        private static bool DrawColorProperty(string label, ref Vector4 color)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            var changed = ImGuiExt.DrawColorValue(ref color);
            ImGui.PopID();
            return changed;
        }

        private static bool DrawVector3Property(string label, ref Vector3 value)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            var changed = ImGuiExt.DrawVector3Value(ref value);
            ImGui.PopID();
            return changed;
        }

        private static string GetComponentLabel(ManaComponent component)
        {
            var label = GetDisplayName(component, component.GetType().Name);
            if (component is MeshRendererComponent meshRenderer)
            {
                label += $" -> {meshRenderer.Mesh.GetType().Name}";
            }

            return label;
        }

        private static string GetDisplayName(ManaObject manaObject, string fallback)
        {
            return string.IsNullOrWhiteSpace(manaObject.Name) ? fallback : manaObject.Name;
        }
    }
}
