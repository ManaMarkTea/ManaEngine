using ImGuiNET;
using Mana.Code.Nodes;
using ManaVoxel.Code.Loaders;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    public class VoxelEditorGameMode : BaseGameMode
    {
        private const string ManaWindowTitle = "Mana";
        private uint _manaDockId;
        private uint _utilityDockId;

        TimeSpan[]? fpsData;

        public ManaSceneNode Cursor { get; private set; } = null!;

        public VoxelEditorGameMode()
        {

        }

        ManaVoxelShape? voxel;
        ManaVoxelsNode? voxelNode;
        int initialVoxelCount;
        int bakedPhysicsVoxelCount;
        Vector4 paintVoxelColor = Colors.Magenta;
        Vector3 importPositionOffset = Vector3.Zero;
        Vector3 importRotationDegrees = Vector3.Zero;
        Vector3 importScale = Vector3.One;
        float bakeExplosionStrength = 70.5f;
        float bakeExplosionRadiusPadding = 1.0f;
        float reassembleSettleDistance = 0.06f;
        float reassembleSettleVelocity = 0.18f;
        float reassemblePullGain = 9.5f;
        float reassembleVelocityDamping = 0.86f;
        float reassembleRotationLerp = 0.18f;
        float clickExplosionStrengthScale = 0.2f;
        bool showLockPreview = false;
        bool showImGuiDemoWindow = false;
        bool useLightTheme = true;
        int lockOverlapPolicyIndex = 0;
        readonly string[] lockOverlapPolicyOptions = new[] { "Closest", "Brightest", "First Seen" };

        public override void Load()
        {
            base.Load();

            Context.Window.FileDrop += Window_FileDrop;

            ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

            float gridSize = 256.0f;
            float halfGridSize = gridSize * 0.5f;

            var xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize);
            var yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, gridSize, 0, gridSize);
            var zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, gridSize, 0, gridSize);
            
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, 0.1f, gridSize), new Vector3(halfGridSize, 0, halfGridSize));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, 0.1f), new Vector3(halfGridSize, halfGridSize, 0));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, gridSize, gridSize), new Vector3(0, halfGridSize, halfGridSize));


            Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
            if (Cursor.Components[0] is MeshRendererComponent renderer)
            {
                renderer.Material.Shader = new ManaLiquidShader(Context);
            }


            MagicaVoxLoader vox = new MagicaVoxLoader();
            vox.Load();

            var voxelsNode = shapes.AddVoxels(Vector3.Zero);
            voxelNode = (ManaVoxelsNode)voxelsNode;
            var voxelShapes = voxelsNode.FindShapes<ManaVoxelShape>();
            Debug.Assert(voxelShapes.Count == 1);

            foreach (var voxel in vox.Voxels)
            {
                voxelShapes[0].AddVoxel(voxel.Position + Vector3.One * 0.5f, voxel.Color);
            }

            voxelShapes[0].CommitData();
            voxel = voxelShapes[0];
            initialVoxelCount = voxel.VoxelCount;
        }

        Vector3 startImportPos = new Vector3(30.0f, 0, 20);
        Vector3 lastImportPos = new Vector3(30.0f, 0, 20);
        private void Window_FileDrop(string[] files)
        {
            var mouseDropWorld = GetProjectedMouseHit();
            if (mouseDropWorld.Position.LengthSquared() != 0)
            {
                lastImportPos = mouseDropWorld.Position;
            }

            foreach (var file in files)
            {
                if (Path.GetExtension(file).Equals(".vox", StringComparison.OrdinalIgnoreCase))
                {
                    if (voxelNode != null && voxelNode.IsBakedToDynamicPhysics)
                    {
                        // Ignore voxel imports once baked into dynamic physics mode.
                        continue;
                    }

                    var voxLoader = new MagicaVoxLoader();
                    voxLoader.Load(file);

                    if (voxel == null)
                    {
                        continue;
                    }

                    foreach (var voxelData in voxLoader.Voxels)
                    {
                        var voxelPosition = voxelData.Position + Vector3.One * 0.5f;
                        voxelPosition += lastImportPos + importPositionOffset;
                        voxel.AddVoxel(voxelPosition, voxelData.Color);
                    }

                    voxel.CommitData();
                    lastImportPos.X += 2.0f;
                    continue;
                }

                var fileWorld = Context.World;
                var model = fileWorld.LoadFromGltf(file);
                model.Position = lastImportPos + importPositionOffset;
                model.Rotation = new Vector3(
                    importRotationDegrees.X.ToRadians(),
                    importRotationDegrees.Y.ToRadians(),
                    importRotationDegrees.Z.ToRadians());
                model.Scale = new Vector3(
                    MathF.Max(0.01f, importScale.X),
                    MathF.Max(0.01f, importScale.Y),
                    MathF.Max(0.01f, importScale.Z));
                lastImportPos.X += 2.0f;

                if (lastImportPos.X > startImportPos.X + 30)
                {
                    lastImportPos.X = startImportPos.X;
                    lastImportPos.Z += 2.0f;
                }                
            }
        }

        RayHit GetProjectedMouseHit()
        {
            var MouseState = Context.Input.Mice.First();

            var mouseStart = new Vector3(MouseState.Position, 0.0f);
            var mouseEnd = new Vector3(MouseState.Position, 1.0f);

            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);

            var hitHandler = new SingleHitHandler();
            Context.Physics.Simulation.RayCast(ray.Origin, ray.Direction, 10000.0f, Context.Physics.BufferPool, ref hitHandler, 0);

            RayHit hit = new RayHit();
            if (hitHandler.ClosestHit.HasValue)
            {
                hit = hitHandler.ClosestHit.Value;
            }

            return hit;
        }


        public const float TimestepDuration = 1 / 60f;
        protected override void DrawMainMenuBar()
        {
            bool hasBakedVoxels = voxelNode != null && voxelNode.IsBakedToDynamicPhysics;

            if (!ImGui.BeginMainMenuBar())
            {
                return;
            }

            if (ImGui.BeginMenu("File"))
            {
                bool canBakeToPhysics = voxelNode != null && !hasBakedVoxels;
                if (ImGui.MenuItem("Bake Voxels To Physics", string.Empty, false, canBakeToPhysics) && voxelNode != null)
                {
                    bakedPhysicsVoxelCount = voxelNode.BakeVoxelsToDynamicPhysics();
                }

                if (ImGui.MenuItem("Lock Voxels For Editing", string.Empty, false, hasBakedVoxels) && voxelNode != null)
                {
                    bakedPhysicsVoxelCount = 0;
                    voxelNode.LockBakedVoxelsForEditing();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Show ImGui Demo", string.Empty, showImGuiDemoWindow))
                {
                    showImGuiDemoWindow = !showImGuiDemoWindow;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Light Theme", string.Empty, useLightTheme))
                {
                    useLightTheme = true;
                    ApplyLightImGuiTheme();
                }

                if (ImGui.MenuItem("Dark Theme", string.Empty, !useLightTheme))
                {
                    useLightTheme = false;
                    ApplyDarkImGuiTheme();
                }

                if (ImGui.MenuItem("Emoji Fallback (Experimental)", string.Empty, EmojiFallbackEnabled))
                {
                    SetEmojiFallbackEnabled(!EmojiFallbackEnabled);
                }

                ImGui.Separator();
                if (ImGui.BeginMenu("Font"))
                {
                    var availableFontPresets = GetAvailableImGuiFontPresets();

                    if (ImGui.BeginMenu("Monospace Size"))
                    {
                        if (ImGui.MenuItem("Small", string.Empty, CurrentMonoSizeTier == ImGuiMonoSizeTier.Small))
                        {
                            SetImGuiMonoSizeTier(ImGuiMonoSizeTier.Small);
                        }

                        if (ImGui.MenuItem("Regular", string.Empty, CurrentMonoSizeTier == ImGuiMonoSizeTier.Regular))
                        {
                            SetImGuiMonoSizeTier(ImGuiMonoSizeTier.Regular);
                        }

                        ImGui.EndMenu();
                    }

                    ImGui.Separator();

                    if (availableFontPresets.Contains(ImGuiFontPreset.SegoeUI) && ImGui.MenuItem("Segoe UI", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.SegoeUI))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SegoeUI);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.SegoeSemibold) && ImGui.MenuItem("Segoe UI Semibold", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.SegoeSemibold))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SegoeSemibold);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Bahnschrift) && ImGui.MenuItem("Bahnschrift", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Bahnschrift))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Bahnschrift);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Calibri) && ImGui.MenuItem("Calibri", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Calibri))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Calibri);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Candara) && ImGui.MenuItem("Candara", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Candara))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Candara);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Corbel) && ImGui.MenuItem("Corbel", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Corbel))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Corbel);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Verdana) && ImGui.MenuItem("Verdana", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Verdana))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Verdana);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Tahoma) && ImGui.MenuItem("Tahoma", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Tahoma))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Tahoma);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Trebuchet) && ImGui.MenuItem("Trebuchet", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Trebuchet))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Trebuchet);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.SegoeUIMono) && ImGui.MenuItem("Segoe UI Mono", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.SegoeUIMono))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SegoeUIMono);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.Consolas) && ImGui.MenuItem("Consolas (Mono)", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Consolas))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Consolas);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.CascadiaMono) && ImGui.MenuItem("Cascadia Mono", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.CascadiaMono))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.CascadiaMono);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.JetBrainsMono) && ImGui.MenuItem("JetBrains Mono", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.JetBrainsMono))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.JetBrainsMono);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.FiraCode) && ImGui.MenuItem("Fira Code", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.FiraCode))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.FiraCode);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.SourceCodePro) && ImGui.MenuItem("Source Code Pro", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.SourceCodePro))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SourceCodePro);
                    }

                    if (availableFontPresets.Contains(ImGuiFontPreset.CourierNew) && ImGui.MenuItem("Courier New (Mono)", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.CourierNew))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.CourierNew);
                    }

                    if (ImGui.MenuItem("Default", string.Empty, CurrentImGuiFontPreset == ImGuiFontPreset.Default))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.Default);
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }

        public override void Update(double deltaTime)
        {
            if (_manaDockId != 0)
            {
                ImGui.SetNextWindowDockID(_manaDockId, ImGuiCond.FirstUseEver);
            }

            ImGui.Begin(ManaWindowTitle);

            bool hasBakedVoxels = voxelNode != null && voxelNode.IsBakedToDynamicPhysics;

            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.SameLine();
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");

            var modeText = hasBakedVoxels ? "Baked" : "Edit";
            var reassemblyText = voxelNode != null && voxelNode.IsReassembling ? "Reassembling" : "Idle";
            ImGui.Text($"Mode: {modeText}");
            ImGui.SameLine();
            ImGui.Text($"State: {reassemblyText}");
            ImGui.SameLine();
            ImGui.Text($"Baked Bodies: {bakedPhysicsVoxelCount}");
            ImGui.Separator();

            var MouseState = Context.Input.Mice.First();

            var mouseStart = new Vector3(MouseState.Position, 0.0f);
            var mouseEnd = new Vector3(MouseState.Position, 1.0f);
            
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var rayOrigin = ray.Origin;
            var rayDirection = ray.Direction;

            if (voxelNode != null)
            {
                voxelNode.ExplosionStrength = bakeExplosionStrength;
                voxelNode.ExplosionRadiusPadding = bakeExplosionRadiusPadding;
                voxelNode.ReassemblePullGain = reassemblePullGain;
                voxelNode.ReassembleVelocityDamping = reassembleVelocityDamping;
                voxelNode.ReassembleRotationLerp = reassembleRotationLerp;
                voxelNode.ReassembleSettleDistance = reassembleSettleDistance;
                voxelNode.ReassembleSettleVelocity = reassembleSettleVelocity;

                lockOverlapPolicyIndex = Math.Clamp(lockOverlapPolicyIndex, 0, lockOverlapPolicyOptions.Length - 1);
                voxelNode.OverlapPolicy = (ManaVoxelsNode.LockOverlapPolicy)lockOverlapPolicyIndex;
                voxelNode.SetLockPreviewEnabled(showLockPreview && hasBakedVoxels);
            }

            if (ImGui.BeginTabBar("ManaTabs"))
            {
                if (ImGui.BeginTabItem("Editor"))
                {
                    ImGui.SeparatorText("Session");
                    ImGui.Text($"Voxels: {voxel?.VoxelCount ?? 0}");
                    ImGui.Text($"Created This Session: {Math.Max(0, (voxel?.VoxelCount ?? 0) - initialVoxelCount)}");
                    ImGui.Text($"Pending Voxels: {voxel?.PendingVoxelCount ?? 0}");
                    ImGui.Text($"Duplicate Skips: {voxel?.DuplicateVoxelRejectCount ?? 0}");
                    ImGui.Text($"Physics Baked Voxels: {bakedPhysicsVoxelCount}");

                    ImGui.Spacing();
                    ImGui.SeparatorText("Edit / Import");
                    ImGui.ColorEdit4("Voxel Paint", ref paintVoxelColor);
                    ImGui.InputFloat3("Import Pos Offset", ref importPositionOffset);
                    ImGui.InputFloat3("Import Rot (deg)", ref importRotationDegrees);
                    ImGui.InputFloat3("Import Scale", ref importScale);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Physics"))
                {
                    ImGui.SeparatorText("Tuning");
                    ImGui.SliderFloat("Explosion Strength", ref bakeExplosionStrength, 1.0f, 150.0f);
                    ImGui.SliderFloat("Click Explosion Scale", ref clickExplosionStrengthScale, 0.02f, 1.0f);
                    ImGui.SliderFloat("Explosion Radius Padding", ref bakeExplosionRadiusPadding, 0.0f, 6.0f);
                    ImGui.SliderFloat("Reassemble Pull", ref reassemblePullGain, 0.0f, 30.0f);
                    ImGui.SliderFloat("Reassemble Damping", ref reassembleVelocityDamping, 0.0f, 0.99f);
                    ImGui.SliderFloat("Reassemble Rotation Lerp", ref reassembleRotationLerp, 0.0f, 1.0f);
                    ImGui.SliderFloat("Settle Distance", ref reassembleSettleDistance, 0.01f, 0.5f);
                    ImGui.SliderFloat("Settle Velocity", ref reassembleSettleVelocity, 0.01f, 2.0f);

                    ImGui.Spacing();
                    ImGui.SeparatorText("Bake");

                    bool canBakeToPhysics = voxelNode != null && !voxelNode.IsBakedToDynamicPhysics;
                    if (!canBakeToPhysics)
                    {
                        ImGui.BeginDisabled();
                    }

                    if (ImGui.Button("Bake Voxels To Physics (Fall)", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    {
                        if (voxelNode != null)
                        {
                            bakedPhysicsVoxelCount = voxelNode.BakeVoxelsToDynamicPhysics();
                        }
                    }

                    if (!canBakeToPhysics)
                    {
                        ImGui.EndDisabled();
                    }

                    ImGui.Spacing();
                    ImGui.SeparatorText("Lock / Overlap");

                    if (!hasBakedVoxels)
                    {
                        ImGui.BeginDisabled();
                    }

                    ImGui.Checkbox("Preview Lock Snap", ref showLockPreview);
                    if (ImGui.Combo("Lock Overlap Policy", ref lockOverlapPolicyIndex, lockOverlapPolicyOptions, lockOverlapPolicyOptions.Length) && voxelNode != null)
                    {
                        lockOverlapPolicyIndex = Math.Clamp(lockOverlapPolicyIndex, 0, lockOverlapPolicyOptions.Length - 1);
                        voxelNode.OverlapPolicy = (ManaVoxelsNode.LockOverlapPolicy)lockOverlapPolicyIndex;
                    }
                    if (showLockPreview && voxelNode != null && hasBakedVoxels)
                    {
                        var previewCount = voxelNode.GetLockPreviewVoxelCount();
                        ImGui.Text($"Lock Preview Voxels: {previewCount}");
                        ImGui.Text($"Lock Preview Overlaps: {voxelNode.LockPreviewOverlapCount}");
                    }

                    ImGui.Spacing();
                    ImGui.SeparatorText("Actions");

                    if (ImGui.Button("Reassemble Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    {
                        voxelNode?.BeginReassembleBakedVoxels();
                    }

                    if (ImGui.Button("Lock Voxels For Editing", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    {
                        if (voxelNode != null)
                        {
                            bakedPhysicsVoxelCount = 0;
                            voxelNode.LockBakedVoxelsForEditing();
                        }
                    }

                    if (ImGui.Button("Explode Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    {
                        voxelNode?.ExplodeBakedVoxels();
                    }

                    if (voxelNode != null && voxelNode.IsBakedToDynamicPhysics)
                    {
                        ImGui.Text(voxelNode.IsReassembling ? "Reassembly: in progress" : "Reassembly: idle");
                    }

                    if (!hasBakedVoxels)
                    {
                        ImGui.EndDisabled();
                    }

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Debug"))
                {
                    ImGui.SeparatorText("Timing");
                    if (fpsData == null)
                    {
                        fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
                    }

                    float[] data = ManaTime.GlobalTime.OrderedHistory(ref fpsData).Select((i) => (float)i.TotalMilliseconds).ToArray<float>();
                    ImGui.PlotLines("FPS", ref data[0], data.Length, 0, "fps", 0, 100, new Vector2(150, 90));

                    ImGui.Spacing();
                    ImGui.SeparatorText("Ray");
                    ImGui.InputFloat3("RAY", ref rayOrigin);
                    ImGui.InputFloat3("DIR", ref rayDirection);

                    ImGui.Spacing();
                    ImGui.SeparatorText("Tools");
                    ImGui.Checkbox("Show ImGui Demo Window", ref showImGuiDemoWindow);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            bool imguiCapturesMouse = ImGui.GetIO().WantCaptureMouse;

            if (!imguiCapturesMouse)
            {
                RayHit hit = GetProjectedMouseHit();
                if (hit.Hit)
                {
                    var pos = hit.Position;
                    pos.X = MathF.Floor(pos.X);
                    pos.Y = MathF.Floor(pos.Y);
                    pos.Z = MathF.Floor(pos.Z);

                    pos.X += 0.5f;
                    pos.Y += 0.5f;
                    pos.Z += 0.5f;

                    Cursor.Position = pos;

                    if (MouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left))
                    {
                        if (voxelNode != null && voxelNode.IsBakedToDynamicPhysics)
                        {
                            var clickStrength = MathF.Max(1.0f, bakeExplosionStrength * clickExplosionStrengthScale);
                            voxelNode.ExplodeBakedVoxelsAt(
                                pos,
                                radius: 6.0f,
                                strength: clickStrength,
                                preserveReassembly: voxelNode.IsReassembling);
                        }
                        else
                        {
                            // Place voxels only when the scene has mouse focus, not while dragging ImGui windows.
                            if (voxel != null)
                            {
                                voxel.AddVoxel(pos, paintVoxelColor);
                                voxel.CommitData();
                            }
                        }
                    }
                }
            }

            if (showImGuiDemoWindow)
            {
                if (_utilityDockId != 0)
                {
                    ImGui.SetNextWindowDockID(_utilityDockId, ImGuiCond.Always);
                }

                ImGui.ShowDemoWindow(ref showImGuiDemoWindow);
            }
            
            ImGui.End();
        }

        protected override void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
            ImGuiDockNative.DockBuilderSetNodeSize(dockspaceId, dockspaceSize);

            uint centerDockId;
            uint rightDockId;
            _ = ImGuiDockNative.DockBuilderSplitNode(
                dockspaceId,
                ImGuiDir.Right,
                0.28f,
                out rightDockId,
                out centerDockId);

            _ = ImGuiDockNative.DockBuilderSplitNode(
                rightDockId,
                ImGuiDir.Down,
                0.45f,
                out _utilityDockId,
                out _manaDockId);

            ImGuiDockNative.DockBuilderDockWindow(ManaWindowTitle, _manaDockId);
            ImGuiDockNative.DockBuilderDockWindow("Camera", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("World Perf", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Irradiance", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Dear ImGui Demo", _utilityDockId);
            BaseGameMode.UtilityDockId = _utilityDockId;
            ImGuiDockNative.DockBuilderFinish(dockspaceId);
        }

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
        }

    }
}
