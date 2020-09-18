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
using BuildXL.Cache.Host.Configuration;

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
                new BoolJsonConverter()
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
            var configDropLayout = manifest.Drops[ConfigDropUri.OriginalString];
            var configFileSpec = configDropLayout[DeploymentConfigurationFileName];
            return deploymentRoot / GetContentRelativePath(new ContentHash(configFileSpec.Hash));
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
                    }
                    .Where(e => !string.IsNullOrEmpty(e.Value))
                    .Select(e => new ConstraintDefinition(e.Key, new[] { e.Value })),
                replacementMacros: new Dictionary<string, string>()
                    {
                        { "Env", parameters.Environment },
                        { "Environment", parameters.Environment },
                        { "Stamp", parameters.Stamp },
                        { "StampId", parameters.Stamp },
                        { "Region", parameters.Region },
                        { "RegionId", parameters.Region },
                        { "Ring", parameters.Ring },
                        { "RingId", parameters.Ring },
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
                return TimeSpan.Parse(reader.GetString());
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}
