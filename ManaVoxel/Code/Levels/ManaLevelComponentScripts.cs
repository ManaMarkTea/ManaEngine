using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Components;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.Gameplay.Trains;

namespace ManaVoxel.Code.Levels
{
    internal sealed class ManaLevelScriptComponentDefinition
    {
        public required string Type { get; init; }

        // Kept by the trimmer: their public properties are what a level saves.
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        public required Type ComponentType { get; init; }

        public required Func<ManaSceneNode, ManaComponent> Create { get; init; }
        public bool AllowMultiple { get; init; }

        public Action<ManaComponent>? AfterPropertiesApplied { get; init; }
    }

    internal static class ManaLevelComponentScripts
    {
        private static readonly IReadOnlyList<ManaLevelScriptComponentDefinition> Definitions =
            new List<ManaLevelScriptComponentDefinition>
            {
                new()
                {
                    Type = "BoxCollider",
                    ComponentType = typeof(ManaBoxColliderComponent),
                    Create = node => new ManaBoxColliderComponent(node),
                    AllowMultiple = true
                },
                new()
                {
                    Type = ManaAudioSourceComponent.ScriptType,
                    ComponentType = typeof(ManaAudioSourceComponent),
                    Create = node => new ManaAudioSourceComponent(node),
                    AllowMultiple = true,
                    AfterPropertiesApplied = component => { _ = ((ManaAudioSourceComponent)component).PrepareAsync(); }
                },
                // A light on a node -- a lamp's bulb, a torch -- so it survives a save, and a prefab can carry one.
                new()
                {
                    Type = "Sprite",
                    ComponentType = typeof(Mana.Code.Components.ManaSpriteComponent),
                    Create = node => new Mana.Code.Components.ManaSpriteComponent(node)
                },
                new()
                {
                    Type = "SpriteSheet",
                    ComponentType = typeof(Mana.Code.Components.ManaSpriteSheetComponent),
                    Create = node => new Mana.Code.Components.ManaSpriteSheetComponent(node)
                },
                new()
                {
                    Type = "Sprite9Slice",
                    ComponentType = typeof(Mana.Code.Components.ManaSprite9SliceComponent),
                    Create = node => new Mana.Code.Components.ManaSprite9SliceComponent(node)
                },
                new()
                {
                    Type = "PolygonCollider",
                    ComponentType = typeof(Mana.Code.Components.ManaPolygonColliderComponent),
                    Create = node => new Mana.Code.Components.ManaPolygonColliderComponent(node)
                },
                new()
                {
                    Type = "Parallax",
                    ComponentType = typeof(Mana.Code.Components.ManaParallaxComponent),
                    Create = node => new Mana.Code.Components.ManaParallaxComponent(node)
                },
                new()
                {
                    Type = "Platformer2D",
                    ComponentType = typeof(Mana.Code.Components.ManaPlatformer2DComponent),
                    Create = node => new Mana.Code.Components.ManaPlatformer2DComponent(node)
                },
                new()
                {
                    Type = "Glow",
                    ComponentType = typeof(Mana.Code.Components.ManaGlowComponent),
                    Create = node => new Mana.Code.Components.ManaGlowComponent(node)
                },
                new()
                {
                    Type = "ParticleEmitter",
                    ComponentType = typeof(Mana.Code.Components.ManaParticleEmitterComponent),
                    Create = node => new Mana.Code.Components.ManaParticleEmitterComponent(node)
                },
                new()
                {
                    Type = "GpuParticleEmitter",
                    ComponentType = typeof(ManaGpuParticleEmitterComponent),
                    Create = node => new ManaGpuParticleEmitterComponent(node)
                },
                // Animators: a rainbow through the lights and glows beneath, and motion laid over a node's placement.
                new()
                {
                    Type = "ColorCycle",
                    ComponentType = typeof(Mana.Code.Components.ManaColorCycleComponent),
                    Create = node => new Mana.Code.Components.ManaColorCycleComponent(node)
                },
                new()
                {
                    Type = "Cloth",
                    ComponentType = typeof(Mana.Code.Components.ManaClothComponent),
                    Create = node => new Mana.Code.Components.ManaClothComponent(node)
                },
                new()
                {
                    Type = "Motion",
                    ComponentType = typeof(Mana.Code.Components.ManaMotionComponent),
                    Create = node => new Mana.Code.Components.ManaMotionComponent(node)
                },
                new()
                {
                    Type = "ProbeVolume",
                    ComponentType = typeof(Mana.Code.Components.ManaProbeVolumeComponent),
                    Create = node => new Mana.Code.Components.ManaProbeVolumeComponent(node)
                },
                new()
                {
                    Type = "Light",
                    ComponentType = typeof(ManaLightSourceComponent),
                    Create = node => new ManaLightSourceComponent(node),
                    AllowMultiple = true
                },
                new()
                {
                    Type = TriggerVolumeComponent.ScriptType,
                    ComponentType = typeof(TriggerVolumeComponent),
                    Create = node => new TriggerVolumeComponent(node)
                },
                new()
                {
                    Type = CollectibleComponent.ScriptType,
                    ComponentType = typeof(CollectibleComponent),
                    Create = node => new CollectibleComponent(node)
                },
                new()
                {
                    Type = CheckpointComponent.ScriptType,
                    ComponentType = typeof(CheckpointComponent),
                    Create = node => new CheckpointComponent(node)
                },
                new()
                {
                    Type = HazardComponent.ScriptType,
                    ComponentType = typeof(HazardComponent),
                    Create = node => new HazardComponent(node)
                },
                new()
                {
                    Type = GoalComponent.ScriptType,
                    ComponentType = typeof(GoalComponent),
                    Create = node => new GoalComponent(node)
                },
                new()
                {
                    Type = EnemyComponent.ScriptType,
                    ComponentType = typeof(EnemyComponent),
                    Create = node => new EnemyComponent(node)
                },
                new()
                {
                    Type = BowComponent.ScriptType,
                    ComponentType = typeof(BowComponent),
                    Create = node => new BowComponent(node)
                },
                new()
                {
                    Type = SpringPadComponent.ScriptType,
                    ComponentType = typeof(SpringPadComponent),
                    Create = node => new SpringPadComponent(node)
                },
                new()
                {
                    Type = BoostPadComponent.ScriptType,
                    ComponentType = typeof(BoostPadComponent),
                    Create = node => new BoostPadComponent(node)
                },
                new()
                {
                    Type = MovingPlatformComponent.ScriptType,
                    ComponentType = typeof(MovingPlatformComponent),
                    Create = node => new MovingPlatformComponent(node)
                },
                new()
                {
                    Type = TrainCarComponent.ScriptType,
                    ComponentType = typeof(TrainCarComponent),
                    Create = node => new TrainCarComponent(node),
                    AfterPropertiesApplied = component => ((TrainCarComponent)component).ResetAfterLevelPropertiesApplied()
                }
            };

