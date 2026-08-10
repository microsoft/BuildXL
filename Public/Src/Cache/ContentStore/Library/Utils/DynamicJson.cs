// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

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
        public static string Serialize<T>(T value) where T : notnull
        {
            Contract.Requires(value is not null);
            return Serialize(value, value.GetType());
        }

        /// <nodoc />
        public static (T? Object, Type Type) Deserialize<T>(string serialized)
            where T : class
        {
            using var document = JsonDocument.Parse(serialized);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(nameof(DynamicJsonWrapper.Type), out var typeElement) ||
                !root.TryGetProperty(nameof(DynamicJsonWrapper.Object), out _))
            {
                throw new JsonException("The serialized value is not a valid dynamic JSON wrapper.");
            }

            var typeName = typeElement.GetString();
            var reflectedType = typeName is null ? null : Type.GetType(typeName);
            if (reflectedType is null)
            {
                throw new JsonException($"Could not resolve serialized type '{typeName}'.");
            }

            if (!typeof(T).IsAssignableFrom(reflectedType))
            {
                throw new JsonException($"Serialized type '{reflectedType.AssemblyQualifiedName}' is not assignable to '{typeof(T).AssemblyQualifiedName}'.");
            }

            var wrapper = JsonSerializer.Deserialize<DynamicJsonWrapper>(serialized);
            return ((T?)wrapper!.Object, reflectedType);
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
