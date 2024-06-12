// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Scheduler
{
    // There are differences in Text.Json between Net5 and Net6; Net6 version allows us to have
    // a succinct code here, and a few features it relies on are not available in Net5.
#if NET6_0_OR_GREATER

    /// <summary>
    /// Extension class for handling config serialization and logging.
    /// </summary>
    public static class ConfigSerializer
    {
        private const string SerializedConfigFileExtension = ".configuration.json";

        private static JsonSerializerOptions GetSerializerOptions(PathTable pathTable, PathTranslator? pathTranslator, bool indent, bool includePaths, bool ignoreNulls)
        {
            var pathConverter = new PathJsonConverter<AbsolutePath>(pathTable, pathTranslator, replacePaths: !includePaths);
            var pathAtomConverter = new PathAtomJsonConverter(pathTable);

            return new JsonSerializerOptions
            {
                WriteIndented = indent,
                IncludeFields = true,
                DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
                // When serializing a type T, the serializer will use the first converter that supports T.
                // If there is an overlap of supported types, make sure that the converters are listed in a desired order.
                Converters = {
                    new JsonStringEnumConverter(allowIntegerValues: true),
                    pathAtomConverter,
                    new CustomLogValueJsonConverter(),
                    new PathConverterFactory(pathTable, pathTranslator, replacePaths: !includePaths),
                    new LocationDataJsonConverter(pathConverter),
                    new DiscriminatingUnionJsonConverter<FileArtifact, PathAtom>(new FileArtifactJsonConverter(pathConverter), pathAtomConverter)
                }
            };
        }

        /// <summary>
        /// Serializes a configuration and writes it as a UTF-8 encoded string into the stream.
        /// </summary>
        /// <param name="configuration">A configuration to serialize.</param>
        /// <param name="utf8Json">The UTF-8 stream to write to.</param>
        /// <param name="pathTable">PathTable to use for converting paths to their string form.</param>
        /// <param name="pathTranslator">If provided, it will be used to translate all <see cref="AbsolutePath"/> values in the configuration.</param>
        /// <param name="indent">Whether to use pretty printing.</param>
        /// <param name="includePaths">Whether to serialize paths values or replace them with a placeholder (to reduced the output size).</param>
        /// <param name="ignoreNulls">Whether to include config settings that have null values.</param>
        public static async Task<Possible<Unit>> SerializeToStreamAsync(this IConfiguration configuration, Stream utf8Json, PathTable pathTable, PathTranslator? pathTranslator, bool indent, bool includePaths, bool ignoreNulls)
        {
            try
            {
                await JsonSerializer.SerializeAsync<object>(utf8Json, configuration, GetSerializerOptions(pathTable, pathTranslator, indent, includePaths, ignoreNulls));
            }
            catch (Exception ex)
            {
                return new Failure<string>(ex.ToStringDemystified());
            }

            return Unit.Void;
        }

        /// <summary>
        /// Serializes a configuration and writes it as a UTF-8 encoded string into the stream.
        /// </summary>
        /// <param name="configuration">A configuration to serialize.</param>
        /// <param name="pathTable">PathTable to use for converting paths to their string form.</param>
        /// <param name="pathTranslator">If provided, it will be used to translate all <see cref="AbsolutePath"/> values in the configuration.</param>
        /// <param name="indent">Whether to use pretty printing.</param>
        /// <param name="includePaths">Whether to serialize paths values or replace them with a placeholder (to reduced the output size).</param>
        /// <param name="ignoreNulls">Whether to include config settings that have null values.</param>
        public static async Task<Possible<Unit>> SerialzieToFileAsync(this IConfiguration configuration, PathTable pathTable, PathTranslator? pathTranslator, bool indent, bool includePaths, bool ignoreNulls)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.Logging.LogsDirectory.IsValid);

            try
            {
                // Save the serialized file into the logs folder.
                var path = Path.Combine(configuration.Logging.LogsDirectory.ToString(pathTable), configuration.Logging.LogPrefix + SerializedConfigFileExtension);
                using var stream = File.Create(path);
                return await SerializeToStreamAsync(configuration, stream, pathTable, pathTranslator, indent, includePaths, ignoreNulls);
            }
            catch (Exception ex)
            {
                return new Failure<string>(ex.ToStringDemystified());
            }
        }

        private class PathConverterFactory : JsonConverterFactory
        {
            private readonly PathTable m_pathTable;
            private readonly PathTranslator? m_pathTranslator;
            private readonly bool m_replacePaths;

            public PathConverterFactory(PathTable pathTable, PathTranslator? pathTranslator, bool replacePaths)
            {
                m_pathTable = pathTable;
                m_pathTranslator = pathTranslator;
                m_replacePaths = replacePaths;
            }

            public override bool CanConvert(Type typeToConvert)
            {
                return typeToConvert == typeof(AbsolutePath)
                    || typeToConvert == typeof(RelativePath);
            }

            public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                if (typeToConvert == typeof(AbsolutePath))
                {
                    return new PathJsonConverter<AbsolutePath>(m_pathTable, m_pathTranslator, m_replacePaths);
                }
                else if (typeToConvert == typeof(RelativePath))
                {
                    return new PathJsonConverter<RelativePath>(m_pathTable, pathTranslator: null, m_replacePaths);
                }

                throw new NotSupportedException($"Cannot create a converter for a type '{typeToConvert}'.");
            }
        }

        private class PathJsonConverter<T> : JsonConverter<T>
        {
            private const string ReplacePathsWith = ".";
            private readonly PathTable m_pathTable;
            private readonly PathTranslator? m_pathTranslator;
            private readonly bool m_replacePaths;

            public PathJsonConverter(PathTable pathTable, PathTranslator? pathTranslator, bool replacePaths)
            {
                m_pathTable = pathTable;
                m_pathTranslator = pathTranslator;
                m_replacePaths = replacePaths;
            }

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // NotSupportedException is used here and elsewhere in this class for two reasons - a) we only need one-way conversion,
                // and b) NotSupportedException has special handling in Text.Json that add additional info to a bubbled up exception.
                throw new NotSupportedException();
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(PrepareValueForSerialization(value));

            public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(PrepareValueForSerialization(value));

            public string PrepareValueForSerialization(T value)
            {
                if (m_replacePaths)
                {
                    return ReplacePathsWith;
                }
                else
                {
                    if (value is AbsolutePath absolutePath)
                    {
                        return m_pathTranslator != null
                            ? m_pathTranslator.Translate(absolutePath.ToString(m_pathTable))
                            : absolutePath.ToString(m_pathTable);
                    }
                    else if (value is RelativePath relativePath)
                    {
                        return relativePath.ToString(m_pathTable.StringTable);
                    }

                    throw new NotSupportedException($"Values of a type '{typeof(T)}' are not supported.");
                }
            }
        }

        private class PathAtomJsonConverter : JsonConverter<PathAtom>
        {
            private readonly PathTable m_pathTable;

            public PathAtomJsonConverter(PathTable pathTable)
            {
                m_pathTable = pathTable;
            }

            public override PathAtom Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, PathAtom value, JsonSerializerOptions options) => writer.WriteStringValue(GetStringFromStringId(value));

            public override void WriteAsPropertyName(Utf8JsonWriter writer, PathAtom value, JsonSerializerOptions options) => writer.WritePropertyName(GetStringFromStringId(value));

            private string GetStringFromStringId(PathAtom pathAtom)
            {
                return pathAtom.IsValid ? pathAtom.ToString(m_pathTable.StringTable) : "{Invalid}";
            }
        }

        /// <summary>
        /// ValueTuples don't have 'names' for their fields, so the default ones (Item1, Item2, ...) are used.
        /// Handle this tuple manually to have a nicer output. Technically, this converter will be called for all
        /// (IReadOnlyList_int, EventLevel?) tuples, i.e., field names used here might not be appropriate there.
        /// However, there is single instance of such tuple type in the repo, so we should be safe here; and we
        /// could deal with collisions later if they ever occur.
        /// </summary>
        private class CustomLogValueJsonConverter : JsonConverter<(IReadOnlyList<int>, EventLevel?)>
        {
            public override (IReadOnlyList<int>, EventLevel?) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, (IReadOnlyList<int>, EventLevel?) value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteStartArray("EventIds");
                foreach (var id in value.Item1)
                {
                    writer.WriteNumberValue(id);
                }

                writer.WriteEndArray();
                writer.WriteString("EventLevel", value.Item2?.ToString());
                writer.WriteEndObject();
            }
        }

        private class LocationDataJsonConverter : JsonConverter<LocationData>
        {
            private readonly PathJsonConverter<AbsolutePath> m_pathConverter;

            public LocationDataJsonConverter(PathJsonConverter<AbsolutePath> pathConverter)
            {
                m_pathConverter = pathConverter;
            }

            public override LocationData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, LocationData value, JsonSerializerOptions options)
                => writer.WriteStringValue($"{m_pathConverter.PrepareValueForSerialization(value.Path)}{(value.IsValid ? $" ({value.Line}, {value.Position})" : string.Empty)}");
        }

        private class DiscriminatingUnionJsonConverter<T, U> : JsonConverter<DiscriminatingUnion<T, U>>
        {
            private readonly JsonConverter<T> m_tConverter;
            private readonly JsonConverter<U> m_uConverter;

            public DiscriminatingUnionJsonConverter(JsonConverter<T> tConverter, JsonConverter<U> uConverter)
            {
                m_tConverter = tConverter;
                m_uConverter = uConverter;
            }

            public override DiscriminatingUnion<T, U>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, DiscriminatingUnion<T, U> value, JsonSerializerOptions options)
            {
                object duValue = value.GetValue();

                if (duValue == null)
                {
                    writer.WriteNullValue();
                }
                else if (duValue is T tValue)
                {
                    m_tConverter.Write(writer, tValue, options);
                }
                else if (duValue is U uValue)
                {
                    m_uConverter.Write(writer, uValue, options);
                }
            }
        }

        /// <summary>
        /// Serializes a FileArtifact as a file path value (i.e., rewrite count is ignored).
        /// </summary>
        private class FileArtifactJsonConverter : JsonConverter<FileArtifact>
        {
            private readonly PathJsonConverter<AbsolutePath> m_pathConverter;

            public FileArtifactJsonConverter(PathJsonConverter<AbsolutePath> pathConverter)
            {
                m_pathConverter = pathConverter;
            }

            public override FileArtifact Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, FileArtifact value, JsonSerializerOptions options) => m_pathConverter.Write(writer, value.Path, options);
        }
    }
#endif
}
