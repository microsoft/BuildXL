// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// Generates a CloudTest DJE session and job configuration JSON file.
    /// </summary>
    public static class ConfigGeneratorHelper
    {
        /// <summary>
        /// Namespace GUID used for deterministic groupId generation per the DJE spec.
        /// </summary>
        /// See https://dev.azure.com/mseng/1ES/_wiki/wikis/1ES.wiki/57118/DJE-API-Flow?anchor=groupid-computation and
        /// https://dev.azure.com/mseng/Domino/_git/CloudTest?path=/private/Services/Expansion/Shared/ExpansionHelper.cs&version=GBmaster&_a=contents
        /// <remarks
        private static readonly Guid s_groupIdNamespace = Guid.Parse("932dcf1e-b732-49a0-82f8-dd0ecf0cedf1");


        /// <summary>
        /// An arbitrary GUID used as a namespace for representing BuildXL submitted sessions (for the purpose of generating stable job IDs).
        /// </summary>

        /// <summary>
        /// Generates the session config and writes it to the specified output file.
        /// Returns the path to the generated file.
        /// </summary>
        public static string GenerateSessionConfig(CloudTestClientArgs arguments)
        {
            var jobs = arguments.Jobs;

            if (jobs.Count == 0)
            {
                throw new InvalidOperationException("No job definitions provided.");
            }

            // Auto-generate IDs
            var testSessionId = Guid.NewGuid().ToString();
            var groupId = ComputeGroupId(arguments.Image, arguments.Sku);

            // Build job placeholders — honor explicit IDs when provided, otherwise auto-generate
            var dynamicJobRequests = jobs.Select(job => new DynamicJobRequest(
                // If the job definition includes an explicit ID, use it; otherwise, generate a stable GUID based on the BuildXL namespace and job name
                JobId: job.Id ?? NewStableGuid(job.Name).ToString(),
                JobName: job.Name)).ToList();

            // Build the session config
            var config = new SessionConfig(
                TestSessionId: testSessionId,
                DisplayName: arguments.DisplayName ?? $"DJE Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                TenantId: arguments.Tenant,
                User: arguments.User ?? "unknown",
                BuildDropLocation: arguments.BuildDropLocation,
                CacheEnabled: arguments.CacheEnabled,
                DynamicGroupRequests: new List<DynamicGroupRequest>
                {
                    new DynamicGroupRequest(
                        SessionId: testSessionId,
                        GroupName: $"{arguments.Image} {arguments.Sku}",
                        GroupId: groupId,
                        Sku: arguments.Sku,
                        Image: arguments.Image,
                        MaxResources: arguments.MaxResources,
                        DynamicJobRequests: dynamicJobRequests)
                },
                VSTSContext: BuildVstsContext(arguments));

            // Serialize and write
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            string json = JsonSerializer.Serialize(config, options);

            string directory = Path.GetDirectoryName(arguments.ConfigOutputFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(arguments.ConfigOutputFile, json);
            return arguments.ConfigOutputFile;
        }

        /// <summary>
        /// Generates the UpdateDynamicJob configuration and writes it to the specified output file.
        /// Returns the path to the generated file.
        /// </summary>
        public static string GenerateUpdateDynamicJobConfig(CloudTestClientArgs arguments)
        {
            var groupId = ComputeGroupId(arguments.Image, arguments.Sku);

            var config = new UpdateDynamicJobConfig(
                SessionId: arguments.SessionId,
                GroupId: groupId,
                JobId: arguments.JobId,
                TestFolder: arguments.TestFolder,
                JobExecutable: NormalizeJobExecutable(arguments.JobExecutable),
                TestExecutionType: arguments.TestExecutionType,
                JobArguments: arguments.JobArguments,
                TestParserType: arguments.TestParserType,
                JobTimeout: arguments.JobTimeout,
                TestCaseTimeout: arguments.TestCaseTimeout,
                TestDependencyHash: AggregateHashes(arguments.TestDependencyHashes),
                Priority: arguments.Priority);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            string json = JsonSerializer.Serialize(config, options);

            string directory = Path.GetDirectoryName(arguments.ConfigOutputFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(arguments.ConfigOutputFile, json);
            return arguments.ConfigOutputFile;
        }

        /// <summary>
        /// If <paramref name="jobExecutable"/> is a relative path, prepends the
        /// <c>[WorkingDirectory]</c> macro so CloudTest resolves it on the worker VM.
        /// Absolute paths and paths that already start with a macro (<c>[...]</c>) are
        /// returned unchanged.
        /// </summary>
        private static string NormalizeJobExecutable(string jobExecutable)
        {
            if (string.IsNullOrEmpty(jobExecutable))
            {
                return jobExecutable;
            }

            // Already references a CloudTest macro (e.g. [WorkingDirectory], [BuildRoot]).
            if (jobExecutable.StartsWith("[", StringComparison.Ordinal))
            {
                return jobExecutable;
            }

            // Absolute path on either platform.
            if (Path.IsPathRooted(jobExecutable))
            {
                return jobExecutable;
            }

            // Relative path: prepend the [WorkingDirectory] macro.
            return Path.Join("[WorkingDirectory]", jobExecutable);
        }

        /// <summary>
        /// Computes the groupId as SHA256-GUID(image + sku) using the DJE namespace GUID.
        /// </summary>
        private static string ComputeGroupId(string image, string sku)
        {
            // Per DJE spec: groupId = SHA256-GUID(image + sku) using namespace 932dcf1e-b732-49a0-82f8-dd0ecf0cedf1
            byte[] namespaceBytes = s_groupIdNamespace.ToByteArray();
            byte[] nameBytes = Encoding.UTF8.GetBytes(image + sku);

            byte[] input = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

            byte[] hash = SHA256.HashData(input);

            // Take first 16 bytes of the hash to form a GUID
            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);

            // Set version to 5 (name-based SHA) and variant bits
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            return new Guid(guidBytes).ToString();
        }

        /// <summary>
        /// Builds the VstsContext JSON string when ADO context arguments are provided.
        /// Returns null if no ADO context is configured.
        /// </summary>
        private static string BuildVstsContext(CloudTestClientArgs arguments)
        {
            if (string.IsNullOrEmpty(arguments.AdoProjectId))
            {
                return null;
            }

            string authToken = Environment.GetEnvironmentVariable(arguments.AdoAccessTokenEnvVar);
            if (string.IsNullOrEmpty(authToken))
            {
                throw new InvalidOperationException(
                    $"ADO context was requested but environment variable '{arguments.AdoAccessTokenEnvVar}' is not set or empty.");
            }

            var vstsContext = new VstsContextPayload(
                ProjectId: arguments.AdoProjectId,
                AuthToken: authToken,
                VSTSUrl: arguments.AdoCollectionUri,
                BuildProperties: new VstsBuildProperties(BuildId: arguments.AdoBuildId),
                CloudTestVSTSRequest: new VstsUploadRequest(UploadResultsToVSTS: "true"));

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            return JsonSerializer.Serialize(vstsContext, options);
        }

        /// <summary>
        /// Generates a deterministic GUID from a string value using SHA-256.
        /// The same input always produces the same GUID.
        /// </summary>
        private static Guid NewStableGuid(string value)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return new Guid(hash.AsSpan(0, 16));
        }



        #region JSON model

        private sealed record SessionConfig(
            string TestSessionId,
            string DisplayName,
            string TenantId,
            string User,
            string BuildDropLocation,
            bool CacheEnabled,
            List<DynamicGroupRequest> DynamicGroupRequests,
            [property: JsonPropertyName("VSTSContext")] string VSTSContext);

        private sealed record DynamicGroupRequest(
            string SessionId,
            string GroupName,
            string GroupId,
            string Sku,
            string Image,
            int MaxResources,
            List<DynamicJobRequest> DynamicJobRequests);

        private sealed record DynamicJobRequest(string JobId, string JobName);

        private sealed record VstsContextPayload(
            string ProjectId,
            string AuthToken,
            [property: JsonPropertyName("VSTSUrl")] string VSTSUrl,
            VstsBuildProperties BuildProperties,
            [property: JsonPropertyName("CloudTestVSTSRequest")] VstsUploadRequest CloudTestVSTSRequest);

        private sealed record VstsBuildProperties(string BuildId);

        private sealed record VstsUploadRequest(string UploadResultsToVSTS);

        private sealed record UpdateDynamicJobConfig(
            string SessionId,
            string GroupId,
            string JobId,
            string TestFolder,
            string JobExecutable,
            string TestExecutionType,
            string JobArguments,
            string TestParserType,
            string JobTimeout,
            string TestCaseTimeout,
            string TestDependencyHash,
            int? Priority);

        /// <summary>
        /// Aggregates multiple hash strings into a single deterministic hash.
        /// Sorts the input hashes, concatenates them, and computes a SHA256 hash.
        /// Returns null if the input list is null or empty.
        /// </summary>
        private static string AggregateHashes(List<string> hashes)
        {
            if (hashes == null || hashes.Count == 0)
            {
                return null;
            }

            if (hashes.Count == 1)
            {
                return hashes[0];
            }

            var sorted = hashes.OrderBy(h => h, StringComparer.Ordinal).ToList();
            var concatenated = string.Join("|", sorted);
            var bytes = Encoding.UTF8.GetBytes(concatenated);
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        #endregion


    }
}
