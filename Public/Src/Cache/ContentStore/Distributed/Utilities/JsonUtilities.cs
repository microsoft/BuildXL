// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Utilities for json serialization and deserialization of cache types
    /// </summary>
    public static class JsonUtilities
    {
        /// <summary>
        /// Options used when deserializing deployment configuration
        /// </summary>
        public static JsonSerializerOptions DefaultSerializationOptions { get; } = GetOptions(indent: false);

        public static JsonSerializerOptions IndentedSerializationOptions { get; } = GetOptions(indent: true);

        private static JsonSerializerOptions GetOptions(bool indent)
        {
            return new JsonSerializerOptions()
            {
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                WriteIndented = indent,
                Converters =
                {
                    new TimeSpanJsonConverter(),
                    new StringConvertibleSettingJsonConverterFactory(),
                    new BoolJsonConverter(),
                    new JsonStringEnumConverter(),
                    FuncJsonConverter.Create(ReadMachineId, (writer, value) => writer.WriteNumberValue(value.Index)),
                    FuncJsonConverter.Create(ReadMachineLocation, (writer, value) => writer.WriteStringValue(value.Path)),
                    FuncJsonConverter.Create(ReadShortHash, (writer, value) => writer.WriteStringValue(value.HashType == HashType.Unknown ? null : value.ToString())),
                }
            };
        }

        private static MachineLocation ReadMachineLocation(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var document = JsonDocument.ParseValue(ref reader);
                var property = document.RootElement.GetProperty(nameof(MachineLocation.Path));
                if (property.ValueKind == JsonValueKind.Null)
                {
                    return default;
                }

                return new MachineLocation(property.GetString());
            }
            
            var data = reader.GetString();
            return data == null ? default : new MachineLocation(data);
        }

        private static MachineId ReadMachineId(ref Utf8JsonReader reader)
        {
            return new MachineId(reader.GetInt32());
        }

        private static ShortHash ReadShortHash(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Skip();
                return default;
            }

            var data = reader.GetString();
            return data == null ? default : new ShortHash(data);
        }

        /// <summary>
        /// Options used when reading deployment configuration
        /// </summary>
        public static JsonDocumentOptions DefaultDocumentOptions { get; } = new JsonDocumentOptions()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Serialize the value to json using <see cref="DefaultSerializationOptions"/>
        /// </summary>
        public static string JsonSerialize<T>(T value, bool indent = false)
        {
            return JsonSerializer.Serialize<T>(value, indent ? IndentedSerializationOptions : DefaultSerializationOptions);
        }

        /// <summary>
        /// Deserialize the value to json using <see cref="DefaultSerializationOptions"/>
        /// </summary>
        public static T JsonDeserialize<T>(string value)
        {
            return JsonSerializer.Deserialize<T>(value, DefaultSerializationOptions);
        }

        /// <summary>
        /// Deserialize the value to json using <see cref="DefaultSerializationOptions"/>
        /// </summary>
        public static ValueTask<T> JsonDeserializeAsync<T>(Stream stream, CancellationToken token = default)
        {
            return JsonSerializer.DeserializeAsync<T>(stream, DefaultSerializationOptions, token);
        }

        /// <summary>
        /// Gets a stream for the binary UTF8 encoded representation of the current string
        /// </summary>
        public static Stream AsStream(this string value)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Creates json converters from delegates
        /// </summary>
        public static class FuncJsonConverter
        {
            /// <summary>
            /// Creates a json converter from the given delegates
            /// </summary>
            public static JsonConverter<T> Create<T>(
                JsonReaderFunc<T> read,
                Action<Utf8JsonWriter, T> write)
            {
                return new Converter<T>(read, write);
            }

            /// <nodoc />
            public delegate T JsonReaderFunc<T>(ref Utf8JsonReader reader);

            private class Converter<T> : JsonConverter<T>
            {
                private readonly JsonReaderFunc<T> _read;
                private readonly Action<Utf8JsonWriter, T> _write;

                public Converter(JsonReaderFunc<T> read, Action<Utf8JsonWriter, T> write)
                {
                    _read = read;
                    _write = write;
                }

                public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                {
                    return _read(ref reader);
                }

                public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                {
                    _write(writer, value);
                }
            }
        }

        private class BoolJsonConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        return bool.Parse(reader.GetString());
                    case JsonTokenType.True:
                        return true;
                    case JsonTokenType.False:
                        return false;
                }

                throw new JsonException();
            }

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            {
                writer.WriteBooleanValue(value);
            }
        }

        private class TimeSpanJsonConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var timeSpanString = reader.GetString();
                if (TimeSpanSetting.TryParseReadableTimeSpan(timeSpanString, out var result))
                {
                    return result;
                }

                return TimeSpan.Parse(timeSpanString);
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }

        private class StringConvertibleSettingJsonConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
            {
                return typeToConvert.IsValueType && typeof(IStringConvertibleSetting).IsAssignableFrom(typeToConvert);
            }

            public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                return (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert));
            }

            private class Converter<T> : JsonConverter<T>
                where T : struct, IStringConvertibleSetting
            {
                private readonly T _defaultValue = default;

                public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                {
                    var stringValue = reader.GetString();
                    return (T)_defaultValue.ConvertFromString(stringValue);
                }

                public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                {
                    writer.WriteStringValue(value.ConvertToString());
                }
            }
        }
    }
}
