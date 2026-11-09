using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManaEditor.Code.Levels
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

    /// <summary>
    /// Writes a <see cref="Vector4"/> -- a colour, mostly -- as <c>[r, g, b, a]</c>, for the same reason: its members
    /// are fields, so without this a light's colour saved as <c>{}</c> and came back black.
    /// </summary>
    /// <summary>A Vector2 as [x, y] -- a sprite's pivot, say.</summary>
    internal sealed class ManaLevelVector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Skip();
                return default;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected [x, y].");
            }

            var values = new float[2];
            var count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (count >= 2)
                {
                    throw new JsonException("Expected two numbers in [x, y].");
                }

                values[count++] = reader.GetSingle();
            }

            if (count != 2)
            {
                throw new JsonException("Expected [x, y].");
            }

            return new Vector2(values[0], values[1]);
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    internal sealed class ManaLevelVector4Converter : JsonConverter<Vector4>
    {
        public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // What a file saved before this converter holds: nothing, as {}. Read as the default, not refused.
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Skip();
                return default;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected [x, y, z, w].");
            }

            var values = new float[4];
            var count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (count >= 4)
                {
                    throw new JsonException("Expected at most four numbers in [x, y, z, w].");
                }

                values[count++] = reader.GetSingle();
            }

            // Three numbers is an opaque colour.
            if (count == 3)
            {
                values[3] = 1.0f;
            }
            else if (count != 4)
            {
                throw new JsonException("Expected [x, y, z] or [x, y, z, w].");
            }

            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteNumberValue(value.W);
            writer.WriteEndArray();
        }
    }
}
