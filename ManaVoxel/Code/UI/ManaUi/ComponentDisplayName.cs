using System.Text;

namespace ManaVoxel.Code.UI.ManaUi;

internal static class ComponentDisplayName
{
    public static string For(Type type)
    {
        // A name the type gives itself wins -- "Sprite 9-Slice" is not something capitals can spell.
        if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>(type) is { DisplayName.Length: > 0 } named)
        {
            return named.DisplayName;
        }
        var name = type.Name;
        if (name.EndsWith("Component", StringComparison.Ordinal)) name = name[..^9];
        if (name.StartsWith("Mana", StringComparison.Ordinal) && name.Length > 4 && char.IsUpper(name[4]))
            name = name[4..];
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            // "Platformer2D" reads "Platformer 2D": a number starts a word, and the letter after it stays on.
            if (i > 0)
            {
                var previous = name[i - 1];
                var startsWord = char.IsUpper(name[i])
                    ? !char.IsDigit(previous) && (!char.IsUpper(previous) || (i + 1 < name.Length && char.IsLower(name[i + 1])))
                    : char.IsDigit(name[i]) && char.IsLetter(previous);
                if (startsWord)
                {
                    text.Append(' ');
                }
            }
            text.Append(name[i]);
        }
        return text.Length > 0 ? text.ToString() : type.Name;
    }
}
