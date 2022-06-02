// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Printing different types of configuration into a json indented format
    /// Mask property values that contain sensitive information, like credentials for connecting to redis.
    /// </summary>
    public static class ConfigurationPrinter
    {
        /// <summary>
        /// We check the serialized json string to contain any element from this list of strings.
        /// If a string is contained in the serialized string, we match the regex expression,s and mask the property value.
        /// Note: If property names get changed, this list needs to be updated accordingly.
        /// </summary>
        public static string[] CheckSensitiveProperties = new string[] {"ConnectionString", "Credentials"};

        /// <nodoc />
        public static string ConfigToString<T>(T config, bool withSecrets = false)
        {
            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.WriteIndented = true;
            jsonOptions.Converters.Add(new AbsolutePathConverter());
            foreach (var converter in DeploymentUtilities.ConfigurationSerializationOptions.Converters)
            {
                jsonOptions.Converters.Add(converter);
            }

            var jsonString = JsonSerializer.Serialize(config, jsonOptions);

            foreach (var sensitiveProperty in CheckSensitiveProperties)
            {
                if (!withSecrets)
                {
                    // Currently System.Text.Json does not support implementing a contract resolver like in Newtonsoft, so we are using regex replace here
                    jsonString = Regex.Replace(jsonString, $"(\"\\w*?{sensitiveProperty}\\w*?\"):.+?\"(.+?)\"", "$1: \"xxxx\"");
                }
            }

            return jsonString;
        }

        /// <nodoc />
        public static void TraceConfiguration<T>(T? config, ILogger logger)
        {
            // Create a context to get structured logging support
            var context = new Context(logger);
            context.Debug(config is null ? "null" : ConfigToString(config), nameof(ConfigurationPrinter), $"{nameof(TraceConfiguration)}.{typeof(T).Name}");
        }
    }

    internal class AbsolutePathConverter : JsonConverter<AbsolutePath>
    {
        public override AbsolutePath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => default!;

        public override void Write(Utf8JsonWriter writer, AbsolutePath value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Path);
        }
    }
}
