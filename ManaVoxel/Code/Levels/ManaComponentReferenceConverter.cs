using System.Text.Json;
using System.Text.Json.Serialization;
using Mana;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Writes a <see cref="ManaComponentReference"/> as the plain string it holds (<c>"TriggerVolumeComponent#1"</c>),
    /// and as null when it points at nothing -- which the property bag then leaves out of the saved level entirely.
    /// </summary>
    internal sealed class ManaComponentReferenceConverter : JsonConverter<ManaComponentReference>
    {
        public override ManaComponentReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.String
                ? new ManaComponentReference(reader.GetString())
                : ManaComponentReference.None;
        }

        public override void Write(Utf8JsonWriter writer, ManaComponentReference value, JsonSerializerOptions options)
        {
            if (value.IsSet)
            {
                writer.WriteStringValue(value.Key);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
