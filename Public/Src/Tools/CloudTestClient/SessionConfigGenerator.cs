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
            string json = GenerateSessionConfigJson(arguments);

            string directory = Path.GetDirectoryName(arguments.ConfigOutputFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(arguments.ConfigOutputFile, json);
            return arguments.ConfigOutputFile;
        }

        /// <summary>
        /// Builds the session config and returns it as a JSON string without writing to disk. Useful for tests that
        /// only need to inspect the generated config (avoiding a write-then-read-back round trip).
        /// </summary>
        public static string GenerateSessionConfigJson(CloudTestClientArgs arguments)
        {
            if (string.IsNullOrEmpty(arguments.SessionInputFile))
            {
                throw new InvalidOperationException("No session input file provided.");
            }

            // Read the entire session definition from a single input file.
            var input = JsonHelpers.ReadJsonFile<SessionInputConfig>(arguments.SessionInputFile)
                ?? throw new InvalidOperationException($"Failed to read session input from '{arguments.SessionInputFile}'.");

            if (string.IsNullOrEmpty(input.Tenant))
            {
                throw new InvalidOperationException($"Session input '{arguments.SessionInputFile}' is missing the required 'tenant' field.");
            }

            if (string.IsNullOrEmpty(input.BuildDropLocation))
            {
                throw new InvalidOperationException($"Session input '{arguments.SessionInputFile}' is missing the required 'buildDropLocation' field.");
            }

            if (input.Groups == null || input.Groups.Count == 0)
            {
                throw new InvalidOperationException($"Session input '{arguments.SessionInputFile}' does not contain any groups.");
            }

            // Auto-generate the session ID
            var testSessionId = Guid.NewGuid().ToString();

            // Build one dynamic group request per group. Each group carries its own image, sku,
            // resources, jobs, and optional setup/cleanup.
            var dynamicGroupRequests = new List<DynamicGroupRequest>(input.Groups.Count);
            var seenGroupNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in input.Groups)
            {
                if (string.IsNullOrEmpty(group.Name))
                {
                    throw new InvalidOperationException(
                        $"Group with image '{group.Image}' and sku '{group.Sku}' in session input '{arguments.SessionInputFile}' is missing the required 'name'. The group name must be resolved by the caller (the DScript SDK does this automatically).");
                }

                if (group.Jobs == null || group.Jobs.Count == 0)
                {
                    throw new InvalidOperationException($"Group '{group.Name}' does not contain any jobs.");
                }

                var groupId = ComputeGroupId(group.Image, group.Sku);
                var groupName = group.Name;

                // Reject duplicate group names within the session. Group names are used to disambiguate jobs at
                // submission time, so two groups sharing a name would make name-based lookup ambiguous.
                if (!seenGroupNames.Add(groupName))
                {
                    throw new InvalidOperationException(
                        $"Duplicate group name '{groupName}' found in the session. Group names must be unique; provide an explicit 'name' to disambiguate groups that share the same image and sku.");
                }

                // Reject duplicate jobs (same name) within the group when the job is referenced by name (no explicit ID).
                // Such jobs would resolve to the same auto-generated ID and make name-based lookup ambiguous.
                var seenJobNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var job in group.Jobs)
                {
                    if (string.IsNullOrEmpty(job.Id) && !seenJobNames.Add(job.Name))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate job name '{job.Name}' found in group '{groupName}'. Job names must be unique within a group when no explicit job ID is provided.");
                    }
                }

                // Build job placeholders — honor explicit IDs when provided, otherwise auto-generate a stable GUID based on
                // the group name and job name (the group name is included so two jobs sharing a name in different groups
                // do not collide on the same auto-generated ID)
                var dynamicJobRequests = group.Jobs.Select(job => new DynamicJobRequest(
                    JobId: !string.IsNullOrEmpty(job.Id) ? job.Id : NewStableGuid(groupName, job.Name).ToString(),
                    JobName: job.Name)).ToList();

                dynamicGroupRequests.Add(new DynamicGroupRequest(
                    SessionId: testSessionId,
                    GroupName: groupName,
                    GroupId: groupId,
                    Sku: group.Sku,
                    Image: group.Image,
                    MaxResources: group.MaxResources,
                    MaxParallelismForJobs: group.MaxParallelismForJobs,
                    DynamicJobRequests: dynamicJobRequests,
                    DynamicGroupSetup: group.DynamicGroupSetup,
                    DynamicGroupCleanup: group.DynamicGroupCleanup,
                    LegacyModuleIdConfigPath: group.LegacyModuleIdConfigPath));
            }

            // Build the session config
            var config = new SessionConfig(
                TestSessionId: testSessionId,
                DisplayName: !string.IsNullOrEmpty(input.DisplayName) ? input.DisplayName : $"DJE Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                TenantId: input.Tenant,
                User: !string.IsNullOrEmpty(input.User) ? input.User : "unknown",
                Stamp: input.Stamp,
                BuildDropLocation: input.BuildDropLocation,
                CacheEnabled: input.CacheEnabled ?? false,
                Properties: FormatProperties(input.Properties),
                FeatureExceptions: FormatFeatureExceptions(input.FeatureExceptions),
                DynamicGroupRequests: dynamicGroupRequests,
                VSTSContext: BuildVstsContext(input.Ado),
                FileProviders: input.FileProviders);

            // Serialize and return
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            return JsonSerializer.Serialize(config, options);
        }

        /// <summary>
        /// Generates the UpdateDynamicJob configuration and writes it to the specified output file.
        /// Returns the path to the generated file.
        /// </summary>
        public static string GenerateUpdateDynamicJobConfig(CloudTestClientArgs arguments)
        {
            // Prefer the groupId resolved from the session config (when the job was referenced by name);
            // otherwise compute it from the image and sku.
            var groupId = !string.IsNullOrEmpty(arguments.GroupId)
                ? arguments.GroupId
                : ComputeGroupId(arguments.Image, arguments.Sku);

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
                TestDependencyHash: BuildAggregateTestDependencyHash(arguments),
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
        /// Builds the VstsContext JSON string when ADO context is provided in the session input.
        /// Returns null if no ADO context is configured.
        /// </summary>
        private static string BuildVstsContext(AdoContextConfig ado)
        {
            if (ado == null || string.IsNullOrEmpty(ado.ProjectId))
            {
                return null;
            }

            string authToken = Environment.GetEnvironmentVariable(ado.AccessTokenEnvVar);
            if (string.IsNullOrEmpty(authToken))
            {
                throw new InvalidOperationException(
                    $"ADO context was requested but environment variable '{ado.AccessTokenEnvVar}' is not set or empty.");
            }

            var vstsContext = new VstsContextPayload(
                ProjectId: ado.ProjectId,
                AuthToken: authToken,
                VSTSUrl: ado.CollectionUri,
                BuildProperties: new VstsBuildProperties(BuildId: ado.BuildId),
                CloudTestVSTSRequest: new VstsUploadRequest(UploadResultsToVSTS: "true"));

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            return JsonSerializer.Serialize(vstsContext, options);
        }

        /// <summary>
        /// Renders the session properties (key/value pairs from the input) as the semicolon-separated
        /// "key=value" string CloudTest expects. Returns null when there are no properties.
        /// </summary>
        private static string FormatProperties(List<PropertyEntry> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return null;
            }

            return string.Join(";", properties.Select(p => $"{p.Key}={p.Value}"));
        }

        /// <summary>
        /// Renders the feature exceptions as the comma-separated string CloudTest expects. Returns null when empty.
        /// </summary>
        private static string FormatFeatureExceptions(List<string> featureExceptions)
        {
            if (featureExceptions == null || featureExceptions.Count == 0)
            {
                return null;
            }

            return string.Join(",", featureExceptions);
        }

        /// <summary>
        /// Generates a deterministic GUID from a string value using SHA-256.
        /// The same input always produces the same GUID.
        /// </summary>
        private static Guid NewStableGuid(string groupName, string jobName)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{groupName}\0{jobName}"));
            return new Guid(hash.AsSpan(0, 16));
        }

        #region JSON model

        private sealed record SessionConfig(
            string TestSessionId,
            string DisplayName,
            string TenantId,
            string User,
            string Stamp,
            string BuildDropLocation,
            bool CacheEnabled,
            string Properties,
            string FeatureExceptions,
            List<DynamicGroupRequest> DynamicGroupRequests,
            [property: JsonPropertyName("VSTSContext")] string VSTSContext,
            List<FileProviderConfig> FileProviders);

        private sealed record DynamicGroupRequest(
            string SessionId,
            string GroupName,
            string GroupId,
            string Sku,
            string Image,
            int MaxResources,
            int? MaxParallelismForJobs,
            List<DynamicJobRequest> DynamicJobRequests,
            GroupSetupConfig DynamicGroupSetup,
            GroupCleanupConfig DynamicGroupCleanup,
            string LegacyModuleIdConfigPath);

        private sealed record DynamicJobRequest(string JobId, string JobName);

        // The entire session definition, deserialized from the single session-input JSON file.
        private sealed record SessionInputConfig(
            string Tenant,
            string BuildDropLocation,
            string DisplayName,
            string User,
            bool? CacheEnabled,
            string Stamp,
            List<PropertyEntry> Properties,
            List<string> FeatureExceptions,
            AdoContextConfig Ado,
            List<GroupFileConfig> Groups,
            List<FileProviderConfig> FileProviders);

        // A single session property. DScript serializes a Map<string,string> as an array of {key,value} objects.
        private sealed record PropertyEntry(string Key, string Value);

        // Azure DevOps context used to build the session's VstsContext. AccessTokenEnvVar names the environment
        // variable (e.g. SYSTEM_ACCESSTOKEN) from which the tool reads the OAuth token at runtime.
        private sealed record AdoContextConfig(
            string ProjectId,
            string CollectionUri,
            string BuildId,
            string AccessTokenEnvVar);

        // Per-group definition deserialized from the session input's "groups" array. Mirrors the DScript Group
        // interface: image/sku/resources, the group's jobs, and optional setup/cleanup.
        private sealed record GroupFileConfig(
            string Name,
            string Sku,
            string Image,
            int MaxResources,
            int? MaxParallelismForJobs,
            List<JobConfig> Jobs,
            GroupSetupConfig DynamicGroupSetup,
            GroupCleanupConfig DynamicGroupCleanup,
            [property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string LegacyModuleIdConfigPath);

        private sealed record JobConfig(string Name, string Id);

        // Group setup/cleanup model — matches CloudTest's GroupSetup/GroupCleanup schema.
        // Path fields use CloudTestPathConverter to handle both plain strings (Path/RelativePath
        // from DScript) and {"prefix":"X","path":"Y"} objects (PrefixedPath from DScript).
        // Resolution: absolute paths pass through, relative paths get [WorkingDirectory]/ prefix,
        // PrefixedPath objects become [prefix]\path.

        private sealed record CopyEntryConfig(
            [property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Source,
            [property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Destination,
            bool? IsRecursive,
            bool? IsZeroCopiedFilesAllowed,
            bool? Writable,
            bool? SkipHashInput);

        private sealed record ScriptEntryConfig(
            [property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Path,
            [property: JsonConverter(typeof(JsonHelpers.ScriptArgsConverter))] string Args,
            string ScriptName,
            int? TimeoutMins);

        private sealed record ServiceEntryConfig(
            [property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Path,
            bool? SkipHashInput);

        private sealed record GroupSetupConfig(
            List<CopyEntryConfig> BuildFiles,
            List<CopyEntryConfig> DataFiles,
            List<ServiceEntryConfig> Services,
            List<ScriptEntryConfig> Scripts,
            int? TimeoutMins);

        private sealed record GroupCleanupConfig(
            List<ScriptEntryConfig> Scripts,
            int? TimeoutMins);

        // File provider model — matches CloudTest's ProviderDefinition schema

        private sealed record FileProviderPropertyConfig(
            string Name,
            string Value);

        private sealed record FileProviderConfig(
            string Type,
            List<FileProviderPropertyConfig> Properties);

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
        /// Builds the aggregated test dependency hash for a job from all of its caching-fingerprint inputs:
        /// the content VsoHashes of the job inputs and session-creation drop artifacts, the drop-relative paths of
        /// those artifacts, the resolved group's dynamic setup/cleanup, and the session's file providers.
        ///
        /// Each content hash is paired with the drop-relative path of the same artifact by position -- the SDK emits
        /// <c>/testDependencyHash</c> and <c>/testDependencyPath</c> in matching order -- and the pairs are aggregated
        /// in order (not sorted). Preserving the pairing and the order is what makes the fingerprint sensitive to a
        /// path swap: relocating identical content from one drop path to another (or swapping the drop paths of two
        /// artifacts with identical content) changes the aggregate even though the multiset of hashes is unchanged.
        /// </summary>
        private static string BuildAggregateTestDependencyHash(CloudTestClientArgs arguments)
        {
            var hashes = arguments.TestDependencyHashes;
            var paths = arguments.TestDependencyPaths;

            // The SDK emits exactly one drop-relative path per content hash, in matching order, so the two lists are
            // always parallel (both empty when the job has no inputs). A size mismatch indicates a real wiring bug.
            if (paths.Count != hashes.Count)
            {
                throw new InvalidOperationException(
                    $"Expected an equal number of test dependency hashes ({hashes.Count}) and paths ({paths.Count}).");
            }

            var builder = new StringBuilder();

            // Note: AppendLine() uses Environment.NewLine (\r\n on Windows, \n on Linux), so this fingerprint is
            // platform-dependent. That is acceptable here: test jobs are themselves platform-specific and CloudTest
            // does not share/mix cached results across platforms, so a Windows-vs-Linux hash difference never collides.
            for (int i = 0; i < hashes.Count; i++)
            {
                builder.Append(paths[i]).Append('=').AppendLine(hashes[i]);
            }

            // The resolved group's setup/cleanup config (group-level inputs deployed to the worker VMs) and the
            // session's file providers (a session-level input shared by every job) have no drop path; fold their
            // content hashes in at fixed trailing positions.
            if (!string.IsNullOrEmpty(arguments.GroupSetupCleanupJson))
            {
                builder.Append("groupSetupCleanup=").AppendLine(Sha256Hex(arguments.GroupSetupCleanupJson));
            }

            if (!string.IsNullOrEmpty(arguments.FileProvidersJson))
            {
                builder.Append("fileProviders=").AppendLine(Sha256Hex(arguments.FileProvidersJson));
            }

            if (builder.Length == 0)
            {
                return null;
            }

            return Sha256Hex(builder.ToString());
        }

        /// <summary>
        /// Computes the SHA-256 hash of a string as a lowercase hex string. Used to fold inline (non-artifact)
        /// content -- the resolved group's setup/cleanup and the session's file providers -- and the final
        /// aggregate into the job's caching fingerprint. The artifact hashes that flow in via
        /// <c>/testDependencyHash</c> are embedded verbatim, so the resulting fingerprint is an opaque,
        /// deterministic token.
        /// </summary>
        private static string Sha256Hex(string content)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        #endregion


    }
}
