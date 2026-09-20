namespace Mana;

/// <summary>Optional creation policy. Public concrete components with a public (ManaSceneNode)
/// constructor are discovered automatically without this attribute.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class EditorComponentAttribute : Attribute
{
    public bool AllowMultiple { get; set; }
    public bool CanAdd { get; set; } = true;
}
