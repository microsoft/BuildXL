// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    /// <summary>
    /// Helpers for writing the consolidated session-input JSON file consumed by generateSessionConfig.
    /// </summary>
    internal static class GroupFileTestHelper
    {
        /// <summary>
        /// Builds a single group's JSON object (not written to disk). Combine one or more of these with
        /// <see cref="WriteGroupsFile"/> or <see cref="WriteSessionInputFile"/> to produce the session-input file.
        /// </summary>
        public static string BuildGroupJson(
            string image = "ubuntu22.04",
            string sku = "Standard_D4s_v3",
            int maxResources = 1,
            int? maxParallelismForJobs = null,
            string jobsJson = """[{"name":"TestSuite_A"}]""",
            string setupJson = null,
            string cleanupJson = null,
            string name = null)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                if (name != null)
                {
                    writer.WriteString("name", name);
                }
                writer.WriteString("image", image);
                writer.WriteString("sku", sku);
                writer.WriteNumber("maxResources", maxResources);
                if (maxParallelismForJobs.HasValue)
                {
                    writer.WriteNumber("maxParallelismForJobs", maxParallelismForJobs.Value);
                }
                if (setupJson != null)
                {
                    writer.WritePropertyName("dynamicGroupSetup");
                    writer.WriteRawValue(setupJson);
                }
                if (cleanupJson != null)
                {
                    writer.WritePropertyName("dynamicGroupCleanup");
                    writer.WriteRawValue(cleanupJson);
                }
                writer.WritePropertyName("jobs");
                writer.WriteRawValue(jobsJson);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Writes the consolidated session-input JSON file the tool consumes, embedding the given groups plus the
        /// session-level fields. The optional *Json parameters take raw JSON (e.g. propertiesJson is a
        /// [{"key":..,"value":..}] array; featureExceptionsJson is a string array; fileProvidersJson is an array).
        /// </summary>
        public static string WriteSessionInputFile(
            TempDirectory temp,
            string fileName,
            string[] groupJsonObjects,
            string tenant = "my-tenant",
            string buildDropLocation = "https://drop.example.com/build/123",
            string stamp = null,
            string propertiesJson = null,
            string featureExceptionsJson = null,
            string fileProvidersJson = null)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("tenant", tenant);
                writer.WriteString("buildDropLocation", buildDropLocation);
                if (stamp != null)
                {
                    writer.WriteString("stamp", stamp);
                }
                if (propertiesJson != null)
                {
                    writer.WritePropertyName("properties");
                    writer.WriteRawValue(propertiesJson);
                }
                if (featureExceptionsJson != null)
                {
                    writer.WritePropertyName("featureExceptions");
                    writer.WriteRawValue(featureExceptionsJson);
                }
                if (fileProvidersJson != null)
                {
                    writer.WritePropertyName("fileProviders");
                    writer.WriteRawValue(fileProvidersJson);
                }
                writer.WritePropertyName("groups");
                writer.WriteStartArray();
                foreach (var groupJson in groupJsonObjects)
                {
                    writer.WriteRawValue(groupJson);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return temp.WriteFile(fileName, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        /// <summary>
        /// Writes a session-input file containing the given groups (with default session-level fields).
        /// </summary>
        public static string WriteGroupsFile(TempDirectory temp, string fileName, params string[] groupJsonObjects)
        {
            return WriteSessionInputFile(temp, fileName, groupJsonObjects);
        }

        /// <summary>
        /// Convenience helper that builds a single group and writes it into a session-input file with default
        /// session-level fields.
        /// </summary>
        public static string WriteGroupFile(
            TempDirectory temp,
            string fileName = "session-input.json",
            string image = "ubuntu22.04",
            string sku = "Standard_D4s_v3",
            int maxResources = 1,
            int? maxParallelismForJobs = null,
            string jobsJson = """[{"name":"TestSuite_A"}]""",
            string setupJson = null,
            string cleanupJson = null,
            string name = null)
        {
            string group = BuildGroupJson(image, sku, maxResources, maxParallelismForJobs, jobsJson, setupJson, cleanupJson, name);
            return WriteSessionInputFile(temp, fileName, new[] { group });
        }

        internal static string ExtractJobId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var groups = doc.RootElement.GetProperty("dynamicGroupRequests");
            var jobs = groups[0].GetProperty("dynamicJobRequests");
            return jobs[0].GetProperty("jobId").GetString();
        }

        /// <summary>
        /// Generates a session config from the given session-input file for use in tests.
        /// </summary>
        internal static void GenerateSessionConfigForTest(TempDirectory temp, string sessionConfigPath, string sessionInputFile)
        {
            var argList = new List<string>
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + sessionInputFile,
                "/configOutputFile:" + sessionConfigPath,
            };

            ConfigGeneratorHelper.GenerateSessionConfig(new CloudTestClientArgs(argList.ToArray()));
        }

        /// <summary>
        /// Generates a session config from the given session-input file and returns the JSON string directly,
        /// without writing it to disk. Use for tests that only need to inspect the generated config (avoids a
        /// write-then-read-back round trip).
        /// </summary>
        internal static string GenerateSessionConfigJson(TempDirectory temp, string sessionInputFile)
        {
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + sessionInputFile,
                // configOutputFile is mandatory at argument-validation time but is never written by the string overload.
                "/configOutputFile:" + temp.GetPath("unused-session-config.json"),
            });

            return ConfigGeneratorHelper.GenerateSessionConfigJson(args);
        }

        /// <summary>
        /// Runs generateUpdateDynamicJobConfig resolving the job by name from the given session config and returns the
        /// aggregated testDependencyHash (null when absent). Extra arguments (e.g. /testDependencyHash, /testDependencyPath)
        /// are appended verbatim.
        /// </summary>
        internal static string GenerateUpdateConfigAndGetHash(TempDirectory temp, string sessionConfigPath, string jobName, string outputFileName, params string[] extraArgs)
        {
            string sessionIdFile = temp.WriteFile(outputFileName + ".session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath(outputFileName);

            var argList = new List<string>
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:" + jobName,
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:" + jobName,
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            };
            argList.AddRange(extraArgs);

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(new CloudTestClientArgs(argList.ToArray()));

            using var doc = JsonDocument.Parse(File.ReadAllText(updateConfigPath));
            return doc.RootElement.TryGetProperty("testDependencyHash", out var hash) ? hash.GetString() : null;
        }
    }
}
