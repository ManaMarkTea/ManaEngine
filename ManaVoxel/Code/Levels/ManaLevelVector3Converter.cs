using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Writes a <see cref="Vector3"/> as <c>[x, y, z]</c>, the same shape the rest of the level file uses.
    /// Without it a script property of this type serialises as <c>{}</c> -- the struct's members are fields,
    /// which System.Text.Json skips -- and every trigger volume would load back as zero-sized.
    /// </summary>
    internal sealed class ManaLevelVector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected [x, y, z].");
            }

            reader.Read();
            var x = reader.GetSingle();
            reader.Read();
            var y = reader.GetSingle();
            reader.Read();
            var z = reader.GetSingle();
            reader.Read();

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("Expected exactly three numbers in [x, y, z].");
            }

            return new Vector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}
