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

        private static JsonSerializerOptions GetSerializerOptions(PathTable pathTable, bool indent, bool includePaths, bool ignoreNulls)
        {
            return new JsonSerializerOptions
            {
                WriteIndented = indent,
                IncludeFields = true,
                DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
                Converters = {
                    new JsonStringEnumConverter(allowIntegerValues: true),
                    new PathAtomJsonConverter(pathTable),
                    new CustomLogValueJsonConverter(),
                    new PathConverterFactory(pathTable, !includePaths)
                }
            };
        }

        /// <summary>
        /// Serializes a configuration and writes it as a UTF-8 encoded string into the stream.
        /// </summary>
        public static async Task<Possible<Unit>> SerializeToStreamAsync(this IConfiguration configuration, Stream utf8Json, PathTable pathTable, bool indent, bool includePaths, bool ignoreNulls)
        {
            try
            {
                await JsonSerializer.SerializeAsync<object>(utf8Json, configuration, GetSerializerOptions(pathTable, indent, includePaths, ignoreNulls));
            }
            catch (Exception ex)
            {
                return new Failure<string>(ex.ToStringDemystified());
            }

            return Unit.Void;
        }

        /// <summary>
        /// Serializes a configuration and writes it into a file.
        /// </summary>
        public static async Task<Possible<Unit>> SerialzieToFileAsync(this IConfiguration configuration, PathTable pathTable, bool indent, bool includePaths, bool ignoreNulls)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.Logging.LogsDirectory.IsValid);

            try
            {
                // Save the serialized file into the logs folder.
                var path = Path.Combine(configuration.Logging.LogsDirectory.ToString(pathTable), configuration.Logging.LogPrefix + SerializedConfigFileExtension);
                using var stream = File.Create(path);
                return await SerializeToStreamAsync(configuration, stream, pathTable, indent, includePaths, ignoreNulls);
            }
            catch (Exception ex)
            {
                return new Failure<string>(ex.ToStringDemystified());
            }
        }

        private class PathConverterFactory : JsonConverterFactory
        {
            private readonly PathTable m_pathTable;
            private readonly bool m_replacePaths;

            public PathConverterFactory(PathTable pathTable, bool replacePaths)
            {
                m_pathTable = pathTable;
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
                    return new PathJsonConverter<AbsolutePath>(m_pathTable, m_replacePaths);
                }
                else if (typeToConvert == typeof(RelativePath))
                {
                    return new PathJsonConverter<RelativePath>(m_pathTable, m_replacePaths);
                }

                throw new NotSupportedException($"Cannot create a converter for a type '{typeToConvert}'.");
            }
        }

        private class PathJsonConverter<T> : JsonConverter<T>
        {
            private const string ReplacePathsWith = ".";
            private readonly PathTable m_pathTable;
            private readonly bool m_replacePaths;

            public PathJsonConverter(PathTable pathTable, bool replacePaths)
            {
                m_pathTable = pathTable;
                m_replacePaths = replacePaths;
            }

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // NotSupportedException is used here and elsewhere in this class for two reasons - a) we only need one-way conversion,
                // and b) NotSupportedException has special handling in Text.Json that add additional info to a bubbled up exception.
                throw new NotSupportedException();
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                if (m_replacePaths)
                {
                    writer.WriteStringValue(ReplacePathsWith);
                }
                else
                {
                    if (value is AbsolutePath absolutePath)
                    {
                        writer.WriteStringValue(absolutePath.ToString(m_pathTable));
                    }
                    else if (value is RelativePath relativePath)
                    {
                        writer.WriteStringValue(relativePath.ToString(m_pathTable.StringTable));
                    }
                }
            }

            public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                if (m_replacePaths)
                {
                    writer.WritePropertyName(ReplacePathsWith);
                }
                else
                {
                    if (value is AbsolutePath absolutePath)
                    {
                        writer.WritePropertyName(absolutePath.ToString(m_pathTable));
                    }
                    else if (value is RelativePath relativePath)
                    {
                        writer.WritePropertyName(relativePath.ToString(m_pathTable.StringTable));
                    }
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
    }
#endif
}
