namespace Mana.Code.Components
{
    /// <summary>
    /// Marks a string (or string[]) property as naming pictures -- a repo path, or a "zip:" path into an asset
    /// pack -- so the inspector offers a picture picker instead of a bare text box.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PicturePathAttribute : Attribute
    {
    }
}
