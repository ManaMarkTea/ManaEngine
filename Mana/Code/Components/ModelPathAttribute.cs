namespace Mana.Code.Components
{
    /// <summary>
    /// Marks a string (or string[]) property as naming models -- repo paths or "zip:" paths into a pack -- so the
    /// inspector offers a model picker instead of a bare text box.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ModelPathAttribute : Attribute
    {
    }
}
