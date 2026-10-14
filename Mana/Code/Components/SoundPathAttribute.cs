namespace Mana.Code.Components
{
    /// <summary>
    /// Marks a string property as naming a sound file (a path like <c>Assets/Audio/...wav</c>), so the inspector offers
    /// a sound picker, with a play button, instead of a bare text box.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SoundPathAttribute : Attribute
    {
    }
}
