// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Allows for serialization and deserialization in the scenario where object type is not know during deserialization.
    /// Works by having a wrapper which includes the type of the encapsulated object.
    /// </summary>
    public static class DynamicJson
    {
        /// <nodoc />
        public static string Serialize(object value, Type type)
        {
            var wrapper = new DynamicJsonWrapper
            {
                Type = type.AssemblyQualifiedName!,
                Object = value,
            };

            return JsonSerializer.Serialize(wrapper);
        }

        /// <nodoc />
        public static string Serialize<T>(T value) => Serialize(value!, value!.GetType());

        /// <nodoc />
        public static (object?, Type) Deserialize(string serialized)
        {
            var wrapper = JsonSerializer.Deserialize<DynamicJsonWrapper>(serialized);
            var type = Type.GetType(wrapper!.Type);
            return (wrapper.Object, type!);
        }

        [JsonConverter(typeof(Converter))]
        private class DynamicJsonWrapper
        {
            public string Type { get; set; } = string.Empty;

            public object? Object { get; set; }
        }

        private class Converter : JsonConverter<DynamicJsonWrapper>
        {
            public override DynamicJsonWrapper Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException();
                }

                string? type = null;
                string? config = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    // Get the key.
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException();
                    }

                    var propertyName = reader.GetString() ?? string.Empty;

                    reader.Read();

                    switch (propertyName)
                    {
                        case nameof(DynamicJsonWrapper.Type): type = reader.GetString(); ; break;
                        case nameof(DynamicJsonWrapper.Object): config = JsonSerializer.Deserialize<JsonElement>(ref reader, options).GetRawText(); break;
                    }
                }

                if (type == null || config == null)
                {
                    throw new Exception();
                }

                var reflectedType = Type.GetType(type)!;
                var wrapper = new DynamicJsonWrapper
                {
                    Object = JsonSerializer.Deserialize(config!, reflectedType, options),
                    Type = type
                };

                return wrapper;
            }

            public override void Write(Utf8JsonWriter writer, DynamicJsonWrapper value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString(nameof(DynamicJsonWrapper.Type), value.Type);
                var reflectedType = Type.GetType(value.Type)!;
                writer.WritePropertyName(nameof(DynamicJsonWrapper.Object));
                JsonSerializer.Serialize(writer: writer, value.Object, reflectedType, options);
                writer.WriteEndObject();
            }
        }
    }
}
