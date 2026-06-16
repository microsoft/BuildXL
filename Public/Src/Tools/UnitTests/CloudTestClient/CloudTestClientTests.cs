// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class CloudTestClientArgsTests
    {
        #region Argument parsing

        [Fact]
        public void ParseGenerateSessionConfigArgs()
        {
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
                "/jobName:TestSuite_B",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "session-config.json"),
            });

            Assert.Equal(CloudTestMode.GenerateSessionConfig, args.Mode);
            Assert.Equal("my-tenant", args.Tenant);
            Assert.Equal("https://drop.example.com/build/123", args.BuildDropLocation);
            Assert.Equal("Standard_D4s_v3", args.Sku);
            Assert.Equal("ubuntu22.04", args.Image);
            Assert.Equal(3, args.MaxResources);
            Assert.Equal(2, args.Jobs.Count);
            Assert.Equal("TestSuite_A", args.Jobs[0].Name);
            Assert.Equal("TestSuite_B", args.Jobs[1].Name);
        }

        [Fact]
        public void ParseGenerateSessionConfigArgsWithOptionalFields()
        {
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/maxParallelismForJobs:5",
                "/stamp:wus2-default",
                "/properties:key1=value1;key2=value2",
                "/featureExceptions:EnableTCDForDynamicJobs,AnotherFlag",
                "/jobName:TestSuite_A",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "session-config.json"),
            });

            Assert.Equal(5, args.MaxParallelismForJobs);
            Assert.Equal("wus2-default", args.Stamp);
            Assert.Equal("key1=value1;key2=value2", args.Properties);
            Assert.Equal("EnableTCDForDynamicJobs,AnotherFlag", args.FeatureExceptions);
        }

        [Fact]
        public void ParseGenerateUpdateDynamicJobConfigArgs()
        {
            using var temp = new TempDirectory();
            string tempSessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + tempSessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + temp.GetPath("update-config.json"),
            });

            Assert.Equal(CloudTestMode.GenerateUpdateDynamicJobConfig, args.Mode);
            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", args.SessionId);
            Assert.Equal("11111111-2222-3333-4444-555555555555", args.JobId);
            Assert.Equal("TestSuite_A", args.TestFolder);
            Assert.Equal("Exe", args.TestExecutionType);
        }

        [Fact]
        public void MissingModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/tenant:foo" }));
        }

        [Fact]
        public void InvalidModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/mode:nonexistent", "/tenant:foo" }));
        }

        [Fact]
        public void MissingMandatoryArgThrows()
        {
            // generateSessionConfig without configOutputFile
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
            }));
        }

        #endregion

        #region Path validation

        [Fact]
        public void ValidPathsDoNotThrow()
        {
            // Ensure well-formed paths pass validation without throwing
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "valid-path", "output.json"),
            });

            Assert.Equal(Path.Combine(Path.GetTempPath(), "valid-path", "output.json"), args.ConfigOutputFile);
        }

        #endregion
    }

    public class JsonHelpersTests
    {
        #region ReadJsonFile

        [Fact]
        public void ReadJsonFileReturnsNullForNullPath()
        {
            Assert.Null(JsonHelpers.ReadJsonFile<object>(null));
        }

        [Fact]
        public void ReadJsonFileReturnsNullForEmptyPath()
        {
            Assert.Null(JsonHelpers.ReadJsonFile<object>(string.Empty));
        }

        [Fact]
        public void ReadJsonFileThrowsForMissingFile()
        {
            Assert.Throws<InvalidOperationException>(() =>
                JsonHelpers.ReadJsonFile<object>(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".json")));
        }

        [Fact]
        public void ReadJsonFileDeserializesCorrectly()
        {
            using var temp = new TempDirectory();
            string path = temp.WriteFile("data.json", """{"name":"test","value":42}""");
            var result = JsonHelpers.ReadJsonFile<SimpleRecord>(path);
            Assert.Equal("test", result.Name);
            Assert.Equal(42, result.Value);
        }

        private record SimpleRecord(string Name, int Value);

        #endregion

        #region ReadJsonDocument

        [Fact]
        public void ReadJsonDocumentReturnsNullForNullPath()
        {
            Assert.Null(JsonHelpers.ReadJsonDocument(null));
        }

        [Fact]
        public void ReadJsonDocumentParsesFile()
        {
            using var temp = new TempDirectory();
            string path = temp.WriteFile("doc.json", """{"key":"val"}""");
            using var doc = JsonHelpers.ReadJsonDocument(path);
            Assert.Equal("val", doc.RootElement.GetProperty("key").GetString());
        }

        [Fact]
        public void ReadJsonDocumentThrowsForMissingFile()
        {
            Assert.Throws<InvalidOperationException>(() =>
                JsonHelpers.ReadJsonDocument(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".json")));
        }

        #endregion

        #region CloudTestPathConverter

        [Theory]
        [InlineData("""{"path":"scripts/setup.ps1"}""", @"[WorkingDirectory]\scripts/setup.ps1", "relative path gets prefixed")]
        [InlineData("""{"path":{"prefix":"BuildRoot","path":"bin/test.dll"}}""", @"[BuildRoot]\bin/test.dll", "prefixed path resolved")]
        [InlineData("""{"path":{"prefix":"WorkingDirectory"}}""", "[WorkingDirectory]", "prefix-only path")]
        [InlineData("""{"path":{"prefix":"VSODrop","path":"TestFiles/group.xml"}}""", @"[VSODrop]\TestFiles/group.xml", "VSODrop prefixed path")]
        [InlineData("""{"path":{"prefix":"LoggingDirectory","path":"results"}}""", @"[LoggingDirectory]\results", "LoggingDirectory prefixed path")]
        public void CloudTestPathConverterDeserializes(string json, string expected, string scenario)
        {
            _ = scenario;
            var result = DeserializePath(json);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CloudTestPathConverterAbsolutePathPassesThrough()
        {
            // Use a path that is absolute on the current OS
            string absolutePath = Path.Combine(Path.GetTempPath(), "drop", "test.exe");
            string json = $"{{\"path\":\"{JsonEncodedText.Encode(absolutePath)}\"}}";
            var result = DeserializePath(json);
            Assert.Equal(absolutePath, result);
        }

        [Fact]
        public void CloudTestPathConverterPrefixedPathMissingPrefixThrows()
        {
            Assert.Throws<JsonException>(() => DeserializePath("""{"path":{"notprefix":"X"}}"""));
        }

        private static string DeserializePath(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var record = JsonSerializer.Deserialize<PathHolder>(json, options);
            return record.Path;
        }

        private record PathHolder([property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Path);

        #endregion

        #region ScriptArgsConverter

        [Fact]
        public void ScriptArgsConverterStringPassesThrough()
        {
            var result = DeserializeArgs("""{"args":"--flag value"}""");
            Assert.Equal("--flag value", result);
        }

        [Fact]
        public void ScriptArgsConverterNumberConverted()
        {
            var result = DeserializeArgs("""{"args":42}""");
            Assert.Equal("42", result);
        }

        [Fact]
        public void ScriptArgsConverterNullReturnsNull()
        {
            var result = DeserializeArgs("""{"args":null}""");
            Assert.Null(result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundWithSeparator()
        {
            var result = DeserializeArgs("""{"args":{"values":["a.dll","b.dll","c.dll"],"separator":","}}""");
            Assert.Equal("a.dll,b.dll,c.dll", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundDefaultSeparator()
        {
            var result = DeserializeArgs("""{"args":{"values":["--config","test.json","--timeout","30"]}}""");
            Assert.Equal("--config test.json --timeout 30", result);
        }

        [Fact]
        public void ScriptArgsConverterNestedCompound()
        {
            // Outer: space-separated, inner: comma-separated
            var result = DeserializeArgs("""{"args":{"values":["-Refs",{"values":["a.dll","b.dll"],"separator":","}],"separator":" "}}""");
            Assert.Equal("-Refs a.dll,b.dll", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundWithNumbers()
        {
            var result = DeserializeArgs("""{"args":{"values":["--timeout",60],"separator":" "}}""");
            Assert.Equal("--timeout 60", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundMissingValuesThrows()
        {
            Assert.Throws<JsonException>(() => DeserializeArgs("""{"args":{"separator":","}}"""));
        }

        private static string DeserializeArgs(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var record = JsonSerializer.Deserialize<ArgsHolder>(json, options);
            return record.Args;
        }

        private record ArgsHolder([property: JsonConverter(typeof(JsonHelpers.ScriptArgsConverter))] string Args);

        #endregion
    }

    public class SessionConfigGeneratorTests
    {
        #region NewStableGuid (via GenerateSessionConfig)

        [Fact]
        public void GenerateSessionConfigProducesDeterministicJobIds()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            var args1 = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestSuite_Stable",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args1);
            string json1 = File.ReadAllText(configPath);

            File.Delete(configPath);

            var args2 = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestSuite_Stable",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args2);
            string json2 = File.ReadAllText(configPath);

            // Extract job IDs from both runs
            string jobId1 = ExtractJobId(json1);
            string jobId2 = ExtractJobId(json2);

            Assert.Equal(jobId1, jobId2);
            // Ensure it's a valid GUID
            Assert.True(Guid.TryParse(jobId1, out _));
        }

        [Fact]
        public void DifferentJobNamesProduceDifferentIds()
        {
            using var temp = new TempDirectory();
            string configPath1 = temp.GetPath("config1.json");
            string configPath2 = temp.GetPath("config2.json");

            var args1 = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:JobAlpha",
                "/configOutputFile:" + configPath1,
            });

            var args2 = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:JobBeta",
                "/configOutputFile:" + configPath2,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args1);
            ConfigGeneratorHelper.GenerateSessionConfig(args2);

            string jobId1 = ExtractJobId(File.ReadAllText(configPath1));
            string jobId2 = ExtractJobId(File.ReadAllText(configPath2));

            Assert.NotEqual(jobId1, jobId2);
        }

        [Fact]
        public void ExplicitJobIdIsPreserved()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");
            string explicitId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                $"/jobIdAndName:{explicitId}#MyJob",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);
            string jobId = ExtractJobId(File.ReadAllText(configPath));

            Assert.Equal(explicitId, jobId);
        }

        #endregion

        #region NewOptionalSessionConfigFields

        [Fact]
        public void SessionConfigIncludesOptionalFields()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/maxParallelismForJobs:5",
                "/stamp:wus2-default",
                "/properties:key1=value1;key2=value2",
                "/featureExceptions:EnableTCDForDynamicJobs",
                "/jobName:TestSuite_A",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);
            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("wus2-default", root.GetProperty("stamp").GetString());
            Assert.Equal("key1=value1;key2=value2", root.GetProperty("properties").GetString());
            Assert.Equal("EnableTCDForDynamicJobs", root.GetProperty("featureExceptions").GetString());

            var group = root.GetProperty("dynamicGroupRequests")[0];
            Assert.Equal(5, group.GetProperty("maxParallelismForJobs").GetInt32());
        }

        [Fact]
        public void SessionConfigOmitsNullOptionalFields()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestSuite_A",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);
            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // When not specified, these fields should be omitted from the JSON (DefaultIgnoreCondition.WhenWritingNull)
            Assert.False(root.TryGetProperty("stamp", out _));
            Assert.False(root.TryGetProperty("properties", out _));
            Assert.False(root.TryGetProperty("featureExceptions", out _));

            var group = root.GetProperty("dynamicGroupRequests")[0];
            Assert.False(group.TryGetProperty("maxParallelismForJobs", out _));
        }

        #endregion

        #region GenerateUpdateDynamicJobConfig

        [Fact]
        public void GenerateUpdateDynamicJobConfigProducesValidJson()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath = temp.GetPath("update-config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/testParserType:TAP",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", root.GetProperty("sessionId").GetString());
            Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("jobId").GetString());
            Assert.Equal("TestSuite_A", root.GetProperty("testFolder").GetString());
            Assert.Equal(temp.GetPath("run.sh"), root.GetProperty("jobExecutable").GetString());
            Assert.Equal("Exe", root.GetProperty("testExecutionType").GetString());
            Assert.Equal("TAP", root.GetProperty("testParserType").GetString());
        }


        [Fact]
        public void RelativeJobExecutableGetsPrefixedWithWorkingDirectory()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath = temp.GetPath("workdir-config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:relative/run.sh",
                "/testExecutionType:Exe",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(Path.Join("[WorkingDirectory]", "relative/run.sh"), root.GetProperty("jobExecutable").GetString());
        }

        #endregion

        #region AggregateHashes

        [Fact]
        public void MultipleHashesAreAggregatedDeterministically()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath1 = temp.GetPath("hash1.json");
            string configPath2 = temp.GetPath("hash2.json");

            // Same hashes in different order should produce the same aggregate
            var args1 = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/testDependencyHash:abc123",
                "/testDependencyHash:def456",
                "/configOutputFile:" + configPath1,
            });

            var args2 = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/testDependencyHash:def456",
                "/testDependencyHash:abc123",
                "/configOutputFile:" + configPath2,
            });

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args1);
            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args2);

            string json1 = File.ReadAllText(configPath1);
            string json2 = File.ReadAllText(configPath2);

            using var doc1 = JsonDocument.Parse(json1);
            using var doc2 = JsonDocument.Parse(json2);

            string hash1 = doc1.RootElement.GetProperty("testDependencyHash").GetString();
            string hash2 = doc2.RootElement.GetProperty("testDependencyHash").GetString();

            Assert.Equal(hash1, hash2);
            Assert.NotNull(hash1);
        }

        #endregion

        #region Group setup/cleanup and file providers

        [Fact]
        public void GroupSetupIsIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            // Write a group setup JSON with a copy entry using a PrefixedPath and a script with compound args
            string setupFile = temp.WriteFile("setup.json", """
            {
                "buildFiles": [
                    {
                        "source": {"prefix": "BuildRoot", "path": "bin/test.dll"},
                        "destination": "scripts/setup.ps1",
                        "isRecursive": true
                    }
                ],
                "scripts": [
                    {
                        "path": {"prefix": "WorkingDirectory", "path": "tools/setup.cmd"},
                        "args": {"values": ["--config", "test.json"], "separator": " "},
                        "timeoutMins": 10
                    }
                ],
                "timeoutMins": 30
            }
            """);

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestJob",
                "/configOutputFile:" + configPath,
                "/dynamicGroupSetupFile:" + setupFile,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var group = doc.RootElement.GetProperty("dynamicGroupRequests")[0];
            var setup = group.GetProperty("dynamicGroupSetup");

            // Verify setup timeout
            Assert.Equal(30, setup.GetProperty("timeoutMins").GetInt32());

            // Verify copy entry with CloudTestPath resolution
            var buildFile = setup.GetProperty("buildFiles")[0];
            Assert.Equal(@"[BuildRoot]\bin/test.dll", buildFile.GetProperty("source").GetString());
            Assert.Equal(@"[WorkingDirectory]\scripts/setup.ps1", buildFile.GetProperty("destination").GetString());
            Assert.True(buildFile.GetProperty("isRecursive").GetBoolean());

            // Verify script with compound args resolved to string
            var script = setup.GetProperty("scripts")[0];
            Assert.Equal(@"[WorkingDirectory]\tools/setup.cmd", script.GetProperty("path").GetString());
            Assert.Equal("--config test.json", script.GetProperty("args").GetString());
            Assert.Equal(10, script.GetProperty("timeoutMins").GetInt32());
        }

        [Fact]
        public void GroupCleanupIsIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            string cleanupFile = temp.WriteFile("cleanup.json", """
            {
                "scripts": [
                    {
                        "path": {"prefix": "WorkingDirectory", "path": "cleanup.ps1"},
                        "args": "--force"
                    }
                ],
                "timeoutMins": 5
            }
            """);

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestJob",
                "/configOutputFile:" + configPath,
                "/dynamicGroupCleanupFile:" + cleanupFile,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var group = doc.RootElement.GetProperty("dynamicGroupRequests")[0];
            var cleanup = group.GetProperty("dynamicGroupCleanup");

            Assert.Equal(5, cleanup.GetProperty("timeoutMins").GetInt32());

            var script = cleanup.GetProperty("scripts")[0];
            Assert.Equal(@"[WorkingDirectory]\cleanup.ps1", script.GetProperty("path").GetString());
            Assert.Equal("--force", script.GetProperty("args").GetString());
        }

        [Fact]
        public void FileProvidersAreIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string configPath = temp.GetPath("config.json");

            string providersFile = temp.WriteFile("providers.json", """
            [
                {
                    "type": "VsoDrop",
                    "properties": [
                        {"name": "DropUrl", "value": "https://drop.example.com/build/123"},
                        {"name": "BuildRoot", "value": ""}
                    ]
                }
            ]
            """);

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:1",
                "/jobName:TestJob",
                "/configOutputFile:" + configPath,
                "/fileProvidersFile:" + providersFile,
            });

            ConfigGeneratorHelper.GenerateSessionConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var providers = doc.RootElement.GetProperty("fileProviders");

            Assert.Equal(1, providers.GetArrayLength());
            var provider = providers[0];
            Assert.Equal("VsoDrop", provider.GetProperty("type").GetString());

            var props = provider.GetProperty("properties");
            Assert.Equal(2, props.GetArrayLength());
            Assert.Equal("DropUrl", props[0].GetProperty("name").GetString());
            Assert.Equal("https://drop.example.com/build/123", props[0].GetProperty("value").GetString());
        }

        #endregion

        private static string ExtractJobId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var groups = doc.RootElement.GetProperty("dynamicGroupRequests");
            var jobs = groups[0].GetProperty("dynamicJobRequests");
            return jobs[0].GetProperty("jobId").GetString();
        }
    }
}