        private static ManaComponent? NthOfType(ManaSceneNode node, Type type, int index)
        {
            foreach (var component in node.Components)
            {
                if (type.IsInstanceOfType(component) && index-- == 0)
                {
                    return component;
                }
            }

            return null;
        }

        public static List<ManaLevelScript>? Capture(ManaSceneNode node)
        {
            List<ManaLevelScript>? scripts = null;
            foreach (var component in node.Components)
            {
                var definition = Definitions.FirstOrDefault(definition =>
                    definition.ComponentType.IsInstanceOfType(component));
                if (definition == null)
                {
                    continue;
                }

                scripts ??= new List<ManaLevelScript>();
                scripts.Add(new ManaLevelScript
                {
                    Id = component.AuthoredId,
                    Type = definition.Type,
                    Name = string.IsNullOrWhiteSpace(component.Label) ? null : component.Label,
                    Properties = ManaLevelComponentPropertyBag.Capture(component, definition.ComponentType)
                });
            }

            return scripts;
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaAudioSourceComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaBoxColliderComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaLightSourceComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaGlowComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaParticleEmitterComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaGpuParticleEmitterComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaPlatformer2DComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(BowComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaParallaxComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaSpriteComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaSpriteSheetComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaSprite9SliceComponent))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Mana.Code.Components.ManaPolygonColliderComponent))]
        /// <param name="occurrence">
        /// Which of its kind this script is on the node, counting from zero. For a type a node may only have one of,
        /// it is ignored in practice; for a type that can repeat without <c>AllowMultiple</c> (trigger volumes), it
        /// picks which existing component receives the properties, so several round-trip in order.
        /// </param>
        public static bool AddToNode(ManaSceneNode node, ManaLevelScript script, int occurrence = 0)
        {
            var definition = Definitions.FirstOrDefault(definition =>
                string.Equals(definition.Type, script.Type, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                return false;
            }

            // Under the scene lock: levels load on a background thread, and the render thread walks every node's
            // component list under that lock. The node is already in the scene by now (a group's children are
            // attached as they load), so an unguarded Add races a walk and throws "collection was modified".
            ManaComponent? component;
            lock (node.Context.World.SceneLock)
            {
                component = definition.AllowMultiple ? null : NthOfType(node, definition.ComponentType, occurrence);
                if (component == null)
                {
                    component = definition.Create(node);
                    node.Components.Add(component);
                }
            }

            if (!string.IsNullOrWhiteSpace(script.Name))
            {
                component.Label = script.Name;
            }

            ManaLevelComponentPropertyBag.Apply(component, definition.ComponentType, script.Properties);
            if (!string.IsNullOrWhiteSpace(script.Id))
            {
                component.AuthoredId = script.Id;
            }

            // Levels saved before volumes had a component of their own kept size and offset inside each piece's script.
            if (component is ManaTriggerListenerComponent listener)
            {
                listener.ApplyLegacyVolume(script.Properties, ManaLevel.SerializerOptions);
            }

            definition.AfterPropertiesApplied?.Invoke(component);
            return true;
        }
    }

    internal static class ManaLevelComponentPropertyBag
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Property types are kept by the annotation on componentType; the web build turns reflection JSON on.")]
        public static Dictionary<string, JsonElement> Capture(
            ManaComponent component,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type componentType)
        {
            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            foreach (var property in SerializableProperties(componentType))
            {
                var value = property.GetValue(component);
                if (value == null)
                {
                    continue;
                }

                using var document = JsonSerializer.SerializeToDocument(
                    value,
                    property.PropertyType,
                    ManaLevel.SerializerOptions);

                // A value that serialises to null (a reference to nothing) is the default, not a setting.
                if (document.RootElement.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                properties[JsonName(property)] = document.RootElement.Clone();
            }

            return properties;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Property types are kept by the annotation on componentType; the web build turns reflection JSON on.")]
        public static void Apply(
            ManaComponent component,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type componentType,
            IReadOnlyDictionary<string, JsonElement> properties)
        {
            foreach (var property in SerializableProperties(componentType))
            {
                if (!TryGet(properties, property, out var value))
                {
                    continue;
                }

                var next = value.Deserialize(property.PropertyType, ManaLevel.SerializerOptions);
                property.SetValue(component, next);
            }
        }

        private static bool TryGet(
            IReadOnlyDictionary<string, JsonElement> properties,
            PropertyInfo property,
            out JsonElement value)
        {
            if (properties.TryGetValue(JsonName(property), out value)
                || properties.TryGetValue(property.Name, out value))
            {
                return true;
            }

            foreach (var pair in properties)
            {
                if (string.Equals(pair.Key, JsonName(property), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Key, property.Name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static IEnumerable<PropertyInfo> SerializableProperties(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type componentType)
        {
            return componentType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.CanRead
                    && property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && property.DeclaringType != typeof(ManaObject)
                    && property.DeclaringType != typeof(ManaComponent)
                    && property.DeclaringType != typeof(ManaCollisionComponent)
                    && !IsIgnored(property));
        }

        private static bool IsIgnored(PropertyInfo property)
        {
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
            return ignore != null && ignore.Condition != JsonIgnoreCondition.Never;
        }

        private static string JsonName(PropertyInfo property)
        {
            var explicitName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }

            return ManaLevel.SerializerOptions.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name;
        }
    }
}
