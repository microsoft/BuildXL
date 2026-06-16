// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// Shared JSON helpers for reading JSON files and resolving CloudTest types.
    /// </summary>
    public static class JsonHelpers
    {
        /// <summary>
        /// Default options for reading JSON files (case-insensitive property names).
        /// </summary>
        public static JsonSerializerOptions ReadOptions { get; } = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Reads and deserializes a JSON file, returning null if the path is null or empty.
        /// </summary>
        public static T ReadJsonFile<T>(string filePath) where T : class
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            string json = ReadJsonText(filePath);
            return JsonSerializer.Deserialize<T>(json, ReadOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize JSON file '{filePath}'.");
        }

        /// <summary>
        /// Reads a JSON file and returns a parsed <see cref="JsonDocument"/> for DOM-style navigation.
        /// Returns null if the path is null or empty.
        /// Caller is responsible for disposing the returned document.
        /// </summary>
        public static JsonDocument ReadJsonDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            string json = ReadJsonText(filePath);
            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Reads the text content of a JSON file, validating the file exists.
        /// </summary>
        public static string ReadJsonText(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"JSON file '{filePath}' does not exist.");
            }

            return File.ReadAllText(filePath);
        }

        /// <summary>
        /// JSON converter for CloudTestPath values from DScript.
        /// Handles two JSON shapes:
        /// - A plain string (from Path or RelativePath in DScript): absolute paths pass through,
        ///   relative paths get "[WorkingDirectory]\" prepended.
        /// - An object {"prefix":"X","path":"Y"} (from PrefixedPath in DScript): resolved to "[X]\Y".
        /// </summary>
        public sealed class CloudTestPathConverter : JsonConverter<string>
        {
            /// <inheritdoc/>
            public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // Absolute or relative path case
                if (reader.TokenType == JsonTokenType.String)
                {
                    string value = reader.GetString();
                    if (Path.IsPathRooted(value))
                    {
                        return value;
                    }

                    // A relative path is interpreted as relative to the working directory.
                    return $@"[WorkingDirectory]\{value}";
                }

                // Prefixed path case
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    string prefix = null;
                    string path = null;
                    var unrecognizedProperties = new List<string>();

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            string propertyName = reader.GetString();
                            reader.Read();
                            if (string.Equals(propertyName, "prefix", StringComparison.OrdinalIgnoreCase))
                            {
                                prefix = reader.GetString();
                            }
                            else if (string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase))
                            {
                                path = reader.GetString();
                            }
                            else
                            {
                                unrecognizedProperties.Add(propertyName);
                            }
                        }
                    }

                    if (prefix == null)
                    {
                        throw new JsonException($"PrefixedPath object must have a 'prefix' property, but found: {string.Join(", ", unrecognizedProperties)} (at index {reader.TokenStartIndex}).");
                    }

                    return path != null ? $@"[{prefix}]\{path}" : $"[{prefix}]";
                }

                throw new JsonException($"Expected a string or an object (with 'prefix'/'path' properties) for CloudTestPath, but found JSON token '{reader.TokenType}' (at index {reader.TokenStartIndex}).");
            }

            /// <inheritdoc/>
            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value);
            }
        }

        /// <summary>
        /// JSON converter for CloudTestArgument values from DScript.
        /// Handles:
        /// - A plain string or number (PrimitiveValue): returned as-is.
        /// - An object {"values":[...],"separator":"..."} (CompoundPrimitiveValue): recursively resolved and joined.
        ///   Separator defaults to space if omitted.
        /// </summary>
        public sealed class ScriptArgsConverter : JsonConverter<string>
        {
            /// <inheritdoc/>
            public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return ReadArgument(ref reader);
            }

            private static string ReadArgument(ref Utf8JsonReader reader)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        return reader.GetString();
                    case JsonTokenType.Number:
                        // DScript only has integer numbers — use GetInt64 to preserve exact values.
                        return reader.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case JsonTokenType.Null:
                        return null;
                    case JsonTokenType.StartObject:
                        return ReadCompoundValue(ref reader);
                    default:
                        throw new JsonException($"Expected a string, number, or object (CompoundPrimitiveValue) for CloudTestArgument, but found JSON token '{reader.TokenType}' (at index {reader.TokenStartIndex}).");
                }
            }

            private static string ReadCompoundValue(ref Utf8JsonReader reader)
            {
                List<string> values = null;
                string separator = " ";
                var unrecognizedProperties = new List<string>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string propertyName = reader.GetString();
                        reader.Read();

                        if (string.Equals(propertyName, "values", StringComparison.OrdinalIgnoreCase))
                        {
                            values = ReadArgumentArray(ref reader);
                        }
                        else if (string.Equals(propertyName, "separator", StringComparison.OrdinalIgnoreCase))
                        {
                            separator = reader.GetString() ?? " ";
                        }
                        else
                        {
                            unrecognizedProperties.Add(propertyName);
                        }
                    }
                }

                if (values == null)
                {
                    throw new JsonException($"CompoundPrimitiveValue object must have a 'values' array property, but found: {string.Join(", ", unrecognizedProperties)} (at index {reader.TokenStartIndex}).");
                }

                return string.Join(separator, values);
            }

            private static List<string> ReadArgumentArray(ref Utf8JsonReader reader)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException($"Expected an array for CompoundPrimitiveValue 'values' property, but found JSON token '{reader.TokenType}' (at index {reader.TokenStartIndex}).");
                }

                var result = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var fragment = ReadArgument(ref reader);
                    if (fragment != null)
                    {
                        result.Add(fragment);
                    }
                }

                return result;
            }

            /// <inheritdoc/>
            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
