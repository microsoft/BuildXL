// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Utilities for interacting with deployment root populated by <see cref="DeploymentIngester"/>
    /// </summary>
    public static class DeploymentUtilities
    {
        /// <summary>
        /// Options used when deserializing deployment configuration
        /// </summary>
        public static JsonSerializerOptions ConfigurationSerializationOptions { get; } = new JsonSerializerOptions()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters =
            {
                new TimeSpanJsonConverter(),
                new BoolJsonConverter(),
                new JsonStringEnumConverter()
            }
        };

        /// <summary>
        /// Special synthesized drop url for config files added by DeploymentRunner
        /// </summary>
        public static Uri ConfigDropUri { get; } = new Uri("config://files/");

        /// <summary>
        /// Options used when reading deployment configuration
        /// </summary>
        public static JsonDocumentOptions ConfigurationDocumentOptions { get; } = new JsonDocumentOptions()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Relative path to root of CAS for deployment files
        /// </summary>
        private static RelativePath CasRelativeRoot { get; } = new RelativePath("cas");

        /// <summary>
        /// Relative path to deployment manifest from deployment root
        /// </summary>
        private static RelativePath DeploymentManifestRelativePath { get; } = new RelativePath("DeploymentManifest.json");

        /// <summary>
        /// File name of deployment configuration in synthesized config drop
        /// </summary>
        public static string DeploymentConfigurationFileName { get; } = "DeploymentConfiguration.json";

        /// <summary>
        /// Gets the relative from deployment root to the file with given hash in CAS
        /// </summary>
        public static RelativePath GetContentRelativePath(ContentHash hash)
        {
            return CasRelativeRoot / FileSystemContentStoreInternal.GetPrimaryRelativePath(hash);
        }

        /// <summary>
        /// Gets the absolute path to the CAS root given the deployment root
        /// </summary>
        public static AbsolutePath GetCasRootPath(AbsolutePath deploymentRoot)
        {
            return deploymentRoot / CasRelativeRoot;
        }

        /// <summary>
        /// Gets the absolute path to the deployment manifest
        /// </summary>
        public static AbsolutePath GetDeploymentManifestPath(AbsolutePath deploymentRoot)
        {
            return deploymentRoot / DeploymentManifestRelativePath;
        }

        /// <summary>
        /// Gets the absolute path to the deployment configuration under the deployment root
        /// </summary>
        public static AbsolutePath GetDeploymentConfigurationPath(AbsolutePath deploymentRoot, DeploymentManifest manifest)
        {
            var configFileSpec = manifest.GetDeploymentConfigurationSpec();
            return deploymentRoot / GetContentRelativePath(new ContentHash(configFileSpec.Hash));
        }

        /// <summary>
        /// Gets the file spec for the deployment configuration file
        /// </summary>
        public static FileSpec GetDeploymentConfigurationSpec(this DeploymentManifest manifest)
        {
            var configDropLayout = manifest.Drops[ConfigDropUri.OriginalString];
            return configDropLayout[DeploymentConfigurationFileName];
        }

        /// <summary>
        /// Serialize the value to json using <see cref="ConfigurationSerializationOptions"/>
        /// </summary>
        public static string JsonSerialize<T>(T value)
        {
            return JsonSerializer.Serialize<T>(value, ConfigurationSerializationOptions);
        }

        /// <summary>
        /// Parses a <see cref="TimeSpan"/> in readable format
        ///
        /// Format:
        /// [-][#d][#h][#m][#s][#ms]
        /// where # represents any valid non-negative double. All parts are optional but string must be non-empty.
        /// </summary>
        public static bool TryParseReadableTimeSpan(string value, out TimeSpan result)
        {
            int start = 0;
            int lastUnitIndex = -1;

            result = TimeSpan.Zero;
            bool isNegative = false;
            bool succeeded = true;

            // Easier to process if all specifiers are mutually exclusive characters
            // So replace 'ms' with 'f' so it doesn't conflict with 'm' and 's' specifiers
            value = value.Trim().Replace("ms", "f");

            for (int i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case ':':
                        // Quickly bypass normal timespans which contain ':' character
                        // that is not allowed in readable timespan format
                        return false;
                    case '-':
                        succeeded = lastUnitIndex == -1;
                        lastUnitIndex = 0;
                        isNegative = true;
                        break;
                    case 'd':
                        succeeded = process(1, TimeSpan.FromDays(1), ref result);
                        break;
                    case 'h':
                        succeeded = process(2, TimeSpan.FromHours(1), ref result);
                        break;
                    case 'm':
                        succeeded = process(3, TimeSpan.FromMinutes(1), ref result);
                        break;
                    case 's':
                        succeeded = process(4, TimeSpan.FromSeconds(1), ref result);
                        break;
                    case 'f':
                        succeeded = process(5, TimeSpan.FromMilliseconds(1), ref result);
                        break;
                }

                if (!succeeded)
                {
                    return false;
                }

                bool process(int unitIndex, TimeSpan unit, ref TimeSpan result)
                {
                    // No duplicate units allowed and units must appear in decreasing order of magnitude.
                    if (unitIndex > lastUnitIndex)
                    {
                        var factorString = value.Substring(start, i - start).Trim();
                        if (double.TryParse(factorString, out var factor))
                        {
                            result += unit.Multiply(factor);
                            lastUnitIndex = unitIndex;
                            start = i + 1;
                            return true;
                        }
                    }

                    // Invalidate
                    return false;
                }
            }

            result = result.Multiply(isNegative ? -1 : 1);
            return start == value.Length;
        }

        /// <summary>
        /// Gets a json preprocessor for the given host parameters
        /// </summary>
        public static JsonPreprocessor GetHostJsonPreprocessor(HostParameters parameters)
        {
            return new JsonPreprocessor(
                constraintDefinitions: new Dictionary<string, string>()
                    {
                        { "Stamp", parameters.Stamp },
                        { "MachineFunction", parameters.MachineFunction },
                        { "Region", parameters.Region },
                        { "Ring", parameters.Ring },
                        { "Environment", parameters.Environment },
                        { "Env", parameters.Environment },
                        { "Machine", parameters.Machine },

                        // Backward compatibility where machine function was not
                        // its own constraint
                        { "Feature", parameters.MachineFunction == null ? null : "MachineFunction_" + parameters.MachineFunction },
                    }
                    .Where(e => !string.IsNullOrEmpty(e.Value))
                    .Select(e => new ConstraintDefinition(e.Key, new[] { e.Value })),
                replacementMacros: new Dictionary<string, string>()
                    {
                        { "Env", parameters.Environment },
                        { "Environment", parameters.Environment },
                        { "Machine", parameters.Machine },
                        { "Stamp", parameters.Stamp },
                        { "StampId", parameters.Stamp },
                        { "Region", parameters.Region },
                        { "RegionId", parameters.Region },
                        { "Ring", parameters.Ring },
                        { "RingId", parameters.Ring },
                        { "ServiceDir", parameters.ServiceDir },
                    }
                .Where(e => !string.IsNullOrEmpty(e.Value))
                .ToDictionary(e => e.Key, e => e.Value));
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
                if (TryParseReadableTimeSpan(timeSpanString, out var result))
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
    }
}
