using ImGuiNET;
using Mana.Code.Materials;
using Mana.Code.ManaShaders;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Silk.NET.OpenGL;

namespace Mana.Code.UI
{
    public sealed class SceneInspectorPanelView
    {
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
        };

        private readonly SceneSelectionState selectionState;

        public SceneInspectorPanelView(SceneSelectionState selectionState)
        {
            this.selectionState = selectionState;
        }

        public void Draw()
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Inspector");
            DrawSelectionInspector();
            ImGui.End();
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
        }

        private void DrawSelectedNodeInspector(ManaSceneNode node)
        {
            var nodeName = node.Name;
            if (ImGui.InputText("Name", ref nodeName, 160))
            {
                node.Name = nodeName;
            }

            ImGui.TextUnformatted($"Type: {node.GetType().Name}");
            DrawTransformEditor(node);

            if (node.Components.Count == 0)
            {
                return;
            }

            ImGui.SeparatorText("Components");
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

            if (ImGui.InputFloat3("Position", ref position))
            {
                node.Position = position;
                changed = true;
            }

            if (ImGui.InputFloat3("Rotation", ref rotationDegrees))
            {
                node.Rotation = new Vector3(
                    rotationDegrees.X.ToRadians(),
                    rotationDegrees.Y.ToRadians(),
                    rotationDegrees.Z.ToRadians());
                changed = true;
            }

            if (ImGui.InputFloat3("Scale", ref scale))
            {
                node.Scale = scale;
                changed = true;
            }

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

            ImGui.TextUnformatted(GetComponentLabel(component));
        }

        private void DrawMeshRendererInspector(MeshRendererComponent meshRenderer)
        {
            if (!ImGui.TreeNodeEx(GetComponentLabel(meshRenderer), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            ImGui.TextUnformatted($"Mesh: {meshRenderer.Mesh.GetType().Name}");
            DrawPrimitiveMeshInspector(meshRenderer.Mesh);
            DrawMaterialInspector(meshRenderer.Material);
            ImGui.TreePop();
        }

        private void DrawPrimitiveMeshInspector(ManaMesh mesh)
        {
            if (mesh is not ManaProceduralMesh proceduralMesh)
            {
                return;
            }

            if (!ImGui.TreeNodeEx("Primitive", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var changed = false;
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

            if (changed)
            {
                proceduralMesh.RebuildGeometry();
            }

            ImGui.TreePop();
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

            if (ImGui.SliderInt("Major Segments", ref majorSegments, 3, 192))
            {
                torus.MajorSegments = Math.Clamp(majorSegments, 3, 192);
                changed = true;
            }

            if (ImGui.SliderInt("Minor Segments", ref minorSegments, 3, 96))
            {
                torus.MinorSegments = Math.Clamp(minorSegments, 3, 96);
                changed = true;
            }

            ImGui.TextUnformatted($"Major Radius: {torus.MajorRadius:0.###}");
            ImGui.TextUnformatted($"Minor Radius: {torus.MinorRadius:0.###}");
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

            if (ImGui.SliderInt("Latitude Segments", ref latitudeSegments, 3, 192))
            {
                sphere.LatitudeSegments = Math.Clamp(latitudeSegments, 3, 192);
                changed = true;
            }

            if (ImGui.SliderInt("Longitude Segments", ref longitudeSegments, 3, 192))
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

            if (ImGui.SliderInt("Subdivisions", ref recursionLevel, 0, 5))
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
            if (!ImGui.InputFloat3(label, ref edited))
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
            if (!ImGui.InputFloat(label, ref edited))
            {
                return false;
            }

            value = MathF.Max(min, edited);
            return true;
        }

        private void DrawMaterialInspector(ManaMaterial material)
        {
            if (!ImGui.TreeNodeEx("Material", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var materialName = material.Name;
            if (ImGui.InputText("Material Name", ref materialName, 160))
            {
                material.Name = materialName;
            }

            ImGui.TextUnformatted($"Shader: {material.Shader.GetType().Name}");
            DrawColorEditor("Albedo", material.Albedo);
            DrawColorEditor("Emission", material.Emission);
            DrawScalarEditor("Metallic", material.Metallic, 0.0f, 1.0f);
            DrawScalarEditor("Roughness", material.Roughness, 0.0f, 1.0f);
            DrawScalarEditor("Ambient", material.Ambient, 0.0f, 1.0f);

            var normalSelector = (float)material.NormalSelector;
            if (ImGui.SliderFloat("Normal Texture Mix", ref normalSelector, 0.0f, 1.0f))
            {
                material.NormalSelector = normalSelector;
            }

            var emissionStrength = (float)material.EmissionStrength;
            if (ImGui.SliderFloat("Emission Strength", ref emissionStrength, 0.0f, 25.0f))
            {
                material.EmissionStrength = emissionStrength;
            }

            DrawShaderUniformInspector(material);

            ImGui.TreePop();
        }

        private static void DrawShaderUniformInspector(ManaMaterial material)
        {
            var uniforms = material.Shader.CompiledShader?.UniformNames;
            if (uniforms == null || uniforms.Count == 0)
            {
                return;
            }

            if (!ImGui.TreeNodeEx($"Shader Uniforms ({uniforms.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var sortedUniforms = uniforms.OrderBy(uniform => uniform.name).ToList();
            var editableUniforms = sortedUniforms
                .Where(uniform => !IsAutomaticUniform(uniform) && CanEditUniform(uniform.type))
                .ToList();
            var automaticUniforms = sortedUniforms
                .Where(IsAutomaticUniform)
                .ToList();
            var unsupportedUniforms = sortedUniforms
                .Where(uniform => !IsAutomaticUniform(uniform) && !CanEditUniform(uniform.type))
                .ToList();

            foreach (var uniform in editableUniforms)
            {
                ImGui.PushID($"uniform_{uniform.name}");
                DrawEditableUniform(material, uniform);
                ImGui.PopID();
            }

            if (editableUniforms.Count == 0)
            {
                ImGui.TextDisabled("No editable material uniforms");
            }

            if (automaticUniforms.Count > 0 && ImGui.TreeNode($"Automatic / Engine ({automaticUniforms.Count})"))
            {
                foreach (var uniform in automaticUniforms)
                {
                    ImGui.BulletText($"{uniform.name}: {uniform.type}");
                }

                ImGui.TreePop();
            }

            if (unsupportedUniforms.Count > 0 && ImGui.TreeNode($"Unsupported ({unsupportedUniforms.Count})"))
            {
                foreach (var uniform in unsupportedUniforms)
                {
                    ImGui.BulletText($"{uniform.name}: {uniform.type}");
                }

                ImGui.TreePop();
            }

            if (material.ShaderUniforms.Count > 0)
            {
                ImGui.Spacing();
                if (ImGui.SmallButton("Clear Uniform Overrides"))
                {
                    material.ShaderUniforms.Clear();
                }
            }

            ImGui.TreePop();
        }

        private static void DrawEditableUniform(ManaMaterial material, ManaShaderUniform uniform)
        {
            var hasOverride = material.ShaderUniforms.TryGetValue(uniform.name, out var value);
            value ??= CreateDefaultUniformValue(uniform.type);
            var changed = false;

            switch (uniform.type)
            {
                case UniformType.Float:
                    var floatValue = value.FloatValue;
                    changed = ImGui.DragFloat(uniform.name, ref floatValue, 0.01f);
                    value.FloatValue = floatValue;
                    break;
                case UniformType.FloatVec2:
                    var vector2Value = value.Vector2Value;
                    changed = ImGui.InputFloat2(uniform.name, ref vector2Value);
                    value.Vector2Value = vector2Value;
                    break;
                case UniformType.FloatVec3:
                    var vector3Value = value.Vector3Value;
                    changed = ImGui.InputFloat3(uniform.name, ref vector3Value);
                    value.Vector3Value = vector3Value;
                    break;
                case UniformType.FloatVec4:
                    var vector4Value = value.Vector4Value;
                    changed = ImGui.InputFloat4(uniform.name, ref vector4Value);
                    value.Vector4Value = vector4Value;
                    break;
                case UniformType.Int:
                    var intValue = value.IntValue;
                    changed = ImGui.InputInt(uniform.name, ref intValue);
                    value.IntValue = intValue;
                    break;
                case UniformType.Bool:
                    var boolValue = value.BoolValue;
                    changed = ImGui.Checkbox(uniform.name, ref boolValue);
                    value.BoolValue = boolValue;
                    break;
            }

            if (changed)
            {
                value.Type = uniform.type;
                material.ShaderUniforms[uniform.name] = value;
            }

            if (hasOverride || material.ShaderUniforms.ContainsKey(uniform.name))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset"))
                {
                    material.ShaderUniforms.Remove(uniform.name);
                }
            }
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
            return AutomaticUniformNames.Contains(GetUniformBaseName(uniform.name));
        }

        private static string GetUniformBaseName(string uniformName)
        {
            var bracketIndex = uniformName.IndexOf('[');
            return bracketIndex < 0 ? uniformName : uniformName[..bracketIndex];
        }

        private static void DrawColorEditor(string label, ManaTextureVector4 value)
        {
            var color = value.Value;
            if (ImGui.ColorEdit4(label, ref color))
            {
                value.Value = color;
            }
        }

        private static void DrawScalarEditor(string label, ManaTextureSingle value, float min, float max)
        {
            var scalar = value.Value;
            if (ImGui.SliderFloat(label, ref scalar, min, max))
            {
                value.Value = scalar;
            }
        }

        private void DrawLightSourceInspector(ManaLightSourceComponent light)
        {
            if (!ImGui.TreeNodeEx(GetComponentLabel(light), ImGuiTreeNodeFlags.DefaultOpen))
            {
                return;
            }

            var enabled = light.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                light.Enabled = enabled;
            }

            var intensity = light.Intensity;
            if (ImGui.SliderFloat("Intensity", ref intensity, 0.0f, 8.0f))
            {
                light.Intensity = intensity;
            }

            var color = light.Color;
            if (ImGui.ColorEdit4("Color", ref color))
            {
                light.Color = color;
            }

            var direction = light.Direction;
            if (ImGui.InputFloat3("Direction", ref direction))
            {
                light.Direction = direction;
            }

            ImGui.TreePop();
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
