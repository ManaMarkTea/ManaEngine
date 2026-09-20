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
                    Type = ManaAudioSourceComponent.ScriptType,
                    ComponentType = typeof(ManaAudioSourceComponent),
                    Create = node => new ManaAudioSourceComponent(node),
                    AllowMultiple = true,
                    AfterPropertiesApplied = component => { _ = ((ManaAudioSourceComponent)component).PrepareAsync(); }
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
                    Type = definition.Type,
                    Properties = ManaLevelComponentPropertyBag.Capture(component, definition.ComponentType)
                });
            }

            return scripts;
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaAudioSourceComponent))]
        public static bool AddToNode(ManaSceneNode node, ManaLevelScript script)
        {
            var definition = Definitions.FirstOrDefault(definition =>
                string.Equals(definition.Type, script.Type, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                return false;
            }

            var component = definition.AllowMultiple ? null : node.Components.FirstOrDefault(definition.ComponentType.IsInstanceOfType);
            if (component == null)
            {
                component = definition.Create(node);
                node.Components.Add(component);
            }

            ManaLevelComponentPropertyBag.Apply(component, definition.ComponentType, script.Properties);
            definition.AfterPropertiesApplied?.Invoke(component);
            return true;
        }
    }

    internal static class ManaLevelComponentPropertyBag
    {
        public static Dictionary<string, JsonElement> Capture(ManaComponent component, Type componentType)
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
                properties[JsonName(property)] = document.RootElement.Clone();
            }

            return properties;
        }

        public static void Apply(
            ManaComponent component,
            Type componentType,
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

        private static IEnumerable<PropertyInfo> SerializableProperties(Type componentType)
        {
            return componentType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.CanRead
                    && property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && property.DeclaringType != typeof(ManaObject)
                    && property.DeclaringType != typeof(ManaComponent)
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
