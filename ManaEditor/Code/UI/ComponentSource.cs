using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ManaGraphics.Code.Diagnostics;

namespace ManaEditor.Code.UI;

/// <summary>Locates a component's own source using the symbols shipped with its assembly.</summary>
public static class ComponentSource
{
    public sealed record Location(string Path, int Line)
    {
        // File URIs expose a Windows drive path without a leading slash. Keep the
        // separator explicit or the drive becomes part of the URI's host (fileC:).
        public string EditorUri => "vscode://file/" + new Uri(Path).AbsolutePath.TrimStart('/') + $":{Line}";
    }

    public static Location? Find(Type type)
    {
        if (OperatingSystem.IsBrowser() || type.Assembly.IsDynamic)
        {
            return null;
        }

        try
        {
            var assemblyPath = type.Assembly.Location;
            if (!File.Exists(assemblyPath)) return null;

            using var assembly = File.OpenRead(assemblyPath);
            using var pe = new PEReader(assembly);
            if (!pe.TryOpenAssociatedPortablePdb(assemblyPath, File.OpenRead, out var provider, out _)
                || provider == null)
            {
                return null;
            }

            using (provider)
            {
                var symbols = provider.GetMetadataReader();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                // Constructors usually lead closest to the declaration. Never resolve inherited methods:
                // that would open a base component instead of the one the user clicked.
                var methods = type.GetConstructors(flags).Cast<MethodBase>()
                    .Concat(type.GetMethods(flags));
                foreach (var method in methods)
                {
                    var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff);
                    var debug = symbols.GetMethodDebugInformation(handle);
                    foreach (var point in debug.GetSequencePoints())
                    {
                        if (point.IsHidden) continue;
                        var document = point.Document.IsNil ? debug.Document : point.Document;
                        if (document.IsNil) continue;
                        var path = symbols.GetString(symbols.GetDocument(document).Name);
                        if (File.Exists(path)) return new Location(path, point.StartLine);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            ManaConsole.WriteDiagnostic($"Could not find source for {type.Name}: {ex.Message}");
        }

        return null;
    }

    public static bool Open(Location location)
    {
        try
        {
            if (OperatingSystem.IsBrowser() || !File.Exists(location.Path)) return false;
            using var process = Process.Start(new ProcessStartInfo(location.EditorUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            ManaConsole.WriteDiagnostic($"Could not open component source '{location.Path}': {ex.Message}");
            return false;
        }
    }
}
