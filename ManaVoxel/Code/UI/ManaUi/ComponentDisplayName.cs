using System.Text;

namespace ManaVoxel.Code.UI.ManaUi;

internal static class ComponentDisplayName
{
    public static string For(Type type)
    {
        var name = type.Name;
        if (name.EndsWith("Component", StringComparison.Ordinal)) name = name[..^9];
        if (name.StartsWith("Mana", StringComparison.Ordinal) && name.Length > 4 && char.IsUpper(name[4]))
            name = name[4..];
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                text.Append(' ');
            text.Append(name[i]);
        }
        return text.Length > 0 ? text.ToString() : type.Name;
    }
}
