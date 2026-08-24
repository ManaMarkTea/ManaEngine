// Mirrors the subset of ManaVoxel/Program.cs's project-wide `global using` directives that the
// source files linked in from ManaVoxel (see ManaVoxel.Web.csproj) rely on. Those files are
// compiled here without ManaVoxel's Program.cs, so without this they lose usings they have always
// silently depended on -- exactly the trap documented in WebPort-Plan.md for Mana.csproj, where
// excluding Program.cs quietly deleted the global usings and produced a long tail of
// "type not found" errors that looked like missing references.
//
// Deliberately only the browser-safe entries: ManaVoxel's list also globals in editor/loader
// namespaces that have no browser-wasm build.
global using System.Numerics;
global using Mana.Code;
global using Mana.Code.Shapes;
global using ManaCore.Graphics;
global using ManaGraphics.Code.Diagnostics;
