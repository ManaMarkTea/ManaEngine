using Mana.Code.Nodes;

namespace Mana;

/// <summary>
/// A pointer from one component to another component on the same node -- "the trigger I listen to", "the collider I
/// measure" -- that can be picked in the inspector and saved in a level.
/// <para>
/// It is a small value rather than a live object reference so it survives everything a live one would not: saving and
/// loading, copying a node, a component being replaced. What it stores is the target's type name and, when the node
/// has several of that type, which one (<c>TriggerVolumeComponent</c>, then <c>TriggerVolumeComponent#1</c>,
/// <c>TriggerVolumeComponent#2</c>, in the order they sit on the node). A component that has been given a
/// <see cref="ManaComponent.Label"/> is pointed at by that instead (<c>@Front Door</c>), which stays right when the
/// components are reordered. It is turned back into the component with
/// <see cref="Resolve{T}"/> or <see cref="ManaSceneNode.GetComponent{T}(ManaComponentReference)"/>, which return null
/// when the target is gone, so holding a stale one is harmless.
/// </para>
/// <para>
/// Declare a property of this type on a component and it appears in the inspector as a picker of the node's
/// components; put <see cref="ComponentReferenceAttribute"/> on it to list only the kind that makes sense.
/// </para>
/// </summary>
public readonly record struct ManaComponentReference(string? Key)
{
    /// <summary>Points at nothing.</summary>
    public static ManaComponentReference None => default;

    // A leading '@' means "the component with this label"; a component type's name cannot start with one.
    private const string LabelPrefix = "@";

    public bool IsSet => !string.IsNullOrEmpty(Key);

    /// <summary>A reference to <paramref name="target"/> -- or to nothing, when it is null.</summary>
    public static ManaComponentReference To(ManaComponent? target)
    {
        if (target == null)
        {
            return None;
        }

        if (!string.IsNullOrWhiteSpace(target.Label))
        {
            return new ManaComponentReference(LabelPrefix + target.Label);
        }

        var type = target.GetType();
        var index = 0;
        foreach (var sibling in target.Owner.Components)
        {
            if (ReferenceEquals(sibling, target))
            {
                break;
            }

            if (sibling.GetType() == type)
            {
                index++;
            }
        }

        return new ManaComponentReference(index == 0 ? type.Name : $"{type.Name}#{index}");
    }

    /// <summary>
    /// The component on <paramref name="owner"/> this points at, or null when it is unset, no longer there, or not
    /// of type <typeparamref name="T"/>.
    /// </summary>
    public T? Resolve<T>(ManaSceneNode owner) where T : class
    {
        if (!IsSet)
        {
            return null;
        }

        if (Key!.StartsWith(LabelPrefix, StringComparison.Ordinal))
        {
            var label = Key[LabelPrefix.Length..];
            foreach (var component in owner.Components)
            {
                if (string.Equals(component.Label, label, StringComparison.Ordinal))
                {
                    return component as T;
                }
            }

            return null;
        }

        if (!TryParse(Key, out var typeName, out var wanted))
        {
            return null;
        }

        var seen = 0;
        foreach (var component in owner.Components)
        {
            if (component.GetType().Name != typeName)
            {
                continue;
            }

            if (seen++ == wanted)
            {
                return component as T;
            }
        }

        return null;
    }

    /// <summary>
    /// Gives <paramref name="target"/> a new label (or clears it, with null) and re-points every reference to it on
    /// its node, so renaming a component does not silently cut the links that used its old name -- or its position,
    /// which a first label replaces. Only references that pointed at it are touched.
    /// </summary>
    public static void Rename(ManaComponent target, string? label)
    {
        var owner = target.Owner;
        var pointing = new List<(ManaComponent Holder, System.Reflection.PropertyInfo Property)>();

        foreach (var holder in owner.Components)
        {
            foreach (var property in holder.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(ManaComponentReference)
                    && property.CanRead
                    && property.CanWrite
                    && property.GetValue(holder) is ManaComponentReference reference
                    && ReferenceEquals(reference.Resolve<ManaComponent>(owner), target))
                {
                    pointing.Add((holder, property));
                }
            }
        }

        target.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

        foreach (var (holder, property) in pointing)
        {
            property.SetValue(holder, To(target));
        }
    }

    public override string ToString() => IsSet ? Key! : "(none)";

    // "Name" is the first of its type; "Name#2" is the third (the number counts the ones before it).
    private static bool TryParse(string key, out string typeName, out int index)
    {
        var hash = key.LastIndexOf('#');
        if (hash < 0)
        {
            typeName = key;
            index = 0;
            return true;
        }

        typeName = key[..hash];
        return int.TryParse(key.AsSpan(hash + 1), out index) && index >= 0;
    }
}

/// <summary>
/// On a <see cref="ManaComponentReference"/> property: which kind of component it may point at, so the inspector's
/// picker lists the ones that fit rather than everything on the node.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ComponentReferenceAttribute : Attribute
{
    public ComponentReferenceAttribute(Type requiredType)
    {
        RequiredType = requiredType;
    }

    public Type RequiredType { get; }
}
